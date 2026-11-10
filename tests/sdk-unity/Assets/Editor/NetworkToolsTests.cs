using System;
using Realtime.Core;
using Realtime.Diagnostics;
using Realtime.Simulation;

public static class NetworkToolsTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception("Network tools: " + message); }
    private static void Equal(double a, double b) { Require(Math.Abs(a - b) < 1e-8, a + " != " + b); }
    public static void Run()
    {
        var meter = new ClientDiagnostics();
        for (int i = 1; i <= 20; i++) meter.Timing(ClientMetric.ConnectionRttMS, i, 1);
        meter.Timing(ClientMetric.ConnectionRttMS, double.NaN, 1); meter.Timing(ClientMetric.ConnectionRttMS, -1, 1);
        var summary = meter.Read(ClientMetric.ConnectionRttMS, 1);
        Require(summary.Count == 20 && summary.Min == 1 && summary.Max == 20 && summary.P95 == 19, "quantiles or invalid samples"); Equal(summary.Mean, 10.5); Equal(summary.Median, 10.5);
        Equal(meter.Read(ClientMetric.ConnectionRttMS, 2).Mean, 10.5);
        meter.Timing(ClientMetric.ConnectionRttMS, 100, 1.5);
        Require(meter.Read(ClientMetric.ConnectionRttMS, 2).Max == 100, "cached metric missed new sample");
        Require(meter.Read(ClientMetric.ConnectionRttMS, 11.25).Count == 1, "cached metric missed expiration");
        for (int i = 1; i <= 1000; i++) meter.Timing(ClientMetric.InputReplyMS, i, 1);
        summary = meter.Read(ClientMetric.InputReplyMS, 1); Require(summary.Count == 512 && summary.Min == 489 && summary.Max == 1000, "bounded samples");
        Require(meter.Read(ClientMetric.ConnectionRttMS, 12).Count == 0, "expired timing samples remain");
        meter.Traffic(true, 300, 1); meter.Traffic(false, 500, 1.5);
        var traffic = meter.ReadTraffic(1.9); Require(traffic.SentBytesPerSecond == 300 && traffic.ReceivedBytesPerSecond == 500, "rolling traffic");
        traffic = meter.ReadTraffic(2.1); Require(traffic.SentBytesPerSecond == 0 && traffic.ReceivedBytesPerSecond == 500 && traffic.SentBytes == 300, "traffic expiration versus totals");
        meter.Reset(); meter.Frame(0); for (int i = 1; i <= 60; i++) meter.Frame(i / 60.0);
        Equal(meter.Read(ClientMetric.FrameMS, 1).Mean, 1000.0 / 60);
        meter.Reset(); Require(meter.ReadTraffic(2).SentBytes == 0 && meter.Read(ClientMetric.FrameMS, 2).Count == 0, "connection reset");
        IClientDiagnostics absent = new NoClientDiagnostics(); absent.Frame(1); absent.Traffic(true, 10, 1); Require(!absent.Enabled && absent.ReadTraffic(1).SentBytes == 0, "missing diagnostics fallback");

        var config = new ConditioningSettings { DelaySeconds = .05, Seed = 9 };
        var condition = new MessageConditioner(config);
        condition.Enqueue(MessageDirection.Send, "input", true, 0); condition.Enqueue(MessageDirection.Receive, "snapshot", true, 0);
        Require(!condition.TryTake(MessageDirection.Send, .049, out _) && !condition.TryTake(MessageDirection.Receive, .049, out _), "early release");
        Require(condition.TryTake(MessageDirection.Send, .05, out var payload) && payload == "input", "outbound delay");
        Require(condition.TryTake(MessageDirection.Receive, .05, out payload) && payload == "snapshot", "inbound delay");
        config.LossProbability = 1; condition = new MessageConditioner(config);
        condition.Enqueue(MessageDirection.Send, "input", true, 0); condition.Enqueue(MessageDirection.Receive, "snapshot", true, 0);
        condition.Enqueue(MessageDirection.Receive, "welcome", false, 0);
        Require(condition.Summary.DroppedSend == 1 && condition.Summary.DroppedReceive == 1 && condition.TryTake(MessageDirection.Receive, .1, out payload) && payload == "welcome", "selective loss harmed control messages");
        Require(!condition.TryTake(MessageDirection.Send, .1, out _), "dropped input delivered");
        config.JitterSeconds = .02; config.LossProbability = .15;
        var a = new MessageConditioner(config); var b = new MessageConditioner(config);
        for (int i = 0; i < 100; i++) { a.Enqueue(MessageDirection.Receive, i.ToString(), true, i * .01); b.Enqueue(MessageDirection.Receive, i.ToString(), true, i * .01); }
        int previous = -1, delivered = 0;
        for (int i = 0; i <= 1200; i++)
        {
            bool left = a.TryTake(MessageDirection.Receive, i / 1000.0, out var x), right = b.TryTake(MessageDirection.Receive, i / 1000.0, out var y);
            Require(left == right && x == y, "seeded trace differs");
            if (left) { Require(int.Parse(x) > previous, "FIFO order changed"); previous = int.Parse(x); delivered++; }
        }
        Require(delivered > 0 && a.Summary.DroppedReceive > 0 && delivered + a.Summary.DroppedReceive == 100, "loss accounting");
        condition = new MessageConditioner(new ConditioningSettings(), 2);
        Require(condition.Enqueue(MessageDirection.Send, "1", false, 0) && condition.Enqueue(MessageDirection.Send, "2", false, 0) && !condition.Enqueue(MessageDirection.Send, "3", false, 0), "queue capacity");
        condition.ClearPending();
        Require(condition.Enqueue(MessageDirection.Send, new string('x', 32768), false, 0), "exact wire boundary rejected");
        condition.ClearPending(); Require(!condition.Enqueue(MessageDirection.Send, new string('x', 32769), false, 0), "payload memory bound");
        Require(!condition.Enqueue(MessageDirection.Send, new string('界', 10923), false, 0), "UTF-8 byte bound used character count");
        condition.Pause(0, 3); condition.Enqueue(MessageDirection.Receive, "old", false, 0); condition.ClearPending();
        condition.Enqueue(MessageDirection.Receive, "new", false, 1);
        Require(!condition.TryTake(MessageDirection.Receive, 2.9, out _) && condition.TryTake(MessageDirection.Receive, 3, out payload) && payload == "new", "pause or connection isolation");
        bool invalid = false; try { new MessageConditioner(new ConditioningSettings { DelaySeconds = double.NaN }); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Require(invalid, "invalid config accepted");
        UnityEngine.Debug.Log("RF_NETWORK_TOOLS_SDK PASS: timing quantiles, bounds/expiry/reset, payload rates, frame samples, selective loss, both delay directions, seeded jitter/FIFO, payload/queue limits, pause and connection isolation");
    }
}
