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
        public const float KeepHealthHeight=10.2f;
        public const float TowerApproachClearance=1.7f;
        private const float ArenaSidePadding=.6f;
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
            Session.TryRestore(Server,gameObject.name);
            if (Debug.isDebugBuild && Argument("-td-auto", "") != "") verification = new DuelVerification(this);
        }

        private void Update()
        {
            if (Session == null) return;
            Session.Pump();
            var state = Session.State;
            if(state!=null&&state.match!=renderedMatch){presentation.Reset();renderedMatch=state.match;}
            int seat = state?.seat ?? 0;
            if (screenWidth != Screen.width || screenHeight != Screen.height || framedSafeArea != Screen.safeArea) FrameCamera();
            if (seat != viewSeat) SetViewSeat(seat);
            if (!Session.CanAct || state == null || (state.phase != "playing" && state.phase != "overtime")) Selected = false;
            if (!Selected) HidePreview();
            if(Selected&&state!=null)presentation.DeploymentGrid.Show(state,Rules.Find(SelectedCard));
            else presentation.DeploymentGrid.Hide();
            presentation.Update(Session);
            Audio.Observe(state);
            hud.Update();
            verification?.Update();
        }

        private void FrameCamera()
        {
            screenWidth = Screen.width; screenHeight = Screen.height;
            framedSafeArea = Screen.safeArea;
            Camera.transform.rotation=Quaternion.Euler(60,0,0);
            Camera.transform.position=-Camera.transform.forward*48;
            Camera.orthographic = true;
            // Frame the castles and their labels, rather than the empty rear grass.
            // The header's empty centre can show the arena; its buttons stay at the sides.
            float height=Mathf.Max(.25f,1-boardBottom-boardTop);
            float sin=Mathf.Sin(60*Mathf.Deg2Rad),cos=Mathf.Cos(60*Mathf.Deg2Rad);
            float keepDistance=0;
            float tacticalHalfWidth=0;
            foreach(var tower in Map.towers)
            {
                if(tower.kind=="keep")keepDistance=Mathf.Max(keepDistance,Mathf.Abs(tower.z));
                tacticalHalfWidth=Mathf.Max(tacticalHalfWidth,Mathf.Abs(tower.x)+TowerApproachClearance);
            }
            foreach(float gate in Map.bridges)
                tacticalHalfWidth=Mathf.Max(tacticalHalfWidth,Mathf.Abs(gate)+Map.bridgeHalfWidth);
            float far=keepDistance*sin+KeepHealthHeight*cos+.45f;
            float near=(keepDistance+2.1f)*sin+.15f;
            float focus=(far-near)*.5f;
            float center=boardBottom+height*.5f;
            float size=(far+near)*.5f/height;
            // Include the full playable map, not only its towers. Narrow screens
            // must zoom out instead of cropping the outside deployment cells.
            // Horizontal safe-area insets (e.g. a side notch) are not drawable.
            float safeWidth=framedSafeArea.width>0?framedSafeArea.width:screenWidth;
            float safeCenterX=framedSafeArea.width>0?framedSafeArea.center.x:screenWidth*.5f;
            float safeAspect=safeWidth/Mathf.Max(1f,screenHeight);
            float playableHalfWidth=Mathf.Max(Map.width*.5f,tacticalHalfWidth)+ArenaSidePadding;
            size=Mathf.Max(size,playableHalfWidth/Mathf.Max(.01f,safeAspect));
            // Keep troops at the far rear edge inside the backbuffer.
            float unitHeight=0;
            foreach(var card in Rules.cards)if(!card.IsSpell)unitHeight=Mathf.Max(unitHeight,presentation.UnitHeight(card.id));
            size=Mathf.Max(size,(Map.length*.5f*sin+unitHeight*cos+.15f-focus)/(2*(1-center)));
            Camera.orthographicSize=size;
            // Render the entire backbuffer so transparent HUD text never accumulates.
            // The offset preserves the arena framing above the dock without a partial viewport.
            Camera.transform.position += Camera.transform.up * (focus-(center-.5f)*2*size);
            // Keep the arena centered in the usable screen area without
            // rotating the camera or changing world-space positions.
            Camera.transform.position -= Camera.transform.right *
                ((safeCenterX-screenWidth*.5f)*2*size/Mathf.Max(1f,screenHeight));
            Camera.rect = new Rect(0, 0, 1, 1);
            Camera.backgroundColor = Map.HasWall ? new Color(151f/255f, 210f/255f, 30f/255f) : new Color(.16f, .28f, .14f);
            DuelLighting.Apply(Camera);
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
            Selected = false; HidePreview(); presentation.Reset();
            try { Session.Connect(server, mode, room, name, gameObject.name, deck); Server = server; }
            catch (Exception error) { Debug.LogWarning("Connect: " + error.Message); Session.Disconnect("伺服器位址無效，請輸入 http:// 或 https:// 位址。"); }
        }
        public void Leave() { Selected = false; HidePreview(); Session.Leave(); presentation.Reset(); }
        public void Select(string card){if(Rules.Find(card)==null||!Session.CanAct||!Session.InHand(card))return;Audio.Play("select",true);if(Selected&&SelectedCard==card)Selected=false;else{SelectedCard=card;Selected=true;}}
        public void BeginDeployment(string card){if(Rules.Find(card)!=null){SelectedCard=card;Selected=true;}}
        public bool CanDeploy(Vector3 point)
        {
            point=SnapDeployment(point);
            var state=Session.State;var card=Rules.Find(SelectedCard);
            return Session.CanAct&&state!=null&&card!=null&&Session.InHand(SelectedCard)&&!Session.Pending&&(state.phase=="playing"||state.phase=="overtime")&&state.energy>=card.cost&&Map.CanDeploy(state.seat,point,state,card);
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
        private void OnDestroy() { verification?.Dispose(); hud?.Dispose(); Session?.Dispose(); presentation?.Dispose(); Audio?.Dispose(); }
        public static string Argument(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }
}
