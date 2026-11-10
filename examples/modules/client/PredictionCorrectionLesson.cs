using System;
using Realtime.Core;
using Realtime.Prediction;
using Realtime.Correction;

namespace Realtime.Examples.Modules
{
    public static class PredictionCorrectionLesson
    {
        private sealed class State { public double X; }
        private sealed class Motion : IPredictionModel<State, double>
        {
            public State CreateState() => new State();
            public void Copy(State from, State to) => to.X = from.X;
            public void Step(State state, double input, double seconds, double tick) => state.X += input * 4 * seconds;
        }
        public static void Main()
        {
            var model = new Motion(); // Application rules, never a Unity Transform.
            IPrediction<State, double> disabled = new AuthorityPrediction<State, double>(model);
            IPrediction<State, double> predicted = new ReplayPrediction<State, double>(model, 30, .2, fixedStep: true);
            foreach (var mode in new[] { disabled, predicted }) {
                mode.Reconcile(new State(), new PredictionAck {ResetHistory=true, ServerTick=0}, 0);
                mode.SentInput(1, 0); // Actual send time, not merely the button press.
                for (int i=0; i<=3; i++) mode.Advance(i/60.0, 1);
            }
            Check.Near(disabled.State.X, 0); Check.Near(predicted.State.X, .2);
            double displayed = predicted.State.X;
            predicted.Reconcile(new State {X=.1}, new PredictionAck {Sequence=1, ServerTick=1, AppliedInputTicks=1}, .05);
            Check.That(predicted.State.X > .1 && predicted.State.X < displayed, "unacknowledged input should replay");
            // Authority changes immediately. Only the presentation offset decays.
            ICorrectionPolicy smooth = new ExponentialCorrection();
            ICorrectionPolicy snap = new SnapCorrection();
            double offset = displayed - predicted.State.X;
            Check.That(smooth.PreserveOffset(Math.Abs(offset), false) && !snap.PreserveOffset(Math.Abs(offset), false), "correction selection");
            double after = offset * smooth.Decay(.08);
            Check.That(after > 0 && after < offset && !smooth.PreserveOffset(3, false), "offset decay and large correction");
            Console.WriteLine($"prediction-correction: authority=0.000, predicted={displayed:F3}, reconciled={predicted.State.X:F3}; display offset {offset:F3} -> {after:F3}");
            predicted.Reconcile(new State {X=5}, new PredictionAck {ResetHistory=true, ServerTick=30}, 1);
            Check.Near(predicted.State.X, 5); // New life: also discard the visual offset.
        }
    }
}
