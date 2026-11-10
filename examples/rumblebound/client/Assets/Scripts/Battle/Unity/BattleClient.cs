using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;
using Realtime.Transport;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleClient:MonoBehaviour
    {
        private UnityGameApi api;
        private BattlePresentation presentation;
        private readonly GameProjection projection=new GameProjection();
        private string loadedKey;
        private bool focused=true;
        private const string IdentityKey="Rumblebound.Example.Battle.Identity";
        private int savedRevision;
        private int googleRevision;
        private bool persistIdentity;
        public bool GooglePending{get;private set;}
        public string GoogleNotice{get;private set;}="";
        [Serializable] private sealed class SavedIdentity{public string credential,userID;}
        public GameFlow Flow{get;private set;}
        public Vector2 Aim{get;private set;}
        public double Now=>Time.realtimeSinceStartupAsDouble;
        public BattlePresentation Presentation=>presentation;
        public bool AutomaticDrive{get;set;}
        public bool Outlines{get;set;}
        public bool Verification{get;private set;}
        public Vector2 VerificationDrive{get;set;}
        public BattleTouchInput TouchInput{get;}=new BattleTouchInput();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal Vector2? ReviewAim;
        internal BattlePreviewKind? ReviewPreview;
        internal bool ReviewSelecting;
#endif
        public bool CanTouchControl=>Flow?.Battle.CanControl==true&&System.Array.Exists(Flow.Battle.State.Latest.players,p=>p.playerID==Flow.Battle.State.PlayerID&&p.hp>0&&!p.left);
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void RB_GoogleBegin(string target);
        [DllImport("__Internal")] private static extern void RB_GoogleCancel();
#endif
        public void GoogleLogin()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(Flow==null||Flow.Busy||Flow.Identity.IsAuthenticated||GooglePending)return;
            GooglePending=true;googleRevision=Flow.Identity.Revision;GoogleNotice="";RB_GoogleBegin(gameObject.name);
#else
            GoogleNotice="Google 登入請使用瀏覽器或 WebGL。";
#endif
        }
        [Preserve] public void OnGoogleSession(string value)
        {
            if(!GooglePending||Flow==null||googleRevision!=Flow.Identity.Revision)return;GooglePending=false;
            try{var identity=JsonUtility.FromJson<Rumblebound.Gameplay.SessionReply>(value);if(identity==null||!Flow.Restore(identity.session,identity.userID))GoogleNotice="登入回覆無效。";}catch(ArgumentException){GoogleNotice="登入回覆無效。";}
        }
        [Preserve] public void OnGoogleError(string error){GooglePending=false;GoogleNotice=error;}
        private void Start()
        {
            Application.runInBackground=true;
            Input.multiTouchEnabled=true;
            api=GetComponent<UnityGameApi>();presentation=GetComponent<BattlePresentation>();
            string origin="http://127.0.0.1:18093";
            if(Application.platform==RuntimePlatform.WebGLPlayer&&!string.IsNullOrEmpty(Application.absoluteURL))origin=new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
            var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-rf-server")origin=args[++i].TrimEnd('/');
            Verification=Array.IndexOf(args,"-rf-battle-smoke")>=0;
            persistIdentity=!Verification&&Array.IndexOf(args,"-rf-combat-smoke")<0&&Array.IndexOf(args,"-rf-touch-smoke")<0&&Array.IndexOf(args,"-rf-hud-review")<0;
            if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))throw new InvalidOperationException("Invalid server origin");
            api.Origin=origin;
            gameObject.name="Battle-"+Guid.NewGuid().ToString("N");
#if !UNITY_WEBGL
            string helper=Path.Combine(Application.streamingAssetsPath,Application.platform==RuntimePlatform.WindowsPlayer?"realtime-webtransport.exe":"realtime-webtransport");
            if(File.Exists(helper))MessageTransport.NativeWebTransportExecutable=helper;
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-rf-native-transport"&&Path.IsPathRooted(args[i+1])&&File.Exists(args[i+1]))MessageTransport.NativeWebTransportExecutable=args[++i];
#endif
            Flow=new GameFlow(api,new UnityGameJson(),origin.Replace("https:","wss:").Replace("http:","ws:")+"/ws",gameObject.name);
            if(persistIdentity)try{var saved=JsonUtility.FromJson<SavedIdentity>(PlayerPrefs.GetString(IdentityKey,""));if(saved!=null)Flow.Restore(saved.credential,saved.userID);}catch(ArgumentException){}
            Flow.Battle.PreviewAction+=presentation.Preview;Flow.Battle.ResolveAction+=presentation.Resolve;
        }
        private void Update()
        {
            if(Flow==null)return;
            BattleHud.PollTouches();
            var drive=ReadControls();
            if(AutomaticDrive&&Flow.Battle.CanControl&&!BattleHud.BlocksGameplay)drive=new Vector2(0,(float)Math.Sin(Now));
            if(Verification)drive=VerificationDrive;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(ReviewAim.HasValue)Aim=ReviewAim.Value;
#endif
            UpdateFacing(drive);Flow.Face(FacingPoint.x,FacingPoint.y);Flow.Pump(Now,drive.x,drive.y);
            if(Flow.Content!=null){
                presentation.Configure(Flow.Playback.Active?Flow.Playback.Content:Flow.Content);
                var world=Flow.Playback.Active?Flow.Playback.View:projection.Update(Flow,Now);presentation.Present(world,Aim,Outlines,Flow.Playback.Active?Flow.Playback.Clock.Tick/Flow.Playback.Content.Rules.tickRate:Now);
                string key=world.snapshot?.matchID+":"+world.generation;
                if(Flow.Battle.Connected&&loadedKey!=key){loadedKey=key;Flow.SceneLoaded();}
            }
            if(persistIdentity&&Flow.Identity.Revision!=savedRevision){savedRevision=Flow.Identity.Revision;if(Flow.Identity.IsAuthenticated)PlayerPrefs.SetString(IdentityKey,JsonUtility.ToJson(new SavedIdentity{credential=Flow.Identity.Credential,userID=Flow.Identity.UserID}));else PlayerPrefs.DeleteKey(IdentityKey);PlayerPrefs.Save();}
        }
        public void Action(string kind)=>Flow?.AimAction(kind,Aim.x,Aim.y,Now);
        [Preserve] public void OnTransportMessage(string value){if(Flow!=null)Flow.Battle.Transport.Receive(Flow.Battle.Transport.Epoch,value);}
        private void OnApplicationFocus(bool value){focused=value;if(!value)TouchInput.Reset();}
        private void OnApplicationPause(bool value){if(value){TouchInput.Reset();Orders.Clear();CurrentTargetID=null;}}
        private void OnDestroy(){
#if UNITY_WEBGL && !UNITY_EDITOR
            if(GooglePending)RB_GoogleCancel();
#endif
            if(Flow!=null){Flow.Battle.PreviewAction-=presentation.Preview;Flow.Battle.ResolveAction-=presentation.Resolve;Flow.Dispose();Flow=null;}}
    }
}
