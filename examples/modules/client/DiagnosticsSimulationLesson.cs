using System;
using Realtime.Core;
using Realtime.Diagnostics;
using Realtime.Simulation;

namespace Realtime.Examples.Modules
{
    public static class DiagnosticsSimulationLesson
    {
        public static void Main()
        {
            IClientDiagnostics disabled = new NoClientDiagnostics(), metrics = new ClientDiagnostics();
            foreach (var monitor in new[] {disabled, metrics}) {
                monitor.Timing(ClientMetric.ConnectionRttMS, 80, 0);
                monitor.Timing(ClientMetric.ConnectionRttMS, 120, .1);
                monitor.Traffic(true, 40, .1);
            }
            Check.That(disabled.Read(ClientMetric.ConnectionRttMS, .1).Count==0, "disabled monitor");
            Check.Near(metrics.Read(ClientMetric.ConnectionRttMS, .1).Mean, 100);
            var network = new MessageConditioner(new ConditioningSettings {DelaySeconds=.1, LossProbability=1, Seed=7});
            // Mark only replaceable data discardable; never drop control implicitly.
            Check.That(network.Enqueue(MessageDirection.Receive, "snapshot", true, 0), "accept simulated drop");
            Check.That(network.Enqueue(MessageDirection.Receive, "welcome", false, 0), "queue control");
            Check.That(!network.TryTake(MessageDirection.Receive, .05, out _), "delay not reached");
            Check.That(network.TryTake(MessageDirection.Receive, .1, out var text) && text=="welcome", "control delivery");
            Check.That(network.Summary.DroppedReceive==1, "replaceable drop");
            Console.WriteLine("diagnostics-simulation: metrics off=0 samples, on RTT mean=100ms; direct delivery -> 100ms control delay, replaceable snapshot dropped");
            network.ClearPending(); metrics.Reset(); // Reconnect clears pending messages/metrics.
            Check.That(metrics.ReadTraffic(.2).SentBytes==0 && network.Summary.PendingReceive==0, "reset");
        }
    }
}
