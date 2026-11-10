using System;
using System.Collections.Generic;
using System.Globalization;
using Realtime.Core;
using Realtime.Transport;
using Realtime.Simulation;

namespace Realtime.Examples.HistoryLesson
{
    // Single owner: input history, validated snapshots, delayed delivery and reset.
    public sealed class ExampleSession : IDisposable
    {
        public MessageTransport Transport {get;}=new MessageTransport();
        private readonly Func<string,ServerMessage> decode;
        private readonly Func<InputMessage,string> encode;
        private readonly ReplicaState received=new ReplicaState();
        private MessageConditioner delivery;
        private long sequence;
        private double nextSend,deadline;
        private double observedNow;
        private Drive previous,held;
        private bool bump;
        private readonly Queue<string> actions=new Queue<string>();
        private long actionSequence;
        private string lastAction;
        private bool inspectEnabled,queryPending;
        private double lookback=1,nextQuery,queryDeadline;
        private long querySequence,pendingQuery,queryRevision,pendingRevision;
        public ServerMessage Preview{get;private set;}
        public ShotOutcome LastShot{get;private set;}
        public long Hits{get;private set;}
        public long Misses{get;private set;}
        public long RejectedShots{get;private set;}
        public TargetTrack Targets{get;}=new TargetTrack();
        public ActionFeedback Feedback{get;}=new ActionFeedback();
        public ReplicaState State {get;}=new ReplicaState();
        public PredictedPlayer Own {get;}=new PredictedPlayer();
        public RemotePresentation Remote {get;}=new RemotePresentation();
        public string Status {get;private set;}="Disconnected";
        public bool Connecting {get;private set;}
        public bool Connected=>State.Joined;
        public ExampleSession(Func<string,ServerMessage> decode,Func<InputMessage,string> encode)
        {this.decode=decode;this.encode=encode;ConfigureDelay(.15);}
        public void ConfigureDelay(double seconds)
        {
            if(seconds<0 || seconds>.3 || double.IsNaN(seconds))throw new ArgumentException("delay");
            var next=new MessageConditioner(new ConditioningSettings {DelaySeconds=seconds,Seed=17});
            if(delivery!=null)while(delivery.TryTake(MessageDirection.Receive,double.MaxValue,out var pending))
                if(!next.Enqueue(MessageDirection.Receive,pending,false,observedNow))throw new InvalidOperationException("delivery queue full");
            delivery=next;
        }
        public void Connect(string endpoint,double now,string callbackTarget="")
        {
            Disconnect();sequence=0;previous=held=default;nextSend=now;deadline=now+10;Connecting=true;Status="Connecting";
            int epoch=Transport.BeginAttempt();
            Transport.Open(epoch,endpoint,"{\"type\":\"hello\",\"protocol\":1}","",callbackTarget,false);
        }
        public void QueueBump(){if(Connected)bump=true;}
        public void Inspect(bool enabled,double seconds)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0||seconds>3)throw new ArgumentException("history offset");
            inspectEnabled=enabled;lookback=seconds;queryRevision++;Preview=null;nextQuery=0;
            if(!enabled)queryPending=false;
        }
        public void ResetTarget(){if(Connected&&actions.Count<8)actions.Enqueue("{\"type\":\"reset-target\"}");}
        public long Fire(bool compensate,double now,bool expired=false,float? aimX=null,float? aimZ=null)
        {
            if(!Connected||actions.Count>=8||!Targets.Sample(Remote.RenderTick,out var target,out long tick))return 0;
            float x=aimX??target.x,z=aimZ??target.z;
            if(float.IsNaN(x+z)||float.IsInfinity(x+z)||Math.Abs(x)>8||Math.Abs(z)>8)throw new ArgumentException("aim");
            long id=++actionSequence;if(!Feedback.Begin(id,"shot",false,x,z,now))return 0;
            if(expired)tick=Math.Max(0,tick-30);
            string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
            actions.Enqueue("{\"type\":\"shot\",\"id\":"+id+",\"tick\":"+tick+",\"observed\":"+State.Tick+",\"life\":"+target.life+",\"x\":"+F(x)+",\"z\":"+F(z)+",\"compensate\":"+(compensate?"true":"false")+"}");return id;
        }
        public long QueueAction(string kind,bool speculative,double now)
        {
            if(kind!="spawn" && kind!="remove" && kind!="rebirth" && kind!="deny" && kind!="timeout")throw new ArgumentException("kind");
            if(!Connected || actions.Count>=8)return 0;
            var own=State.Players[State.LocalPlayerID];long id=++actionSequence;
            if(!Feedback.Begin(id,kind,speculative,own.x+1.5f,own.z+1.5f,now))return 0;
            lastAction="{\"type\":\"action\",\"id\":"+id+",\"kind\":\""+kind+"\"}";actions.Enqueue(lastAction);return id;
        }
        public void ResendLast(){if(Connected && lastAction!=null && actions.Count<8)actions.Enqueue(lastAction);}
        public void Pump(double now,float x,float z)
        {
            observedNow=now;held=new Drive(x,z);Own.Advance(now,held);Feedback.Advance(now);
            while(Transport.TryRead(out var incoming)) {
                try {
                    var message=decode(incoming.JSON);
                    if(message==null)throw new InvalidOperationException();
                    if(message.type=="error"){Disconnect("Disconnected: "+message.code);break;}
                    if(message.type=="history-result"){
                        if(message.protocol!=1||message.id<=0||message.tick<0||message.history==null)throw new InvalidOperationException("invalid history response");
                        if(message.found)ReplicaState.ValidateTarget(message.target);
                        if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,false,now))throw new InvalidOperationException("history queue full");continue;
                    }
                    if(message.type=="action-result"){
                        if(!Connected || message.protocol!=1 || message.id<=0 || string.IsNullOrEmpty(message.kind) || float.IsNaN(message.x+message.z) || float.IsInfinity(message.x+message.z))throw new InvalidOperationException("invalid result");
                        if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,false,now))throw new InvalidOperationException("result queue full");
                        continue;
                    }
                    if(!received.Apply(message))continue;
                    if(message.type=="welcome"){delivery.ClearPending();Apply(message,now);Own.Advance(now,held);}
                    else if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,true,now))throw new InvalidOperationException("snapshot queue full");
                } catch(Exception){Disconnect("Disconnected: invalid server message");break;}
            }
            if(Connecting && now>deadline)Disconnect("Disconnected: welcome timeout");
            if(!Connected)return;
            while(delivery.TryTake(MessageDirection.Receive,now,out var text)){
                var message=decode(text);
                if(message.type=="history-result"){
                    if(inspectEnabled&&message.id==pendingQuery){queryPending=false;if(pendingRevision==queryRevision&&(!message.found||message.target.life==State.Target.life))Preview=message;}
                }else if(message.type=="action-result"){
                    if(Feedback.Resolve(message)&&message.shot!=null){var shot=message.shot;if(shot.id!=message.id||shot.accepted!=message.accepted||shot.usedTick<0||shot.shotTick<shot.usedTick)throw new InvalidOperationException("invalid shot result");LastShot=shot;if(!shot.accepted)RejectedShots++;else if(shot.hit)Hits++;else Misses++;}
                }else Apply(message,now);
            }
            Remote.Advance(now);
            if(Transport.Sending)return;
            if(actions.Count>0){_ = Transport.Send(actions.Dequeue());return;}
            if(bump){bump=false;_ = Transport.Send("{\"type\":\"bump\"}");return;}
            if(inspectEnabled&&now>=nextQuery&&(!queryPending||now>queryDeadline)){
                pendingQuery=++querySequence;pendingRevision=queryRevision;queryPending=true;queryDeadline=now+2;nextQuery=now+.2;
                long tick=Math.Max(0,State.Tick-(long)Math.Round(lookback*30));
                _ = Transport.Send("{\"type\":\"history\",\"id\":"+pendingQuery+",\"tick\":"+tick+"}");return;
            }
            if(now>=nextSend || previous.X!=x || previous.Z!=z) {
                previous=held;nextSend=now+1.0/30;
                Own.SentInput(++sequence,now);
                _ = Transport.Send(encode(new InputMessage {seq=sequence,x=x,z=z}));
            }
        }
        private void Apply(ServerMessage message,double now)
        {
            if(!State.Apply(message))return;
            Targets.Accept(message);if(Preview?.found==true&&Preview.target.life!=State.Target.life)Preview=null;
            Own.Reconcile(State.Players[State.LocalPlayerID],State.Tick,now,message.type=="welcome");
            Remote.Accept(message,now);
            Connecting=false;Status="Connected";
        }
        public void Disconnect(string status="Disconnected")
        {Transport.Close();State.Clear();received.Clear();delivery.ClearPending();Own.Clear();Remote.Clear();Targets.Clear();Feedback.Clear();actions.Clear();actionSequence=querySequence=pendingQuery=0;queryPending=false;Preview=null;LastShot=null;Hits=Misses=RejectedShots=0;lastAction=null;Connecting=false;bump=false;Status=status;}
        public void Dispose(){Disconnect();Transport.Dispose();}
    }
}
