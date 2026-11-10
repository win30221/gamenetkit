using System;
using System.Collections.Generic;
using System.Globalization;
using Realtime.Replication;

namespace Realtime.Examples.CodecLesson
{
    // SDK stores own cloned authoritative models; rendering remains an app concern.
    public sealed class ReplicaState
    {
        private readonly ReplicaStore<PlayerState> playerStore = new ReplicaStore<PlayerState>(p=>p.id.ToString(CultureInfo.InvariantCulture),p=>"1",p=>new PlayerState{id=p.id,x=p.x,z=p.z,ack=p.ack,inputTick=p.inputTick},8);
        private readonly ReplicaStore<WorldObject> objectStore = new ReplicaStore<WorldObject>(o=>o.id,o=>o.life.ToString(CultureInfo.InvariantCulture),o=>new WorldObject{id=o.id,owner=o.owner,life=o.life,x=o.x,z=o.z,kind=o.kind},45);
        private readonly Dictionary<long, PlayerState> players = new Dictionary<long, PlayerState>();
        private readonly List<string> changes = new List<string>();
        public IReadOnlyDictionary<long, PlayerState> Players => players;
        public IReadOnlyDictionary<string,WorldObject> Objects=>objectStore.Values;
        public IReadOnlyList<string> Changes=>changes;
        public long Added{get;private set;}
        public long Removed{get;private set;}
        public long Updated{get;private set;}
        public InterestSummary Interest{get;private set;}
        public CodecSummary Codec{get;private set;}
        public long LocalPlayerID { get; private set; }
        public long Tick { get; private set; } = -1;
        public bool Joined => LocalPlayerID > 0;

        public void Clear() { playerStore.Clear();objectStore.Clear();players.Clear();changes.Clear();Added=Removed=Updated=0;Interest=null;Codec=null;LocalPlayerID=0;Tick=-1; }

        // Stale snapshots are harmless; malformed state is a protocol failure.
        public bool Apply(ServerMessage message)
        {
            if (message == null || message.protocol != 1 || message.generation!=1 || message.tick < 0 || message.players == null || message.objects==null)
                throw new InvalidOperationException("Invalid world envelope");
            bool welcome = message.type == "welcome";
            if ((!welcome && message.type != "snapshot") || (welcome && Joined) || (!welcome && !Joined))
                throw new InvalidOperationException("Unexpected world message");
            if (!welcome && message.tick <= Tick) return false;
            var encoding=message.codec;
            if(encoding==null || (encoding.mode!="json"&&encoding.mode!="delta") || encoding.stream==0 || (encoding.step!=0&&encoding.step!=.01&&encoding.step!=.1) || double.IsNaN(encoding.maxError) || encoding.maxError<0 || encoding.maxError>encoding.step/2+1e-6)throw new InvalidOperationException("Invalid codec summary");
            if (message.players.Length > 8 || message.objects.Length>45) throw new InvalidOperationException("Roster exceeds capacity");
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
            var info=message.interest;
            if(info==null || (info.mode!="all"&&info.mode!="aoi") || !Finite(info.radius) || info.radius<2 || info.radius>6 || info.margin!=.75f || !Finite(info.centerX) || !Finite(info.centerZ) || Math.Abs(info.centerX-next[own].x)>.0001f || Math.Abs(info.centerZ-next[own].z)>.0001f || info.totalPlayers<next.Count || info.totalPlayers>8 || info.totalObjects<13 || info.totalObjects>45 || info.totalObjects<message.objects.Length || info.ownedObjects<0 || info.ownedObjects>4 || info.mode=="all"&&(info.totalPlayers!=next.Count||info.totalObjects!=message.objects.Length))throw new InvalidOperationException("Invalid interest summary");
            var objectIDs=new HashSet<string>(StringComparer.Ordinal);var owners=new Dictionary<long,int>();
            int globals=0,ownReceived=0;
            foreach(var o in message.objects){
                if(o==null || string.IsNullOrEmpty(o.id) || o.id.Length>32 || o.life==0 || !Finite(o.x) || !Finite(o.z) || Math.Abs(o.x)>8.001f || Math.Abs(o.z)>8.001f || !objectIDs.Add(o.id))throw new InvalidOperationException("Invalid object");
                if(o.kind=="marker"){
                    // AOI can include an owned marker while its owner is outside
                    // the received player roster. Ownership remains server-side.
                    if(o.owner<=0 || !o.id.StartsWith("object-",StringComparison.Ordinal) || info.mode=="all"&&!next.ContainsKey(o.owner))throw new InvalidOperationException("Invalid marker owner");
                    owners.TryGetValue(o.owner,out int count);if(count>=4)throw new InvalidOperationException("Owner capacity");owners[o.owner]=count+1;if(o.owner==own)ownReceived++;
                }else if(o.kind=="global"){
                    if(o.id!="landmark-hub" || o.owner!=0 || o.life!=1 || o.x!=0 || o.z!=7)throw new InvalidOperationException("Invalid global landmark");globals++;
                }else if(o.kind!="landmark" || o.owner!=0 || o.life!=1 || !o.id.StartsWith("landmark-",StringComparison.Ordinal))throw new InvalidOperationException("Invalid landmark");
            }
            if(globals!=1 || ownReceived>info.ownedObjects)throw new InvalidOperationException("Invalid required objects");
            // Validate both complete rosters before either SDK store changes.
            if(!playerStore.Apply(message.generation,message.tick,message.players) || !objectStore.Apply(message.generation,message.tick,message.objects))throw new InvalidOperationException("Replica apply failed");
            players.Clear();
            foreach (var p in playerStore.Values.Values) players.Add(p.id,p);
            foreach(var id in objectStore.Removed)Log("不再接收 "+id);
            foreach(var id in objectStore.Added)Log("開始接收 "+id+" · life "+Objects[id].life);
            Added+=objectStore.Added.Count;Removed+=objectStore.Removed.Count;Updated+=objectStore.Updated.Count;
            LocalPlayerID = own;
            Tick = message.tick;
            Interest=new InterestSummary{mode=info.mode,radius=info.radius,margin=info.margin,centerX=info.centerX,centerZ=info.centerZ,totalPlayers=info.totalPlayers,totalObjects=info.totalObjects,ownedObjects=info.ownedObjects};
            Codec=new CodecSummary{mode=encoding.mode,step=encoding.step,maxError=encoding.maxError,stream=encoding.stream};
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void Log(string value){changes.Insert(0,value);if(changes.Count>8)changes.RemoveAt(changes.Count-1);}
    }
}
