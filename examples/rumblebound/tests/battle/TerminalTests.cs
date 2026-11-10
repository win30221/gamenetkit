using System;
using System.Threading;
using System.Threading.Tasks;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;

public static class TerminalTests
{
    public static void Run(GameContent content,Snapshot latest,string playerID)
    {
        var json=new GameJson();Snapshot Copy()=>json.Read<Snapshot>(json.Write(latest));
        var seed=Copy();seed.type="welcome";seed.playerID=playerID;
        var state=new BattleState();state.Apply(seed,content,seed.matchID);
        var ended=Copy();ended.type="result";ended.playerID=playerID;ended.match.phase="ended";
        ended.match.result=new MatchResult{outcome="win",reason="forfeit",winnerTeamID=ended.players[0].teamID,endedTick=ended.tick};
        if(!state.Apply(ended,content,ended.matchID)||state.Playing||state.Latest.match.result==null)throw new Exception("Terminal result at current tick was not committed");
        if(state.Apply(ended,content,ended.matchID))throw new Exception("Duplicate result was applied twice");
        var initial=new BattleState();if(!initial.Apply(ended,content,ended.matchID)||!initial.Joined)throw new Exception("Result-only reattachment was rejected");
        var invalid=json.Read<Snapshot>(json.Write(ended));invalid.match.result.winnerTeamID="";
        var untouched=new BattleState();untouched.Apply(seed,content,seed.matchID);
        try{untouched.Apply(invalid,content,invalid.matchID);throw new Exception("Invalid winning result accepted");}catch(InvalidOperationException){}
        if(!ReferenceEquals(untouched.Latest,seed))throw new Exception("Invalid result partially replaced state");
        RetainResult(content,ended);
        Console.WriteLine("REMAKE_TERMINAL PASS same-tick final result, duplicate rejection, result-only reattachment, atomic invalid settlement, retained result and explicit room return");
    }
    private static void RetainResult(GameContent content,Snapshot ended)
    {
        var api=new ResultFixtureApi(content.Config,ended.playerID);
        using var flow=new GameFlow(api,new GameJson(),"ws://127.0.0.1/unused-result-fixture");
        flow.Pump(0,0,0);flow.Restore("fixture-credential",ended.playerID);
        if(!flow.Create())throw new Exception("Result fixture could not enter room");flow.Pump(1,0,0);
        flow.Battle.State.Apply(ended,content,ended.matchID);int requests=api.Calls;
        flow.Pump(10,0,0);flow.Pump(30,0,0);
        if(flow.Stage!="result"||flow.Battle.State.Latest?.match?.result==null||flow.Recovery.Pending||api.Calls!=requests)throw new Exception("Completed match was timed out, polled or reconnected instead of retaining its result");
        if(!flow.ReturnToRoom())throw new Exception("Completed result cannot return to room");flow.Pump(31,0,0);
        if(flow.Stage!="room"||flow.Battle.State.Joined)throw new Exception("Explicit room return did not clear the result");
    }
    // Controlled client lifecycle regression; real natural-end browser playback
    // is verified separately against the unmodified 180-second game rules.
    private sealed class ResultFixtureApi:IGameApi
    {
        private readonly LabConfig config;private readonly string user;
        public int Calls;
        public ResultFixtureApi(LabConfig config,string user){this.config=config;this.user=user;}
        public Task<ApiReply> Request(string path,string body,string credential,CancellationToken cancellation)
        {
            Calls++;var json=new GameJson();
            var room=new GameRoom{code="result-fixture",hostID=user,youID=user,phase="preparing",teams=new[]{new RoomTeam{teamID="t1",capacity=4},new RoomTeam{teamID="t2",capacity=4}},members=new[]{new RoomMember{userID=user,teamID="t1",heroID="ranger"}}};
            return Task.FromResult(new ApiReply{Status=200,Body=path=="/api/config"?json.Write(config):json.Write(room)});
        }
    }
}
