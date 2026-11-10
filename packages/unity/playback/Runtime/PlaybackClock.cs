using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Realtime.Playback
{
    public sealed class PlaybackClock : IPlaybackClock
    {
        private long[] ticks = Array.Empty<long>();
        private int rate;
        private double speed = 1;
        public bool Playing { get; private set; }
        public bool Ended => ticks.Length > 0 && Tick >= LastTick;
        public double Tick { get; private set; }
        public double Speed { get => speed; set { if (!Finite(value) || value < .25 || value > 4) throw new ArgumentOutOfRangeException(nameof(value)); speed = value; } }
        public long FirstTick => ticks.Length == 0 ? 0 : ticks[0];
        public long LastTick => ticks.Length == 0 ? 0 : ticks[ticks.Length - 1];
        public int FrameIndex { get; private set; } = -1;
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public void Load(IReadOnlyList<long> source, int tickRate)
        {
            if (source == null || source.Count == 0 || source.Count > 120000 || tickRate < 1 || tickRate > 240) throw new ArgumentException("Invalid playback definition");
            var copy = new long[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                long tick = source[i];
                if (tick < 0 || tick > 1000000000000L || i > 0 && tick <= copy[i - 1]) throw new ArgumentException("Playback ticks must strictly increase");
                copy[i] = tick;
            }
            if (copy[copy.Length - 1] - copy[0] > tickRate * 3600L) throw new ArgumentException("Playback exceeds one hour");
            ticks = copy; rate = tickRate; Playing = false; speed = 1; Seek(FirstTick);
        }
        public void Play() { if (ticks.Length == 0) return; if (Ended) Seek(FirstTick); Playing = ticks.Length > 1; }
        public void Pause() => Playing = false;
        public void Seek(double tick)
        {
            if (!Finite(tick)) throw new ArgumentOutOfRangeException(nameof(tick));
            if (ticks.Length == 0) return;
            Tick = Math.Max(FirstTick, Math.Min(LastTick, tick));
            int low = 0, high = ticks.Length - 1;
            while (low < high) { int middle = low + (high - low + 1) / 2; if (ticks[middle] <= Tick) low = middle; else high = middle - 1; }
            FrameIndex = low; if (Ended) Playing = false;
        }
        public void Advance(double unscaledSeconds)
        {
            if (!Finite(unscaledSeconds) || unscaledSeconds < 0) throw new ArgumentOutOfRangeException(nameof(unscaledSeconds));
            if (Playing) Seek(Tick + Math.Min(unscaledSeconds, 3600) * rate * speed);
        }
        public void Clear() { ticks = Array.Empty<long>(); rate = 0; Tick = 0; FrameIndex = -1; Playing = false; speed = 1; }
    }
}
