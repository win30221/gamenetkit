using System;
using Realtime.Core;
using Realtime.Clock;

namespace Realtime.Examples.Modules
{
    public static class ClockLesson
    {
        public static void Main()
        {
            IServerClock baseline = new SnapshotClock();
            IServerClock estimated = new DisciplinedClock(30);
            baseline.Observe(30, 1, .1); estimated.Observe(30, 1, .1);
            Check.Near(baseline.Tick(1.1), 30); Check.Near(estimated.Tick(1.1), 34.5);
            Check.Near(estimated.Tick(10), 45); // Outage extrapolation is bounded.
            Console.WriteLine("clock: last snapshot=30, estimated at +100ms=34.5, long outage capped at 45");
            estimated.Reset(); Check.Near(estimated.Tick(0), 0);
        }
    }
}
