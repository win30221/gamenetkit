#if UNITY_EDITOR || TOWER_DUEL_SOLO
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.Http;
#else
using System.Runtime.InteropServices;
#endif
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;

namespace TowerDuel.Solo
{
    // Optional test adapter. The normal client never references this component.
    public sealed class TowerDuelSoloPlugin : MonoBehaviour
    {
        [Serializable] private sealed class RoomRequest { public string mode; }
        [Serializable] private sealed class RoomReply { public string room,mode,server; }
        [Serializable] private sealed class SavedRoom { public string room,mode,server; }
        [Serializable] private sealed class BrowserReply { public int id,status; public string body,error; }
        private const string SavedKey="TowerDuel.Solo.v1";
        [Serializable] private sealed class ProbeReport
        {
            public bool passed,ownUnitsSeen,opponentUnitsSeen;
            public string mode,room,phase,status,error;
            public int seat,frames,accepted,resumes;
            public long tick;
        }
        private TowerDuelClient client;
        private VisualElement ui;
        private Button practice,sandbox,multiplayer,modeBack;
        private VisualElement menu;
        private Label hint;
        private string api,room,mode,reportPath;
        private bool busy,hadState,wasOwned,recoveryTested,finished,multiplayerForm,previousState,keepMultiplayerForm;
        private double started,nextReport,nextDeploy;
        private readonly ProbeReport report=new ProbeReport();
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10)};
#else
        private int nextRequest;
        private readonly Dictionary<int,BrowserReply> replies=new Dictionary<int,BrowserReply>();
        [DllImport("__Internal")] private static extern void TD_SoloRequest(int id,string method,string url,string body,string target);
        [DllImport("__Internal")] private static extern void TD_SoloCancel(int id);
        [DllImport("__Internal")] private static extern void TD_SoloCancelAll();
        [Preserve] public void OnSoloResponse(string json)
        {
            try{var reply=JsonUtility.FromJson<BrowserReply>(json);if(reply!=null)replies[reply.id]=reply;}
            catch(Exception error){Debug.LogWarning("Solo companion response: "+error.Message);}
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if(string.IsNullOrEmpty(TowerDuelClient.Argument("-td-solo-api","")))return;
#endif
            new GameObject("TowerDuelSoloPlugin").AddComponent<TowerDuelSoloPlugin>();
        }

        private IEnumerator Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            api=new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
#else
            api=TowerDuelClient.Argument("-td-solo-api","").TrimEnd('/');
            if(!Uri.TryCreate(api,UriKind.Absolute,out var endpoint)||endpoint.Scheme!="http"||endpoint.Host!="127.0.0.1")
            {Debug.LogError("Solo plugin requires a local companion API.");yield break;}
#endif
            double deadline=DuelSession.Now+15;
            while(DuelSession.Now<deadline)
            {
                client=FindAnyObjectByType<TowerDuelClient>();
                if(client!=null&&client.Session!=null&&client.GetComponent<UIDocument>()!=null)break;
                yield return null;
            }
            if(client==null||client.Session==null){Debug.LogError("Solo plugin could not find the running client.");yield break;}
            ui=client.GetComponent<UIDocument>().rootVisualElement;
            var friends=ui.Q("friendRoom");
            menu=new VisualElement{name="soloPluginMenu"};
            var actions=new VisualElement();actions.style.flexDirection=FlexDirection.Row;
            practice=new Button(()=>Begin("practice")){name="soloPracticeButton",text="單人練習"};
            sandbox=new Button(()=>Begin("sandbox")){name="soloSandboxButton",text="自由測試"};
            practice.AddToClassList("menu-button");sandbox.AddToClassList("menu-button");
            foreach(var button in new[]{practice,sandbox}){button.style.flexGrow=1;button.style.flexBasis=0;button.style.minHeight=48;button.style.marginTop=12;actions.Add(button);}
            sandbox.style.marginLeft=12;menu.Add(actions);
            multiplayer=new Button(()=>ShowMultiplayer(true)){name="soloMultiplayerButton",text="雙人對戰"};
            multiplayer.AddToClassList("menu-button");multiplayer.style.minHeight=48;menu.Add(multiplayer);
            hint=new Label();hint.AddToClassList("menu-status");menu.Add(hint);
            friends.parent.Insert(friends.parent.IndexOf(friends),menu);
            modeBack=new Button(()=>{keepMultiplayerForm=false;ShowMultiplayer(false);}){name="soloModeBackButton",text="← 模式選擇"};
            modeBack.AddToClassList("menu-button");modeBack.style.minHeight=48;
            friends.parent.Add(modeBack);ShowMultiplayer(false);
            try
            {
                var saved=JsonUtility.FromJson<SavedRoom>(PlayerPrefs.GetString(SavedKey,""));
                if(saved!=null&&saved.server==client.Server&&(saved.mode=="practice"||saved.mode=="sandbox"))
                {room=saved.room;mode=saved.mode;}
            }
            catch(Exception){PlayerPrefs.DeleteKey(SavedKey);}
            started=DuelSession.Now;
            reportPath=Debug.isDebugBuild?TowerDuelClient.Argument("-td-solo-report",""):"";
            string automatic=TowerDuelClient.Argument("-td-solo-mode","");
