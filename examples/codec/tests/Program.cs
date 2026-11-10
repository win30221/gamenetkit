using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Realtime.Examples.CodecLesson;

public static class CodecTests
{
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static ServerMessage Model(long tick,uint stream,string mode)=>new ServerMessage{type=tick==0?"welcome":"snapshot",protocol=1,generation=1,tick=tick,playerID=1,tickRate=30,snapshotRate=15,players=new[]{new PlayerState{id=1}},objects=new[]{new WorldObject{id="landmark-hub",kind="global",life=1,z=7}},interest=new InterestSummary{mode="aoi",radius=3,margin=.75f,totalPlayers=1,totalObjects=13},codec=new CodecSummary{mode=mode,stream=stream}};
    private static ServerMessage Envelope(long id,long baseline,uint stream,ServerMessage model)=>new ServerMessage{type="frame",protocol=1,stream=stream,id=id,@base=baseline,tick=model.tick,data=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(model,Json)))};
    private static void Components()
    {
        var channel=new CodecChannel(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json));
        bool Receive(ServerMessage m,out FrameReceipt receipt)=>channel.Receive(m,JsonSerializer.Serialize(m,Json),m.tick/30.0,out _,out _,out receipt);
        Require(Receive(Model(0,1,"json"),out _),"welcome");
        Require(Receive(Envelope(1,0,2,Model(2,2,"delta")),out var first)&&first.Kind=="keyframe"&&first.WireBytes>first.JSONBytes,"full envelope");
        Require(channel.TakeAck(out uint stream,out long ack)&&stream==2&&ack==1,"validated ack");
        channel.ForgetBaseline();var missing=Envelope(2,1,2,Model(4,2,"delta"));missing.data="AA==";
        Require(!Receive(missing,out _)&&channel.Recovering&&channel.Metrics.Missing==1,"missing baseline");
        Require(channel.TakeAck(out stream,out ack)&&ack==0&&stream==2,"missing base did not request full");
        Require(Receive(Envelope(3,0,2,Model(6,2,"delta")),out _)&&!channel.Recovering&&channel.Metrics.Recoveries==1,"full recovery");
        Require(Receive(Envelope(1,0,3,Model(8,3,"delta")),out _)&&channel.Stream==3,"new stream frame IDs did not reset");
        Require(!Receive(Envelope(7,0,2,Model(10,2,"delta")),out _)&&channel.Stream==3,"old stream accepted");
        Require(!Receive(Envelope(1,0,3,Model(10,3,"delta")),out _),"stale frame accepted");
        channel.TakeAck(out _,out _);var invalid=Envelope(2,0,3,Model(12,3,"delta"));invalid.data="%%%";
        try{Receive(invalid,out _);throw new Exception("invalid encoding accepted");}catch(InvalidOperationException){}
        channel.Clear();Require(channel.Stream==0&&channel.Ack==0&&channel.Metrics.Missing==0,"codec reset");
        Console.WriteLine("Actual FrameDecoder/channel: JSON/full envelope, validated ACK, missing base ACK0/full recovery, stream revisions, stale/invalid rejection and clear PASS");
    }
    private static ExampleSession Session()=>new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
    public static async Task<int> Main(string[] args){try{await Run(args);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    private static async Task Run(string[] args)
    {
        Components();using var one=Session();using var two=Session();var clock=Stopwatch.StartNew();float drive=0;string endpoint=ExampleEndpoint.Normalize(args[0]);one.Connect(endpoint,0);two.Connect(endpoint,0);
        async Task Until(Func<bool> condition,[CallerLineNumber]int line=0){double end=clock.Elapsed.TotalSeconds+7;while(!condition()){Require(clock.Elapsed.TotalSeconds<end,$"timeout line {line}: {one.Status}/{two.Status}, mode={one.State.Codec?.mode}, step={one.State.Codec?.step}, delta={one.Codec.Metrics.Deltas}, ack={one.Codec.Ack}, missing={one.Codec.Metrics.Missing}, recoveries={one.Codec.Metrics.Recoveries}, x={(one.State.Joined?one.State.Players[one.State.LocalPlayerID].x:0)}");one.Pump(clock.Elapsed.TotalSeconds,drive,0);two.Pump(clock.Elapsed.TotalSeconds,0,0);await Task.Delay(8);}}
        await Until(()=>one.State.Players.Count==2&&two.State.Players.Count==2&&one.Codec.Metrics.JSONFrames>1);
        long own=one.State.LocalPlayerID;Require(one.Codec.Metrics.Latest.WireBytes==one.Codec.Metrics.Latest.JSONBytes,"JSON size boundary");
        one.SetCodec("delta",0);await Until(()=>one.State.Codec.mode=="delta"&&one.Codec.Metrics.Deltas>2);
        Require(one.Codec.Metrics.Latest.WireBytes<one.Codec.Metrics.Latest.JSONBytes&&one.State.Objects.Count==13,"real Go/C# delta did not preserve full state or reduce payload");
        Require(two.State.Codec.mode=="json"&&two.Codec.Metrics.Deltas==0,"per-peer encoding leaked");
        drive=1;float start=one.State.Players[own].x;await Until(()=>one.State.Players[own].x>start+.4);
        one.QueueAction("spawn",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Interest.ownedObjects==1&&two.State.Objects.Count==14);
        string marker=one.State.Objects.Values.Single(o=>o.kind=="marker").id;one.QueueAction("rebirth",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Objects[marker].life==2);
        one.SetCodec("delta",.1);await Until(()=>one.State.Codec.step==.1&&one.State.Codec.maxError>0);
        Require(one.State.Codec.maxError<=.050001&&two.State.Codec.step==0,"quantization error/peer isolation");
        Require(Math.Abs(one.State.Players[own].x/.1-Math.Round(one.State.Players[own].x/.1))<.0001,"coordinate not quantized");
        // The other recipient sees the same authority at full precision.
        drive=one.State.Players[own].x>6?-1:1; // Stay away from the clamped integer boundary.
        await Until(()=>Math.Abs(two.State.Players[own].x/.1-Math.Round(two.State.Players[own].x/.1))>.05);
        long deltas=one.Codec.Metrics.Deltas;await Until(()=>one.Codec.Metrics.Deltas>deltas);long misses=one.Codec.Metrics.Missing,recoveries=one.Codec.Metrics.Recoveries;one.Codec.ForgetBaseline();
        await Until(()=>one.Codec.Metrics.Missing>misses&&one.Codec.Metrics.Recoveries>recoveries&&!one.Codec.Recovering);Require(one.Connected,"baseline loss disconnected session");
        drive=0;one.SetInterest("aoi",3);await Until(()=>one.State.Interest.mode=="aoi"&&one.State.Objects.Count<14);
        one.SetCodec("json",.01);await Until(()=>one.State.Codec.mode=="json"&&one.State.Codec.step==.01&&one.Codec.Metrics.Latest.Kind=="json");Require(one.Codec.Metrics.Latest.WireBytes==one.Codec.Metrics.Latest.JSONBytes,"JSON fallback");
        one.SetCodec("delta",0);await Until(()=>one.State.Codec.mode=="delta"&&one.State.Codec.step==0);
        one.QueueAction("remove",false,clock.Elapsed.TotalSeconds);await Until(()=>one.State.Interest.ownedObjects==0&&two.State.Objects.Count==13);
        int epoch=one.Transport.Epoch;one.Disconnect();Require(one.Codec.Stream==0&&one.Codec.Metrics.Bytes==0&&one.State.Codec==null,"disconnect leaked codec state");
        await Until(()=>two.State.Players.Count==1);one.Connect(endpoint,clock.Elapsed.TotalSeconds);one.Transport.Receive(epoch,"old callback");await Until(()=>one.Connected&&one.State.Codec.mode=="delta"&&one.Codec.Metrics.Deltas>0);
        Console.WriteLine("RF_CODEC PASS: actual Go Encoder/Quantize + C# FrameDecoder, two WS, acknowledged delta/JSON/full recovery, per-peer streams, immutable authority/quantized output, object life/removal, AOI, mode switching and reconnect");
    }
}
