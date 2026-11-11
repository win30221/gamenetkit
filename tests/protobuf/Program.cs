using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GameNetKit.ProtobufExample;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Realtime.Protobuf;
using Realtime.Transport;

public static class ProtobufTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    private static async Task Until(Func<bool> ready)
    {
        using var deadline=new CancellationTokenSource(8000);
        while(!ready())await Task.Delay(5,deadline.Token);
    }
    private static ClientPacket Hello(uint version=1)=>new ClientPacket{Hello=new Hello{ProtocolVersion=version,Name="C# 測試"}};
    private sealed class Client : IDisposable
    {
        public readonly MessageTransport Transport=new MessageTransport();
        public readonly ProtobufSession<ClientPacket,ServerPacket> Session;
        public readonly List<ServerPacket> Packets=new List<ServerPacket>();
        public readonly List<string> Failures=new List<string>();
        private int pumpThread;
        public Client(Func<ServerPacket,bool> accepts=null)
        {
            Session=new ProtobufSession<ClientPacket,ServerPacket>(Transport,ServerPacket.Parser,accepts);
            Session.Received+=packet=>{Require(Environment.CurrentManagedThreadId==pumpThread,"Received ran outside the Pump caller");Packets.Add(packet);};
            Session.Failed+=code=>{Require(Environment.CurrentManagedThreadId==pumpThread,"Failed ran outside the Pump caller");Failures.Add(code);};
        }
        public void Open(string url,uint version=1)=>Session.Open(Transport.BeginAttempt(),url,Hello(version));
        public int Pump(int budget=64){pumpThread=Environment.CurrentManagedThreadId;return Session.Pump(budget);}
        public ulong ID=>Packets.FirstOrDefault(p=>p.Welcome!=null)?.Welcome.PlayerId??0;
        public WorldSnapshot Latest=>Packets.LastOrDefault(p=>p.Snapshot!=null)?.Snapshot;
        public void Dispose()=>Transport.Dispose();
    }
    public static async Task<int> Main(string[] args)
    {
        try{
            if(args.Length!=4)throw new ArgumentException("Expected Go server executable, port, Go fixture and C# fixture paths");
            CompatibilityCheck.Run();
            var fromGo=CompatibilityProbe.Parser.ParseFrom(File.ReadAllBytes(args[2]));
            Require(fromGo.HasOptionalValue&&fromGo.OptionalValue==0&&fromGo.Number==ulong.MaxValue&&fromGo.BodyCase==CompatibilityProbe.BodyOneofCase.Number&&fromGo.Values.SequenceEqual(new[]{9007199254740993UL,ulong.MaxValue}),"Go fixture semantics changed in C#");
            var forGo=new CompatibilityProbe{OptionalValue=0,Number=ulong.MaxValue};forGo.Values.Add(9007199254740993UL);forGo.Values.Add(ulong.MaxValue);File.WriteAllBytes(args[3],forGo.ToByteArray());
            await SharedWorld(args[0],int.Parse(args[1]));await LocalFailures();
            Console.WriteLine("PROTOBUF_NATIVE PASS official runtime compatibility, Go->C# semantics, typed Go WS world, owner delivery, ACK/stale/version/replacement, malformed/unknown-body/close errors and send validation");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static async Task SharedWorld(string executable,int port)
    {
        // Refuse to accidentally connect to an unrelated server already on this test port.
        var probe=new TcpListener(IPAddress.Loopback,port);probe.Start();probe.Stop();
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
        info.ArgumentList.Add("-listen");info.ArgumentList.Add("127.0.0.1:"+port);
        using var server=new Process{StartInfo=info};Require(server.Start(),"Go server failed to start");
        Task<string> stderr=server.StandardError.ReadToEndAsync(),stdout=server.StandardOutput.ReadToEndAsync();
        try{
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(1)};
            await Until(()=>{
                if(server.HasExited)throw new Exception("Go server exited before readiness: "+stderr.GetAwaiter().GetResult());
                try{return http.GetStringAsync("http://127.0.0.1:"+port+"/health").GetAwaiter().GetResult().Trim()=="ok";}catch(HttpRequestException){return false;}catch(TaskCanceledException){return false;}
            });
            string url="ws://127.0.0.1:"+port+"/ws-protobuf";
            using var one=new Client();using var two=new Client();one.Open(url);two.Open(url);
            await Task.Delay(150);Require(one.Packets.Count==0&&two.Packets.Count==0,"background receive invoked a typed application handler");
            Require(one.Pump(1)==1&&one.Packets.Count==1&&one.Packets[0].Welcome!=null,"Pump budget or welcome ordering failed");
            await Until(()=>{one.Pump();two.Pump();return one.ID!=0&&two.ID!=0&&one.Latest?.Players.Count==2&&two.Latest?.Players.Count==2;});
            Require(one.ID!=two.ID&&one.Latest.Players.Select(p=>p.Id).SequenceEqual(two.Latest.Players.Select(p=>p.Id)),"clients did not share the same world players");
            ulong firstID=one.ID;
            Require(await one.Session.TrySendAsync(new ClientPacket{Move=new MoveInput{Sequence=1,X=1,Z=-1}}),"typed move was not accepted locally");
            await Until(()=>{one.Pump();two.Pump();return one.Latest?.Players.Any(p=>p.Id==firstID&&p.Ack==1)==true&&two.Latest?.Players.Any(p=>p.Id==firstID&&p.Ack==1)==true;});
            var moved=two.Latest.Players.Single(p=>p.Id==firstID);Require(Math.Abs(moved.X-.1f)<.00001&&Math.Abs(moved.Z+.1f)<.00001,"Go did not apply the typed displacement");
            Require(one.Session.LastReceivedMS>0&&one.Failures.Count==0&&two.Failures.Count==0,"typed timestamps or unexpected transport failure");
            Require(await one.Session.TrySendAsync(new ClientPacket{Move=new MoveInput{Sequence=1,X=1}}),"duplicate move submission failed");
            await Until(()=>{one.Pump();two.Pump();return one.Packets.Any(p=>p.Error?.Code=="stale_sequence");});
            Require(one.Latest.Players.Single(p=>p.Id==firstID).Ack==1,"stale move changed ACK");
            // Unknown future oneof field 99 is valid protobuf with no known body.
            var future=ClientPacket.Parser.ParseFrom(new byte[]{0x9a,0x06,0x00});Require(future.BodyCase==ClientPacket.BodyOneofCase.None,"unknown client body unexpectedly recognized");
            Require(await one.Session.TrySendAsync(future),"unknown-field envelope failed local encode");
            await Until(()=>{one.Pump();return one.Packets.Any(p=>p.Error?.Code=="unsupported_body");});
            using(var wrong=new Client()){
                wrong.Open(url,2);await Until(()=>{wrong.Pump();return wrong.Packets.Any(p=>p.Error?.Code=="protocol_version_or_hello_required");});
                Require(wrong.ID==0,"mismatched protocol version entered world");
                await Until(()=>{wrong.Pump();return wrong.Failures.Contains("connection_closed");});
            }
            one.Transport.Close();Require(one.Pump()==0&&one.Session.LastReceivedMS==0,"close retained queued data or receive timestamp");one.Packets.Clear();one.Open(url);
            await Until(()=>{one.Pump();two.Pump();return one.ID!=0&&one.ID!=firstID&&one.Latest?.Players.Any(p=>p.Id==one.ID&&p.Ack==0)==true&&!one.Latest.Players.Any(p=>p.Id==firstID);});
            Require(!one.Latest.Players.Any(p=>p.Id==firstID),"replaced player's identity leaked into replacement world snapshot");
            one.Transport.Close();Require(one.Pump()==0,"close retained binary events");
            Require(!await one.Session.TrySendAsync(new ClientPacket{Move=new MoveInput{Sequence=1}}),"closed typed session accepted send");
            Expect<ArgumentNullException>(()=>{_ = one.Session.TrySendAsync(null);},"null send");
            Expect<ArgumentOutOfRangeException>(()=>{_ = one.Session.TrySendAsync(new ClientPacket{Hello=new Hello{Name=new string('x',32768)}});},"oversize protobuf send");
            Expect<ArgumentOutOfRangeException>(()=>one.Pump(0),"zero Pump budget");
            Expect<ArgumentOutOfRangeException>(()=>one.Pump(65),"large Pump budget");
        }finally{
            if(!server.HasExited){server.Kill();await server.WaitForExitAsync();}
            _ = await stderr;_ = await stdout;
        }
    }
    private static void Expect<T>(Action action,string reason) where T:Exception
    {try{action();}catch(T){return;}throw new Exception(reason+" did not reject synchronously");}
    private static async Task LocalFailures()
    {
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();
        await using var app=builder.Build();app.Urls.Add("http://127.0.0.1:0");app.UseWebSockets();
        app.MapGet("/fixture/{mode}",async(HttpContext context,string mode)=>{
            using var socket=await context.WebSockets.AcceptWebSocketAsync();var buffer=new byte[32768];
            try{
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
                byte[] payload=mode=="malformed"?new byte[]{0x52,0xff}:new byte[]{0x9a,0x06,0x00};
                if(mode=="close")payload=new ServerPacket{Welcome=new Welcome{PlayerId=77,ProtocolVersion=1}}.ToByteArray();
                await socket.SendAsync(new ArraySegment<byte>(payload),WebSocketMessageType.Binary,true,context.RequestAborted);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"fixture complete",context.RequestAborted);
            }catch(OperationCanceledException){}catch(WebSocketException){}
        });
        await app.StartAsync();string root=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single().Replace("http://","ws://");
        foreach(string mode in new[]{"malformed","unknown","close"}){
            using var client=new Client(p=>p.BodyCase!=ServerPacket.BodyOneofCase.None);client.Open(root+"/fixture/"+mode);
            await Until(()=>{client.Pump();return client.Failures.Count>0;});
            string expected=mode=="malformed"?"protobuf_decode_failed":mode=="unknown"?"unsupported_message":"connection_closed";
            Require(client.Failures.SequenceEqual(new[]{expected}),"unexpected typed failure for "+mode+": "+string.Join(",",client.Failures));
            Require(client.Packets.Count==(mode=="close"?1:0),"unknown/malformed message reached typed handler or final welcome was lost");
            Require(client.Pump()==0&&client.Failures.Count==1,"terminal typed failure repeated");
        }
        using(var permissive=new Client()){
            permissive.Open(root+"/fixture/unknown");await Until(()=>{permissive.Pump();return permissive.Packets.Count>0;});
            Require(permissive.Packets[0].BodyCase==ServerPacket.BodyOneofCase.None,"adapter overrode application unknown-body policy");
        }
        await app.StopAsync();
    }
}
