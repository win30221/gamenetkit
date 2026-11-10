using System;
using Realtime.Core;
using Realtime.Simulation;

namespace Realtime.Examples.NetworkRecovery
{
    public sealed class SimulatedLink
    {
        private MessageConditioner send,receive;
        public double UpMS {get;private set;}
        public double DownMS {get;private set;}
        public double JitterMS {get;private set;}
        public double LossPercent {get;private set;}
        public ConditioningSummary Send=>send.Summary;
        public ConditioningSummary Receive=>receive.Summary;
        public SimulatedLink(){Configure(0,150,0,0,0);}
        public void Configure(double up,double down,double jitter,double loss,double now)
        {
            if(!Valid(up,300)||!Valid(down,300)||!Valid(jitter,100)||!Valid(loss,30))throw new ArgumentException("network settings");
            double paused=receive?.Summary.PausedUntil??0;
            var tx=new MessageConditioner(new ConditioningSettings {DelaySeconds=up/1000,JitterSeconds=jitter/1000,LossProbability=loss/100,Seed=17});
            var rx=new MessageConditioner(new ConditioningSettings {DelaySeconds=down/1000,JitterSeconds=jitter/1000,LossProbability=loss/100,Seed=29});
            if(paused>now)rx.Pause(now,Math.Min(10,paused-now));
            UpMS=up;DownMS=down;JitterMS=jitter;LossPercent=loss;send=tx;receive=rx;
        }
        private static bool Valid(double value,double max)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0&&value<=max;
        public bool Outgoing(string value,bool discardable,double now)=>send.Enqueue(MessageDirection.Send,value,discardable,now);
        public bool Incoming(string value,bool discardable,double now)=>receive.Enqueue(MessageDirection.Receive,value,discardable,now);
        public bool TakeOutgoing(double now,out string value)=>send.TryTake(MessageDirection.Send,now,out value);
        public bool TakeIncoming(double now,out string value)=>receive.TryTake(MessageDirection.Receive,now,out value);
        public void PauseSnapshots(double now,double seconds)=>receive.Pause(now,seconds);
        public void Clear(){send.ClearPending();receive.ClearPending();}
    }
}
