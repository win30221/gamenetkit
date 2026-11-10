using System;
using System.Collections.Generic;
using Realtime.Core;
using Realtime.Prediction;
using Realtime.Correction;
using UnityEngine;

public static class SdkTests
{
    private sealed class State { public double X; public double[] Extra = new double[1]; }
    private sealed class Model : IPredictionModel<State, double>
    {
        public State Committed;
        public readonly List<double> Durations = new List<double>();
        public State CreateState() { var state = new State(); if (Committed == null) Committed = state; return state; }
        public void Copy(State from, State to) { to.X = from.X; to.Extra[0] = from.Extra[0]; }
        public void Step(State state, double input, double seconds, double tick)
        {
            if (ReferenceEquals(state, Committed) && seconds > 1e-10) Durations.Add(seconds);
            state.X += input * seconds; state.Extra[0] += seconds;
        }
    }
    public static void Run()
    {
        OptimizedContracts();
        NetworkToolsTests.Run();
        SevenModulesTests.Run();
        PlaybackTests.Run();
        int cases = 0;
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (bool fixedStep in new[] { false, true })
        {
            var model = new Model(); var predictor = new ReplayPrediction<State, double>(model, 30, 10, fixedStep);
            var authority = new State(); predictor.Reconcile(authority, new PredictionAck { ResetHistory = true }, 0);
            predictor.SentInput(1, 0); predictor.Advance(0, 1);
            for (int frame = 1; frame <= fps; frame++) predictor.Advance((double)frame / fps, 1);
            Equal(predictor.State.X, 1); Equal(authority.X, 0); Equal(authority.Extra[0], 0);
            if (fixedStep)
            {
                Require(predictor.Metrics.CommittedSteps == 30, "fixed tick count depends on render FPS");
                foreach (double dt in model.Durations) Equal(dt, 1.0 / 30);
            }
            authority.X = .4; authority.Extra[0] = .4;
            predictor.Reconcile(authority, new PredictionAck { Sequence = 1, ServerTick = 12, AppliedInputTicks = 12 }, 1);
            Equal(predictor.State.X, 1); Equal(authority.X, .4);
            Require(predictor.Metrics.ReplayedSteps > 0, "ack replay was skipped");
            var changing = new ReplayPrediction<State, double>(new Model(), 30, 10, fixedStep);
            changing.Reconcile(new State(), new PredictionAck { ResetHistory = true }, 0);
            changing.SentInput(1, 0); changing.Advance(0, 1);
            for (int frame = 1; frame <= fps; frame++)
                changing.Advance((double)frame / fps, frame <= fps / 2 ? 1 : -1);
            Equal(changing.State.X, 0);
            changing.Reconcile(new State { X = .4 }, new PredictionAck { Sequence = 1, ServerTick = 12, AppliedInputTicks = 12 }, 1);
            Equal(changing.State.X, 0);
            authority.X = -4; predictor.Reconcile(authority, new PredictionAck { ServerTick = 3000, ResetHistory = true }, 100);
            predictor.Advance(100, 1); Equal(predictor.State.X, -4);
            cases++;
        }
        var previewModel = new Model(); var preview = new ReplayPrediction<State, double>(previewModel, 30, .1, true);
        preview.Reconcile(new State(), new PredictionAck { ResetHistory = true }, 0); preview.Advance(0, 1);
        preview.Advance(.01, 1); Equal(preview.State.X, .01); Equal(previewModel.Committed.X, 0);
        preview.Advance(1.0 / 30, 1); Equal(previewModel.Committed.X, 1.0 / 30);
        double frozen = preview.State.X; preview.Advance(1, 1); preview.Advance(100, 1); Equal(preview.State.X, frozen);
        var disabledModel = new Model(); var disabled = new AuthorityPrediction<State, double>(disabledModel);
        disabled.Reconcile(new State { X = 5 }, new PredictionAck { ServerTick = 10 }, 0); disabled.Advance(1, 1);
        Equal(disabled.State.X, 5); Require(disabledModel.Durations.Count == 0, "disabled prediction simulated input");
        var smooth = new ExponentialCorrection(); var snap = new SnapCorrection();
        Require(smooth.PreserveOffset(.1, false) && !smooth.PreserveOffset(3, false) && !smooth.PreserveOffset(.1, true), "correction gates");
        Equal(Math.Pow(smooth.Decay(1.0 / 30), 30), Math.Pow(smooth.Decay(1.0 / 144), 144));
        Require(!snap.PreserveOffset(.1, false) && snap.Decay(.01) == 0, "snap policy still smoothed");
        Debug.Log("RF_SDK_TESTS PASS: " + cases + " constant and reversal replay pairs at 30/60/144 Hz, exact committed 30 Hz, preview isolation, immutable authority, freeze/reset, disabled prediction and correction policies; no game imports");
    }
    private static void Equal(double actual, double expected) { Require(Math.Abs(actual - expected) < 1e-8, actual + " != " + expected); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid argument rejection"); }
    private static void OptimizedContracts()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
            Throws(() => new ExponentialCorrection(invalid));
            Throws(() => new ExponentialCorrection(.08, invalid));
            Throws(() => new Realtime.Interpolation.FixedTimeline(30, 15, invalid));
            Throws(() => new ReplayPrediction<State, double>(new Model(), 30, invalid, true));
            Throws(() => new ExponentialCorrection().Decay(invalid));
            var fixedTimeline = new Realtime.Interpolation.FixedTimeline(30, 15);
            fixedTimeline.Reset(10, 1); double before = fixedTimeline.RenderTick;
            fixedTimeline.Observe(11, invalid); fixedTimeline.Advance(invalid);
            Equal(fixedTimeline.RenderTick, before);
            Throws(() => fixedTimeline.Reset(1, invalid));
        }
        var history = new InputHistory<int>();
        var expected = new List<InputHistory<int>.Frame>();
        for (int i = 0; i < 1000; i++) {
            double start = i / 500.0, end = (i + 1) / 500.0;
            history.Record(start, end, i);
            expected.Add(new InputHistory<int>.Frame { Start = start, End = end, Input = i });
            expected.RemoveAll(frame => frame.End < end - 1);
            if (expected.Count > 240) expected.RemoveRange(0, expected.Count - 240);
            Require(history.Frames.Count == expected.Count, "ring count");
            for (int j = 0; j < expected.Count; j++) Require(history.Frames[j].Input == expected[j].Input, "ring order");
            history.SentInput(i, end);
        }
        Require(!history.TryAcknowledgedTime(879, out _) && history.TryAcknowledgedTime(880, out _), "sent eviction");
        history.DiscardBefore(950); Require(!history.TryAcknowledgedTime(949, out _) && history.TryAcknowledgedTime(950, out _), "sent discard");
        int cursor = 0;
        for (double time = 1; time < 2.1; time += .003) Require(history.HeldAt(time, -1) == history.HeldAt(time, -1, ref cursor), "cursor differs from linear lookup");
        history.Clear(); history.Record(0, .1, 42); history.Record(.3, .4, 43);
        cursor = 0; Require(history.HeldAt(.2, -1, ref cursor) == -1 && history.HeldAt(.35, -1, ref cursor) == 43, "gap lookup");
        history.Trim(2); Require(history.Frames.Count == 0, "trim releases frames");
        Debug.Log("RF_OPTIMIZED_CONTRACTS PASS finite settings, ring wrap/eviction, replay cursor and gaps");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("SDK test: " + message); }
}
