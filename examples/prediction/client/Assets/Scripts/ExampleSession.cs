using System;
using Realtime.Core;
using Realtime.Transport;
using Realtime.Simulation;

namespace Realtime.Examples.PredictionLesson
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
        private Drive previous,held;
        private bool bump;
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
            delivery=new MessageConditioner(new ConditioningSettings {DelaySeconds=seconds,Seed=17});
        }
        public void Connect(string endpoint,double now,string callbackTarget="")
        {
            Disconnect();sequence=0;previous=held=default;nextSend=now;deadline=now+10;Connecting=true;Status="Connecting";
            int epoch=Transport.BeginAttempt();
            Transport.Open(epoch,endpoint,"{\"type\":\"hello\",\"protocol\":1}","",callbackTarget,false);
        }
        public void QueueBump(){if(Connected)bump=true;}
        public void Pump(double now,float x,float z)
        {
            held=new Drive(x,z);Own.Advance(now,held);
            while(Transport.TryRead(out var incoming)) {
                try {
                    var message=decode(incoming.JSON);
                    if(message==null)throw new InvalidOperationException();
                    if(message.type=="error"){Disconnect("Disconnected: "+message.code);break;}
                    if(!received.Apply(message))continue;
                    if(message.type=="welcome"){delivery.ClearPending();Apply(message,now);Own.Advance(now,held);}
                    else if(!delivery.Enqueue(MessageDirection.Receive,incoming.JSON,true,now))throw new InvalidOperationException("snapshot queue full");
                } catch(Exception){Disconnect("Disconnected: invalid server message");break;}
            }
            if(Connecting && now>deadline)Disconnect("Disconnected: welcome timeout");
            if(!Connected)return;
            while(delivery.TryTake(MessageDirection.Receive,now,out var text))Apply(decode(text),now);
            Remote.Advance(now);
            if(Transport.Sending)return;
            if(bump){bump=false;_ = Transport.Send("{\"type\":\"bump\"}");return;}
            if(now>=nextSend || previous.X!=x || previous.Z!=z) {
                previous=held;nextSend=now+1.0/30;
                Own.SentInput(++sequence,now);
                _ = Transport.Send(encode(new InputMessage {seq=sequence,x=x,z=z}));
            }
        }
        private void Apply(ServerMessage message,double now)
        {
            if(!State.Apply(message))return;
            Own.Reconcile(State.Players[State.LocalPlayerID],State.Tick,now,message.type=="welcome");
            Remote.Accept(message,now);
            Connecting=false;Status="Connected";
        }
        public void Disconnect(string status="Disconnected")
        {Transport.Close();State.Clear();received.Clear();delivery.ClearPending();Own.Clear();Remote.Clear();Connecting=false;bump=false;Status=status;}
        public void Dispose(){Disconnect();Transport.Dispose();}
    }
}
