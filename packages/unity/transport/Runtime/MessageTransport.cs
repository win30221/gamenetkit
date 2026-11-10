using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Realtime.Transport
{
    // Owns native sockets, cancellation, bounded receive framing, send
    // serialization and stale-completion isolation. No Unity scene/UI state.
    // Open/BeginAttempt/Close/Dispose and sending belong to one application owner;
    // the receive callbacks alone may run on background threads.
    public sealed class MessageTransport : IDisposable
    {
        // Wire envelope bound. Application codecs may impose a smaller raw-payload limit.
        public const int MaxMessageBytes = 32 * 1024;
        private static readonly UTF8Encoding StrictUTF8=new UTF8Encoding(false,true);
        public string BrowserRuntimeURL {get;set;}="/transport.js";
        public struct Incoming { public int Attempt; public string JSON; public double ReceivedMS; }
        public readonly struct FailureDiagnostic
        {
            public int Attempt { get; }
            public string Operation { get; }
            public string Reason { get; }
            internal FailureDiagnostic(int attempt, string operation, string reason)
            { Attempt = attempt; Operation = operation; Reason = reason; }
        }
        private FailureDiagnostic lastFailure;
        public FailureDiagnostic LastFailure { get { lock(inboxGate) return lastFailure; } }
        public static double ClockMS => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);
        private readonly Queue<Incoming> inbox = new Queue<Incoming>(MaxPendingMessages);
        private readonly object inboxGate = new object();
        private bool receiveFailed;
        private bool sending, opened, openStarted;
        public const int MaxPendingMessages = 64;
        private ClientWebSocket socket;
        private CancellationTokenSource lifetime;
#if !UNITY_WEBGL || UNITY_EDITOR
        private NativeWebTransport nativeWebTransport;
#endif
        public int Epoch { get; private set; }
        public bool Sending { get { lock(inboxGate)return sending; } }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int RB_SupportsWebTransport();
        public static bool SupportsWebTransport => RB_SupportsWebTransport() != 0;
        private int browserHandle;
        [DllImport("__Internal")] private static extern int RB_Create();
        [DllImport("__Internal")] private static extern void RB_Open(int handle,string url,string hello,string hash,string target,int useWT,string runtimeURL);
        [DllImport("__Internal")] private static extern int RB_TrySendTo(int handle,string json);
        [DllImport("__Internal")] private static extern void RB_CloseFor(int handle);
        [DllImport("__Internal")] private static extern void RB_Destroy(int handle);
#endif
#if !UNITY_WEBGL || UNITY_EDITOR
        public static string NativeWebTransportExecutable {get;set;}
        public static bool SupportsWebTransport => !string.IsNullOrEmpty(NativeWebTransportExecutable)&&Path.IsPathRooted(NativeWebTransportExecutable)&&File.Exists(NativeWebTransportExecutable);
#endif

        public int BeginAttempt()
        {
            // Starting a replacement always ends the previous lifetime, even
            // when a caller has not explicitly disconnected first.
            Close();
            return Epoch;
        }

        public void Open(int attempt, string url, string hello, string hash, string target, bool webTransport)
        {
            if (attempt != Epoch) return;
            ValidateText(hello);
            lock(inboxGate) {
                if (openStarted) throw new InvalidOperationException("Begin a new attempt before opening another transport");
                openStarted = true;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            if(browserHandle==0)browserHandle=RB_Create();
            if(browserHandle==0)throw new IOException("Browser connection capacity exceeded");
            RB_Open(browserHandle,url,hello,hash,target,webTransport?1:0,BrowserRuntimeURL);
            lock(inboxGate){if(attempt==Epoch)opened=true;}
#else
            if(webTransport){
                if(!SupportsWebTransport)throw new NotSupportedException("Configure NativeWebTransportExecutable before using native WebTransport");
                nativeWebTransport=new NativeWebTransport(NativeWebTransportExecutable,url,hello,hash,value=>Receive(attempt,value),()=>QueueError(attempt,"connection_closed"));
                lock(inboxGate){if(attempt==Epoch)opened=true;}return;
            }
            lifetime = new CancellationTokenSource();
            socket = new ClientWebSocket();
            _ = ReceiveNative(socket, lifetime.Token, attempt, url, hello);
#endif
        }

        public bool TryRead(out Incoming message)
        {
            lock(inboxGate){
                while (inbox.Count>0) {message=inbox.Dequeue();if (message.Attempt == Epoch) return true;}
                message = default; return false;
            }
        }

        private async Task ReceiveNative(ClientWebSocket current, CancellationToken ct, int attempt, string url, string hello)
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                { timeout.CancelAfter(8000); await current.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false); }
                // Keep network reads off Unity's synchronization context. Arrival
                // timestamps must not wait for the next rendered frame.
                await SendNative(current, hello, ct).ConfigureAwait(false);
                lock(inboxGate){if(attempt!=Epoch)return;opened=true;}
                // The extra byte detects overflow while still accepting a
                // maximum-size payload followed by an empty final fragment.
                var bytes = new byte[MaxMessageBytes+1];
                while (!ct.IsCancellationRequested)
                {
                    int count=0;
                    WebSocketReceiveResult part;
                    do
                    {
                        part = await current.ReceiveAsync(new ArraySegment<byte>(bytes,count,bytes.Length-count), ct).ConfigureAwait(false);
                        if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Connection closed");
                        count+=part.Count;
                        if (part.MessageType != WebSocketMessageType.Text || count > MaxMessageBytes)
                            throw new InvalidDataException("Invalid server message");
                    } while (!part.EndOfMessage);
                    if (attempt != Epoch) return;
                    Receive(attempt, StrictUTF8.GetString(bytes,0,count));
                }
            }
            catch (Exception error) { QueueError(attempt, "connection_closed", Reason(error, ct)); }
        }

        private static Task SendNative(ClientWebSocket target, string message, CancellationToken ct)
        { return target.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, ct); }

        // Rejections are synchronous so existing fire-and-forget callers do not
        // create an unobserved faulted Task. I/O failures use the error inbox.
        public Task Send(string json) => StartSend(json,true);
        public Task<bool> TrySend(string json) => StartSend(json,false);

        private Task<bool> StartSend(string json,bool required)
        {
            ValidateText(json);
            lock(inboxGate){
                bool connected=opened&&!receiveFailed;
#if !UNITY_WEBGL || UNITY_EDITOR
                connected=connected&&(nativeWebTransport!=null||(socket!=null&&socket.State==WebSocketState.Open&&lifetime!=null&&!lifetime.IsCancellationRequested));
#endif
                if(!connected){if(required)throw new IOException("Transport is not open");return Task.FromResult(false);}
                if(sending){if(required)throw new InvalidOperationException("Transport is busy; retry with TrySend");return Task.FromResult(false);}
                sending=true;
#if UNITY_WEBGL && !UNITY_EDITOR
                try{
                    if(RB_TrySendTo(browserHandle,json)==0){
                        if(required)throw new IOException("Browser transport is not ready for this send");
                        return Task.FromResult(false);
                    }
                    return Task.FromResult(true);
                }finally{sending=false;}
#else
                return SendAccepted(json,Epoch,socket,lifetime,nativeWebTransport);
#endif
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task<bool> SendAccepted(string json,int attempt,ClientWebSocket target,CancellationTokenSource source,NativeWebTransport webTransport)
        {
            try
            {
                if(webTransport!=null)await webTransport.Send(json);
                else await SendNative(target, json, source.Token);
                lock(inboxGate)return attempt==Epoch&&!receiveFailed;
            }
            catch (Exception error) { QueueError(attempt, "send_failed", Reason(error, default));return false; }
            finally { lock(inboxGate){if (attempt == Epoch) sending = false;} }
        }
#endif

        // Fixed reason codes only: exception messages may contain URLs or credentials.
        private static string Reason(Exception error, CancellationToken lifetimeToken)
        {
            if (error is OperationCanceledException) return lifetimeToken.IsCancellationRequested ? "cancelled" : "timeout";
            if (error is DecoderFallbackException) return "invalid_utf8";
            if (error is InvalidDataException) return "invalid_framing";
            if (error is WebSocketException) return "websocket_error";
            if (error is IOException) return "io_closed";
            return "unexpected_error";
        }
        private void QueueError(int attempt, string code, string reason = "connection_closed")
        {
            lock(inboxGate){
                if (attempt != Epoch || receiveFailed) return;
                lastFailure = new FailureDiagnostic(attempt, code, reason);
                if(inbox.Count>=MaxPendingMessages){FailReceive(attempt,"receive_overflow");return;}
                receiveFailed=true;
                // Preserve messages preceding a normal close, including a
                // final match result. Only a real overflow invalidates backlog.
                inbox.Enqueue(new Incoming{Attempt=attempt,JSON="{\"type\":\"error\",\"code\":\""+code+"\"}",ReceivedMS=ClockMS});
            }
        }

        // A stalled browser may deliver several seconds of callbacks before
        // Unity renders again. Delta frames cannot be dropped individually.
        // Replace the entire backlog with one terminal event and ignore the
        // remaining callbacks until the owner starts a fresh connection.
        private void FailReceive(int attempt,string code){
            lastFailure = new FailureDiagnostic(attempt, code, code);
            receiveFailed=true;
            inbox.Clear();
            inbox.Enqueue(new Incoming{Attempt=attempt,JSON="{\"type\":\"error\",\"code\":\""+code+"\"}",ReceivedMS=ClockMS});
        }

        public void Close()
        {
            lock(inboxGate){Epoch++;receiveFailed=false;opened=false;openStarted=false;sending=false;lastFailure=default;inbox.Clear();}
#if UNITY_WEBGL && !UNITY_EDITOR
            if(browserHandle!=0)RB_CloseFor(browserHandle);
#else
            nativeWebTransport?.Dispose();nativeWebTransport=null;
            lifetime?.Cancel(); socket?.Abort(); socket?.Dispose();
            socket = null; lifetime?.Dispose(); lifetime = null;
#endif
        }

        public void InterruptForDiagnostics() {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(browserHandle!=0){RB_CloseFor(browserHandle);QueueError(Epoch,"connection_closed");}
#else
            socket?.Abort();
            if(nativeWebTransport!=null){nativeWebTransport.Dispose();nativeWebTransport=null;QueueError(Epoch,"connection_closed");}
#endif
        }
        public void Receive(int attempt, string json)
        {
            lock(inboxGate){
                if (attempt != Epoch || receiveFailed) return;
                ValidateText(json);
                if(inbox.Count>=MaxPendingMessages){FailReceive(attempt,"receive_overflow");return;}
                inbox.Enqueue(new Incoming { Attempt = attempt, JSON = json, ReceivedMS = ClockMS });
            }
        }

        private static void ValidateText(string value)
        {if(value==null||Encoding.UTF8.GetByteCount(value)>MaxMessageBytes)throw new IOException("Message exceeds transport text limit");}
        public void Dispose() {
            Close();
#if UNITY_WEBGL && !UNITY_EDITOR
            if(browserHandle!=0){RB_Destroy(browserHandle);browserHandle=0;}
#endif
        }
    }
}
