using System;
using UnityEngine;
using Realtime.Examples.Presentation;
using UnityEngine.Networking;
namespace Realtime.Examples.ReplayLesson
{
    public sealed class ExampleClient:LessonBehaviour
    {
        private ExampleSession session;
        private int ownMode=1,correction=1,remote=2,delay=1;
        private readonly PlaybackWorld playback=new PlaybackWorld();private bool replayMode,loading;private int speed=1;
        private double previous;private UnityWebRequest request;
        protected override bool LiveInput=>!replayMode&&!loading;
        private System.Collections.IEnumerator LoadClip()
        {
            string id=session.Recording.ID;if(!RecordingStatus.ValidID(id)||loading)yield break;
            loading=true;var address=new UriBuilder(ExampleEndpoint.Normalize(Endpoint));address.Scheme=address.Scheme=="wss"?"https":"http";address.Path="/recordings/"+id;
            request=UnityWebRequest.Get(address.Uri);request.timeout=10;
            yield return request.SendWebRequest();
            try{
                if(request.result!=UnityWebRequest.Result.Success)throw new InvalidOperationException(request.error);
                if(request.downloadHandler.data.Length>8*1024*1024)throw new InvalidOperationException("Recording exceeds limit");
                playback.Load(JsonUtility.FromJson<PlaybackFile>(request.downloadHandler.text));playback.Play();replayMode=true;AutoMove=false;Notice="";
            }catch(Exception error){Notice=error.Message;}
            finally{request.Dispose();request=null;loading=false;}
        }
        protected override string Title=>"6. Replay — actual C# SDK";
        protected override string DefaultEndpoint=>ExampleEndpoint.Default;
        protected override string Status=>session.Status;
        protected override int SmokeRoster=>session.State.Players.Count;
        protected override long SmokeAck=>session.State.Players.TryGetValue(session.State.LocalPlayerID,out var player)?player.ack:0;
        protected override float OwnX=>(float)session.Own.X;
        protected override void Initialize(){session=new ExampleSession(JsonUtility.FromJson<ServerMessage>,v=>JsonUtility.ToJson(v));}
        protected override void Connect()=>session.Connect(ExampleEndpoint.Normalize(Endpoint),Now,gameObject.name);
        protected override void Disconnect()=>session.Disconnect();
        protected override void Receive(string json)=>session.Transport.Receive(session.Transport.Epoch,json);
        protected override void Pump(float x,float z){double now=Now;session.Pump(now,x,z);if(replayMode)playback.Advance(Math.Max(0,now-previous));previous=now;}
        protected override void Release(){request?.Abort();request?.Dispose();request=null;playback.Clear();session.Dispose();}
        protected override void OnDisable(){StopAllCoroutines();request?.Abort();request?.Dispose();request=null;loading=false;base.OnDisable();}
        protected override void Render(){var state=replayMode?playback.State:session.State;
            foreach(var p in state.Players.Values){
                float x=p.x,z=p.z;bool own=p.id==state.LocalPlayerID;
                if(replayMode){playback.Sample(p.id,out x,out z);}
                else if(own&&session.Own.Ready){x=(float)session.Own.X;z=(float)session.Own.Z;}
                else if(!own&&session.Remote.Sample(p.id,out float rx,out float rz)){x=rx;z=rz;}
                World.Put("player-"+p.id,x,z,own?SceneView.Own:SceneView.Remote);
                if(ShowAuthority)World.Ring("authority-"+p.id,p.x,p.z,.6f,own?SceneView.Own:SceneView.Remote);
            }
            foreach(var obj in state.Objects.Values)World.Put("object-"+obj.id+"-"+obj.life,obj.x,obj.z,SceneView.Marker,.45f);
            if(!replayMode)foreach(var row in session.Feedback.Rows)if(row.visible)World.Ring("action-"+row.id,row.x,row.z,.45f,Color.yellow);}
        private void PredictionControls(){
            int p=GUILayout.Toolbar(ownMode,new[]{"Wait for server","Predict locally"});
            if(p!=ownMode){ownMode=p;session.Own.SetMode(p==0?"authority":"prediction",Now);}
            int c=GUILayout.Toolbar(correction,new[]{"Snap correction","Smooth correction"});
            if(c!=correction){correction=c;session.Own.SetCorrection(c==0?"snap":"smooth");}
        }
        protected override void Controls(){GUILayout.Label(replayMode?"OFFLINE PLAYBACK (live input blocked)":"LIVE AUTHORITY");
            GUILayout.Label($"Recording: {session.Recording.Status}; {session.Recording.Frames} frames");
            if(!replayMode&&!loading){
                if(GUILayout.Button("Start recording"))session.Record("start");
                if(GUILayout.Button("Stop recording"))session.Record("stop");
                if(session.Recording.Status=="ready"&&GUILayout.Button("Load and play recording"))StartCoroutine(LoadClip());
            }
            if(loading)GUILayout.Label("Loading verified recording...");
            if(replayMode){
                if(GUILayout.Button(playback.Clock.Playing?"Pause":"Play")){if(playback.Clock.Playing)playback.Pause();else playback.Play();}
                float seek=GUILayout.HorizontalSlider((float)playback.Position,0,(float)playback.Duration);
                if(Math.Abs(seek-playback.Position)>.05)playback.Seek(seek);
                GUILayout.Label($"{playback.Position:F1} / {playback.Duration:F1}s");
                int next=GUILayout.Toolbar(speed,new[]{"0.5x","1x","2x"});if(next!=speed){speed=next;playback.Clock.Speed=new[]{.5,1,2}[speed];}
                if(GUILayout.Button("Return to live")){replayMode=false;playback.Pause();}
            }}
        protected override void Details(){PredictionControls();
            int next=GUILayout.Toolbar(remote,new[]{"Remote OFF","Fixed","Adaptive"});
            if(next!=remote){remote=next;session.Remote.SetMode(RemoteModes[remote],Now);}
            int d=GUILayout.Toolbar(delay,new[]{"Clean","150ms","300ms"});if(d!=delay){delay=d;session.ConfigureDelay(delay*.15);}
            GUILayout.Label($"Tick {session.State.Tick}; correction {session.Own.LastError:F3}; buffer {session.Remote.Metrics.BufferSeconds*1000:F0}ms");}
    }
}
