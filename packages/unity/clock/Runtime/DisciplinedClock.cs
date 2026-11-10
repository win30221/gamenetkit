using System;
using Realtime.Core;

namespace Realtime.Clock
{
    // RTT midpoint is an estimate, not proof of equal uplink/downlink delay.
    public sealed class DisciplinedClock : IServerClock
    {
        private readonly double rate;
        private bool ready;
        private double offset, last, observation, latestServer;
        public string Mode => "disciplined";
        public DisciplinedClock(int tickRate) { if (tickRate < 1) throw new ArgumentOutOfRangeException(nameof(tickRate)); rate = tickRate; }
        public void Reset() { ready = false; offset = last = observation = latestServer = 0; }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public void Observe(double serverTick, double now, double roundTripSeconds)
        {
            if (!Finite(serverTick) || !Finite(now) || !Finite(roundTripSeconds) || serverTick < latestServer || roundTripSeconds < 0 || roundTripSeconds > 5) return;
            double sample = serverTick + Math.Min(.5, roundTripSeconds * .5) * rate - now * rate;
            if (!ready || now - observation > 2) offset = sample;
            else offset += Math.Clamp(sample - offset, -rate * .1, rate * .1) * .2;
            latestServer = serverTick; observation = now; ready = true;
        }
        public double Tick(double now)
        {
            if (!ready || !Finite(now)) return last;
            // Bound extrapolation during a stalled simulation or connection.
            double candidate = Math.Max(latestServer, Math.Min(latestServer + rate * .5, now * rate + offset));
            last = Math.Max(last, candidate); return last;
        }
    }
}
