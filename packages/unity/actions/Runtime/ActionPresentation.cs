using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Realtime.Actions
{
    public sealed class ActionPresentation : IActionPresentation
    {
        private sealed class Entry { public long ID; public double Created; public SpeculativeState State; }
        private readonly Dictionary<long,Entry> entries = new Dictionary<long,Entry>();
        private readonly Queue<Entry> order = new Queue<Entry>();
        private readonly double timeout, retention;
        private readonly int capacity;
        public int Pending {get;private set;}
        public long Confirmed {get;private set;}
        public long Rejected {get;private set;}
        public long Expired {get;private set;}
        public ActionPresentation(int capacity=128,double timeout=1,double retention=4)
        { if(capacity<1 || timeout<=0 || retention<timeout || double.IsNaN(timeout+retention) || double.IsInfinity(timeout+retention))throw new ArgumentOutOfRangeException();this.capacity=capacity;this.timeout=timeout;this.retention=retention; }
        public bool Begin(long id,double now)
        {
            if(id<=0 || double.IsNaN(now) || double.IsInfinity(now))return false; Advance(now);
            if(entries.ContainsKey(id) || entries.Count>=capacity)return false;
            var e=new Entry{ID=id,Created=now,State=SpeculativeState.Pending};entries.Add(id,e);order.Enqueue(e);Pending++;return true;
        }
        public bool WasPresented(long id)=>entries.ContainsKey(id);
        public bool Resolve(long id,bool accepted)
        {
            if(!entries.TryGetValue(id,out var e) || e.State==SpeculativeState.Confirmed || e.State==SpeculativeState.Rejected)return false;
            if(e.State==SpeculativeState.Pending)Pending--;
            e.State=accepted?SpeculativeState.Confirmed:SpeculativeState.Rejected;if(accepted)Confirmed++;else Rejected++;return true;
        }
        public void Advance(double now)
        {
            foreach(var e in order)if(e.State==SpeculativeState.Pending && now-e.Created>timeout){e.State=SpeculativeState.Expired;Pending--;Expired++;}
            while(order.Count>0 && now-order.Peek().Created>retention){var e=order.Dequeue();entries.Remove(e.ID);}
        }
        public void Clear(){entries.Clear();order.Clear();Pending=0;Confirmed=Rejected=Expired=0;}
    }
}
