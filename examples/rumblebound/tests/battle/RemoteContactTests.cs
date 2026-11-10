using System;
using Realtime.Core;
using Realtime.Correction;
using Rumblebound.Gameplay;

public static class RemoteContactTests
{
    // Deterministic predictor: create a known local error and remote proxy push
    // without mixing transport timing or the crowd simulator into policy tests.
    private sealed class Prediction : IPrediction<GameMovement.MotionState, GameMovement.Drive>
    {
        private readonly IPredictionModel<GameMovement.MotionState, GameMovement.Drive> model;
        public double LocalX, Push;
        public string Mode => "test";
        public bool Enabled => true;
        public GameMovement.MotionState State { get; }
        public PredictionMetrics Metrics => default;
        public Prediction(IPredictionModel<GameMovement.MotionState, GameMovement.Drive> model) { this.model = model; State = model.CreateState(); }
        public void SentInput(long sequence, double now) { }
        public void Reconcile(GameMovement.MotionState authority, PredictionAck ack, double now) { model.Copy(authority, State); }
        public void Advance(double now, GameMovement.Drive input)
        {
            State.X = LocalX;
            foreach (var body in State.Bodies) if (body.ID == "remote") body.X = 5 + Push;
        }
    }
    private static void Require(bool value, string reason) { if (!value) throw new Exception("Remote contact: " + reason); }
    private static void Near(double actual, double expected, string reason) { Require(Math.Abs(actual - expected) < 1e-9, reason + " (" + actual + " != " + expected + ")"); }
    private static PlayerState Player(string id, double x, long life = 1) =>
        new PlayerState { playerID = id, entityID = id, life = life, x = x, hp = 100, online = true };
    private static Snapshot State(long tick, long life = 1) => new Snapshot
    {
        type = tick == 0 ? "welcome" : "snapshot", tick = tick,
        players = new[] { Player("own", 0), Player("remote", 5, life) }
    };
    private static void Legal(GameMovement movement, Snapshot state, ArenaMapDefinition map, double radius)
    {
        movement.Position(state.players[0], true, out double ax, out double az);
        movement.Position(state.players[1], false, out double bx, out double bz);
        Require(ArenaCollision.CircleClear(map, ax, az, radius) && ArenaCollision.CircleClear(map, bx, bz, radius), "presentation crossed terrain");
        Require(Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)) >= 2 * radius - 1e-6, "presentation bodies overlapped");
    }
    private static void Exercise(double seconds, ICorrectionPolicy local)
    {
        var rules = new Rules { tickRate = 30, snapshotRate = 15, inputTimeoutTicks = 6, arenaHalfSize = 20, playerRadius = .4,
            collision = new CollisionConfig { iterations = 8, pushSpeed = 6, bodyMass = 1, pushStrength = 1 } };
        var map = new ArenaMapDefinition { arenaHalfSize = 20,
            obstacles = new[] { new ArenaObstacle { x = 8, halfX = 1, halfZ = 1, blocksMovement = true } } };
        Prediction prediction = null;
        var movement = new GameMovement(rules, map, new LatestSnapshotTimeline(), model => prediction = new Prediction(model), local,
            remoteContactSmoothingSeconds: seconds);
        movement.Accept(State(0), "own", 0);
        prediction.LocalX = 1; prediction.Push = .5; movement.Advance(0, 0, 0);
        prediction.LocalX = 0; prediction.Push = 0;
        var authority = State(2); movement.Accept(authority, "own", .01);
        movement.Position(authority.players[0], true, out double own, out _);
        movement.Position(authority.players[1], false, out double remote, out _);
        Near(own, local.Smooths ? 1 : 0, "remote option changed local correction policy");
        Near(remote, seconds == 0 ? 5 : 5.5, "remote option did not control authority rebase");
        Legal(movement, authority, map, rules.playerRadius);
        movement.Advance(.02, 0, 0);
        movement.Position(authority.players[0], true, out own, out _);
        movement.Position(authority.players[1], false, out remote, out _);
        Near(own, local.Smooths ? local.Decay(.02) : 0, "local Snap/smoothing was bypassed");
        Near(remote, seconds == 0 ? 5 : 5 + .5 * Math.Exp(-.02 / seconds), "wrong independent contact decay");
        Legal(movement, authority, map, rules.playerRadius);

        // Same entity, missed death: no remote push from the previous life may
        // survive even when its contact smoothing window has not yet elapsed.
        authority = State(4, 2); movement.Accept(authority, "own", .025);
        movement.Position(authority.players[1], false, out remote, out _);
        Near(remote, 5, "previous life retained a remote contact offset");
        Legal(movement, authority, map, rules.playerRadius);

        // Direct contact settling still has to keep map and player separation.
        prediction.LocalX = 5; prediction.Push = 2.5; movement.Advance(.04, 0, 0);
        Legal(movement, authority, map, rules.playerRadius);
        authority = State(6, 2); movement.Accept(authority, "own", .05); movement.Advance(.06, 0, 0);
        Legal(movement, authority, map, rules.playerRadius);
    }
    public static void Run()
    {
        foreach (double seconds in new[] { 0, GameMovement.DefaultRemoteContactSmoothingSeconds, .2 })
        {
            Exercise(seconds, new SnapCorrection());
            Exercise(seconds, new ExponentialCorrection());
        }
        foreach (double invalid in new[] { -.01, 1.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool rejected = false;
            try { new GameMovement(new Rules(), null, null, null, null, remoteContactSmoothingSeconds: invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "invalid smoothing value accepted");
        }
        Console.WriteLine("REMAKE_REMOTE_CONTACT PASS independent local/remote correction, configurable contact decay, life reset and collision safety");
    }
}
