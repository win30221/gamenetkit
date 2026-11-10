using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleHud:MonoBehaviour
    {
        private static BattleHud instance;
        private BattleClient client;
        private BattleFiles files;
        private GameFlow Flow=>client.Flow;
        private UIDocument document;
        private PanelSettings panel;
        private VisualElement root,hudLayer,screenLayer,modalLayer,dock,result,healthLayer,scoreRow,hpFill;
        private ScrollView scroll;
        private Label timer,objective,hpNumber,heroLabel,life,attackName,attackTime,dashTime,connection,performance,basicTime;
        private readonly Dictionary<string,VisualElement> health=new Dictionary<string,VisualElement>();
        private string signature,context,scoreSignature,replayLocation="",technicalNotice="";
        private Label technicalStatus;
        private bool technicalReconnectPending;
        private bool dirty=true,screenBlocks=true,takeover,showStats,recordingOpen,replayOpen,replayAdvanced;
        private BattleSettings selection;
        private double nextRefresh,nextScale,statsAt;
        private int statsFrames;
        private float fps;
        private VisualElement replayPanel,replayProgress;
        private Button replayToggle,replayBackward,replayForward;
        private Slider replaySeek;
        private Label replayTime;
        private const string PerformanceKey="Rumblebound.Example.ShowPerformance";
        public static bool EditingText=>instance?.FocusedText()!=null;
        public static bool PointerOverPanel=>instance!=null&&instance.PointerIsOnUI(Input.mousePosition);
        public static Rect ArenaPixels{
            get{
                return Screen.safeArea;
            }
        }
        public static bool BlocksGameplay=>instance!=null&&(instance.screenBlocks||instance.navigation.IsOpen||EditingText||
            (instance.root?.focusController?.focusedElement is Button)&&(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Return)));
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern float RF_Battle_UIPixelRatio();
#endif
        private void Awake(){instance=this;client=GetComponent<BattleClient>();files=GetComponent<BattleFiles>();}
        private void Start()
        {
            panel=Instantiate(Resources.Load<PanelSettings>("UI/GamePanelSettings"));
            if(panel==null)throw new InvalidOperationException("Missing original game UI panel settings");
            panel.name="Rumblebound responsive UI";panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("UI/GameTheme");panel.sortingOrder=10;
            document=gameObject.AddComponent<UIDocument>();document.panelSettings=panel;
            root=document.rootVisualElement;root.name="rumbleboundUI";root.AddToClassList("game-ui");root.focusable=true;root.tabIndex=-1;root.pickingMode=PickingMode.Ignore;
            hudLayer=Box("hud-layer");hudLayer.pickingMode=PickingMode.Ignore;
            screenLayer=Box("screen-layer");modalLayer=Box("modal-layer");modalLayer.pickingMode=PickingMode.Ignore;
            root.Add(hudLayer);root.Add(screenLayer);root.Add(modalLayer);
            scroll=new ScrollView(ScrollViewMode.Vertical){horizontalScrollerVisibility=ScrollerVisibility.Hidden};scroll.AddToClassList("menu-scroll");
            navigationHeader=Box("session-header-dock");screenLayer.Add(navigationHeader);screenLayer.Add(scroll);dock=Box("menu-dock");screenLayer.Add(dock);
            BuildHUD();BuildTouchControls();result=Box("result-backdrop");modalLayer.Add(result);showStats=PlayerPrefs.GetInt(PerformanceKey,0)==1;
            root.RegisterCallback<KeyDownEvent>(evt=>{
                if(evt.keyCode!=KeyCode.Escape||Flow?.Battle.HasResult==true)return;
                BackMenu();evt.StopPropagation();
            });
            Scale();root.Focus();
        }
        private static void Classes(VisualElement element,string classes){foreach(string name in (classes??"").Split(' '))if(name.Length>0)element.AddToClassList(name);}
        private static VisualElement Box(string classes=null){var element=new VisualElement();Classes(element,classes);return element;}
        private static Label Text(string value,string classes=null){var label=new Label(value);Classes(label,classes);return label;}
        private Button Button(string text,Action action,string classes=null,bool enabled=true)
        {
            var button=new Button(()=>{action();MarkDirty();}){text=text,name=text};button.AddToClassList("button");Classes(button,classes);button.SetEnabled(enabled);return button;
        }
        private Button Choice(string text,bool selected,Action action)
        {
            Button choice=null;choice=Button(text,()=>{action();foreach(var sibling in choice.parent.Children())if(sibling is Button other)other.EnableInClassList("secondary",other!=choice);},(selected?"":"secondary ")+"small-button grow");return choice;
        }
        private static TextField Field(string label,string value,Action<string> changed,int maxLength=0)
        {
            var field=new TextField(label){value=value??"",name=label};field.AddToClassList("input");if(maxLength>0)field.maxLength=maxLength;field.RegisterValueChangedCallback(evt=>changed(evt.newValue));return field;
        }
        private static VisualElement Row(params VisualElement[] children){var row=Box("row");foreach(var child in children)row.Add(child);return row;}
        private void MarkDirty(){dirty=true;}
        private static string HeroName(string id)=>id=="tank"?"騎士":id=="mage"?"魔法師":id=="ranger"?"弓箭手":"未選擇";
        private static Color TeamColor(string id)=>BattlePresentation.TeamColor(id);
        private TextField FocusedText(){var focused=root?.focusController?.focusedElement as VisualElement;return focused as TextField??focused?.GetFirstAncestorOfType<TextField>();}
        private bool PointerIsOnUI(Vector2 pointer)
        {
            if(root?.panel==null)return false;
            var picked=root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(pointer.x,Screen.height-pointer.y)));
            return picked!=null&&picked!=root&&picked!=hudLayer&&picked!=modalLayer&&picked!=healthLayer;
        }
        private void Scale()
        {
            float ratio=1;
#if UNITY_WEBGL && !UNITY_EDITOR
            ratio=Mathf.Max(1,RF_Battle_UIPixelRatio());
#endif
            panel.scale=ratio*Mathf.Clamp(Mathf.Min(Screen.width/ratio/1280f,Screen.height/ratio/720f),1,1.6f);
            root.EnableInClassList("compact",Screen.width/panel.scale<760);root.EnableInClassList("short-screen",Screen.height/panel.scale<600);
            root.EnableInClassList("narrow-replay",Screen.width/panel.scale<680);
            root.EnableInClassList("battle-short",Screen.height/panel.scale<600);
            root.EnableInClassList("battle-narrow",Screen.width/panel.scale<600);
            float rail=TouchMode?170:210;
            root.EnableInClassList("slim-rails",false);
            var healthCard=hudLayer?.Q<VisualElement>(className:"health-card");if(healthCard!=null)healthCard.style.width=Mathf.Min(230,rail);
            var safe=Screen.safeArea;root.style.paddingLeft=safe.xMin/panel.scale;root.style.paddingRight=(Screen.width-safe.xMax)/panel.scale;root.style.paddingBottom=safe.yMin/panel.scale;root.style.paddingTop=(Screen.height-safe.yMax)/panel.scale;
        }
        private string MenuSignature()
        {
            var f=Flow;var room=f.Room;
            return f.Stage+"|"+f.Battle.State.Latest?.match?.phase+"|"+f.Identity.Revision+"|"+f.Busy+"|"+f.Notice+"|"+navigation.Page+"|"+navigation.ConfirmDiscard+"|"+resumeAfterTechnicalApply+"|"+f.Recovery.Pending+"|"+f.Battle.Connected+"|"+f.Battle.CanControl+"|"+files.Loading+"|"+files.Notice+"|"+
                room?.code+room?.phase+room?.started+room?.canStart+room?.botMode+"|"+string.Join(";",room?.members?.Select(m=>m.userID+m.teamID+m.heroID+m.ready)??Array.Empty<string>())+"|"+
                string.Join(";",room?.teams?.Select(t=>t.teamID+t.capacity)??Array.Empty<string>());
        }
        private void LateUpdate()
        {
            if(root==null||Flow==null)return;double now=client.Now;statsFrames++;RefreshTechnicalFeedback();
            if(now-statsAt>=.5){fps=(float)(statsFrames/Math.Max(.001,now-statsAt));statsFrames=0;statsAt=now;}
            if(now>=nextScale){nextScale=now+.25;Scale();}
            if(Input.GetMouseButtonDown(0)&&!PointerOverPanel)root.Focus();
            if(now>=nextRefresh||dirty){nextRefresh=now+.1;string next=MenuSignature();if(dirty||signature!=next){signature=next;dirty=false;BuildMenus();}RefreshHUD();}
            UpdateMiniMap();PositionHealth();UpdateReplay();RefreshTouchControls();
        }
        private void BuildMenus()
        {
            var f=Flow;bool playback=f.Playback.Active,terminal=f.Battle.HasResult;
            if((terminal||playback||navigation.InGame&&!InGameContext)&&navigation.IsOpen)CloseMenu();
            if(InGameContext&&navigation.IsOpen&&!navigation.InGame)navigation.Open(navigation.Page,true);
            bool visible=!playback&&!terminal&&(f.Stage!="battle"||navigation.IsOpen||f.Battle.State.Latest==null||f.Battle.State.Latest.match?.phase=="loading");
            screenBlocks=visible;
            screenLayer.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;result.style.display=terminal&&!playback?DisplayStyle.Flex:DisplayStyle.None;
            if(terminal&&!playback){result.Clear();var content=Box("panel result-panel");result.Add(content);BuildResult(content,content);}
            root.EnableInClassList("in-game-menu",navigation.InGame);navigationHeader.Clear();navigationHeader.style.display=navigation.InGame?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible){RefreshNavigationPrompt();if(!navigation.IsOpen)root.Focus();return;}
            string next=settingsOpen?"settings":advanced?"advanced":menuOpen&&f.Stage=="battle"?"pause":f.Stage+(f.Room?.code??"")+lobbyPractice;
            if(navigation.InGame&&context!=null)menuScroll[context]=scroll.scrollOffset;
            bool same=context==next;Vector2 offset=same?scroll.scrollOffset:navigation.InGame&&menuScroll.TryGetValue(next,out var savedOffset)?savedOffset:Vector2.zero;
            var focused=FocusedText();string name=same?(focused as VisualElement??root.focusController?.focusedElement as VisualElement)?.name:null;
            int cursor=focused?.cursorIndex??0,selectionIndex=focused?.selectIndex??0;context=next;
            root.EnableInClassList("room-screen",f.Identity.IsAuthenticated||settingsOpen||advanced);
            scroll.Clear();dock.Clear();var frame=Box("menu-frame");frame.style.minHeight=Screen.safeArea.height/panel.scale;scroll.Add(frame);
            var hero=Box("hero");hero.Add(Text("RUMBLEBOUND","hero-title"));hero.Add(Text("集結，準備開戰。","hero-subtitle"));hero.Add(Text("自由組隊 · 爭奪據點 · 三分鐘決勝","hero-copy"));frame.Add(hero);
            var contentPanel=Box("panel main-panel");frame.Add(contentPanel);
            if(navigation.InGame)BuildSessionNavigation();
            if(!navigation.InGame&&!settingsOpen&&!advanced){var toolbar=Box("menu-toolbar");var button=Button("設定",()=>OpenMenuPage(BattleMenuPage.Settings),"secondary small-button");button.name="settingsButton";toolbar.Add(button);contentPanel.Add(toolbar);}
            if(settingsOpen)BuildSettings(contentPanel);else if(advanced)BuildAdvanced(contentPanel);else if(menuOpen&&f.Stage=="battle")BuildPause(contentPanel);
            else if(f.Content==null){contentPanel.Add(Text("正在連接競技場…","title"));contentPanel.Add(Button("重新載入",()=>f.ReloadContent(),enabled:!f.Busy));}
            else if(!f.Identity.IsAuthenticated)BuildLogin(contentPanel,dock);else if(f.Room==null)BuildLobby(contentPanel,dock);else BuildRoom(contentPanel,dock);
            if(!string.IsNullOrEmpty(f.Notice)&&settingsOpen)contentPanel.Add(Text(Message(f.Notice),"status"));
            if(f.Room==null&&!settingsOpen&&!advanced)BuildReplayLauncher(contentPanel);
            if(navigation.InGame){BuildSessionFooter();contentPanel.SetEnabled(!resumeAfterTechnicalApply);}
            dock.style.display=dock.childCount>0?DisplayStyle.Flex:DisplayStyle.None;if(dock.childCount>0)frame.style.minHeight=0;
            frame.RegisterCallbackOnce<GeometryChangedEvent>(evt=>{if(frame.panel==null)return;var target=navigation.ConfirmDiscard?navigationPrompt?.Q<Button>("keepEditingButton"):string.IsNullOrEmpty(name)?null:screenLayer.Q<VisualElement>(name);if(target==null&&!same&&navigation.InGame)target=navigationHeader.Q<Button>("menuTab"+navigation.Page);if(target!=null&&target.enabledInHierarchy){target.Focus();if(target is TextField field)field.SelectRange(cursor,selectionIndex);}else root.Focus();scroll.scrollOffset=new Vector2(0,Mathf.Clamp(offset.y,0,Mathf.Max(0,scroll.verticalScroller.highValue)));});
            RefreshNavigationPrompt();
        }
        private void BuildPause(VisualElement parent)
        {
            if(Flow.Room?.practice==true){parent.Add(Text("切換對手行為後，可直接按下方繼續練習。","muted"));BuildPracticeControls(parent,true);parent.Add(Button("離開練習",()=>{CloseMenu();Flow.Leave();},"danger",!Flow.Busy));}
            else{parent.Add(Text("比賽會繼續進行。關閉選單即可恢復操作。","muted"));parent.Add(Text("右鍵移動／追擊　A＋左鍵移動攻擊\nQ／E 按住預覽、放開施放　S 停止","instructions"));parent.Add(Button(Flow.Battle.Connected?"中斷連線":"重新連線",()=>{if(Flow.Battle.Connected)Flow.StopBattle();else Flow.Enter(takeover);},"secondary",!Flow.Busy));}BuildRecording(parent);
        }
        private Toggle Toggle(string label,bool value,Action<bool> changed){var toggle=new Toggle{text=label,value=value,name=label};toggle.RegisterValueChangedCallback(evt=>changed(evt.newValue));return toggle;}
        private void BuildSettings(VisualElement parent)
        {
            if(!navigation.InGame)parent.Add(Button(Flow.Room!=null?"返回房間":Flow.Identity.IsAuthenticated?"返回遊戲大廳":"返回登入",BackMenu,"secondary small-button"));parent.Add(Text("設定","title"));parent.Add(Text("效能資訊","subtitle"));
            parent.Add(Toggle("顯示 FPS 與延遲",showStats,value=>{showStats=value;PlayerPrefs.SetInt(PerformanceKey,value?1:0);PlayerPrefs.Save();}));
            var zoom=new Slider("鏡頭距離",5,10){value=client.Presentation.CameraZoom,name="cameraZoom"};zoom.RegisterValueChangedCallback(evt=>client.Presentation.SetCameraZoom(evt.newValue));parent.Add(zoom);parent.Add(Text("向左拉近，向右拉遠。","hint"));
            var audio=client.Presentation.Audio;if(audio!=null){var group=Box("feedback-settings");group.Add(Text("音效","subtitle"));group.Add(Toggle("靜音",audio.Muted,audio.SetMuted));var volume=new Slider("音量",0,1){value=audio.Volume};volume.RegisterValueChangedCallback(evt=>audio.SetVolume(evt.newValue));group.Add(volume);parent.Add(group);}
            if(!navigation.InGame){parent.Add(Button("框架技術選項",()=>OpenMenuPage(BattleMenuPage.Technical),"secondary"));BuildRecording(parent);}if(Flow.Identity.IsAuthenticated&&Flow.Room==null)parent.Add(Button("切換身份／重新登入",()=>{CloseMenu();Flow.Logout();},"danger small-button",!Flow.Busy));
        }
        private void TechnicalChanged(bool refresh=false)
        {
            technicalNotice=HasTechnicalChanges?"設定已變更，尚未套用。":"目前設定已套用。";if(technicalStatus!=null)technicalStatus.text=technicalNotice;if(resumeButton!=null)resumeButton.text=ResumeButtonText;if(applyOnlyButton!=null)applyOnlyButton.SetEnabled(!MenuWorking&&HasTechnicalChanges);if(refresh)MarkDirty();
        }
        private void Option(VisualElement parent,string label,string value,IEnumerable<string> values,Action<string> changed,bool enabled=true)
        {
            var options=values.ToList();if(options.Count==0)return;
            var row=Box("technical-option");row.Add(Text(label,"technical-label"));var choices=Box("technical-choices");
            foreach(string option in options){var choice=Button(OptionName(option),()=>{changed(option);TechnicalChanged();},"technical-choice"+(option==value?" selected":""),enabled&&options.Count>1);choice.name=label+":"+option;choices.Add(choice);}
            row.Add(choices);parent.Add(row);
        }
        private void TechnicalSlider(VisualElement parent,string label,double value,float min,float max,Func<float,string> format,Action<float> changed,bool enabled=true)
        {
            var group=Box("technical-slider");var header=Box("technical-slider-header");header.Add(Text(label,"technical-label"));var number=Text(format((float)value),"technical-value");header.Add(number);group.Add(header);
            var slider=new Slider(min,max){value=(float)value,name=label,tooltip=label};slider.AddToClassList("technical-track");slider.SetEnabled(enabled);
            slider.RegisterValueChangedCallback(evt=>{changed(evt.newValue);number.text=format(evt.newValue);TechnicalChanged();});group.Add(slider);parent.Add(group);
        }
        private void ApplyTechnicalOptions(bool closeAfter=false)
        {
            var f=Flow;if(MenuWorking)return;
            bool changed=HasTechnicalChanges;
            if(!changed&&(!navigation.InGame||f.Battle.CanControl)){if(closeAfter){if(navigation.InGame)ResumeGame();else CloseMenu();}else technicalNotice="目前設定已套用。";return;}
            var candidate=(selection??f.Selection).Copy();if(candidate.Prediction=="authority")candidate.Correction="snap";
            try{candidate.Validate();}catch(InvalidOperationException){technicalNotice="無法套用，請確認所選組件與設定相容。";navigation.Select(BattleMenuPage.Technical);return;}
            var previous=f.Selection;f.Selection=candidate;bool reconnect=f.Room?.started==true;
            if(reconnect&&!f.Enter(takeover)){f.Selection=previous;technicalNotice="目前無法重新連線，設定尚未套用，請稍後重試。";return;}
            navigation.KeepEditing();resumeAfterTechnicalApply=closeAfter;technicalReconnectPending=reconnect;selection=candidate.Copy();technicalNotice=reconnect?"設定已套用，正在重新連線…":"設定已套用，下次進入對戰時生效。";
            Debug.Log("RF_BATTLE_SETTINGS APPLIED "+JsonUtility.ToJson(candidate));
            if(!reconnect&&closeAfter)CloseMenu();
        }
        private void RefreshTechnicalFeedback()
        {
            if(!technicalReconnectPending)return;var f=Flow;
            if(f.Battle.Connected&&(!resumeAfterTechnicalApply||f.Battle.CanControl)){technicalReconnectPending=false;if(!HasTechnicalChanges)technicalNotice="設定已套用，已重新連線。";MarkDirty();Debug.Log("RF_BATTLE_SETTINGS RECONNECTED generation="+f.Battle.State.Generation);if(resumeAfterTechnicalApply)ResumeGame();}
            else if(!f.Busy&&!f.Battle.Connecting&&!f.Recovery.Pending&&(f.Room==null||!string.IsNullOrEmpty(f.Notice))){technicalReconnectPending=false;resumeAfterTechnicalApply=false;technicalNotice=f.Room==null?"設定已套用，下次進入對戰時生效。":"設定已儲存，但重新連線失敗，請查看錯誤提示。";MarkDirty();}
        }
        private static string OptionName(string value)
        {
            switch(value){case "authority":return "等待權威";case "frame":return "逐幀預測";case "fixed-step":return "固定步長預測";case "snap":return "直接校正";case "exponential":return "平滑校正";case "latest":return "關閉平滑";case "fixed":return "固定平滑";case "adaptive":return "自適應平滑";case "snapshot":return "快照時間";case "disciplined":return "往返校時";case "all":return "全部物件";case "radius":return "附近物件";case "json":return "JSON";case "delta":return "差分";case "websocket":return "WebSocket";case "webtransport":return "WebTransport";default:return value;}
        }
        private void BuildAdvanced(VisualElement parent)
        {
            var f=Flow;selection=selection??f.Selection.Copy();if(!navigation.InGame)parent.Add(Button("返回設定",BackMenu,"secondary small-button"));parent.Add(Text("框架技術選項","title"));parent.Add(Text("直接點選選項；金色外框表示目前選擇。修改後按下方套用。","hint"));
            Option(parent,"自己的移動：預測",selection.Prediction,BattleNetcode.Predictions.Keys,value=>{selection.Prediction=value;if(!BattleNetcode.Predictions[value].Enabled)selection.Correction="snap";});Option(parent,"自己的移動：校正",selection.Correction,BattleNetcode.Corrections.Keys,value=>selection.Correction=value,BattleNetcode.Predictions[selection.Prediction].Enabled);Option(parent,"其他玩家：平滑",selection.Timeline,BattleNetcode.Timelines.Modes,value=>selection.Timeline=value);
            if(!BattleNetcode.Predictions[selection.Prediction].Enabled)parent.Add(Text("等待權威時使用直接校正；開啟預測後可選平滑校正。","hint"));
            Option(parent,"伺服器時鐘",selection.Clock,BattleNetcode.ClockFactory==null?new[]{"snapshot"}:new[]{"snapshot","disciplined"},value=>selection.Clock=value);
            if(Realtime.Transport.MessageTransport.SupportsWebTransport&&!string.IsNullOrEmpty(f.Content?.Config.webTransportURL))Option(parent,"傳輸",selection.Transport,new[]{"websocket","webtransport"},value=>selection.Transport=value);
            Option(parent,"快照格式",selection.Server.codec,BattleNetcode.DecoderFactory==null?new[]{"json"}:new[]{"json","delta"},value=>selection.Server.codec=value);Option(parent,"同步範圍",selection.Server.interest,new[]{"all","radius"},value=>selection.Server.interest=value);
            TechnicalSlider(parent,"可見半徑",selection.Server.radius,4,40,value=>value.ToString("0.0"),value=>selection.Server.radius=value,selection.Server.interest=="radius");
            if(selection.Server.interest!="radius")parent.Add(Text("選擇「附近物件」後可調整可見半徑。","hint"));
            parent.Add(Toggle("位置量化",selection.Server.quantize,value=>{selection.Server.quantize=value;TechnicalChanged();}));parent.Add(Toggle("歷史狀態",selection.Server.history,value=>{selection.Server.history=value;if(!value)selection.Server.lag="off";TechnicalChanged(true);}));parent.Add(Toggle("近戰延遲補償",selection.Server.lag=="melee",value=>{selection.Server.lag=value?"melee":"off";if(value)selection.Server.history=true;TechnicalChanged(true);}));
            if(BattleNetcode.ActionsFactory!=null)parent.Add(Toggle("動作預先呈現",selection.SpeculativeActions,value=>{selection.SpeculativeActions=value;TechnicalChanged();}));
            if(BattleNetcode.ConditionerFactory!=null){
                parent.Add(Text("弱網模擬","subtitle"));parent.Add(Text("上行為送往伺服器，下行為收到的訊息；抖動會在延遲上加減。","hint"));
                TechnicalSlider(parent,"上行延遲",selection.SendDelaySeconds,0,.3f,value=>(value*1000).ToString("0")+" ms",value=>selection.SendDelaySeconds=Math.Min(.3,value));
                TechnicalSlider(parent,"上行抖動",selection.SendJitterSeconds,0,.3f,value=>"± "+(value*1000).ToString("0")+" ms",value=>selection.SendJitterSeconds=Math.Min(.3,value));
                TechnicalSlider(parent,"上行丟包",selection.SendLossProbability,0,1,value=>(value*100).ToString("0.0")+" %",value=>selection.SendLossProbability=value);
                TechnicalSlider(parent,"下行延遲",selection.DelaySeconds,0,.3f,value=>(value*1000).ToString("0")+" ms",value=>selection.DelaySeconds=Math.Min(.3,value));
                TechnicalSlider(parent,"下行抖動",selection.ReceiveJitterSeconds,0,.3f,value=>"± "+(value*1000).ToString("0")+" ms",value=>selection.ReceiveJitterSeconds=Math.Min(.3,value));
                TechnicalSlider(parent,"下行丟包",selection.ReceiveLossProbability,0,1,value=>(value*100).ToString("0.0")+" %",value=>selection.ReceiveLossProbability=value);
            }
            parent.Add(Text("即時診斷","subtitle"));parent.Add(Text("下列開關立即生效。","hint"));
            parent.Add(Toggle("自動重連",f.AutoReconnect,value=>f.AutoReconnect=value));parent.Add(Toggle("自動往返移動",client.AutomaticDrive,value=>client.AutomaticDrive=value));parent.Add(Toggle("顯示權威位置外框",client.Outlines,value=>client.Outlines=value));parent.Add(Toggle("接管自己的舊連線",takeover,value=>takeover=value));
            parent.Add(Button("中斷網路測試重連",()=>f.Battle.Transport.InterruptForDiagnostics(),"secondary",f.Battle.Connected));parent.Add(Button("清除解碼基準",()=>f.Battle.ForgetBaseline(),"secondary",f.Battle.Connected));parent.Add(Text("RTT "+(f.Battle.RTTSeconds*1000).ToString("0")+"ms · ACK "+f.Battle.FrameAck+" · 缺基準 "+f.Battle.MissingBaselines,"hint"));
            if(!navigation.InGame){var footer=Box("footer technical-footer");technicalStatus=Text(string.IsNullOrEmpty(technicalNotice)?"修改後請套用。":technicalNotice,"hint technical-status");footer.Add(technicalStatus);if(!string.IsNullOrEmpty(f.Notice))footer.Add(Text(Message(f.Notice),"status"));applyOnlyButton=Button("套用設定",()=>ApplyTechnicalOptions(),enabled:!MenuWorking&&HasTechnicalChanges);footer.Add(applyOnlyButton);dock.Add(footer);}
        }
        private void BuildRecording(VisualElement parent)
        {
            var f=Flow;var fold=new Foldout{text="錄製與離線播放",value=recordingOpen};fold.RegisterValueChangedCallback(evt=>{if(evt.target==fold)recordingOpen=evt.newValue;});fold.Add(Button(f.Recording.Recording?"停止錄製":"開始錄製",()=>{if(f.Recording.Recording)f.Recording.Stop();else f.StartRecording();},"secondary",f.Battle.Connected));fold.Add(Text(f.Recording.Frames+" 張快照","hint"));fold.Add(Button("匯出錄製檔",files.Export,"secondary",f.Recording.Frames>0));if(!string.IsNullOrEmpty(files.Notice))fold.Add(Text(files.Notice,"status"));parent.Add(fold);
        }
        private void BuildReplayLauncher(VisualElement parent)
        {
            var fold=new Foldout{text="對戰重播",value=replayOpen};fold.RegisterValueChangedCallback(evt=>{if(evt.target==fold)replayOpen=evt.newValue;});
            fold.Add(Text("選擇下載的 .playback.json，直接觀看對戰。","hint"));
            if(files.PickerSupported){var pick=Button(files.Loading?"正在載入…":"選擇重播檔案",files.SelectReplay,enabled:files.CanLoad&&!files.Loading);pick.name="selectReplayFile";fold.Add(pick);}
            var source=new Foldout{text="進階：網址或路徑",value=replayAdvanced};source.RegisterValueChangedCallback(evt=>{if(evt.target==source)replayAdvanced=evt.newValue;});source.Add(Field("檔案路徑／網址",replayLocation,value=>replayLocation=value));source.Add(Button("載入離線重播",()=>files.Load(replayLocation),"secondary",files.CanLoad&&!files.Loading));fold.Add(source);
            if(Flow.Recording.Frames>0)fold.Add(Button("匯出錄製檔",files.Export,"secondary"));if(BattleNetcode.PlaybackFactory==null)fold.Add(Text("未安裝播放組件，仍可錄製與匯出。","hint"));if(!string.IsNullOrEmpty(files.Notice))fold.Add(Text(files.Notice,"status"));parent.Add(fold);
        }
        private static void Ignore(VisualElement element){element.pickingMode=PickingMode.Ignore;foreach(var child in element.Children())Ignore(child);}
        private static VisualElement Ability(string title,string key,out Label cooldown){var card=Box("ability-card");card.Add(Text(title,"ability-name"));cooldown=Text("就緒","ability-time");card.Add(cooldown);card.Add(Text(key,"ability-key"));return card;}
        private void BuildHUD()
        {
            var top=Box("hud-top");timer=Text("03:00","timer");scoreRow=Box("scores");objective=Text("爭奪據點","objective");top.Add(timer);top.Add(scoreRow);top.Add(objective);Ignore(top);hudLayer.Add(top);var menu=Button("選單",()=>OpenMenuPage(BattleMenuPage.Session),"secondary small-button hud-menu");menu.focusable=false;menu.tabIndex=-1;hudLayer.Add(menu);
            var bottom=Box("hud-bottom");var card=Box("health-card");heroLabel=Text("生命值","health-title");hpNumber=Text("100 / 100","health-number");var track=Box("hp-track");hpFill=Box("hp-fill");track.Add(hpFill);life=Text("","hint");card.Add(heroLabel);card.Add(hpNumber);card.Add(track);card.Add(life);bottom.Add(card);var abilities=Box("ability-row");abilities.Add(Ability("普攻","右鍵／A",out basicTime));var attack=Ability("技能","Q",out attackTime);attackName=attack.Q<Label>(className:"ability-name");abilities.Add(attack);abilities.Add(Ability("衝刺","E",out dashTime));bottom.Add(abilities);Ignore(bottom);hudLayer.Add(bottom);
            healthLayer=Box("world-health-layer");healthLayer.pickingMode=PickingMode.Ignore;hudLayer.Add(healthLayer);connection=Text("","objective solo-hint");Ignore(connection);hudLayer.Add(connection);performance=Text("","performance-overlay");Ignore(performance);root.Add(performance);
        }
        private void RefreshHUD()
        {
            var f=Flow;var world=client.Presentation.World;bool playback=f.Playback.Active;
            hudLayer.style.display=(f.Stage=="battle"||playback)&&world?.snapshot!=null&&world.snapshot.match?.phase!="loading"&&!screenBlocks&&!menuOpen&&!settingsOpen&&!advanced&&!f.Battle.HasResult?DisplayStyle.Flex:DisplayStyle.None;
            hudLayer.Q<VisualElement>(className:"hud-bottom").style.display=playback?DisplayStyle.None:DisplayStyle.Flex;hudLayer.Q<Button>(className:"hud-menu").style.display=playback?DisplayStyle.None:DisplayStyle.Flex;
            performance.style.display=showStats&&!settingsOpen?DisplayStyle.Flex:DisplayStyle.None;performance.text="FPS "+fps.ToString("0")+"  ·  "+(1000/Math.Max(1,fps)).ToString("0.0")+" ms\n連線 RTT "+(f.Battle.Connected?(f.Battle.RTTSeconds*1000).ToString("0")+" ms":"—");
            var snapshot=world?.snapshot;if(snapshot==null)return;var content=f.Playback.Active?f.Playback.Content:f.Content;var match=snapshot.match;int rate=content.Rules.tickRate;double remaining=Math.Max(0,(match?.endsAtTick??snapshot.tick)-world.tick)/(double)rate;timer.text=((int)remaining/60).ToString("00")+":"+((int)remaining%60).ToString("00");objective.text=match?.pointStatus=="contested"?"爭奪中 · 暫停計分":match?.pointStatus=="scoring"?MenuTeamName(match.scoringTeamID)+"正在得分":"站入據點即可得分";if(match?.point!=null)objective.text="據點 "+(match.point.index+1)+" · "+objective.text;if(f.Room?.practice==true&&!root.ClassListContains("battle-short"))objective.text="練習對手 · "+PracticeModeName(f.Room.botMode)+"\n"+objective.text;
            connection.text=f.Recovery.Pending?"連線中斷，正在自動重連…":world.frozen?"連線暫停，保留最後畫面":"";connection.style.display=connection.text.Length>0?DisplayStyle.Flex:DisplayStyle.None;var own=world.players.FirstOrDefault(p=>p.own)?.state;
            string scores=string.Join(";",match?.scores?.Select(s=>s.teamID+s.score+s.forfeited)??Array.Empty<string>());if(scoreSignature!=scores){scoreSignature=scores;scoreRow.Clear();foreach(var score in match?.scores??Array.Empty<TeamScore>()){var card=Box("score-card");var color=TeamColor(score.teamID);card.style.borderTopColor=color;var label=Text(MenuTeamName(score.teamID)+(score.teamID==own?.teamID?" · 你":""),"score-name");label.style.color=color;card.Add(label);card.Add(Text(score.forfeited?"離場":score.score+" / "+content.Rules.scoreGoal,"score-number"));Ignore(card);scoreRow.Add(card);}}
            if(own!=null){var rule=content.Hero(own.heroID);heroLabel.text=HeroName(own.heroID)+" · 生命值";hpNumber.text=own.hp+" / "+rule.maxHP;hpFill.style.width=Length.Percent(100*Mathf.Clamp01(own.hp/(float)rule.maxHP));life.text=own.hp<=0?"復活倒數 "+Seconds(own.respawnTick,world.tick,rate)+" 秒":own.protectedUntilTick>world.tick?"出生保護中":"";life.style.display=life.text.Length==0?DisplayStyle.None:DisplayStyle.Flex;attackName.text=own.heroID=="mage"?"落火球":own.heroID=="tank"?"揮砍":"射箭";basicTime.text=own.attackReleaseTick>world.tick?"出手中":own.basicReadyTick<=world.tick?"就緒":Seconds(own.basicReadyTick,world.tick,rate)+"s";attackTime.text=own.fireReadyTick<=world.tick?"就緒":Seconds(own.fireReadyTick,world.tick,rate)+"s";dashTime.text=own.dashReadyTick<=world.tick?"就緒":Seconds(own.dashReadyTick,world.tick,rate)+"s";}
            var ids=new HashSet<string>();foreach(var player in world.players){var p=player.state;ids.Add(p.playerID);if(!health.TryGetValue(p.playerID,out var element)){element=Box("world-health");element.Add(Text("","world-health-number"));var track=Box("hp-track");track.Add(Box("hp-fill"));element.Add(track);Ignore(element);healthLayer.Add(element);health[p.playerID]=element;}element.Q<Label>().text=p.left?"離場":p.hp.ToString();var fill=element.Q<VisualElement>(className:"hp-fill");fill.style.width=Length.Percent(100*Mathf.Clamp01(p.hp/(float)content.Hero(p.heroID).maxHP));fill.style.backgroundColor=TeamColor(p.teamID);}
            foreach(string id in health.Keys.Where(id=>!ids.Contains(id)).ToArray()){health[id].RemoveFromHierarchy();health.Remove(id);}
        }
        private static string Seconds(long until,long tick,int rate)=>(Math.Max(0,until-tick)/(double)rate).ToString("0.0");
        private void PositionHealth()
        {
            if(root.panel==null)return;foreach(var pair in health){if(!client.Presentation.TryActorBounds(pair.Key,out var bounds)){pair.Value.style.display=DisplayStyle.None;continue;}pair.Value.style.display=DisplayStyle.Flex;Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);for(int corner=0;corner<8;corner++){var point=client.Presentation.Camera.WorldToScreenPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));var ui=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(point.x,Screen.height-point.y));min=Vector2.Min(min,ui);max=Vector2.Max(max,ui);}var style=pair.Value.resolvedStyle;if(float.IsNaN(style.height)||float.IsNaN(style.width))continue;pair.Value.style.left=(min.x+max.x-style.width)/2;pair.Value.style.top=min.y-style.height-6;}
        }
        private void UpdateReplay()
        {
            if(!Flow.Playback.Active){if(replayPanel!=null){replayPanel.RemoveFromHierarchy();replayPanel=null;}return;}
            if(replayPanel==null){
                replayPanel=Box("playback-dock");replayPanel.pickingMode=PickingMode.Ignore;
                var player=Box("playback-panel");player.name="replayPlayer";replayPanel.Add(player);
                replayTime=Text("","playback-time");replayTime.name="replayTime";
                var heading=Box("playback-heading");heading.Add(Text("對戰重播","playback-title"));heading.Add(replayTime);
                var header=Box("playback-header");header.Add(heading);header.Add(Button("離開重播",()=>Flow.EndPlayback(),"playback-button playback-exit"));player.Add(header);
                replaySeek=new Slider(0,1){name="replaySeek",tooltip="拖曳以跳轉播放位置"};replaySeek.AddToClassList("playback-slider");
                replaySeek.RegisterValueChangedCallback(evt=>{var clock=Flow.Playback.Clock;Flow.Playback.Seek(clock.FirstTick+evt.newValue*(clock.LastTick-clock.FirstTick));});
                replayProgress=Box("playback-progress");replayProgress.pickingMode=PickingMode.Ignore;replaySeek.Q(className:"unity-base-slider__tracker").Add(replayProgress);player.Add(replaySeek);
                replayToggle=Button("暫停",()=>{var clock=Flow.Playback.Clock;if(clock.Playing)clock.Pause();else clock.Play();},"playback-button playback-primary");replayToggle.name="replayToggle";
                replayBackward=Button("上一格",()=>Flow.Playback.Step(-1),"playback-button");replayBackward.name="replayBackward";
                replayForward=Button("下一格",()=>Flow.Playback.Step(1),"playback-button");replayForward.name="replayForward";
                var transport=Box("playback-transport");transport.Add(replayToggle);transport.Add(replayBackward);transport.Add(replayForward);transport.Add(Button("重新播放",()=>{Flow.Playback.Seek(Flow.Playback.Clock.FirstTick);Flow.Playback.Clock.Play();},"playback-button playback-restart"));
                var speeds=Box("playback-speed-options");speeds.name="replaySpeed";
                foreach(double speed in new[]{.25,.5,1,2,4}){
                    Button choice=null;string label=speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×";
                    choice=Button(label,()=>{Flow.Playback.Clock.Speed=speed;foreach(var sibling in speeds.Children())sibling.EnableInClassList("selected",sibling==choice);},"playback-button playback-speed-choice");
                    choice.tooltip="播放速度 "+label;choice.EnableInClassList("selected",speed==Flow.Playback.Clock.Speed);speeds.Add(choice);
                }
                var speedGroup=Box("playback-speed-group");speedGroup.Add(Text("速度","playback-speed-label"));speedGroup.Add(speeds);
                var controls=Box("playback-controls");controls.Add(transport);controls.Add(speedGroup);player.Add(controls);modalLayer.Add(replayPanel);replayToggle.Focus();
            }
            var value=Flow.Playback.Clock;float progress=(float)((value.Tick-value.FirstTick)/Math.Max(1,value.LastTick-value.FirstTick));
            replayToggle.text=value.Playing?"暫停":"播放";replaySeek.SetValueWithoutNotify(progress);replayProgress.style.width=Length.Percent(100*Mathf.Clamp01(progress));
            replayBackward.SetEnabled(value.Tick>value.FirstTick);replayForward.SetEnabled(value.Tick<value.LastTick);
            double rate=Flow.Playback.Content.Rules.tickRate;
            replayTime.text=ReplayTime((value.Tick-value.FirstTick)/rate)+" / "+ReplayTime((value.LastTick-value.FirstTick)/rate);
        }
        private static string ReplayTime(double seconds)
        {
            long tenths=(long)Math.Round(Math.Max(0,seconds)*10);
            return (tenths/600).ToString("00")+":"+(tenths/10%60).ToString("00")+"."+(tenths%10);
        }
        private static string Message(string raw)
        {
            switch(raw){case "session_expired":return "身份已過期，請重新登入。";case "room_not_found":return "找不到房間，請確認代碼。";case "room_full":return "房間人數已滿，請選擇其他房間。";case "team_full":return "隊伍人數已滿。";case "room_not_ready":return "請確認至少兩隊有人，且全員已準備。";case "room_started":return "房間已開始對戰，請等待下一場。";case "server_unavailable":return "暫時無法連線到伺服器，請稍後重試。";case "takeover_required":return "原身份仍有連線，請稍後重試或接管。";case "connection_closed":return "連線已中斷。";case "receive_overflow":return "連線暫時中斷，正在重新連線。";case "host_required":return "這項操作需要房主執行。";case "version_mismatch":case "map_mismatch":return "遊戲版本不同，請重新整理遊戲。";default:return raw;}
        }
        private void OnDestroy(){if(instance==this)instance=null;if(panel!=null)Destroy(panel);}
    }
}
