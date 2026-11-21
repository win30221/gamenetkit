using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel.Editor
{
    public static class TowerDuelSpriteContent
    {
        private const string Root = "Assets/Resources/TowerDuel/B3";
        [Serializable] private sealed class Definition
        {
            public string kind;
            public int tileSize, baseline, referenceHeight, walkFrames, attackFrames, impactFrame;
            public float unitHeight, worldHeight, teamMaskTop;
            public bool baked;
        }
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
            foreach (string file in Directory.GetFiles(Root + "/Units", "definition.json", SearchOption.AllDirectories))
            {
                var definition = JsonUtility.FromJson<Definition>(File.ReadAllText(file));
                if (DuelRules.Load().Find(definition.kind) == null) throw new InvalidOperationException("Unknown B3 unit " + definition.kind);
                string folder = Root + "/Units/" + definition.kind;
                string suffix=definition.baked?"-baked":"";
                var idle = Import(folder + "/idle"+suffix+".png", true,definition.baked?4096:2048);
                var walk = Import(folder + "/walk"+suffix+".png", true,definition.baked?4096:2048);
                var attack = Import(folder + "/attack"+suffix+".png", true,definition.baked?4096:2048);
                Import(Root + "/Cards/" + definition.kind + ".png", false);
                Import(Root + "/UI/CardFrame.png", true);
                string path = Root + "/" + definition.kind + ".asset";
                var set = AssetDatabase.LoadAssetAtPath<DuelSpriteSet>(path);
                if (!set) { set = ScriptableObject.CreateInstance<DuelSpriteSet>(); AssetDatabase.CreateAsset(set, path); }
                set.ClearFrames();set.Baked=definition.baked;
                set.Kind = definition.kind; set.Idle = idle; set.Walk = walk; set.Attack = attack;
                set.TileSize = definition.tileSize; set.Baseline = definition.baseline; set.ReferenceHeight = definition.referenceHeight;
                set.WalkFrames = definition.walkFrames; set.AttackFrames = definition.attackFrames; set.ImpactFrame = definition.impactFrame;
                set.UnitHeight = definition.unitHeight; set.WorldHeight = definition.worldHeight;
                set.TeamMaskTop = definition.teamMaskTop > 0 ? definition.teamMaskTop : 2;
                EditorUtility.SetDirty(set);
                // Validate the actual imported dimensions before exposing the new prefab.
                set.Frame(DuelSpriteClip.Idle, 7, 0); set.Frame(DuelSpriteClip.Walk, 7, 5); set.Frame(DuelSpriteClip.Attack, 7, 5);
                var root = new GameObject(definition.kind);
                try
                {
                    var facing = new GameObject("Facing"); facing.transform.SetParent(root.transform, false);
                    var renderer = facing.AddComponent<SpriteRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    var sprites = root.AddComponent<DuelSpriteUnit>(); sprites.Configure(set, renderer);
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
                    PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + definition.kind + ".prefab");
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
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
