using System;
using System.Collections.Generic;
using System.Text;
using Realtime.Core;

namespace Realtime.Simulation
{
    public sealed class MessageConditioner : IMessageConditioner
    {
        public const int MaxMessageBytes=32*1024;
        private struct Scheduled { public string Payload; public double Due; }
        private sealed class Lane
        {
            public readonly Queue<Scheduled> Queue = new Queue<Scheduled>();
            public uint Random; public double LastDue; public long Dropped;
        }
        private readonly Lane send, receive;
        private readonly ConditioningSettings settings;
        private readonly int capacity;
        private double pausedUntil;
        public MessageConditioner(ConditioningSettings settings, int capacity = 128)
        {
            settings.Validate(); if (capacity < 1 || capacity > 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.settings = settings; this.capacity = capacity;
            uint seed = settings.Seed == 0 ? 1u : settings.Seed;
            send = new Lane { Random = seed }; receive = new Lane { Random = seed ^ 0x9e3779b9u };
            if (receive.Random == 0) receive.Random = 1;
        }
        private static double Next(Lane lane)
        { uint x = lane.Random; x ^= x << 13; x ^= x >> 17; x ^= x << 5; lane.Random = x; return x / 4294967296.0; }
        private Lane Get(MessageDirection direction) => direction == MessageDirection.Send ? send : receive;
        private static void ValidTime(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now)); }
        public bool Enqueue(MessageDirection direction, string payload, bool discardable, double observedAt)
        {
            ValidTime(observedAt); if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (Encoding.UTF8.GetByteCount(payload) > MaxMessageBytes) return false;
            var lane = Get(direction);
            if (discardable && (observedAt < pausedUntil || Next(lane) < settings.LossProbability)) { lane.Dropped++; return true; }
            if (lane.Queue.Count >= capacity) return false; // fail explicitly; never silently discard a control message
            double delay = Math.Max(0, settings.DelaySeconds + (Next(lane) * 2 - 1) * settings.JitterSeconds);
            double due = Math.Max(lane.LastDue, observedAt + delay); lane.LastDue = due;
            lane.Queue.Enqueue(new Scheduled { Payload = payload, Due = due }); return true;
        }
        public bool TryTake(MessageDirection direction, double now, out string payload)
        {
            ValidTime(now); var lane = Get(direction); payload = null;
            if (now < pausedUntil || lane.Queue.Count == 0 || lane.Queue.Peek().Due > now + 1e-10) return false;
            payload = lane.Queue.Dequeue().Payload; return true;
        }
        public void Pause(double now, double seconds)
        { ValidTime(now); if (seconds < 0 || seconds > 10 || double.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds)); pausedUntil = Math.Max(pausedUntil, now + seconds); }
        public void ClearPending()
        { send.Queue.Clear(); receive.Queue.Clear(); send.LastDue = receive.LastDue = 0; }
        public ConditioningSummary Summary => new ConditioningSummary { PendingSend = send.Queue.Count, PendingReceive = receive.Queue.Count,
            DroppedSend = send.Dropped, DroppedReceive = receive.Dropped, PausedUntil = pausedUntil };
    }
}
