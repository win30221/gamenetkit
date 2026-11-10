using System;
using System.Collections.Generic;

namespace Realtime.Examples.PredictionLesson
{
    // Sole owner of the latest complete authoritative roster. No Unity objects or prediction.
    public sealed class ReplicaState
    {
        private readonly Dictionary<long, PlayerState> players = new Dictionary<long, PlayerState>();
        public IReadOnlyDictionary<long, PlayerState> Players => players;
        public long LocalPlayerID { get; private set; }
        public long Tick { get; private set; } = -1;
        public bool Joined => LocalPlayerID > 0;

        public void Clear() { players.Clear(); LocalPlayerID = 0; Tick = -1; }

        // Stale snapshots are harmless; malformed state is a protocol failure.
        public bool Apply(ServerMessage message)
        {
            if (message == null || message.protocol != 1 || message.tick < 0 || message.players == null)
                throw new InvalidOperationException("Invalid world envelope");
            bool welcome = message.type == "welcome";
            if ((!welcome && message.type != "snapshot") || (welcome && Joined) || (!welcome && !Joined))
                throw new InvalidOperationException("Unexpected world message");
            if (!welcome && message.tick <= Tick) return false;
            if (message.players.Length > 8) throw new InvalidOperationException("Roster exceeds capacity");
            var next = new Dictionary<long, PlayerState>();
            foreach (var player in message.players)
            {
                if (player == null || player.id <= 0 || player.ack < 0 || player.inputTick < 0 || player.inputTick > message.tick+1 || !Finite(player.x) || !Finite(player.z) ||
                    Math.Abs(player.x) > 8.001f || Math.Abs(player.z) > 8.001f || next.ContainsKey(player.id))
                    throw new InvalidOperationException("Invalid player state");
                next.Add(player.id, new PlayerState { id = player.id, x = player.x, z = player.z, ack = player.ack, inputTick=player.inputTick });
            }
            long own = welcome ? message.playerID : LocalPlayerID;
            if (own <= 0 || !next.ContainsKey(own)) throw new InvalidOperationException("Local player missing");
            if (welcome && (message.tickRate != 30 || message.snapshotRate != 15))
                throw new InvalidOperationException("Unsupported world rates");
            players.Clear();
            foreach (var item in next) players.Add(item.Key, item.Value);
            LocalPlayerID = own;
            Tick = message.tick;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
