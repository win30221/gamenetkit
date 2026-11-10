using System;
using System.Collections.Generic;
using Realtime.Core;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // Keeps sampled views stable between snapshots. A new life gets a new view
    // even when it reuses a player ID; scene presenters can reset that instance.
    public sealed class ActorRoster
    {
        private readonly Dictionary<string,ActorView> actors=new Dictionary<string,ActorView>();
        private readonly HashSet<string> present=new HashSet<string>();
        private readonly List<string> removed=new List<string>();
        private long incarnation;
        public ActorView[] Views{get;private set;}=Array.Empty<ActorView>();

        private bool Set(PlayerState player,bool replace=false)
        {
            if(!replace&&actors.TryGetValue(player.playerID,out var view)&&view.state.entityID==player.entityID&&view.state.life==player.life&&view.state.heroID==player.heroID){view.state=player;return false;}
            actors[player.playerID]=new ActorView{state=player,incarnation=++incarnation};return true;
        }
        private void Publish(bool changed)
        {
            if(!changed)return;
            var next=new ActorView[actors.Count];actors.Values.CopyTo(next,0);Views=next;
        }
        public void ApplyReplicas(IReplicaStore<PlayerState> store)
        {
            bool changed=false;
            foreach(string id in store.Removed)changed|=actors.Remove(id);
            foreach(string id in store.Added)changed|=Set(store.Values[id],true);
            foreach(string id in store.Updated)changed|=Set(store.Values[id]);
            Publish(changed);
        }
        // Replay, terminal/same-tick messages and optional-package removal use
        // the same view lifecycle, without building a new set each render frame.
        public void ApplySnapshot(PlayerState[] players)
        {
            present.Clear();bool changed=false;
            foreach(var player in players){present.Add(player.playerID);changed|=Set(player);}
            removed.Clear();foreach(string id in actors.Keys)if(!present.Contains(id))removed.Add(id);
            foreach(string id in removed)changed|=actors.Remove(id);
            Publish(changed);
        }
        public void Clear(){actors.Clear();present.Clear();removed.Clear();Views=Array.Empty<ActorView>();}
    }
}
