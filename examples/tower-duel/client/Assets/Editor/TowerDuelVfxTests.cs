using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TowerDuel.Editor
{
    public static class TowerDuelVfxTests
    {
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static readonly string[] Kinds = { "Hit", "HeavyHit", "Slash", "Explosion", "Collapse", "Death" };
        public static string Run()
        {
            Require(Application.isPlaying, "Run VFX tests in an empty Play Mode scene.");
            foreach (string name in new[] { "Hit", "HeavyHit", "Slash", "Explosion", "Collapse", "Death", "Frost", "Fireball" })
            {
                var prefab = DuelFeedbackPool.Load(name);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing script in " + name);
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Require(renderer.sharedMaterial, "Missing main VFX material in " + name + "/" + renderer.name);
                    foreach (var material in renderer.sharedMaterials)
                        if(material)Require(material.shader && !ShaderUtil.ShaderHasError(material.shader), "Broken VFX shader in " + name + "/" + material.name);
                    var system=renderer.GetComponent<ParticleSystem>();
                    if(system&&system.trails.enabled)Require(((ParticleSystemRenderer)renderer).trailMaterial, "Missing enabled particle trail material in " + name);
                }
            }
            var root = new GameObject("VFX regression");
            try
            {
                using (var effect = new DuelParticleEffect(DuelFeedbackPool.Load("Hit"), root.transform, 42))
                using (var reference = new DuelParticleEffect(DuelFeedbackPool.Load("Hit"), root.transform, 42))
                {
                    effect.Present(.12f);
                    Require(effect.ParticleCount > 0, "The reused hit emitted no particles.");
                    var before = Snapshot(effect.Root);
                    effect.Present(.12f);
                    Same(before, Snapshot(effect.Root), "Repeated render tick advanced particles.");
                    effect.Present(.22f); effect.Present(.08f); reference.Present(.08f);
                    Same(Snapshot(effect.Root), Snapshot(reference.Root), "Seeking backwards did not restart with the stable seed.");
                    effect.Clear(); Require(!effect.Active && effect.ParticleCount == 0, "Clearing a reused effect left particles behind.");
                }
                using (var pool = new DuelFeedbackPool(root.transform))
                {
                    // Saturate every family, then verify that replay reuses the same instances.
                    for (int i = 0; i < 120; i++) foreach (string kind in Kinds) pool.Play(kind, Vector3.zero, Vector3.forward, 1, 100, 99);
                    Require(pool.AllocatedCount == DuelFeedbackPool.Capacity, "Feedback did not respect its per-family pool budget.");
                    Require(pool.ParticleCount == 0, "A scheduled hit played before its render tick.");
                    var instances = DirectChildren(root.transform);
                    for (int i = 0; i < 20; i++) foreach (string kind in Kinds) pool.Play(kind, Vector3.zero, Vector3.forward, 1, 100, 99);
                    Require(instances.SetEquals(DirectChildren(root.transform)), "A warmed feedback pool allocated new actors.");
                    pool.Update(104); Require(pool.ParticleCount > 0, "Scheduled feedback did not play at the render tick.");
                    pool.Update(200); Require(pool.ActiveCount == 0 && pool.ParticleCount == 0, "Expired feedback survived.");
                    pool.Play("Explosion", Vector3.zero, Vector3.zero, 1, 200, 201, 203);
                    pool.Update(203);Require(pool.ActiveCount==0&&pool.ParticleCount==0,"Spell particles survived their authoritative end tick.");
                    pool.Play("Explosion", Vector3.zero, Vector3.zero, 1, 210, 215);
                    pool.Reset(); Require(pool.ActiveCount == 0 && pool.ParticleCount == 0, "Match reset retained feedback.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return "TOWER_DUEL_VFX_TESTS PASS: reused shaders/prefabs, stable render clock, rewind, bounded pooling, expiry and reset";
        }
        private static List<Vector3> Snapshot(Transform root)
        {
            var positions = new List<Vector3>();
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var particles = new ParticleSystem.Particle[system.main.maxParticles];
                int count = system.GetParticles(particles);
                for (int i = 0; i < count; i++) positions.Add(particles[i].position);
            }
            return positions;
        }
        private static void Same(List<Vector3> a, List<Vector3> b, string message)
        {
            Require(a.Count == b.Count, message);
            for (int i = 0; i < a.Count; i++) Require(Vector3.Distance(a[i], b[i]) < .0001f, message);
        }
        private static HashSet<Transform> DirectChildren(Transform parent)
        {
            var result = new HashSet<Transform>(); foreach (Transform child in parent) result.Add(child); return result;
        }
    }
}
