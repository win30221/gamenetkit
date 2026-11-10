using System;
using Realtime.Core;
using Realtime.Transport;
using UnityEngine;

public sealed class MinimalClient : MonoBehaviour
{
    [Serializable] private sealed class PointState { public string type; public long tick; public double x; }
    private readonly MessageTransport connection = new MessageTransport();
    private readonly IPresentationTimeline timeline = new LatestSnapshotTimeline();
    private Transform marker; private PointState state; private double start, nextSend;
    private bool smoke;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() { new GameObject("MinimalClient").AddComponent<MinimalClient>(); }
    private void Start()
    {
        smoke = Array.IndexOf(Environment.GetCommandLineArgs(), "-rf-minimal-smoke") >= 0;
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10);
        marker = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        start = Time.realtimeSinceStartupAsDouble;
        int epoch = connection.BeginAttempt();
        connection.Open(epoch, "ws://127.0.0.1:18091/ws", "{\"type\":\"hello\"}", "", gameObject.name, false);
    }
    [UnityEngine.Scripting.Preserve]
    public void OnTransportMessage(string json) => connection.Receive(connection.Epoch, json);
    public void OnWire(string json)
    {
        var value = JsonUtility.FromJson<PointState>(json);
        if (value?.type == "error") { state = null; connection.Close(); return; }
        if (value?.type != "snapshot" || value.tick < 0 || double.IsNaN(value.x) || double.IsInfinity(value.x)) return;
        state = value; timeline.Observe(value.tick, Time.realtimeSinceStartupAsDouble);
    }
    private void Update()
    {
        while (connection.TryRead(out var message)) OnWire(message.JSON);
        double now = Time.realtimeSinceStartupAsDouble; timeline.Advance(now);
        if (state != null) marker.position = new Vector3((float)state.x, 0, 0);
        if (state != null && now >= nextSend && !connection.Sending)
        {
            nextSend = now + 1.0 / 30;
            double drive = smoke ? 1 : Input.GetKey(KeyCode.RightArrow) ? 1 : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
            _ = connection.Send("{\"type\":\"input\",\"x\":" + drive + "}");
        }
        if (smoke && now - start > 4)
        {
            bool passed = state != null && state.x > 1 && timeline.RenderTick > 0;
            Debug.Log(passed ? "RF_MINIMAL PASS: independent server + core/transport only, no game or interpolation dependency" : "RF_MINIMAL FAIL");
            Application.Quit(passed ? 0 : 1);
        }
    }
    private void OnDestroy() { connection.Dispose(); }
}
