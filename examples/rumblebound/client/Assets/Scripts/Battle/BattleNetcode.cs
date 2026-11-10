using Rumblebound.Gameplay;
using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Rumblebound.Example.Battle
{
    // Application composition root. Framework packages never import this file.
    public static class BattleNetcode
    {
        public static readonly Func<IPlaybackClock> PlaybackFactory = BattleNetcodeModules.Playback();
        public sealed class PredictionContext
        {
            public IPredictionModel<GameMovement.MotionState, GameMovement.Drive> Model;
            public int TickRate;
            public double Timeout;
        }
        public sealed class PredictionRegistration
        {
            public bool Enabled, FixedTicks;
            public Func<PredictionContext, IPrediction<GameMovement.MotionState, GameMovement.Drive>> Create;
        }
        public static readonly TimelineCatalog Timelines = BuildCatalog();
        public static readonly Dictionary<string, PredictionRegistration> Predictions = BuildPredictions();
        public static readonly Dictionary<string, Func<ICorrectionPolicy>> Corrections = BuildCorrections();
        public static readonly Func<IClientDiagnostics> DiagnosticsFactory = BattleNetcodeModules.Diagnostics();
        public static readonly Func<ConditioningSettings, IMessageConditioner> ConditionerFactory = BattleNetcodeModules.Conditioner();
        public static readonly Func<int,IServerClock> ClockFactory = BattleNetcodeModules.Clock();
        public static readonly Func<IReplicaStore<PlayerState>> ReplicaFactory = BattleNetcodeModules.Replica();
        public static readonly Func<IActionPresentation> ActionsFactory = BattleNetcodeModules.Actions();
        public static readonly Func<IFrameDecoder> DecoderFactory = BattleNetcodeModules.Decoder();
        public static IClientDiagnostics CreateDiagnostics() => DiagnosticsFactory == null ? new NoClientDiagnostics() : DiagnosticsFactory();
        public static IMessageConditioner CreateConditioner(ConditioningSettings settings)
        {
            if (ConditionerFactory == null) throw new InvalidOperationException("Message simulation module is not installed");
            return ConditionerFactory(settings);
        }
        private static Dictionary<string, PredictionRegistration> BuildPredictions()
        {
            var result = new Dictionary<string, PredictionRegistration>(StringComparer.Ordinal) {
                ["authority"] = new PredictionRegistration { Create = c => new AuthorityPrediction<GameMovement.MotionState, GameMovement.Drive>(c.Model) }
            };
            BattleNetcodeModules.InstallPredictions(result); return result;
        }
        private static Dictionary<string, Func<ICorrectionPolicy>> BuildCorrections()
        {
            var result = new Dictionary<string, Func<ICorrectionPolicy>>(StringComparer.Ordinal) { ["snap"] = () => new SnapCorrection() };
            BattleNetcodeModules.InstallCorrections(result); return result;
        }
        private static TimelineCatalog BuildCatalog()
        {
            var catalog = new TimelineCatalog();
            BattleNetcodeModules.Install(catalog);
            return catalog;
        }
        public static string DefaultMode => Timelines.Contains("adaptive") ? "adaptive" : "latest";
        public static string DefaultPrediction => Predictions.ContainsKey("frame") ? "frame" : "authority";
        public static string DefaultCorrection => Corrections.ContainsKey("exponential") ? "exponential" : "snap";
        public static IPrediction<GameMovement.MotionState, GameMovement.Drive> CreatePrediction(string id,
            IPredictionModel<GameMovement.MotionState, GameMovement.Drive> model, Rules rules)
        {
            if (!Predictions.TryGetValue(id, out var module)) throw new InvalidOperationException("Prediction module is not installed: " + id);
            var prediction = module.Create(new PredictionContext { Model = model, TickRate = rules.tickRate, Timeout = (double)rules.inputTimeoutTicks / rules.tickRate });
            if (prediction.Mode != id || prediction.Enabled != module.Enabled) throw new InvalidOperationException("Prediction module identity mismatch: " + id);
            return prediction;
        }
        public static ICorrectionPolicy CreateCorrection(string id)
        {
            if (!Corrections.TryGetValue(id, out var factory)) throw new InvalidOperationException("Correction module is not installed: " + id);
            var policy = factory(); if (policy.Mode != id) throw new InvalidOperationException("Correction module identity mismatch: " + id);
            return policy;
        }
        public static IPresentationTimeline Create(string mode, Rules rules) => Timelines.Create(mode, rules.tickRate, rules.snapshotRate);
        public static IPresentationTimeline CreateDefault(Rules rules) => Create(DefaultMode, rules);
    }
}
