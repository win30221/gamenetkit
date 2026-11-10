using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Realtime.Core;
using Realtime.Transport;
using Realtime.Simulation;

namespace Realtime.Examples.InterestLesson
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
        private bool interestDirty=true;
        public string InterestMode{get;private set;}="all";
        public double Radius{get;private set;}=3;
        public int SnapshotBytes{get;private set;}
        public ActionFeedback Feedback{get;}=new ActionFeedback();
        public ReplicaState State {get;}=new ReplicaState();
        public PredictedPlayer Own {get;}=new PredictedPlayer();
        public RemotePresentation Remote {get;}=new RemotePresentation();
        public string Status {get;private set;}="Disconnected";
        public bool Connecting {get;private set;}
        public bool Connected=>State.Joined;
        public ExampleSession(Func<string,ServerMessage> decode,Func<InputMessage,string> encode)
        {this.decode=decode;this.encode=encode;ConfigureDelay(0);}
        public void SetInterest(string mode,double radius)
        {
            if((mode!="all"&&mode!="aoi") || double.IsNaN(radius) || double.IsInfinity(radius) || radius<2 || radius>6)throw new ArgumentException("interest");
            InterestMode=mode;Radius=radius;interestDirty=true;
        }
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
            Disconnect();interestDirty=true;sequence=0;previous=held=default;nextSend=now;deadline=now+10;Connecting=true;Status="Connecting";
            int epoch=Transport.BeginAttempt();
            Transport.Open(epoch,endpoint,"{\"type\":\"hello\",\"protocol\":1}","",callbackTarget,false);
        }
        public void QueueBump(){if(Connected)bump=true;}
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
                    if(message.type=="action-result"){
                        if(!Connected || message.protocol!=1 || message.id<=0 || string.IsNullOrEmpty(message.kind) || float.IsNaN(message.x+message.z) || float.IsInfinity(message.x+message.z))throw new InvalidOperationException("invalid result");
                        if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,false,now))throw new InvalidOperationException("result queue full");
                        continue;
                    }
                    if(!received.Apply(message))continue;
                    if(message.type=="welcome"){delivery.ClearPending();Apply(message,now,Encoding.UTF8.GetByteCount(incoming.JSON));Own.Advance(now,held);}
                    else if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,true,now))throw new InvalidOperationException("snapshot queue full");
                } catch(Exception){Disconnect("Disconnected: invalid server message");break;}
            }
            if(Connecting && now>deadline)Disconnect("Disconnected: welcome timeout");
            if(!Connected)return;
            while(delivery.TryTake(MessageDirection.Receive,now,out var text)){
                var message=decode(text);if(message.type=="action-result")Feedback.Resolve(message);else Apply(message,now,Encoding.UTF8.GetByteCount(text));
            }
            Remote.Advance(now);
            if(Transport.Sending)return;
            if(interestDirty){interestDirty=false;_ = Transport.Send("{\"type\":\"interest\",\"mode\":\""+InterestMode+"\",\"radius\":"+Radius.ToString(CultureInfo.InvariantCulture)+"}");return;}
            if(actions.Count>0){_ = Transport.Send(actions.Dequeue());return;}
            if(bump){bump=false;_ = Transport.Send("{\"type\":\"bump\"}");return;}
            if(now>=nextSend || previous.X!=x || previous.Z!=z) {
                previous=held;nextSend=now+1.0/30;
                Own.SentInput(++sequence,now);
                _ = Transport.Send(encode(new InputMessage {seq=sequence,x=x,z=z}));
            }
        }
        private void Apply(ServerMessage message,double now,int bytes)
        {
            if(!State.Apply(message))return;
            if(message.type=="snapshot")SnapshotBytes=bytes;
            Own.Reconcile(State.Players[State.LocalPlayerID],State.Tick,now,message.type=="welcome");
            Remote.Accept(message,now);
            Connecting=false;Status="Connected";
        }
        public void Disconnect(string status="Disconnected")
        {Transport.Close();State.Clear();received.Clear();delivery.ClearPending();Own.Clear();Remote.Clear();Feedback.Clear();actions.Clear();actionSequence=0;lastAction=null;SnapshotBytes=0;Connecting=false;bump=false;Status=status;}
        public void Dispose(){Disconnect();Transport.Dispose();}
    }
}
