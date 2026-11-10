using System;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public static class MinimalStartupTests
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Until(Func<bool> check)
    {using var timeout=new CancellationTokenSource(5000);while(!check())await Task.Delay(5,timeout.Token);}
    public static async Task<int> Main()
    {
        var hello=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();
        await using var app=builder.Build();app.Urls.Add("http://127.0.0.1:18091");app.UseWebSockets();
        app.MapGet("/ws",async(HttpContext context)=>{
            using var socket=await context.WebSockets.AcceptWebSocketAsync();var buffer=new byte[1024];
            try{
                var first=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
                Require(Encoding.UTF8.GetString(buffer,0,first.Count)=="{\"type\":\"hello\"}","minimal hello changed");hello.TrySetResult(true);
                var reading=socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
                await snapshot.Task.WaitAsync(context.RequestAborted);
                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"snapshot\",\"tick\":2,\"x\":1}")),WebSocketMessageType.Text,true,context.RequestAborted);
                var received=await reading;input.TrySetResult(Encoding.UTF8.GetString(buffer,0,received.Count));
                await failure.Task.WaitAsync(context.RequestAborted);
                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"error\",\"code\":\"send_failed\"}")),WebSocketMessageType.Text,true,context.RequestAborted);failed.TrySetResult(true);
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer),context.RequestAborted);
            }catch(Exception error)when(error is WebSocketException||error is OperationCanceledException){}
        });
        var client=new MinimalClient();
        void Invoke(string method)=>typeof(MinimalClient).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(client,null);
        try{
            await app.StartAsync();
            UnityEngine.Time.realtimeSinceStartupAsDouble=0;Invoke("Start");Invoke("Update");
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for(int i=1;i<=5;i++){UnityEngine.Time.realtimeSinceStartupAsDouble=i*.01;Invoke("Update");await Task.Delay(5);}
            client.OnWire("{\"type\":\"pong\"}");client.OnWire("{\"type\":\"snapshot\",\"tick\":-1,\"x\":1}");Invoke("Update");
            Require(!input.Task.IsCompleted,"minimal sent input before its first valid snapshot");
            snapshot.TrySetResult(true);
            await Until(()=>{UnityEngine.Time.realtimeSinceStartupAsDouble+=.01;Invoke("Update");return input.Task.IsCompleted;});
            using(var message=JsonDocument.Parse(await input.Task))Require(message.RootElement.GetProperty("type").GetString()=="input","minimal did not send input after snapshot");
            failure.TrySetResult(true);await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var state=typeof(MinimalClient).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic);
            await Until(()=>{UnityEngine.Time.realtimeSinceStartupAsDouble+=.01;Invoke("Update");return state.GetValue(client)==null;});
            Invoke("Update");
            Console.WriteLine("MINIMAL_STARTUP PASS actual client Start/Update waits for valid initial snapshot, sends through real WS after ready, and stops on terminal error; Unity view is stubbed");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{Invoke("OnDestroy");snapshot.TrySetResult(true);failure.TrySetResult(true);await app.StopAsync();}
    }
}

// Exercise the actual client lifecycle without starting the Unity Editor.
// Only view/input APIs are stubbed; MessageTransport uses a real WebSocket.
namespace UnityEngine
{
    public class MonoBehaviour{public GameObject gameObject=new GameObject("MinimalClient");}
    public sealed class GameObject
    {
        public string name;public Transform transform=new Transform();public GameObject(string name){this.name=name;}
        public T AddComponent<T>() where T:new()=>new T();public static GameObject CreatePrimitive(PrimitiveType value)=>new GameObject("marker");
    }
    public sealed class Transform{public Vector3 position;}
    public sealed class Camera{public Transform transform=new Transform();}
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public enum PrimitiveType{Cube}public enum KeyCode{RightArrow,LeftArrow}public enum RuntimeInitializeLoadType{AfterSceneLoad}
    [AttributeUsage(AttributeTargets.Method)]public sealed class RuntimeInitializeOnLoadMethodAttribute:Attribute{public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType value){}}
    public static class Time{public static double realtimeSinceStartupAsDouble;}
    public static class Input{public static bool GetKey(KeyCode value)=>false;}
    public static class JsonUtility{public static T FromJson<T>(string json)=>JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions{IncludeFields=true});}
    public static class Debug{public static void Log(string value)=>Console.WriteLine(value);}
    public static class Application{public static void Quit(int code){}}
}
namespace UnityEngine.Scripting{[AttributeUsage(AttributeTargets.Method)]public sealed class PreserveAttribute:Attribute{}}
