using System;
using System.Collections.Generic;
using Realtime.Core;
using Rumblebound.Example.Battle;
using Rumblebound.Gameplay;

public static class PresentationStateTests
{
    private sealed class Store:IReplicaStore<PlayerState>
    {
        public readonly Dictionary<string,PlayerState> Items=new Dictionary<string,PlayerState>();
        public IReadOnlyDictionary<string,PlayerState> Values=>Items;
        public IReadOnlyList<string> Added{get;set;}=Array.Empty<string>();
        public IReadOnlyList<string> Updated{get;set;}=Array.Empty<string>();
        public IReadOnlyList<string> Removed{get;set;}=Array.Empty<string>();
        public bool Apply(uint generation,long tick,PlayerState[] values)=>throw new NotSupportedException();
        public void Clear()=>Items.Clear();
        public void Changes(string[] added,string[] updated,string[] removed){Added=added;Updated=updated;Removed=removed;}
    }
    private static void Require(bool value,string reason){if(!value)throw new Exception("Presentation state: "+reason);}
    private static PlayerState Player(string id,long life=1,string hero="ranger",double x=0)=>new PlayerState{playerID=id,entityID="entity-"+id,life=life,heroID=hero,x=x};
    private static ActorView Find(ActorRoster roster,string id)=>Array.Find(roster.Views,p=>p.state.playerID==id);
    public static void Run()
    {
        var store=new Store();var roster=new ActorRoster();
        store.Items["a"]=Player("a");store.Items["b"]=Player("b");store.Changes(new[]{"a","b"},Array.Empty<string>(),Array.Empty<string>());roster.ApplyReplicas(store);
        var originalArray=roster.Views;var a=Find(roster,"a");var b=Find(roster,"b");
        Require(originalArray.Length==2&&a.incarnation!=b.incarnation,"replica additions need distinct scene lifetimes");
        store.Items["a"]=Player("a",x:4);store.Changes(Array.Empty<string>(),new[]{"a","b"},Array.Empty<string>());roster.ApplyReplicas(store);
        Require(ReferenceEquals(originalArray,roster.Views)&&ReferenceEquals(a,Find(roster,"a"))&&a.state.x==4,"position updates must reuse the view and roster array");
        store.Items.Remove("b");store.Items["c"]=Player("c");store.Changes(new[]{"c"},new[]{"a"},new[]{"b"});roster.ApplyReplicas(store);
        Require(Find(roster,"b")==null&&Find(roster,"c")!=null&&ReferenceEquals(a,Find(roster,"a")),"replica removals/additions must affect only their scene instances");
        // A remove+add may arrive between two render frames, including reuse of
        // an identical life token. Consuming only the final Values loses it.
        store.Items["a"]=Player("a");store.Changes(new[]{"a"},new[]{"c"},new[]{"a"});roster.ApplyReplicas(store);
        Require(!ReferenceEquals(a,Find(roster,"a"))&&a.incarnation!=Find(roster,"a").incarnation,"remove+add lifecycle must survive a coalesced render");
        a=Find(roster,"a");store.Items.Remove("a");store.Changes(Array.Empty<string>(),new[]{"c"},new[]{"a"});roster.ApplyReplicas(store);
        store.Items["a"]=Player("a");store.Changes(new[]{"a"},new[]{"c"},Array.Empty<string>());roster.ApplyReplicas(store);
        Require(a.incarnation!=Find(roster,"a").incarnation,"multiple snapshots before rendering must retain the intervening removal");
        var c=Find(roster,"c");store.Items["c"]=Player("c",hero:"mage");store.Changes(Array.Empty<string>(),new[]{"a","c"},Array.Empty<string>());roster.ApplyReplicas(store);
        Require(!ReferenceEquals(c,Find(roster,"c")),"hero replacement must reset its scene instance even with unchanged life");
        roster.Clear();store.Changes(Array.Empty<string>(),new[]{"a","c"},Array.Empty<string>());roster.ApplyReplicas(store);
        Require(roster.Views.Length==2,"binding after initial replication must restore views from updated entries");

        roster.Clear();roster.ApplySnapshot(new[]{Player("a"),Player("b")});a=Find(roster,"a");originalArray=roster.Views;
        roster.ApplySnapshot(new[]{Player("b",x:3),Player("a",x:2)});
        Require(ReferenceEquals(originalArray,roster.Views)&&ReferenceEquals(a,Find(roster,"a"))&&a.state.x==2,"replay/optional fallback must preserve same-life views independent of wire order");
        roster.ApplySnapshot(new[]{Player("a",life:2)});
        Require(roster.Views.Length==1&&Find(roster,"b")==null&&!ReferenceEquals(a,Find(roster,"a")),"fallback must handle new life and removal");
        long prior=Find(roster,"a").incarnation;roster.Clear();roster.ApplySnapshot(new[]{Player("a",life:2)});
        Require(Find(roster,"a").incarnation!=prior,"generation/replay seek reset must create a fresh scene lifetime");
        Console.WriteLine("REMAKE_PRESENTATION_STATE PASS replica deltas, coalesced remove/add, stable same-life views/arrays, hero replacement and replay/optional/reset lifecycles");
    }
}
