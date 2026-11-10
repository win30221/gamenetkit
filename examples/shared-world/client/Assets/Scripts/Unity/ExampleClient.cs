using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace Realtime.Examples.SharedWorld
{
    // Unity lifecycle / serialization / input adapter. Session and presentation have separate owners.
    public sealed class ExampleClient : MonoBehaviour
    {
        private ExampleSession session;
        private WorldPresentation presentation;
        private string endpoint;
        private bool focused = true;
        private bool paused;
        private bool editingEndpoint;
        private string configurationError;

        private void Awake()
        {
            Application.runInBackground = true;
            presentation = new WorldPresentation(transform);
            session = new ExampleSession(new NetcodeComposition(), JsonUtility.FromJson<ServerMessage>, input => JsonUtility.ToJson(input));
            try { endpoint = ExampleEndpoint.Resolve(Environment.GetCommandLineArgs(), Application.absoluteURL); }
            catch (Exception error) { configurationError = error.Message; endpoint = ExampleEndpoint.Default; }
        }

        private void Start() { if (configurationError == null) Reconnect(); }

        private void Update()
        {
            float x = 0, z = 0;
            if (focused && !paused && !editingEndpoint)
            {
                x = (Key(KeyCode.D, KeyCode.RightArrow) ? 1 : 0) - (Key(KeyCode.A, KeyCode.LeftArrow) ? 1 : 0);
                z = (Key(KeyCode.W, KeyCode.UpArrow) ? 1 : 0) - (Key(KeyCode.S, KeyCode.DownArrow) ? 1 : 0);
            }
            session.Pump(Time.realtimeSinceStartupAsDouble, x, z);
            presentation.Render(session.State);
        }

        private static bool Key(KeyCode first, KeyCode second) => Input.GetKey(first) || Input.GetKey(second);

        private void OnApplicationFocus(bool value)
        { focused = value; if (!value) session?.StopInput(Time.realtimeSinceStartupAsDouble); }
        private void OnApplicationPause(bool value)
        { paused = value; if (value) session?.StopInput(Time.realtimeSinceStartupAsDouble); }

        private void Reconnect()
        {
            try
            {
                endpoint = ExampleEndpoint.Normalize(endpoint);
                configurationError = null;
                session.Connect(endpoint, gameObject.name, Time.realtimeSinceStartupAsDouble);
            }
            catch (Exception error) { configurationError = error.Message; }
            presentation.Render(session.State);
        }

        [Preserve] public void OnTransportMessage(string json) => session?.ReceiveBrowserMessage(json);

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Math.Min(Screen.width - 32, 540), 235), GUI.skin.box);
            GUILayout.Label("Shared World — core + transport");
            GUILayout.Label(configurationError ?? session.Status);
            GUILayout.Label("WASD / arrows • Your cube: blue • Others: orange");
            GUILayout.Label("Player " + session.State.LocalPlayerID + "   Tick " + session.State.Tick + "   Players " + session.State.Players.Count);
            GUILayout.Label("Server");
            GUI.SetNextControlName("ServerEndpoint");
            endpoint = GUILayout.TextField(endpoint);
            editingEndpoint = GUI.GetNameOfFocusedControl() == "ServerEndpoint";
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reconnect")) { GUI.FocusControl(null); Reconnect(); }
            if (GUILayout.Button("Disconnect")) { session.Disconnect(); presentation.Render(session.State); }
            GUILayout.EndHorizontal();
            GUILayout.Label(focused && !paused ? "Release keys to stop. Positions are received from the server." : "Unfocused — input stopped");
            GUILayout.EndArea();
        }

        private void OnDestroy() { session?.Dispose(); presentation?.Dispose(); }
    }
}
