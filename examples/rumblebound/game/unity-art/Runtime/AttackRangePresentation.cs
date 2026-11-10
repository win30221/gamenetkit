using Rumblebound.Presentation;
using Rumblebound.Gameplay;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Rumblebound.Presentation
{
    public sealed class AttackRangePresentation : IDisposable
    {
        private const int Segments = 40;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material fillMaterial, lineMaterial;
        private readonly LineRenderer outline, direction;
        private readonly Vector3[] sector = new Vector3[Segments + 2];
        private readonly Vector3[] corridor = new Vector3[4];
        private readonly int[] sectorTriangles = new int[Segments * 3];
        private readonly int[] corridorTriangles = {0, 3, 2, 0, 2, 1};
        public bool Visible => root != null && root.activeSelf;
        public Vector3 EndPoint { get; private set; }

        public AttackRangePresentation(Transform owner)
        {
            root = new GameObject("Local attack range");
            root.transform.SetParent(owner, false);
            var source = Resources.Load<Material>("Mage/TargetCircle");
            if (source == null) throw new InvalidOperationException("Attack range material missing");
            fillMaterial = new Material(source); lineMaterial = new Material(source);
            mesh = new Mesh {name = "Local attack range fill"}; mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = fillMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            outline = CreateLine("Attack range outline", true, .04f);
            direction = CreateLine("Arrow direction", false, .035f);
            for (int i = 0; i < Segments; i++)
            { sectorTriangles[i * 3] = 0; sectorTriangles[i * 3 + 1] = i + 1; sectorTriangles[i * 3 + 2] = i + 2; }
            Hide();
        }

        private LineRenderer CreateLine(string name, bool loop, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = false; line.loop = loop; line.widthMultiplier = width;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }

        public void Show(Rules rules, HeroDefinition hero, ArenaMapDefinition map, Vector3 position, Vector2 aim, bool ready)
        {
            bool melee = HeroRules.IsMelee(hero);
            if (rules == null || hero == null || (!melee && hero.attackKind != "ranged") || aim.sqrMagnitude < .000001f)
            { Hide(); return; }
            aim.Normalize();
            Vector3 forward = new Vector3(aim.x, 0, aim.y), right = new Vector3(aim.y, 0, -aim.x);
            position.y = 0;
            root.transform.SetPositionAndRotation(position + Vector3.up * .055f, Quaternion.LookRotation(forward));
            Color tint = melee ? new Color(1f, .73f, .25f) : new Color(.32f, .86f, 1f);
            fillMaterial.color = new Color(tint.r, tint.g, tint.b, ready ? .13f : .045f);
            lineMaterial.color = new Color(tint.r, tint.g, tint.b, ready ? .85f : .3f);
            mesh.Clear();
            if (melee)
            {
                sector[0] = Vector3.zero;
                outline.positionCount = sector.Length;
                for (int i = 0; i <= Segments; i++)
                {
                    float angle = (float)(hero.meleeArcDegrees * (i / (double)Segments - .5) * Mathf.Deg2Rad);
                    Vector3 ray = forward * Mathf.Cos(angle) + right * Mathf.Sin(angle);
                    float reach = Clip(map, position, ray, (float)hero.meleeRange, 0);
                    sector[i + 1] = new Vector3(Mathf.Sin(angle) * reach, 0, Mathf.Cos(angle) * reach);
                }
                mesh.vertices = sector; mesh.triangles = sectorTriangles;
                for (int i = 0; i < sector.Length; i++) outline.SetPosition(i, sector[i] + Vector3.up * .012f);
                direction.gameObject.SetActive(false);
                EndPoint = position + forward * Clip(map, position, forward, (float)hero.meleeRange, 0);
            }
            else
            {
                // The first authority sweep includes the muzzle offset, then
                // exactly projectileLifetimeTicks fixed steps before expiry.
                float reach = (float)HeroRules.RangedReach(rules, hero);
                float length = Clip(map, position, forward, reach, rules.projectileRadius);
                float radius = (float)rules.projectileRadius;
                corridor[0] = new Vector3(-radius, 0, 0); corridor[1] = new Vector3(radius, 0, 0);
                corridor[2] = new Vector3(radius, 0, length); corridor[3] = new Vector3(-radius, 0, length);
                mesh.vertices = corridor; mesh.triangles = corridorTriangles;
                outline.positionCount = corridor.Length;
                for (int i = 0; i < corridor.Length; i++) outline.SetPosition(i, corridor[i] + Vector3.up * .012f);
                float head = Mathf.Min(.35f, length * .4f), halfWidth = Mathf.Min(radius, head * .6f);
                direction.gameObject.SetActive(true); direction.positionCount = 5;
                direction.SetPosition(0, Vector3.up * .014f);
                direction.SetPosition(1, new Vector3(0, .014f, length));
                direction.SetPosition(2, new Vector3(-halfWidth, .014f, length - head));
                direction.SetPosition(3, new Vector3(0, .014f, length));
                direction.SetPosition(4, new Vector3(halfWidth, .014f, length - head));
                EndPoint = position + forward * length;
            }
            mesh.RecalculateBounds();
            root.SetActive(true);
        }

        private static float Clip(ArenaMapDefinition map, Vector3 origin, Vector3 direction, float reach, double radius)
        {
            return ArenaCollision.FirstMapHit(map, origin.x, origin.z, direction.x * reach, direction.z * reach,
                radius, true, out double at, out _, out _) ? reach * (float)at : reach;
        }

        public void Hide() { if (root != null) root.SetActive(false); }
        public void Dispose()
        {
            Hide(); Object.Destroy(root); Object.Destroy(mesh);
            Object.Destroy(fillMaterial); Object.Destroy(lineMaterial);
        }
    }
}
