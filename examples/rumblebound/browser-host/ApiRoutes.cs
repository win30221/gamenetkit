using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Rumblebound.Example.Hosting
{
    public static class ApiRoutes
    {
        public static void Map(WebApplication app,string backend)
        {
            var client=new HttpClient(new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=1024*1024};
            app.Lifetime.ApplicationStopped.Register(client.Dispose);
            app.MapMethods("/api/{**path}",new[]{"GET","HEAD","POST"},async(HttpContext context)=>{
                string origin=context.Request.Headers.Origin.ToString();
                if(origin.Length>0&&origin!=context.Request.Scheme+"://"+context.Request.Host){context.Response.StatusCode=403;return;}
                using var outgoing=new HttpRequestMessage(new HttpMethod(context.Request.Method),backend.TrimEnd('/')+context.Request.Path.ToString()+context.Request.QueryString.ToString());
                if(context.Request.Headers.TryGetValue("Authorization",out var auth))outgoing.Headers.TryAddWithoutValidation("Authorization",auth.ToString());
                if(origin.Length>0)outgoing.Headers.TryAddWithoutValidation("Origin",origin);
                bool google=context.Request.Path.StartsWithSegments("/api/auth/google");
                if(google&&context.Request.Cookies.TryGetValue("rb_google_attempt",out var attempt))outgoing.Headers.TryAddWithoutValidation("Cookie","rb_google_attempt="+attempt);
                if(context.Request.Method=="POST"){
                    using var data=new MemoryStream();byte[] buffer=new byte[4096];int count;
                    while((count=await context.Request.Body.ReadAsync(buffer,0,buffer.Length,context.RequestAborted))>0){if(data.Length+count>16384){context.Response.StatusCode=413;return;}data.Write(buffer,0,count);}
                    outgoing.Content=new ByteArrayContent(data.ToArray());outgoing.Content.Headers.ContentType=MediaTypeHeaderValue.Parse(context.Request.ContentType??"application/json");
                }
                try{
                    using var response=await client.SendAsync(outgoing,HttpCompletionOption.ResponseContentRead,context.RequestAborted);
                    context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=response.Content.Headers.ContentType?.ToString()??"application/json";context.Response.Headers.CacheControl="no-store";
                    context.Response.ContentLength=response.Content.Headers.ContentLength;
                    if(google&&response.Headers.TryGetValues("Set-Cookie",out var cookies))foreach(string cookie in cookies)if(cookie.StartsWith("rb_google_attempt=",StringComparison.Ordinal))context.Response.Headers.Append("Set-Cookie",cookie+(context.Request.IsHttps&&!cookie.Contains("Secure",StringComparison.OrdinalIgnoreCase)?"; Secure":""));
                    if(context.Request.Method!="HEAD")await response.Content.CopyToAsync(context.Response.Body);
                }catch(HttpRequestException){context.Response.StatusCode=502;}
            });
        }
    }
}
