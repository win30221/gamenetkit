using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel.Editor
{
    [Serializable] internal sealed class B3SpriteDefinition
    {
        public string kind, groundKind;
        public int tileSize, baseline, referenceHeight, walkFrames, attackFrames, impactFrame;
        public int idleFrames = 1;
        public float idleFps = 8f, walkFps = 8f;
        public float unitHeight, worldHeight, teamMaskTop;
        public bool baked;

        internal static B3SpriteDefinition Read(string path)
        {
            // Overwrite a constructed object: old definitions omit the new optional fields.
            var definition = new B3SpriteDefinition();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), definition);
            if (definition.tileSize <= 0 || definition.idleFrames < 1 ||
                definition.walkFrames < 2 || definition.attackFrames < 2 ||
                definition.impactFrame < 1 || definition.impactFrame >= definition.attackFrames ||
                !ValidFps(definition.idleFps) || !ValidFps(definition.walkFps))
                throw new InvalidOperationException("Invalid B3 animation settings: " + path);
            // The baked atlas doubles the height. Validate before importing or allocating.
            foreach (string clip in new[] { "idle", "walk", "attack" }) definition.AtlasImportSize(clip, true);
            return definition;
        }

        private static bool ValidFps(float fps) => fps > 0f && fps <= 60f && !float.IsNaN(fps) && !float.IsInfinity(fps);
        internal int Columns(string clip) => clip == "idle" ? idleFrames : clip == "walk" ? walkFrames : attackFrames;
        internal int AtlasImportSize(string clip, bool withShadows)
        {
            long width = (long)tileSize * Columns(clip), height = (long)tileSize * (withShadows ? 16 : 8);
            if (width > 4096 || height > 4096)
                throw new InvalidOperationException("B3 atlas exceeds the 4096px platform budget: " + kind + "/" + clip);
            return Mathf.Max(2048, Mathf.NextPowerOfTwo((int)Math.Max(width, height)));
        }
    }

    public static class TowerDuelSpriteContent
    {
        private const string Root = "Assets/Resources/TowerDuel/B3";
        [MenuItem("Tower Duel/Prepare B3 sprite characters")]
        public static void Prepare()
        {
            if (!Directory.Exists(Root + "/Units")) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/B3Sprite.shader");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("B3 sprite shader did not compile");
            string materialPath = Root + "/Sprite.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material = new Material(shader) { name = "B3 Sprite" }; AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader; material.SetFloat("_ZWrite", 1); material.SetFloat("_Cutoff", .08f);
            EditorUtility.SetDirty(material);
            var contactShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/ContactShadow.shader");
            if (!contactShader || ShaderUtil.ShaderHasError(contactShader)) throw new InvalidOperationException("Foot contact shader did not compile");
            var contactMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/ContactShadow.mat");
            if (!contactMaterial) { contactMaterial = new Material(contactShader) { name = "Soft foot contact" }; AssetDatabase.CreateAsset(contactMaterial, Root + "/ContactShadow.mat"); }
            contactMaterial.shader = contactShader; EditorUtility.SetDirty(contactMaterial);
            var projectedShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/ProjectedShadow.shader");
            if (!projectedShader || ShaderUtil.ShaderHasError(projectedShader)) throw new InvalidOperationException("Projected sprite shadow shader did not compile");
            var projectedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/ProjectedShadow.mat");
            if (!projectedMaterial) { projectedMaterial = new Material(projectedShader) { name = "Animated sprite ground shadow" }; AssetDatabase.CreateAsset(projectedMaterial, Root + "/ProjectedShadow.mat"); }
            projectedMaterial.shader = projectedShader; EditorUtility.SetDirty(projectedMaterial);
            var bakedShader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/BakedShadow.shader");
            if(!bakedShader||ShaderUtil.ShaderHasError(bakedShader))throw new InvalidOperationException("Baked shadow shader did not compile");
            var bakedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/BakedShadow.mat");
            if(!bakedMaterial){bakedMaterial=new Material(bakedShader){name="Baked ground shadow"};AssetDatabase.CreateAsset(bakedMaterial,Root+"/BakedShadow.mat");}
            bakedMaterial.shader=bakedShader;EditorUtility.SetDirty(bakedMaterial);
            var rules = DuelRules.Load();
            var groundSets = new Dictionary<string, DuelSpriteSet>();
            var airborneSets = new Dictionary<string, DuelSpriteSet>();
            foreach (string file in Directory.GetFiles(Root + "/Units", "definition.json", SearchOption.AllDirectories))
            {
                var definition = B3SpriteDefinition.Read(file);
                bool airborneVariant = !string.IsNullOrEmpty(definition.groundKind);
                string cardKind = airborneVariant ? definition.groundKind : definition.kind;
                if (rules.Find(cardKind) == null) throw new InvalidOperationException("Unknown B3 unit " + cardKind);
                string folder = Root + "/Units/" + definition.kind;
                string suffix=definition.baked?"-baked":"";
                var idle = Import(folder + "/idle"+suffix+".png", true,definition.AtlasImportSize("idle",definition.baked));
                var walk = Import(folder + "/walk"+suffix+".png", true,definition.AtlasImportSize("walk",definition.baked));
                var attack = Import(folder + "/attack"+suffix+".png", true,definition.AtlasImportSize("attack",definition.baked));
                if (!airborneVariant) Import(Root + "/Cards/" + cardKind + ".png", false);
                string path = Root + "/" + definition.kind + ".asset";
                var set = AssetDatabase.LoadAssetAtPath<DuelSpriteSet>(path);
                if (!set) { set = ScriptableObject.CreateInstance<DuelSpriteSet>(); AssetDatabase.CreateAsset(set, path); }
                set.ClearFrames();set.Baked=definition.baked;
                set.Kind = definition.kind; set.Idle = idle; set.Walk = walk; set.Attack = attack;
                set.TileSize = definition.tileSize; set.Baseline = definition.baseline; set.ReferenceHeight = definition.referenceHeight;
                set.IdleFrames = definition.idleFrames; set.WalkFrames = definition.walkFrames; set.AttackFrames = definition.attackFrames; set.ImpactFrame = definition.impactFrame;
                set.IdleFps = definition.idleFps; set.WalkFps = definition.walkFps;
                set.UnitHeight = definition.unitHeight; set.WorldHeight = definition.worldHeight;
                set.TeamMaskTop = definition.teamMaskTop > 0 ? definition.teamMaskTop : 2;
                EditorUtility.SetDirty(set);
                // Validate the actual imported dimensions before exposing the new prefab.
                foreach (var clip in new[] { DuelSpriteClip.Idle, DuelSpriteClip.Walk, DuelSpriteClip.Attack })
                {
                    int last = set.FrameCount(clip) - 1;
                    set.Frame(clip, 7, last);
                    if (set.Baked) set.ShadowFrame(clip, 7, last);
                }
                (airborneVariant ? airborneSets : groundSets).Add(cardKind, set);
            }
            Import(Root + "/UI/CardFrame.png", true);
            foreach (var pair in groundSets)
            {
                var set = pair.Value;
                airborneSets.TryGetValue(pair.Key, out var airborneSet);
                if (airborneSet && airborneSet.Baked != set.Baked)
                    throw new InvalidOperationException("Ground and airborne shadows must use the same format: " + pair.Key);
                var root = new GameObject(pair.Key);
                try
                {
                    var facing = new GameObject("Facing"); facing.transform.SetParent(root.transform, false);
                    var renderer = facing.AddComponent<SpriteRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    var sprites = root.AddComponent<DuelSpriteUnit>(); sprites.Configure(set, renderer, airborneSet);
                    var marker = new GameObject("Team marker");
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.localPosition = Vector3.up * .02f;
                    marker.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("TowerDuel/Units/TeamMarker");
                    var markerRenderer = marker.AddComponent<MeshRenderer>();
                    markerRenderer.sharedMaterial = Resources.Load<Material>("TowerDuel/Materials/Blue");
                    markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    var art = root.AddComponent<DuelUnitArt>(); art.Sprites = sprites; art.Height = set.UnitHeight;
                    art.TeamMarker = marker.GetComponent<Renderer>();
                    art.TeamMarker.enabled = false;
                    PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + pair.Key + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            DuelSpriteLibrary.ClearCache();
            Debug.Log("Prepared B3 sprite characters, matching card portraits and frame");
        }
        [MenuItem("Tower Duel/Build B3 Windows review")]
        public static void BuildWindowsReview()
        {
            Prepare();
            var previous=PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption=InsecureHttpOption.DevelopmentOnly;
                TowerDuelBuild.BuildPreparedWindows();
            }
            finally { PlayerSettings.insecureHttpOption=previous;AssetDatabase.SaveAssets(); }
        }
        private static Texture2D Import(string path, bool alpha,int maxSize=2048)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing B3 artwork " + path);
            importer.textureType = TextureImporterType.Default; importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true; importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha; importer.mipmapEnabled = false; importer.isReadable = false;
            importer.maxTextureSize = maxSize; importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.ClearPlatformTextureSettings("Standalone");
            importer.ClearPlatformTextureSettings("WebGL");
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
