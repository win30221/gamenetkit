using System;
using System.Collections.Generic;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // Validate the complete payload before publishing it or acknowledging a frame.
    public sealed class BattleState
    {
        public Snapshot Latest{get;private set;}
        public string PlayerID{get;private set;}
        public uint Generation{get;private set;}
        public long Tick=>Latest?.tick??-1;
        public bool Joined=>!string.IsNullOrEmpty(PlayerID);
        public bool Playing=>Joined&&Latest.match?.phase=="playing";
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static bool ID(string id)=>!string.IsNullOrEmpty(id)&&id.Length<=128;
        public static void Validate(Snapshot state,GameContent content,string matchID)
        {
            var validator=new BattleState();validator.Apply(state,content,matchID,true);
        }
        public bool Apply(Snapshot state,GameContent content,string matchID)
            =>Apply(state,content,matchID,false);
        private bool Apply(Snapshot state,GameContent content,string matchID,bool observer)
        {
            if(state==null||state.protocol!=content.Rules.protocol||state.content!=content.Rules.content||state.matchID!=matchID||state.mapHash!=content.Config.mapHash||state.generation==0||state.tick<0||state.tick>9007199254740991L||state.inputTick<0||state.inputTick>state.tick||state.actionAck<0||state.players==null||state.players.Length>8)throw new InvalidOperationException("Invalid battle envelope");
            bool welcome=state.type=="welcome",start=state.type=="start",terminal=state.type=="result";
            bool initial=welcome||terminal&&!Joined;
            if(terminal&&state.match?.phase!="ended")throw new InvalidOperationException("Result has no ended match");
            if(!observer&&(welcome&&Joined||!initial&&!Joined)||!welcome&&!start&&!terminal&&state.type!="snapshot")throw new InvalidOperationException("Unexpected battle message");
            if(!observer&&!initial&&state.generation!=Generation)return false;
            if(!observer&&!initial&&!start&&(state.tick<Tick||state.tick==Tick&&(!terminal||Latest.match?.phase=="ended")))return false;
            var ids=new HashSet<string>();var entities=new HashSet<string>();
            foreach(var p in state.players){
                var hero=content.Hero(p?.heroID);
                if(p==null||!ID(p.playerID)||!ID(p.entityID)||!ids.Add(p.playerID)||!entities.Add(p.entityID)||hero==null||!Finite(p.x)||!Finite(p.z)||Math.Abs(p.x)>content.Map.arenaHalfSize-content.Rules.playerRadius+.001||Math.Abs(p.z)>content.Map.arenaHalfSize-content.Rules.playerRadius+.001||p.hp<0||p.hp>hero.maxHP||p.ack<0||p.life<0||p.fireReadyTick<0||p.dashReadyTick<0||p.respawnTick<0||p.attackStopUntilTick<0||p.basicReadyTick<0||p.attackReleaseTick<0||p.attackTargetLife<0||p.basicActionID<0||(!string.IsNullOrEmpty(p.attackTargetID)&&!ID(p.attackTargetID))||!Finite(p.facingX+p.facingZ+p.dashX+p.dashZ))throw new InvalidOperationException("Invalid authoritative player");
            }
            string local=initial?state.playerID:PlayerID;if(!observer&&!ids.Contains(local))throw new InvalidOperationException("Local seat missing");
            if((state.projectiles?.Length??0)>content.Rules.maxProjectiles||(state.groundCasts?.Length??0)>content.Rules.maxActiveGroundCasts||(state.projectileEnds?.Length??0)>256||(state.combatEvents?.Length??0)>256||(state.groundImpacts?.Length??0)>256)throw new InvalidOperationException("Battle effect budget exceeded");
            var effects=new HashSet<string>();
            foreach(var p in state.projectiles??Array.Empty<ProjectileState>())if(p==null||!ID(p.id)||!effects.Add(p.id)||!ID(p.ownerID)||!Finite(p.x+p.z+p.dx+p.dz)||p.expiresTick<0)throw new InvalidOperationException("Invalid projectile");
            foreach(var p in state.groundCasts??Array.Empty<GroundCastState>())if(p==null||!ID(p.id)||!Finite(p.x+p.z+p.radius)||p.radius<=0||p.radius>20||p.startTick<0||p.fallStartTick<p.startTick||p.impactTick<p.fallStartTick)throw new InvalidOperationException("Invalid ground cast");
            foreach(var p in state.projectileEnds??Array.Empty<ProjectileEnd>())if(p==null||!ID(p.id)||!Finite(p.x+p.z)||p.tick<0)throw new InvalidOperationException("Invalid projectile end");
            foreach(var p in state.combatEvents??Array.Empty<CombatEvent>())if(p==null||!ID(p.id)||!Finite(p.x+p.z+p.dx+p.dz)||p.tick<0)throw new InvalidOperationException("Invalid combat event");
            foreach(var p in state.groundImpacts??Array.Empty<GroundImpactState>())if(p==null||!ID(p.id)||!Finite(p.x+p.z+p.radius)||p.radius<=0||p.tick<0||(p.targets?.Length??0)>8)throw new InvalidOperationException("Invalid ground impact");
            if(state.match!=null){
                var m=state.match;if(m.phase!="loading"&&m.phase!="playing"&&m.phase!="ended"||(m.scores?.Length??0)>4||m.endsAtTick<0||m.rotationTick<0)throw new InvalidOperationException("Invalid match state");
                if(m.point!=null&&(!Finite(m.point.x+m.point.z+m.point.radius)||m.point.radius<=0))throw new InvalidOperationException("Invalid control point");
                var teams=new HashSet<string>();foreach(var team in m.scores??Array.Empty<TeamScore>())if(team==null||!ID(team.teamID)||!teams.Add(team.teamID)||team.score<0||team.scoreTicks<0)throw new InvalidOperationException("Invalid score");
                if(m.phase=="ended"){
                    var result=m.result;bool winner=result!=null&&!string.IsNullOrEmpty(result.winnerTeamID);
                    if(result==null||result.endedTick<0||result.endedTick>state.tick||!(result.outcome=="win"&&winner&&teams.Contains(result.winnerTeamID)&&(result.reason=="score"||result.reason=="time"||result.reason=="forfeit")||result.outcome=="draw"&&!winner&&result.reason=="time"||result.outcome=="abort"&&!winner&&result.reason=="no_teams"))throw new InvalidOperationException("Invalid match settlement");
                }
            }
            Latest=state;PlayerID=local;Generation=state.generation;return true;
        }
        public void Clear(){Latest=null;PlayerID=null;Generation=0;}
    }
}
