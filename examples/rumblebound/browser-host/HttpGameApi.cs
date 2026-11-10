using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rumblebound.Example.Battle;

namespace Rumblebound.Example.Hosting
{
    // Native/host adapter. Unity supplies a UnityWebRequest implementation of
    // the same interface; no HTTP completion mutates GameFlow directly.
    public sealed class HttpGameApi:IGameApi,IDisposable
    {
        private readonly HttpClient http;
        public HttpGameApi(string origin)
        {
            var address=new Uri(origin);if(address.Scheme!="http"&&address.Scheme!="https")throw new ArgumentException("HTTP origin required");
            http=new HttpClient(new HttpClientHandler{UseCookies=false}){BaseAddress=address,Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=1024*1024};
        }
        public async Task<ApiReply> Request(string path,string body,string credential,CancellationToken cancellation)
        {
            if(!path.StartsWith("/api/",StringComparison.Ordinal))throw new ArgumentException("Game API path required");
            using(var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,path)){
                if(!string.IsNullOrEmpty(credential))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",credential);
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseContentRead,cancellation).ConfigureAwait(false))
                    return new ApiReply{Status=(int)response.StatusCode,Body=await response.Content.ReadAsStringAsync().ConfigureAwait(false)};
            }
        }
        public void Dispose()=>http.Dispose();
    }
}
