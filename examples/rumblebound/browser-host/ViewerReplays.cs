using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rumblebound.Example.Battle;

namespace Rumblebound.Example.Hosting
{
    // The HTTP upload owns bytes only. Validation/export executes on the same
    // viewer loop that owns GameFlow; UI sockets never carry large replay files.
    public sealed class ViewerReplays:IDisposable
    {
        private sealed class Request{public bool Import;public string Text;public TaskCompletionSource<string> Reply=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);}
        private static readonly ConcurrentDictionary<string,ViewerReplays> viewers=new ConcurrentDictionary<string,ViewerReplays>();
        private static readonly SemaphoreSlim capacity=new SemaphoreSlim(8);
        private readonly SemaphoreSlim http=new SemaphoreSlim(1);
        private readonly Channel<Request> requests=Channel.CreateBounded<Request>(2);
        public string ID{get;}=Guid.NewGuid().ToString("N");
        public static ViewerReplays Create(){if(!capacity.Wait(0))return null;var viewer=new ViewerReplays();viewers[viewer.ID]=viewer;return viewer;}
        public void Pump(GameFlow flow)
        {
            if(!requests.Reader.TryRead(out var request))return;
            if(request.Reply.Task.IsCanceled)return;
            try{if(request.Import){flow.LoadPlayback(request.Text);request.Reply.TrySetResult("ok");}else request.Reply.TrySetResult(flow.Recording.Export());}
            catch(Exception error)when(error is InvalidOperationException||error is ArgumentException||error is System.Text.Json.JsonException||error is FormatException){request.Reply.TrySetException(new InvalidDataException("重播格式不正確，或尚未離開房間。"));}
        }
        public void Dispose(){viewers.TryRemove(ID,out _);requests.Writer.TryComplete();while(requests.Reader.TryRead(out var r))r.Reply.TrySetCanceled();capacity.Release();}
        public static void Map(WebApplication app)
        {
            app.MapMethods("/viewer/{id}/replay",new[]{"GET","POST"},async(HttpContext context,string id)=>{
                string origin=context.Request.Headers.Origin.ToString();
                if(origin.Length>0&&origin!=context.Request.Scheme+"://"+context.Request.Host){context.Response.StatusCode=403;return;}
                if(!viewers.TryGetValue(id,out var viewer)){context.Response.StatusCode=404;return;}
                if(!viewer.http.Wait(0)){context.Response.StatusCode=429;return;}
                try{
                bool import=context.Request.Method=="POST";string text=null;
                if(import){
                    var bodyLimit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(bodyLimit!=null&&!bodyLimit.IsReadOnly)bodyLimit.MaxRequestBodySize=BattleRecording.MaxBytes;
                    using var bytes=new MemoryStream();byte[] buffer=new byte[65536];int count;
                    while((count=await context.Request.Body.ReadAsync(buffer,0,buffer.Length,context.RequestAborted))>0){if(bytes.Length+count>BattleRecording.MaxBytes){context.Response.StatusCode=413;return;}bytes.Write(buffer,0,count);}
                    try{text=new UTF8Encoding(false,true).GetString(bytes.GetBuffer(),0,(int)bytes.Length);}catch(DecoderFallbackException){context.Response.StatusCode=400;return;}
                }
                var request=new Request{Import=import,Text=text};if(!viewer.requests.Writer.TryWrite(request)){context.Response.StatusCode=429;return;}
                try{
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);timeout.CancelAfter(10000);
                    using var cancel=timeout.Token.Register(()=>request.Reply.TrySetCanceled());
                    string result=await request.Reply.Task.WaitAsync(timeout.Token);
                    context.Response.ContentType=import?"text/plain; charset=utf-8":"application/json; charset=utf-8";context.Response.Headers.CacheControl="no-store";
                    if(!import)context.Response.Headers.ContentDisposition="attachment; filename=rumblebound.playback.json";
                    await context.Response.WriteAsync(result,context.RequestAborted);
                }catch(InvalidDataException){context.Response.StatusCode=400;await context.Response.WriteAsync("重播格式不正確，或尚未離開房間。",context.RequestAborted);}
                catch(OperationCanceledException){if(!context.RequestAborted.IsCancellationRequested)context.Response.StatusCode=408;}
                }finally{viewer.http.Release();}
            });
        }
    }
}
