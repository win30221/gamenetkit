using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Realtime.Transport;
using Realtime.Examples.ReplayLesson;

// The page displays SDK results; recording belongs to Go, playback belongs to C#.
public static class Program
{
    private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    private static double Now=>MessageTransport.ClockMS/1000;
    public static async Task Main(string[] args)
    {
        string url=args[0],backend=args[1],web=args[2];
        using var recordings=new HttpClient{BaseAddress=new Uri(backend),Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=8*1024*1024};
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();var app=builder.Build();app.UseWebSockets();
        Realtime.Examples.Browser.UnityRoutes.Map(app,backend,web);
        app.MapGet("/healthz",()=>Results.Text("ok\n"));
        app.MapGet("/",()=>Results.Content(File.ReadAllText(Path.Combine(web,"index.html")),"text/html; charset=utf-8"));
        app.MapGet("/app.js",()=>Results.Content(File.ReadAllText(Path.Combine(web,"app.js")),"text/javascript; charset=utf-8"));
        app.MapGet("/style.css",()=>Results.Content(File.ReadAllText(Path.Combine(web,"style.css")),"text/css; charset=utf-8"));
        app.MapGet("/recordings/{id}",async(string id,HttpContext context)=>{
            if(!RecordingStatus.ValidID(id))return Results.NotFound();
            try{
                byte[] data=await recordings.GetByteArrayAsync("/recordings/"+id,context.RequestAborted);
                context.Response.Headers.CacheControl="no-store";
                return Results.File(data,"application/json","replay-"+id+".json");
            }catch(HttpRequestException){return Results.NotFound();}
        });
        app.MapGet("/lesson",async(HttpContext context)=>{
            if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
            using var socket=await context.WebSockets.AcceptWebSocketAsync();
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,app.Lifetime.ApplicationStopping);
            var controls=Channel.CreateBounded<string>(16);var reader=ReadControls(socket,controls.Writer,lifetime);
            using var session=new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
            var playback=new PlaybackWorld();Task<PlaybackFile> loading=null;string loadedID="",notice="";
            session.Connect(ExampleEndpoint.Normalize(backend),Now);
            string mode="prediction",correction="smooth",profile="delay",remote="adaptive";
            bool automatic=false,replayMode=false;float x=0,z=0,direction=1;double previous=Now;
            try{
                while(!lifetime.IsCancellationRequested){
                    double now=Now;
                    for(int n=0;n<16&&controls.Reader.TryRead(out var text);n++){
                        using var document=JsonDocument.Parse(text);var control=document.RootElement;
                        if(control.TryGetProperty("view",out var value)){
                            if(value.GetString()!="live")throw new InvalidDataException("view");
                            replayMode=false;playback.Pause();x=z=0;automatic=false;notice="";
                        }
                        bool live=!replayMode&&loading==null;
                        if(control.TryGetProperty("record",out value)&&live){
                            string operation=value.GetString();session.Record(operation);notice="";
                        }
                        if(control.TryGetProperty("play",out value)&&value.GetBoolean()){
                            if(replayMode){if(playback.Clock.Playing)playback.Pause();else playback.Play();}
                            else if(loading==null&&session.Recording.Status=="ready"){
                                if(playback.Loaded&&loadedID==session.Recording.ID){playback.Seek(0);playback.Play();replayMode=true;}
                                else{loadedID=session.Recording.ID;loading=LoadClip(recordings,loadedID,lifetime.Token);}
                                x=z=0;automatic=false;notice="";
                            }
                        }
                        if(control.TryGetProperty("seek",out value)&&replayMode)playback.Seek(value.GetDouble());
                        if(control.TryGetProperty("speed",out value)&&replayMode)playback.Clock.Speed=value.GetDouble();
                        // Playback controls never issue movement or actions to the authority.
                        live=!replayMode&&loading==null;
                        if(control.TryGetProperty("auto",out value)&&live)automatic=value.GetBoolean();
                        if(control.TryGetProperty("x",out value)&&live)x=Axis(value);
                        if(control.TryGetProperty("z",out value)&&live)z=Axis(value);
                        if(control.TryGetProperty("perform",out value)&&live)session.QueueAction(value.GetString(),false,now);
                        if(control.TryGetProperty("mode",out value)){
                            string next=value.GetString();session.Own.SetMode(next,now);mode=next;
                        }
                        if(control.TryGetProperty("correction",out value)){
                            string next=value.GetString();session.Own.SetCorrection(next);correction=next;
                        }
                        if(control.TryGetProperty("remote",out value)){
                            string next=value.GetString();session.Remote.SetMode(next,now);remote=next;
                        }
                        if(control.TryGetProperty("profile",out value)){
                            string next=value.GetString();if(next!="clean"&&next!="delay"&&next!="high")throw new InvalidDataException("profile");
                            session.ConfigureDelay(next=="clean"?0:next=="delay"?.15:.3);profile=next;
                        }
                        if(control.TryGetProperty("connection",out value)){
                            if(value.GetString()=="disconnect")session.Disconnect();
                            else if(value.GetString()=="reconnect")session.Connect(ExampleEndpoint.Normalize(backend),now);
                            else throw new InvalidDataException("connection");
                            x=z=0;automatic=false;
                        }
                    }
                    if(loading!=null&&loading.IsCompleted){
                        try{playback.Load(await loading);replayMode=true;playback.Play();}
                        catch(Exception error)when(error is HttpRequestException||error is JsonException||error is ArgumentException||error is InvalidOperationException||error is OperationCanceledException){notice="播放檔載入失敗，請重新錄製。";loadedID="";}
                        loading=null;
                    }
                    if(!replayMode&&loading==null&&automatic&&session.State.Players.TryGetValue(session.State.LocalPlayerID,out var own)){
                        if(own.x>5.5f)direction=-1;else if(own.x< -5.5f)direction=1;
                    }
                    session.Pump(now,replayMode||loading!=null?0:automatic?direction:x,replayMode||loading!=null||automatic?0:z);
                    if(replayMode)playback.Advance(Math.Max(0,now-previous));previous=now;
                    var state=replayMode?playback.State:session.State;
                    long identity=state.LocalPlayerID;
                    var players=state.Players.Values.Select(p=>{
                        double px=p.x,pz=p.z;
                        if(replayMode){if(playback.Sample(p.id,out float sx,out float sz)){px=sx;pz=sz;}}
                        else if(p.id==identity){px=session.Own.X;pz=session.Own.Z;}
                        else if(session.Remote.Sample(p.id,out float sx,out float sz)){px=sx;pz=sz;}
                        return new{id=p.id,x=px,z=pz,latestX=p.x,latestZ=p.z};
                    }).ToArray();
                    string output=JsonSerializer.Serialize(new{
                        view=replayMode?"playback":"live",loading=loading!=null,notice,connected=session.Connected,status=session.Status,
                        id=identity,tick=state.Tick,players,objects=state.Objects.Values,mode,correction,remote,profile,
                        liveTick=session.State.Tick,liveID=session.State.LocalPlayerID,pending=session.Feedback.Pending,
                        record=new{status=session.Recording.Status,id=session.Recording.ID,frames=session.Recording.Frames,duration=session.Recording.Duration,reason=session.Recording.Reason},
                        playback=new{loaded=playback.Loaded,playing=playback.Clock.Playing,ended=playback.Clock.Ended,position=playback.Position,duration=playback.Duration,speed=playback.Clock.Speed,frameIndex=playback.Clock.FrameIndex,frames=playback.FrameCount}
                    },Json);
                    using(var send=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){
                        send.CancelAfter(1000);await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(output)),WebSocketMessageType.Text,true,send.Token);
                    }
                    await Task.Delay(16,lifetime.Token);
                }
            }catch(Exception error)when(error is OperationCanceledException||error is WebSocketException||error is JsonException||error is InvalidDataException||error is InvalidOperationException||error is ArgumentException){
                // A viewer owns its live session and its offline playback independently.
            }finally{
                lifetime.Cancel();await reader;
                if(loading!=null)try{await loading;}catch(Exception){}
                playback.Clear();
                if(socket.State==WebSocketState.Open||socket.State==WebSocketState.CloseReceived){using var timeout=new CancellationTokenSource(500);try{await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"closed",timeout.Token);}catch(Exception){}}
            }
        });
        Console.WriteLine("Replay lesson: "+url+" (Go journal + actual C# PlaybackClock; Ctrl+C stops both services)");
        await app.RunAsync(url);
    }
    private static async Task<PlaybackFile> LoadClip(HttpClient client,string id,CancellationToken token)
    {return JsonSerializer.Deserialize<PlaybackFile>(await client.GetStringAsync("/recordings/"+id,token),Json);}
    private static float Axis(JsonElement value)
    {float result=value.GetSingle();if(!float.IsFinite(result)||Math.Abs(result)>1)throw new InvalidDataException("axis");return result;}
    private static async Task ReadControls(WebSocket socket,ChannelWriter<string> writer,CancellationTokenSource lifetime)
    {
        var buffer=new byte[1024];
        try{
            while(!lifetime.IsCancellationRequested){
                int count=0;WebSocketReceiveResult message;
                do{
                    if(count==buffer.Length)throw new InvalidDataException("control too large");
                    message=await socket.ReceiveAsync(new ArraySegment<byte>(buffer,count,buffer.Length-count),lifetime.Token);
                    if(message.MessageType==WebSocketMessageType.Close)return;
                    if(message.MessageType!=WebSocketMessageType.Text)throw new InvalidDataException("text required");count+=message.Count;
                }while(!message.EndOfMessage);
                if(!writer.TryWrite(Encoding.UTF8.GetString(buffer,0,count)))throw new InvalidDataException("control queue full");
            }
        }catch(Exception error)when(error is OperationCanceledException||error is WebSocketException||error is InvalidDataException){}
        finally{lifetime.Cancel();writer.TryComplete();}
    }
}
