using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Realtime.Examples.NetworkRecovery
{
    public sealed class RecoveryEvent
    {public double at {get;set;} public string kind {get;set;} public string detail {get;set;}}
    public sealed class RecoveryController
    {
        private readonly ReconnectPolicy policy;
        private readonly Queue<RecoveryEvent> events=new Queue<RecoveryEvent>();
        private double origin=double.NaN;
        private bool welcomed;
        public bool Enabled {get;private set;}=true;
        public bool Pending=>policy.Pending;
        public int Attempts=>policy.Attempts;
        public int Rejoins {get;private set;}
        public RecoveryEvent[] Events=>events.ToArray();
        public RecoveryController(ReconnectPolicy policy=null){this.policy=policy??new ReconnectPolicy();}
        public double Wait(double now)=>Pending&&!double.IsInfinity(policy.Next)?Math.Max(0,policy.Next-now):0;
        public void Record(double now,string kind,string detail)
        {
            if(double.IsNaN(origin))origin=now;
            events.Enqueue(new RecoveryEvent {at=Math.Max(0,now-origin),kind=kind,detail=detail});
            while(events.Count>16)events.Dequeue();
        }
        public void ManualStart(double now){policy.Stop();Record(now,"connecting","manual");}
        public void Connected(double now,long id)
        {
            if(welcomed)Rejoins++;welcomed=true;
            if(Enabled)policy.Connected();else policy.Stop();
            Record(now,"connected","player "+id);
        }
        public void Failed(double now,bool retryable,string reason)
        {
            Record(now,"lost",reason);
            if(Enabled&&policy.Fail(now,retryable))Record(now,"scheduled",Wait(now).ToString("F1")+"s");
            else policy.Stop();
        }
        public string Poll(double now)
        {
            string result=policy.Poll(now);
            if(result=="retry")Record(now,"retry",policy.Attempts.ToString());
            if(result=="expired")Record(now,"expired","retry window exhausted");
            return result;
        }
        public void Stop(double now){policy.Stop();Record(now,"stopped","manual stop; no retry");}
        public void SetEnabled(bool value,bool connected,double now)
        {Enabled=value;if(!value)policy.Stop();else if(connected)policy.Connected();Record(now,"auto",value?"on":"off");}
    }
}
