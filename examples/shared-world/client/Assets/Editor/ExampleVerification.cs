using System;
using UnityEngine;

namespace Realtime.Examples.SharedWorld.Editor
{
    public static class ExampleVerification
    {
        public static void Run()
        {
            var state = new ReplicaState();
            var welcome = JsonUtility.FromJson<ServerMessage>("{\"type\":\"welcome\",\"protocol\":1,\"playerID\":1,\"tick\":0,\"tickRate\":30,\"snapshotRate\":15,\"players\":[{\"id\":1,\"x\":-2,\"z\":0,\"ack\":0},{\"id\":2,\"x\":2,\"z\":0,\"ack\":0}]}");
            Require(state.Apply(welcome) && state.Players.Count == 2 && state.LocalPlayerID == 1, "welcome");
            welcome.players[0].x = 7;
            Require(state.Players[1].x == -2, "snapshot must be copied");
            var update = JsonUtility.FromJson<ServerMessage>("{\"type\":\"snapshot\",\"protocol\":1,\"tick\":2,\"players\":[{\"id\":1,\"x\":-1,\"z\":0,\"ack\":2}]}");
            Require(state.Apply(update) && state.Players.Count == 1 && state.Players[1].ack == 2, "leave/ack");
            Require(!state.Apply(update), "stale snapshot");
            update.tick = 3; update.players[0].x = float.NaN;
            bool rejected = false;
            try { state.Apply(update); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && state.Tick == 2 && state.Players[1].x == -1, "atomic validation");
            state.Clear(); Require(!state.Joined && state.Players.Count == 0 && state.Tick == -1, "clear");
            Require(JsonUtility.ToJson(new InputMessage { seq = 3, x = 1 }).Contains("\"type\":\"input\""), "input codec");
            Require(ExampleEndpoint.Normalize("https://localhost:18443") == "wss://localhost:18443/ws", "endpoint");
            Debug.Log("RF_SHARED_WORLD_STATE PASS: Unity JSON, roster lifecycle, stale/invalid snapshots and endpoint");
        }
        private static void Require(bool value, string condition) { if (!value) throw new Exception(condition); }
    }
}
