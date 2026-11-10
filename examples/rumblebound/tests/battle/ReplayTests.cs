using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;

public static class ReplayTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Replay: "+reason);}
    private static string OriginalDigest(IReadOnlyList<PlaybackFrame> frames)
    {
        using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(frames.Select(f=>f.payload))))).Replace("-","").ToLowerInvariant();
    }
    private static void DigestChecks()
    {
        var cases=new List<string[]>{
            Array.Empty<string>(),new[]{"",null,"中文、é、\ud83d\ude80"},
            new[]{new string('a',1023)+"\ud83d\ude80"+new string('界',2050)},
            new[]{new string('a',1023)+"\ud83d","",null,"\ude80尾"},
            new[]{"\ud800","x\udc00","\ud800","\udc00","\ud800"},
            new[]{new string('界',4097),"\r\n\\\"","\ud83d\ude80"}
        };
        var random=new Random(1729);var alphabet=new[]{'x','中','é','\ud800','\udc00','\ud83d','\ude80','\0'};
        for(int i=0;i<24;i++){
            var payloads=new string[1+random.Next(8)];
            for(int p=0;p<payloads.Length;p++){
                var characters=new char[random.Next(3000)];for(int c=0;c<characters.Length;c++)characters[c]=alphabet[random.Next(alphabet.Length)];payloads[p]=new string(characters);
            }
            cases.Add(payloads);
        }
        foreach(var payloads in cases){
            var frames=payloads.Select(payload=>new PlaybackFrame{payload=payload}).ToArray();string expected=OriginalDigest(frames);
            Require(BattleRecording.PayloadDigest(frames)==expected&&BattleRecording.PayloadDigest(frames)==expected,"incremental UTF-8 digest changed at a Unicode/chunk/payload boundary");
        }
        var large=Enumerable.Range(0,256).Select(_=>new PlaybackFrame{payload=new string('x',4096)}).ToArray();
        // Warm both paths before comparing cumulative allocations in the native
        // .NET test process. This is not a WebGL heap or device timing test.
        BattleRecording.PayloadDigest(cases[0].Select(payload=>new PlaybackFrame{payload=payload}).ToArray());OriginalDigest(Array.Empty<PlaybackFrame>());
        long before=GC.GetAllocatedBytesForCurrentThread();string actual=BattleRecording.PayloadDigest(large);long bounded=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();string original=OriginalDigest(large);long bulk=GC.GetAllocatedBytesForCurrentThread()-before;
        Require(actual==original&&bounded<65536&&bulk>bounded+2*1024*1024,"hash scratch allocations must stay bounded independently of recording size");
        Console.WriteLine("REMAKE_REPLAY_HASH PASS Unicode/surrogate/chunk equivalence; native .NET 1 MiB hash allocations original="+bulk+" bounded="+bounded);
    }
    private sealed class PaddedExportJson:IGameJson
    {
        private readonly IGameJson inner;public PaddedExportJson(IGameJson inner){this.inner=inner;}
        public T Read<T>(string text)=>inner.Read<T>(text);
        public string Write<T>(T value)=>inner.Write(value)+(value is PlaybackClip?new string(' ',BattleRecording.MaxBytes):"");
    }
    private static void RecordingLimits(GameContent content,Snapshot source,IGameJson json)
    {
        var state=json.Read<Snapshot>(json.Write(source));state.tick=state.inputTick=0;state.match.result=null;state.match.phase="playing";
        var duration=new BattleRecording(json);duration.Start(content,state);state.tick=content.Rules.tickRate*3600L;duration.Accept(state);
        Require(duration.Recording&&duration.Frames==2,"exactly one hour must remain recordable");
        state.tick++;duration.Accept(state);Require(!duration.Recording&&duration.Frames==2,"recording exceeded its one-hour limit or discarded accepted frames");

        var capacity=new BattleRecording(json);state.tick=state.inputTick=0;state.code=new string('x',16000);capacity.Start(content,state);
        for(int i=1;i<5000&&capacity.Recording;i++){state.tick=i;capacity.Accept(state);}
        Require(!capacity.Recording&&capacity.Frames>1000&&capacity.Frames<5000,"serialized byte budget did not stop recording");
        int retained=capacity.Frames;state.code="";state.tick++;capacity.Accept(state);Require(capacity.Frames==retained,"stopped recording accepted another snapshot");
        string text=capacity.Export();Require(Encoding.UTF8.GetByteCount(text)<=BattleRecording.MaxBytes,"accepted recording exported past 32 MiB");
        var clip=json.Read<PlaybackClip>(text);Require(clip.frames.Length==retained,"capacity stop lost accepted snapshots");

        // A serializer may add valid trailing JSON whitespace. The final byte
        // check must still enforce the complete exported file's size.
        var oversized=new BattleRecording(new PaddedExportJson(json));state.tick=0;oversized.Start(content,state);
        bool rejected=false;try{oversized.Export();}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"final export size check was bypassed");
        Console.WriteLine("REMAKE_REPLAY_LIMITS PASS one-hour boundary, serialized recording capacity, retained prefix and final 32 MiB export guard");
    }
    private static void ProjectileSerialization(IGameJson json)
    {
        var items=new List<ShotView>(16);var view=new WorldView{projectiles=items};
        items.Add(new ShotView{state=new ProjectileState{id="first"}});items.Add(new ShotView{state=new ProjectileState{id="second"}});
        Require(json.Read<WorldView>(json.Write(view)).projectiles.Count==2,"projectile JSON included unused list capacity");
        items.RemoveAt(0);var one=json.Read<WorldView>(json.Write(view));
        Require(one.projectiles.Count==1&&one.projectiles[0].state.id=="second","projectile JSON retained a removed shot");
        items.Clear();Require(json.Read<WorldView>(json.Write(view)).projectiles.Count==0,"empty projectile JSON retained old list entries");
        view.projectiles=new[]{new ShotView{state=new ProjectileState{id="replay"}}};
        Require(json.Read<WorldView>(json.Write(view)).projectiles[0].state.id=="replay","replay array is incompatible with the sampled projectile list");
    }
    public static void Run(string path)
    {
        DigestChecks();
        var json=new GameJson();var replay=new BattleReplay(json);replay.Load(File.ReadAllText(path));
        if(!replay.Active||replay.Clock.LastTick<=replay.Clock.FirstTick)throw new Exception("Real Go export did not load");
        for(int i=0;i<=20;i++){
            replay.Seek(replay.Clock.FirstTick+(replay.Clock.LastTick-replay.Clock.FirstTick)*i/20.0);
            foreach(var p in replay.View.players)if(double.IsNaN(p.x+p.z)||Math.Abs(p.x)>10||Math.Abs(p.z)>10)throw new Exception("Illegal replay pose");
        }
        replay.Seek(replay.Clock.FirstTick);replay.Clock.Speed=2;replay.Clock.Play();double before=replay.Clock.Tick;replay.Advance(.1);
        if(Math.Abs(replay.Clock.Tick-Math.Min(replay.Clock.LastTick,before+6))>.001)throw new Exception("Replay bypassed SDK clock");
        replay.Clock.Pause();double paused=replay.Clock.Tick;replay.Advance(1);if(replay.Clock.Tick!=paused)throw new Exception("Pause advanced");
        var frames=json.Read<PlaybackClip>(File.ReadAllText(path)).frames;
        replay.Seek(replay.Clock.FirstTick);replay.Clock.Play();replay.Step(1);
        if(replay.Clock.Playing||replay.Clock.Tick!=frames[1].tick)throw new Exception("Frame step did not pause at next recorded frame");
        replay.Step(-1);replay.Step(-1);if(replay.Clock.Tick!=replay.Clock.FirstTick)throw new Exception("Frame step crossed first frame");
        replay.Seek(replay.Clock.LastTick);replay.Step(1);if(replay.Clock.Tick!=replay.Clock.LastTick)throw new Exception("Frame step crossed last frame");
        replay.Seek(paused);
        var content=replay.Content;var snapshot=json.Read<Snapshot>(json.Write(replay.View.snapshot));
        var recorder=new BattleRecording(json);recorder.Start(content,snapshot);snapshot.tick++;recorder.Accept(snapshot);
        snapshot.match.phase="ended";snapshot.match.result=new MatchResult{outcome="win",reason="forfeit",winnerTeamID=snapshot.players[0].teamID,endedTick=snapshot.tick};recorder.Accept(snapshot);recorder.Stop();
        string text=recorder.Export();Require(recorder.Export()==text,"repeated export changed the recording or digest");
        var exported=json.Read<PlaybackClip>(text);Require(exported.sourceDigest==OriginalDigest(exported.frames),"same-tick replacement changed the legacy digest contract");
        var observed=new BattleReplay(json);observed.Load(text);
        if(observed.Content.Config.matchID!=content.Config.matchID||observed.Clock.LastTick-observed.Clock.FirstTick!=1)throw new Exception("Observed clip changed identity/ticks");
        observed.Seek(observed.Clock.LastTick);if(observed.View.snapshot.match.result?.winnerTeamID!=snapshot.match.result.winnerTeamID||recorder.Frames!=2)throw new Exception("Recording lost the terminal result sharing the last tick");
        var invalid=json.Read<PlaybackClip>(text);var state=json.Read<Snapshot>(invalid.frames[0].payload);if(state.players.Length==0)throw new Exception("Fixture needs actors");state.players[0].hp=100000;invalid.frames[0].payload=json.Write(state);
        double retained=observed.Clock.Tick;var retainedContent=observed.Content;
        try{observed.Load(json.Write(invalid));throw new Exception("Invalid HP clip accepted");}catch(InvalidOperationException){}
        if(!ReferenceEquals(retainedContent,observed.Content)||observed.Clock.Tick!=retained)throw new Exception("Invalid clip partially replaced playback");
        recorder.Start(content,replay.View.snapshot);snapshot.generation++;recorder.Accept(snapshot);if(recorder.Recording)throw new Exception("Recording crossed generation");
        ProjectileSerialization(json);RecordingLimits(content,replay.View.snapshot,json);
        Console.WriteLine("REMAKE_REPLAY PASS real Go export, spectator sampling/seeks, SDK speed/pause, observed recording/import, atomic invalid-file rejection and generation stop");
    }
}
