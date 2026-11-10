using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.SharedWorld;

// Executes the actual Unity application's pure session and SDK transport outside
// the Editor. System.Text.Json supplies the codec here; this is not a Player test.
public static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    public static async Task Main(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var oneNet = new NetcodeComposition(); var twoNet = new NetcodeComposition();
        using var one = new ExampleSession(oneNet, s => JsonSerializer.Deserialize<ServerMessage>(s, Json), i => JsonSerializer.Serialize(i, Json));
        using var two = new ExampleSession(twoNet, s => JsonSerializer.Deserialize<ServerMessage>(s, Json), i => JsonSerializer.Serialize(i, Json));
        var endpoint = ExampleEndpoint.Normalize(args.Length > 0 ? args[0] : "http://127.0.0.1:18092");
        float x = 0, z = 0;
        async Task Until(Func<bool> condition)
        {
            double deadline = clock.Elapsed.TotalSeconds + 6;
            while (!condition()) {
                Require(clock.Elapsed.TotalSeconds < deadline, "timeout: " + one.Status + " / " + two.Status);
                one.Pump(clock.Elapsed.TotalSeconds, x, 0); two.Pump(clock.Elapsed.TotalSeconds, 0, z);
                await Task.Delay(10);
            }
        }
        one.Connect(endpoint, "one", 0); two.Connect(endpoint, "two", 0);
        await Until(() => one.State.Players.Count == 2 && two.State.Players.Count == 2);
        long id1 = one.State.LocalPlayerID, id2 = two.State.LocalPlayerID;
        Require(id1 != id2, "IDs reused");
        float start = two.State.Players[id1].x, other = two.State.Players[id2].x;
        x = 1; await Until(() => two.State.Players[id1].x > start + .5f);
        Require(two.State.Players[id2].x == other, "input controlled the other player");
        x = 0; one.StopInput(clock.Elapsed.TotalSeconds);
        long tick = two.State.Tick;
        await Until(() => two.State.Tick > tick + 6);
        float stopped = two.State.Players[id1].x; tick = two.State.Tick;
        await Until(() => two.State.Tick > tick + 6);
        Require(two.State.Players[id1].x == stopped, "stop drifted");
        Require(oneNet.Timeline.RenderTick == one.State.Tick, "timeline not connected");
        int oldEpoch = oneNet.Transport.Epoch;
        one.Disconnect(); await Until(() => two.State.Players.Count == 1);
        z = 1; await Until(() => two.State.Players[id2].z > .5f); z = 0;
        one.Connect(endpoint, "one", clock.Elapsed.TotalSeconds);
        oneNet.Transport.Receive(oldEpoch, "bad stale payload");
        await Until(() => one.State.Players.Count == 2 && two.State.Players.Count == 2);
        Require(one.State.LocalPlayerID > Math.Max(id1, id2), "reconnect reused player ID");
        var state = new ReplicaState();
        var welcome = new ServerMessage { type="welcome",protocol=1,playerID=1,tick=0,tickRate=30,snapshotRate=15,players=new[]{new PlayerState{id=1,x=2}} };
        state.Apply(welcome); welcome.players[0].x = 7; Require(state.Players[1].x == 2, "DTO was retained");
        var update = new ServerMessage {type="snapshot",protocol=1,tick=2,players=new[]{new PlayerState{id=1,x=3}}};
        state.Apply(update); Require(!state.Apply(update), "stale tick accepted");
        update.tick=4; update.players[0].x=float.NaN;
        bool invalid=false;try{state.Apply(update);}catch(InvalidOperationException){invalid=true;}
        Require(invalid && state.Tick==2 && state.Players[1].x==3,"invalid state partially applied");
        state.Clear();Require(!state.Joined && state.Players.Count==0,"state not cleared");
        Console.WriteLine("RF_NATIVE_SESSION PASS: two C# application sessions + SDK ClientWebSocket, shared roster, input ownership, stop, leave, reconnect, stale epoch, atomic state; no Unity renderer");
    }
}
