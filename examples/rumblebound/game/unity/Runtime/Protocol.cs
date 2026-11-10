using System;

namespace Rumblebound.Gameplay
{
    [Serializable] public sealed class Rules
    {
        public int protocol, tickRate, snapshotRate, maxPlayers, inputTimeoutTicks, reconnectTicks;
        public int maxActiveGroundCasts;
        public int maxHP, maxProjectiles, projectileLifetimeTicks, projectileDamage, fireCooldownTicks, dashTicks, dashCooldownTicks, respawnTicks;
        public int matchDurationTicks, scoreGoal, pointRotationTicks, pointWarningTicks;
        public string content;
        public double speed, arenaHalfSize, playerRadius, projectileSpeed, projectileRadius, dashSpeed, controlPointRadius;
        public PointLocation[] controlPoints;
        public HeroDefinition[] heroes;
        public CollisionConfig collision;
    }
    [Serializable] public sealed class HeroDefinition
    {
        public string heroID, attackKind;
        public double basicRange;
        public int basicDamage,basicWindupTicks,basicCooldownTicks;
        public double castRange, impactRadius;
        public int groundDamage, castWindupTicks, fallTicks, attackStopTicks;
        public int maxHP, projectileDamage, projectileLifetimeTicks, fireCooldownTicks, dashCooldownTicks, meleeDamage;
        public double speed, projectileSpeed, dashSpeed, meleeRange, meleeArcDegrees;
    }
    [Serializable] public sealed class LabConfig
    {
        public Rules rules;
        public string matchID, webTransportURL, certificateHash;
        public string mapJSON, mapHash;
        [NonSerialized] public ArenaMapDefinition map;
        public bool networkLab, connectionProbe;
        public string[] netFeatures;
        public string googleClientID;
        public bool guestLogin;
    }
    [Serializable] public sealed class SessionReply { public string session, userID, error; }
    [Serializable] public sealed class TicketReply { public string ticket, matchID, error; }
    [Serializable] public sealed class InputMessage
    {
        public string type, content, matchID, ticket, action;
        public string targetID;
        public long targetLife;
        public int protocol;
        public uint generation;
        public long seq, actionID, actionTick;
        public long frameAck, viewTick, observedTick;
        public double x, z, echo, aimX, aimZ, facingX, facingZ;
        public bool hasGroundTarget;
        public double targetX, targetZ;
    }
    [Serializable] public sealed class ProbeMessage
    {
        public string type, content, matchID;
        public int protocol;
        public uint generation;
        public double echo;
        public bool Matches(ProbeMessage other) => other != null && echo == other.echo &&
            protocol == other.protocol && content == other.content && matchID == other.matchID && generation == other.generation;
    }
    [Serializable] public sealed class LoadedMessage
    {
        public string type, content, matchID, mapHash;
        public int protocol;
        public uint generation;
    }
    [Serializable] public sealed class PlayerState
    {
        public long fireActionID;
        public long basicReadyTick,attackReleaseTick,attackTargetLife,basicActionID;
        public string attackTargetID;
        public long life;
        public string playerID, entityID, teamID, heroID;
        public double x, z, facingX, facingZ;
        public double dashX, dashZ;
        public long dashEndTick;
        public long ack, fireReadyTick, dashReadyTick, respawnTick, protectedUntilTick, attackStopUntilTick;
        public int hp;
        public bool online, left, bot;
    }
    [Serializable] public sealed class ProjectileState
    {
        public long actionID;
        public string id, ownerID, teamID;
        public double x, z, dx, dz;
        public long expiresTick;
    }
    [Serializable] public sealed class ProjectileEnd
    {
        public string id, kind, targetID, ownerID;
        public double x, z;
        public long tick;
    }
    [Serializable] public sealed class CombatEvent
    {
        public long actionID;
        public string id, kind, ownerID, targetID;
        public double x, z, dx, dz;
        public long tick;
    }
    [Serializable] public sealed class GroundCastState
    {
        public long actionID;
        public string id, ownerID, teamID;
        public double x, z, radius;
        public long startTick, fallStartTick, impactTick;
    }
    [Serializable] public sealed class GroundImpactState
    {
        public string id, ownerID;
        public double x, z, radius;
        public long tick;
        public string[] targets;
    }
    [Serializable] public sealed class PointLocation
    {
        public int index;
        public double x, z, radius;
    }
    [Serializable] public sealed class TeamScore
    {
        public string teamID;
        public int score;
        public long scoreTicks;
        public bool forfeited;
    }
    [Serializable] public sealed class MatchResult
    {
        public string outcome, reason, winnerTeamID;
        public long endedTick;
    }
    [Serializable] public sealed class MatchState
    {
        public long endsAtTick, rotationTick;
        public PointLocation point, nextPoint;
        public TeamScore[] scores;
        public string phase, pointStatus, scoringTeamID;
        public MatchResult result;
    }
    [Serializable] public sealed class Snapshot
    {
        public TechState tech;
        public long clockTick;
        public bool clockValid;
        public string type, content, matchID, playerID, code, mapHash, botMode;
        public bool practice;
        public int protocol;
        public uint generation;
        public long tick, inputTick, actionAck;
        public double echo, connectionRttMS; // RTT is stamped locally by the transport.
        public string actionCode;
        public PlayerState[] players;
        public ProjectileState[] projectiles;
        public ProjectileEnd[] projectileEnds;
        public CombatEvent[] combatEvents;
        public GroundCastState[] groundCasts;
        public GroundImpactState[] groundImpacts;
        public MatchState match;
    }
    // Same planar operation as Go, never Unity physics. Prediction uses elapsed
    // frame time while the authority and parity tests use fixed ticks.
    public static class Movement
    {
        public static void Step(ref double x, ref double z, double dx, double dz, Rules c)
        { Advance(ref x, ref z, dx, dz, 1.0 / c.tickRate, c); }
        public static void Advance(ref double x, ref double z, double dx, double dz, double seconds, Rules c)
        {
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length > 1) { dx /= length; dz /= length; }
            double limit = c.arenaHalfSize - c.playerRadius;
            x = Math.Max(-limit, Math.Min(limit, x + dx * c.speed * seconds));
            z = Math.Max(-limit, Math.Min(limit, z + dz * c.speed * seconds));
        }
    }
}
