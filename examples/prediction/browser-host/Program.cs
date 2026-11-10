using System;
using System.IO;
using System.Linq;
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
using Realtime.Examples.PredictionLesson;

// Local visual teaching adapter. SDK/session/sampling run in C#, not a JS port.
public static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions {IncludeFields=true};
    private static double Now => MessageTransport.ClockMS/1000;
    public static async Task Main(string[] args)
    {
        string url=args[0], backend=args[1], web=args[2];
        var builder=WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app=builder.Build();
        app.UseWebSockets();
        Realtime.Examples.Browser.UnityRoutes.Map(app,backend,web);
        app.MapGet("/healthz",()=>Results.Text("ok\n"));
        app.MapGet("/",()=>Results.Content(File.ReadAllText(Path.Combine(web,"index.html")),"text/html; charset=utf-8"));
        app.MapGet("/app.js",()=>Results.Content(File.ReadAllText(Path.Combine(web,"app.js")),"text/javascript; charset=utf-8"));
        app.MapGet("/style.css",()=>Results.Content(File.ReadAllText(Path.Combine(web,"style.css")),"text/css; charset=utf-8"));
        app.MapGet("/lesson",async (HttpContext context)=> {
            if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
            using var socket=await context.WebSockets.AcceptWebSocketAsync();
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,app.Lifetime.ApplicationStopping);
            var controls=Channel.CreateBounded<string>(16);
            var reader=ReadControls(socket,controls.Writer,lifetime);
            using var session=new ExampleSession(s=>JsonSerializer.Deserialize<ServerMessage>(s,Json),i=>JsonSerializer.Serialize(i,Json));
            session.Connect(ExampleEndpoint.Normalize(backend),Now);
            string mode="prediction", correction="smooth", profile="delay", remote="adaptive";
            bool automatic=true;
            float x=0,z=0,direction=1;
            try {
                while(!lifetime.IsCancellationRequested) {
                    for(int n=0;n<16 && controls.Reader.TryRead(out var text);n++) {
                        using var document=JsonDocument.Parse(text);
                        var control=document.RootElement;
                        if(control.TryGetProperty("mode",out var value)) {
                            string next=value.GetString();
                            if(next!="authority" && next!="prediction")throw new InvalidDataException("mode");
                            session.Own.SetMode(next,Now);mode=next;
                        }
                        if(control.TryGetProperty("correction",out var correctionValue)) {
                            string next=correctionValue.GetString();
                            if(next!="snap" && next!="smooth")throw new InvalidDataException("correction");
                            session.Own.SetCorrection(next);correction=next;
                        }
                        if(control.TryGetProperty("profile",out value)) {
                            string next=value.GetString();
                            if(next!="clean" && next!="delay" && next!="high")throw new InvalidDataException("profile");
                            session.ConfigureDelay(next=="clean"?0:next=="delay"?.15:.3);profile=next;
                        }
                        if(control.TryGetProperty("remote",out value)) {
                            string next=value.GetString();
                            if(next!="latest" && next!="fixed" && next!="adaptive")throw new InvalidDataException("remote");
                            session.Remote.SetMode(next,Now);remote=next;
                        }
                        if(control.TryGetProperty("auto",out value))automatic=value.GetBoolean();
                        if(control.TryGetProperty("x",out value))x=Axis(value);
                        if(control.TryGetProperty("z",out value))z=Axis(value);
                        if(control.TryGetProperty("action",out value)) {
                            if(value.GetString()=="disconnect")session.Disconnect();
                            else if(value.GetString()=="reconnect")session.Connect(ExampleEndpoint.Normalize(backend),Now);
                            else if(value.GetString()=="bump"){session.QueueBump();continue;}
                            else throw new InvalidDataException("action");
                            x=z=0;
                        }
                    }
                    if(automatic && session.State.Players.TryGetValue(session.State.LocalPlayerID,out var own)) {
                        if(own.x>5.5f)direction=-1;else if(own.x< -5.5f)direction=1;
                    }
                    session.Pump(Now,automatic?direction:x,automatic?0:z);
                    var players=session.State.Players.Values.Select(p=> {
                        bool own=p.id==session.State.LocalPlayerID;
                        double px=p.x,pz=p.z;
                        if(own){px=session.Own.X;pz=session.Own.Z;}
                        else if(session.Remote.Sample(p.id,out float sx,out float sz)){px=sx;pz=sz;}
                        return new {id=p.id,x=px,z=pz,latestX=p.x,latestZ=p.z,
                            predictedX=own?session.Own.PredictedX:p.x,predictedZ=own?session.Own.PredictedZ:p.z};
                    }).ToArray();
                    long ack=session.State.Players.TryGetValue(session.State.LocalPlayerID,out var local)?local.ack:0;
                    string output=JsonSerializer.Serialize(new {status=session.Status,connected=session.Connected,id=session.State.LocalPlayerID,
                        tick=session.State.Tick,ack,error=session.Own.LastError,offset=session.Own.Offset,replays=session.Own.Metrics.ReplayedSteps,
                        corrections=session.Own.Corrections,mode,correction,profile,remote,remoteBuffer=session.Remote.Metrics.BufferSeconds*1000,players});
                    using(var send=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
                        send.CancelAfter(1000);
                        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(output)),WebSocketMessageType.Text,true,send.Token);
                    }
                    await Task.Delay(16,lifetime.Token);
                }
            } catch(Exception error) when(error is OperationCanceledException || error is WebSocketException || error is JsonException || error is InvalidDataException || error is InvalidOperationException) {
                // Closing this viewer disposes only its own SDK session.
            } finally {
                lifetime.Cancel();await reader;
                if(socket.State==WebSocketState.Open || socket.State==WebSocketState.CloseReceived) {
                    using var timeout=new CancellationTokenSource(500);
                    try{await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"closed",timeout.Token);}catch(Exception){}
                }
            }
        });
        Console.WriteLine("Prediction lesson: "+url+" (actual C# SDK; Ctrl+C stops both services)");
        await app.RunAsync(url);
    }
    private static float Axis(JsonElement value)
    {float result=value.GetSingle();if(!float.IsFinite(result) || Math.Abs(result)>1)throw new InvalidDataException("axis");return result;}
    private static async Task ReadControls(WebSocket socket,ChannelWriter<string> writer,CancellationTokenSource lifetime)
    {
        var buffer=new byte[1024];
        try {
            while(!lifetime.IsCancellationRequested) {
                int count=0;WebSocketReceiveResult message;
                do {
                    if(count==buffer.Length)throw new InvalidDataException("control too large");
                    message=await socket.ReceiveAsync(new ArraySegment<byte>(buffer,count,buffer.Length-count),lifetime.Token);
                    if(message.MessageType==WebSocketMessageType.Close)return;
                    if(message.MessageType!=WebSocketMessageType.Text)throw new InvalidDataException("text required");
                    count+=message.Count;
                } while(!message.EndOfMessage);
                if(!writer.TryWrite(Encoding.UTF8.GetString(buffer,0,count)))throw new InvalidDataException("control queue full");
            }
        } catch(Exception error) when(error is OperationCanceledException || error is WebSocketException || error is InvalidDataException) {}
        finally {lifetime.Cancel();writer.TryComplete();}
    }
}
