using System;
using Realtime.Core;

namespace Realtime.Prediction
{
    // The committed fixed-step state and the fractional render preview own
    // distinct model buffers. Preview physics never changes the next real tick.
    public sealed class ReplayPrediction<TState, TInput> : IPrediction<TState, TInput> where TState : class
    {
        private readonly IPredictionModel<TState, TInput> model;
        private readonly InputHistory<TInput> history = new InputHistory<TInput>();
        private readonly TState simulation, rendered;
        private readonly double rate, timeout, interval;
        private readonly bool fixedStep;
        private bool initialized;
        private double lastFrame = double.NaN, lastSnapshot, authorityTick, simulationTime, simulationTick;
        private TInput lastInput;
        private PredictionMetrics metrics;
        public string Mode => fixedStep ? "fixed-step" : "frame";
        public bool Enabled => true;
        public TState State => rendered;
        public PredictionMetrics Metrics => metrics;
        public ReplayPrediction(IPredictionModel<TState, TInput> model, int tickRate, double timeoutSeconds, bool fixedStep)
        {
            if (tickRate <= 0 || timeoutSeconds <= 0 || double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds)) throw new ArgumentOutOfRangeException();
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            rate = tickRate; interval = 1.0 / tickRate; timeout = timeoutSeconds; this.fixedStep = fixedStep;
            simulation = model.CreateState(); rendered = model.CreateState();
            if (simulation == null || rendered == null || ReferenceEquals(simulation, rendered))
                throw new ArgumentException("Prediction model must create independent mutable state buffers");
        }
        public void SentInput(long sequence, double now) { history.SentInput(sequence, now); }
        public void Reconcile(TState authority, PredictionAck ack, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
            bool replay = initialized && !ack.ResetHistory && history.TryAcknowledgedTime(ack.Sequence, out _);
            if (ack.ResetHistory)
            { history.Clear(); lastFrame = double.NaN; metrics = default; }
            model.Copy(authority, simulation);
            authorityTick = ack.ServerTick; simulationTick = ack.ServerTick; simulationTime = now;
            if (replay && history.TryAcknowledgedTime(ack.Sequence, out double sentTime))
            {
                double anchor = sentTime + Math.Max(0, ack.AppliedInputTicks) / rate;
                if (fixedStep)
                {
                    double start = Math.Min(now, anchor);
                    // HeldAt's fallback is for the next live input, not missing
                    // replay history. Clipped/trimmed/stalled intervals cannot
                    // be reconstructed safely: resume from this authority now.
                    // Include the tail through now so the next Advance cannot
                    // silently fill a gap after the last retained input frame.
                    if (HasReplayCoverage(start, now))
                    {
                        simulationTime = start;
                        IntegrateFixed(now, true);
                        Preview(now);
                    }
                    else
                    {
                        model.Copy(simulation, rendered);
                        // Discarded history ends at this authority arrival. The
                        // next live Advance must not count that old tail again.
                        lastFrame = now;
                    }
                }
                else
                {
                    for (int i = 0; i < history.Frames.Count; i++)
                    {
                        var frame = history.Frames[i];
                        double seconds = frame.End - Math.Max(anchor, frame.Start);
                        if (seconds <= 0) continue;
                        model.Step(simulation, frame.Input, seconds, ack.ServerTick + Math.Max(0, frame.Start - anchor) * rate);
                        metrics.ReplayedSteps++;
                    }
                    model.Copy(simulation, rendered);
                }
            }
            else model.Copy(simulation, rendered);
            history.DiscardBefore(ack.Sequence);
            lastSnapshot = now; initialized = true; metrics.CommittedTick = simulationTick;
        }
        private bool HasReplayCoverage(double start, double end)
        {
            const double epsilon = 1e-10;
            double covered = start;
            if (end <= covered + epsilon) return true;
            for (int i = 0; i < history.Frames.Count; i++)
            {
                var frame = history.Frames[i];
                if (frame.End <= covered + epsilon) continue;
                if (frame.Start > covered + epsilon) return false;
                covered = frame.End;
                if (covered >= end - epsilon) return true;
            }
            return false;
        }
        public void Advance(double now, TInput input)
        {
            if (!initialized || double.IsNaN(now) || double.IsInfinity(now)) return;
            double elapsed = double.IsNaN(lastFrame) ? 0 : Math.Max(0, now - lastFrame);
            double dt = Math.Min(.05, elapsed); lastFrame = now; lastInput = input;
            if (now - lastSnapshot <= timeout)
            {
                history.Record(now - dt, now, input);
                if (fixedStep)
                {
                    if (elapsed > dt)
                    {
                        simulationTime += elapsed - dt; simulationTick += (elapsed - dt) * rate;
                        metrics.DroppedSeconds += elapsed - dt;
                    }
                    IntegrateFixed(now, false); Preview(now);
                }
                else
                {
                    model.Step(simulation, input, dt, authorityTick + Math.Max(0, now - dt - lastSnapshot) * rate);
                    metrics.CommittedSteps++; simulationTick = authorityTick + Math.Max(0, now - lastSnapshot) * rate;
                    model.Copy(simulation, rendered);
                }
            }
            history.Trim(now); metrics.CommittedTick = simulationTick;
        }
        private void IntegrateFixed(double end, bool replay)
        {
            // At most the retained one-second history, plus rounding slack.
            int budget = (int)Math.Ceiling(rate) + 2;
            int cursor = 0;
            while (end - simulationTime >= interval - 1e-10 && budget-- > 0)
            {
                var input = history.HeldAt(simulationTime, lastInput, ref cursor);
                model.Step(simulation, input, interval, simulationTick);
                simulationTime += interval; simulationTick++;
                if (replay) metrics.ReplayedSteps++; else metrics.CommittedSteps++;
            }
            if (end - simulationTime >= interval)
            { metrics.DroppedSeconds += end - simulationTime; simulationTime = end; }
        }
        private void Preview(double now)
        {
            model.Copy(simulation, rendered);
            double tail = Math.Max(0, Math.Min(interval, now - simulationTime));
            if (tail <= 1e-10) return;
            model.Step(rendered, history.HeldAt(simulationTime, lastInput), tail, simulationTick);
            metrics.PreviewSteps++;
        }
    }
}
