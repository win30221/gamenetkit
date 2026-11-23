using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace TowerDuel
{
    public sealed class DuelHud : IDisposable
    {
        private readonly TowerDuelClient client;
        private readonly UIDocument document;
        private readonly PanelSettings panel;
        private readonly VisualElement root,world,lobby,waiting,result,dock,health,dragPreview,energyRow;
        private readonly VisualElement dragFrame,nextFrame;
        private readonly DuelMenu menu;
        private readonly Button ready,copy;
        private readonly Dictionary<string,Button> cards=new Dictionary<string,Button>();
        private readonly List<DuelCardDrag> cardDrags=new List<DuelCardDrag>();
        private Button draggingCard;
        private Vector2 dragOrigin,dragPosition,dragSize,dragCardOrigin;
        private bool battlefieldDragStarted,hasDragPreviewPoint;
        private Vector3 dragPreviewPoint;
        private float dragCardOpacity=1f,lastDragAnimationTime;
        private const float DragCardFadeSeconds=.16f;
        private const float DragCardMaxOpacity=.68f;
        private readonly List<VisualElement> pips=new List<VisualElement>();
        private readonly Dictionary<long,VisualElement> bars=new Dictionary<long,VisualElement>();
        private readonly Dictionary<long,VisualElement> fills=new Dictionary<long,VisualElement>();
        private readonly Dictionary<long,Label> healthLabels=new Dictionary<long,Label>();
        private float displayedEnergy,targetEnergy;
        private string energyMatch;
        private int pointer=-1;
        private double nextLabels;
        private double copyFeedbackUntil;
        private string copyRoom="";
        private bool wasLobby=true;
        private Rect appliedSafeArea;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void TD_CopyRoomCode(string code,string target);
        [DllImport("__Internal")] private static extern void TD_CancelRoomCodeCopy();
        [DllImport("__Internal")] private static extern void TD_CloseKeyboard();
#endif
        public DuelHud(TowerDuelClient client)
        {
            this.client=client;
            panel=UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("TowerDuel/DuelPanel"));
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("TowerDuel/DuelTheme");panel.sortingOrder=10;
            document=client.gameObject.AddComponent<UIDocument>();document.panelSettings=panel;
            // Our custom theme supplies its own document root layout.
            var documentRoot=document.rootVisualElement;
            documentRoot.style.position=Position.Absolute;documentRoot.style.left=0;documentRoot.style.right=0;documentRoot.style.top=0;documentRoot.style.bottom=0;
            Resources.Load<VisualTreeAsset>("TowerDuel/Duel").CloneTree(document.rootVisualElement);
            root=document.rootVisualElement.Q("duelRoot");root.style.unityFontDefinition=FontDefinition.FromSDFFont(Resources.Load<FontAsset>("TowerDuel/DuelFont"));
            root.RegisterCallback<GeometryChangedEvent>(_=>LayoutViewport());
            root.Query<Label>().ForEach(label=>label.enableRichText=false);
            world=root.Q("worldInput");health=root.Q("healthLayer");lobby=root.Q("lobbyLayer");waiting=root.Q("waitingLayer");result=root.Q("resultLayer");dock=root.Q("dock");
            dragPreview=root.Q("dragPreview");energyRow=root.Q("energyRow");
            dragFrame=CardFrame();dragPreview.Insert(1,dragFrame);Display(dragFrame,false);
            nextFrame=CardFrame(true);root.Q("nextCardArt").Add(nextFrame);Display(nextFrame,false);
            menu=new DuelMenu(lobby,client,BlurInputs);
            ready=root.Q<Button>("readyButton");
            foreach(var definition in client.Rules.cards)
            {
                string id=definition.id;var button=new Button{name=id+"Card",tooltip=definition.name+" · "+definition.cost+" 能量\n"+definition.description};button.AddToClassList("unit-card");
                var portrait=new VisualElement{pickingMode=PickingMode.Ignore};portrait.AddToClassList("card-art");portrait.style.backgroundImage=DuelSpriteLibrary.Card(id);button.Add(portrait);
                if(DuelSpriteLibrary.Converted(id)){button.AddToClassList("b3-card");button.Add(CardFrame());}
                var title=new Label(definition.name){pickingMode=PickingMode.Ignore};title.AddToClassList("card-name");button.Add(title);
                var cost=new Label(definition.cost.ToString("0")){pickingMode=PickingMode.Ignore};cost.AddToClassList("card-cost");button.Add(cost);
                root.Q("cardRow").Add(button);
                button.clicked+=()=>{client.Select(id);world.Focus();};cards.Add(id,button);
                var drag=new DuelCardDrag(CanPlay,
                    (point,touch)=>StartDrag(id,button,point,touch),MoveDrag,DropDrag,CancelDrag,
                    ()=>{client.Select(id);world.Focus();});
                button.AddManipulator(drag);cardDrags.Add(drag);
            }
            for(int i=0;i<10;i++)
            {
                var pip=new VisualElement();pip.AddToClassList("energy-pip");
                var fill=new VisualElement();fill.AddToClassList("energy-pip-fill");pip.Add(fill);
                root.Q("energyPips").Add(pip);pips.Add(fill);
            }
            client.Session.StateChanged+=ObserveEnergy;client.Session.ActionConfirmed+=ObserveActionEnergy;
            root.Q<Button>("leaveButton").clicked+=client.Leave;
            root.Q<Button>("waitingLeaveButton").clicked+=client.Leave;
            root.Q<Button>("backButton").clicked+=client.Leave;
            copy=root.Q<Button>("copyButton");copy.clicked+=CopyRoomCode;
            ready.clicked+=()=>client.Session.Ready();
            root.Q<Button>("rematchButton").clicked+=()=>client.Session.Rematch();
            root.Q<Button>("audioButton").clicked+=()=>client.Audio.Toggle();
            world.RegisterCallback<PointerDownEvent>(evt=>{
                if(!evt.isPrimary||evt.button!=0||!client.Selected||client.Session.Pending)return;
                if(!WorldPoint(evt.position,out var p))return;
                pointer=evt.pointerId;world.CapturePointer(pointer);client.ShowPreview(p,true);evt.StopPropagation();
            });
            world.RegisterCallback<PointerMoveEvent>(evt=>{if(client.Selected&&WorldPoint(evt.position,out var p))client.ShowPreview(p,true);else client.HidePreview();});
            world.RegisterCallback<PointerUpEvent>(evt=>{
                if(pointer!=evt.pointerId)return;
                bool valid=WorldPoint(evt.position,out var p);world.ReleasePointer(pointer);pointer=-1;
                if(valid)client.Deploy(p);client.HidePreview();evt.StopPropagation();
            });
            world.RegisterCallback<PointerCancelEvent>(_=>CancelPointer());
            world.RegisterCallback<PointerCaptureOutEvent>(_=>{pointer=-1;client.HidePreview();});
            root.RegisterCallback<KeyDownEvent>(evt=>{if(evt.keyCode==KeyCode.Escape){CancelCardDrags();client.Selected=false;CancelPointer();}});
            DuelCartoonSkin.Apply(root);
            // ShowCards(false) selects the battle page during DuelMenu construction.
            // Reapply that selected texture after skinning all generic menu buttons.
            DuelCartoonSkin.SetMenuTab(root.Q<Button>("battleTab"), true);
            Update();
        }
        private bool CanPlay()=>client.Session.CanAct&&(client.Session.State.phase=="playing"||client.Session.State.phase=="overtime")&&!client.Session.Pending;
        private static VisualElement CardFrame(bool small=false)
        {
            var frame=new VisualElement{pickingMode=PickingMode.Ignore};frame.AddToClassList("b3-card-frame");
            if(small)frame.AddToClassList("b3-small-frame");return frame;
        }
        private void BlurInputs()
        {
            menu.BlurInputs();
            world.Focus();
#if UNITY_WEBGL && !UNITY_EDITOR
            TD_CloseKeyboard();
#endif
        }
        private void StartDrag(string card,Button button,Vector2 point,bool touch)
        {
            client.BeginDeployment(card);draggingCard=button;
            battlefieldDragStarted=false;hasDragPreviewPoint=false;
            dragCardOpacity=1f;lastDragAnimationTime=Time.unscaledTime;
            bool b3=DuelSpriteLibrary.Converted(card);dragPreview.EnableInClassList("b3-card",b3);Display(dragFrame,b3);
            dragOrigin=point;dragPosition=point;
            dragSize=button.worldBound.size;dragCardOrigin=button.worldBound.position;
            dragPreview.style.scale=new Scale(new Vector3(dragSize.x/112,dragSize.y/150,1));
            button.AddToClassList("dragging");
            dragPreview.Q("dragArt").style.backgroundImage=button.Q(className:"card-art").resolvedStyle.backgroundImage;
            var definition=client.Rules.Find(card);
            dragPreview.Q<Label>("dragName").text=definition.name;
            dragPreview.Q<Label>("dragCost").text=definition.cost.ToString("0");
            MoveDrag(point);
        }
        private bool DropPoint(Vector2 point,out Vector3 worldPoint)
        {
            worldPoint=default;
            return root.worldBound.Contains(point)&&!root.Q("dockFrame").worldBound.Contains(point)
                &&!root.Q("audioButton").worldBound.Contains(point)&&!root.Q("leaveButton").worldBound.Contains(point)&&WorldPoint(point,out worldPoint);
        }
        private bool DragOnBattlefield(Vector2 point)
        {
            // The card/placement visual mode depends on the pointer crossing the hand,
            // not on whether a valid deployment point can currently be resolved.
            return point.y<root.Q("dockFrame").worldBound.yMin;
        }
        private void MoveDrag(Vector2 point)
        {
            dragPosition=point;
            bool battlefield=DragOnBattlefield(point);
            battlefieldDragStarted|=battlefield;

            // A reversible, time-based card fade works for mouse and touch,
            // including a fast return from the arena to the hand.
            float now=Time.unscaledTime;
            float delta=Mathf.Max(0f,now-lastDragAnimationTime);
            lastDragAnimationTime=now;
            dragCardOpacity=Mathf.MoveTowards(dragCardOpacity,battlefield?0f:1f,delta/DragCardFadeSeconds);
            var position=dragCardOrigin+point-dragOrigin;
            dragPreview.style.translate=new Translate(position.x,position.y);
            dragPreview.style.opacity=dragCardOpacity*DragCardMaxOpacity;
            Display(dragPreview,dragCardOpacity>.001f);
            dragPreview.EnableInClassList("invalid",false);

            // The closest legal deployment cell is also the ghost's location
            // when the pointer is over the wall, enemy territory or outside
            // the playable map. Never render troops on an illegal cell.
            if(battlefield&&DragPoint(point,out var nearest))
            {
                dragPreviewPoint=nearest;hasDragPreviewPoint=true;
            }
            // During return-to-hand the last legal ghost stays in place until
            // the card has faded in, avoiding a blank intermediate frame.
            bool model=battlefieldDragStarted&&hasDragPreviewPoint&&(battlefield||dragCardOpacity<.999f);
            client.ShowPreview(dragPreviewPoint,model);
        }
        private bool DragPoint(Vector2 point,out Vector3 p)
        {
            p=default;
            if(!root.worldBound.Contains(point)||!WorldPoint(point,out var aim))return false;
            var state=client.Session.State;
            var card=client.Rules.Find(client.SelectedCard);
            if(state==null||card==null)return false;
            return client.Map.TryNearestDeployment(state.seat,aim,state,card,out p);
        }
        private void DropDrag(Vector2 point)
        {
            Vector3 p=default;
            // Rendering a snapped ghost does not bypass the actual input and
            // server-side placement requirements.
            bool valid=CanPlay()&&DropPoint(point,out var aim)&&client.CanDeploy(aim)
                &&DragPoint(point,out p)&&client.CanDeploy(p);
            if(valid)client.Deploy(p);
            CancelDrag();
        }
        private void CancelDrag()
        {
            draggingCard?.RemoveFromClassList("dragging");draggingCard=null;
            battlefieldDragStarted=false;hasDragPreviewPoint=false;
            dragCardOpacity=1f;dragPreview.style.opacity=DragCardMaxOpacity;
            Display(dragPreview,false);
            client.Selected=false;client.HidePreview();
        }
        private void CancelCardDrags(){foreach(var drag in cardDrags)drag.Cancel();CancelDrag();}
        private void CopyRoomCode()
        {
            var state=client.Session.State;
            if(state==null||string.IsNullOrEmpty(state.room))return;
            copyRoom=state.room;copyFeedbackUntil=0;
            GUIUtility.systemCopyBuffer=copyRoom;
            copy.text="正在複製…";copy.SetEnabled(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            TD_CopyRoomCode(copyRoom,client.gameObject.name);
#else
            RoomCodeCopied("copied");
#endif
        }
        public void RoomCodeCopied(string result)
        {
            if(client.Session.State?.room!=copyRoom)return;
            copy.SetEnabled(true);
            copy.text=result=="copied"?"已複製房間碼":result=="manual"?"請手動複製":"複製房間碼";
            copyFeedbackUntil=DuelSession.Now+3;
        }
        private void ResetCopy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TD_CancelRoomCodeCopy();
#endif
            copyRoom="";copyFeedbackUntil=0;copy.text="複製房間碼";copy.SetEnabled(true);
        }
        private bool WorldPoint(Vector2 ui,out Vector3 point)
        {
            point=Vector3.zero;float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
            if(width<=0||height<=0)return false;
            var screen=new Vector2(ui.x/width*Screen.width,(1-ui.y/height)*Screen.height);
            if(!client.Camera.pixelRect.Contains(screen))return false;
            var ray=client.Camera.ScreenPointToRay(screen);var plane=new Plane(Vector3.up,Vector3.zero);
            if(!plane.Raycast(ray,out var enter))return false;point=DuelPresentation.MirrorDepth(ray.GetPoint(enter),client.Session.State?.seat??0);return true;
        }
        private void CancelPointer(){if(pointer>=0&&world.HasPointerCapture(pointer))world.ReleasePointer(pointer);pointer=-1;client.HidePreview();}
        private void Text(string name,string value){var label=root.Q<Label>(name);if(label.text!=value)label.text=value;}
        private static void Display(VisualElement e,bool show)=>e.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        private void LayoutViewport()
        {
            if(dock==null||root.resolvedStyle.width<=0||root.resolvedStyle.height<=0)return;
            float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
            if(float.IsNaN(width)||float.IsNaN(height))return;
            appliedSafeArea=Screen.safeArea;
            float unitsPerPixel=height/Mathf.Max(1,Screen.height);
            menu?.Layout(appliedSafeArea,unitsPerPixel);
            float safeWidth=appliedSafeArea.width*unitsPerPixel;
            float availableWidth=Mathf.Max(1f,safeWidth-16f);
            // Hand occupies only the reserved 14.5% lower edge.
            const float dockFraction=.145f;
            float scale=Mathf.Min(.9f,availableWidth/600f,height*dockFraction/234f);
            var frame=root.Q("dockFrame");
            frame.style.left=appliedSafeArea.center.x*unitsPerPixel-300f*scale;
            frame.style.width=600f*scale;
            frame.style.height=height*dockFraction;
            frame.style.bottom=appliedSafeArea.yMin*unitsPerPixel;
            dock.style.left=300f*scale-300f;
            dock.style.bottom=height*dockFraction-234f*scale;
            dock.style.scale=new Scale(new Vector3(scale,scale,1));

            // The arena and card hand are height-framed. Scale the HUD from the
            // panel height as well; never let a wider viewport enlarge its badges.
            // Only positions change with horizontal aspect ratio.
            const float hudReferenceHeight=900f;
            const float leftBadgeWidth=196f, rightBadgeWidth=204f;
            float hudScale=height/hudReferenceHeight;
            float edgeInset=8f*hudScale;
            bool stacked=safeWidth<(leftBadgeWidth+rightBadgeWidth+16f)*hudScale+2f*edgeInset;
            var top=root.Q("topHud");
            top.style.top=7f*hudScale+(Screen.height-appliedSafeArea.yMax)*unitsPerPixel;
            top.style.left=edgeInset+appliedSafeArea.xMin*unitsPerPixel;
            top.style.right=edgeInset+(Screen.width-appliedSafeArea.xMax)*unitsPerPixel;
            top.style.height=(stacked?92f:44f)*hudScale;
            // Scale the two absolute-positioned corner groups individually. Do
            // not scale the full-width topHud or the right controls would drift.
            var info=top.Q(className:"header-copy");
            var actions=top.Q(className:"hud-actions");
            info.style.scale=new Scale(new Vector3(hudScale,hudScale,1));
            actions.style.scale=new Scale(new Vector3(hudScale,hudScale,1));
            actions.style.top=stacked?48f*hudScale:0f;
        }
        public void Update()
        {
            if(appliedSafeArea!=Screen.safeArea)LayoutViewport();
            var state=client.Session.State;
            if(state!=null&&wasLobby)BlurInputs();wasLobby=state==null;
            if(!CanPlay())CancelCardDrags();
            if(draggingCard!=null)MoveDrag(dragPosition);
            if(copyRoom!=""&&(state?.room!=copyRoom||(state.phase!="waiting"&&state.phase!="countdown")))ResetCopy();
            if(copyFeedbackUntil>0&&DuelSession.Now>=copyFeedbackUntil){copyFeedbackUntil=0;copy.text="複製房間碼";}
            Display(lobby,state==null);Display(waiting,state!=null&&(state.phase=="waiting"||state.phase=="countdown"));Display(result,state!=null&&state.phase=="ended");Display(dock,state!=null);Display(health,state!=null);
            if(state==null)menu.Update();
            Display(root.Q("topHud"),state!=null);Display(root.Q("dockFrame"),state!=null);
            if(state!=null&&state.phase=="ended")
            {
                Text("resultTitle",state.winner<0?"平手":state.winner==state.seat?"勝利！":"再接再厲");Text("resultReason",DuelText.Reason(state.reason));
                int own=0,enemy=0;foreach(var tower in state.towers)if(tower.hp==0){if(tower.seat==state.seat)enemy+=tower.kind=="keep"?3:1;else own+=tower.kind=="keep"?3:1;}
                Text("scoreLabel","破塔　你 "+Mathf.Min(3,own)+" : "+Mathf.Min(3,enemy)+" 對手");
                var rematch=root.Q<Button>("rematchButton");rematch.text=state.players[state.seat].rematch?"等待對手確認…":"同房再戰";rematch.SetEnabled(client.Session.CanAct&&!state.players[state.seat].rematch);
            }
            if(DuelSession.Now>=nextLabels)
            {
                nextLabels=DuelSession.Now+.1;
                root.Q<Button>("audioButton").text=client.Audio.Muted?"音效關":"音效開";
                if(state!=null)
                {
                    Text("roomLabel","房間 "+state.room+" · "+state.players[state.seat].name);
                    int seconds=Mathf.CeilToInt(state.remaining);Text("timerLabel",(state.phase=="overtime"?"加時 ":"")+seconds/60+":"+(seconds%60).ToString("00"));
                    var opponent=state.players[1-state.seat];
                    Text("noticeLabel",client.Session.Recovering?"正在恢復連線，對戰持續進行…":!opponent.connected&&opponent.reconnectRemaining>0?"等待對手返回　"+Mathf.CeilToInt(opponent.reconnectRemaining)+" 秒":client.Session.Pending?"正在確認出牌…":client.Session.Status);
                    Text("nextCardLabel","下一張\n"+client.Rules.Find(state.nextCard).name);
                    var next=root.Q("nextCardArt");next.style.backgroundImage=DuelSpriteLibrary.Card(state.nextCard);
                    bool nextB3=DuelSpriteLibrary.Converted(state.nextCard);next.EnableInClassList("b3-next",nextB3);Display(nextFrame,nextB3);
                    Text("inviteCode",state.room);
                    bool waitingForPlayers=state.phase=="waiting";
                    Text("waitingTitle",waitingForPlayers?"好友房":state.quick?"配對成功":"即將開始");
                    Display(root.Q("invitePanel"),waitingForPlayers&&!state.quick);
                    Display(ready,waitingForPlayers);
                    string names="";for(int i=0;i<2;i++)names+=(i==state.seat?"你：":"對手：")+(state.players[i].connected?state.players[i].name+(state.players[i].ready?"　已準備":"　尚未準備"):"等待加入")+(i==0?"\n":"");Text("playersLabel",names);
                    ready.SetEnabled(client.Session.CanAct&&waitingForPlayers&&!state.players[state.seat].ready);
                    ready.text=state.players[state.seat].ready?"已準備":"準備對戰";
                    Text("waitingHint",state.phase=="countdown"?"即將開始　"+Mathf.CeilToInt(state.countdown):"將房間碼傳給朋友，雙方準備後開始。");
                    foreach(var definition in client.Rules.cards)
                    {
                        var card=cards[definition.id];card.SetEnabled((state.phase=="playing"||state.phase=="overtime")&&!client.Session.Pending);
                        bool inHand=client.Session.InHand(definition.id);Display(card,inHand);card.SetEnabled(inHand&&CanPlay());
                        card.EnableInClassList("selected",client.Selected&&client.SelectedCard==definition.id);card.EnableInClassList("unaffordable",state.energy<definition.cost);
                    }
                    foreach(string id in state.hand)cards[id].BringToFront();
                }
            }
            if(state!=null)
            {
                // Frame both keeps in the remaining field, not underneath the HUD.
                client.SetBoardInsets(.16f+Screen.safeArea.yMin/Screen.height,
                    .06f+(Screen.height-Screen.safeArea.yMax)/Screen.height);
                UpdateHealth(state);
                UpdateEnergy(state);
            }
            else energyMatch=null;
        }
        private void ObserveActionEnergy(DuelMessage message){if(client.Session.State!=null)ObserveEnergy(client.Session.State);}
        private void ObserveEnergy(DuelMessage state)
        {
            if(energyMatch!=state.match||state.energy<targetEnergy)displayedEnergy=state.energy;
            energyMatch=state.match;targetEnergy=Mathf.Clamp(state.energy,0,10);
        }
        private void UpdateEnergy(DuelMessage state)
        {
            if(energyMatch!=state.match)ObserveEnergy(state);
            displayedEnergy=Mathf.Lerp(displayedEnergy,targetEnergy,1-Mathf.Exp(-18*Time.unscaledDeltaTime));
            if(targetEnergy>=9.999f)displayedEnergy=10;
            for(int i=0;i<pips.Count;i++)pips[i].style.width=Length.Percent(100*Mathf.Clamp01(displayedEnergy-i));
            bool full=targetEnergy>=9.999f&&CanPlay();
            energyRow.EnableInClassList("energy-full",full);
            Text("energyLabel","能量 "+displayedEnergy.ToString("0.0")+" / 10");
            Text("energyMode",state.phase=="overtime"?(full?"能量已滿・雙倍回能":"雙倍回能"):full?"能量已滿":"");
        }
        private void UpdateHealth(DuelMessage state)
        {
            var seen=new HashSet<long>();
            // Match the arena's height-based framing, independent of window width.
            float healthScale=root.resolvedStyle.height/1000f;
            foreach(var group in new[]{state.towers,state.units})foreach(var e in group)
            {
                if(e.hp<=0||e.hp>=e.maxHP)continue;seen.Add(e.id);
                if(!bars.TryGetValue(e.id,out var bar))
                {
                    bar=new VisualElement{pickingMode=PickingMode.Ignore};bar.AddToClassList("health-bar");if(client.Rules.Find(e.kind)!=null)bar.AddToClassList("unit-health");if(e.seat!=state.seat)bar.AddToClassList("enemy");
                    var fill=new VisualElement{pickingMode=PickingMode.Ignore};fill.AddToClassList("health-fill");bar.Add(fill);
                    var amount=new Label{pickingMode=PickingMode.Ignore};amount.AddToClassList("health-amount");bar.Add(amount);healthLabels.Add(e.id,amount);
                    if(e.kind=="keep"){var status=new Label{pickingMode=PickingMode.Ignore};status.AddToClassList("keep-status");bar.Add(status);}
                    else if(client.Rules.Find(e.kind)!=null){var status=new Label{pickingMode=PickingMode.Ignore};status.AddToClassList("unit-status");bar.Add(status);}
                    health.Add(bar);bars.Add(e.id,bar);fills.Add(e.id,fill);
                }
                bar.EnableInClassList("enemy",e.seat!=state.seat);
                bool unit=client.Rules.Find(e.kind)!=null;
                healthLabels[e.id].text=e.hp.ToString();
                if(e.kind=="keep")bar.Q<Label>(className:"keep-status").text=e.active?"主堡啟動":"主堡待命";
                else if(unit)
                {
                    float deployMilliseconds=Mathf.Clamp((float)(e.readyTick-client.Session.RenderTick)*1000/state.tickRate,0,client.Rules.Find(e.kind).deployTicks*1000f/state.tickRate);
                    bar.Q<Label>(className:"unit-status").text=e.IsDeploying(client.Session.RenderTick)?"部署 "+Mathf.CeilToInt(deployMilliseconds)+"ms":client.Session.SampleSlowed(e)?"減速":e.charging?"衝鋒":"";
                }
                var worldPos=DuelPresentation.MirrorDepth(unit?client.Session.Sample(e):e.Position,state.seat);
                var bodyCenter=worldPos+Vector3.up*(unit?client.Presentation.UnitHeight(e.kind)*.5f:1f);
                worldPos.y+=e.kind=="keep"?TowerDuelClient.KeepHealthHeight:e.kind=="tower"?(client.Map.HasWall?7.4f:3.9f):client.Presentation.UnitHeight(e.kind)+.22f;
                var screen=client.Camera.WorldToScreenPoint(worldPos);bool visible=!client.Presentation.Environment.WallOccludes(client.Camera,bodyCenter)&&!client.Presentation.Environment.WallOccludes(client.Camera,worldPos)&&screen.z>0&&client.Camera.pixelRect.Contains(new Vector2(screen.x,screen.y));Display(bar,visible);
                if(visible)
                {
                    var p=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
                    bar.style.scale=new Scale(new Vector3(healthScale,healthScale,1));
                    bar.style.left=p.x-(unit?16:42)*healthScale;
                    bar.style.top=Mathf.Max(20*healthScale,p.y);
                    fills[e.id].style.width=Length.Percent(100f*e.hp/e.maxHP);
                }
            }
            var removed=new List<long>();foreach(var pair in bars)if(!seen.Contains(pair.Key))removed.Add(pair.Key);foreach(long id in removed){bars[id].RemoveFromHierarchy();bars.Remove(id);fills.Remove(id);healthLabels.Remove(id);}
        }
        public void Dispose(){client.Session.StateChanged-=ObserveEnergy;client.Session.ActionConfirmed-=ObserveActionEnergy;CancelCardDrags();ResetCopy();UnityEngine.Object.Destroy(document);UnityEngine.Object.Destroy(panel);}
    }
}
