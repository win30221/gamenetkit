using System;
using UnityEngine;
using Realtime.Examples.Presentation;

namespace Realtime.Examples.NetworkRecovery
{
    public sealed class ExampleClient:LessonBehaviour
    {
        private ExampleSession session;
        private int ownMode=1,correction=1,remote=2,delay=1,clock=1;

        protected override string Title=>"4. NetworkRecovery — actual C# SDK";
        protected override string DefaultEndpoint=>ExampleEndpoint.Default;
        protected override string Status=>session.Status;
        protected override int SmokeRoster=>session.State.Players.Count;
        protected override long SmokeAck=>session.State.Players.TryGetValue(session.State.LocalPlayerID,out var player)?player.ack:0;
        protected override float OwnX=>(float)session.Own.X;
        protected override void Initialize(){session=new ExampleSession(JsonUtility.FromJson<ServerMessage>,v=>JsonUtility.ToJson(v));}
        protected override void Connect()=>session.Connect(ExampleEndpoint.Normalize(Endpoint),Now,gameObject.name);
        protected override void Disconnect()=>session.Disconnect();
        protected override void Receive(string json)=>session.Transport.Receive(session.Transport.Epoch,json);
        protected override void Pump(float x,float z){session.Pump(Now,x,z);}
        protected override void Release(){session.Dispose();}
        protected override void Render(){var state=session.State;
            foreach(var p in state.Players.Values){
                float x=p.x,z=p.z;bool own=p.id==state.LocalPlayerID;
                if(own&&session.Own.Ready){x=(float)session.Own.X;z=(float)session.Own.Z;}
                else if(!own&&session.Remote.Sample(p.id,out float rx,out float rz)){x=rx;z=rz;}
                World.Put("player-"+p.id,x,z,own?SceneView.Own:SceneView.Remote);
                if(ShowAuthority)World.Ring("authority-"+p.id,p.x,p.z,.6f,own?SceneView.Own:SceneView.Remote);
            }}
        private void PredictionControls(){
            int p=GUILayout.Toolbar(ownMode,new[]{"Wait for server","Predict locally"});
            if(p!=ownMode){ownMode=p;session.Own.SetMode(p==0?"authority":"prediction",Now);}
            int c=GUILayout.Toolbar(correction,new[]{"Snap correction","Smooth correction"});
            if(c!=correction){correction=c;session.Own.SetCorrection(c==0?"snap":"smooth");}
        }
        protected override void Controls(){bool auto=GUILayout.Toggle(session.Recovery.Enabled,"Automatic reconnect");
            if(auto!=session.Recovery.Enabled)session.SetAutoReconnect(auto,Now);
            if(GUILayout.Button("Disconnect link for 4 seconds"))session.SimulateOutage(Now);
            GUILayout.Label($"Retry {session.Recovery.Attempts}; wait {session.Recovery.Wait(Now):F1}s; rejoins {session.Recovery.Rejoins}");
            foreach(var e in session.Recovery.Events)GUILayout.Label($"{e.at:F1}s {e.kind}: {e.detail}");}
        protected override void Details(){PredictionControls();
            int next=GUILayout.Toolbar(remote,new[]{"Remote OFF","Fixed","Adaptive"});
            if(next!=remote){remote=next;session.Remote.SetMode(RemoteModes[remote],Now);}
            int d=GUILayout.Toolbar(delay,new[]{"Clean","150ms","300ms"});if(d!=delay){delay=d;session.ConfigureDelay(delay*.15);}
            GUILayout.Label($"Tick {session.State.Tick}; correction {session.Own.LastError:F3}; buffer {session.Remote.Metrics.BufferSeconds*1000:F0}ms");
            int clockMode=GUILayout.Toolbar(clock,new[]{"Snapshot clock","Disciplined clock"});
            if(clockMode!=clock){clock=clockMode;session.SetClock(clock==0?"snapshot":"disciplined",Now);}
            if(GUILayout.Button("Pause snapshots for 2 seconds"))session.Link.PauseSnapshots(Now,2);}
    }
}
