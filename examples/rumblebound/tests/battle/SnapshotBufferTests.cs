using System;
using Realtime.Core;
using Rumblebound.Gameplay;

public static class SnapshotBufferTests
{
    private sealed class Frame { public double Position; public bool Present = true; }
    private sealed class Timeline : IPresentationTimeline
    {
        public string Mode => "test";
        public bool Interpolates => true;
        public double RenderTick { get; set; }
        public TimelineMetrics Metrics => default;
        public void Reset(long tick, double now) { }
        public void Observe(long tick, double now) { }
        public void Advance(double now) { }
    }
    private static readonly SnapshotSelector<Frame, int, double> select = Select;
    private static bool Select(Frame frame, int unused, out double value) { value = frame.Position; return frame.Present; }
    private static void Require(bool value, string reason) { if (!value) throw new Exception("SnapshotBuffer: " + reason); }
    private static void Near(double actual, double expected, string reason) { Require(Math.Abs(actual - expected) < 1e-9, reason); }
    private static Rules Rules() => new Rules { tickRate = 30, snapshotRate = 15, inputTimeoutTicks = 6, arenaHalfSize = 100, playerRadius = .4 };
    private static PlayerState Player(double x, long life = 1, string entity = "entity") =>
        new PlayerState { playerID = "remote", entityID = entity, life = life, x = x, hp = 100, online = true };
    private static Snapshot State(long tick, PlayerState player = null, ProjectileState shot = null) => new Snapshot
    {
        type = "snapshot", tick = tick,
        players = player == null ? Array.Empty<PlayerState>() : new[] { player },
        projectiles = shot == null ? Array.Empty<ProjectileState>() : new[] { shot }
    };
    private static GameMovement Movement(Timeline timeline, bool mapped = false) => new GameMovement(Rules(),
        mapped ? new ArenaMapDefinition { arenaHalfSize = 100 } : null, timeline, null, new SnapCorrection());

    public static void Run()
    {
        var buffer = new SnapshotBuffer<Frame>(3);
        Require(!buffer.TryBracket(15, 0, select, out _), "empty history has no bracket");
        buffer.Add(10, new Frame { Position = 0 });
        buffer.Add(20, new Frame { Position = 10 });
        buffer.Add(30, new Frame { Position = 20, Present = false });
        buffer.Add(40, new Frame { Position = 30 });
        Require(buffer.Count == 3 && buffer[0].Tick == 20 && buffer[2].Tick == 40, "wrap retains bounded chronological history");
        Require(!buffer.Add(40, new Frame { Position = 100 }) && !buffer.Add(35, new Frame { Position = 100 }), "duplicate and older ticks are ignored");
        Require(buffer.TryBracket(30, 0, select, out var bracket) && bracket.BeforeTick == 20 && bracket.AfterTick == 40,
            "missing entity samples are skipped");
        Near(bracket.Before + (bracket.After - bracket.Before) * bracket.Fraction, 20, "filtered midpoint");
        Require(buffer.TryBracket(5, 0, select, out bracket) && bracket.BeforeTick == 20 && bracket.AfterTick == 20 && bracket.Fraction == 0,
            "before history clamps to first entity sample");
        Require(buffer.TryBracket(50, 0, select, out bracket) && bracket.BeforeTick == 40 && !bracket.HasAfter,
            "after history holds last entity sample");
        Require(buffer.TryBracket(20, 0, select, out bracket) && bracket.BeforeTick == bracket.AfterTick && bracket.Fraction == 0,
            "exact tick uses one sample");
        Require(buffer.TryBracket(25, 0, select, out bracket, 1) && bracket.BeforeTick == 40,
            "application can exclude the previous lifetime");
        buffer.Clear();
        Require(buffer.Count == 0 && buffer.Add(1, new Frame { Position = 7 }), "reset permits a new lower tick domain");
        Require(!buffer.TryBracket(double.NaN, 0, select, out _), "NaN render tick is rejected");

        var timeline = new Timeline { RenderTick = 20 };
        var movement = Movement(timeline);
        movement.Accept(State(10, Player(0)), "own", 0);
        movement.Accept(State(20), "own", .1);
        var latest = Player(20);
        movement.Accept(State(30, latest), "own", .2);
        movement.Position(latest, false, out double x, out _);
        Near(x, 10, "game actor interpolation keeps missing-snapshot sampling");
        timeline.RenderTick = 35;
        movement.Position(latest, false, out x, out _);
        Near(x, 20, "game actor never extrapolates beyond authority");

        foreach (string boundary in new[] { "death", "left", "missing", "entity", "life" })
        {
            timeline = new Timeline { RenderTick = 15 };
            movement = Movement(timeline, true);
            movement.Accept(State(10, Player(-8)), "own", 0);
            if (boundary == "death" || boundary == "left" || boundary == "missing")
            {
                var gap = boundary == "missing" ? null : Player(-7);
                if (gap != null) { gap.hp = boundary == "death" ? 0 : 100; gap.left = boundary == "left"; }
                movement.Accept(State(15, gap), "own", .1);
            }
            latest = Player(8, boundary == "life" ? 2 : 1, boundary == "entity" ? "replacement" : "entity");
            movement.Accept(State(20, latest), "own", .2);
            movement.Position(latest, false, out x, out _);
            Near(x, 8, "actor continuity resets at " + boundary);
        }

        timeline = new Timeline { RenderTick = 12 };
        movement = Movement(timeline);
        var shot = new ProjectileState { id = "shot", x = 0 };
        movement.Accept(State(10, shot: shot), "own", 0);
        movement.Accept(State(20, shot: new ProjectileState { id = "shot", x = 10 }), "own", .1);
        var ending = new ProjectileEnd { id = "shot", tick = 14, x = 6 };
        movement.ProjectilePosition(shot, out x, out _, ending);
        Near(x, 3, "terminal endpoint takes precedence over later snapshot");
        timeline.RenderTick = 16;
        movement.ProjectilePosition(shot, out x, out _, ending);
        Near(x, 6, "projectile stops exactly at its terminal endpoint");
        timeline.RenderTick = 30;
        movement.ProjectilePosition(shot, out x, out _);
        Near(x, 10, "projectile holds its latest sample without an endpoint");
        Console.WriteLine("REMAKE_SNAPSHOT_BUFFER PASS bounded ring/reset, filtered brackets, actor lifetime continuity and projectile terminal sampling");
    }
}
