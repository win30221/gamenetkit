using System;
using System.Collections.Generic;
using System.Globalization;
using Realtime.Replication;

namespace Realtime.Examples.HistoryLesson
{
    // SDK stores own cloned authoritative models; rendering remains an app concern.
    public sealed class ReplicaState
    {
        private readonly ReplicaStore<PlayerState> playerStore = new ReplicaStore<PlayerState>(p=>p.id.ToString(CultureInfo.InvariantCulture),p=>"1",p=>new PlayerState{id=p.id,x=p.x,z=p.z,ack=p.ack,inputTick=p.inputTick},8);
        private readonly ReplicaStore<WorldObject> objectStore = new ReplicaStore<WorldObject>(o=>o.id,o=>o.life.ToString(CultureInfo.InvariantCulture),o=>new WorldObject{id=o.id,owner=o.owner,life=o.life,x=o.x,z=o.z},32);
        private readonly Dictionary<long, PlayerState> players = new Dictionary<long, PlayerState>();
        private readonly ReplicaStore<TargetState> targets=new ReplicaStore<TargetState>(_=>"target",t=>t.life.ToString(CultureInfo.InvariantCulture),CloneTarget,1);
        public TargetState Target=>targets.Values.TryGetValue("target",out var value)?value:null;
        public HistoryInfo History{get;private set;}
        private readonly List<string> changes = new List<string>();
        public IReadOnlyDictionary<long, PlayerState> Players => players;
        public IReadOnlyDictionary<string,WorldObject> Objects=>objectStore.Values;
        public IReadOnlyList<string> Changes=>changes;
        public long Added{get;private set;}
        public long Removed{get;private set;}
        public long Updated{get;private set;}
        public long LocalPlayerID { get; private set; }
        public long Tick { get; private set; } = -1;
        public bool Joined => LocalPlayerID > 0;

        public void Clear() { playerStore.Clear();objectStore.Clear();targets.Clear();History=null;players.Clear();changes.Clear();Added=Removed=Updated=0;LocalPlayerID=0;Tick=-1; }

        // Stale snapshots are harmless; malformed state is a protocol failure.
        public bool Apply(ServerMessage message)
        {
            if (message == null || message.protocol != 1 || message.generation!=1 || message.tick < 0 || message.players == null || message.objects==null)
                throw new InvalidOperationException("Invalid world envelope");
            bool welcome = message.type == "welcome";
            if ((!welcome && message.type != "snapshot") || (welcome && Joined) || (!welcome && !Joined))
                throw new InvalidOperationException("Unexpected world message");
            if (!welcome && message.tick <= Tick) return false;
            ValidateTarget(message.target);
            var history=message.history;
            if(history==null||history.count<1||history.count>90||history.first<0||history.last<history.first||history.last>message.tick||history.maxRewind!=15||Target!=null&&message.target.life<Target.life)throw new InvalidOperationException("Invalid target history");
            if (message.players.Length > 8 || message.objects.Length>32) throw new InvalidOperationException("Roster exceeds capacity");
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
            var objectIDs=new HashSet<string>(StringComparer.Ordinal);var owners=new Dictionary<long,int>();
            foreach(var o in message.objects){
                if(o==null || string.IsNullOrEmpty(o.id) || o.id.Length>32 || o.life==0 || !next.ContainsKey(o.owner) || !Finite(o.x) || !Finite(o.z) || Math.Abs(o.x)>8.001f || Math.Abs(o.z)>8.001f || !objectIDs.Add(o.id))throw new InvalidOperationException("Invalid object");
                owners.TryGetValue(o.owner,out int count);if(count>=4)throw new InvalidOperationException("Owner capacity");owners[o.owner]=count+1;
            }
            // Validate both complete rosters before either SDK store changes.
            if(!playerStore.Apply(message.generation,message.tick,message.players) || !objectStore.Apply(message.generation,message.tick,message.objects))throw new InvalidOperationException("Replica apply failed");
            if(!targets.Apply(message.generation,message.tick,new[]{message.target}))throw new InvalidOperationException("Target replica failed");
            players.Clear();
            foreach (var p in playerStore.Values.Values) players.Add(p.id,p);
            foreach(var id in objectStore.Removed)Log("移除 "+id);
            foreach(var id in objectStore.Added)Log("新增 "+id+" · life "+Objects[id].life);
            Added+=objectStore.Added.Count;Removed+=objectStore.Removed.Count;Updated+=objectStore.Updated.Count;
            LocalPlayerID = own;
            Tick = message.tick;
            History=new HistoryInfo{first=history.first,last=history.last,count=history.count,maxRewind=history.maxRewind};
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static void ValidateTarget(TargetState target){if(target==null||target.life==0||target.hp<1||target.hp>5||!Finite(target.x)||!Finite(target.z)||Math.Abs(target.x)>5.001f||target.z!=3)throw new InvalidOperationException("Invalid target");}
        public static TargetState CloneTarget(TargetState target)=>new TargetState{life=target.life,hp=target.hp,x=target.x,z=target.z};
        private void Log(string value){changes.Insert(0,value);if(changes.Count>8)changes.RemoveAt(changes.Count-1);}
    }
}
