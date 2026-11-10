using System;
using System.Collections.Generic;
using Realtime.Core;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    [Serializable] public sealed class ActorView{public PlayerState state;public double x,z;public bool own;public long incarnation;}
    [Serializable] public sealed class ShotView{public ProjectileState state;public double x,z;}
    [Serializable] public sealed class ActionView{public long id;public string kind,status;public double x,z,dx,dz,radius;public double until;}
    [Serializable] public sealed class WorldView
    {
        public string playerID;
        public uint generation;
        public long tick;
        public double renderTick,correction,rtt;
        public bool frozen;
        public ActorView[] players=Array.Empty<ActorView>();
        public IReadOnlyList<ShotView> projectiles=Array.Empty<ShotView>();
        public Snapshot snapshot;
        public ActionView[] actions=Array.Empty<ActionView>();
    }
    // The browser and Unity consume identical sampled poses. Cached presentation
    // freezes during recovery; it cannot submit movement or rewind the world.
    public sealed class GameProjection
    {
        private readonly ActorRoster roster=new ActorRoster();
        private readonly Dictionary<string,ShotView> shots=new Dictionary<string,ShotView>();
        private readonly List<ShotView> visibleShots=new List<ShotView>();
        private readonly Dictionary<string,ProjectileEnd> ends=new Dictionary<string,ProjectileEnd>();
        private readonly HashSet<string> activeShots=new HashSet<string>();
        private readonly List<string> removed=new List<string>();
        private Snapshot rosterSnapshot,shotSnapshot;
        private uint generation;
        private string match;
        private GameFlow bound;
        private readonly List<ActionView> actions=new List<ActionView>();
        private long actionRevision,publishedActions=-1;
        private double now;
        private void Preview(long id,string kind,double dx,double dz,bool ground,double tx,double tz)
        {
            var session=bound.Battle;var p=Array.Find(session.State.Latest.players,p=>p.playerID==session.State.PlayerID);session.Movement.Position(p,true,out double x,out double z);
            var hero=bound.Content.Hero(p.heroID);
            if(kind=="cancel")return;
            if(actions.Count>=8)actions.RemoveAt(0);
            actions.Add(new ActionView{id=id,kind=kind=="attack"?"basic":kind=="dash"?"dash":ground?"ground":hero.attackKind,status="pending",x=ground?tx:x,z=ground?tz:z,dx=dx,dz=dz,radius=ground?hero.impactRadius:hero.attackKind=="melee"?hero.meleeRange:.8,until=now+.4});actionRevision++;
        }
        private void Resolve(long id,bool accepted){var cue=actions.Find(c=>c.id==id);if(cue!=null){cue.status=accepted?"confirmed":"rejected";cue.until=now+(accepted ? .15 : .08);}}
        public WorldView View{get;private set;}=new WorldView();
        private void Identity(Snapshot state)
        {
            if(generation==state.generation&&match==state.matchID)return;
            roster.Clear();rosterSnapshot=shotSnapshot=null;shots.Clear();visibleShots.Clear();ends.Clear();activeShots.Clear();actions.Clear();actionRevision++;generation=state.generation;match=state.matchID;
        }
        private void ReplicasApplied(IReplicaStore<PlayerState> store)
        {
            var state=bound.Battle.State.Latest;if(state==null)return;
            Identity(state);roster.ApplyReplicas(store);rosterSnapshot=state;
        }
        public WorldView Update(GameFlow flow,double time=0)
        {
            now=time;if(!ReferenceEquals(bound,flow)){if(bound!=null){bound.Battle.PreviewAction-=Preview;bound.Battle.ResolveAction-=Resolve;bound.Battle.ReplicasApplied-=ReplicasApplied;}Clear();bound=flow;flow.Battle.PreviewAction+=Preview;flow.Battle.ResolveAction+=Resolve;flow.Battle.ReplicasApplied+=ReplicasApplied;}
            if(actions.RemoveAll(c=>c.until<=now)>0)actionRevision++;
            var session=flow.Battle;var state=session.State.Latest;
            if(state==null){
                if(actions.Count>0){actions.Clear();actionRevision++;}View.actions=Array.Empty<ActionView>();publishedActions=-1;
                if(flow.Identity.IsAuthenticated&&flow.Room?.started==true){View.frozen=true;return View;}
                Clear();return View;
            }
            Identity(state);
            if(!ReferenceEquals(rosterSnapshot,state)){roster.ApplySnapshot(state.players);rosterSnapshot=state;}
            var actors=roster.Views;
            for(int i=0;i<actors.Length;i++){
                var actor=actors[i];var p=actor.state;actor.own=p.playerID==session.State.PlayerID;session.Movement.Position(p,actor.own,out actor.x,out actor.z);
            }
            if(!ReferenceEquals(shotSnapshot,state)){
                activeShots.Clear();
                foreach(var p in state.projectiles??Array.Empty<ProjectileState>()){if(!shots.TryGetValue(p.id,out var shot))shots[p.id]=shot=new ShotView();shot.state=p;activeShots.Add(p.id);}
                // Terminal events for shots never seen by this recipient need no cache.
                foreach(var e in state.projectileEnds??Array.Empty<ProjectileEnd>())if(shots.ContainsKey(e.id))ends[e.id]=e;
                shotSnapshot=state;
            }
            removed.Clear();
            foreach(var item in shots){
                ends.TryGetValue(item.Key,out var end);
                if(!activeShots.Contains(item.Key)&&(end==null||session.Movement.RenderTick>=end.tick)){removed.Add(item.Key);continue;}
                session.Movement.ProjectilePosition(item.Value.state,out item.Value.x,out item.Value.z,end);
            }
            foreach(string id in removed){shots.Remove(id);ends.Remove(id);}
            visibleShots.Clear();foreach(var shot in shots.Values)visibleShots.Add(shot);View.projectiles=visibleShots;
            if(publishedActions!=actionRevision){View.actions=actions.ToArray();publishedActions=actionRevision;}
            View.playerID=session.State.PlayerID;View.generation=state.generation;View.tick=state.tick;View.renderTick=session.Movement.RenderTick;View.correction=session.Movement.Correction;View.rtt=session.RTTSeconds;View.players=actors;View.snapshot=state;View.frozen=false;return View;
        }
        public void Clear(){View=new WorldView();roster.Clear();rosterSnapshot=shotSnapshot=null;shots.Clear();visibleShots.Clear();ends.Clear();activeShots.Clear();actions.Clear();actionRevision++;publishedActions=-1;generation=0;match=null;}
    }
}
