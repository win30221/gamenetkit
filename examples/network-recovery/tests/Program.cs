using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.NetworkRecovery;
using Realtime.Core;

public static class NetworkTests
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
        RecoveryChecks();
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
        await Until(()=>one.Telemetry.Metrics.Read(ClientMetric.ConnectionRttMS,clock.Elapsed.TotalSeconds).Count>0);
        Require(one.Telemetry.Metrics.ReadTraffic(clock.Elapsed.TotalSeconds).SentBytes>0,"traffic not measured");
        drive=.2f;one.Link.PauseSnapshots(clock.Elapsed.TotalSeconds,2);
        await Until(()=>one.Telemetry.SnapshotAge(clock.Elapsed.TotalSeconds)>1.1);
        double frozen=one.Own.PredictedX,wait=clock.Elapsed.TotalSeconds;
        await Until(()=>clock.Elapsed.TotalSeconds>wait+.3);Near(one.Own.PredictedX,frozen);
        Require(one.Connected&&!one.Recovery.Pending,"snapshot pause triggered reconnect");
        await Until(()=>one.Telemetry.SnapshotAge(clock.Elapsed.TotalSeconds)<.3);
        drive=0;long beforeRecovery=one.State.LocalPlayerID;
        int oldAttempt=one.Transport.Epoch;one.SimulateOutage(clock.Elapsed.TotalSeconds,.2);
        Require(one.Recovery.Pending&&!one.Connected,"outage not scheduled");
        one.Transport.Receive(oldAttempt,"old data");
        await Until(()=>one.Connected&&one.State.LocalPlayerID>beforeRecovery);
        Require(one.Recovery.Rejoins>0&&one.Recovery.Events.Length<=16,"recovery history not bounded or missing");
        one.SimulateOutage(clock.Elapsed.TotalSeconds,1);one.Disconnect();
        wait=clock.Elapsed.TotalSeconds;await Until(()=>clock.Elapsed.TotalSeconds>wait+.7);
        Require(!one.Connected&&!one.Recovery.Pending,"manual stop restarted");
        Console.WriteLine("RF_NETWORK_RECOVERY PASS: baseline prediction/interpolation, telemetry/probe matching, snapshot stall/recovery, real SDK reconnect/new identity, cancel/expiry/old epoch, bounded queues/events");
    }
    private static void RecoveryChecks()
    {
        var recovery=new RecoveryController(new ReconnectPolicy(1,.1,.2));
        recovery.Failed(0,true,"initial failure");Require(!recovery.Pending,"initial failure auto-looped");
        recovery.Connected(1,1);recovery.Failed(1,true,"link loss");Require(recovery.Pending,"retry missing");
        Require(recovery.Poll(1.05)=="none"&&recovery.Poll(1.11)=="retry","backoff not respected");
        recovery.Failed(1.11,true,"again");Near(recovery.Wait(1.11),.2);recovery.Stop(1.2);Require(recovery.Poll(2)=="none","cancel failed");
        recovery.Connected(3,2);recovery.Failed(3,true,"loss");Require(recovery.Poll(4.1)=="expired"&&!recovery.Pending,"deadline not enforced");
        recovery.Connected(5,3);recovery.SetEnabled(false,true,5);recovery.Failed(5.1,true,"loss");Require(!recovery.Pending,"disabled retry ran");
        var telemetry=new SessionTelemetry();telemetry.Probe(1,0);
        telemetry.Pong(new ServerMessage {type="pong",protocol=1,id=99,tick=6},.1);
        Require(telemetry.Metrics.Read(ClientMetric.ConnectionRttMS,.1).Count==0,"unmatched probe accepted");
        var pong=new ServerMessage {type="pong",protocol=1,id=1,tick=6};telemetry.Pong(pong,.2);telemetry.Pong(pong,.3);
        Require(telemetry.Metrics.Read(ClientMetric.ConnectionRttMS,.3).Count==1,"probe consumed twice");Near(telemetry.Clock.Tick(10),21);
        telemetry.Reset();telemetry.Pong(pong,.4);Require(telemetry.Metrics.Read(ClientMetric.ConnectionRttMS,.4).Count==0,"stale probe survived reset");
        var link=new SimulatedLink();link.Configure(100,200,0,30,0);
        for(int i=0;i<30;i++)Require(link.Outgoing("input",true,0),"send queue overflow");
        Require(link.Send.DroppedSend>0,"loss simulation missing");
        Require(!link.TakeOutgoing(.05,out _),"send delay ignored");Require(link.TakeOutgoing(.1,out _),"send delay not released");
        link.PauseSnapshots(0,3);Require(link.Incoming("snapshot",true,.1),"pause input");Require(link.Incoming("pong",false,.1),"control input");
        Require(!link.TakeIncoming(1,out _)&&link.TakeIncoming(3,out var value)&&value=="pong","pause dropped control or failed to resume");
    }
}
