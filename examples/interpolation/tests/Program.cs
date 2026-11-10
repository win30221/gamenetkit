using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Realtime.Examples.InterpolationLesson;

public static class InterpolationTests
{
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static ServerMessage Frame(long tick,float x,bool welcome=false)=>new ServerMessage {
        type=welcome?"welcome":"snapshot",protocol=1,playerID=welcome?1:0,tick=tick,tickRate=30,snapshotRate=15,
        players=new[]{new PlayerState {id=1,x=x}}
    };
    private static void Sampling()
    {
        foreach(int fps in new[]{30,60,144}) {
            double latestJump=Motion("latest",fps),fixedJump=Motion("fixed",fps),adaptiveJump=Motion("adaptive",fps);
            Require(fixedJump<latestJump && adaptiveJump<latestJump,"interpolation should reduce snapshot steps at "+fps);
            Console.WriteLine($"FPS {fps}: largest x step off={latestJump:F4}, fixed={fixedJump:F4}, adaptive={adaptiveJump:F4}");
        }
        var view=new InterpolatedWorld();var original=Frame(0,0,true);view.Accept(original,0);
        original.players[0].x=7;view.Latest(1,out float copied,out _);Require(copied==0,"retained DTO reference");
        view.Accept(Frame(2,2),.067);view.SetMode("latest",.067);view.Sample(1,out float x,out _);Require(x==2,"mode switch not applied");
        int count=view.BufferedFrames;view.Accept(Frame(2,7),.08);Require(view.BufferedFrames==count,"duplicate polluted history");
        bool rejected=false;try{view.SetMode("unknown",.1);}catch(InvalidOperationException){rejected=true;}
        Require(rejected && view.Mode=="latest","unknown mode changed active timeline");
        var empty=Frame(4,0);empty.players=Array.Empty<PlayerState>();view.Accept(empty,.133);
        Require(!view.Sample(1,out _,out _),"departed ID sampled from old frames");
        view.Accept(Frame(0,-2,true),1);Require(view.BufferedFrames==1,"welcome did not reset history");
        for(int i=1;i<=200;i++)view.Accept(Frame(i*2,0),1+i/15.0);
        Require(view.BufferedFrames==128,"history unbounded");view.Advance(100);
        Require(view.RenderTick<=400,"outage extrapolated past latest");
        view.Clear();Require(view.RenderTick==-1 && !view.Sample(1,out _,out _),"disconnect did not clear");
    }
    private static double Motion(string mode,int fps)
    {
        var view=new InterpolatedWorld();view.SetMode(mode,0);view.Accept(Frame(0,0,true),0);
        int next=1;double previous=0,previousTick=double.NegativeInfinity,maxStep=0;
        double[] jitter={0,.015,-.01,.02,-.005};
        for(int f=0;f<=fps*3;f++) {
            double now=f/(double)fps;
            while(next<=45 && next/15.0+jitter[next%jitter.Length]<=now) {
                view.Accept(Frame(next*2,next/15f*2),next/15.0+jitter[next%jitter.Length]);next++;
            }
            view.Advance(now);Require(view.RenderTick>=previousTick,"render time moved backwards");
            Require(view.RenderTick<=(next-1)*2+1e-8,"extrapolation");previousTick=view.RenderTick;
            Require(view.Sample(1,out float x,out _) && x>=0 && x<=6,"invalid sampled position");
            if(now>.6 && now<2.8)maxStep=Math.Max(maxStep,Math.Abs(x-previous));
            previous=x;
        }
        return maxStep;
    }
    public static async Task Main(string[] args)
    {
        Sampling();
        var json=new JsonSerializerOptions {IncludeFields=true};
        var clock=Stopwatch.StartNew();
        var oneNet=new NetcodeComposition();var twoNet=new NetcodeComposition();
        using var one=new ExampleSession(oneNet,s=>JsonSerializer.Deserialize<ServerMessage>(s,json),i=>JsonSerializer.Serialize(i,json));
        using var two=new ExampleSession(twoNet,s=>JsonSerializer.Deserialize<ServerMessage>(s,json),i=>JsonSerializer.Serialize(i,json));
        string endpoint=ExampleEndpoint.Normalize(args[0]);
        two.ConfigureNetwork(.15,.1,1); // Welcome/control must survive even 100% snapshot loss.
        one.Connect(endpoint,"one",0);two.Connect(endpoint,"two",0);
        float drive=0;
        async Task Until(Func<bool> condition) {
            double deadline=clock.Elapsed.TotalSeconds+6;
            while(!condition()) {
                Require(clock.Elapsed.TotalSeconds<deadline,"timeout "+one.Status+" / "+two.Status);
                one.Pump(clock.Elapsed.TotalSeconds,drive,0);two.Pump(clock.Elapsed.TotalSeconds,0,0);
                await Task.Delay(8);
            }
        }
        await Until(()=>one.State.Players.Count==2 && two.State.Players.Count==2 && two.Network.DroppedReceive>2);
        Require(two.Connected && two.View.BufferedFrames==1,"loss affected welcome or failed to discard snapshots");
        long id=one.State.LocalPlayerID,other=two.State.LocalPlayerID;
        float origin=two.State.Players[id].x,stationary=two.State.Players[other].x;
        two.ConfigureNetwork(.1,.05,.1);drive=1;
        await Until(()=>two.State.Players[id].x>origin+.6f && two.View.BufferedFrames>3);
        Require(two.State.Players[other].x==stationary,"input ownership broken");
        foreach(string mode in new[]{"latest","fixed","adaptive"}) {
            two.View.SetMode(mode,clock.Elapsed.TotalSeconds);
            await Until(()=>two.View.BufferedFrames>4);
            two.Pump(clock.Elapsed.TotalSeconds,0,0);
            Require(two.View.Sample(id,out float sample,out _) && Math.Abs(sample)<=8,"live sample failed "+mode);
        }
        drive=0;one.StopInput(clock.Elapsed.TotalSeconds);
        int epoch=oneNet.Transport.Epoch;one.Disconnect();
        Require(one.View.BufferedFrames==0,"disconnect leaked snapshots");
        await Until(()=>two.State.Players.Count==1);
        one.Connect(endpoint,"one",clock.Elapsed.TotalSeconds);oneNet.Transport.Receive(epoch,"old data");
        await Until(()=>one.Connected && one.State.Players.Count==2 && two.State.Players.Count==2);
        Require(one.State.LocalPlayerID>Math.Max(id,other),"reconnect reused identity");
        Console.WriteLine("RF_INTERPOLATION PASS: actual SDK timelines, 30/60/144 FPS, bounded sampling, mode/reset/roster, real sessions, conditioned snapshots, old epoch");
    }
}
