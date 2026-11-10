using System;
using Realtime.Core;

namespace Realtime.Correction
{
    public sealed class ExponentialCorrection : ICorrectionPolicy
    {
        private readonly double seconds, snapDistance;
        public string Mode => "exponential";
        public bool Smooths => true;
        public ExponentialCorrection(double seconds = .08, double snapDistance = 2)
        { if (seconds <= 0 || snapDistance <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || double.IsNaN(snapDistance) || double.IsInfinity(snapDistance)) throw new ArgumentOutOfRangeException(); this.seconds = seconds; this.snapDistance = snapDistance; }
        public bool PreserveOffset(double errorDistance, bool forceSnap)
        { return !forceSnap && errorDistance >= 0 && errorDistance < snapDistance; }
        public double Decay(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
            return Math.Exp(-Math.Max(0, elapsed) / seconds);
        }
    }
}
