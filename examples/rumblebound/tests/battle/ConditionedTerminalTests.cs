using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;

public static class ConditionedTerminalTests
{
    private sealed class CountingJson : IGameJson
    {
        private readonly GameJson inner = new GameJson();
        public int SnapshotReads;
        public T Read<T>(string text) { if (typeof(T) == typeof(Snapshot)) SnapshotReads++; return inner.Read<T>(text); }
        public string Write<T>(T value) => inner.Write(value);
    }
    private static void Require(bool value, string reason) { if (!value) throw new Exception("Conditioned terminal: " + reason); }
    public static async Task Run(GameContent content, Snapshot latest, string playerID)
    {
        var json = new GameJson();
        Snapshot Copy() => json.Read<Snapshot>(json.Write(latest));
        var welcome = Copy(); welcome.type = "welcome"; welcome.playerID = playerID;
        welcome.match.phase = "playing"; welcome.match.result = null;
        var ended = Copy(); ended.type = "result"; ended.playerID = playerID; ended.match.phase = "ended";
        ended.match.result = new MatchResult { outcome = "win", reason = "forfeit", winnerTeamID = ended.players[0].teamID, endedTick = ended.tick };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port, messages = 0;
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = peer.GetStream();
            await Upgrade(stream, timeout.Token);
            using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
            var bytes = new byte[32768];
            var hello = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token);
            Require(hello.MessageType == WebSocketMessageType.Text && hello.EndOfMessage, "fixture received no complete hello");
            await Send(socket, json.Write(welcome), timeout.Token);
            await finish.Task.WaitAsync(timeout.Token);
            await Send(socket, json.Write(ended), timeout.Token);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "match ended", timeout.Token);
            closed.TrySetResult(true);
            try
            {
                while (true)
                {
                    var part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token);
                    if (part.MessageType == WebSocketMessageType.Close) break;
                    if (part.EndOfMessage) Interlocked.Increment(ref messages);
                }
            }
            catch (WebSocketException) { }
        }, timeout.Token);
        try
        {
            var clientJson = new CountingJson();
            using (var session = new BattleSession(clientJson))
            {
                session.Connect(content, new BattleSettings { DelaySeconds = .2, SendDelaySeconds = .15 },
                    "ws://127.0.0.1:" + port + "/ws", "fixture-ticket", welcome.matchID, "", 0);
                var wait = Stopwatch.StartNew(); double now = 0;
                while (!session.Connected)
                {
                    Require(wait.Elapsed.TotalSeconds < 5 && session.Failure.Length == 0, "fixture did not join: " + session.Failure);
                    session.Pump(now += .001, 0, 0); await Task.Delay(1, timeout.Token);
                }
                session.MarkLoaded(); session.Pump(now += .001, 0, 0);
                Require(clientJson.SnapshotReads == 1, "unconditioned welcome was fully parsed more than once");
                Require(session.SendConditioning.PendingSend > 0, "fixture did not queue delayed outbound control");
                long submitted = session.Metrics.ReadTraffic(now).SentMessages;
                bool validated = false;
                session.ValidatedSnapshot += state => { if (state.type == "result") validated = true; };
                session.PauseSnapshots(now, 10);
                finish.TrySetResult(true); await closed.Task.WaitAsync(timeout.Token);
                // A completed server write does not mean the client receiver has
                // queued it yet. Hold logical time before any send becomes due.
                while (!validated)
                {
                    Require(wait.Elapsed.TotalSeconds < 5 && session.Failure.Length == 0, "terminal was not received");
                    session.Pump(now, 0, 0); await Task.Delay(1, timeout.Token);
                }
                Require(clientJson.SnapshotReads == 2, "result was parsed again before delayed delivery");

                // Outbound is due, but the reliable terminal snapshot is still in
                // receive conditioning. Its following real WS close must not
                // flush an input/probe/control message into the closed transport.
                now += .16;
                for (int i = 0; i < 20; i++) { session.Pump(now, 0, 0); await Task.Delay(5, timeout.Token); }
                Require(validated && !session.HasResult && session.Connected && session.Failure.Length == 0,
                    "underlying close discarded the delayed terminal snapshot");
                Require(session.Metrics.ReadTraffic(now).SentMessages == submitted && Volatile.Read(ref messages) == 0,
                    "a message was submitted while awaiting terminal delivery");
                // A known terminal must also survive a deliberate receive pause
                // longer than the live connection timeout, even after close was
                // consumed in either of the earlier pumps.
                session.Pump(now += 4.2, 0, 0); session.Pump(now += 4.2, 0, 0);
                Require(!session.HasResult && session.Failure.Length == 0, "live timeout discarded a validated paused settlement");
                session.Pump(now += 2, 0, 0);
                Require(session.HasResult && session.State.Latest.match.result != null && session.Failure.Length == 0,
                    "conditioned terminal result was not retained");
                Require(clientJson.SnapshotReads == 3, "conditioned result was fully parsed more than once at delivery");
                Require(session.SendConditioning.PendingSend == 0, "finished match retained delayed sends");
                session.Pump(now + 5, 0, 0);
                Require(session.HasResult && session.Failure.Length == 0, "retained terminal was timed out");
            }
            await server.WaitAsync(timeout.Token);
        }
        finally { timeout.Cancel(); listener.Stop(); }
        Console.WriteLine("REMAKE_CONDITIONED_TERMINAL PASS real WS result then close, outbound suppression during delivery delay and retained settlement");
    }
    private static Task Send(WebSocket socket, string payload, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)), WebSocketMessageType.Text, true, token);
    private static async Task Upgrade(NetworkStream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        string key = null, line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)) key = line.Substring(line.IndexOf(':') + 1).Trim();
        Require(key != null, "fixture did not receive a WebSocket upgrade");
        using var sha = SHA1.Create();
        string accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
        await stream.WriteAsync(response, 0, response.Length, token);
    }
}
