using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.HistoryLesson;

public static class HistoryTests
{
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static ServerMessage Frame(long tick,uint life=1,float x=-5)=>new ServerMessage{type=tick==0?"welcome":"snapshot",protocol=1,generation=1,tick=tick,playerID=1,tickRate=30,snapshotRate=15,players=new[]{new PlayerState{id=1,z=-5}},objects=Array.Empty<WorldObject>(),target=new TargetState{life=life,hp=5,x=x,z=3},history=new HistoryInfo{first=0,last=tick,count=(int)(tick/2+1),maxRewind=15}};
    private static void Components()
    {
        var state=new ReplicaState();var frame=Frame(0);state.Apply(frame);frame.target.x=99;frame.history.first=99;Require(state.Target.x== -5&&state.History.first==0,"snapshot did not clone target/history");
        var bad=Frame(2,1,-4.6f);bad.players=new[]{bad.players[0],bad.players[0]};try{state.Apply(bad);throw new Exception("bad roster accepted");}catch(InvalidOperationException){}Require(state.Tick==0&&state.Target.x== -5,"invalid frame changed target");
        state.Apply(Frame(2,1,-4.6f));state.Apply(Frame(4,2,-5));Require(state.Target.life==2,"new target life");
        bad=Frame(6,1);try{state.Apply(bad);throw new Exception("old life accepted");}catch(InvalidOperationException){}
        var track=new TargetTrack();track.Accept(Frame(0));track.Accept(Frame(2,1,-4.6f));Require(track.Sample(1,out var sample,out long tick)&&tick==1&&Math.Abs(sample.x+4.8)<.0001,"target pose sampling");
        track.Accept(Frame(4,2,-5));Require(track.Sample(1,out sample,out tick)&&tick==4&&sample.life==2&&sample.x== -5,"render tick crossed new-life baseline");
        track.Clear();state.Clear();Require(state.Target==null&&state.History==null&&!track.Sample(0,out _,out _),"target/history reset");
        Console.WriteLine("C# target replica/track: clones, atomic roster validation, life/reset and sampled tick clamping PASS");
    }
    private static ExampleSession Session()=>new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
    public static async Task<int> Main(string[] args){try{await Run(args);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    private static async Task Run(string[] args)
    {
        Components();using var one=Session();using var two=Session();var clock=Stopwatch.StartNew();string endpoint=ExampleEndpoint.Normalize(args[0]);
        one.ConfigureDelay(.2);two.ConfigureDelay(0);one.Remote.SetMode("latest",0);one.Connect(endpoint,0);two.Connect(endpoint,0);one.Inspect(true,.5);
        async Task Until(Func<bool> condition,[CallerLineNumber]int line=0){double end=clock.Elapsed.TotalSeconds+9;while(!condition()){Require(clock.Elapsed.TotalSeconds<end,$"timeout line {line}: {one.Status}, tick={one.State.Tick}, target={one.State.Target?.x}, life={one.State.Target?.life}, pending={one.Feedback.Pending}, last={one.LastShot?.reason}");one.Pump(clock.Elapsed.TotalSeconds,0,0);two.Pump(clock.Elapsed.TotalSeconds,0,0);await Task.Delay(8);}}
        await Until(()=>one.Connected&&two.Connected&&one.State.Players.Count==2&&one.State.Tick>35&&one.Preview?.found==true);
        Require(one.Preview.tick<one.State.Tick&&one.State.Target.hp==5&&two.State.Target.hp==5,"history lookup did not stay read-only");
        await Until(()=>Math.Abs(one.State.Target.x)<2);long first=one.Fire(false,clock.Elapsed.TotalSeconds);Require(first>0,"shot not queued");await Until(()=>one.LastShot?.id==first);
        Require(one.LastShot.accepted&&!one.LastShot.hit&&!one.LastShot.currentHit&&one.LastShot.historicalHit&&two.State.Target.hp==5,"current-only shot should miss visible delayed target");
        await Until(()=>Math.Abs(one.State.Target.x)<2&&one.Feedback.Pending==0);long second=one.Fire(true,clock.Elapsed.TotalSeconds);await Until(()=>one.LastShot?.id==second&&two.State.Target.hp==4);
        Require(one.LastShot.accepted&&one.LastShot.hit&&one.LastShot.historicalHit&&one.Hits==1,"compensated shot should hit and change authority HP");
        long old=one.Fire(true,clock.Elapsed.TotalSeconds,true);await Until(()=>one.LastShot?.id==old);Require(!one.LastShot.accepted&&one.LastShot.reason=="outside_window"&&one.RejectedShots==1&&two.State.Target.hp==4,"expired shot changed live state");
        uint life=one.State.Target.life;one.ResetTarget();await Until(()=>one.State.Target.life>life&&two.State.Target.life>life);Require(one.State.Target.hp==5&&(!one.Preview?.found??true||one.Preview.target.life==one.State.Target.life),"reset leaked old life/history");
        one.Inspect(true,3);await Until(()=>one.Preview!=null);Require(one.State.Target.hp==5,"old history inspection changed HP");one.Inspect(false,0);Require(one.Preview==null,"inspection disable kept ghost");
        int epoch=one.Transport.Epoch;long identity=one.State.LocalPlayerID;one.Disconnect();Require(one.State.Target==null&&one.LastShot==null&&one.Preview==null,"disconnect state leak");
        await Until(()=>two.State.Players.Count==1);one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(epoch,"old callback");await Until(()=>one.Connected&&one.State.Players.Count==2);Require(one.State.LocalPlayerID>identity,"reconnect identity");
        Console.WriteLine("RF_HISTORY PASS: actual SDK History/Bounded + C# session, two WS, read-only historical lookup, delayed current miss vs compensated hit, live HP only, expired-window rejection, life/history reset, render baseline and reconnect isolation");
    }
}
