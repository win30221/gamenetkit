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
        private readonly VisualElement root,world,lobby,waiting,result,dock,health,energyRow;
        private readonly VisualElement nextFrame;
        private readonly DuelMenu menu;
        private readonly Button ready,copy;
        private readonly Dictionary<string,Button> cards=new Dictionary<string,Button>();
        private readonly List<DuelCardDrag> cardDrags=new List<DuelCardDrag>();
        private sealed class DragState
        {
            public string Card,Match;
            public Button Button;
            public VisualElement CardPreview;
            public DuelDeploymentPreview Model;
            public Vector2 Origin,Position,CardOrigin;
            public Vector3 PreviewPoint;
            public bool HasPoint;
            public float Opacity,LastAnimationTime;
        }
        private readonly Dictionary<int,DragState> activeDrags=new Dictionary<int,DragState>();
        private readonly HashSet<int> worldPointers=new HashSet<int>();
        private const int MaxActiveDrags=4;
        private const float DragCardFadeSeconds=.16f;
        private const float DragCardMaxOpacity=.68f;
        private readonly List<VisualElement> pips=new List<VisualElement>();
        private readonly Dictionary<long,VisualElement> bars=new Dictionary<long,VisualElement>();
        private readonly Dictionary<long,VisualElement> fills=new Dictionary<long,VisualElement>();
        private readonly Dictionary<long,Label> healthLabels=new Dictionary<long,Label>();
        private float displayedEnergy,targetEnergy;
        private string energyMatch;
        private double nextLabels;
        private double copyFeedbackUntil;
        private string copyRoom="";
        private bool wasLobby=true;
        private bool hudMenuOpen;
        private Rect appliedSafeArea;
        private int layoutScreenWidth=-1,layoutScreenHeight=-1;
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
            energyRow=root.Q("energyRow");
            root.Q("dragPreview").RemoveFromHierarchy();
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
                button.clicked+=()=>SelectCard(id);cards.Add(id,button);
                var drag=new DuelCardDrag(()=>CanStartCard(id),
                    (pointer,point)=>StartDrag(pointer,id,button,point),MoveDrag,DropDrag,CancelDrag,
                    ()=>SelectCard(id));
                button.AddManipulator(drag);cardDrags.Add(drag);
            }
            for(int i=0;i<10;i++)
            {
                var pip=new VisualElement();pip.AddToClassList("energy-pip");
                var fill=new VisualElement();fill.AddToClassList("energy-pip-fill");pip.Add(fill);
                root.Q("energyPips").Add(pip);pips.Add(fill);
            }
            client.Session.StateChanged+=ObserveEnergy;client.Session.ActionConfirmed+=ObserveActionEnergy;
            root.Q<Button>("hudMenuButton").clicked+=()=>SetHudMenu(!hudMenuOpen);
            SetHudMenu(false);
            root.Q<Button>("leaveButton").clicked+=()=>{SetHudMenu(false);client.Leave();};
            root.Q<Button>("waitingLeaveButton").clicked+=client.Leave;
            root.Q<Button>("backButton").clicked+=client.Leave;
            copy=root.Q<Button>("copyButton");copy.clicked+=CopyRoomCode;
            ready.clicked+=()=>client.Session.Ready();
            root.Q<Button>("rematchButton").clicked+=()=>client.Session.Rematch();
            root.Q<Button>("audioButton").clicked+=()=>client.Audio.Toggle();
            world.RegisterCallback<PointerDownEvent>(evt=>{
                if(hudMenuOpen){SetHudMenu(false);evt.StopPropagation();return;}
                if(activeDrags.ContainsKey(evt.pointerId)||(evt.pointerType==UnityEngine.UIElements.PointerType.mouse&&evt.button!=0)||!client.Selected)return;
                if(!cards.TryGetValue(client.SelectedCard,out var button)||!DragOnBattlefield(evt.position)||!DropPoint(evt.position,out _))return;
                if(!StartDrag(evt.pointerId,client.SelectedCard,button,evt.position,true))return;
                worldPointers.Add(evt.pointerId);world.CapturePointer(evt.pointerId);evt.StopPropagation();
            });
            world.RegisterCallback<PointerMoveEvent>(evt=>{
                if(!worldPointers.Contains(evt.pointerId))return;
                MoveDrag(evt.pointerId,evt.position);evt.StopPropagation();
            });
            world.RegisterCallback<PointerUpEvent>(evt=>{
                if(!worldPointers.Contains(evt.pointerId))return;
                ReleaseWorldPointer(evt.pointerId);DropDrag(evt.pointerId,evt.position);evt.StopPropagation();
            });
            world.RegisterCallback<PointerCancelEvent>(evt=>{
                if(worldPointers.Contains(evt.pointerId)){ReleaseWorldPointer(evt.pointerId);CancelDrag(evt.pointerId);}
            });
            world.RegisterCallback<PointerCaptureOutEvent>(evt=>{
                if(worldPointers.Remove(evt.pointerId))CancelDrag(evt.pointerId);
            });
            root.RegisterCallback<KeyDownEvent>(evt=>{if(evt.keyCode==KeyCode.Escape)CancelCardDrags();});
            DuelCartoonSkin.Apply(root);
            DuelCartoonSkin.SetMenuTab(root.Q<Button>("battleTab"), true);
            Update();
        }
        private void SetHudMenu(bool open)
        {
            hudMenuOpen=open;
            Display(root.Q("hudMenuPanel"),open);
        }
        private bool CanPlay()=>client.Session.CanAct&&(client.Session.State.phase=="playing"||client.Session.State.phase=="overtime");
        private bool CardDragging(string card)
        {
            foreach(var drag in activeDrags.Values)if(drag.Card==card)return true;
            return false;
        }
        private bool CanStartCard(string card)=>CanPlay()&&activeDrags.Count<MaxActiveDrags&&client.Session.InHand(card)&&!client.Session.CardPending(card)&&!CardDragging(card);
        private void SelectCard(string card)
        {
            if(!CanStartCard(card))return;
            client.Select(card);world.Focus();RefreshDeploymentGrid();
        }
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
        private VisualElement CreateCardPreview(string card,Button button)
        {
            var definition=client.Rules.Find(card);
            var preview=new VisualElement{pickingMode=PickingMode.Ignore};preview.AddToClassList("drag-card");
            var art=new VisualElement{pickingMode=PickingMode.Ignore};art.AddToClassList("card-art");
            art.style.backgroundImage=button.Q(className:"card-art").resolvedStyle.backgroundImage;preview.Add(art);
            if(DuelSpriteLibrary.Converted(card))
            {
                preview.AddToClassList("b3-card");var frame=CardFrame();DuelCartoonSkin.PaintCardFrame(frame);preview.Add(frame);
            }
            var title=new Label(definition.name){pickingMode=PickingMode.Ignore};title.AddToClassList("card-name");preview.Add(title);
            var cost=new Label(definition.cost.ToString("0")){pickingMode=PickingMode.Ignore};cost.AddToClassList("card-cost");DuelCartoonSkin.PaintBadge(cost);preview.Add(cost);
            root.Add(preview);return preview;
        }
        private bool StartDrag(int pointer,string card,Button button,Vector2 point,bool fromBattlefield=false)
        {
            if(activeDrags.ContainsKey(pointer)||!CanStartCard(card))return false;
            if(client.Selected&&client.SelectedCard==card)client.Selected=false;
            var size=button.worldBound.size;
            var preview=CreateCardPreview(card,button);
            preview.style.scale=new Scale(new Vector3(size.x/112,size.y/150,1));
            var drag=new DragState
            {
                Card=card,Match=client.Session.State.match,Button=button,CardPreview=preview,Model=client.Presentation.AcquirePreview(),
                Origin=point,Position=point,CardOrigin=fromBattlefield?point-size*.5f:button.worldBound.position,
                Opacity=fromBattlefield?0f:1f,LastAnimationTime=Time.unscaledTime
            };
            activeDrags.Add(pointer,drag);button.AddToClassList("dragging");
            MoveDrag(pointer,point);RefreshDeploymentGrid();return true;
        }
        private bool DropPoint(Vector2 point,out Vector3 worldPoint)
        {
            worldPoint=default;
            return root.worldBound.Contains(point)&&!root.Q("dockFrame").worldBound.Contains(point)
                &&!root.Q("hudMenuButton").worldBound.Contains(point)
                &&!(hudMenuOpen&&root.Q("hudMenuPanel").worldBound.Contains(point))&&WorldPoint(point,out worldPoint);
        }
        private bool DragOnBattlefield(Vector2 point)
        {
            // The card/placement visual mode depends on the pointer crossing the hand,
            // not on whether a valid deployment point can currently be resolved.
            return point.y<root.Q("dockFrame").worldBound.yMin;
        }
        private void MoveDrag(int pointer,Vector2 point)
        {
            if(!activeDrags.TryGetValue(pointer,out var drag))return;
            drag.Position=point;
            bool battlefield=DragOnBattlefield(point);
            float now=Time.unscaledTime;
            float delta=Mathf.Max(0f,now-drag.LastAnimationTime);
            drag.LastAnimationTime=now;
            drag.Opacity=Mathf.MoveTowards(drag.Opacity,battlefield?0f:1f,delta/DragCardFadeSeconds);
            var position=drag.CardOrigin+point-drag.Origin;
            drag.CardPreview.style.translate=new Translate(position.x,position.y);
            drag.CardPreview.style.opacity=drag.Opacity*DragCardMaxOpacity;
            Display(drag.CardPreview,drag.Opacity>.001f);

            if(battlefield)drag.HasPoint=DragPoint(drag.Card,point,out drag.PreviewPoint);
            bool visible=drag.HasPoint&&(battlefield||drag.Opacity<.999f);
            var state=client.Session.State;
            var definition=client.Rules.Find(drag.Card);
            if(state==null){drag.Model.Hide();return;}
            var aim=drag.PreviewPoint;
            client.Presentation.Preview(aim,visible,client.Session.CanDeploy(aim,drag.Card),drag.Card,state.seat,
                DuelRules.Formation(state.seat,aim,definition),definition.IsSpell?definition.radius:definition.BodyRadius,state,drag.Model);
        }
        private bool DragPoint(string card,Vector2 point,out Vector3 p)
        {
            p=default;
            if(!DropPoint(point,out var aim))return false;
            var state=client.Session.State;
            var definition=client.Rules.Find(card);
            if(state==null||definition==null)return false;
            return client.Map.TryNearestDeployment(state.seat,aim,state,definition,out p);
        }
        private void DropDrag(int pointer,Vector2 point)
        {
            if(!activeDrags.TryGetValue(pointer,out var drag))return;
            MoveDrag(pointer,point);
            if(CanPlay()&&client.Session.State.match==drag.Match&&DragOnBattlefield(point)&&drag.HasPoint)
                client.Session.Deploy(drag.PreviewPoint,drag.Card);
            CancelDrag(pointer);
        }
        private void CancelDrag(int pointer)
        {
            if(!activeDrags.TryGetValue(pointer,out var drag))return;
            activeDrags.Remove(pointer);
            drag.Button.RemoveFromClassList("dragging");drag.CardPreview.RemoveFromHierarchy();
            client.Presentation.ReleasePreview(drag.Model);
            RefreshDeploymentGrid();
        }
        private void CancelCardDrags()
        {
            client.Selected=false;
            foreach(var drag in cardDrags)drag.Cancel();
            foreach(int pointer in new List<int>(worldPointers))ReleaseWorldPointer(pointer);
            foreach(int pointer in new List<int>(activeDrags.Keys))CancelDrag(pointer);
            RefreshDeploymentGrid();
        }
        private void RefreshDeploymentGrid()
        {
            var grid=client.Presentation.DeploymentGrid;
            var state=client.Session.State;
            if(state==null||!CanPlay()){grid.Hide();return;}
            // One shared grid avoids stacked translucent overlays. Each finger still
            // computes legality and renders its own footprint using its own card.
            DragState focus=null;
            foreach(var drag in activeDrags.Values)
            {
                if(focus==null||DragOnBattlefield(drag.Position))focus=drag;
            }
            if(focus!=null)
            {
                grid.Show(state,client.Rules.Find(focus.Card));
                grid.Highlight(DuelPresentation.MirrorDepth(focus.PreviewPoint,state.seat),focus.HasPoint&&DragOnBattlefield(focus.Position));
            }
            else if(client.Selected)
            {
                grid.Show(state,client.Rules.Find(client.SelectedCard));grid.Highlight(Vector3.zero,false);
            }
            else grid.Hide();
        }
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
        private void ReleaseWorldPointer(int pointer){worldPointers.Remove(pointer);if(world.HasPointerCapture(pointer))world.ReleasePointer(pointer);}
        private void Text(string name,string value){var label=root.Q<Label>(name);if(label.text!=value)label.text=value;}
        private static void Display(VisualElement e,bool show)=>e.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        private void LayoutViewport()
        {
            if(dock==null||root.resolvedStyle.width<=0||root.resolvedStyle.height<=0)return;
            float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
            if(float.IsNaN(width)||float.IsNaN(height))return;
            appliedSafeArea=Screen.safeArea;
            layoutScreenWidth=Screen.width;
            layoutScreenHeight=Screen.height;
            // UI Toolkit scales with the screen (DuelPanel reference 720x1000).
            // Convert physical pixels into panel units to keep HUD *rendered* size fixed.
            float unitsPerPixel=height/Mathf.Max(1,Screen.height);
            menu?.Layout(appliedSafeArea,unitsPerPixel);
            float safeWidth=appliedSafeArea.width*unitsPerPixel;
            float availableWidth=Mathf.Max(1f,safeWidth-16f);
            // Hand occupies only the reserved 14.5% lower edge.
            const float dockFraction=.145f;
            float scale=Mathf.Min(.9f,availableWidth/600f,height*dockFraction/220f);
            var frame=root.Q("dockFrame");
            frame.style.left=appliedSafeArea.center.x*unitsPerPixel-300f*scale;
            frame.style.width=600f*scale;
            frame.style.height=height*dockFraction;
            frame.style.bottom=appliedSafeArea.yMin*unitsPerPixel;
            dock.style.left=300f*scale-300f;
            dock.style.bottom=height*dockFraction-220f*scale;
            dock.style.scale=new Scale(new Vector3(scale,scale,1));

            // Fixed physical-pixel header: centered timer, top-right menu.
            const float edgeInset=8f, timerWidth=100f;
            var top=root.Q("topHud");
            top.style.top=(7f+Screen.height-appliedSafeArea.yMax)*unitsPerPixel;
            top.style.left=(edgeInset+appliedSafeArea.xMin)*unitsPerPixel;
            top.style.right=(edgeInset+Screen.width-appliedSafeArea.xMax)*unitsPerPixel;
            top.style.height=44f*unitsPerPixel;
            var timer=top.Q("timerLabel");
            timer.style.left=Mathf.Max(0,(safeWidth-(2f*edgeInset+timerWidth)*unitsPerPixel)/2f);
            var pixelScale=new Scale(new Vector3(unitsPerPixel,unitsPerPixel,1f));
            timer.style.scale=pixelScale;
            top.Q("hudActions").style.scale=pixelScale;
        }
        public void Update()
        {
            if(appliedSafeArea!=Screen.safeArea||
                layoutScreenWidth!=Screen.width||layoutScreenHeight!=Screen.height)
                LayoutViewport();
            var state=client.Session.State;
            if(state!=null&&wasLobby){BlurInputs();menu.ClosePreview();}wasLobby=state==null;
            if(!CanPlay())CancelCardDrags();
            else
            {
                // Match changes, hand replacement and capture loss affect only their own gestures.
                foreach(int pointer in new List<int>(activeDrags.Keys))
                {
                    var drag=activeDrags[pointer];
                    if(drag.Match!=state.match||!client.Session.InHand(drag.Card)||client.Session.CardPending(drag.Card))
                    {if(worldPointers.Contains(pointer))ReleaseWorldPointer(pointer);CancelDrag(pointer);}
                    else MoveDrag(pointer,drag.Position);
                }
                RefreshDeploymentGrid();
            }
            if(copyRoom!=""&&(state?.room!=copyRoom||(state.phase!="waiting"&&state.phase!="countdown")))ResetCopy();
            if(copyFeedbackUntil>0&&DuelSession.Now>=copyFeedbackUntil){copyFeedbackUntil=0;copy.text="複製房間碼";}
            bool playing=state!=null&&(state.phase=="playing"||state.phase=="overtime");
            Display(lobby,state==null);Display(waiting,state!=null&&(state.phase=="waiting"||state.phase=="countdown"));Display(result,state!=null&&state.phase=="ended");Display(dock,playing);Display(health,state!=null);
            if(state==null)menu.Update();
            if(!playing&&hudMenuOpen)SetHudMenu(false);
            Display(root.Q("topHud"),playing);Display(root.Q("dockFrame"),playing);
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
                    Text("noticeLabel",client.Session.Recovering?"正在恢復連線，對戰持續進行…":!opponent.connected&&opponent.reconnectRemaining>0?"等待對手返回　"+Mathf.CeilToInt(opponent.reconnectRemaining)+" 秒":client.Session.Pending?"正在確認出牌…":"");
                    Text("nextCardLabel","下一張");
                    root.Q("nextCardArt").tooltip=client.Rules.Find(state.nextCard).name;
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
                        var card=cards[definition.id];
                        bool inHand=client.Session.InHand(definition.id);Display(card,inHand);card.SetEnabled(inHand&&CanPlay()&&!client.Session.CardPending(definition.id));
                        card.EnableInClassList("selected",client.Selected&&client.SelectedCard==definition.id);card.EnableInClassList("unaffordable",client.Session.AvailableEnergy<definition.cost);
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
            Text("energyLabel",displayedEnergy.ToString("0.0"));
            Text("energyMode",state.phase=="overtime"?"×2":"");
            Display(root.Q("energyMode"),state.phase=="overtime");
        }
        private void UpdateHealth(DuelMessage state)
        {
            var seen=new HashSet<long>();
            // Match the arena's height-based framing, independent of window width.
            float healthScale=root.resolvedStyle.height/1000f;
            float combatScale=TowerDuelClient.VisualScaleForSize(client.Camera.orthographicSize);
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
                healthLabels[e.id].text=unit?"":e.hp.ToString();
                Display(healthLabels[e.id],!unit);
                if(e.kind=="keep")bar.Q<Label>(className:"keep-status").text=e.active?"主堡啟動":"主堡待命";
                else if(unit)
                {
                    float deployMilliseconds=Mathf.Clamp((float)(e.readyTick-client.Session.RenderTick)*1000/state.tickRate,0,client.Rules.Find(e.kind).deployTicks*1000f/state.tickRate);
                    bar.Q<Label>(className:"unit-status").text=e.IsDeploying(client.Session.RenderTick)?"部署 "+Mathf.CeilToInt(deployMilliseconds)+"ms":client.Session.SampleSlowed(e)?"減速":e.charging?"衝鋒":"";
                }
                var worldPos=DuelPresentation.MirrorDepth(unit?client.Session.Sample(e):e.Position,state.seat);
                var bodyCenter=worldPos+Vector3.up*(unit?client.Presentation.UnitHeight(e.kind)*.5f:1f);
                worldPos.y+=(e.kind=="keep"?TowerDuelClient.KeepHealthHeight:e.kind=="tower"?(client.Map.HasWall?7.4f:3.9f):client.Presentation.UnitHeight(e.kind))*combatScale+.22f;
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
        public void Dispose(){client.Session.StateChanged-=ObserveEnergy;client.Session.ActionConfirmed-=ObserveActionEnergy;CancelCardDrags();ResetCopy();menu.Dispose();UnityEngine.Object.Destroy(document);UnityEngine.Object.Destroy(panel);}
    }
}
