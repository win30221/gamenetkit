using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GameNetKit.ProtobufExample;
using Realtime.Protobuf;
using Realtime.Transport;
using UnityEngine;

public sealed class ProtobufDemo : MonoBehaviour
{
    private MessageTransport transport, observer;
    private ProtobufSession<ClientPacket, ServerPacket> session, observerSession;
    private WorldSnapshot snapshot;
    private ulong ownID, sequence;
    private string endpoint = "ws://127.0.0.1:18101/ws-protobuf", status = "Ready";
    private double nextSend, started;
    private bool smoke, passed, sending, connected;
    private readonly Dictionary<ulong, GameObject> actors = new Dictionary<ulong, GameObject>();
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PB_Report(string result);
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => new GameObject("Protobuf example").AddComponent<ProtobufDemo>();

    private void Start()
    {
        Application.runInBackground = true;
        try { CompatibilityCheck.Run(); Debug.Log("PROTOBUF_COMPATIBILITY PASS"); }
        catch (Exception error) { Fail(error.Message); return; }
        smoke = Array.IndexOf(Environment.GetCommandLineArgs(), "-protobufSmoke") >= 0 || Application.absoluteURL.Contains("smoke=1");
        if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var page) && (page.Scheme == "http" || page.Scheme == "https"))
            endpoint = (page.Scheme == "https" ? "wss://" : "ws://") + page.Authority + "/ws-protobuf";
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 15, 0);
        camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.orthographic = true; camera.orthographicSize = 12;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .15f);
        if (smoke) Connect();
    }

    private void Connect()
    {
        Disconnect();
        started = Time.realtimeSinceStartupAsDouble;
        status = "Connecting"; passed = false; sequence = 0; ownID = 0;
        transport = new MessageTransport();
        session = new ProtobufSession<ClientPacket, ServerPacket>(transport, ServerPacket.Parser, KnownBody);
        session.Failed += Fail;
        session.Received += Receive;
        session.Open(transport.BeginAttempt(), endpoint, Hello("Unity"));
        if (smoke)
        {
            observer = new MessageTransport();
            observerSession = new ProtobufSession<ClientPacket, ServerPacket>(observer, ServerPacket.Parser, KnownBody);
            observerSession.Failed += Fail;
            observerSession.Received += packet =>
            {
                if (packet.BodyCase != ServerPacket.BodyOneofCase.Snapshot || packet.Snapshot.Players.Count < 2 || snapshot == null) return;
                foreach (var player in packet.Snapshot.Players)
                    if (player.Id == ownID && player.Ack > 0 && player.X > .01f && !passed)
                    { passed = true; status = "Two-client binary WS verified"; Report("PROTOBUF_PLAYER_SMOKE PASS"); }
            };
            observerSession.Open(observer.BeginAttempt(), endpoint, Hello("Observer"));
        }
    }
    private static bool KnownBody(ServerPacket packet) => packet.BodyCase != ServerPacket.BodyOneofCase.None;
    private static ClientPacket Hello(string name) => new ClientPacket { Hello = new Hello { ProtocolVersion = 1, Name = name } };
    private void Receive(ServerPacket packet)
    {
        switch (packet.BodyCase)
        {
            case ServerPacket.BodyOneofCase.Welcome:
                if (packet.Welcome.ProtocolVersion != 1) { Fail("protocol_version"); return; }
                ownID = packet.Welcome.PlayerId; connected = true; status = "Connected";
                break;
            case ServerPacket.BodyOneofCase.Error: Fail(packet.Error.Code); break;
            case ServerPacket.BodyOneofCase.Snapshot:
                snapshot = packet.Snapshot; Project(); break;
        }
    }
    private void Update()
    {
        try
        {
            session?.Pump(); observerSession?.Pump();
            if (smoke && !passed && transport != null && Time.realtimeSinceStartupAsDouble - started > 15) { Fail("smoke_timeout"); return; }
            if (!connected || sending || Time.realtimeSinceStartupAsDouble < nextSend) return;
            float x = smoke ? .5f : ((Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0));
            float z = smoke ? 0 : ((Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            nextSend = Time.realtimeSinceStartupAsDouble + .05;
            _ = SendMove(x, z);
        }
        catch (Exception error) { Fail(error.GetType().Name + ": " + error.Message); }
    }
    private async Task SendMove(float x, float z)
    {
        sending = true;
        var active = session;
        try { await active.TrySendAsync(new ClientPacket { Move = new MoveInput { Sequence = ++sequence, X = x, Z = z } }); }
        catch (Exception error) { if (session == active) Fail(error.Message); }
        finally { if (session == active) sending = false; }
    }
    private void Project()
    {
        var live = new HashSet<ulong>();
        foreach (var player in snapshot.Players)
        {
            live.Add(player.Id);
            if (!actors.TryGetValue(player.Id, out var actor))
            {
                actor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = actor.GetComponent<Renderer>();
                renderer.material = new Material(Shader.Find("Unlit/Color"));
                renderer.material.color = player.Id == ownID ? Color.cyan : new Color(1, .65f, .2f);
                actors.Add(player.Id, actor);
            }
            actor.transform.position = new Vector3(player.X, 0, player.Z);
        }
        var removed = new List<ulong>();
        foreach (var entry in actors) if (!live.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (var id in removed) { Destroy(actors[id].GetComponent<Renderer>().material); Destroy(actors[id]); actors.Remove(id); }
    }
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, 530, 200), GUI.skin.box);
        GUILayout.Label("GameNetKit · Protobuf / binary WebSocket");
        endpoint = GUILayout.TextField(endpoint);
        GUILayout.Label(status + " | player " + ownID + " | players " + (snapshot?.Players.Count ?? 0));
        GUILayout.Label("WASD / arrows to move. Open two clients to share the same world.");
        if (GUILayout.Button(connected ? "Disconnect" : "Connect")) { if (connected) Disconnect(); else Connect(); }
        GUILayout.EndArea();
    }
    private void Fail(string code) { status = "Error: " + code; Report("PROTOBUF_PLAYER_FAIL " + code); Disconnect(); }
    private static void Report(string message)
    {
        Debug.Log(message);
#if UNITY_WEBGL && !UNITY_EDITOR
        PB_Report(message);
#endif
    }
    private void Disconnect()
    {
        connected = false; sending = false;
        session = observerSession = null;
        transport?.Dispose(); observer?.Dispose(); transport = observer = null;
        snapshot = null;
        foreach (var actor in actors.Values) { Destroy(actor.GetComponent<Renderer>().material); Destroy(actor); }
        actors.Clear();
    }
    private void OnDestroy() => Disconnect();
}
