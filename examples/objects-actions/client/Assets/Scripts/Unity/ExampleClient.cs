using System;
using UnityEngine;
using Realtime.Examples.Presentation;

namespace Realtime.Examples.ObjectsActions
{
    public sealed class ExampleClient:LessonBehaviour
    {
        private ExampleSession session;
        private int ownMode=1,correction=1,remote=2,delay=1;
        private int tab;private bool speculative=true;
        protected override string Title=>"5. ObjectsActions — actual C# SDK";
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
            }
            foreach(var obj in state.Objects.Values)World.Put("object-"+obj.id+"-"+obj.life,obj.x,obj.z,SceneView.Marker,.45f);
            foreach(var row in session.Feedback.Rows)if(row.visible)World.Ring("action-"+row.id,row.x,row.z,.45f,Color.yellow);}
        private void PredictionControls(){
            int p=GUILayout.Toolbar(ownMode,new[]{"Wait for server","Predict locally"});
            if(p!=ownMode){ownMode=p;session.Own.SetMode(p==0?"authority":"prediction",Now);}
            int c=GUILayout.Toolbar(correction,new[]{"Snap correction","Smooth correction"});
            if(c!=correction){correction=c;session.Own.SetCorrection(c==0?"snap":"smooth");}
        }
        protected override void Controls(){tab=GUILayout.Toolbar(tab,new[]{"Object lifecycle","Action feedback"});
            speculative=GUILayout.Toggle(speculative,"Immediate local action preview");
            foreach(string kind in tab==0?new[]{"spawn","remove","rebirth"}:new[]{"spawn","deny","timeout"})
                if(GUILayout.Button(kind))session.QueueAction(kind,speculative,Now);
            if(GUILayout.Button("Resend previous action ID"))session.ResendLast();
            GUILayout.Label($"Objects {session.State.Objects.Count}; +{session.State.Added} / -{session.State.Removed}");
            GUILayout.Label($"Pending {session.Feedback.Pending}; confirmed {session.Feedback.Confirmed}; rejected {session.Feedback.Rejected}; expired {session.Feedback.Expired}");
            foreach(var row in session.Feedback.Rows)GUILayout.Label($"{row.id} {row.kind}: {row.phase} {row.reason}");}
        protected override void Details(){PredictionControls();
            int next=GUILayout.Toolbar(remote,new[]{"Remote OFF","Fixed","Adaptive"});
            if(next!=remote){remote=next;session.Remote.SetMode(RemoteModes[remote],Now);}
            int d=GUILayout.Toolbar(delay,new[]{"Clean","150ms","300ms"});if(d!=delay){delay=d;session.ConfigureDelay(delay*.15);}
            GUILayout.Label($"Tick {session.State.Tick}; correction {session.Own.LastError:F3}; buffer {session.Remote.Metrics.BufferSeconds*1000:F0}ms");}
    }
}
