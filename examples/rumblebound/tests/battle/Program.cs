using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;
using Realtime.Core;

public sealed class GameJson:IGameJson
{
    private readonly JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
    public T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,options);
    public string Write<T>(T value)=>JsonSerializer.Serialize(value,options);
}
public static class BattleTests
{
    private sealed class Guest{public string session,userID;}
    private sealed class Team{public string teamID;public int capacity;}
    private sealed class Room{public string code,matchID,phase;public Team[] teams;public bool started;}
    private sealed class Ticket{public string ticket,matchID;}
    private static readonly GameJson Json=new GameJson();
    private static void Require(bool value,string why){if(!value)throw new Exception(why);}
    private static async Task<T> Request<T>(HttpClient http,string path,string token=null,object body=null)
    {
        using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,path);
        if(token!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request);string text=await response.Content.ReadAsStringAsync();
        Require(response.IsSuccessStatusCode,path+": "+response.StatusCode+" "+text);return Json.Read<T>(text);
    }
    public static async Task<int> Main(string[] args)
    {try{MenuTests.Run();TouchTests.Run();NetworkSettingsTests.Run();ReplyTrackerTests.Run();SnapshotBufferTests.Run();RemoteContactTests.Run();PredictionGapTests.Run();PresentationStateTests.Run();await Run(args[0]);await FlowTests.Run(args[0]);ReplayTests.Run(args[1]);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    private static async Task Run(string origin)
    {
        using var http=new HttpClient{BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(8)};
        var config=await Request<LabConfig>(http,"/api/config");var content=new GameContent(config,Json);Require(Math.Abs(content.Hero("ranger").speed-4.8)<1e-9&&Math.Abs(content.Hero("tank").speed-4)<1e-9&&Math.Abs(content.Hero("mage").speed-4.4)<1e-9,"walking speed scale not delivered to client");AimAssistTests.Run(content);OrdersTests.Run(content);
        var first=await Request<Guest>(http,"/api/session/guest",body:new{});var second=await Request<Guest>(http,"/api/session/guest",body:new{});
        var room=await Request<Room>(http,"/api/room/create",first.session,new{teamCount=2});
        room=await Request<Room>(http,"/api/room/join",second.session,new{code=room.code});
        await Request<Room>(http,"/api/room/team",first.session,new{teamID=room.teams[0].teamID});
        await Request<Room>(http,"/api/room/team",second.session,new{teamID=room.teams[1].teamID});
        await Request<Room>(http,"/api/room/hero",first.session,new{heroID="ranger"});
        await Request<Room>(http,"/api/room/hero",second.session,new{heroID="tank"});
        var ready=new{ready=true,protocol=content.Rules.protocol,content=content.Rules.content,mapHash=content.Config.mapHash};
        await Request<Room>(http,"/api/room/ready",first.session,ready);await Request<Room>(http,"/api/room/ready",second.session,ready);
        room=await Request<Room>(http,"/api/room/start",first.session,new{});Require(room.started&&room.phase=="loading","loading gate absent");
        var t1=await Request<Ticket>(http,"/api/room/ticket",first.session,new{});var t2=await Request<Ticket>(http,"/api/room/ticket",second.session,new{});
        using var one=new BattleSession(Json);using var two=new BattleSession(Json);var timer=Stopwatch.StartNew();
        double Now()=>timer.Elapsed.TotalSeconds;double drive=0;
        async Task Until(Func<bool> predicate,string stage){double end=Now()+9;while(!predicate()){Require(Now()<end,stage+": "+one.Status+" / "+two.Status+" action="+one.LastActionCode);one.Pump(Now(),0,drive);two.Pump(Now(),0,0);await Task.Delay(8);}}
        string endpoint=origin.Replace("https:","wss:").Replace("http:","ws:")+"/ws";
        var selection=new BattleSettings{DelaySeconds=.15,ReceiveJitterSeconds=.01,ReceiveLossProbability=.1,SendDelaySeconds=.06,SendJitterSeconds=.01,SendLossProbability=.2,SpeculativeActions=true};selection.Server.codec="delta";
        one.Connect(content,selection,endpoint,t1.ticket,t1.matchID,"",Now());two.Connect(content,new BattleSettings(),endpoint,t2.ticket,t2.matchID,"",Now());
        await Until(()=>one.Connected&&two.Connected,"welcome");
        Require(one.State.Tick==0&&two.State.Tick==0&&!one.State.Playing,"preloaded match advanced");
        one.MarkLoaded();await Until(()=>one.LoadedSent,"first loaded");
        double waiting=Now();while(Now()-waiting<.2){one.Pump(Now(),0,0);two.Pump(Now(),0,0);await Task.Delay(8);}
        Require(!one.State.Playing&&!two.State.Playing,"one loaded peer bypassed barrier");two.MarkLoaded();
        await Until(()=>one.State.Playing&&two.State.Playing&&one.FrameAck>0,"match start and codec");
        string id=one.State.PlayerID,entity=one.State.Latest.players.First(p=>p.playerID==id).entityID;
        uint generation=one.State.Generation;long tick=one.State.Tick;
        var before=one.State.Latest.players.First(p=>p.playerID==id);one.Movement.Position(before,true,out _,out double start);
        drive=1;one.Pump(Now(),0,1);await Task.Delay(18);one.Pump(Now(),0,1);
        one.Movement.Position(before,true,out _,out double predicted);Require(predicted>start+.05,"local motion waited for delayed authority");
        await Until(()=>two.State.Latest.players.First(p=>p.playerID==id).z>start+.5,"shared authoritative movement");drive=0;
        double settle=Now();while(Now()-settle<.5){one.Pump(Now(),0,0);two.Pump(Now(),0,0);await Task.Delay(8);}
        Require(one.QueueAction("fire",1,0,Now()),"fire not queued");await Until(()=>one.LastActionCode=="fired","fire confirmation");Require(one.ConfirmedActions==1&&one.ActionStarts==1,"action ledger lost confirmation");
        await Until(()=>one.State.Latest.players.First(p=>p.playerID==id).attackStopUntilTick<=one.State.Tick,"attack recovery before dash");
        Require(one.QueueAction("dash",0,1,Now()),"dash not queued");await Until(()=>one.LastActionCode=="dashed","dash confirmation");
        long ack=one.FrameAck;one.ForgetBaseline();await Until(()=>one.MissingBaselines>0&&one.FrameAck>ack,"missing baseline recovery");
        await Until(()=>one.RTTSeconds>0,"matched clock probe");
        foreach(ClientMetric metric in Enum.GetValues(typeof(ClientMetric)))Require(one.Metrics.Read(metric,Now()).Count>0,"missing diagnostic samples: "+metric);
        Require(one.RTTSeconds>=.17,"application RTT excluded a simulated direction");
        Require(one.SendConditioning.DroppedSend>0&&one.ReceiveConditioning.DroppedReceive>0,"independent loss controls did not reach both directions");
        var resolved=new System.Collections.Generic.List<long>();long previousAction=one.State.Latest.actionAck;long confirmations=one.ConfirmedActions;
        void Resolved(long actionID,bool accepted){Require(accepted,"queued cancel was rejected");resolved.Add(actionID);}
        one.ResolveAction+=Resolved;double queueStarted=Now();
        try{
            for(int i=0;i<4;i++)Require(one.QueueAction("cancel",0,1,Now()),"bounded action queue rejected an available slot");
            Require(!one.QueueAction("cancel",0,1,Now()),"full action queue accepted a fifth command");
            await Until(()=>resolved.Count==4&&!one.HasPendingAction,"four ordered action confirmations under conditioning");
            Require(resolved.SequenceEqual(Enumerable.Range(1,4).Select(i=>previousAction+i))&&one.ConfirmedActions==confirmations+4,"queued actions were skipped, repeated or resolved out of order");
            Console.WriteLine("REMAKE_ACTION_QUEUE PASS four ordered one-use confirmations under two-way conditioning; elapsed="+(Now()-queueStarted).ToString("0.000")+"s RTT="+one.RTTSeconds.ToString("0.000")+"s (stop-and-wait retained)");
        }finally{one.ResolveAction-=Resolved;}
        var invalid=Json.Read<Snapshot>(Json.Write(one.State.Latest));invalid.tick+=2;invalid.players[0].x=1000;
        long beforeTick=one.State.Tick;try{one.State.Apply(invalid,content,t1.matchID);throw new Exception("invalid state accepted");}catch(InvalidOperationException){}
        Require(one.State.Tick==beforeTick,"partial invalid snapshot published");
        long priorAck=one.State.Latest.players.First(p=>p.playerID==id).ack;
        int epoch=one.Transport.Epoch;one.Disconnect();await Until(()=>two.State.Latest.players.First(p=>p.playerID==id).online==false,"offline seat");
        var reconnect=await Request<Ticket>(http,"/api/room/ticket",first.session,new{matchID=t1.matchID,takeover=false});
        one.Connect(content,selection,endpoint,reconnect.ticket,reconnect.matchID,"",Now());one.Transport.Receive(epoch,"obsolete callback");
        await Until(()=>one.Connected,"reconnect welcome");Require(one.State.PlayerID==id&&one.State.Generation>generation&&one.State.Latest.players.First(p=>p.playerID==id).entityID==entity,"identity/seat was replaced on reconnect");
        Require(one.InputSequence>=priorAck&&!one.CanControl,"reconnect lost sequence baseline or bypassed local loading");
        one.MarkLoaded();await Until(()=>one.State.Playing&&one.State.Tick>tick,"reconnected playing");
        drive=-1;double previousZ=two.State.Latest.players.First(p=>p.playerID==id).z;
        await Until(()=>two.State.Latest.players.First(p=>p.playerID==id).z<previousZ-.2,"reconnect input accepted");drive=0;
        await Until(()=>one.State.Latest.players.First(p=>p.playerID==id).fireReadyTick<=one.State.Tick,"reconnected skill cooldown preserved");
        Require(one.QueueAction("fire",1,0,Now()),"reconnect action not queued");await Until(()=>one.LastActionCode=="fired","reconnect action IDs accepted");
        TerminalTests.Run(content,one.State.Latest,id);
        await ConditionedTerminalTests.Run(content,one.State.Latest,id);
        // Dropped unreliable input must not discard the reliable loading/probe
        // path, acknowledge an unsent action, or leak a delayed command on reset.
        one.Disconnect();await Until(()=>!two.State.Latest.players.First(p=>p.playerID==id).online,"offline before full uplink loss");
        var lossTicket=await Request<Ticket>(http,"/api/room/ticket",first.session,new{matchID=t1.matchID,takeover=false});
        var lossSelection=new BattleSettings{SendLossProbability=1,SpeculativeActions=true};
        one.Connect(content,lossSelection,endpoint,lossTicket.ticket,lossTicket.matchID,"",Now());
        await Until(()=>one.Connected,"full-loss welcome");one.MarkLoaded();await Until(()=>one.CanControl&&one.RTTSeconds>0,"reliable loaded and ping with full input loss");
        long lostAck=one.State.Latest.actionAck;
        Require(one.QueueAction("fire",1,0,Now()),"loss action not queued");
        double lossStarted=Now();while(Now()-lossStarted<.4){one.Pump(Now(),0,1);two.Pump(Now(),0,0);await Task.Delay(8);}
        Require(one.Connected&&one.HasPendingAction&&one.State.Latest.actionAck==lostAck&&one.SendConditioning.DroppedSend>0,"discarded uplink action was falsely acknowledged");
        one.Disconnect();Require(one.SendConditioning.PendingSend==0&&one.ReceiveConditioning.PendingReceive==0&&!one.HasPendingAction,"disconnect retained conditioned messages or pending action");
        Console.WriteLine("REMAKE_BATTLE_NATIVE PASS actual new API + C# SDK: loading, shared movement, action retries, delta recovery, six diagnostic metrics, two-way delay/loss, reliable controls under full input loss, clean reset and same identity/seat with new generation");
    }
}
