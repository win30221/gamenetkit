using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TowerDuel.Editor
{
    // Rumblebound owns the acquired source assets. This ignored import mirror keeps
    // standalone Unity projects reproducible without downloading or committing duplicates.
    public static class TowerDuelVfxContent
    {
        public const string Imported = "Assets/ReusedVFX";
        public const string Output = Imported + "/Resources/TowerDuel/BattleFX";
        private const string Remaster = "Art/VFX/CartoonFXRemaster";
        private const string Legacy = "Art/VFX/CartoonFXLegacy";
        private static readonly Regex GuidPattern = new Regex(@"guid:\s*([a-fA-F0-9]{32})");
        private static Scene authoringScene;

        [InitializeOnLoadMethod]
        private static void PrepareOnOpen()
        {
            EditorApplication.delayCall += () =>
            {
                if(!EditorApplication.isPlayingOrWillChangePlaymode&&!File.Exists(Output+"/Hit.prefab"))Prepare();
            };
        }

        [MenuItem("Tower Duel/Prepare reused combat effects")]
        public static void Prepare()
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../rumblebound/game/unity-art"));
            if (!Directory.Exists(source)) throw new InvalidOperationException("Missing canonical Rumblebound VFX: " + source);
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string meta in Directory.EnumerateFiles(source, "*.meta", SearchOption.AllDirectories))
            {
                string asset = meta.Substring(0, meta.Length - 5);
                if (!File.Exists(asset)) continue;
                var match = GuidPattern.Match(File.ReadAllText(meta));
                if (match.Success) index[match.Groups[1].Value] = asset;
            }
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Include(string path)
            {
                path = Path.GetFullPath(path);
                if (!selected.Add(path)) return;
                if (!File.Exists(path)) throw new FileNotFoundException("Missing reused effect dependency", path);
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".prefab" || extension == ".mat" || extension == ".asset" || extension == ".shader")
                    foreach (Match reference in GuidPattern.Matches(File.ReadAllText(path)))
                        if (index.TryGetValue(reference.Groups[1].Value, out string dependency)) Include(dependency);
                if (extension == ".shader")
                    foreach (string include in Directory.EnumerateFiles(Path.GetDirectoryName(path), "*.cginc")) Include(include);
            }
            string[] seeds = {
                "Runtime/Resources/VFX/WallHit.prefab", "Runtime/Resources/VFX/PlayerHit.prefab",
                "Runtime/Resources/Mage/FireballDrop.prefab", "Runtime/Resources/Mage/GroundImpact.prefab",
                Legacy + "/CFX3 Prefabs/Magic Misc/CFX3_MagicAura_B_Runic.prefab",
                "Art/VFX/Sugawara/SlashPlane.asset", "Art/VFX/Sugawara/MeleeSlash.mat"
            };
            foreach (string seed in seeds) Include(Path.Combine(source, seed));
            // Keep the acquisition, author, license and URP adaptation records beside the mirror.
            foreach (string folder in new[] { Remaster, Legacy, "Art/VFX/Hun0FX", "Art/VFX/Sugawara" })
                foreach (string record in Directory.EnumerateFiles(Path.Combine(source, folder), "*", SearchOption.TopDirectoryOnly))
                    if (!record.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) selected.Add(record);
            var records = new List<SourceRecord>();
            using (var hash = SHA256.Create())
                foreach (string path in selected.OrderBy(p => p, StringComparer.Ordinal))
                {
                    string relative = Path.GetRelativePath(source, path).Replace('\\', '/');
                    string destination = Imported + "/Source/" + relative;
                    CopyIfChanged(path, destination);
                    if (File.Exists(path + ".meta")) CopyIfChanged(path + ".meta", destination + ".meta");
                    records.Add(new SourceRecord { path = relative, sha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() });
                }
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            authoringScene = EditorSceneManager.NewPreviewScene();
            try
            {
            GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(Imported + "/Source/" + path)
                ?? throw new InvalidOperationException("Could not import VFX: " + path);
            var hit = Load(seeds[0]); var heavy = Load(seeds[1]);
            var fire = Load(seeds[2]); var explosion = Load(seeds[3]); var frost = Load(seeds[4]);
            Variant("Hit", hit, .85f, false, null, 64);
            Variant("HeavyHit", heavy, 1.35f, false, new Color(1, .80f, .38f), 64);
            Variant("Fireball", fire, 1.15f, true, null, 72);
            Variant("Explosion", explosion, .58f, false, null, 96);
            Variant("Frost", frost, 1.15f, true, new Color(.30f, .78f, 1f, .65f), 48);
            PrepareSlash();
            PrepareDust(explosion, "Death", false);
            PrepareDust(explosion, "Collapse", true);
            PrepareTrail();
            var manifest = new ImportRecord { canonicalSource = "examples/rumblebound/game/unity-art", downloadedFiles = 0, files = records.ToArray() };
            File.WriteAllText(Imported + "/reuse-manifest.json", JsonUtility.ToJson(manifest, true));
            AssetDatabase.SaveAssets();
            Debug.Log("Tower Duel reused " + records.Count + " existing VFX source files; downloaded 0 files.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(authoringScene);
            }
        }

        private static void CopyIfChanged(string source, string destination)
        {
            byte[] bytes = File.ReadAllBytes(source);
            if (File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(bytes)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, bytes);
        }

        private static void Variant(string name, GameObject source, float scale, bool loop, Color? tint, int maximum)
        {
            var root = NewRoot(name);
            try
            {
                var art = UnityEngine.Object.Instantiate(source, root.transform, false);
                art.name = "Reused " + source.name;
                art.transform.localScale *= scale;
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = system.main;
                    main.playOnAwake = false; main.loop = loop; main.prewarm = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    main.maxParticles = Mathf.Min(main.maxParticles, maximum);
                    if (tint.HasValue) main.startColor = Recolor(main.startColor, tint.Value);
                    if (name == "Frost")
                    {
                        main.startLifetime = Mathf.Min(2.2f, Maximum(main.startLifetime));
                        main.duration = 2;
                        main.startColor = new Color(.35f,.82f,1,.65f);
                        var colors = system.colorOverLifetime;
                        if(colors.enabled)
                        {
                            var gradient = new Gradient();
                            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.15f),new GradientAlphaKey(.6f,.7f),new GradientAlphaKey(0,1)});
                            colors.color = gradient;
                        }
                    }
                    if (name == "Fireball" && (system.name == "Smoke" || system.name == "Embers"))
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    if(name=="Frost")
                    {
                        string materialPath=Output+"/Frost-"+renderer.sharedMaterial.name+".mat";
                        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if(!material){material=new Material(renderer.sharedMaterial);AssetDatabase.CreateAsset(material,materialPath);}
                        if(material.HasProperty("_TintColor"))material.SetColor("_TintColor",new Color(.4f,.6f,.8f,.65f));
                        material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend",(float)BlendMode.One);
                        EditorUtility.SetDirty(material);renderer.sharedMaterial=material;
                    }
                }
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
                foreach (var light in root.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
                PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient source, Color color)
        {
            Color Convert(Color old) => new Color(color.r, color.g, color.b, old.a * color.a);
            Gradient ConvertGradient(Gradient old)
            {
                var value = new Gradient();
                value.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                    old.alphaKeys.Select(k => new GradientAlphaKey(k.alpha * color.a, k.time)).ToArray());
                return value;
            }
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Convert(source.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Convert(source.colorMin), Convert(source.colorMax));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(ConvertGradient(source.gradient));
                default: return new ParticleSystem.MinMaxGradient(ConvertGradient(source.gradientMin), ConvertGradient(source.gradientMax));
            }
        }

        private static void PrepareSlash()
        {
            var root = NewRoot("Slash");
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Imported + "/Source/Art/VFX/Sugawara/SlashPlane.asset");
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Imported + "/Source/Art/VFX/Sugawara/MeleeSlash.mat");
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(root, Output + "/Slash.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void PrepareDust(GameObject explosion, string name, bool debris)
        {
            var root = NewRoot(name);
            try
            {
                var smoke = explosion.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == "Smoke");
                var dust = UnityEngine.Object.Instantiate(smoke.gameObject, root.transform, false).GetComponent<ParticleSystem>();
                dust.name = "Reused smoke"; dust.transform.localPosition = Vector3.zero;
                dust.transform.localScale = Vector3.one * (debris ? .85f : .25f);
                var main = dust.main;
                main.playOnAwake = false; main.loop = false; main.prewarm = false;
                main.stopAction = ParticleSystemStopAction.None; main.maxParticles = debris ? 24 : 8;
                main.startColor = new Color(.67f, .65f, .57f, debris ? .8f : .6f);
                main.startLifetime = debris ? new ParticleSystem.MinMaxCurve(.6f, 1.1f) : new ParticleSystem.MinMaxCurve(.25f, .5f);
                if (debris)
                {
                    var stones = new GameObject("Falling stone fragments"); stones.transform.SetParent(root.transform, false);
                    var particles = stones.AddComponent<ParticleSystem>();
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var stoneMain = particles.main;
                    stoneMain.playOnAwake = false; stoneMain.loop = false; stoneMain.duration = .1f;
                    stoneMain.startLifetime = new ParticleSystem.MinMaxCurve(.65f, 1.1f);
                    stoneMain.startSize = new ParticleSystem.MinMaxCurve(.12f, .28f);
                    stoneMain.startSpeed = new ParticleSystem.MinMaxCurve(2, 4);
                    stoneMain.startRotation3D = true;
                    stoneMain.startRotationX = new ParticleSystem.MinMaxCurve(0, 6.28f);
                    stoneMain.startRotationZ = new ParticleSystem.MinMaxCurve(0, 6.28f);
                    stoneMain.gravityModifier = 1.2f; stoneMain.maxParticles = 16;
                    var emission = particles.emission; emission.rateOverTime = 0;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0, 14) });
                    var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = .6f;
                    stones.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    string meshPath = Output + "/Debris.asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (!mesh) { mesh = UnityEngine.Object.Instantiate(DuelEffects.CrystalMesh()); AssetDatabase.CreateAsset(mesh, meshPath); }
                    renderer.mesh = mesh;
                    renderer.sharedMaterial = Material("Stone", "Universal Render Pipeline/Lit", new Color(.43f, .46f, .48f));
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void PrepareTrail()
        {
            var material = Material("Trail", "Universal Render Pipeline/Particles/Unlit", Color.white);
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        private static Material Material(string name, string shaderName, Color color)
        {
            string path = Output + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find(shaderName)); AssetDatabase.CreateAsset(material, path); }
            material.shader=Shader.Find(shaderName);
            material.SetColor("_BaseColor", color); EditorUtility.SetDirty(material); return material;
        }
        private static float Maximum(ParticleSystem.MinMaxCurve curve) => curve.mode == ParticleSystemCurveMode.Constant ? curve.constant : Mathf.Max(curve.constantMin, curve.constantMax);
        private static GameObject NewRoot(string name)
        {
            var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, authoringScene); return root;
        }
        [Serializable] private sealed class SourceRecord { public string path, sha256; }
        [Serializable] private sealed class ImportRecord { public string canonicalSource; public int downloadedFiles; public SourceRecord[] files; }
    }
}
