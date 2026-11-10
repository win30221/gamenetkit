using System;
using System.Collections.Generic;
using Realtime.Core;
using Realtime.Clock;
using Realtime.Diagnostics;

namespace Realtime.Examples.NetworkRecovery
{
    public sealed class SessionTelemetry
    {
        private readonly SnapshotClock snapshot=new SnapshotClock();
        private readonly DisciplinedClock disciplined=new DisciplinedClock(30);
        private readonly Dictionary<long,double> probes=new Dictionary<long,double>(),inputs=new Dictionary<long,double>();
        private readonly Queue<long> inputOrder=new Queue<long>();
        private double lastSnapshot=double.NaN;
        public IClientDiagnostics Metrics {get;private set;}=new ClientDiagnostics();
        public string ClockMode {get;private set;}="disciplined";
        public IServerClock Clock=>ClockMode=="disciplined"?(IServerClock)disciplined:snapshot;
        public double SnapshotAge(double now)=>double.IsNaN(lastSnapshot)?0:Math.Max(0,now-lastSnapshot);
        public void SelectClock(string mode)
        {if(mode!="snapshot"&&mode!="disciplined")throw new ArgumentException("clock");ClockMode=mode;}
        public void SetEnabled(bool enabled){Metrics=enabled?(IClientDiagnostics)new ClientDiagnostics():new NoClientDiagnostics();}
        public void Reset()
        {probes.Clear();inputs.Clear();inputOrder.Clear();snapshot.Reset();disciplined.Reset();Metrics.Reset();lastSnapshot=double.NaN;}
        public void Probe(long id,double now)
        {
            foreach(var key in new List<long>(probes.Keys))if(now-probes[key]>5)probes.Remove(key);
            if(probes.Count<16)probes[id]=now;
        }
        public void Pong(ServerMessage pong,double now)
        {
            if(pong.protocol!=1 || pong.id<=0 || pong.tick<0)throw new InvalidOperationException("invalid pong");
            if(!probes.TryGetValue(pong.id,out double sent))return;
            probes.Remove(pong.id);double rtt=now-sent;
            if(rtt<0 || rtt>5)return;
            Metrics.Timing(ClientMetric.ConnectionRttMS,rtt*1000,now);
            snapshot.Observe(pong.tick,now,rtt);disciplined.Observe(pong.tick,now,rtt);
        }
        public void Input(long id,double now)
        {
            while(inputOrder.Count>=128)inputs.Remove(inputOrder.Dequeue());
            inputs[id]=now;inputOrder.Enqueue(id);
        }
        public void Snapshot(long tick,long ack,double error,double now)
        {
            if(double.IsNaN(lastSnapshot))disciplined.Observe(tick,now,0); // Welcome baseline until the first matched probe.
            if(!double.IsNaN(lastSnapshot))Metrics.Timing(ClientMetric.SnapshotIntervalMS,(now-lastSnapshot)*1000,now);
            lastSnapshot=now;snapshot.Observe(tick,now,0);
            Metrics.Timing(ClientMetric.CorrectionDistance,error,now);
            if(inputs.TryGetValue(ack,out double sent))Metrics.Timing(ClientMetric.InputReplyMS,(now-sent)*1000,now);
            while(inputOrder.Count>0 && inputOrder.Peek()<=ack)inputs.Remove(inputOrder.Dequeue());
        }
    }
}