#if UNITY_WEBGL && !UNITY_EDITOR
            foreach(string part in new Uri(Application.absoluteURL).Query.TrimStart('?').Split('&'))
            {var pair=part.Split('=');if(pair.Length==2&&pair[0]=="mode")automatic=Uri.UnescapeDataString(pair[1]);}
#endif
            Debug.Log("Tower Duel optional solo plugin ready: "+api);
            if(automatic=="practice"||automatic=="sandbox")
            {
                yield return null;
                Begin(automatic);
            }
            else if(automatic=="multiplayer"){keepMultiplayerForm=true;ShowMultiplayer(true);}
        }

        private void ShowMultiplayer(bool show)
        {
            if(busy)return;
            multiplayerForm=show;
            ui.Q("friendRoom").style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            ui.Q("quickMatchButton").style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            menu.style.display=show?DisplayStyle.None:DisplayStyle.Flex;
            modeBack.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            ui.Q<TextField>("nameField").Blur();
        }

        private void Begin(string requested)
        {
            if(busy||client.Session.State!=null||client.Session.Connecting||client.Session.Recovering||client.Session.Searching)return;
            StartCoroutine(CreateRoom(requested));
        }

        private IEnumerator CreateRoom(string requested)
        {
            busy=true;hint.text="正在建立練習…";
            if(room!=null){string previous=room;room=null;ClearSaved(previous);yield return ReleaseRoom(previous);}
            string json=null;
            yield return Request("POST","/solo/rooms",JsonUtility.ToJson(new RoomRequest{mode=requested}),value=>json=value);
                if(json==null)
                {hint.text="目前無法建立練習，請稍後再試。";busy=false;yield break;}
                RoomReply result=null;
                try{result=JsonUtility.FromJson<RoomReply>(json);}catch(Exception){}
                if(result==null||result.room==null||result.room.Length!=6||result.server!=client.Server||result.mode!=requested)
                {hint.text="測試外掛回覆不相容，請重新建置。";busy=false;yield break;}
                room=result.room;mode=requested;hadState=false;
                PlayerPrefs.SetString(SavedKey,JsonUtility.ToJson(new SavedRoom{room=room,mode=mode,server=client.Server}));PlayerPrefs.Save();
                string name=ui.Q<TextField>("nameField").value;
                client.Connect(client.Server,"join",room,name);
                hint.text=requested=="practice"?"電腦對手已加入。":"自由測試：對手不出兵，塔仍正常防守。";
                Debug.Log("Solo plugin joined normal backend room "+room+" via "+client.Server);
            busy=false;
        }

        private IEnumerator ReleaseRoom(string code)
        {
            yield return Request("DELETE","/solo/rooms/"+Uri.EscapeDataString(code),null,_=>{});
        }

        private IEnumerator Request(string method,string path,string body,Action<string> complete)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int id=++nextRequest;
            TD_SoloRequest(id,method,api+path,body??"",gameObject.name);
            double deadline=DuelSession.Now+10;
            while(!replies.ContainsKey(id)&&DuelSession.Now<deadline)yield return null;
            string result=null;
            if(replies.TryGetValue(id,out var reply))
            {if(reply.status>=200&&reply.status<300)result=reply.body;else Debug.LogWarning("Solo API request failed: "+reply.status);replies.Remove(id);}
            else TD_SoloCancel(id);
            complete(result);
#else
            using(var request=new HttpRequestMessage(new HttpMethod(method),api+path))
            {
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                var pending=http.SendAsync(request);
            while(!pending.IsCompleted)yield return null;
                string result=null;
                try{using(var response=pending.GetAwaiter().GetResult()){if(response.IsSuccessStatusCode)result=response.Content.ReadAsStringAsync().GetAwaiter().GetResult();}}
                catch(Exception error){Debug.LogWarning("Solo companion: "+error.Message);}
                complete(result);
            }
#endif
        }

        private static void ClearSaved(string code)
        {
            try{var saved=JsonUtility.FromJson<SavedRoom>(PlayerPrefs.GetString(SavedKey,""));if(saved?.room==code){PlayerPrefs.DeleteKey(SavedKey);PlayerPrefs.Save();}}
            catch(Exception){PlayerPrefs.DeleteKey(SavedKey);}
        }

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TD_SoloCancelAll();
#else
            http.Dispose();
