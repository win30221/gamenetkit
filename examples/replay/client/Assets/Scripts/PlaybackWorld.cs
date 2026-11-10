using System;
using System.Collections.Generic;
using Realtime.Playback;

namespace Realtime.Examples.ReplayLesson
{
    [Serializable] public sealed class PlaybackFile
    {
        public int version,tickRate;
        public string application,sourceDigest;
        public long localPlayerID;
        public ServerMessage[] frames;
    }
    public sealed class PlaybackWorld
    {
        public PlaybackClock Clock{get;}=new PlaybackClock();
        public ReplicaState State{get;}=new ReplicaState();
        private ServerMessage[] frames=Array.Empty<ServerMessage>();
        private long localPlayerID;
        private int applied=-1;
        public bool Loaded=>frames.Length>0;
        public int FrameCount=>frames.Length;
        public double Position=>(Clock.Tick-Clock.FirstTick)/30;
        public double Duration=>(Clock.LastTick-Clock.FirstTick)/30.0;
        public void Load(PlaybackFile file)
        {
            if(file==null || file.version!=1 || file.application!="replay-lesson" || file.tickRate!=30 || file.localPlayerID<=0 || file.frames==null || file.frames.Length<1 || file.frames.Length>451 || file.sourceDigest==null || file.sourceDigest.Length!=64)throw new ArgumentException("Invalid playback definition");
            foreach(char c in file.sourceDigest)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))throw new ArgumentException("Invalid source digest");
            var next=new ServerMessage[file.frames.Length];var ticks=new long[next.Length];var validation=new ReplicaState();
            for(int i=0;i<next.Length;i++){
                var source=file.frames[i];
                if(source==null || source.type!="snapshot" || source.protocol!=1 || source.generation!=1 || source.players==null || source.objects==null || source.tick<0 || source.tick>1000000000000L || i>0&&source.tick<=ticks[i-1])throw new ArgumentException("Invalid playback frame");
                ticks[i]=source.tick;
                if(ticks[i]-ticks[0]>900)throw new ArgumentException("Playback exceeds thirty seconds");
                next[i]=Clone(source);
                validation.Apply(i==0?Welcome(next[i],file.localPlayerID):next[i]);
            }
            // Validation is complete before replacing the previously loaded clip.
            Clock.Load(ticks,30);frames=next;localPlayerID=file.localPlayerID;Rebuild();
        }
        public void Play()=>Clock.Play();
        public void Pause()=>Clock.Pause();
        public void Seek(double seconds){Clock.Pause();Clock.Seek(Clock.FirstTick+seconds*30);if(Loaded)Rebuild();}
        public void Advance(double seconds)
        {
            Clock.Advance(seconds);
            while(applied<Clock.FrameIndex){applied++;State.Apply(frames[applied]);}
        }
        private void Rebuild(){State.Clear();applied=Clock.FrameIndex;State.Apply(Welcome(frames[applied],localPlayerID));}
        public bool Sample(long id,out float x,out float z)
        {
            x=z=0;if(!State.Players.TryGetValue(id,out var current))return false;x=current.x;z=current.z;
            if(applied+1>=frames.Length)return true;
            PlayerState next=null;foreach(var p in frames[applied+1].players)if(p.id==id){next=p;break;}
            if(next==null)return true;
            double amount=(Clock.Tick-frames[applied].tick)/(frames[applied+1].tick-frames[applied].tick);
            amount=Math.Max(0,Math.Min(1,amount));x+=(next.x-x)*(float)amount;z+=(next.z-z)*(float)amount;return true;
        }
        public void Clear(){Clock.Clear();State.Clear();frames=Array.Empty<ServerMessage>();applied=-1;localPlayerID=0;}
        private static ServerMessage Welcome(ServerMessage f,long own)=>new ServerMessage{type="welcome",protocol=1,generation=1,tick=f.tick,playerID=own,tickRate=30,snapshotRate=15,players=f.players,objects=f.objects};
        private static ServerMessage Clone(ServerMessage f)
        {
            var players=new PlayerState[f.players.Length];var objects=new WorldObject[f.objects.Length];
            for(int i=0;i<players.Length;i++){var p=f.players[i];if(p==null)throw new ArgumentException("Null player");players[i]=new PlayerState{id=p.id,x=p.x,z=p.z,ack=p.ack,inputTick=p.inputTick};}
            for(int i=0;i<objects.Length;i++){var o=f.objects[i];if(o==null)throw new ArgumentException("Null object");objects[i]=new WorldObject{id=o.id,owner=o.owner,life=o.life,x=o.x,z=o.z};}
            return new ServerMessage{type="snapshot",protocol=f.protocol,generation=f.generation,tick=f.tick,players=players,objects=objects};
        }
    }
}
