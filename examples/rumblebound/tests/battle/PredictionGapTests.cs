using System;
using Realtime.Core;
using Realtime.Prediction;

public static class PredictionGapTests
{
    private sealed class State { public double X; }
    private sealed class Model : IPredictionModel<State, double>
    {
        public State CreateState() => new State();
        public void Copy(State source, State target) { target.X = source.X; }
        public void Step(State state, double input, double seconds, double tick) { state.X += 6 * input * seconds; }
    }
    private static void Require(bool value, string reason) { if (!value) throw new Exception("Prediction gap: " + reason); }
    private static void Near(double actual, double expected, string reason) { Require(Math.Abs(actual - expected) < 1e-9, reason + " (" + actual + " != " + expected + ")"); }
    private static ReplayPrediction<State, double> Start()
    {
        var prediction = new ReplayPrediction<State, double>(new Model(), 30, 5, true);
        prediction.Reconcile(new State(), new PredictionAck { ResetHistory = true }, 0);
        prediction.SentInput(1, 0); prediction.Advance(0, 1);
        return prediction;
    }
    private static PredictionAck Ack(double applied = 2) => new PredictionAck { Sequence = 1, ServerTick = 2, AppliedInputTicks = applied };
    public static void Run()
    {
        var prediction = Start();
        prediction.Advance(.15, 1);
        Near(prediction.State.X, .3, "long frame must simulate at most 50ms");
        Near(prediction.Metrics.DroppedSeconds, .1, "clipped frame drop accounting");
        prediction.Reconcile(new State { X = 1 }, Ack(), .15);
        Near(prediction.State.X, 1, "replay resurrected a clipped frame interval");
        Require(prediction.Metrics.ReplayedSteps == 0, "incomplete history was partially simulated");
        prediction.Advance(.15, 1);
        Near(prediction.State.X, 1, "same-time Advance filled the missing interval");
        prediction.Advance(.15 + 1.0 / 60, 1);
        Near(prediction.State.X, 1.1, "new post-authority fractional prediction did not resume");

        prediction = Start();
        prediction.Advance(.02, 1); prediction.Advance(.12, 1); prediction.Advance(.14, 1);
        prediction.Reconcile(new State { X = 2 }, Ack(0), .14);
        Near(prediction.State.X, 2, "replay bridged an internal history gap");
        prediction.Advance(.14 + 1.0 / 60, 1);
        Near(prediction.State.X, 2.1, "internal gap fallback left an old replay clock");

        prediction = Start();
        prediction.Advance(1.0 / 30, 1);
        prediction.Reconcile(new State { X = 3 }, Ack(1), .1);
        Near(prediction.State.X, 3, "unrecorded replay tail was extrapolated");
        prediction.Advance(.1, 1);
        Near(prediction.State.X, 3, "next live Advance filled an unrecorded replay tail");
        prediction.Advance(.1 + 1.0 / 60, 1);
        Near(prediction.State.X, 3.1, "tail fallback delayed the first post-authority preview");
        prediction.Advance(.1 + 1.0 / 30, 1);
        Near(prediction.State.X, 3.2, "tail fallback skipped the next committed step");

        prediction = Start();
        for (int frame = 1; frame <= 90; frame++) prediction.Advance(frame / 60.0, 1);
        prediction.Reconcile(new State { X = 4 }, Ack(), 1.5);
        Near(prediction.State.X, 4, "trimmed history was invented from held-input fallback");

        prediction = Start();
        for (int frame = 1; frame <= 12; frame++) prediction.Advance(frame / 60.0, 1);
        Near(prediction.State.X, 1.2, "ordinary fixed-step simulation changed");
        Require(prediction.Metrics.CommittedSteps == 6, "30Hz committed step count changed");
        var authority = new State { X = .4 };
        prediction.Reconcile(authority, Ack(), .2);
        Near(prediction.State.X, 1.2, "contiguous acknowledged history did not replay");
        Near(authority.X, .4, "replay mutated authority");
        Require(prediction.Metrics.ReplayedSteps == 4, "contiguous replay step count changed");
        prediction.Advance(.2 + 1.0 / 60, 1);
        Near(prediction.State.X, 1.3, "fractional render preview changed");
        Require(prediction.Metrics.CommittedSteps == 6, "preview committed an extra simulation step");
        prediction.Advance(.2 + 1.0 / 30, 1);
        Near(prediction.State.X, 1.4, "preview corrupted the next committed step");
        Require(prediction.Metrics.CommittedSteps == 7, "next 30Hz simulation step was lost");

        prediction.Reconcile(new State { X = 5 }, new PredictionAck { ResetHistory = true, ServerTick = 10 }, .3);
        prediction.Advance(.3, 1); prediction.Advance(.3 + 1.0 / 60, 1);
        Near(prediction.State.X, 5.1, "reset retained previous history or abandoned replay time");
        Require(prediction.Metrics.ReplayedSteps == 0, "reset retained replay counters");
        Console.WriteLine("REMAKE_PREDICTION_GAPS PASS clipped/internal/trimmed/tail gap rejection, authority fallback, contiguous replay, 30Hz preview isolation and reset");
    }
}
