using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rumblebound.Example.Battle;
using Rumblebound.Example.Hosting;
using Realtime.Transport;

public sealed class HostJson:IGameJson
{
    private readonly JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
    public T Read<T>(string text)=>JsonSerializer.Deserialize<T>(text,options);
    public string Write<T>(T value)=>JsonSerializer.Serialize(value,options);
}
public static class Program
{
    private static double Now=>MessageTransport.ClockMS/1000;
    public static async Task Main(string[] args)
    {
        string url=args[0],backend=args[1],web=args[2];var json=new HostJson();
        if(args.Length>3&&File.Exists(args[3]))MessageTransport.NativeWebTransportExecutable=Path.GetFullPath(args[3]);
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();var app=builder.Build();app.UseWebSockets();
        Realtime.Examples.Browser.UnityRoutes.Map(app,backend,web,UnityBuildPage.Html,rootPlayer:true);
        ApiRoutes.Map(app,backend);
        ViewerReplays.Map(app);
        app.MapGet("/google-login.js",()=>Results.File(Path.GetFullPath(Path.Combine(web,"../game/web/google-login.js")),"text/javascript; charset=utf-8"));
        app.MapGet("/healthz",()=>Results.Text("ok\n"));
        app.MapGet("/diagnostics",(HttpContext context)=>context.Request.Path.Value.EndsWith("/",StringComparison.Ordinal)?Results.File(Path.Combine(web,"index.html"),"text/html; charset=utf-8"):Results.Redirect("/diagnostics/"));
        foreach(var file in new[]{"app.js","style.css"}){
            string name=file;string content=name.EndsWith(".js")?"text/javascript; charset=utf-8":"text/css; charset=utf-8";
            app.MapGet("/diagnostics/"+name,()=>Results.File(Path.Combine(web,name),content));
            app.MapGet("/"+name,()=>Results.File(Path.Combine(web,name),content));
        }
        app.MapGet("/play",async(HttpContext context)=>{
            string origin=context.Request.Headers.Origin.ToString();
            if(origin.Length>0&&origin!=context.Request.Scheme+"://"+context.Request.Host){context.Response.StatusCode=403;return;}
            if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
            using var replayFiles=ViewerReplays.Create();if(replayFiles==null){context.Response.StatusCode=429;return;}
            using var socket=await context.WebSockets.AcceptWebSocketAsync();using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,app.Lifetime.ApplicationStopping);
            var controls=Channel.CreateBounded<string>(16);var reader=Read(socket,controls.Writer,lifetime);
            using var api=new HttpGameApi(backend);using var flow=new GameFlow(api,json,backend.Replace("http:","ws:").Replace("https:","wss:")+"/ws");
            var projection=new GameProjection();float x=0,z=0;bool automatic=false;float direction=1;int publishedRevision=-1;GameContent publishedContent=null;
            try{
                while(!lifetime.IsCancellationRequested){
                    for(int n=0;n<16&&controls.Reader.TryRead(out var command);n++){
                        using var document=JsonDocument.Parse(command);var c=document.RootElement;
                        string op=c.GetProperty("op").GetString();string S(string key,string fallback="")=>c.TryGetProperty(key,out var value)?value.GetString():fallback;
                        int I(string key,int fallback=0)=>c.TryGetProperty(key,out var value)?value.GetInt32():fallback;
                        double D(string key,double fallback=0)=>c.TryGetProperty(key,out var value)?value.GetDouble():fallback;
                        bool B(string key)=>c.TryGetProperty(key,out var value)&&value.GetBoolean();
                        switch(op){
                            case "drive":x=Axis(D("x"));z=Axis(D("z"));break;
                            case "stop":x=z=0;break;
                            case "auto":automatic=B("value");break;
                            case "guest":flow.Guest();break;
                            case "restore":flow.Restore(S("credential"),S("userID"));break;
                            case "config":flow.ReloadContent();break;
                            case "create":flow.Create(I("teams",2));break;
                            case "join":flow.Join(S("code"));break;
                            case "team":flow.Team(S("team"));break;
                            case "hero":flow.Hero(S("hero"));break;
                            case "assign":flow.Assign(S("member"),S("team"));break;
                            case "capacity":flow.Capacity(S("team"),I("capacity",1));break;
                            case "ready":flow.Ready(B("value"));break;
                            case "start":flow.Start();break;
                            case "enter":flow.Enter(B("takeover"));break;
                            case "disconnect":flow.StopBattle();x=z=0;automatic=false;break;
                            case "leave":flow.Leave();x=z=0;automatic=false;break;
                            case "return":flow.ReturnToRoom();x=z=0;break;
                            case "logout":flow.Logout();x=z=0;automatic=false;break;
                            case "practice":flow.Practice(S("hero","ranger"),S("botHero","tank"),S("mode","idle"));break;
                            case "practice-mode":flow.PracticeMode(S("mode","idle"));break;
                            case "scene-ready":flow.SceneLoaded();break;
                            case "action":flow.AimAction(S("kind","fire"),D("x"),D("z"),Now);break;
                            case "aim":flow.Face(D("x"),D("z"));break;
                            case "reconnect":flow.AutoReconnect=B("value");break;
                            case "forget":flow.Battle.ForgetBaseline();break;
                            case "cut":flow.Battle.Transport.InterruptForDiagnostics();break;
                            case "record-start":flow.StartRecording();break;
                            case "record-stop":flow.Recording.Stop();break;
                            case "replay-play":flow.Playback.Clock?.Play();break;
                            case "replay-pause":flow.Playback.Clock?.Pause();break;
                            case "replay-seek":flow.Playback.Seek(D("tick"));break;
                            case "replay-speed":if(flow.Playback.Active)flow.Playback.Clock.Speed=D("value",1);break;
                            case "replay-end":flow.EndPlayback();break;
                            case "settings":var next=json.Read<BattleSettings>(c.GetProperty("value").GetRawText())??throw new InvalidDataException("Missing settings");next.Validate();flow.Selection=next;break;
                            default:throw new InvalidDataException("Unknown control");
                        }
                    }
                    if(automatic&&flow.Battle.CanControl){
                        var own=flow.Battle.State.Latest.players;
                        foreach(var p in own)if(p.playerID==flow.Battle.State.PlayerID){if(p.z>5)direction=-1;else if(p.z< -5)direction=1;}
                    }
                    replayFiles.Pump(flow);flow.Pump(Now,automatic?0:x,automatic?direction:z);
                    var world=flow.Playback.Active?flow.Playback.View:projection.Update(flow,Now);
                    object credential=null;
                    if(flow.Identity.Revision!=publishedRevision){publishedRevision=flow.Identity.Revision;if(publishedRevision>0)credential=new{value=flow.Identity.Credential,userID=flow.Identity.UserID};}
                    var activeContent=flow.Playback.Active?flow.Playback.Content:flow.Content;
                    object definition=null;if(activeContent!=null&&!ReferenceEquals(publishedContent,activeContent)){publishedContent=activeContent;definition=new{rules=activeContent.Rules,map=activeContent.Map,googleClientID=activeContent.Config.googleClientID,webTransport=MessageTransport.SupportsWebTransport&&!string.IsNullOrEmpty(activeContent.Config.webTransportURL)};}
                    var frame=new{viewerID=replayFiles.ID,stage=flow.Stage,status=flow.Battle.Status,notice=flow.Notice,busy=flow.Busy,userID=flow.Identity.UserID,room=flow.Room,
                        config=definition,world,
                        recording=new{active=flow.Recording.Recording,frames=flow.Recording.Frames,notice=flow.Recording.Notice},
                        replay=new{active=flow.Playback.Active,playing=flow.Playback.Clock?.Playing??false,tick=flow.Playback.Clock?.Tick??0,first=flow.Playback.Clock?.FirstTick??0,last=flow.Playback.Clock?.LastTick??0,speed=flow.Playback.Clock?.Speed??1},
                        connected=flow.Battle.Connected,canControl=flow.Battle.CanControl,loaded=flow.Battle.LoadedSent,credential,
                        recovery=new{enabled=flow.AutoReconnect,pending=flow.Recovery.Pending,attempts=flow.Recovery.Attempts,wait=flow.Recovery.Pending&&!double.IsInfinity(flow.Recovery.Next)?Math.Max(0,flow.Recovery.Next-Now):0},
                        net=new{settings=flow.Selection,frameAck=flow.Battle.FrameAck,missing=flow.Battle.MissingBaselines,confirmed=flow.Battle.ConfirmedActions,rejected=flow.Battle.RejectedActions,action=flow.Battle.LastActionCode,
                            predictions=BattleNetcode.Predictions.Keys,corrections=BattleNetcode.Corrections.Keys,timelines=BattleNetcode.Timelines.Modes,
                            clocks=BattleNetcode.ClockFactory==null?new[]{"snapshot"}:new[]{"snapshot","disciplined"},codecs=BattleNetcode.DecoderFactory==null?new[]{"json"}:new[]{"json","delta"},speculative=BattleNetcode.ActionsFactory!=null}};
                    byte[] data=Encoding.UTF8.GetBytes(json.Write(frame));using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(3000);
                    await socket.SendAsync(data,WebSocketMessageType.Text,true,timeout.Token);await Task.Delay(16,lifetime.Token);
                }
            }catch(Exception error)when(error is WebSocketException||error is OperationCanceledException||error is InvalidDataException||error is JsonException||error is InvalidOperationException||error is ArgumentException){}
            finally{lifetime.Cancel();socket.Abort();try{await reader;}catch(Exception){}}
        });
        Console.WriteLine("Rumblebound remake: "+url+" (actual C# SDK + shared Go game; Ctrl+C stops)");await app.RunAsync(url);
    }
    private static float Axis(double value){if(double.IsNaN(value)||double.IsInfinity(value)||Math.Abs(value)>1)throw new InvalidDataException("Invalid axis");return(float)value;}
    private static async Task Read(WebSocket socket,ChannelWriter<string> commands,CancellationTokenSource lifetime)
    {
        var bytes=new byte[8192];
        try{while(!lifetime.IsCancellationRequested){int count=0;WebSocketReceiveResult part;
            do{if(count==bytes.Length)throw new InvalidDataException("Control limit");part=await socket.ReceiveAsync(new ArraySegment<byte>(bytes,count,bytes.Length-count),lifetime.Token);if(part.MessageType==WebSocketMessageType.Close)return;if(part.MessageType!=WebSocketMessageType.Text)throw new InvalidDataException("Text control required");count+=part.Count;}while(!part.EndOfMessage);
            await commands.WriteAsync(Encoding.UTF8.GetString(bytes,0,count),lifetime.Token);
        }}finally{commands.TryComplete();lifetime.Cancel();}
    }
}
