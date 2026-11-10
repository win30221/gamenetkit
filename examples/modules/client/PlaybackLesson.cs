using System;
using Realtime.Playback;

namespace Realtime.Examples.Modules
{
    public static class PlaybackLesson
    {
        public static void Main()
        {
            var clock = new PlaybackClock();
            clock.Load(new long[] {0, 30, 60}, 30); // Application owns frame payloads.
            clock.Play(); clock.Advance(.5); Check.Near(clock.Tick, 15);
            clock.Pause(); clock.Advance(1); Check.Near(clock.Tick, 15);
            clock.Seek(30); Check.That(clock.FrameIndex==1, "seek index");
            clock.Speed=2; clock.Play(); clock.Advance(.25); Check.Near(clock.Tick, 45);
            Console.WriteLine("playback: live timeline untouched; play=15, paused=15, seek=30, then 2x for .25s -> 45");
            clock.Clear(); Check.That(clock.FrameIndex==-1 && !clock.Playing, "exit playback");
        }
    }
}
