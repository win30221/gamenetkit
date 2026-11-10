using System;
using Realtime.Transport;
using UnityEngine;
using UnityEngine.Scripting;

namespace Realtime.Examples.Presentation
{
    // Scene/input lifecycle only. Each adapter owns its real application session.
    public abstract class LessonBehaviour : MonoBehaviour
    {
        protected SceneView World;
        protected string Endpoint, Notice="";
        protected bool Advanced, AutoMove, ShowAuthority=true;
        protected static readonly string[] RemoteModes={"latest","fixed","adaptive"};
        protected static double Now=>MessageTransport.ClockMS/1000;
        private bool ready,focused=true,paused,editing;
        private float direction=1;
        private Vector2 scroll;
        private bool smoke,smokeObserved;
        private double smokeStarted,smokeReady;
        private float smokeOrigin;
        protected virtual int SmokeRoster=>0;
        protected virtual long SmokeAck=>0;
        protected abstract string Title{get;}
        protected abstract string DefaultEndpoint{get;}
        protected abstract string Status{get;}
        protected abstract void Initialize();
        protected abstract void Connect();
        protected abstract void Disconnect();
        protected abstract void Pump(float x,float z);
        protected abstract void Render();
        protected abstract void Controls();
        protected abstract void Release();
        protected abstract void Receive(string json);
        protected virtual bool LiveInput=>true;
        protected virtual float OwnX=>0;
        protected virtual void Details(){}

        public static void Boot<T>(string name) where T:LessonBehaviour
        {
            if(UnityEngine.Object.FindAnyObjectByType<T>()!=null)return;
            var root=new GameObject(name+"-"+Guid.NewGuid().ToString("N"));
            UnityEngine.Object.DontDestroyOnLoad(root);root.AddComponent<T>();
        }
        protected virtual void Awake()
        {
            Application.runInBackground=true;
            try {
                World=new SceneView(transform);Initialize();ready=true;Endpoint=DefaultEndpoint;
                var args=Environment.GetCommandLineArgs();
                smoke=Array.IndexOf(args,"-rf-lesson-smoke")>=0;smokeStarted=Now;
                bool explicitEndpoint=false;
                for(int i=0;i<args.Length;i++)if(args[i]=="-rf-server"){
                    if(++i>=args.Length)throw new ArgumentException("-rf-server requires URL");Endpoint=args[i];explicitEndpoint=true;
                }
                if(!explicitEndpoint&&!string.IsNullOrEmpty(Application.absoluteURL)) {
                    // The public lesson host proxies /ws to the private Go service.
                    var page=new Uri(Application.absoluteURL);
                    Endpoint=new Uri(page.GetLeftPart(UriPartial.Authority)+"/ws").AbsoluteUri;
                }
            }catch(Exception error){Notice=error.Message;}
        }
        protected virtual void Start(){if(ready&&Notice.Length==0)TryConnect();}
        private void TryConnect(){try{Connect();Notice="";}catch(Exception error){Notice=error.Message;Disconnect();}}
        protected virtual void Update()
        {
            if(!ready)return;
            float x=0,z=0;
            if(focused&&!paused&&!editing&&LiveInput){
                if(AutoMove){if(OwnX>5.5f)direction=-1;else if(OwnX< -5.5f)direction=1;x=direction;}
                else {x=(Key(KeyCode.D,KeyCode.RightArrow)?1:0)-(Key(KeyCode.A,KeyCode.LeftArrow)?1:0);
                    z=(Key(KeyCode.W,KeyCode.UpArrow)?1:0)-(Key(KeyCode.S,KeyCode.DownArrow)?1:0);}
            }
            if(smoke)x=.65f;
            Pump(x,z);World.Begin();Render();World.End();
            if(smoke){
                if(!smokeObserved&&SmokeRoster>=2&&Status=="Connected"){smokeObserved=true;smokeOrigin=OwnX;smokeReady=Now;}
                if(smokeObserved&&SmokeRoster>=2&&SmokeAck>=5&&Now-smokeReady>=1&&Math.Abs(OwnX-smokeOrigin)>.35f){Debug.Log("RF_LESSON_PLAYER PASS "+Title+": Unity JSON/lifecycle, actual SDK/socket, two-player roster, input ACK and sampled movement");smoke=false;Application.Quit(0);}
                else if(Now-smokeStarted>20){Debug.LogError("RF_LESSON_PLAYER FAIL "+Title+": "+Notice+" "+Status+" roster="+SmokeRoster+" ack="+SmokeAck);smoke=false;Application.Quit(1);}
            }
        }
        private static bool Key(KeyCode a,KeyCode b)=>Input.GetKey(a)||Input.GetKey(b);
        protected virtual void OnApplicationFocus(bool value){focused=value;if(!value&&ready)Pump(0,0);}
        protected virtual void OnApplicationPause(bool value){paused=value;if(value&&ready)Pump(0,0);}
        [Preserve] public void OnTransportMessage(string json){if(ready)Receive(json);}
        protected virtual void OnGUI()
        {
            GUI.skin.label.wordWrap=true;
            GUILayout.BeginArea(new Rect(10,10,Math.Max(270,Screen.width*.38f-15),Screen.height-20),GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Title);GUILayout.Label(Notice.Length>0?Notice:ready?Status:"Initialization failed");
            if(ready){
                Controls();AutoMove=GUILayout.Toggle(AutoMove,"Auto move (off: WASD / arrows)");
                Advanced=GUILayout.Toggle(Advanced,"Advanced settings");
                if(Advanced){
                    ShowAuthority=GUILayout.Toggle(ShowAuthority,"Authority outlines");Details();
                    GUI.SetNextControlName("Endpoint");Endpoint=GUILayout.TextField(Endpoint);
                    GUILayout.BeginHorizontal();
                    if(GUILayout.Button("Reconnect")){GUI.FocusControl(null);TryConnect();}
                    if(GUILayout.Button("Disconnect"))Disconnect();
                    GUILayout.EndHorizontal();
                }
                editing=GUI.GetNameOfFocusedControl()=="Endpoint";
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        protected virtual void OnDisable(){if(ready)Disconnect();}
        protected virtual void OnDestroy(){if(ready)Release();World?.Dispose();}
    }
}
