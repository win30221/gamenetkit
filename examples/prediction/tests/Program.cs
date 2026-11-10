using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.PredictionLesson;

public static class PredictionTests
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
    public static async Task Main(string[] args)
    {
        UnitChecks();
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
        int old=one.Transport.Epoch;one.Disconnect();Require(!one.Own.Ready && one.Own.Offset==0 && one.Remote.BufferedFrames==0,"disconnect leaked prediction or remote snapshots");
        await Until(()=>two.State.Players.Count==1);one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(old,"old data");
        await Until(()=>one.State.Players.Count==2 && two.State.Players.Count==2);
        Require(one.State.LocalPlayerID>Math.Max(id,other),"reconnect reused identity");
        Console.WriteLine("RF_PREDICTION PASS: actual SDK prediction/correction, FPS/ack/timeout/bounds, real delayed WS, immediate input, own authority bumps, snap/smooth settling, reset/old epoch");
    }
}
