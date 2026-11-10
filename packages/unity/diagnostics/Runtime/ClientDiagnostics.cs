using System;
using Realtime.Core;

namespace Realtime.Diagnostics
{
    public sealed class ClientDiagnostics : IClientDiagnostics
    {
        private sealed class Window
        {
            private const int Capacity = 512;
            private readonly double[] values = new double[Capacity], times = new double[Capacity], sorted = new double[Capacity];
            private int start, count;
            private bool dirty = true;
            private MetricSummary cached;
            public void Clear() { start = count = 0; dirty = true; cached = default; }
            public void Add(double value, double now)
            {
                Trim(now);
                dirty = true;
                if (count == Capacity) { start = (start + 1) % Capacity; count--; }
                int index = (start + count++) % Capacity; values[index] = value; times[index] = now;
            }
            private void Trim(double now)
            { while (count > 0 && times[start] < now - 10) { start = (start + 1) % Capacity; count--; dirty = true; } }
            public MetricSummary Read(double now)
            {
                Trim(now); if (count == 0) return default;
                if (!dirty) return cached;
                double sum = 0;
                for (int i = 0; i < count; i++) { double value = values[(start + i) % Capacity]; sorted[i] = value; sum += value; }
                Array.Sort(sorted, 0, count);
                cached = new MetricSummary { Count = count, Mean = sum / count, Min = sorted[0], Max = sorted[count - 1],
                    Median = count % 2 == 0 ? (sorted[count / 2 - 1] + sorted[count / 2]) * .5 : sorted[count / 2],
                    P95 = sorted[(int)Math.Ceiling(count * .95) - 1] };
                dirty = false;
                return cached;
            }
        }
        private struct Payload { public double Time; public int Bytes; public bool Sent; }
        private readonly Window[] windows;
        private readonly Payload[] traffic = new Payload[2048];
        private int trafficStart, trafficCount;
        private TrafficSummary totals;
        private double lastFrame = double.NaN;
        public bool Enabled => true;
        public ClientDiagnostics()
        { windows = new Window[Enum.GetValues(typeof(ClientMetric)).Length]; for (int i = 0; i < windows.Length; i++) windows[i] = new Window(); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public void Reset()
        { foreach (var window in windows) window.Clear(); trafficStart = trafficCount = 0; totals = default; lastFrame = double.NaN; }
        public void Frame(double now)
        {
            if (!Finite(now) || (!double.IsNaN(lastFrame) && now < lastFrame)) return;
            if (!double.IsNaN(lastFrame) && now > lastFrame) Timing(ClientMetric.FrameMS, (now - lastFrame) * 1000, now);
            lastFrame = now;
        }
        public void Timing(ClientMetric metric, double value, double now)
        { if (Finite(value) && Finite(now) && value >= 0) windows[(int)metric].Add(value, now); }
        public MetricSummary Read(ClientMetric metric, double now) => windows[(int)metric].Read(now);
        public void Traffic(bool sent, int payloadBytes, double now)
        {
            if (payloadBytes < 0 || !Finite(now)) return;
            TrimTraffic(now);
            if (trafficCount == traffic.Length) { trafficStart = (trafficStart + 1) % traffic.Length; trafficCount--; }
            traffic[(trafficStart + trafficCount++) % traffic.Length] = new Payload { Time = now, Bytes = payloadBytes, Sent = sent };
            if (sent) { totals.SentBytes += payloadBytes; totals.SentMessages++; }
            else { totals.ReceivedBytes += payloadBytes; totals.ReceivedMessages++; }
        }
        private void TrimTraffic(double now)
        { while (trafficCount > 0 && traffic[trafficStart].Time <= now - 1) { trafficStart = (trafficStart + 1) % traffic.Length; trafficCount--; } }
        public TrafficSummary ReadTraffic(double now)
        {
            TrimTraffic(now); var result = totals;
            for (int i = 0; i < trafficCount; i++) { var item = traffic[(trafficStart + i) % traffic.Length];
                if (item.Sent) result.SentBytesPerSecond += item.Bytes; else result.ReceivedBytesPerSecond += item.Bytes; }
            return result;
        }
    }
}
