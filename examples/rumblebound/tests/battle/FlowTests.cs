using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rumblebound.Example.Battle;
using Rumblebound.Example.Hosting;
using Rumblebound.Gameplay;

public static class FlowTests
{
    private static void Require(bool value,string why){if(!value)throw new Exception(why);}
    public static async Task Run(string origin)
    {
        var json=new GameJson();using var firstApi=new HttpGameApi(origin);using var secondApi=new HttpGameApi(origin);
        string endpoint=origin.Replace("https:","wss:").Replace("http:","ws:")+"/ws";
        using var one=new GameFlow(firstApi,json,endpoint);using var two=new GameFlow(secondApi,json,endpoint);
        var timer=Stopwatch.StartNew();double Now()=>timer.Elapsed.TotalSeconds;
        GameProjection projection=null;
        async Task Until(Func<bool> predicate,string stage){double end=Now()+10;while(!predicate()){Require(Now()<end,stage+": "+one.Notice+" / "+two.Notice+" "+one.Battle.Status+" / "+two.Battle.Status);one.Pump(Now(),0,0);two.Pump(Now(),0,0);projection?.Update(one,Now());await Task.Delay(8);}}
        await Until(()=>one.Content!=null&&two.Content!=null&&!one.Busy&&!two.Busy,"configuration");
        Require(one.Guest()&&two.Guest(),"guest command rejected");await Until(()=>one.Identity.IsAuthenticated&&two.Identity.IsAuthenticated&&!one.Busy&&!two.Busy,"guests");
        string user=one.Identity.UserID,credential=one.Identity.Credential;
        Require(one.Create(),"create rejected");await Until(()=>one.Room!=null&&!one.Busy,"create room");
        Require(two.Join(one.Room.code),"join rejected");await Until(()=>two.Room!=null&&!two.Busy,"join room");
        Require(one.Team(one.Room.teams[0].teamID)&&two.Team(two.Room.teams[1].teamID),"team selection rejected");await Until(()=>!one.Busy&&!two.Busy,"teams");
        string home=one.Room.teams[0].teamID,away=one.Room.teams[1].teamID;
        Require(one.Capacity(away,2),"host capacity rejected");await Until(()=>one.Room.teams.First(t=>t.teamID==away).capacity==2&&!one.Busy&&!two.Busy,"host capacity reply");
        Require(one.Assign(two.Identity.UserID,home),"host assignment rejected");await Until(()=>one.Room.members.First(m=>m.userID==two.Identity.UserID).teamID==home&&!one.Busy&&!two.Busy,"host assignment reply");
        Require(one.Assign(two.Identity.UserID,away),"host reassignment rejected");await Until(()=>one.Room.members.First(m=>m.userID==two.Identity.UserID).teamID==away&&!one.Busy&&!two.Busy,"host reassignment reply");
        Require(one.Hero("ranger")&&two.Hero("tank"),"hero selection rejected");await Until(()=>!one.Busy&&!two.Busy,"heroes");
        Require(one.Ready(true)&&two.Ready(true),"ready rejected");await Until(()=>one.Room.canStart&&!one.Busy&&!two.Busy,"all ready");
        Require(one.Start(),"start rejected");await Until(()=>one.Battle.Connected&&two.Battle.Connected,"automatic tickets");
        one.SceneLoaded();two.SceneLoaded();await Until(()=>one.Battle.CanControl&&two.Battle.CanControl,"loaded battle");
        // Bind after welcome/start, as a presenter created after scene loading
        // would, then deliver several real snapshots before the next render.
        projection=new GameProjection();
        await ProjectionSnapshots(one,two,projection,Now);
        var shooter=one.Battle.State.Latest.players.First(p=>p.playerID==one.Battle.State.PlayerID);
        var shotViews=projection.View.projectiles;
        var aim=new[]{(X:1.0,Z:0.0),(X:-1.0,Z:0.0),(X:0.0,Z:1.0),(X:0.0,Z:-1.0)}.First(d=>ArenaNavigation.Clear(one.Content.Map,shooter.x,shooter.z,shooter.x+d.X*6,shooter.z+d.Z*6,one.Content.Rules.projectileRadius));
        Require(one.AimAction("fire",shooter.x+aim.X*6,shooter.z+aim.Z*6,Now()),"world-space ranged aim rejected locally");await Until(()=>one.Battle.LastActionCode=="fired"&&projection.View.projectiles.Count>0,"non-ground world-space aim accepted by authority");
        Require(ReferenceEquals(shotViews,projection.View.projectiles),"projectile spawn replaced the reusable view list");
        using(var encoded=System.Text.Json.JsonDocument.Parse(json.Write(projection.View)))Require(encoded.RootElement.GetProperty("projectiles").GetArrayLength()==projection.View.projectiles.Count,"projectile JSON included unused buffer capacity");
        await Until(()=>projection.View.projectiles.Count==0,"projectile terminal presentation");
        Require(ReferenceEquals(shotViews,projection.View.projectiles),"projectile removal replaced the reusable view list");
        using(var encoded=System.Text.Json.JsonDocument.Parse(json.Write(projection.View)))Require(encoded.RootElement.GetProperty("projectiles").GetArrayLength()==0,"projectile JSON retained a removed view");
        string player=one.Battle.State.PlayerID;uint generation=one.Battle.State.Generation;
        var priorRoster=projection.View.players;var priorActor=priorRoster.First(p=>p.own);var priorState=priorActor.state;
        one.Battle.Transport.InterruptForDiagnostics();await Until(()=>one.Recovery.Pending,"reconnect scheduled");
        Require(projection.View.frozen&&ReferenceEquals(priorRoster,projection.View.players),"projection did not preserve the last scene roster during recovery");
        var retiredState=priorActor.state;
        await Until(()=>one.Battle.Connected&&one.Battle.State.Generation>generation,"automatic identity rejoin");
        var rejoined=projection.Update(one,Now());var rejoinedActor=rejoined.players.First(p=>p.own);var rejoinedRoster=rejoined.players;
        Require(!rejoined.frozen&&rejoined.generation==one.Battle.State.Generation&&!ReferenceEquals(priorRoster,rejoinedRoster)&&!ReferenceEquals(priorActor,rejoinedActor)&&priorActor.incarnation!=rejoinedActor.incarnation,"new connection generation reused a stale scene lifetime");
        Require(rejoinedActor.state.playerID==priorState.playerID&&rejoinedActor.state.entityID==priorState.entityID&&ReferenceEquals(priorActor.state,retiredState),"projection recovery replaced the authority seat or mutated a retired view");
        Require(one.Identity.UserID==user&&one.Identity.Credential==credential&&one.Battle.State.PlayerID==player,"recovery replaced identity/seat");one.SceneLoaded();
        await Until(()=>one.Battle.CanControl,"rejoin scene");
        Require(ReferenceEquals(rejoinedRoster,projection.View.players)&&ReferenceEquals(rejoinedActor,projection.View.players.First(p=>p.own))&&projection.View.snapshot==one.Battle.State.Latest,"rejoin scene loading rebuilt an unchanged roster or left its snapshot stale");
        one.Battle.Transport.InterruptForDiagnostics();await Until(()=>one.Recovery.Pending,"second outage");one.AutoReconnect=false;
        double wait=Now();while(Now()-wait<1){one.Pump(Now(),0,0);two.Pump(Now(),0,0);await Task.Delay(8);}
        Require(!one.Recovery.Pending&&!one.Battle.Connected,"disabling recovery did not cancel retries");
        await Until(()=>!one.Busy,"manual rejoin readiness");Require(one.Enter(),"manual rejoin rejected");await Until(()=>one.Battle.Connected,"manual rejoin");one.SceneLoaded();
        await Until(()=>one.Battle.CanControl&&!one.Busy,"manual rejoin loaded");Require(!one.Leave()&&one.Battle.Connected,"live private match bypassed frozen roster");
        one.AutoReconnect=true;uint beforeStall=one.Battle.State.Generation;
        double stall=Now();while(Now()-stall<5){two.Pump(Now(),0,0);await Task.Delay(10);}
        one.Pump(Now(),0,0);Require(one.Recovery.Pending&&one.Notice=="receive_overflow","stalled receive did not enter recovery");
        await Until(()=>one.Battle.Connected&&one.Battle.State.Generation!=beforeStall,"receive overflow reconnect");one.SceneLoaded();
        await Until(()=>one.Battle.CanControl&&!one.Busy,"receive overflow loaded");Require(one.Identity.UserID==user,"overflow recovery changed identity");
        Require(one.Logout(),"private logout rejected");await Until(()=>!one.Identity.IsAuthenticated&&!one.Busy,"private logout");
        Require(one.Guest(),"new guest rejected");await Until(()=>one.Identity.IsAuthenticated&&!one.Busy,"new guest");
        Require(one.Practice("mage","ranger","idle"),"practice rejected");await Until(()=>one.Battle.Connected,"practice ticket");one.SceneLoaded();await Until(()=>one.Battle.CanControl&&!one.Busy,"practice loading");
        Require(one.AimAction("skill",0,0,Now()),"mage skill rejected locally");await Until(()=>one.Battle.LastActionCode=="cast","mage authoritative ground skill");
        Require(one.PracticeMode("capture+attack+dodge+kite"),"practice mode rejected");await Until(()=>one.Room.botMode=="capture+attack+dodge+kite"&&!one.Busy,"practice behavior");
        Require(one.Leave(),"practice leave rejected");await Until(()=>one.Room==null&&!one.Busy,"practice stopped");
        Require(one.Logout(),"logout rejected");await Until(()=>!one.Identity.IsAuthenticated&&!one.Busy,"logout");
        await RevisionIsolation(json,one.Content,endpoint);
        BackgroundPoll(json,one.Content,endpoint);
        Console.WriteLine("REMAKE_FLOW PASS shared C# flow: config/login/rooms/team/hero/ready/automatic ticket/loaded, real snapshot projection/view reuse and generation recovery, same identity reconnect, actual stalled receive overflow/recovery, cancellation/manual rejoin, mage practice/AI mode/leave/logout, and obsolete HTTP completion isolation");
    }
    private static async Task ProjectionSnapshots(GameFlow one,GameFlow two,GameProjection projection,Func<double> now)
    {
        var view=projection.Update(one,now());var roster=view.players;var own=roster.First(p=>p.own);var peer=roster.First(p=>!p.own);
        var initialSnapshot=view.snapshot;var initialState=own.state;double startX=initialState.x,startZ=initialState.z;long startTick=view.tick;
        Require(roster.Length==one.Battle.State.Latest.players.Length&&roster.All(p=>p.incarnation>0),"late projection binding did not seed the complete loaded roster");
        projection.Update(one,now());Require(ReferenceEquals(roster,projection.View.players)&&ReferenceEquals(own,projection.View.players.First(p=>p.own)),"render without a new snapshot allocated another actor roster");
        // Pick a clear short path from the real spawn; movement must reach the
        // server and the event-fed view, rather than only local prediction.
        var candidates=new[]{(X:1.0,Z:0.0),(X:-1.0,Z:0.0),(X:0.0,Z:1.0),(X:0.0,Z:-1.0)};
        var drive=candidates.First(d=>ArenaNavigation.Clear(one.Content.Map,startX,startZ,startX+d.X*1.5,startZ+d.Z*1.5,one.Content.Rules.playerRadius));
        int applied=0;void Observe(Realtime.Core.IReplicaStore<PlayerState> store){applied++;}
        one.Battle.ReplicasApplied+=Observe;
        try{
            double deadline=now()+3;
            while(one.Battle.State.Tick<startTick+8||Math.Abs(one.Battle.State.Latest.players.First(p=>p.playerID==one.Battle.State.PlayerID).x-startX)+Math.Abs(one.Battle.State.Latest.players.First(p=>p.playerID==one.Battle.State.PlayerID).z-startZ)<.15){
                Require(now()<deadline,"real movement snapshots did not reach projection test");
                one.Pump(now(),drive.X,drive.Z);two.Pump(now(),0,0);await Task.Delay(8);
            }
            one.Pump(now(),0,0);two.Pump(now(),0,0);
            if(one.Battle.Replicas!=null){
                Require(applied>=2&&!ReferenceEquals(initialState,own.state)&&ReferenceEquals(own.state,one.Battle.Replicas.Values[one.Battle.State.PlayerID]),"multiple real replica applications did not update the already-published actor before rendering");
                Require(Math.Abs(own.state.x-startX)+Math.Abs(own.state.z-startZ)>.15,"replicated actor pose stayed at the initial spawn");
            }
            view=projection.Update(one,now());
            Require(ReferenceEquals(roster,view.players)&&ReferenceEquals(own,view.players.First(p=>p.own))&&ReferenceEquals(peer,view.players.First(p=>!p.own)),"same-life snapshots recreated scene actor views or their roster array");
            Require(view.snapshot==one.Battle.State.Latest&&!ReferenceEquals(initialSnapshot,view.snapshot)&&view.tick>=startTick+8,"projection did not publish the latest real authority snapshot");
            double moved=Math.Abs(own.x-startX)+Math.Abs(own.z-startZ);
            Require(moved>.15&&moved<3&&!double.IsNaN(own.x+own.z+peer.x+peer.z),"sampled live actor pose did not follow real movement");
        }finally{one.Battle.ReplicasApplied-=Observe;}
    }
    private sealed class HeldApi:IGameApi
    {
        public readonly Queue<TaskCompletionSource<ApiReply>> Pending=new Queue<TaskCompletionSource<ApiReply>>();
        public readonly Queue<CancellationToken> Tokens=new Queue<CancellationToken>();
        public Task<ApiReply> Request(string path,string body,string credential,CancellationToken token){var source=new TaskCompletionSource<ApiReply>();Pending.Enqueue(source);Tokens.Enqueue(token);return source.Task;}
    }
    private static void BackgroundPoll(GameJson json,GameContent content,string endpoint)
    {
        var api=new HeldApi();using var flow=new GameFlow(api,json,endpoint);
        api.Pending.Dequeue().SetResult(new ApiReply{Status=200,Body=json.Write(content.Config)});api.Tokens.Dequeue();flow.Pump(0,0,0);
        Require(flow.Restore("poll-credential","poll-user"),"poll fixture login");flow.Pump(.1,0,0);
        var poll=api.Pending.Dequeue();var token=api.Tokens.Dequeue();
        Require(!flow.Busy,"background room poll disabled user controls");
        flow.Pump(3,0,0);Require(api.Pending.Count==0&&!token.IsCancellationRequested,"slow background poll was restarted before completion");
        Require(flow.Create()&&flow.Busy&&token.IsCancellationRequested,"create did not replace a slow background poll");
        var create=api.Pending.Dequeue();api.Tokens.Dequeue();
        poll.SetResult(new ApiReply{Status=401,Body="{}"});flow.Pump(3.1,0,0);
        Require(flow.Identity.IsAuthenticated&&flow.Busy,"obsolete poll changed the active command");
        create.SetResult(new ApiReply{Status=200,Body=json.Write(new GameRoom{code="poll-room",youID="poll-user",hostID="poll-user",teams=new[]{new RoomTeam{teamID="t1",capacity=1},new RoomTeam{teamID="t2",capacity=1}},members=new[]{new RoomMember{userID="poll-user",teamID="t1"}}})});flow.Pump(3.2,0,0);
        Require(flow.Room?.code=="poll-room"&&!flow.Busy,"user command reply lost to a canceled poll");
        flow.Pump(4.3,0,0);api.Pending.Dequeue();token=api.Tokens.Dequeue();
        Require(!flow.Busy&&flow.Leave()&&flow.Busy&&token.IsCancellationRequested,"leave was blocked by background polling");
        api.Pending.Dequeue().SetResult(new ApiReply{Status=404,Body="{\"error\":\"not_in_room\"}"});api.Tokens.Dequeue();flow.Pump(4.4,0,0);
        Require(flow.Room==null&&!flow.Busy&&flow.Notice=="","leaving an expired room retained stale room state or an error");
        flow.Pump(5.4,0,0);api.Pending.Dequeue();token=api.Tokens.Dequeue();
        Require(!flow.Busy&&flow.Logout()&&flow.Busy&&token.IsCancellationRequested,"logout was blocked by background polling");
    }
    private static Task RevisionIsolation(GameJson json,GameContent content,string endpoint)
    {
        var api=new HeldApi();using var flow=new GameFlow(api,json,endpoint);
        api.Pending.Dequeue().SetResult(new ApiReply{Status=200,Body=json.Write(content.Config)});flow.Pump(0,0,0);Require(flow.Guest(),"held guest rejected");
        var old=api.Pending.Dequeue();Require(flow.Restore("new-credential","new-user"),"replacement login rejected");
        old.SetResult(new ApiReply{Status=200,Body="{\"session\":\"old-credential\",\"userID\":\"old-user\"}"});flow.Pump(.1,0,0);
        Require(flow.Identity.Credential=="new-credential"&&flow.Identity.UserID=="new-user","late HTTP completion replaced active login");return Task.CompletedTask;
    }
}