#endif
        }

        private void LateUpdate()
        {
            if(ui==null||client==null||finished)return;
            var state=client.Session.State;
            bool owned=state!=null&&state.room==room;
            bool available=!busy&&!client.Session.Connecting&&!client.Session.Recovering&&!client.Session.Searching;
            practice.SetEnabled(available);sandbox.SetEnabled(available);multiplayer.SetEnabled(available);modeBack.SetEnabled(available);
            if(state==null&&previousState&&!client.Session.Connecting&&!client.Session.Recovering)ShowMultiplayer(keepMultiplayerForm);
            previousState=state!=null;
#if UNITY_WEBGL && !UNITY_EDITOR
            if(state==null&&!multiplayerForm&&string.IsNullOrEmpty(client.Session.Status))ui.Q<Label>("lobbyStatus").text="選擇模式即可開始。";
#endif
            if(owned)
            {
                hadState=true;
                if(state.phase=="waiting"&&!state.players[state.seat].ready)client.Session.Ready();
                string title=mode=="practice"?"單人練習":"自由測試";
                ui.Q<Label>("roomLabel").text=title+" · "+state.players[state.seat].name;
                ui.Q("inviteCode").style.display=DisplayStyle.None;ui.Q("copyButton").style.display=DisplayStyle.None;ui.Q("readyButton").style.display=DisplayStyle.None;
                ui.Q("waitingLayer").Q<Label>(className:"section-title").text=title;
                if(state.phase=="ended")ui.Q<Button>("rematchButton").text="再練一場";
            }
            else if(wasOwned)
            {
                ui.Q("inviteCode").style.display=DisplayStyle.Flex;ui.Q("copyButton").style.display=DisplayStyle.Flex;ui.Q("readyButton").style.display=DisplayStyle.Flex;
                ui.Q("waitingLayer").Q<Label>(className:"section-title").text="邀請朋友來一場";
                ui.Q<Button>("rematchButton").text="同房再戰";
            }
            wasOwned=owned;
            if(room!=null&&state==null&&!client.Session.Connecting&&!client.Session.Recovering&&!busy&&(hadState||DuelSession.Now-started>15))
            {string previous=room;room=null;hadState=false;ClearSaved(previous);StartCoroutine(ReleaseRoom(previous));}
            if(!string.IsNullOrEmpty(reportPath))Probe(owned);
        }

        // Opt-in native smoke test; kept entirely inside the removable adapter.
        private void Probe(bool owned)
        {
            report.frames++;report.mode=mode;report.status=client.Session.Status;
            var state=client.Session.State;
            if(owned)
            {
                report.room=state.room;report.phase=state.phase;report.seat=state.seat;report.tick=state.tick;
                report.accepted=client.Session.Accepted;report.resumes=client.Session.Resumes;
                foreach(var unit in state.units){if(unit.seat==state.seat)report.ownUnitsSeen=true;else report.opponentUnitsSeen=true;}
                if(client.Session.Accepted>=2&&!client.Session.Pending&&!recoveryTested)
                {recoveryTested=true;client.Session.SimulateDisconnect();}
                if(client.Session.CanAct&&(state.phase=="playing"||state.phase=="overtime")&&!client.Session.Pending&&DuelSession.Now>=nextDeploy)
                {
                    string card=null;foreach(string id in state.hand)if(!client.Rules.Find(id).IsSpell&&state.energy>=client.Rules.Find(id).cost){card=id;break;}
                    if(card!=null)
                    {
                        client.Select(card);client.Deploy(new Vector3(-5.5f,0,state.seat==0?-3.5f:3.5f));
                        nextDeploy=DuelSession.Now+.5;
                    }
                }
                report.passed=report.frames>300&&report.accepted>=3&&report.resumes>0&&report.ownUnitsSeen&&(mode=="sandbox"?!report.opponentUnitsSeen:report.opponentUnitsSeen);
            }
            bool expired=DuelSession.Now-started>90;
            if(expired)report.error="Solo native smoke test timed out.";
            if(report.passed||expired||DuelSession.Now>=nextReport)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));nextReport=DuelSession.Now+1;
            }
            if(report.passed||expired)
            {
                finished=true;
                if(report.passed)ScreenCapture.CaptureScreenshot(Path.ChangeExtension(reportPath,"png"));
                StartCoroutine(FinishProbe(report.passed));
            }
        }

        private IEnumerator FinishProbe(bool passed)
        {
            yield return new WaitForEndOfFrame();yield return null;
            client.Leave();
            if(room!=null)yield return ReleaseRoom(room);
            Application.Quit(passed?0:1);
        }
    }
}
#endif
