using System;
using Realtime.Core;
using Realtime.Interpolation;

namespace Realtime.Examples.Modules
{
    public static class InterpolationLesson
    {
        public static void Main()
        {
            var catalog = new TimelineCatalog(); // core already registers "latest".
            catalog.Register("fixed", (rate, snapshots) => new FixedTimeline(rate, snapshots, intervals: 1));
            catalog.Register("adaptive", (rate, snapshots) => new SnapshotInterpolation(rate, snapshots));
            var latest = catalog.Create("latest", 30, 15);
            var smooth = catalog.Create("fixed", 30, 15);
            foreach (var timeline in new[] { latest, smooth }) {
                timeline.Reset(0, 0);        // Welcome establishes the baseline.
                timeline.Observe(4, 4.0/30); // Receive snapshot tick 4, position 4.
                timeline.Advance(5.0/30);    // Update at the next display frame.
            }
            // Application-owned samples: (tick=0,x=0), (tick=4,x=4).
            // A timeline does not store DTOs or move Transforms.
            double position = Math.Clamp(smooth.RenderTick / 4, 0, 1) * 4;
            Check.Near(latest.RenderTick, 4); Check.Near(position, 3);
            Console.WriteLine($"interpolation: latest x={latest.RenderTick:F1}, fixed sampled x={position:F1}; adaptive is another registered choice");
            smooth.Reset(30, 1); // On reconnect also clear the application's samples.
            Check.Near(smooth.RenderTick, 28);
        }
    }
}
