using Rumblebound.Gameplay;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rumblebound.Presentation
{
    // Owns only visual objects. A stopped projectile remains on the delayed
    // rendering timeline until its authoritative contact point has been shown.
    public sealed class ProjectilePresentation : IDisposable
    {
        public const float ArrowHeight = .68f;
        private const int MaxTerminalVisuals = 64;
        private sealed class Visual
        {
            public GameObject actor;
            public Renderer[] renderers;
            public Material[][] defaultMaterials;
            public int[] teamRendererIndices;
            public Vector3 defaultScale;
            public Quaternion defaultRotation;
            public ProjectileState state;
            public ProjectileEnd ending;
            public int contactFrame = -1;
        }
        private readonly Func<GameObject> create;
        private readonly float height;
        private readonly bool preservePrefabScale;
        private readonly Dictionary<string, Visual> active = new Dictionary<string, Visual>();
        private readonly Stack<Visual> available = new Stack<Visual>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly HashSet<string> completed = new HashSet<string>();
        private readonly Dictionary<string, ProjectileEnd> endings = new Dictionary<string, ProjectileEnd>();
        private readonly List<string> expired = new List<string>();
        private ProjectileState[] latest = Array.Empty<ProjectileState>();
        public int ActiveCount => active.Count;
        public int AllocatedCount => active.Count + available.Count;
        public IReadOnlyList<ProjectileState> Latest => latest;
        public event Action<ProjectileEnd> ContactShown;

        public ProjectilePresentation(Func<GameObject> create, float height = .42f, bool preservePrefabScale = false)
        {
            this.create = create;
            this.height = height;
            this.preservePrefabScale = preservePrefabScale;
        }

        public void Accept(ProjectileState[] projectiles, string ownTeam, int capacity, Material friendly, Material enemy,
            ProjectileEnd[] ends = null, double radius = .18)
        {
            latest = projectiles ?? Array.Empty<ProjectileState>();
            capacity = Math.Max(0, capacity);
            if (capacity == 0)
            {
                Clear();
                while (available.Count > 0) Destroy(available.Pop().actor);
                return;
            }
            seen.Clear();
            foreach (var projectile in latest)
                if (projectile != null && !string.IsNullOrEmpty(projectile.id) && seen.Count < capacity) seen.Add(projectile.id);
            endings.Clear();
            if (ends != null)
                foreach (var end in ends)
                    if (end != null && !string.IsNullOrEmpty(end.id) && endings.Count < MaxTerminalVisuals)
                        endings[end.id] = end;
            expired.Clear();
            foreach (string id in completed) if (!endings.ContainsKey(id)) expired.Add(id);
            foreach (string id in expired) completed.Remove(id);

            expired.Clear();
            foreach (var pair in active)
            {
                if (endings.TryGetValue(pair.Key, out var ending)) pair.Value.ending = ending;
                else if (!seen.Contains(pair.Key) && pair.Value.ending == null) expired.Add(pair.Key);
            }
            foreach (string id in expired) Release(id);
            // Several queued snapshots may be consumed before the next render.
            // Keep their terminal backlog bounded independently of live shots.
            int pendingEnds = 0;
            foreach (var visual in active.Values) if (visual.ending != null) pendingEnds++;
            while (pendingEnds > MaxTerminalVisuals)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var pair in active)
                    if (pair.Value.ending != null && (pair.Value.ending.tick < oldestTick ||
                        pair.Value.ending.tick == oldestTick && string.CompareOrdinal(pair.Key, oldest) < 0))
                    { oldest = pair.Key; oldestTick = pair.Value.ending.tick; }
                if (oldest == null) break;
                completed.Add(oldest); Release(oldest); pendingEnds--;
            }
            foreach (var projectile in latest)
            {
                if (projectile == null || string.IsNullOrEmpty(projectile.id) || !seen.Contains(projectile.id) || completed.Contains(projectile.id)) continue;
                Visual visual = Acquire(projectile.id);
                visual.state = projectile;
                if (endings.TryGetValue(projectile.id, out var ending)) visual.ending = ending;
                Style(visual, radius, !string.IsNullOrEmpty(ownTeam) && projectile.teamID == ownTeam ? friendly : enemy);
                Vector3 direction = new Vector3((float)projectile.dx, 0, (float)projectile.dz);
                visual.actor.transform.rotation = direction.sqrMagnitude > .000001f
                    ? Quaternion.LookRotation(direction, Vector3.up) : visual.defaultRotation;
                if (visual.ending == null)
                {
                    visual.actor.SetActive(true);
                    visual.actor.transform.position = new Vector3((float)projectile.x, height, (float)projectile.z);
                }
            }
            foreach (var pair in endings)
            {
                if (completed.Contains(pair.Key) || active.ContainsKey(pair.Key) || active.Count >= capacity + MaxTerminalVisuals) continue;
                // A very short shot may hit between two snapshots. Show its
                // contact once, without inventing an unseen flight segment.
                Visual visual = Acquire(pair.Key);
                visual.ending = pair.Value;
                Style(visual, radius, null);
                visual.actor.SetActive(false);
            }
            int retained = 0;
            foreach (var visual in active.Values) if (visual.ending != null) retained++;
            while (AllocatedCount > capacity + retained && available.Count > 0) Destroy(available.Pop().actor);
        }

        private Visual Acquire(string id)
        {
            if (active.TryGetValue(id, out var visual)) return visual;
            if (available.Count > 0) visual = available.Pop();
            else
            {
                GameObject actor = create();
                Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
                var defaults = new Material[renderers.Length][];
                var teamIndices = new List<int>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    defaults[i] = renderers[i].sharedMaterials;
                    // Imported arrow meshes retain their palette. A small,
                    // explicitly named accent carries friend/enemy color.
                    if (!preservePrefabScale || renderers[i].name == "TeamAccent") teamIndices.Add(i);
                }
                visual = new Visual { actor = actor, renderers = renderers, defaultMaterials = defaults,
                    teamRendererIndices = teamIndices.ToArray(), defaultScale = actor.transform.localScale,
                    defaultRotation = actor.transform.rotation };
            }
            visual.state = null; visual.ending = null; visual.contactFrame = -1;
            visual.actor.transform.localScale = visual.defaultScale;
            visual.actor.transform.rotation = visual.defaultRotation;
            visual.actor.name = "Projectile " + id;
            active.Add(id, visual);
            return visual;
        }

        private void Style(Visual visual, double radius, Material teamMaterial)
        {
            // The arrow is authored at game scale, with its tip at +radius and
            // its tail extending backward. Collision radius must not squash it.
            visual.actor.transform.localScale = preservePrefabScale ? visual.defaultScale
                : new Vector3((float)(2 * radius), .18f, (float)(2 * radius));
            foreach (int index in visual.teamRendererIndices)
            {
                if (teamMaterial == null) visual.renderers[index].sharedMaterials = visual.defaultMaterials[index];
                else visual.renderers[index].sharedMaterial = teamMaterial;
            }
        }

        private void Release(string id)
        {
            Visual visual = active[id]; active.Remove(id);
            visual.actor.SetActive(false);
            visual.state = null; visual.ending = null; visual.contactFrame = -1;
            available.Push(visual);
        }

        public void Render(GameMovement movement) { Render(movement, Time.frameCount); }

        public void Render(GameMovement movement, int frame)
        {
            if (movement == null) return;
            expired.Clear();
            foreach (var pair in active)
            {
                Visual visual = pair.Value;
                if (visual.ending != null && movement.RenderTick >= visual.ending.tick)
                {
                    if (visual.contactFrame >= 0 && frame > visual.contactFrame)
                    { expired.Add(pair.Key); completed.Add(pair.Key); continue; }
                    if (visual.contactFrame < 0)
                    { visual.contactFrame = frame; ContactShown?.Invoke(visual.ending); }
                    visual.actor.SetActive(true);
                    visual.actor.transform.position = new Vector3((float)visual.ending.x, height, (float)visual.ending.z);
                    continue;
                }
                if (visual.state == null) continue;
                movement.ProjectilePosition(visual.state, out double x, out double z, visual.ending);
                visual.actor.SetActive(true);
                visual.actor.transform.position = new Vector3((float)x, height, (float)z);
            }
            foreach (string id in expired) Release(id);
        }

        public void Clear()
        {
            latest = Array.Empty<ProjectileState>();
            foreach (var visual in active.Values) { visual.actor.SetActive(false); available.Push(visual); }
            active.Clear(); seen.Clear(); completed.Clear(); endings.Clear(); expired.Clear();
        }

        public void Dispose()
        {
            Clear();
            ContactShown = null;
            while (available.Count > 0) Destroy(available.Pop().actor);
        }

        private static void Destroy(GameObject actor)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(actor); return; }
#endif
            UnityEngine.Object.Destroy(actor);
        }
    }
}
