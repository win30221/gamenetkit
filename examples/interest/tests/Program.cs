using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.InterestLesson;

public static class InterestTests
{
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(double actual,double expected,double tolerance=.0001){Require(Math.Abs(actual-expected)<tolerance,$"expected {expected}, got {actual}");}
    private static WorldObject Global()=>new WorldObject{id="landmark-hub",kind="global",life=1,z=7};
    private static ServerMessage Frame(long tick,bool marker=true,uint life=1)=>new ServerMessage{type=tick==0?"welcome":"snapshot",protocol=1,generation=1,tick=tick,playerID=1,tickRate=30,snapshotRate=15,players=new[]{new PlayerState{id=1}},objects=marker?new[]{Global(),new WorldObject{id="object-1",kind="marker",owner=2,life=life,x=1}}:new[]{Global()},interest=new InterestSummary{mode="aoi",radius=3,margin=.75f,totalPlayers=2,totalObjects=14}};
    private static void Components()
    {
        var state=new ReplicaState();var envelope=Frame(0);state.Apply(envelope);envelope.objects[1].life=99;envelope.interest.radius=6;
        Require(state.Objects["object-1"].life==1&&state.Interest.radius==3&&state.Players.Count==1,"received state must own clones and allow AOI-hidden owner");
        state.Apply(Frame(2,false));Require(state.Objects.Count==1&&state.Interest.totalObjects==14,"AOI departure changed authority totals");
        state.Apply(Frame(4));Require(state.Objects["object-1"].life==1&&state.Added==3&&state.Removed==1,"reentry lost same ID/life");
        state.Apply(Frame(6,true,2));Require(state.Objects["object-1"].life==2&&state.Added==4&&state.Removed==2,"new life not replaced");
        var bad=Frame(8);bad.objects=new[]{Global(),bad.objects[1],bad.objects[1]};
        try{state.Apply(bad);throw new Exception("duplicate accepted");}catch(InvalidOperationException){}
        Require(state.Tick==6&&state.Objects["object-1"].life==2,"invalid AOI snapshot partially mutated state");
        bad=Frame(8);bad.interest.centerX=7;try{state.Apply(bad);throw new Exception("unmatched center accepted");}catch(InvalidOperationException){}
        bad=Frame(8);bad.objects=Array.Empty<WorldObject>();try{state.Apply(bad);throw new Exception("required global missing");}catch(InvalidOperationException){}
        state.Clear();Require(state.Interest==null&&state.Players.Count==0,"scope reset");
        foreach(string mode in new[]{"latest","fixed","adaptive"}){
            var remote=new RemotePresentation();remote.SetMode(mode,0);
            remote.Accept(new ServerMessage{type="welcome",tick=0,players=new[]{new PlayerState{id=1},new PlayerState{id=2,x=0}}},0);
            remote.Accept(new ServerMessage{type="snapshot",tick=2,players=new[]{new PlayerState{id=1}}},.067);remote.Advance(.067);
            Require(!remote.Sample(2,out _,out _),"departed player resurrected");
            remote.Accept(new ServerMessage{type="snapshot",tick=4,players=new[]{new PlayerState{id=1},new PlayerState{id=2,x=7}}},.134);remote.Advance(.135);
            Require(remote.Sample(2,out float x,out _),"reentered player missing");Near(x,7);
            remote.Clear();Require(remote.BufferedFrames==0,"old AOI history leaked");
        }
        foreach(int fps in new[]{30,60,144}){
            var own=new PredictedPlayer();own.Reconcile(new PlayerState{id=1},0,0,true);own.Advance(0,new Drive(1,0));own.SentInput(1,0);
            for(int f=1;f<=fps/6;f++)own.Advance(f/(double)fps,new Drive(1,0));Near(own.PredictedX,2.0/3);
            Console.WriteLine($"FPS {fps}: retained local prediction PASS");
        }
        Console.WriteLine("Actual ReplicaStore and remote timeline: projected roster/hidden owner, cloned scope, disappearance/reentry/same life/new life, atomic invalid state and fresh latest/fixed/adaptive reentry baseline PASS");
    }
    private static ExampleSession Session()=>new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
    public static async Task<int> Main(string[] args)
    {
        try{await Run(args);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static async Task Run(string[] args)
    {
        Components();using var one=Session();using var two=Session();var clock=Stopwatch.StartNew();float drive=0;string endpoint=ExampleEndpoint.Normalize(args[0]);
        one.Connect(endpoint,0);
        async Task Until(Func<bool> condition,[System.Runtime.CompilerServices.CallerArgumentExpression("condition")]string stage=""){double end=clock.Elapsed.TotalSeconds+7;while(!condition()){Require(clock.Elapsed.TotalSeconds<end,"timeout "+stage+": "+one.Status+" / "+two.Status);one.Pump(clock.Elapsed.TotalSeconds,drive,0);two.Pump(clock.Elapsed.TotalSeconds,0,0);await Task.Delay(8);}}
        // Geometry assertions below require the first session in the left spawn.
        // Await its welcome rather than relying on concurrent handshakes winning in order.
        await Until(()=>one.Connected);two.Connect(endpoint,clock.Elapsed.TotalSeconds);
        await Until(()=>one.State.Players.Count==2&&two.State.Players.Count==2&&one.SnapshotBytes>0);
        long own=one.State.LocalPlayerID,other=two.State.LocalPlayerID;int allBytes=one.SnapshotBytes;
        Require(one.State.Objects.Count==13&&two.State.Objects.Count==13,"initial full world");
        one.SetInterest("aoi",3);await Until(()=>one.State.Interest.mode=="aoi"&&one.State.Players.Count==1);
        Require(one.State.Objects.Count<13&&one.State.Objects.ContainsKey("landmark-hub")&&!one.State.Objects.ContainsKey("landmark-12")&&one.State.Interest.totalObjects==13&&one.SnapshotBytes<allBytes,"far payload not filtered");
        Require(two.State.Players.Count==2&&two.State.Objects.Count==13&&two.State.Interest.mode=="all","recipient selection leaked");
        one.SetInterest("aoi",5);await Until(()=>one.State.Interest.radius==5&&one.State.Players.ContainsKey(other));
        one.SetInterest("aoi",3);await Until(()=>one.State.Interest.radius==3&&!one.State.Players.ContainsKey(other));Require(!one.Remote.Sample(other,out _,out _),"removed remote pose persisted");
        drive=1;await Until(()=>one.State.Players[own].x> -1&&one.State.Players.ContainsKey(other));drive=0;
        Near(one.State.Interest.centerX,one.State.Players[own].x);
        one.QueueAction("spawn",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Interest.ownedObjects==1&&one.State.Objects.Values.Any(o=>o.kind=="marker"));
        string marker=one.State.Objects.Values.Single(o=>o.kind=="marker").id;
        drive=-1;await Until(()=>one.State.Players[own].x< -6&&!one.State.Objects.ContainsKey(marker));drive=0;
        Require(one.State.Interest.ownedObjects==1&&one.State.Interest.totalObjects==14&&two.State.Objects.ContainsKey(marker),"AOI removed authoritative object");
        one.QueueAction("rebirth",false,clock.Elapsed.TotalSeconds);await Until(()=>two.State.Objects[marker].life==2);Require(!one.State.Objects.ContainsKey(marker),"hidden object data leaked with action");
        drive=1;await Until(()=>one.State.Objects.ContainsKey(marker));drive=0;Require(one.State.Objects[marker].life==2,"reentry used stale object life");
        one.QueueAction("remove",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Interest.ownedObjects==0&&two.State.Objects.Count==13);
        one.SetInterest("all",3);await Until(()=>one.State.Interest.mode=="all"&&one.State.Objects.Count==13&&one.State.Players.Count==2);
        one.SetInterest("aoi",3);await Until(()=>one.State.Interest.mode=="aoi");
        int epoch=one.Transport.Epoch;one.Disconnect();Require(one.State.Interest==null&&one.Remote.BufferedFrames==0&&one.SnapshotBytes==0,"disconnect scope leak");
        await Until(()=>two.State.Players.Count==1);one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(epoch,"old callback");
        await Until(()=>one.Connected&&one.State.Interest.mode=="aoi"&&two.State.Players.Count==2);Require(one.State.LocalPlayerID>Math.Max(own,other),"reconnect identity");
        Console.WriteLine("RF_INTEREST PASS: actual Go Radius + C# replicas/timelines, two real WS, independent all/AOI, JSON payload reduction, own/global required, radius changes, player and marker leave/reentry/current life, authoritative counts/removal, retained prediction and reconnect reset");
    }
}
