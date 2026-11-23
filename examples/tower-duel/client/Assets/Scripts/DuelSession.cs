using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Realtime.Core;
using Realtime.Interpolation;
using Realtime.Transport;
using UnityEngine;

namespace TowerDuel
{
    public sealed class DuelSession : IDisposable
    {
        [Serializable] private sealed class ResumeData { public string server,room,token,name; public long saved; }
        private const string ResumeKey="TowerDuel.Resume.v3";
        public readonly MessageTransport Transport=new MessageTransport();
        private readonly IPresentationTimeline timeline=new FixedTimeline(30,15);
        private readonly SnapshotBuffer<DuelFrame> frames=new SnapshotBuffer<DuelFrame>(32);
        private readonly ReconnectPolicy recovery=new ReconnectPolicy();
        private readonly List<EffectState> sampledEffects=new List<EffectState>(16);
        private readonly DuelMap map;
        private readonly DuelRules rules;
        private DeployMessage pending;
        private Task<bool> send;
        private long nextAction;
        private bool readyRequested,rematchRequested,leaving;
        private double deadline,lastReceive,nextSend,pendingSince,nextSave;
        private string server,playerName,callback,room,token;
        public DuelMessage State {get;private set;}
        public string Status {get;private set;}="";
        public bool Connecting {get;private set;}
        public bool Searching {get;private set;}
        public bool Recovering=>recovery.Pending;
        public double RenderTick=>timeline.RenderTick;
        public bool Pending=>pending!=null;
        public bool CanAct=>State!=null&&!Recovering&&!Connecting&&!leaving;
        public int Accepted {get;private set;}
        public int Rejected {get;private set;}
        public int Resumes {get;private set;}
        public int LastSnapshotBytes {get;private set;}
        public string LastPlayedCard {get;private set;}
        public Action<DuelMessage> StateChanged;
        public Action<DuelMessage> ActionConfirmed;
        public DuelSession(DuelMap map,DuelRules rules){this.map=map;this.rules=rules;}
        public static double Now=>MessageTransport.ClockMS/1000;
        public void Connect(string endpoint,string mode,string code,string name,string callbackName,string[] deck=null)
        {
            Disconnect();server=endpoint.Trim();playerName=name.Trim();callback=callbackName;room=code.Trim().ToUpperInvariant();
            Open(mode,Now,deck);
            Searching=mode=="quick";
            Status=Searching?"正在尋找對手…":"連線中…";
        }
        private void Open(string mode,double now,string[] deck=null)
        {
            var endpoint=new UriBuilder(server);
            if(endpoint.Scheme=="http")endpoint.Scheme="ws";else if(endpoint.Scheme=="https")endpoint.Scheme="wss";
            if(endpoint.Scheme!="ws"&&endpoint.Scheme!="wss")throw new ArgumentException("Unsupported endpoint");
            endpoint.Path="/ws";endpoint.Query="";endpoint.Fragment="";
            HelloMessage hello=mode!="resume"&&deck!=null
                ? new DeckHelloMessage{deck=deck} : new HelloMessage();
            hello.mode=mode;
            hello.room=room??"";
            hello.name=playerName??"";
            hello.mapHash=map.Hash;
            hello.rulesHash=rules.Hash;
            hello.token=mode=="resume"?token:"";
            int epoch=Transport.BeginAttempt();Transport.Open(epoch,endpoint.Uri.AbsoluteUri,JsonUtility.ToJson(hello),"",callback,false);
            Connecting=true;deadline=now+(Recovering?5:12);send=null;nextSend=0;
        }
        public bool TryRestore(string endpoint,string callbackName)
        {
            try
            {
                var saved=JsonUtility.FromJson<ResumeData>(PlayerPrefs.GetString(ResumeKey,""));
                if(saved==null||saved.server!=endpoint||string.IsNullOrEmpty(saved.token)||DateTimeOffset.UtcNow.ToUnixTimeSeconds()-saved.saved>40)return false;
                server=endpoint;callback=callbackName;room=saved.room;token=saved.token;playerName=saved.name;
                // A saved seat is eligible for recovery before this process receives a welcome.
                recovery.Connected();BeginRecovery(Now);return true;
            }
            catch(Exception){PlayerPrefs.DeleteKey(ResumeKey);return false;}
        }
        private void SaveResume()
        {
            if(string.IsNullOrEmpty(token)||Now<nextSave)return;
            nextSave=Now+5;
            PlayerPrefs.SetString(ResumeKey,JsonUtility.ToJson(new ResumeData{server=server,room=room,token=token,name=playerName,saved=DateTimeOffset.UtcNow.ToUnixTimeSeconds()}));
            PlayerPrefs.Save();
        }
        public bool InHand(string card)=>State?.hand!=null&&Array.IndexOf(State.hand,card)>=0;
        public void Ready(){if(CanAct&&State.phase=="waiting")readyRequested=true;}
        public void Rematch(){if(CanAct&&State.phase=="ended")rematchRequested=true;}
        public bool Deploy(Vector3 point,string card="guard")
        {
            if(!CanAct||Pending||(State.phase!="playing"&&State.phase!="overtime"))return false;
            var definition=rules.Find(card);if(definition==null)return false;
            if(!InHand(card)){Status=DuelText.Reason("card_not_in_hand");return false;}
            if(State.energy+1e-5f<definition.cost){Status=DuelText.Reason("not_enough_energy");return false;}
            if(!map.CanDeploy(State.seat,point,State,definition)){Status=definition.IsSpell?"請在戰場內指定法術落點。":DuelText.Reason("invalid_placement");return false;}
            pending=new DeployMessage{match=State.match,action=++nextAction,card=card,x=point.x,z=point.z};pendingSince=Now;nextSend=0;Status="正在確認出牌…";return true;
        }
        private void BeginRecovery(double now)
        {
            if(string.IsNullOrEmpty(token)||!recovery.Fail(now,true)){Disconnect("連線已中斷，請重新加入。");return;}
            Connecting=false;Transport.Close();send=null;
            Status="連線中斷，正在恢復原局…";
        }
        public void SimulateDisconnect(){if(Debug.isDebugBuild)BeginRecovery(Now);}
        public void Pump()=>Pump(Now);
        internal void Pump(double now)
        {
            if(leaving)return;
            // Poll during an in-flight handshake too, so attempts cannot extend the SDK's window.
            string retry=recovery.Poll(now);
            if(retry=="expired"){Disconnect("未能在寬限時間內恢復連線，請重新開房。");return;}
            if(retry=="retry"){try{Open("resume",now);}catch(Exception){BeginRecovery(now);}}
            while(Transport.TryRead(out var incoming))
            {
                try
                {
                    var message=JsonUtility.FromJson<DuelMessage>(incoming.JSON);
                    if(message==null)throw new InvalidOperationException("empty message");
                    if(message.type=="error")
                    {
                        if(message.code=="resume_expired"||message.code=="rules_mismatch"||message.code=="map_mismatch"||message.code=="invalid_hello"||(!Recovering&&State==null)){Disconnect(DuelText.Reason(message.code));break;}
                        BeginRecovery(now);break;
                    }
                    if(message.type=="queued")
                    {
                        if(Searching&&State==null)
                        {
                            Connecting=false;
                            deadline=now+8;
                            Status="正在尋找對手…";
                        }
                        continue;
                    }
                    if(message.type=="action")
                    {
                        if(State!=null&&pending!=null&&message.match==State.match&&message.action==pending.action)
                        {
                            if(message.accepted){Accepted++;LastPlayedCard=pending.card;}else Rejected++;
                            if(message.hand!=null&&message.hand.Length==4&&message.handVersion>=State.handVersion){State.hand=message.hand;State.nextCard=message.nextCard;State.handVersion=message.handVersion;State.energy=message.energy;}
                            Status=message.accepted?"部署成功。":DuelText.Reason(message.reason);
                            pending=null;ActionConfirmed?.Invoke(message);
                        }
                        continue;
                    }
                    if(message.type!="welcome"&&message.type!="snapshot")throw new InvalidOperationException("unknown message");
                    Validate(message,map.Hash,rules.Hash,rules);
                    double received=incoming.ReceivedMS/1000;
                    LastSnapshotBytes=System.Text.Encoding.UTF8.GetByteCount(incoming.JSON);
                    bool fresh=State==null||message.match!=State.match;
                    if(fresh&&message.type!="welcome")continue;
                    if(!fresh&&message.seat!=State.seat)throw new InvalidOperationException("seat mismatch");
                    if(!fresh&&message.type=="snapshot"&&message.tick<=State.tick)continue;
                    if(fresh)
                    {
                        pending=null;nextAction=0;readyRequested=rematchRequested=false;Accepted=Rejected=0;LastPlayedCard=null;frames.Clear();timeline.Reset(message.tick,received);
                    }
                    if(message.type=="welcome")
                    {
                        if(Recovering)Resumes++;
                        Searching=false;
                        recovery.Connected();Connecting=false;token=message.resumeToken;room=message.room;nextSave=0;SaveResume();
                        nextAction=Math.Max(nextAction,message.actionWatermark);frames.Clear();timeline.Reset(message.tick,received);pendingSince=now;nextSend=0;
                    }
                    else timeline.Observe(message.tick,received);
                    if(!fresh&&message.handVersion<State.handVersion){message.hand=State.hand;message.nextCard=State.nextCard;message.handVersion=State.handVersion;message.energy=State.energy;}
                    bool phaseChanged=fresh||State.phase!=message.phase;
                    State=message;frames.Add(message.tick,new DuelFrame(message));Connecting=false;lastReceive=received;
                    if(phaseChanged||message.type=="welcome")
                    {
                        Status=message.phase=="waiting"?"已連線，等待雙方準備。":message.phase=="countdown"?"雙方已準備，即將開始。":message.phase=="playing"?"拖曳部隊到己方半場；法術可指定整個戰場。":message.phase=="overtime"?"延長賽！先摧毀敵塔即可獲勝。":DuelText.Reason(message.reason);
                    }
                    StateChanged?.Invoke(message);SaveResume();
                }
                catch(Exception error){Debug.LogWarning("Tower Duel rejected message: "+error.Message);Disconnect("伺服器資料不相容，請更新遊戲。");break;}
            }
            if(Connecting&&now>deadline){if(Recovering)BeginRecovery(now);else Disconnect("連線逾時，請確認伺服器位址。");return;}
            if(Searching&&now>deadline){Disconnect("配對連線已中斷，請再試一次。");return;}
            if(State==null||Recovering)return;
            if(now-lastReceive>4){BeginRecovery(now);return;}
            timeline.Advance(now);
            if(send!=null)
            {
                if(!send.IsCompleted)return;
                try{if(!send.GetAwaiter().GetResult())nextSend=now+.25;}catch(Exception){BeginRecovery(now);return;}
                send=null;
            }
            if(pending!=null&&now-pendingSince>8){BeginRecovery(now);return;}
            if(now<nextSend||Transport.Sending)return;
            string json=null;
            if(pending!=null)json=JsonUtility.ToJson(pending);
            else if(readyRequested&&State.phase=="waiting"&&!State.players[State.seat].ready)json=JsonUtility.ToJson(new ReadyMessage{match=State.match});
            else if(rematchRequested&&State.phase=="ended"&&!State.players[State.seat].rematch)json=JsonUtility.ToJson(new ControlMessage{type="rematch",match=State.match});
            if(json!=null){send=Transport.TrySend(json);nextSend=now+.5;}
        }
        public async void Leave()
        {
            if(leaving)return;leaving=true;int epoch=Transport.Epoch;
            try {if(State!=null&&!Recovering&&!Transport.Sending)await Transport.TrySend(JsonUtility.ToJson(new ControlMessage{type="leave",match=State.match}));}
            catch(Exception){}
            finally{if(epoch==Transport.Epoch)Disconnect();leaving=false;}
        }
        public Vector3 Sample(EntityState entity)
        {
            if(frames.TryBracket(timeline.RenderTick,entity.id,SelectUnit,out SnapshotBracket<EntityState> bracket))
            {
                return bracket.HasAfter?Vector3.Lerp(bracket.Before.Position,bracket.After.Position,(float)bracket.Fraction):bracket.Before.Position;
            }
            return entity.Position;
        }
        private static bool SelectUnit(DuelFrame frame,long id,out EntityState entity)=>frame.Units.TryGetValue(id,out entity);
        public bool SampleSlowed(EntityState entity)
        {
            // Match the ring and interpolated foot position, rather than a newer
            // snapshot's status that can arrive before the soldier crosses it.
            var position=Sample(entity);
            var effects=SampleEffects();
            for(int i=0;i<effects.Count;i++)
            {
                var effect=effects[i];
                if(effect.kind!="frost"||effect.seat==entity.seat||RenderTick<effect.impact||RenderTick>=effect.end||(entity.airborne&&!rules.Find(effect.kind).hitAir))continue;
                var offset=new Vector2(position.x-effect.x,position.z-effect.z);
                var reach=effect.radius+entity.radius;
                if(offset.sqrMagnitude<=reach*reach+1e-5f)return true;
            }
            return false;
        }
        public IReadOnlyList<EffectState> SampleEffects()
        {
            sampledEffects.Clear();
            if(State==null||State.phase=="ended")return sampledEffects;
            // Both rosters matter: the newer one can first reveal a spell that has already started,
            // while the older one retains spells omitted before their rendered lifetime ends.
            if(frames.TryBracket(RenderTick,0,SelectEffects,out SnapshotBracket<EffectState[]> bracket))
            {
                AddEffects(bracket.Before);
                if(bracket.HasAfter)AddEffects(bracket.After);
            }
            else AddEffects(State.effects);
            return sampledEffects;
        }
        private void AddEffects(EffectState[] source)
        {
            foreach(var effect in source)
            {
                if(RenderTick<effect.start||RenderTick>=effect.end)continue;
                bool known=false;foreach(var sample in sampledEffects)if(sample.id==effect.id){known=true;break;}
                if(!known)sampledEffects.Add(effect);
            }
        }
        private static bool SelectEffects(DuelFrame frame,int context,out EffectState[] effects){effects=frame.State.effects;return true;}
        public static void Validate(DuelMessage m,string hash,string rulesHash,DuelRules catalog=null)
        {
            if(m.protocol!=3||m.mapHash!=hash||m.rulesHash!=rulesHash||string.IsNullOrEmpty(m.match)||m.seat<0||m.seat>1||m.tick<0||m.tickRate!=30||m.snapshotRate!=15||m.players==null||m.players.Length!=2||m.towers==null||m.towers.Length!=6||m.units==null||m.units.Length>80||!DuelMap.Finite(m.energy)||m.energy<0||m.energy>10.001f||m.hand==null||m.hand.Length!=4||m.effects==null||m.effects.Length>16||m.handVersion<0||m.actionWatermark<0)throw new InvalidOperationException("invalid state");
            if(m.phase!="waiting"&&m.phase!="countdown"&&m.phase!="playing"&&m.phase!="overtime"&&m.phase!="ended")throw new InvalidOperationException("invalid phase");
            var hand=new HashSet<string>();foreach(string id in m.hand)if(string.IsNullOrEmpty(id)||!hand.Add(id)||(catalog!=null&&catalog.Find(id)==null))throw new InvalidOperationException("invalid hand");
            if(string.IsNullOrEmpty(m.nextCard)||hand.Contains(m.nextCard)||(catalog!=null&&catalog.Find(m.nextCard)==null))throw new InvalidOperationException("invalid next card");
            var ids=new HashSet<long>();
            foreach(var group in new[]{m.units,m.towers})foreach(var e in group)
            {
                if(e==null||e.id<=0||!ids.Add(e.id)||e.seat<0||e.seat>1)throw new InvalidOperationException("invalid entity identity");
                if(!DuelMap.Finite(e.x)||!DuelMap.Finite(e.z)||Mathf.Abs(e.x)>10||Mathf.Abs(e.z)>15||e.hp<0||e.hp>e.maxHP||e.maxHP<=0||!DuelMap.Finite(e.radius)||e.radius<=0)throw new InvalidOperationException("invalid entity");
            }
            foreach(var e in m.units)if(catalog!=null&&(catalog.Find(e.kind)==null||catalog.Find(e.kind).IsSpell))throw new InvalidOperationException("unsupported unit");
            foreach(var e in m.towers)if(e.kind!="keep"&&e.kind!="tower")throw new InvalidOperationException("unsupported tower");
            foreach(var e in m.effects)if(e==null||!ids.Add(e.id)||catalog==null||catalog.Find(e.kind)==null||!catalog.Find(e.kind).IsSpell||!DuelMap.Finite(e.x)||!DuelMap.Finite(e.z)||!DuelMap.Finite(e.radius)||e.radius<=0||e.radius>5||e.start>e.impact||e.impact>=e.end)throw new InvalidOperationException("invalid effect");
        }
        public void Disconnect(string reason="")
        {
            Searching=false;
            recovery.Stop();Transport.Close();State=null;Connecting=false;pending=null;send=null;readyRequested=rematchRequested=false;frames.Clear();timeline.Reset(-1,0);nextAction=0;Accepted=Rejected=0;token=null;PlayerPrefs.DeleteKey(ResumeKey);PlayerPrefs.Save();Status=reason;
        }
        public void ReceiveBrowser(string json)=>Transport.Receive(Transport.Epoch,json);
        public void Dispose(){recovery.Stop();Transport.Dispose();}
    }
}
