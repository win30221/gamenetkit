using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Realtime.Core;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // Captures validated authority snapshots, including the recipient's AOI and
    // quantization. Server-input journals remain the full-world verification source.
    public sealed class BattleRecording
    {
        public const int MaxBytes=32*1024*1024;
        private readonly IGameJson json;
        private readonly List<PlaybackFrame> frames=new List<PlaybackFrame>();
        private GameContent content;
        private string match;
        private uint generation;
        private int bytes;
        public bool Recording{get;private set;}
        public int Frames=>frames.Count;
        public string Notice{get;private set;}="";
        public BattleRecording(IGameJson json){this.json=json;}
        public void Start(GameContent definition,Snapshot initial)
        {
            if(definition==null||initial==null)throw new InvalidOperationException("Connect before recording");
            frames.Clear();content=definition;match=initial.matchID;generation=initial.generation;bytes=Encoding.UTF8.GetByteCount(json.Write(content.Config));Notice="";Recording=true;Accept(initial);
        }
        public void Accept(Snapshot state)
        {
            if(!Recording)return;
            if(state.matchID!=match||state.generation!=generation){Stop();Notice="連線世代改變，已停止錄製。";return;}
            if(frames.Count>0&&state.tick<frames[frames.Count-1].tick)return;
            // A validated start/result may commit at the last snapshot's tick.
            // Replace that frame so the playback clock still has unique ticks.
            bool replace=frames.Count>0&&state.tick==frames[frames.Count-1].tick;
            int previousBytes=replace?Encoding.UTF8.GetByteCount(json.Write(frames[frames.Count-1])):0;
            var copy=json.Read<Snapshot>(json.Write(state));copy.type="snapshot";copy.generation=1;copy.playerID="";copy.actionAck=0;copy.actionCode="";copy.tech=null;
            string payload=json.Write(copy);int size=Encoding.UTF8.GetByteCount(json.Write(new PlaybackFrame{tick=copy.tick,payload=payload}));
            if(!replace&&frames.Count>=120000||bytes-previousBytes+size>MaxBytes-65536||frames.Count>0&&copy.tick-frames[0].tick>content.Rules.tickRate*3600L){Stop();Notice="已達錄製容量，已停止。";return;}
            var frame=new PlaybackFrame{tick=copy.tick,payload=payload};if(replace)frames[frames.Count-1]=frame;else frames.Add(frame);bytes=bytes-previousBytes+size;
        }
        public void Stop(){Recording=false;}
        internal static string PayloadDigest(IReadOnlyList<PlaybackFrame> source)
        {
            // Keep one encoder across frames: a surrogate pair can straddle a
            // chunk or payload boundary in the same UTF-8 stream as Concat.
            var characters=new char[1024];var buffer=new byte[Encoding.UTF8.GetMaxByteCount(characters.Length)];
            var encoder=Encoding.UTF8.GetEncoder();
            using(var hash=SHA256.Create()){
                for(int i=0;i<source.Count;i++){
                    string payload=source[i].payload;if(string.IsNullOrEmpty(payload))continue;
                    for(int offset=0;offset<payload.Length;){
                        int count=Math.Min(characters.Length,payload.Length-offset);payload.CopyTo(offset,characters,0,count);offset+=count;
                        int written=encoder.GetBytes(characters,0,count,buffer,0,false);
                        if(written>0)hash.TransformBlock(buffer,0,written,null,0);
                    }
                }
                int tail=encoder.GetBytes(Array.Empty<char>(),0,0,buffer,0,true);hash.TransformFinalBlock(buffer,0,tail);
                return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
            }
        }
        public string Export()
        {
            if(frames.Count==0)throw new InvalidOperationException("No recording");
            var config=json.Read<LabConfig>(json.Write(content.Config));config.matchID=match;
            string digest=PayloadDigest(frames);
            string result=json.Write(new PlaybackClip{version=1,model="rumblebound-view-v1",tickRate=content.Rules.tickRate,protocol=content.Rules.protocol,content=content.Rules.content,mapHash=content.Config.mapHash,matchID=match,config=json.Write(config),sourceDigest=digest,frames=frames.ToArray()});
            if(Encoding.UTF8.GetByteCount(result)>MaxBytes)throw new InvalidOperationException("Recording export limit");return result;
        }
    }
    public sealed class BattleReplay
    {
        private sealed class Timeline:IPresentationTimeline
        {
            private readonly IPlaybackClock clock;public Timeline(IPlaybackClock clock){this.clock=clock;}
            public string Mode=>"playback";public bool Interpolates=>true;public double RenderTick=>clock.Tick;public TimelineMetrics Metrics=>default;
            public void Reset(long tick,double now){}public void Observe(long tick,double now){}public void Advance(double now){}
        }
        private readonly IGameJson json;
        private PlaybackClip clip;
        private GameMovement movement;
        private int applied=-1;
        private long revision;
        public IPlaybackClock Clock{get;private set;}
        public GameContent Content{get;private set;}
        public bool Active=>clip!=null;
        public WorldView View{get;private set;}=new WorldView();
        public BattleReplay(IGameJson json){this.json=json;}
        public void Load(string text)
        {
            if(text==null||Encoding.UTF8.GetByteCount(text)>BattleRecording.MaxBytes)throw new InvalidOperationException("Playback size limit");
            if(BattleNetcode.PlaybackFactory==null)throw new InvalidOperationException("Playback module is unavailable");
            var next=json.Read<PlaybackClip>(text);
            if(next==null||next.version!=1||next.model!="rumblebound-sim-v1"&&next.model!="rumblebound-view-v1"||next.frames==null||next.frames.Length==0||next.frames.Length>120000||next.sourceDigest==null||next.sourceDigest.Length!=64||next.sourceDigest.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidOperationException("Invalid playback format");
            var definition=new GameContent(json.Read<LabConfig>(next.config),json);
            if(next.protocol!=definition.Rules.protocol||next.content!=definition.Rules.content||next.tickRate!=definition.Rules.tickRate||next.mapHash!=definition.Config.mapHash||next.matchID!=definition.Config.matchID)throw new InvalidOperationException("Playback configuration mismatch");
            var ticks=new long[next.frames.Length];
            for(int i=0;i<ticks.Length;i++){
                var frame=next.frames[i];if(frame==null||frame.payload==null||Encoding.UTF8.GetByteCount(frame.payload)>32768)throw new InvalidOperationException("Playback frame limit");
                var state=json.Read<Snapshot>(frame.payload);
                if(state==null||state.type!="snapshot"||state.tick!=frame.tick||state.generation!=1)throw new InvalidOperationException("Invalid playback snapshot");
                BattleState.Validate(state,definition,next.matchID);ticks[i]=frame.tick;
            }
            var clock=BattleNetcode.PlaybackFactory();clock.Load(ticks,next.tickRate);
            // Commit only after every frame validates. A rejected file preserves
            // the current clip, clock and view.
            clip=next;Content=definition;Clock=clock;Rebuild();Clock.Play();
        }
        private void Rebuild(){revision++;applied=-1;movement=new GameMovement(Content.Rules,Content.Map,new Timeline(Clock),null,new SnapCorrection(),false);Update();}
        public void Seek(double tick){if(!Active)return;Clock.Seek(tick);Rebuild();}
        public void Step(int direction){if(!Active)return;Clock.Pause();Seek(clip.frames[Math.Max(0,Math.Min(clip.frames.Length-1,Clock.FrameIndex+direction))].tick);}
        public void Advance(double seconds){if(!Active)return;Clock.Advance(seconds);Update();}
        private void Update()
        {
            int index=Clock.FrameIndex,end=Math.Min(clip.frames.Length-1,index+1);
            if(applied<0)applied=Math.Max(-1,index-4);
            while(applied<end){var state=json.Read<Snapshot>(clip.frames[++applied].payload);movement.Accept(state,"",state.tick/(double)clip.tickRate);}
            movement.Advance(Clock.Tick/clip.tickRate,0,0);
            var latest=json.Read<Snapshot>(clip.frames[index].payload);
            var actors=latest.players.Select(p=>{movement.Position(p,false,out double x,out double z);return new ActorView{state=p,x=x,z=z};}).ToArray();
            var shots=(latest.projectiles??Array.Empty<ProjectileState>()).Select(p=>{movement.ProjectilePosition(p,out double x,out double z);return new ShotView{state=p,x=x,z=z};}).ToArray();
            View=new WorldView{generation=(uint)revision,tick=latest.tick,renderTick=Clock.Tick,players=actors,projectiles=shots,snapshot=latest};
        }
        public GameMovement Movement=>movement;
        public void Clear(){clip=null;Content=null;Clock?.Clear();Clock=null;movement=null;View=new WorldView();applied=-1;}
    }
}
