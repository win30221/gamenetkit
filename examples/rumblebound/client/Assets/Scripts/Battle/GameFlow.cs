using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Realtime.Core;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    public sealed class ApiReply{public int Status;public string Body;}
    public interface IGameApi{Task<ApiReply> Request(string path,string body,string credential,CancellationToken cancellation);}
    [Serializable] public sealed class RoomTeam{public string teamID;public int capacity;}
    [Serializable] public sealed class RoomMember{public string userID,teamID,heroID;public bool ready,bot;}
    [Serializable] public sealed class GameRoom
    {
        public string code,hostID,youID,matchID,phase,botMode;
        public RoomTeam[] teams;public RoomMember[] members;
        public bool canStart,started,practice;
    }
    [Serializable] internal sealed class EmptyRequest{}
    [Serializable] internal sealed class CountRequest{public int teamCount;}
    [Serializable] internal sealed class CodeRequest{public string code;}
    [Serializable] internal sealed class TeamRequest{public string teamID;}
    [Serializable] internal sealed class HeroRequest{public string heroID;}
    [Serializable] internal sealed class AssignRequest{public string memberID,teamID;}
    [Serializable] internal sealed class CapacityRequest{public string teamID;public int capacity;}
    [Serializable] internal sealed class ReadyRequest{public bool ready;public int protocol;public string content,mapHash;}
    [Serializable] internal sealed class PracticeRequest{public string heroID,botHeroID,mode,content,mapHash;public int protocol;}
    [Serializable] internal sealed class ModeRequest{public string mode;}
    [Serializable] internal sealed class EnterRequest{public string matchID;public bool takeover;}
    [Serializable] internal sealed class ApiError{public string error=null;}

    // All replies commit on Pump. Cancellation and the SDK session revision keep
    // old HTTP completions from changing a replaced login, room or connection.
    public sealed class GameFlow:IDisposable
    {
        private readonly IGameApi api;
        private readonly IGameJson json;
        private readonly string endpoint,callback;
        private Task<ApiReply> pending;
        private CancellationTokenSource request;
        private int requestRevision;
        private string operation,attemptedMatch;
        private double nextRoomPoll,lastNow;
        private double playbackNow;
        private bool wasConnected,wasConnecting,disposed,retryDue,waitingForOwnClose,autoReconnect=true;
        public SessionState Identity{get;}=new SessionState();
        public ReconnectPolicy Recovery{get;}=new ReconnectPolicy();
        public BattleSession Battle{get;}
        public BattleRecording Recording{get;}
        public BattleReplay Playback{get;}
        public GameContent Content{get;private set;}
        public GameRoom Room{get;private set;}
        public BattleSettings Selection{get;set;}=new BattleSettings();
        public bool AutoReconnect{get=>autoReconnect;set{autoReconnect=value;if(!value){if(operation=="retry")CancelRequest();Recovery.Stop();retryDue=false;}}}
        // Background reads are replaceable; only user commands block controls.
        public bool Busy=>pending!=null&&operation!="poll";
        public string Notice{get;private set;}="";
        public string Stage=>Playback.Active?"replay":Content==null?"loading":!Identity.IsAuthenticated?"login":Battle.HasResult?"result":Battle.Connected?"battle":Room==null?"lobby":!Room.started?"room":"battle";
        public GameFlow(IGameApi api,IGameJson json,string websocketEndpoint,string callback="")
        {
            this.api=api??throw new ArgumentNullException(nameof(api));this.json=json??throw new ArgumentNullException(nameof(json));endpoint=websocketEndpoint;this.callback=callback;
            Battle=new BattleSession(json);Recording=new BattleRecording(json);Playback=new BattleReplay(json);Battle.ValidatedSnapshot+=Recording.Accept;Begin("config","/api/config",null);
        }
        private bool Begin(string op,string path,object body)
        {
            if(pending!=null&&operation=="poll")CancelRequest();
            if(disposed||Busy||Playback.Active)return false;
            operation=op;requestRevision=Identity.Revision;request=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try{pending=api.Request(path,body==null?null:json.Write(body),Identity.Credential,request.Token);return true;}
            catch(Exception){request.Dispose();request=null;operation=null;Notice="請求未能送出。";return false;}
        }
        private void CancelRequest()
        {
            request?.Cancel();request?.Dispose();request=null;
            if(pending!=null)_=pending.ContinueWith(task=>{var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            pending=null;operation=null;
        }
        public bool Guest()=>Content!=null&&!Identity.IsAuthenticated&&Begin("guest","/api/session/guest",new EmptyRequest());
        public bool ReloadContent()=>!Busy&&Begin("config","/api/config",null);
        public bool Restore(string credential,string userID)
        {
            if(string.IsNullOrEmpty(credential)||credential.Length>512||credential.Any(char.IsControl)||string.IsNullOrEmpty(userID)||userID.Length>128)return false;
            if(operation!="config")CancelRequest();Battle.Disconnect();Recovery.Stop();wasConnected=wasConnecting=false;Room=null;attemptedMatch=null;
            Identity.Accept(credential,userID);nextRoomPoll=0;return true;
        }
        public bool Create(int teams=2)=>Identity.IsAuthenticated&&teams>=2&&teams<=4&&Begin("room","/api/room/create",new CountRequest{teamCount=teams});
        public bool Join(string code)=>Identity.IsAuthenticated&&!string.IsNullOrWhiteSpace(code)&&code.Length<=32&&Begin("room","/api/room/join",new CodeRequest{code=code.Trim()});
        public bool Team(string id)=>Room!=null&&!Room.started&&Begin("room","/api/room/team",new TeamRequest{teamID=id});
        public bool Hero(string id)=>Room!=null&&!Room.started&&Content.Hero(id)!=null&&Begin("room","/api/room/hero",new HeroRequest{heroID=id});
        public bool Assign(string member,string team)=>Room!=null&&!Room.started&&Begin("room","/api/room/assign",new AssignRequest{memberID=member,teamID=team});
        public bool Capacity(string team,int capacity)=>Room!=null&&!Room.started&&capacity>=1&&capacity<=4&&Begin("room","/api/room/capacity",new CapacityRequest{teamID=team,capacity=capacity});
        public bool Ready(bool value)=>Room!=null&&!Room.started&&Begin("room","/api/room/ready",new ReadyRequest{ready=value,protocol=Content.Rules.protocol,content=Content.Rules.content,mapHash=Content.Config.mapHash});
        public bool Start()=>Room!=null&&!Room.started&&Room.canStart&&Begin("room","/api/room/start",new EmptyRequest());
        public bool Practice(string hero,string botHero,string mode)=>Content!=null&&Identity.IsAuthenticated&&Room==null&&Content.Hero(hero)!=null&&Content.Hero(botHero)!=null&&Begin("room","/api/practice/start",new PracticeRequest{heroID=hero,botHeroID=botHero,mode=mode,protocol=Content.Rules.protocol,content=Content.Rules.content,mapHash=Content.Config.mapHash});
        public bool PracticeMode(string mode)=>Room!=null&&Room.practice&&Begin("room","/api/practice/mode",new ModeRequest{mode=mode});
        public bool Leave()
        {
            if(Room==null||Busy)return false;string path=Room.practice?"/api/practice/leave":"/api/room/leave";
            if(Room.started&&!Room.practice){Notice="room_started";return false;}
            StopBattle();return Begin("leave",path,new EmptyRequest());
        }
        public bool ReturnToRoom()
        {
            if(!Battle.HasResult||Room==null||Room.practice||Busy)return false;StopBattle();return Begin("poll","/api/room/current",null);
        }
        public void StopBattle(){if(operation=="ticket"||operation=="retry")CancelRequest();Battle.Disconnect();Recovery.Stop();retryDue=waitingForOwnClose=false;wasConnected=wasConnecting=false;attemptedMatch=Room?.matchID;}
        public bool Enter(bool takeover=false)
        {
            if(pending!=null&&operation=="poll")CancelRequest();
            if(Room==null||!Room.started||Busy)return false;
            waitingForOwnClose=Battle.Connected||Battle.Connecting;Battle.Disconnect();wasConnected=wasConnecting=false;Recovery.Connected();attemptedMatch=Room.matchID;
            return Begin("ticket","/api/room/ticket",new EnterRequest{matchID=Room.matchID,takeover=takeover});
        }
        public bool Logout()
        {
            if(Busy)return false;StopBattle();return Identity.IsAuthenticated&&Begin("logout","/api/logout",new EmptyRequest());
        }
        private bool Retryable(string code)=>code=="connection_closed"||code=="receive_overflow"||code=="send_failed"||code=="snapshot_timeout"||code=="connection_timeout"||code=="connection_failed"||code=="network_error"||code=="server_unavailable"||(Recovery.Pending||waitingForOwnClose)&&code=="takeover_required";
        private void Fail(string code,bool retry)
        {
            Notice=code;Battle.Disconnect(code);wasConnected=wasConnecting=retryDue=false;
            Recovery.Fail(lastNow,AutoReconnect&&retry&&Retryable(code));
        }
        private void PollReply()
        {
            if(pending==null||!pending.IsCompleted)return;
            var task=pending;string op=operation;int revision=requestRevision;pending=null;operation=null;request.Dispose();request=null;
            if(op!="config"&&revision!=Identity.Revision)return;
            ApiReply reply;
            try{reply=task.GetAwaiter().GetResult();}catch{Notice="server_unavailable";if(op=="ticket"||op=="retry")Fail("server_unavailable",true);return;}
            if(reply.Status<200||reply.Status>=300){
                string error;try{error=json.Read<ApiError>(reply.Body)?.error??"request_failed";}catch{error="request_failed";}
                if(op=="leave"&&reply.Status==404&&error=="not_in_room"){Room=null;attemptedMatch=null;Notice="";return;}
                if(op=="poll"&&reply.Status==404){Room=null;return;}
                if(reply.Status==401){StopBattle();Identity.Clear();Room=null;attemptedMatch=null;}
                Notice=error;if(op=="ticket"||op=="retry")Fail(error,reply.Status>=500||Retryable(error));return;
            }
            try{
                if(op!="poll")Notice="";
                if(op=="config")Content=new GameContent(json.Read<LabConfig>(reply.Body),json);
                else if(op=="guest"){
                    var login=json.Read<SessionReply>(reply.Body);if(login==null||!Identity.Accept(login.session,login.userID))throw new InvalidOperationException("Invalid login reply");nextRoomPoll=0;
                }else if(op=="logout"){Identity.Clear();Room=null;attemptedMatch=null;}
                else if(op=="leave"){Room=null;attemptedMatch=null;}
                else if(op=="room"||op=="poll"){
                    var room=json.Read<GameRoom>(reply.Body);
                    if(room==null||string.IsNullOrEmpty(room.code)||string.IsNullOrEmpty(room.youID)||room.members==null||room.members.Length>8||room.teams==null||room.teams.Length<2||room.teams.Length>4||room.members.Any(m=>m==null)||room.teams.Any(t=>t==null))throw new InvalidOperationException("Invalid room reply");
                    if(room.youID!=Identity.UserID)Identity.Accept(Identity.Credential,room.youID);
                    Room=room;nextRoomPoll=lastNow+1;
                    if(!room.started)attemptedMatch=null;
                    else if(attemptedMatch!=room.matchID)Enter();
                }else if(op=="ticket"||op=="retry"){
                    var ticket=json.Read<TicketReply>(reply.Body);if(ticket==null||ticket.matchID!=attemptedMatch||string.IsNullOrEmpty(ticket.ticket))throw new InvalidOperationException("Stale room ticket");
                    bool webTransport=Selection.Transport=="webtransport";
                    if(webTransport&&(string.IsNullOrEmpty(Content.Config.webTransportURL)||!Realtime.Transport.MessageTransport.SupportsWebTransport)){Fail("webtransport_unavailable",false);return;}
                    Battle.Connect(Content,Selection,webTransport?Content.Config.webTransportURL:endpoint,ticket.ticket,ticket.matchID,callback,lastNow,webTransport,Content.Config.certificateHash);wasConnecting=Battle.Connecting;
                }
            }catch{Notice="invalid_server_reply";if(op=="ticket"||op=="retry")Fail("invalid_server_reply",false);}
        }
        public void Pump(double now,double x,double z)
        {
            if(disposed)return;lastNow=now;
            if(Playback.Active){Playback.Advance(Math.Max(0,now-playbackNow));playbackNow=now;return;}
            PollReply();Battle.Pump(now,x,z);if(Recording.Recording&&!Battle.Connected)Recording.Stop();
            if(Battle.HasResult){if(pending!=null)CancelRequest();Recovery.Stop();wasConnected=wasConnecting=retryDue=waitingForOwnClose=false;Notice="";return;}
            if(Battle.Connected&&!wasConnected){Recovery.Connected();waitingForOwnClose=false;wasConnected=true;wasConnecting=false;}
            else if(!Battle.Connected&&!Battle.Connecting&&(wasConnected||wasConnecting))Fail(Battle.Failure.Length==0?"connection_closed":Battle.Failure,true);
            if(!AutoReconnect&&Recovery.Pending){Recovery.Stop();retryDue=false;}
            string retry=Recovery.Poll(now);
            if(retry=="expired")Notice="reconnect_expired";
            if(retry=="retry")retryDue=true;
            if(retryDue&&!Busy&&Identity.IsAuthenticated&&Room!=null)if(Begin("retry","/api/room/ticket",new EnterRequest{matchID=attemptedMatch}))retryDue=false;
            if(Content!=null&&Identity.IsAuthenticated&&pending==null&&!Recovery.Pending&&!Battle.Connected&&!Battle.Connecting&&now>=nextRoomPoll){nextRoomPoll=now+1;Begin("poll","/api/room/current",null);}
        }
        public void SceneLoaded()=>Battle.MarkLoaded();
        public void StartRecording(){if(!Battle.Connected)throw new InvalidOperationException("Connect before recording");Recording.Start(Content,Battle.State.Latest);}
        public void LoadPlayback(string text)
        {
            if(Room!=null||Battle.Connected||Battle.Connecting||Busy||Recovery.Pending)throw new InvalidOperationException("Leave the room before playback");
            CancelRequest();
            Playback.Load(text);playbackNow=lastNow;
        }
        public void EndPlayback(){Playback.Clear();nextRoomPoll=lastNow+1;}
        public bool AimAction(string kind,double targetX,double targetZ,double now)
        {
            if(!Battle.CanControl)return false;var own=Battle.State.Latest.players.First(p=>p.playerID==Battle.State.PlayerID);
            Battle.Movement.Position(own,true,out double x,out double z);
            return Battle.QueueAction(kind,targetX-x,targetZ-z,now,(kind=="fire"||kind=="skill")&&Content.Hero(own.heroID)?.attackKind=="ground",targetX,targetZ);
        }
        public void Face(double targetX,double targetZ)
        {
            if(!Battle.CanControl)return;var own=Battle.State.Latest.players.First(p=>p.playerID==Battle.State.PlayerID);
            Battle.Movement.Position(own,true,out double x,out double z);Battle.Face(targetX-x,targetZ-z);
        }
        public void Dispose(){if(disposed)return;disposed=true;CancelRequest();Battle.ValidatedSnapshot-=Recording.Accept;Recording.Stop();Playback.Clear();Battle.Dispose();Recovery.Stop();}
    }
}
