using System;
using Realtime.Replication;

namespace Realtime.Examples.Modules
{
    public static class ReplicationLesson
    {
        private sealed class Entity { public string ID, Life; public double X; }
        public static void Main()
        {
            var store = new ReplicaStore<Entity>(p=>p.ID, p=>p.Life, p=>new Entity {ID=p.ID, Life=p.Life, X=p.X});
            var first = new Entity {ID="a", Life="1", X=1};
            Check.That(store.Apply(1, 0, new[] {first, new Entity {ID="b", Life="1"}}), "initial roster");
            first.X = 99; Check.Near(store.Values["a"].X, 1); // Store owns a deep copy.
            Check.That(store.Apply(1, 1, new[] {new Entity {ID="a", Life="2", X=2}}), "new life");
            Check.That(store.Added.Count==1 && store.Removed.Count==2 && store.Values.Count==1, "replacement and departure");
            Check.That(!store.Apply(1, 0, Array.Empty<Entity>()) && store.Values.Count==1, "old snapshot must not erase state");
            Console.WriteLine("replication: manual roster diff -> added=a(new life), removed=a(old life)+b; stale snapshot ignored");
            // Instantiate/remove only after successful Apply; SDK owns no objects.
            store.Clear(); Check.That(store.Values.Count==0, "disconnect clear");
        }
    }
}
