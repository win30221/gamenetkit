using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.ObjectsActions;

public static class ObjectsTests
{
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Near(double value,double expected,double tolerance=.0001){Require(Math.Abs(value-expected)<tolerance,$"expected {expected}, got {value}");}
    private static PlayerState Player(float x,long ack=0,long inputTick=0)=>new PlayerState {id=1,x=x,ack=ack,inputTick=inputTick};
    private static void UnitChecks()
    {
        foreach(int fps in new[]{30,60,144}) {
            var own=new PredictedPlayer();own.Reconcile(Player(0),0,0,true);own.Advance(0,new Drive(1,0));own.SentInput(1,0);
            for(int f=1;f<=fps/6;f++)own.Advance(f/(double)fps,new Drive(1,0));
            Near(own.PredictedX,2.0/3);double before=own.X;
            own.Reconcile(Player(.3f,1,1),3,1.0/6,false);
            Near(own.PredictedX,.3+4.0/15);Near(own.X,before);Require(own.Offset>.09,"smooth correction lost display position");
            double offset=own.Offset;own.Advance(.2,new Drive(0,0));Require(own.Offset<offset,"offset failed to decay");
            own.SetCorrection("snap");own.Reconcile(Player(-1,1,1),4,.2,false);Near(own.Offset,0);Near(own.X,own.PredictedX);
            own.SetMode("authority",.2);own.Advance(.25,new Drive(1,0));Near(own.X,-1);
            own.SetMode("prediction",.25);own.Advance(.25,new Drive(1,0));own.Advance(.3,new Drive(1,0));Require(own.X> -1,"mode did not enable prediction");
            double stopped=own.PredictedX;own.Advance(10,new Drive(1,0));Near(own.PredictedX,stopped); // Snapshot timeout.
            own.Clear();Require(!own.Ready && own.Offset==0 && own.Corrections==0,"reset leaked state");
            Console.WriteLine($"FPS {fps}: immediate motion, ack replay, display-only smoothing, snap, mode switch and timeout PASS");
        }
        var model=new MotionModel();var state=new MotionState();model.Step(state,new Drive(1,1),1,0);Near(Math.Sqrt(state.X*state.X+state.Z*state.Z),4);
        state.X=7.9;model.Step(state,new Drive(1,0),1,0);Near(state.X,8);
        var player=new PredictedPlayer();player.Reconcile(Player(0),0,0,true);player.Advance(0,new Drive(1,0));
        for(int i=1;i<=6;i++)player.Advance(i/30.0,new Drive(1,0));
        double moving=player.PredictedX;
        for(int i=7;i<=10;i++)player.Advance(i/30.0,new Drive(0,0));
        Near(player.PredictedX,moving);
        player.Advance(11/30.0,new Drive(-1,0));Require(player.PredictedX<moving,"reverse did not respond");
        player.SetCorrection("smooth");player.Reconcile(Player(-5),12,.4,false);Near(player.Offset,0); // Large errors snap.
    }
    private static void ComponentChecks()
    {
        var state=new ReplicaState();
        ServerMessage Snapshot(long tick,uint life=1)=>new ServerMessage{type=tick==0?"welcome":"snapshot",protocol=1,generation=1,tick=tick,playerID=1,tickRate=30,snapshotRate=15,players=new[]{Player(0)},objects=new[]{new WorldObject{id="object-1",owner=1,life=life,x=1,z=1}}};
        var first=Snapshot(0);state.Apply(first);first.objects[0].x=7;Near(state.Objects["object-1"].x,1);
        state.Apply(Snapshot(1,2));Require(state.Added==2 && state.Removed==1 && state.Objects["object-1"].life==2,"rebirth must remove/add same ID");
        var bad=Snapshot(2);bad.players[0].x=5;bad.objects=new[]{bad.objects[0],bad.objects[0]};
        try{state.Apply(bad);throw new Exception("duplicate accepted");}catch(InvalidOperationException){}
        Require(state.Tick==1 && state.Objects["object-1"].life==2,"invalid roster partially mutated replicas");Near(state.Players[1].x,0);
        var empty=Snapshot(2);empty.objects=Array.Empty<WorldObject>();state.Apply(empty);Require(state.Objects.Count==0 && state.Removed==2,"complete roster omission did not remove");
        Require(!state.Apply(Snapshot(1)),"stale snapshot applied");state.Clear();Require(state.Added==0 && state.Players.Count==0,"replica clear");
        var feedback=new ActionFeedback();
        ServerMessage Result(long id,bool accepted)=>new ServerMessage{id=id,kind="spawn",accepted=accepted,x=1,z=2};
        feedback.Begin(1,"spawn",true,0,0,0);Require(feedback.Rows[0].visible,"missing provisional effect");
        feedback.Resolve(Result(1,true));Require(!feedback.Resolve(Result(1,true)) && feedback.Confirmed==1 && feedback.Rows[0].starts==1,"duplicate confirmation replayed effect");
        feedback.Begin(2,"spawn",false,0,0,.1);Require(!feedback.Rows[1].visible,"waiting mode presented early");feedback.Resolve(Result(2,true));Require(feedback.Rows[1].starts==1,"waiting mode failed to present confirmation");
        feedback.Begin(3,"spawn",true,0,0,.2);feedback.Resolve(Result(3,false));Require(feedback.Rows[2].phase=="rejected" && !feedback.Rows[2].visible && feedback.Rejected==1,"rejection did not withdraw provisional effect");
        feedback.Begin(4,"spawn",true,0,0,.3);feedback.Advance(1.8);Require(feedback.Rows[3].phase=="expired" && !feedback.Rows[3].visible && feedback.Pending==0,"expiry");
        Require(feedback.Resolve(Result(4,true)) && feedback.Rows[3].starts==1 && feedback.Rows[3].visible,"late confirmation duplicated or disappeared");
        Require(!feedback.Resolve(Result(99,true)),"unknown confirmation accepted");feedback.Advance(5);Require(feedback.Rows.Count==0 && !feedback.Resolve(Result(4,true)),"retention not bounded");
        feedback.Clear();Require(feedback.Confirmed==0 && feedback.Rows.Count==0,"action clear");
        Console.WriteLine("Actual ReplicaStore/ActionPresentation: atomic full rosters, clones, rebirth, stale/clear, waiting/provisional, duplicate/late/unknown results and retention PASS");
    }
    public static async Task Main(string[] args)
    {
        UnitChecks();
        ComponentChecks();
        var json=new JsonSerializerOptions {IncludeFields=true};
        using var one=new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,json),i=>JsonSerializer.Serialize(i,json));
        using var two=new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,json),i=>JsonSerializer.Serialize(i,json));
        var clock=Stopwatch.StartNew();string endpoint=ExampleEndpoint.Normalize(args[0]);float drive=0;
        one.ConfigureDelay(.2);two.ConfigureDelay(.2);one.Own.SetCorrection("snap");
        one.Connect(endpoint,0);two.Connect(endpoint,0);
        async Task Until(Func<bool> condition) {
            double deadline=clock.Elapsed.TotalSeconds+6;
            while(!condition()) {
                Require(clock.Elapsed.TotalSeconds<deadline,"timeout "+one.Status+" / "+two.Status);
                one.Pump(clock.Elapsed.TotalSeconds,drive,0);two.Pump(clock.Elapsed.TotalSeconds,0,0);await Task.Delay(8);
            }
        }
        await Until(()=>one.State.Players.Count==2 && two.State.Players.Count==2);
        long id=one.State.LocalPlayerID,other=two.State.LocalPlayerID;
        double start=one.Own.X;float auth=one.State.Players[id].x,otherX=two.State.Players[other].x;
        drive=1;await Until(()=>one.Own.PredictedX>start+.1);
        Near(one.State.Players[id].x,auth); // Predicted input is visible before the delayed response.
        await Until(()=>one.Own.Metrics.ReplayedSteps>0 && one.State.Players[id].x>auth+.6f);
        Require(one.State.Players[id].inputTick>0,"server omitted applied-input tick");
        drive=0;double stop=clock.Elapsed.TotalSeconds;await Until(()=>clock.Elapsed.TotalSeconds>stop+.55);
        float settled=one.State.Players[id].x;long tick=one.State.Tick;
        await Until(()=>one.State.Tick>tick+6);Near(one.State.Players[id].x,settled);
        one.QueueBump();await Until(()=>one.Own.LastError>.8);Near(one.Own.Offset,0);
        one.Own.SetCorrection("smooth");one.QueueBump();await Until(()=>one.Own.LastError>.8 && one.Own.Offset>.6);
        double peak=one.Own.Offset,when=clock.Elapsed.TotalSeconds;await Until(()=>clock.Elapsed.TotalSeconds>when+.2);
        Require(one.Own.Offset<peak/2,"live correction did not settle");
        Near(two.State.Players[other].x,otherX);
        long action=one.QueueAction("spawn",false,clock.Elapsed.TotalSeconds);
        Require(action>0 && !one.Feedback.Rows.Last().visible,"waiting mode");
        await Until(()=>one.Feedback.Confirmed==1 && one.State.Objects.Count==1 && two.State.Objects.Count==1);
        string objectID=one.State.Objects.Keys.Single();Require(one.Feedback.Rows.Last().starts==1,"accepted effect");
        one.ResendLast();double sent=clock.Elapsed.TotalSeconds;await Until(()=>clock.Elapsed.TotalSeconds>sent+.35);
        Require(one.State.Objects.Count==1 && one.Feedback.Confirmed==1 && one.Feedback.Rows.Last().starts==1,"duplicate action executed/presented again");
        one.QueueAction("rebirth",false,clock.Elapsed.TotalSeconds);
        await Until(()=>one.State.Objects[objectID].life==2 && two.State.Objects[objectID].life==2);
        Require(one.State.Added==2 && one.State.Removed==1,"live SDK rebirth");
        one.QueueAction("deny",true,clock.Elapsed.TotalSeconds);Require(one.Feedback.Rows.Last().visible,"live provisional effect");
        await Until(()=>one.Feedback.Rejected==1);Require(one.State.Objects.Count==1,"denied action changed world");
        one.QueueAction("timeout",true,clock.Elapsed.TotalSeconds);
        await Until(()=>one.Feedback.Expired==1);Require(one.Feedback.Rows.Last().phase=="expired" && !one.Feedback.Rows.Last().visible,"live timeout not withdrawn");
        one.QueueAction("remove",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Objects.Count==0 && two.State.Objects.Count==0);
        one.QueueAction("spawn",false,clock.Elapsed.TotalSeconds);await Until(()=>two.State.Objects.Count==1);
        int old=one.Transport.Epoch;one.Disconnect();Require(!one.Own.Ready && one.Own.Offset==0 && one.Remote.BufferedFrames==0,"disconnect leaked prediction or remote snapshots");
        Require(one.State.Objects.Count==0 && one.Feedback.Rows.Count==0,"disconnect leaked actions/objects");
        await Until(()=>two.State.Players.Count==1 && two.State.Objects.Count==0);one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(old,"old data");
        await Until(()=>one.State.Players.Count==2 && two.State.Players.Count==2);
        Require(one.State.LocalPlayerID>Math.Max(id,other),"reconnect reused identity");
        Console.WriteLine("RF_OBJECTS_ACTIONS PASS: actual SDK replicas/actions, real delayed WS, shared spawn/remove/rebirth, dedup, rejection/expiry, disconnect cleanup/old epoch; prediction/correction at 30/60/144 FPS retained");
    }
}
