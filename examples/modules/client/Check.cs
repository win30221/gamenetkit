using System;

namespace Realtime.Examples.Modules
{
    internal static class Check
    {
        public static void That(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        public static void Near(double actual, double expected)
        { That(Math.Abs(actual - expected) < 1e-6, $"Expected {expected}, got {actual}"); }
    }
}
