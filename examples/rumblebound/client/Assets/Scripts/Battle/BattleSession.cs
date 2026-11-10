using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Realtime.Core;
using Realtime.Transport;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // One update owner. HTTP identity/room orchestration and scene loading are
    // separate; this object never obtains credentials or creates Unity objects.
    public sealed class BattleSession:IDisposable
    {
        public event Action<long,string,double,double,bool,double,double> PreviewAction;
        public event Action<long,bool> ResolveAction;
        public event Action<Snapshot> ValidatedSnapshot;
        public event Action<IReplicaStore<PlayerState>> ReplicasApplied;
        private sealed class ActionIntent{public long ID;public string Kind,TargetID;public long TargetLife;public double X,Z,TargetX,TargetZ;public bool Ground;}
        private readonly IGameJson json;
        private readonly BattleState received=new BattleState();
        private readonly Queue<ActionIntent> actions=new Queue<ActionIntent>();
        private readonly ProbeScheduler probes=new ProbeScheduler();
        private readonly ReplyTracker inputReplies=new ReplyTracker();
        private GameContent content;
        private BattleSettings settings;
        private IFrameDecoder decoder;
        private IReplicaStore<PlayerState> replicas;
        private IMessageConditioner conditioner,outboundConditioner;
        private IServerClock clock;
        private IActionPresentation actionFeedback;
        private string matchID;
        private long sequence,actionSequence,highestFrame,validatedAck;
        private double nextInput,deadline,lastWire,nextWireInput,lastSnapshot=double.NaN;
        private double lastX,lastZ;
        private double facingX,facingZ;
        private bool loadedRequested,loadedSent;
        public MessageTransport Transport{get;}=new MessageTransport();
        public BattleState State{get;}=new BattleState();
        public GameMovement Movement{get;private set;}
        public IReplicaStore<PlayerState> Replicas=>replicas;
        public IClientDiagnostics Metrics{get;private set;}=new NoClientDiagnostics();
        public string Status{get;private set;}="Disconnected";
        public string Failure{get;private set;}="";
        public string ValidationFailure{get;private set;}="";
        public string LastActionCode{get;private set;}="";
        public bool Connecting{get;private set;}
        public bool HasResult=>State.Joined&&State.Latest.match?.phase=="ended";
        public bool Connected=>State.Joined&&!HasResult;
        public bool LoadedSent=>loadedSent;
        public bool CanControl=>State.Playing&&loadedSent;
        public long InputSequence=>sequence;
        public long FrameAck=>validatedAck;
        public bool HasPendingAction=>actions.Count>0;
        public int MissingBaselines{get;private set;}
        public int ActionStarts{get;private set;}
        public long ConfirmedActions=>actionFeedback?.Confirmed??0;
        public long RejectedActions=>actionFeedback?.Rejected??0;
        public double RTTSeconds{get;private set;}
        public ConditioningSummary SendConditioning=>outboundConditioner?.Summary??default;
        public ConditioningSummary ReceiveConditioning=>conditioner?.Summary??default;
        public BattleSession(IGameJson json){this.json=json??throw new ArgumentNullException(nameof(json));}

        public void Connect(GameContent definition,BattleSettings selection,string endpoint,string ticket,string roomMatchID,string callback,double now,bool webTransport=false,string certificateHash="")
        {
            Disconnect();definition=definition??throw new ArgumentNullException(nameof(definition));selection.Validate();
            if(string.IsNullOrEmpty(ticket)||string.IsNullOrEmpty(roomMatchID))throw new ArgumentException("A room ticket is required");
            content=definition;settings=selection.Copy();matchID=roomMatchID;
            decoder=BattleNetcode.DecoderFactory?.Invoke();replicas=BattleNetcode.ReplicaFactory?.Invoke();actionFeedback=BattleNetcode.ActionsFactory?.Invoke();
            clock=settings.Clock=="disciplined"?BattleNetcode.ClockFactory(content.Rules.tickRate):new SnapshotClock();
            conditioner=CreateConditioner(settings.DelaySeconds,settings.ReceiveJitterSeconds,settings.ReceiveLossProbability,17);
            outboundConditioner=CreateConditioner(settings.SendDelaySeconds,settings.SendJitterSeconds,settings.SendLossProbability,29);
            Metrics=BattleNetcode.CreateDiagnostics();Connecting=true;Status="Connecting";Failure="";deadline=now+10;lastWire=now;nextInput=now;
            try{
                Transport.Open(Transport.BeginAttempt(),endpoint,json.Write(new HelloMessage{protocol=content.Rules.protocol,content=content.Rules.content,matchID=matchID,ticket=ticket,net=settings.Server}),certificateHash,callback,webTransport);
            }catch{Disconnect("connection_failed");throw;}
        }
        public void MarkLoaded(){if(Connected&&State.Latest.match?.phase!="ended")loadedRequested=true;}
        private void Apply(Snapshot state,double now)
        {
            bool initial=!State.Joined;
            if(!State.Apply(state,content,matchID))return;
            if(initial){
                clock.Reset();clock.Observe(state.tick,now,0);
                var self=state.players.First(p=>p.playerID==State.PlayerID);
                sequence=Math.Max(sequence,self.ack);actionSequence=Math.Max(actionSequence,state.actionAck);
                var rules=content.ForHero(self.heroID);
                var timeline=BattleNetcode.Create(settings.Timeline,rules);
                if(timeline is IClockAwareTimeline aware)aware.SourceClock=clock;
                Movement=new GameMovement(rules,content.Map,timeline,model=>BattleNetcode.CreatePrediction(settings.Prediction,model,rules),BattleNetcode.CreateCorrection(settings.Correction),BattleNetcode.Predictions[settings.Prediction].FixedTicks);
            }else if(settings.Clock=="snapshot")clock.Observe(state.tick,now,0);
            // The generic store rejects duplicate ticks. A reliable result can
            // share the final tick and must keep its complete validated roster.
            if(replicas!=null&&state.match?.phase!="ended"&&replicas.Apply(state.generation,state.tick,state.players))ReplicasApplied?.Invoke(replicas);
            Movement.Accept(state,State.PlayerID,now);
            if(!initial){
                var self=state.players.First(p=>p.playerID==State.PlayerID);
                if(inputReplies.TryComplete(self.ack,now,out double reply))Metrics.Timing(ClientMetric.InputReplyMS,reply*1000,now);
                inputReplies.DiscardThrough(self.ack);
                Metrics.Timing(ClientMetric.CorrectionDistance,Movement.Correction,now);
                if(!double.IsNaN(lastSnapshot))Metrics.Timing(ClientMetric.SnapshotIntervalMS,(now-lastSnapshot)*1000,now);
            }
            lastSnapshot=now;
            if(actions.Count>0&&state.actionAck>=actions.Peek().ID){
                var action=actions.Dequeue();bool accepted=state.actionAck==action.ID&&(state.actionCode=="fired"||state.actionCode=="melee"||state.actionCode=="cast"||state.actionCode=="dashed"||state.actionCode=="attack_started"||state.actionCode=="cancelled");
                if(actionFeedback!=null&&!actionFeedback.Resolve(action.ID,accepted)){
                    // A queued command can outlive the short presentation window.
                    // Its first authority acknowledgement still counts and retains
                    // preview deduplication while that snapshot is presented.
                    if(actionFeedback.Begin(action.ID,now))actionFeedback.Resolve(action.ID,accepted);
                }
                LastActionCode=state.actionCode??"";
                ResolveAction?.Invoke(action.ID,accepted);
                if(actions.Count>0)nextInput=now;
            }
            Connecting=false;Status=State.Playing?"Playing":state.match?.phase=="ended"?"Match ended":"Waiting for scene loading";
        }
        private void Deliver(string payload,double now)=>Deliver(json.Read<Snapshot>(payload),now);
        private void Deliver(Snapshot state,double now)
        {
            if(state.type=="pong"){
                if(!probes.TryComplete(ProbeID(state.echo),now,out double rtt))return;RTTSeconds=rtt;
                clock.Observe(state.clockValid?state.clockTick:State.Tick,now,rtt);Metrics.Timing(ClientMetric.ConnectionRttMS,rtt*1000,now);return;
            }
            Apply(state,now);
        }
        private void Wire(string payload,double now)
        {
            if(received.Latest?.match?.phase=="ended")return;
            var envelope=json.Read<FrameEnvelope>(payload);if(envelope==null)throw new InvalidOperationException("Empty battle message");
            if(envelope.type=="error"){Disconnect(json.Read<Snapshot>(payload)?.code??"connection_closed");return;}
            Snapshot state;
            if(envelope.type=="frame"){
                if(!received.Joined||envelope.generation!=received.Generation)return;
                if(envelope.id<=highestFrame||envelope.tick<=received.Tick)return;
                if(envelope.protocol!=content.Rules.protocol||envelope.content!=content.Rules.content||envelope.matchID!=matchID||decoder==null)throw new InvalidOperationException("Invalid codec identity");
                var result=decoder.Decode(envelope.id,envelope.@base,envelope.data,out string decoded);
                if(result==FrameDecodeResult.MissingBaseline){MissingBaselines++;validatedAck=0;nextInput=now;return;}
                if(result==FrameDecodeResult.Stale)return;
                if(result!=FrameDecodeResult.Applied)throw new InvalidOperationException("Invalid encoded snapshot");
                payload=decoded;
                state=json.Read<Snapshot>(payload);if(state==null||state.type!="snapshot"||state.tick!=envelope.tick||state.generation!=envelope.generation)throw new InvalidOperationException("Codec envelope/payload mismatch");
            }else state=json.Read<Snapshot>(payload);
            if(state.type=="pong"){
                if(!received.Joined||state.protocol!=content.Rules.protocol||state.content!=content.Rules.content||state.matchID!=matchID||state.generation!=received.Generation||state.tick<0||state.clockValid&&(state.clockTick<0||state.clockTick>9007199254740991L)||!probes.Contains(ProbeID(state.echo)))return;
            }else {if(!received.Apply(state,content,matchID))return;ValidatedSnapshot?.Invoke(state);}
            if(envelope.type=="frame"){highestFrame=envelope.id;validatedAck=decoder.Ack;}
            if(state.type=="welcome"||conditioner==null)Deliver(state,now);
            else if(!conditioner.Enqueue(MessageDirection.Receive,payload,state.type=="snapshot",now))throw new InvalidOperationException("Battle delivery queue full");
        }
        public void Pump(double now,double x,double z)
        {
            long movementTicks=0;
            try{PumpCore(now,x,z,ref movementTicks);}
            finally{if(movementTicks>0)Metrics.Timing(ClientMetric.ClientStepMS,movementTicks*1000.0/Stopwatch.Frequency,now);}
        }
        private void AdvanceMovement(double now,double x,double z,ref long elapsed)
        {
            if(Movement==null)return;
            long started=Stopwatch.GetTimestamp();
            Movement.Advance(now,CanControl?x:0,CanControl?z:0);
            elapsed+=Stopwatch.GetTimestamp()-started;
        }
        private void PumpCore(double now,double x,double z,ref long movementTicks)
        {
            if(double.IsNaN(x+z)||double.IsInfinity(x+z)||Math.Abs(x)>1||Math.Abs(z)>1)throw new ArgumentException("Invalid drive");
            if(HasResult)return;
            Metrics.Frame(now);actionFeedback?.Advance(now);
            AdvanceMovement(now,x,z,ref movementTicks);
            while(Transport.TryRead(out var incoming)){
                lastWire=now;Metrics.Traffic(false,Encoding.UTF8.GetByteCount(incoming.JSON),now);
                try{Wire(incoming.JSON,now);}catch(Exception error){Disconnect("invalid_server_message");ValidationFailure=error.GetType().Name+": "+error.Message;break;}
                if(HasResult)break;
            }
            if(HasResult){FinishMatch();return;}
            if(Connecting&&now>deadline){Disconnect("connection_timeout");return;}
            if(!Connected)return;
            if(now-lastWire>4&&received.Latest?.match?.phase!="ended"){Disconnect("snapshot_timeout");return;}
            if(conditioner!=null)while(conditioner.TryTake(MessageDirection.Receive,now,out var pending)){
                try{Deliver(pending,now);}catch(Exception error){Disconnect("invalid_server_message");ValidationFailure=error.GetType().Name+": "+error.Message;return;}
            }
            if(HasResult){FinishMatch();return;}
            // A validated reliable result may still be behind simulated receive
            // delay. Its closed transport must never receive new input/probes.
            if(received.Latest?.match?.phase=="ended"){outboundConditioner?.ClearPending();return;}
            AdvanceMovement(now,x,z,ref movementTicks);
            FlushOutbound(now);
            if(!Connected||Transport.Sending)return;
            if(loadedRequested&&!loadedSent){
                loadedSent=Send(json.Write(new LoadedMessage{type="loaded",protocol=content.Rules.protocol,content=content.Rules.content,matchID=matchID,mapHash=content.Config.mapHash,generation=State.Generation}),now);return;
            }
            if(probes.TryBegin(now,out long echo)){
                Send(json.Write(new ProbeMessage{type="ping",protocol=content.Rules.protocol,content=content.Rules.content,matchID=matchID,generation=State.Generation,echo=echo}),now);return;
            }
            if(!CanControl)return;
            // Navigation produces a slightly different vector every frame.
            // Bound wire input even at uncapped native frame rates; prediction
            // still consumes every local frame and discrete actions wait <=1/60s.
            if(now>=nextWireInput&&(now>=nextInput||x!=lastX||z!=lastZ)){
                nextWireInput=now+1.0/60;
                lastX=x;lastZ=z;nextInput=now+1.0/content.Rules.tickRate;
                var action=actions.Count>0?actions.Peek():null;var message=new InputMessage{type="input",protocol=content.Rules.protocol,content=content.Rules.content,matchID=matchID,generation=State.Generation,seq=++sequence,x=x,z=z,frameAck=FrameAck,viewTick=Math.Max(0,(long)Math.Floor(Movement.RenderTick)),observedTick=State.Tick};
                message.facingX=facingX;message.facingZ=facingZ;
                if(action!=null){message.targetID=action.TargetID;message.targetLife=action.TargetLife;message.action=action.Kind;message.actionID=action.ID;message.actionTick=message.viewTick;message.aimX=action.X;message.aimZ=action.Z;message.hasGroundTarget=action.Ground;message.targetX=action.Ground?action.TargetX:0;message.targetZ=action.Ground?action.TargetZ:0;}
                Movement.SentInput(message.seq,now);inputReplies.TryStart(message.seq,now);Send(json.Write(message),now,true);
            }
        }
        private static long ProbeID(double echo)=>double.IsNaN(echo)||double.IsInfinity(echo)||echo<=0||echo>9007199254740991L||echo!=Math.Floor(echo)?0:(long)echo;
        private static IMessageConditioner CreateConditioner(double delay,double jitter,double loss,uint seed)=>delay==0&&jitter==0&&loss==0?null:BattleNetcode.CreateConditioner(new ConditioningSettings{DelaySeconds=delay,JitterSeconds=jitter,LossProbability=loss,Seed=seed});
        private void FinishMatch(){Transport.Close();Connecting=false;actions.Clear();probes.Clear();inputReplies.Clear();conditioner?.ClearPending();outboundConditioner?.ClearPending();Failure="";Status="Match ended";}
        private bool Send(string value,double now,bool discardable=false)
        {
            if(outboundConditioner==null)return Submit(value,now);
            if(!outboundConditioner.Enqueue(MessageDirection.Send,value,discardable,now)){Disconnect("send_queue_full");return false;}
            FlushOutbound(now);return Connected;
        }
        private void FlushOutbound(double now)
        {
            if(outboundConditioner==null)return;
            while(Connected&&!Transport.Sending&&outboundConditioner.TryTake(MessageDirection.Send,now,out string payload))if(!Submit(payload,now))break;
        }
        private bool Submit(string value,double now)
        {
            try{_=Transport.Send(value);Metrics.Traffic(true,Encoding.UTF8.GetByteCount(value),now);return true;}
            catch(InvalidOperationException){Disconnect("send_failed");return false;}
            catch(IOException){Disconnect("send_failed");return false;}
        }
        public bool WasActionPresented(long id)
        {
            if(settings?.SpeculativeActions!=true)return false;
            if(actionFeedback?.WasPresented(id)==true)return true;
            foreach(var pending in actions)if(pending.ID==id)return true;
            return false;
        }
        public void Face(double x,double z){double length=Math.Sqrt(x*x+z*z);if(double.IsNaN(length)||double.IsInfinity(length))throw new ArgumentException("Invalid facing");if(length>1e-8){facingX=x/length;facingZ=z/length;}}
        public bool QueueAction(string kind,double aimX,double aimZ,double now,bool ground=false,double targetX=0,double targetZ=0,string targetID=null,long targetLife=0)
        {
            if(!CanControl||actions.Count>=4)return false;
            if(kind!="fire"&&kind!="dash"&&kind!="skill"&&kind!="attack"&&kind!="cancel"||double.IsNaN(aimX+aimZ+targetX+targetZ)||double.IsInfinity(aimX+aimZ+targetX+targetZ))throw new ArgumentException("Invalid action intent");
            if(kind=="attack"&&(string.IsNullOrEmpty(targetID)||targetID.Length>32||targetLife<0))throw new ArgumentException("Invalid attack target");
            double length=Math.Sqrt(aimX*aimX+aimZ*aimZ);if(length<1e-8){if(kind!="attack"&&kind!="cancel"&&!ground)return false;aimX=0;aimZ=1;length=1;}
            var own=State.Latest.players.First(p=>p.playerID==State.PlayerID);if(own.hp<=0||own.left)return false;
            var action=new ActionIntent{ID=++actionSequence,Kind=kind,TargetID=targetID,TargetLife=targetLife,X=aimX/length,Z=aimZ/length,Ground=ground,TargetX=Math.Clamp(targetX,-content.Map.arenaHalfSize,content.Map.arenaHalfSize),TargetZ=Math.Clamp(targetZ,-content.Map.arenaHalfSize,content.Map.arenaHalfSize)};
            if(actionFeedback!=null&&!actionFeedback.Begin(action.ID,now))return false;
            actions.Enqueue(action);if(settings.SpeculativeActions){ActionStarts++;PreviewAction?.Invoke(action.ID,kind,action.X,action.Z,ground,action.TargetX,action.TargetZ);}nextInput=now;return true;
        }
        public void ForgetBaseline(){decoder?.Reset();nextInput=0;}
        public void PauseSnapshots(double now,double seconds)=>conditioner?.Pause(now,seconds);
        public void Disconnect(string code="")
        {
            Transport.Close();State.Clear();received.Clear();Movement=null;replicas?.Clear();decoder?.Reset();conditioner?.ClearPending();outboundConditioner?.ClearPending();clock?.Reset();actionFeedback?.Clear();
            facingX=facingZ=0;
            if(code.Length==0)ValidationFailure="";
            actions.Clear();probes.Clear();inputReplies.Clear();lastSnapshot=double.NaN;Connecting=false;loadedRequested=loadedSent=false;sequence=actionSequence=highestFrame=validatedAck=0;MissingBaselines=ActionStarts=0;RTTSeconds=0;LastActionCode="";Failure=code;Status=code.Length==0?"Disconnected":"Disconnected: "+code;
        }
        public void Dispose(){Disconnect();Transport.Dispose();}
    }
}
