using System;
using System.Collections.Generic;

namespace Realtime.Examples.HistoryLesson
{
    // Application-owned target poses sampled on the existing SDK render tick.
    public sealed class TargetTrack
    {
        private sealed class Frame{public long Tick;public TargetState Target;}
        private readonly List<Frame> frames=new List<Frame>();
        public void Accept(ServerMessage message)
        {
            if(message.type=="welcome"||frames.Count>0&&frames[frames.Count-1].Target.life!=message.target.life)Clear();
            frames.Add(new Frame{Tick=message.tick,Target=ReplicaState.CloneTarget(message.target)});if(frames.Count>128)frames.RemoveAt(0);
        }
        public bool Sample(double renderTick,out TargetState target,out long tick)
        {
            target=null;tick=0;if(frames.Count==0)return false;
            double time=Math.Max(frames[0].Tick,Math.Min(frames[frames.Count-1].Tick,renderTick));
            Frame before=frames[0],after=before;
            foreach(var frame in frames){after=frame;if(frame.Tick>=time)break;before=frame;}
            target=ReplicaState.CloneTarget(before.Target);double amount=after.Tick==before.Tick?0:(time-before.Tick)/(after.Tick-before.Tick);
            target.x+=(after.Target.x-target.x)*(float)amount;target.z+=(after.Target.z-target.z)*(float)amount;tick=(long)Math.Floor(time);return true;
        }
        public void Clear()=>frames.Clear();
    }
}
