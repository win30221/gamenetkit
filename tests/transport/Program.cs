using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Realtime.Core;
using Realtime.Simulation;
using Realtime.Transport;

public static class TransportTests
{
    private static void Require(bool value,string text){if(!value)throw new Exception(text);}
    public static async Task<int> Main(string[] args)
    {
        try{await Run();if(args.Length==2)await NativeWT(args[0],args[1]);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static async Task Until(Func<bool> predicate)
    {using var deadline=new CancellationTokenSource(5000);while(!predicate())await Task.Delay(5,deadline.Token);}
    private static async Task NativeWT(string executable,string fixture)
    {
        string saved=MessageTransport.NativeWebTransportExecutable;
        using var server=new Process{StartInfo=new ProcessStartInfo(fixture){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        server.Start();
        try{
            var config=JsonDocument.Parse(await server.StandardOutput.ReadLineAsync());
            string endpoint=config.RootElement.GetProperty("url").GetString(),hash=config.RootElement.GetProperty("hash").GetString();
            MessageTransport.NativeWebTransportExecutable=executable;Require(MessageTransport.SupportsWebTransport,"configured helper unavailable");
            using var one=new MessageTransport();using var two=new MessageTransport();
            int epoch=one.BeginAttempt();one.Open(epoch,endpoint,"boundary",hash,"",true);
            two.Open(two.BeginAttempt(),endpoint,"second",hash,"",true);
            string welcome=null,other=null;
            await Until(()=>{if(one.TryRead(out var a))welcome=a.JSON;if(two.TryRead(out var b))other=b.JSON;return welcome!=null&&other!=null;});
            Require(welcome==new string('x',32768)&&other=="reliable:second","native WT reliable streams changed");
            await one.Send("opaque datagram 雪");string echo=null;
            await Until(()=>{if(one.TryRead(out var a))echo=a.JSON;return echo!=null;});Require(echo=="opaque datagram 雪","WT datagram changed");
            one.Close();one.Receive(epoch,"stale");Require(!one.TryRead(out _),"WT old epoch leaked");
            await two.Send("peer survives");echo=null;await Until(()=>{if(two.TryRead(out var a))echo=a.JSON;return echo!=null;});Require(echo=="peer survives","WT close affected peer");
            one.Open(one.BeginAttempt(),endpoint,"hello",new string('0',64),"",true);
            string rejected=null;await Until(()=>{if(one.TryRead(out var a))rejected=a.JSON;return rejected!=null;});Require(rejected.Contains("connection_closed"),"wrong certificate accepted");
            one.Close();one.Open(one.BeginAttempt(),endpoint,"replacement",hash,"",true);welcome=null;
            await Until(()=>{if(one.TryRead(out var a))welcome=a.JSON;return welcome!=null;});Require(welcome=="reliable:replacement","WT replacement failed");
            two.InterruptForDiagnostics();Require(two.TryRead(out var interrupted)&&interrupted.JSON.Contains("connection_closed"),"WT diagnostics interruption did not notify owner");
            Console.WriteLine("TRANSPORT_NATIVE_WT PASS actual HTTP/3 pinned TLS, reliable 32 KiB, datagram UTF-8, two hidden helper processes, wrong pin rejection, close/replacement/diagnostic isolation");
        }finally{
            MessageTransport.NativeWebTransportExecutable=saved;
            server.StandardInput.Close();if(!server.WaitForExit(3000))server.Kill();
        }
    }
    private static async Task Run()
    {
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();
        await using var app=builder.Build();app.Urls.Add("http://127.0.0.1:0");app.UseWebSockets();
        var received=new ConcurrentQueue<string>();
        var releaseStall=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string exact= new string('界',MessageTransport.MaxMessageBytes/3)+"xx";
        app.MapGet("/ws/{mode}",async(HttpContext context,string mode)=>{
            using var socket=await context.WebSockets.AcceptWebSocketAsync();var buffer=new byte[65536];
            try{
                var hello=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
                Require(Encoding.UTF8.GetString(buffer,0,hello.Count)=="hello","hello changed");
                if(mode=="oversize"){
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(new string('x',MessageTransport.MaxMessageBytes+1))),WebSocketMessageType.Text,true,context.RequestAborted);
                }else if(mode=="invalid"){
                    await socket.SendAsync(new ArraySegment<byte>(new byte[]{0xff}),WebSocketMessageType.Text,true,context.RequestAborted);
                }else if(mode=="binary"){
                    await socket.SendAsync(new ArraySegment<byte>(new byte[]{1}),WebSocketMessageType.Binary,true,context.RequestAborted);
                }else if(mode=="fragmented-oversize"){
                    await socket.SendAsync(new ArraySegment<byte>(new byte[MessageTransport.MaxMessageBytes]),WebSocketMessageType.Text,false,context.RequestAborted);
                    await socket.SendAsync(new ArraySegment<byte>(new byte[]{1}),WebSocketMessageType.Text,true,context.RequestAborted);
                }else if(mode=="terminal"){
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("final result")),WebSocketMessageType.Text,true,context.RequestAborted);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"finished",context.RequestAborted);
                }else if(mode=="framing"){
                    await socket.SendAsync(new ArraySegment<byte>(Array.Empty<byte>()),WebSocketMessageType.Text,true,context.RequestAborted);
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(exact)),WebSocketMessageType.Text,false,context.RequestAborted);
                    await socket.SendAsync(new ArraySegment<byte>(Array.Empty<byte>()),WebSocketMessageType.Text,true,context.RequestAborted);
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("tail 雪")),WebSocketMessageType.Text,true,context.RequestAborted);
                }else if(mode=="stall"){
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ready")),WebSocketMessageType.Text,true,context.RequestAborted);
                    await releaseStall.Task.WaitAsync(context.RequestAborted);
                }else{
                    byte[] bytes=Encoding.UTF8.GetBytes(exact);
                    // Deliberately split inside a UTF-8 code point, not at a character boundary.
                    await socket.SendAsync(new ArraySegment<byte>(bytes,0,10001),WebSocketMessageType.Text,false,context.RequestAborted);
                    await socket.SendAsync(new ArraySegment<byte>(bytes,10001,bytes.Length-10001),WebSocketMessageType.Text,true,context.RequestAborted);
                }
                using var payload=new MemoryStream();
                while(socket.State==WebSocketState.Open){
                    var part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
                    if(part.MessageType==WebSocketMessageType.Close)break;payload.Write(buffer,0,part.Count);
                    if(part.EndOfMessage){received.Enqueue(Encoding.UTF8.GetString(payload.ToArray()));payload.SetLength(0);}
                }
            }catch(Exception error)when(error is WebSocketException||error is OperationCanceledException){}
        });
        await app.StartAsync();
        string root=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single().Replace("http:","ws:");
        using var one=new MessageTransport();using var two=new MessageTransport();
        Require(!await one.TrySend("before open"),"unopened transport accepted a send");
        bool closedThrows=false;try{_ = one.Send("before open");}catch(IOException){closedThrows=true;}
        Require(closedThrows,"closed Send did not fail synchronously");
        int first=one.BeginAttempt(),second=two.BeginAttempt();
        one.Open(first,root+"/ws/valid","hello","","",false);two.Open(second,root+"/ws/valid","hello","","",false);
        bool duplicateRejected=false;
        try{one.Open(first,root+"/ws/valid","hello","","",false);}catch(InvalidOperationException){duplicateRejected=true;}
        Require(duplicateRejected,"duplicate Open must not replace an active lifetime");
        string payload=null,peer=null;
        await Until(()=>{if(one.TryRead(out var a))payload=a.JSON;if(two.TryRead(out var b))peer=b.JSON;return payload!=null&&peer!=null;});
        Require(Encoding.UTF8.GetByteCount(payload)==MessageTransport.MaxMessageBytes&&payload==peer,"fragmented UTF-8 exact boundary");
        Require(await one.TrySend(payload),"open transport refused a send");await Until(()=>received.TryPeek(out _));Require(received.TryDequeue(out var outgoing)&&outgoing==payload,"wire send changed");
        try{await one.Send(payload+"x");throw new Exception("oversized send accepted");}catch(IOException){}
        Require(received.IsEmpty,"oversized send reached wire");
        one.Close();one.Receive(first,"stale");Require(!one.TryRead(out _),"old epoch reached inbox");
        Require(!await one.TrySend("after close"),"closed transport accepted a send");
        await two.Send("peer remains connected");await Until(()=>received.TryPeek(out _));Require(received.TryDequeue(out outgoing)&&outgoing=="peer remains connected","closing one socket closed peer");
        foreach(string mode in new[]{"oversize","fragmented-oversize","invalid","binary"}){
            int epoch=one.BeginAttempt();one.Open(epoch,root+"/ws/"+mode,"hello","","",false);
            string error=null;await Until(()=>{if(one.TryRead(out var value))error=value.JSON;return error!=null;});
            Require(error.Contains("connection_closed"),"invalid native payload reached application");
            bool reasonMatches=mode=="invalid"?(one.LastFailure.Reason=="invalid_utf8"||one.LastFailure.Reason=="websocket_error"):one.LastFailure.Reason=="invalid_framing";
            Require(one.LastFailure.Attempt==epoch && one.LastFailure.Operation=="connection_closed" && reasonMatches,"safe framing diagnostics: "+mode+" "+one.LastFailure.Reason);
            one.Close();Require(one.LastFailure.Reason==null,"replacement retained old diagnostics");
        }
        one.Open(one.BeginAttempt(),root+"/ws/framing","hello","","",false);
        var framing=new System.Collections.Generic.List<string>();
        await Until(()=>{while(one.TryRead(out var next))framing.Add(next.JSON);return framing.Count>=3;});
        Require(framing.SequenceEqual(new[]{"",exact,"tail 雪"}),"empty/final-empty fragments or buffer reuse changed payloads");one.Close();
        using(var busy=new MessageTransport()){
            busy.Open(busy.BeginAttempt(),root+"/ws/stall","hello","","",false);
            await Until(()=>busy.TryRead(out var ready)&&ready.JSON=="ready");
            Task<bool> blocked=null;
            for(int i=0;i<2048;i++){
                var send=busy.TrySend(payload);
                if(!send.IsCompleted&&await Task.WhenAny(send,Task.Delay(50))!=send){blocked=send;break;}
                Require(await send,"unstalled send failed");
            }
            Require(blocked!=null&&busy.Sending,"could not exercise real socket backpressure");
            Require(!await busy.TrySend("must not be submitted"),"busy transport accepted overlapping send");
            bool busyThrows=false;try{_ = busy.Send("must not be submitted either");}catch(InvalidOperationException){busyThrows=true;}
            Require(busyThrows,"busy Send did not fail synchronously");
            busy.Close();Require(!await blocked&&!busy.Sending,"cancelled send reported success or retained busy state");
            releaseStall.TrySetResult(true);
        }
        one.Open(one.BeginAttempt(),root+"/ws/terminal","hello","","",false);
        await Task.Delay(100);string terminal=null,closed=null;
        await Until(()=>{while(one.TryRead(out var next)){if(terminal==null)terminal=next.JSON;else closed=next.JSON;}return terminal!=null&&closed!=null;});
        Require(terminal=="final result"&&closed.Contains("connection_closed"),"normal close discarded preceding final result");one.Close();
        int current=one.BeginAttempt();one.Receive(current,payload);Require(one.TryRead(out var valid)&&valid.JSON==payload,"public receive rejected boundary");
        try{one.Receive(current,payload+"x");throw new Exception("public receive accepted oversize");}catch(IOException){}
        for(int i=0;i<10000;i++)one.Receive(current,"queued-"+i);
        Require(one.TryRead(out var overflow)&&overflow.JSON.Contains("receive_overflow"),"stalled receive did not produce a controlled failure");
        Require(!one.TryRead(out _),"overflow retained stale frames or repeated errors");
        one.Receive(current,"still paused");Require(!one.TryRead(out _),"failed epoch accepted another callback");
        int replacement=one.BeginAttempt();one.Receive(current,"stale");one.Receive(replacement,"recovered");
        Require(one.TryRead(out var recovered)&&recovered.JSON=="recovered"&&!one.TryRead(out _),"receive overflow did not reset cleanly on replacement");
        var simulation=new MessageConditioner(new ConditioningSettings());
        Require(simulation.Enqueue(MessageDirection.Receive,payload,false,0),"simulation rejected valid wire envelope");
        Require(!simulation.Enqueue(MessageDirection.Receive,payload+"x",false,0),"simulation accepted oversize");
        Require(simulation.TryTake(MessageDirection.Receive,0,out var simulated)&&simulated==payload,"simulation changed payload");
        two.Close();await app.StopAsync();
        Console.WriteLine("TRANSPORT_NATIVE PASS real WS busy/closed send results, fragmented UTF-8/exact 32 KiB/empty final frame/buffer reuse, send/receive overflow, invalid types/text, parallel instances, stale epochs and simulation envelope bound");
    }
}
