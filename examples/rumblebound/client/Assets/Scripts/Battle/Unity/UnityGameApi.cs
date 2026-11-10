using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed class UnityGameJson:IGameJson
    {
        private static readonly JsonSerializerSettings settings=new JsonSerializerSettings{MaxDepth=64,TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,DateParseHandling=DateParseHandling.None};
        public T Read<T>(string text){try{return JsonConvert.DeserializeObject<T>(text,settings);}catch(JsonException error){throw new FormatException("Invalid game JSON",error);}}
        public string Write<T>(T value)=>JsonConvert.SerializeObject(value,settings);
    }
    // Requests start, complete and abort on Unity's main thread. Cancellation is
    // observed by the coroutine; callbacks never mutate GameFlow directly.
    public sealed class UnityGameApi:MonoBehaviour,IGameApi
    {
        private sealed class Pending{public UnityWebRequest Request;public TaskCompletionSource<ApiReply> Result;}
        private readonly List<Pending> active=new List<Pending>();
        public string Origin{private get;set;}
        public Task<ApiReply> Request(string path,string body,string credential,CancellationToken cancellation)
        {
            if(!isActiveAndEnabled)throw new ObjectDisposedException(nameof(UnityGameApi));
            var result=new TaskCompletionSource<ApiReply>();
            StartCoroutine(Send(path,body,credential,cancellation,result));return result.Task;
        }
        private IEnumerator Send(string path,string body,string credential,CancellationToken token,TaskCompletionSource<ApiReply> result)
        {
            using(var request=new UnityWebRequest(Origin+path,body==null?"GET":"POST")){
                var pending=new Pending{Request=request,Result=result};active.Add(pending);
                try{
                    request.downloadHandler=new DownloadHandlerBuffer();request.timeout=10;
                    if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
                    if(!string.IsNullOrEmpty(credential))request.SetRequestHeader("Authorization","Bearer "+credential);
                    var operation=request.SendWebRequest();
                    while(!operation.isDone){if(token.IsCancellationRequested){request.Abort();result.TrySetCanceled();yield break;}yield return null;}
                    if(token.IsCancellationRequested){result.TrySetCanceled();yield break;}
                    if(request.downloadedBytes>1024*1024){result.TrySetException(new InvalidOperationException("API response limit"));yield break;}
                    if(request.result==UnityWebRequest.Result.ConnectionError||request.result==UnityWebRequest.Result.DataProcessingError)result.TrySetException(new InvalidOperationException("API unavailable"));
                    else result.TrySetResult(new ApiReply{Status=(int)request.responseCode,Body=request.downloadHandler.text});
                }finally{active.Remove(pending);}
            }
        }
        private void OnDisable(){foreach(var p in active.ToArray()){p.Request.Abort();p.Result.TrySetCanceled();p.Request.Dispose();}StopAllCoroutines();active.Clear();}
    }
}
