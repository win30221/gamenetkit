using System;
using Realtime.Transport;
using UnityEngine;
using UnityEngine.Scripting;

namespace Realtime.Examples.InterpolationLesson
{
    public sealed class ExampleClient : MonoBehaviour
    {
        private ExampleSession session;
        private WorldPresentation presentation;
        private string endpoint, error;
        private bool focused=true, paused, editing, autoMove=true, compareSelf=true, showLatest=true;
        private float direction=1;
        private int mode=1, profile;
        private static readonly string[] modes={"OFF", "Fixed", "Adaptive"};
        private static readonly string[] ids={"latest", "fixed", "adaptive"};
        private static double Now => MessageTransport.ClockMS/1000;

        private void Awake()
        {
            Application.runInBackground=true;
            presentation=new WorldPresentation(transform);
            session=new ExampleSession(new NetcodeComposition(), JsonUtility.FromJson<ServerMessage>, value=>JsonUtility.ToJson(value));
            try {endpoint=ExampleEndpoint.Resolve(Environment.GetCommandLineArgs(),Application.absoluteURL);}
            catch(Exception problem) {error=problem.Message; endpoint=ExampleEndpoint.Default;}
        }
        private void Start() {if(error==null) Reconnect();}
        private void Update()
        {
            float x=0,z=0;
            if (!paused && autoMove) {
                if(session.State.Players.TryGetValue(session.State.LocalPlayerID,out var own)) {
                    if(own.x>5.5f) direction=-1; else if(own.x< -5.5f) direction=1;
                }
                x=direction;
            } else if(focused && !paused && !editing) {
                x=(Key(KeyCode.D,KeyCode.RightArrow)?1:0)-(Key(KeyCode.A,KeyCode.LeftArrow)?1:0);
                z=(Key(KeyCode.W,KeyCode.UpArrow)?1:0)-(Key(KeyCode.S,KeyCode.DownArrow)?1:0);
            }
            session.Pump(Now,x,z);
            presentation.Render(session.State,session.View,compareSelf,showLatest);
        }
        private static bool Key(KeyCode a,KeyCode b)=>Input.GetKey(a)||Input.GetKey(b);
        private void OnApplicationFocus(bool value) {focused=value;if(!value)session?.StopInput(Now);}
        private void OnApplicationPause(bool value) {paused=value;if(value)session?.StopInput(Now);}
        [Preserve] public void OnTransportMessage(string json)=>session?.ReceiveBrowserMessage(json);
        private void Reconnect()
        {
            try {endpoint=ExampleEndpoint.Normalize(endpoint);error=null;session.Connect(endpoint,gameObject.name,Now);}
            catch(Exception problem) {error=problem.Message;}
            presentation.Render(session.State,session.View,compareSelf,showLatest);
        }
        private void OnGUI()
        {
            GUI.skin.label.wordWrap=true;
            GUILayout.BeginArea(new Rect(12,12,Screen.width*.38f-12,Screen.height-24),GUI.skin.box);
            GUILayout.Label("INTERPOLATION LESSON - actual C# SDK");
            GUILayout.Label(error??session.Status);
            GUILayout.Label("Solid = selected presentation | outline = same delivered latest snapshot");
            int selection=GUILayout.Toolbar(mode,modes);
            if(selection!=mode){mode=selection;session.View.SetMode(ids[mode],Now);}
            GUILayout.Label("Snapshot delivery: affects presentation only (input/control stay direct)");
            int nextProfile=GUILayout.Toolbar(profile,new[]{"Clean", "Delay 150ms", "Jitter + loss"});
            if(nextProfile!=profile) {
                profile=nextProfile;
                session.ConfigureNetwork(profile==0?0:.15,profile==2?.10:0,profile==2?.15:0);
            }
            autoMove=GUILayout.Toggle(autoMove,"Auto move back and forth (turn off for WASD / arrows)");
            compareSelf=GUILayout.Toggle(compareSelf,"Interpolate own cube too (single-window observation, no prediction)");
            showLatest=GUILayout.Toggle(showLatest,"Show latest-snapshot outlines");
            var metrics=session.View.Metrics;
            GUILayout.Label($"Player {session.State.LocalPlayerID} | players {session.State.Players.Count} | delivered frames {session.View.BufferedFrames}");
            GUILayout.Label($"Received tick {session.State.Tick} | render tick {session.View.RenderTick:F2} | buffer {metrics.BufferSeconds*1000:F0}ms");
            GUILayout.Label($"Arrival jitter {metrics.JitterSeconds*1000:F0}ms | underruns {metrics.Underruns} | dropped snapshots {session.Network.DroppedReceive}");
            GUI.SetNextControlName("Endpoint");endpoint=GUILayout.TextField(endpoint);
            editing=GUI.GetNameOfFocusedControl()=="Endpoint";
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Reconnect")){GUI.FocusControl(null);Reconnect();}
            if(GUILayout.Button("Disconnect")){session.Disconnect();presentation.Render(session.State,session.View,compareSelf,showLatest);}
            GUILayout.EndHorizontal();
            GUILayout.Label("Try OFF -> Fixed -> Adaptive under jitter. Smooth motion costs buffer delay.");
            GUILayout.EndArea();
        }
        private void OnDestroy(){session?.Dispose();presentation?.Dispose();}
    }
}
