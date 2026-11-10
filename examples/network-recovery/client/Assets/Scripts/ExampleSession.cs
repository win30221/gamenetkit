using System;
using System.Text;
using Realtime.Core;
using Realtime.Transport;

namespace Realtime.Examples.NetworkRecovery
{
    public sealed class ExampleSession : IDisposable
    {
        public MessageTransport Transport {get;}=new MessageTransport();
        private readonly Func<string,ServerMessage> decode;
        private readonly Func<InputMessage,string> encode;
        private readonly ReplicaState received=new ReplicaState();
        private long sequence,probe;
        private double nextSend,nextProbe,deadline,lastWire,blockedUntil,lastNow;
        private string endpoint,callbackTarget="";
        private Drive previous,held;
        public ReplicaState State {get;}=new ReplicaState();
        public PredictedPlayer Own {get;}=new PredictedPlayer();
        public RemotePresentation Remote {get;}=new RemotePresentation();
        public SimulatedLink Link {get;}=new SimulatedLink();
        public SessionTelemetry Telemetry {get;}=new SessionTelemetry();
        public RecoveryController Recovery {get;}
        public string Status {get;private set;}="Disconnected";
        public bool Connecting {get;private set;}
        public bool Connected=>State.Joined;
        public double BlockRemaining(double now)=>Math.Max(0,blockedUntil-now);
        public ExampleSession(Func<string,ServerMessage> decode,Func<InputMessage,string> encode,ReconnectPolicy policy=null)
        {this.decode=decode;this.encode=encode;Recovery=new RecoveryController(policy);Remote.SetClock(Telemetry.Clock,0);}
        public void ConfigureDelay(double seconds)=>Link.Configure(0,seconds*1000,0,0,lastNow);
        public void SetClock(string mode,double now){Telemetry.SelectClock(mode);Remote.SetClock(Telemetry.Clock,now);}
        public void SetAutoReconnect(bool enabled,double now)
        {
            bool retrying=Recovery.Pending&&!Connected;
            Recovery.SetEnabled(enabled,Connected,now);
            if(!enabled&&retrying){ResetAttempt();Status="Automatic retry disabled";}
        }
        public void Connect(string endpoint,double now,string callbackTarget="")
        {this.endpoint=endpoint;this.callbackTarget=callbackTarget;Recovery.ManualStart(now);StartAttempt(now);}
        private void ResetAttempt()
        {Transport.Close();State.Clear();received.Clear();Link.Clear();Own.Clear();Remote.Clear();Telemetry.Reset();Connecting=false;previous=held=default;}
        private void StartAttempt(double now)
        {
            ResetAttempt();lastNow=now;
            if(now<blockedUntil){Failed(now,"simulated link unavailable",true);return;}
            sequence=0;nextSend=now;nextProbe=now+.25;deadline=now+10;lastWire=now;Connecting=true;Status="Connecting";
            int epoch=Transport.BeginAttempt();
            try{Transport.Open(epoch,endpoint,"{\"type\":\"hello\",\"protocol\":1}","",callbackTarget,false);}
            catch(Exception){Failed(now,"connection_closed",true);}
        }
        private void Failed(double now,string reason,bool retryable)
        {ResetAttempt();Status="Disconnected: "+reason;Recovery.Failed(now,retryable,reason);}
        public void SimulateOutage(double now,double seconds=4)
        {
            if(!Connected)return;
            if(seconds<0||seconds>10||double.IsNaN(seconds))throw new ArgumentException("outage");
            blockedUntil=now+seconds;Failed(now,"simulated link loss",true);
        }
        public void QueueBump(){if(Connected&&!Link.Outgoing("{\"type\":\"bump\"}",false,lastNow))Failed(lastNow,"send queue full",false);}
        public void Pump(double now,float x,float z)
        {
            lastNow=now;Telemetry.Metrics.Frame(now);
            string retry=Recovery.Poll(now);
            if(retry=="expired"){ResetAttempt();Status="Reconnect window expired";}
            if(retry=="retry")StartAttempt(now);
            held=new Drive(x,z);Own.Advance(now,held);
            while(Transport.TryRead(out var incoming)) {
                Telemetry.Metrics.Traffic(false,Encoding.UTF8.GetByteCount(incoming.JSON),now);lastWire=now;
                try {
                    var message=decode(incoming.JSON);if(message==null)throw new InvalidOperationException();
                    if(message.type=="error"){Failed(now,message.code,message.code=="connection_closed"||message.code=="send_failed");break;}
                    if(message.type=="pong") {
                        if(message.protocol!=1||message.id<=0||message.tick<0)throw new InvalidOperationException();
                        if(!Link.Incoming(incoming.JSON,false,now))throw new InvalidOperationException("receive queue full");
                        continue;
                    }
                    if(!received.Apply(message))continue;
                    if(message.type=="welcome") {Link.Clear();Apply(message,now);Own.Advance(now,held);Recovery.Connected(now,State.LocalPlayerID);}
                    else if(!Link.Incoming(incoming.JSON,true,now))throw new InvalidOperationException("snapshot queue full");
                }catch(Exception){Failed(now,"invalid server message",false);break;}
            }
            if(Connecting&&now>deadline)Failed(now,"welcome timeout",true);
            if(Connected&&now-lastWire>4)Failed(now,"no server traffic",true);
            if(!Connected)return;
            while(Link.TakeIncoming(now,out string text)) {
                var message=decode(text);
                if(message.type=="pong")Telemetry.Pong(message,now);else Apply(message,now);
            }
            Remote.Advance(now);
            if(now>=nextSend||previous.X!=x||previous.Z!=z) {
                previous=held;nextSend=now+1.0/30;
                long seq=++sequence;Telemetry.Input(seq,now);
                if(!Link.Outgoing(encode(new InputMessage {seq=seq,x=x,z=z}),true,now)){Failed(now,"send queue full",false);return;}
                // This is the simulated network submission boundary. Delayed
                // physical forwarding must not shift the prediction history.
                Own.SentInput(seq,now);
            }
            if(now>=nextProbe) {
                nextProbe=now+.5;Telemetry.Probe(++probe,now);
                if(!Link.Outgoing("{\"type\":\"ping\",\"id\":"+probe+"}",false,now)){Failed(now,"send queue full",false);return;}
            }
            if(!Transport.Sending&&Link.TakeOutgoing(now,out string outgoing)) {
                Telemetry.Metrics.Traffic(true,Encoding.UTF8.GetByteCount(outgoing),now);
                _=Transport.Send(outgoing);
            }
        }
        private void Apply(ServerMessage message,double now)
        {
            bool stalled=Connected&&Telemetry.SnapshotAge(now)>.8;
            if(!State.Apply(message))return;
            var own=State.Players[State.LocalPlayerID];
            Own.Reconcile(own,State.Tick,now,message.type=="welcome");Remote.Accept(message,now);
            Telemetry.Snapshot(State.Tick,own.ack,Own.LastError,now);
            if(stalled)Recovery.Record(now,"resumed","snapshots resumed");
            Connecting=false;Status="Connected";
        }
        public void Disconnect(string status="Disconnected")
        {Recovery.Stop(lastNow);ResetAttempt();Status=status;}
        public void Dispose(){Disconnect();Transport.Dispose();}
    }
}
