using System;
using System.Collections.Generic;
using Realtime.Core;
using Realtime.Interpolation;

namespace Realtime.Examples.CodecLesson
{
    // Application snapshot storage + DTO sampling. SDK timelines own only time.
    public sealed class RemotePresentation
    {
        private sealed class Frame { public long Tick; public double Arrival; public PlayerState[] Players; }
        private readonly List<Frame> frames = new List<Frame>();
        private readonly Dictionary<long,long> firstVisible=new Dictionary<long,long>();
        private readonly TimelineCatalog catalog = new TimelineCatalog();
        private IPresentationTimeline timeline;
        public string Mode { get; private set; } = "adaptive";
        public double RenderTick => frames.Count == 0 ? -1 : timeline.RenderTick;
        public TimelineMetrics Metrics => timeline.Metrics;
        public int BufferedFrames => frames.Count;

        public RemotePresentation()
        {
            catalog.Register("fixed", (rate, snapshots) => new FixedTimeline(rate, snapshots));
            catalog.Register("adaptive", (rate, snapshots) => new SnapshotInterpolation(rate, snapshots));
            timeline = catalog.Create(Mode, 30, 15);
        }

        public void SetMode(string mode, double now)
        {
            var next = catalog.Create(mode, 30, 15); // Unknown mode cannot mutate active state.
            Mode = mode; timeline = next;
            if (frames.Count == 0) return;
            timeline.Reset(frames[0].Tick, frames[0].Arrival);
            for (int i=1; i<frames.Count; i++) timeline.Observe(frames[i].Tick, frames[i].Arrival);
            timeline.Advance(now);
        }

        public void Accept(ServerMessage message, double now)
        {
            if (message.type == "welcome") Clear();
            if (frames.Count > 0 && message.tick <= frames[frames.Count-1].Tick) return;
            var players = new PlayerState[message.players.Length];
            for (int i=0; i<players.Length; i++) {
                var p=message.players[i];
                players[i]=new PlayerState {id=p.id, x=p.x, z=p.z, ack=p.ack};
            }
            var present=new HashSet<long>();foreach(var p in players){present.Add(p.id);if(!firstVisible.ContainsKey(p.id))firstVisible.Add(p.id,message.tick);}
            var departed=new List<long>();foreach(var id in firstVisible.Keys)if(!present.Contains(id))departed.Add(id);foreach(var id in departed)firstVisible.Remove(id);
            frames.Add(new Frame {Tick=message.tick, Arrival=now, Players=players});
            if (frames.Count == 1) timeline.Reset(message.tick, now);
            else timeline.Observe(message.tick, now);
            if (frames.Count > 128) frames.RemoveAt(0);
        }

        public void Advance(double now) { if (frames.Count > 0) timeline.Advance(now); }
        public void Clear() { frames.Clear();firstVisible.Clear(); timeline = catalog.Create(Mode, 30, 15); }

        public bool Latest(long id, out float x, out float z)
        {
            var player=frames.Count == 0 ? null : Find(frames[frames.Count-1], id);
            x=player?.x ?? 0; z=player?.z ?? 0; return player != null;
        }

        public bool Sample(long id, out float x, out float z)
        {
            x=z=0;
            if (frames.Count == 0) return false;
            // A departed ID must not be resurrected from old buffered frames.
            var last=Find(frames[frames.Count-1], id);
            if (last == null) return false;
            // Re-entry starts a new presentation baseline for this ID. Buffered
            // poses from before it left the AOI must never appear again.
            Frame before=null,after=null;
            foreach (var frame in frames) {
                if(frame.Tick<firstVisible[id])continue;
                if(before==null)before=frame;
                after=frame;
                if (frame.Tick >= RenderTick) break;
                before=frame;
            }
            if(before==null || after==null){x=last.x;z=last.z;return true;}
            var a=Find(before,id); var b=Find(after,id);
            if (a == null || b == null) {x=last.x; z=last.z; return true;}
            double alpha=after.Tick==before.Tick ? 0 : Math.Clamp((RenderTick-before.Tick)/(after.Tick-before.Tick),0,1);
            x=(float)(a.x+(b.x-a.x)*alpha); z=(float)(a.z+(b.z-a.z)*alpha);
            return true;
        }
        private static PlayerState Find(Frame frame, long id)
        { foreach (var player in frame.Players) if (player.id==id) return player; return null; }
    }
}
