using System;
using UnityEngine;
using Realtime.Examples.Presentation;

namespace Realtime.Examples.HistoryLesson
{
    public sealed class ExampleClient:LessonBehaviour
    {
        private ExampleSession session;
        private int ownMode=1,correction=1,remote=2,delay=1;
        private int tab;private bool inspect,compensated=true;private float lookback=1;
        protected override string Title=>"9. History — actual C# SDK";
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
            foreach(var row in session.Feedback.Rows)if(row.visible)World.Ring("action-"+row.id,row.x,row.z,.45f,Color.yellow);
            if(session.Targets.Sample(session.Remote.RenderTick,out var target,out var tick))World.Put("target-"+target.life,target.x,target.z,SceneView.Target,.7f);
            var preview=session.Preview;if(inspect&&preview!=null&&preview.found&&preview.target!=null)World.Ring("history",preview.target.x,preview.target.z,.5f,Color.cyan);
            var shot=session.LastShot;if(shot!=null){
                World.Ring("shot-past",shot.pastX,shot.pastZ,.5f,SceneView.Marker);
                World.Ring("shot-current",shot.currentX,shot.currentZ,.5f,SceneView.Remote);
                World.Segment("aim",shot.originX,shot.originZ,shot.aimX,shot.aimZ,shot.hit?Color.green:Color.red);
            }}
        private void PredictionControls(){
            int p=GUILayout.Toolbar(ownMode,new[]{"Wait for server","Predict locally"});
            if(p!=ownMode){ownMode=p;session.Own.SetMode(p==0?"authority":"prediction",Now);}
            int c=GUILayout.Toolbar(correction,new[]{"Snap correction","Smooth correction"});
            if(c!=correction){correction=c;session.Own.SetCorrection(c==0?"snap":"smooth");}
        }
        protected override void Controls(){tab=GUILayout.Toolbar(tab,new[]{"History query","Lag compensated shot"});
            if(tab==0){
                bool next=GUILayout.Toggle(inspect,"Show historical position (blue ring)");
                float seconds=GUILayout.HorizontalSlider(lookback,0,3);GUILayout.Label($"Look back {seconds:F2}s");
                if(next!=inspect||Math.Abs(seconds-lookback)>.025){inspect=next;lookback=seconds;session.Inspect(inspect,lookback);}
                var h=session.State.History;if(h!=null)GUILayout.Label($"History ticks {h.first}..{h.last}; {h.count} samples");
                if(session.Preview!=null)GUILayout.Label(session.Preview.found?"History sample found":"Requested tick unavailable");
            }else{
                compensated=GUILayout.Toggle(compensated,"Compensate for displayed target time");
                if(GUILayout.Button("Aim at displayed target and shoot"))session.Fire(compensated,Now);
                GUILayout.Label($"Hits {session.Hits}; misses {session.Misses}; rejected {session.RejectedShots}");
                var shot=session.LastShot;if(shot!=null)GUILayout.Label($"{shot.reason}; current hit {shot.currentHit}; past hit {shot.historicalHit}; used {shot.usedTick}");
            }
            if(session.State.Target!=null)GUILayout.Label($"Target life {session.State.Target.life}; HP {session.State.Target.hp}");}
        protected override void Details(){PredictionControls();
            int next=GUILayout.Toolbar(remote,new[]{"Remote OFF","Fixed","Adaptive"});
            if(next!=remote){remote=next;session.Remote.SetMode(RemoteModes[remote],Now);}
            int d=GUILayout.Toolbar(delay,new[]{"Clean","150ms","300ms"});if(d!=delay){delay=d;session.ConfigureDelay(delay*.15);}
            GUILayout.Label($"Tick {session.State.Tick}; correction {session.Own.LastError:F3}; buffer {session.Remote.Metrics.BufferSeconds*1000:F0}ms");
            if(GUILayout.Button("Send expired shot"))session.Fire(compensated,Now,true);
            if(GUILayout.Button("Reset shared target"))session.ResetTarget();}
    }
}
