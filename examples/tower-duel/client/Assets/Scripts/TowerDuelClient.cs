using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace TowerDuel
{
    public sealed class TowerDuelClient : MonoBehaviour
    {
        public DuelSession Session { get; private set; }
        public DuelMap Map { get; private set; }
        public DuelRules Rules {get;private set;}
        public Camera Camera { get; private set; }
        public string Server { get; private set; } = "http://127.0.0.1:18110";
        public bool Selected { get; set; }
        public string SelectedCard {get;private set;}="guard";
        public DuelPresentation Presentation=>presentation;
        public DuelAudio Audio {get;private set;}
        public DuelUnitAssets UnitAssets {get;private set;}
        private bool restoreChecked;
        public const float KeepHealthHeight=10.2f;
        public const float TowerApproachClearance=1.7f;
        private const float SideMarginPixels=8f;
        public float BoardBottom=>boardBottom;
        public float BoardTop=>boardTop;
        private DuelHud hud;
        private DuelPresentation presentation;
        private DuelVerification verification;
        private int viewSeat = -1, screenWidth, screenHeight;
        private Rect framedSafeArea;
        private float boardBottom=.19f,boardTop=.02f;
        private string renderedMatch;

        private void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            gameObject.name = "TowerDuelClient";
#if UNITY_WEBGL && !UNITY_EDITOR
            // The published player and WebSocket endpoint share one HTTPS origin.
            Server = new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
#else
            Server = Argument("-td-server", Server);
#endif
            Map = DuelMap.Load();
            Rules=DuelRules.Load();
            UnitAssets=new DuelUnitAssets(Rules);
            Session = new DuelSession(Map,Rules);
            Audio=new DuelAudio(transform);
            Session.ActionConfirmed+=message=>Audio.Play(message.accepted?"deploy":"reject",true);
            Camera = Camera.main;
            if (!Camera) throw new InvalidOperationException("Arena camera is missing");
            if(!Camera.GetComponent<AudioListener>())Camera.gameObject.AddComponent<AudioListener>();
            presentation = new DuelPresentation(transform, Map,Rules);
            FrameCamera();
            SetViewSeat(0);
            hud = new DuelHud(this);
            if(UnitAssets.Ready) TryRestoreAfterAssets();
            else if(UnitAssets.CanStart) StartCoroutine(UnitAssets.Preload());
            if (Debug.isDebugBuild && Argument("-td-auto", "") != "") verification = new DuelVerification(this);
        }

        private void TryRestoreAfterAssets()
        {
            if(restoreChecked || !UnitAssets.Ready)return;
            restoreChecked=true;
            Session.TryRestore(Server,gameObject.name);
        }
        public void RetryUnitAssets()
        {
            if(UnitAssets.CanStart)StartCoroutine(UnitAssets.Preload());
        }

        private void Update()
        {
            if (Session == null) return;
            TryRestoreAfterAssets();
            Session.Pump();
            var state = Session.State;
            if(state!=null&&state.match!=renderedMatch){presentation.Reset();renderedMatch=state.match;}
            int seat = state?.seat ?? 0;
            if (screenWidth != Screen.width || screenHeight != Screen.height || framedSafeArea != Screen.safeArea) FrameCamera();
            if (seat != viewSeat) SetViewSeat(seat);
            if (!Session.CanAct || state == null || (state.phase != "playing" && state.phase != "overtime")) Selected = false;
            if(Selected&&(!Session.InHand(SelectedCard)||Session.CardPending(SelectedCard)))Selected=false;
            // HUD owns independent gesture previews and the shared deployment grid.
            presentation.Update(Session);
            Audio.Observe(state);
            hud.Update();
            verification?.Update();
        }

        // Fit every legal placement coordinate inside the horizontal safe area.
        // Padding is in physical pixels, not world units, so narrow phones do
        // not waste additional horizontal space on a fixed world-space margin.
        public static float HorizontalFitSize(float mapHalfWidth,float safeWidth,float pixelHeight)
        {
            float inset=Mathf.Min(SideMarginPixels,Mathf.Max(0f,safeWidth*.04f));
            return mapHalfWidth*Mathf.Max(1f,pixelHeight)/Mathf.Max(1f,safeWidth-2f*inset);
        }

        // Mildly enlarge only combat sprites when a wide arena must be shown
        // through a narrow viewport; logical positions and hit radii never change.
        public static float VisualScaleForSize(float orthographicSize)=>
            Mathf.Clamp(1f+Mathf.Max(0f,orthographicSize-18f)*.03f,1f,1.20f);

        private void FrameCamera()
        {
            screenWidth=Screen.width;screenHeight=Screen.height;
            framedSafeArea=Screen.safeArea;
            Camera.transform.rotation=Quaternion.Euler(60,0,0);
            Camera.transform.position=-Camera.transform.forward*48;
            Camera.orthographic=true;

            float availableHeight=Mathf.Max(.25f,1-boardBottom-boardTop);
            float sin=Mathf.Sin(60*Mathf.Deg2Rad),cos=Mathf.Cos(60*Mathf.Deg2Rad);
            float keepDistance=0f,unitHeight=0f;
            foreach(var tower in Map.towers)
                if(tower.kind=="keep")keepDistance=Mathf.Max(keepDistance,Mathf.Abs(tower.z));
            foreach(var card in Rules.cards)
                if(!card.IsSpell)unitHeight=Mathf.Max(unitHeight,presentation.UnitHeight(card.id));

            float safeWidth=framedSafeArea.width>0?framedSafeArea.width:screenWidth;
            float safeCenterX=framedSafeArea.width>0?framedSafeArea.center.x:screenWidth*.5f;
            float horizontalSize=HorizontalFitSize(Map.width*.5f,safeWidth,screenHeight);
            float size=horizontalSize;
            float far=0f,near=Map.length*.5f*sin+.15f;

            // The roof and the tallest troop at the rear are the vertical
            // extremes. Refine after sprite compensation to avoid clipping.
            for(int i=0;i<3;i++)
            {
                float artScale=VisualScaleForSize(size);
                far=Mathf.Max(keepDistance*sin+KeepHealthHeight*artScale*cos+.45f,
                    Map.length*.5f*sin+unitHeight*artScale*cos+.15f);
                size=Mathf.Max(horizontalSize,(far+near)*.5f/availableHeight);
            }
            float focus=(far-near)*.5f;
            float center=boardBottom+availableHeight*.5f;
            Camera.orthographicSize=size;

            // The HUD's top and bottom are reserved; center the battlefield
            // in the remaining height without changing camera yaw or tilt.
            Camera.transform.position+=Camera.transform.up*(focus-(center-.5f)*2f*size);
            Camera.transform.position-=Camera.transform.right*
                ((safeCenterX-screenWidth*.5f)*2f*size/Mathf.Max(1f,screenHeight));
            Camera.rect=new Rect(0,0,1,1);
            Camera.backgroundColor=Map.HasWall?new Color(151f/255f,210f/255f,30f/255f):new Color(.16f,.28f,.14f);
            DuelLighting.Apply(Camera);
            if(viewSeat>=0)presentation.Environment.SetView(Camera,viewSeat);
        }
        private void SetViewSeat(int seat)
        {
            viewSeat=seat;
            presentation.Environment.SetView(Camera,seat);
        }
        public void SetBoardInsets(float bottom,float top)
        {
            bottom=Mathf.Clamp(bottom,.14f,.46f);top=Mathf.Clamp(top,.02f,.25f);
            if(Mathf.Abs(bottom-boardBottom)<.001f&&Mathf.Abs(top-boardTop)<.001f)return;
            boardBottom=bottom;boardTop=top;FrameCamera();
        }

        public void Connect(string server, string mode, string room, string name, string[] deck = null)
        {
            if(!UnitAssets.Ready)return;
            Selected = false; HidePreview(); presentation.Reset();
            try { Session.Connect(server, mode, room, name, gameObject.name, deck); Server = server; }
            catch (Exception error) { Debug.LogWarning("Connect: " + error.Message); Session.Disconnect("伺服器位址無效，請輸入 http:// 或 https:// 位址。"); }
        }
        public void Leave() { Selected = false; HidePreview(); Session.Leave(); presentation.Reset(); }
        public void Select(string card){if(Rules.Find(card)==null||!Session.CanAct||!Session.InHand(card)||Session.CardPending(card))return;Audio.Play("select",true);if(Selected&&SelectedCard==card)Selected=false;else{SelectedCard=card;Selected=true;}}
        public void BeginDeployment(string card){if(Rules.Find(card)!=null){SelectedCard=card;Selected=true;}}
        public bool CanDeploy(Vector3 point)
        {
            return Session.CanDeploy(SnapDeployment(point),SelectedCard);
        }
        public Vector3 SnapDeployment(Vector3 point)=>Map.SnapDeployment(point,Rules.Find(SelectedCard));
        public void Deploy(Vector3 point) { if (Session.Deploy(SnapDeployment(point),SelectedCard)) Selected = false; }
        public void ShowPreview(Vector3 point, bool visible)
        {
            point=SnapDeployment(point);
            var state = Session.State;
            var card=Rules.Find(SelectedCard);
            bool valid = CanDeploy(point);
            presentation.DeploymentGrid.Highlight(DuelPresentation.MirrorDepth(point,state?.seat??0),visible);
            presentation.Preview(point,visible,valid,SelectedCard,state?.seat??0,DuelRules.Formation(state?.seat??0,point,card),card!=null?(card.IsSpell?card.radius:card.BodyRadius):.32f, state);
        }
        public void HidePreview()
        {
            presentation?.Preview(Vector3.zero,false,false);presentation?.DeploymentGrid.Highlight(Vector3.zero,false);
            if(!Selected)presentation?.DeploymentGrid.Hide();
        }

        [Preserve] public void OnTransportMessage(string json) => Session?.ReceiveBrowser(json);
        [Preserve] public void OnRoomCodeCopied(string result) => hud?.RoomCodeCopied(result);
        private void OnApplicationFocus(bool focused){if(!focused)hud?.CancelInput();}
        private void OnApplicationPause(bool paused){if(paused)hud?.CancelInput();}
        private void OnDestroy() { verification?.Dispose(); hud?.Dispose(); Session?.Dispose(); presentation?.Dispose(); Audio?.Dispose(); }
        public static string Argument(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }
}
