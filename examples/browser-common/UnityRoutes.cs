using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Realtime.Examples.Browser
{
    // Raw proxy and built-player files only; /lesson keeps using its C# session.
    public static class UnityRoutes
    {
        private const int MaxFrame=32*1024;
        public static void Map(WebApplication app,string backend,string web,string missingBuildPage=null,bool rootPlayer=false)
        {
            string lesson=Directory.GetParent(Path.GetFullPath(web)).FullName;
            string framework=Directory.GetParent(Directory.GetParent(lesson).FullName).FullName;
            string runtime=Path.Combine(framework,"packages/unity/transport/Browser/transport.js");
            string viewStyle=Path.Combine(framework,"examples/browser-common/unity-view.css");
            string buildRoot=Path.GetFullPath(Path.Combine(lesson,"client/Builds/WebGL"));
            app.MapGet("/transport.js",()=>Results.File(runtime,"text/javascript; charset=utf-8"));
            app.MapGet("/unity-view.css",()=>Results.File(viewStyle,"text/css; charset=utf-8"));
            app.MapGet("/mobile-view.js",()=>Results.File(Path.Combine(framework,"examples/browser-common/mobile-view.js"),"text/javascript; charset=utf-8"));
            IResult Asset(HttpContext context,string asset){
                string relative=string.IsNullOrEmpty(asset)?"index.html":asset;
                string path=Path.GetFullPath(Path.Combine(buildRoot,relative));
                if(relative=="index.html"&&!File.Exists(path)&&missingBuildPage!=null){
                    context.Response.Headers.CacheControl="no-store";
                    return Results.Content(missingBuildPage,"text/html; charset=utf-8",statusCode:503);
                }
                if(!path.StartsWith(buildRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return Results.NotFound();
                string extension=Path.GetExtension(path).ToLowerInvariant();
                string content=extension==".html"?"text/html; charset=utf-8":extension==".js"?"text/javascript":extension==".wasm"?"application/wasm":extension==".css"?"text/css":extension==".json"?"application/json":"application/octet-stream";
                context.Response.Headers.CacheControl="no-store";
                if(relative=="index.html")return Results.Content(File.ReadAllText(path).Replace("</head>","<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,viewport-fit=cover\"><link rel=\"stylesheet\" href=\"/unity-view.css\"><script type=\"module\" src=\"/mobile-view.js\"></script></head>"),content);
                return Results.File(path,content);
            }
            if(rootPlayer){
                app.MapMethods("/",new[]{"GET","HEAD"},(HttpContext context)=>Asset(context,null));
                foreach(string folder in new[]{"Build","TemplateData","StreamingAssets"}){
                    string prefix=folder;
                    app.MapMethods("/"+prefix+"/{**asset}",new[]{"GET","HEAD"},(HttpContext context,string asset)=>Asset(context,prefix+"/"+asset));
                }
                app.MapGet("/rf-battle-build.txt",(HttpContext context)=>Asset(context,"rf-battle-build.txt"));
            }
            // ASP.NET endpoint matching ignores a trailing slash, so /unity's
            // handler must serve the directory path rather than redirect itself.
            app.MapGet("/unity",(HttpContext context)=>context.Request.Path.Value.EndsWith("/",StringComparison.Ordinal)?Asset(context,null):Results.Redirect("/unity/"));
            app.MapGet("/unity/{**asset}",(HttpContext context,string asset)=>Asset(context,asset));
            app.MapGet("/ws",async(HttpContext context)=>{
                if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
                string origin=context.Request.Headers.Origin.ToString();
                if(origin.Length>0&&(!Uri.TryCreate(origin,UriKind.Absolute,out var source)||
                    !string.Equals(source.GetLeftPart(UriPartial.Authority),context.Request.Scheme+"://"+context.Request.Host,StringComparison.OrdinalIgnoreCase))){context.Response.StatusCode=403;return;}
                using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,app.Lifetime.ApplicationStopping);
                using var upstream=new ClientWebSocket();
                var address=new UriBuilder(backend){Scheme="ws",Path="/ws",Query="",Fragment=""};
                try{
                    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){timeout.CancelAfter(8000);await upstream.ConnectAsync(address.Uri,timeout.Token);}
                }catch(Exception){context.Response.StatusCode=502;return;}
                using var downstream=await context.WebSockets.AcceptWebSocketAsync();
                try{
                    var send=Forward(downstream,upstream,lifetime.Token);
                    var receive=Forward(upstream,downstream,lifetime.Token);
                    await Task.WhenAny(send,receive);lifetime.Cancel();upstream.Abort();downstream.Abort();
                    await Task.WhenAll(send,receive);
                }catch(Exception error)when(error is WebSocketException||error is OperationCanceledException||error is IOException){}
                finally{lifetime.Cancel();upstream.Abort();downstream.Abort();}
            });
        }
        private static async Task Forward(WebSocket source,WebSocket target,CancellationToken token)
        {
            byte[] data=new byte[MaxFrame];
            while(!token.IsCancellationRequested){
                int count=0;WebSocketReceiveResult part;
                do{
                    if(count==data.Length)throw new IOException("Message exceeds limit");
                    part=await source.ReceiveAsync(new ArraySegment<byte>(data,count,data.Length-count),token);
                    if(part.MessageType==WebSocketMessageType.Close)return;
                    if(part.MessageType!=WebSocketMessageType.Text)throw new IOException("Expected text message");
                    count+=part.Count;
                }while(!part.EndOfMessage);
                await target.SendAsync(new ArraySegment<byte>(data,0,count),WebSocketMessageType.Text,true,token);
            }
        }
    }
}
