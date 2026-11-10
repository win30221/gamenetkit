using Rumblebound.Gameplay;
using Realtime.Core;
using System;
using System.Collections.Generic;

namespace Rumblebound.Example.Battle
{
    // To remove the optional package: remove its manifest dependency and these
    // registrations. This is the only application reference to its concrete types.
    public static class BattleNetcodeModules
    {
        public static Func<IPlaybackClock> Playback()
        {
            Func<IPlaybackClock> factory=null;
            // BEGIN OPTIONAL playback
            factory=()=>new Realtime.Playback.PlaybackClock();
            // END OPTIONAL playback
            return factory;
        }
        public static Func<int,IServerClock> Clock()
        {
            Func<int,IServerClock> factory=null;
            // BEGIN OPTIONAL clock
            factory=rate=>new Realtime.Clock.DisciplinedClock(rate);
            // END OPTIONAL clock
            return factory;
        }
        public static Func<IReplicaStore<PlayerState>> Replica()
        {
            Func<IReplicaStore<PlayerState>> factory=null;
            // BEGIN OPTIONAL replication
            factory=()=>new Realtime.Replication.ReplicaStore<PlayerState>(p=>p.playerID,p=>p.entityID+":"+p.life,ClonePlayer);
            // END OPTIONAL replication
            return factory;
        }
        public static Func<IActionPresentation> Actions()
        {
            Func<IActionPresentation> factory=null;
            // BEGIN OPTIONAL actions
            factory=()=>new Realtime.Actions.ActionPresentation();
            // END OPTIONAL actions
            return factory;
        }
        public static Func<IFrameDecoder> Decoder()
        {
            Func<IFrameDecoder> factory=null;
            // BEGIN OPTIONAL codec
            factory=()=>new Realtime.Codec.FrameDecoder();
            // END OPTIONAL codec
            return factory;
        }
        private static PlayerState ClonePlayer(PlayerState p) => new PlayerState {
            playerID=p.playerID,entityID=p.entityID,teamID=p.teamID,heroID=p.heroID,x=p.x,z=p.z,facingX=p.facingX,facingZ=p.facingZ,
            ack=p.ack,online=p.online,left=p.left,bot=p.bot,hp=p.hp,fireReadyTick=p.fireReadyTick,fireActionID=p.fireActionID,life=p.life,
            basicReadyTick=p.basicReadyTick,attackReleaseTick=p.attackReleaseTick,attackTargetID=p.attackTargetID,attackTargetLife=p.attackTargetLife,basicActionID=p.basicActionID,
            attackStopUntilTick=p.attackStopUntilTick,dashReadyTick=p.dashReadyTick,respawnTick=p.respawnTick,protectedUntilTick=p.protectedUntilTick,
            dashX=p.dashX,dashZ=p.dashZ,dashEndTick=p.dashEndTick
        };
        public static Func<IClientDiagnostics> Diagnostics()
        {
            // BEGIN OPTIONAL diagnostics
            return () => new Realtime.Diagnostics.ClientDiagnostics();
            // END OPTIONAL diagnostics
            #pragma warning disable CS0162
            return null;
            #pragma warning restore CS0162
        }
        public static Func<ConditioningSettings, IMessageConditioner> Conditioner()
        {
            // BEGIN OPTIONAL simulation
            return settings => new Realtime.Simulation.MessageConditioner(settings);
            // END OPTIONAL simulation
            #pragma warning disable CS0162
            return null;
            #pragma warning restore CS0162
        }
        public static void Install(TimelineCatalog catalog)
        {
            // BEGIN OPTIONAL interpolation
            catalog.Register("fixed", (tick, snapshot) => new Realtime.Interpolation.FixedTimeline(tick, snapshot));
            catalog.Register("adaptive", (tick, snapshot) => new Realtime.Interpolation.SnapshotInterpolation(tick, snapshot));
            // END OPTIONAL interpolation
        }
        public static void InstallPredictions(Dictionary<string, BattleNetcode.PredictionRegistration> modules)
        {
            // BEGIN OPTIONAL prediction
            modules.Add("frame", new BattleNetcode.PredictionRegistration { Enabled = true, Create = c => new Realtime.Prediction.ReplayPrediction<GameMovement.MotionState, GameMovement.Drive>(c.Model, c.TickRate, c.Timeout, false) });
            modules.Add("fixed-step", new BattleNetcode.PredictionRegistration { Enabled = true, FixedTicks = true, Create = c => new Realtime.Prediction.ReplayPrediction<GameMovement.MotionState, GameMovement.Drive>(c.Model, c.TickRate, c.Timeout, true) });
            // END OPTIONAL prediction
        }
        public static void InstallCorrections(Dictionary<string, Func<ICorrectionPolicy>> modules)
        {
            // BEGIN OPTIONAL correction
            modules.Add("exponential", () => new Realtime.Correction.ExponentialCorrection());
            // END OPTIONAL correction
        }
    }
}
