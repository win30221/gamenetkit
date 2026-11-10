using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.ReplayLesson;

public static class ReplayTests
{
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Near(double actual,double expected,double tolerance=.0001){Require(Math.Abs(actual-expected)<tolerance,$"expected {expected}, got {actual}");}
    private static ServerMessage Frame(long tick,float x,uint life=0)=>new ServerMessage{type="snapshot",protocol=1,generation=1,tick=tick,players=new[]{new PlayerState{id=1,x=x}},objects=life==0?Array.Empty<WorldObject>():new[]{new WorldObject{id="object-1",owner=1,life=life,x=2,z=1}}};
    private static PlaybackFile Clip()=>new PlaybackFile{version=1,application="replay-lesson",tickRate=30,localPlayerID=1,sourceDigest=new string('a',64),frames=new[]{Frame(10,0),Frame(12,1,1),Frame(14,2,2),Frame(16,3)}};
    private static void ComponentChecks()
    {
        foreach(int fps in new[]{30,60,144}){
            var playback=new PlaybackWorld();var source=Clip();playback.Load(source);source.frames[1].players[0].x=7;
            Require(playback.FrameCount==4&&playback.State.Tick==10&&!playback.Clock.Playing,"load baseline");
            playback.Play();for(int f=0;f<fps;f++)playback.Advance(1.0/fps);
            Require(playback.Clock.Ended&&!playback.Clock.Playing&&playback.State.Tick==16&&playback.State.Objects.Count==0,"end state");
            playback.Seek(1.0/30);playback.Sample(1,out float x,out _);Near(x,.5);Require(!playback.Clock.Playing&&playback.State.Tick==10,"seek must pause/rebuild");
            playback.Seek(2.0/30);Require(playback.State.Objects["object-1"].life==1,"seek first life");
            playback.Seek(4.0/30);Require(playback.State.Objects["object-1"].life==2,"seek rebirth");
            playback.Seek(0);Require(playback.State.Objects.Count==0,"backward seek leaked future objects");
            playback.Clock.Speed=2;playback.Play();playback.Advance(1.0/30);Near(playback.Clock.Tick,12);
            playback.Pause();double paused=playback.Clock.Tick;playback.Advance(5);Near(playback.Clock.Tick,paused);
            var bad=Clip();bad.frames[1].objects=new[]{bad.frames[1].objects[0],bad.frames[1].objects[0]};
            try{playback.Load(bad);throw new Exception("invalid object roster accepted");}catch(InvalidOperationException){}
            Near(playback.Clock.Tick,paused);Require(playback.State.Objects["object-1"].life==1,"invalid load partially replaced old clip");
            bad=Clip();bad.frames[1].tick=10;try{playback.Load(bad);throw new Exception("duplicate ticks accepted");}catch(ArgumentException){}
            playback.Seek(100);Near(playback.Clock.Tick,16);playback.Play();Near(playback.Clock.Tick,10);
            playback.Clear();Require(!playback.Loaded&&playback.State.Objects.Count==0&&playback.Clock.FrameIndex==-1,"playback reset");
            var single=Clip();single.frames=new[]{Frame(0,0)};playback.Load(single);playback.Play();Require(playback.Clock.Ended&&!playback.Clock.Playing,"single frame playback");
            Console.WriteLine($"FPS {fps}: actual PlaybackClock, smooth sampling, pause/speed, seek/respawn/removal, boundaries, immutable/atomic load and clear PASS");
        }
    }
    private static ExampleSession Session()=>new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
    public static async Task Main(string[] args)
    {
        ComponentChecks();using var one=Session();using var two=Session();using var http=new HttpClient{BaseAddress=new Uri(args[0])};
        var clock=Stopwatch.StartNew();float drive=0,otherDrive=0;string endpoint=ExampleEndpoint.Normalize(args[0]);
        one.ConfigureDelay(.05);two.ConfigureDelay(.05);one.Connect(endpoint,0);two.Connect(endpoint,0);
        async Task Until(Func<bool> condition){double end=clock.Elapsed.TotalSeconds+7;while(!condition()){Require(clock.Elapsed.TotalSeconds<end,"timeout "+one.Status+" / "+one.Recording.Status);one.Pump(clock.Elapsed.TotalSeconds,drive,0);two.Pump(clock.Elapsed.TotalSeconds,otherDrive,0);await Task.Delay(8);}}
        await Until(()=>one.Connected&&two.Connected&&one.State.Players.Count==2);
        long identity=one.State.LocalPlayerID;float start=one.State.Players[identity].x;
        one.Record("start");await Until(()=>one.Recording.Status=="recording");string id=one.Recording.ID;
        Require(two.Recording.Status=="idle","recording leaked to another viewer");
        using(var response=await http.GetAsync("/recordings/"+id))Require((int)response.StatusCode==404,"active recording exposed");
        one.Record("start");drive=1;await Until(()=>one.State.Players[identity].x>start+.4);
        one.QueueAction("spawn",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Objects.Count==1&&two.State.Objects.Count==1);
        string objectID=one.State.Objects.Keys.Single();one.QueueAction("rebirth",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Objects[objectID].life==2);
        drive=0;one.QueueAction("remove",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Objects.Count==0&&one.Feedback.Pending==0);
        one.Record("stop");await Until(()=>one.Recording.Status=="ready");Require(one.Recording.ID==id,"duplicate start restarted recording");
        string data=await http.GetStringAsync("/recordings/"+id);var file=JsonSerializer.Deserialize<PlaybackFile>(data,Json);
        Require(file.frames.Length>20&&file.frames.All(f=>f.players.Length==2),"missing full frames or shared players");
        Require(file.frames.Any(f=>f.objects.Any(o=>o.life==1))&&file.frames.Any(f=>f.objects.Any(o=>o.life==2))&&file.frames.Last().objects.Length==0,"recording omitted object lifecycle");
        Require(file.frames.Last().players.Single(p=>p.id==identity).x>file.frames[0].players.Single(p=>p.id==identity).x+.4,"recording omitted motion");
        var replay=new PlaybackWorld();replay.Load(file);replay.Seek(replay.Duration);Require(replay.State.Objects.Count==0,"playback final frame");
        int lifeFrame=Array.FindIndex(file.frames,f=>f.objects.Any(o=>o.life==1));replay.Seek((file.frames[lifeFrame].tick-file.frames[0].tick)/30.0);Require(replay.State.Objects[objectID].life==1,"historical object not rebuilt");
        long liveTick=one.State.Tick;otherDrive=1;replay.Play();
        await Until(()=>{replay.Advance(.01);return one.State.Tick>liveTick+8;});
        Require(one.State.LocalPlayerID==identity&&one.State.Objects.Count==0,"playback altered live replicas");
        two.Record("start");await Until(()=>two.Recording.Status=="recording");Require(two.Recording.ID!=id,"recordings share identity");
        two.Record("stop");await Until(()=>two.Recording.Status=="ready");Require(await http.GetStringAsync("/recordings/"+id)==data,"another recording overwrote completed data");
        int oldEpoch=one.Transport.Epoch;one.Disconnect();Require(one.Recording.Status=="idle"&&one.State.Objects.Count==0,"disconnect leaked recording metadata");
        one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(oldEpoch,"old callback");await Until(()=>one.Connected&&one.State.Players.Count==2);
        Require(one.State.LocalPlayerID>identity&&one.Recording.Status=="idle"&&replay.Loaded,"live reconnect disturbed offline clip or accepted stale callback");
        Console.WriteLine("RF_REPLAY_LESSON PASS: actual Go SDK recording + C# PlaybackClock, two real WS, private recording controls, verified full snapshots/motion/object lives, immutable playback, simultaneous live updates, pause/seek isolation, independent recordings and reconnect cleanup");
    }
}
