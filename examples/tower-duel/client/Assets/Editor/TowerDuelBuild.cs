using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace TowerDuel.Editor
{
    public static class TowerDuelBuild
    {
        private const string Root = "Assets/Resources/TowerDuel";
        private const string Scene = "Assets/Scenes/TowerDuel.unity";
        [MenuItem("Tower Duel/Prepare playable scene")]
        public static void Setup()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Material("Blue", new Color(.10f, .37f, .74f));
            Material("Red", new Color(.76f, .17f, .13f));
            Material("Steel", new Color(.53f, .63f, .69f));
            Material("Skin", new Color(.91f, .67f, .42f));
            Material("Gold", new Color(.95f, .67f, .21f));
            Material("Boot", new Color(.14f, .11f, .09f));
            Material("Bolt", new Color(1, .72f, .24f), true);
            Material("Legal", new Color(.38f, 1, .55f), true);
            Material("Illegal", new Color(1, .25f, .18f), true);
            TowerDuelCharacters.Prepare();
            TowerDuelContent.Prepare();
            TowerDuelVfxContent.Prepare();
            PrepareText();
            var scene = EditorSceneManager.OpenScene("Assets/TwinbridgeArena/TwinbridgeArena.unity");
            TowerDuelSurroundings.Build();
            new GameObject("TowerDuelClient").AddComponent<TowerDuelClient>();
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(.045f, .085f, .13f);
            EditorSceneManager.SaveScene(scene, Scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.companyName = "GameNetKit";
            PlayerSettings.productName = "TowerDuel";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"com.gamenetkit.towerduel");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.WebGL,"com.gamenetkit.towerduel");
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 720; PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            QualitySettings.vSyncCount = 0;
            if (!GraphicsSettings.defaultRenderPipeline) throw new InvalidOperationException("URP is required");
            AssetDatabase.SaveAssets();
            Debug.Log("Tower Duel setup complete: " + Scene);
        }
        private static void Material(string name, Color color, bool unlit = false)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("Missing URP shader");
            if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetColor("_BaseColor", color);
            if (!unlit) material.SetFloat("_Smoothness", .15f);
            EditorUtility.SetDirty(material);
        }
        private static void PrepareText()
        {
            string fontPath = Root + "/DuelFont.asset";
            var font = AssetDatabase.LoadAssetAtPath<FontAsset>(fontPath);
            if (!font)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "/TowerDuelUI.ttf");
                if (!source) throw new InvalidOperationException("Run scripts/Prepare-Font.py first");
                font = FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                font.name = "Tower Duel UI";
                AssetDatabase.CreateAsset(font, fontPath);
                font.TryAddCharacters(File.ReadAllText(Root + "/glyphs.txt"), out string missing);
                if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Font is missing subset glyphs: " + missing);
                foreach (var texture in font.atlasTextures) if (texture && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                if (font.material && !AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
            }
            var text = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(Root + "/DuelText.asset");
            if (!text) { text = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(text, Root + "/DuelText.asset"); }
            text.defaultFontAsset = font;
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/DuelPanel.asset");
            if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, Root + "/DuelPanel.asset"); }
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(720, 1000);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f;
            panel.textSettings = text; panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/DuelTheme.tss");
            EditorUtility.SetDirty(font); EditorUtility.SetDirty(text); EditorUtility.SetDirty(panel);
        }
        [MenuItem("Tower Duel/Build Windows development player")]
        public static void BuildWindows()
        {
            Setup();
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Scene }, locationPathName = "Builds/Windows/TowerDuel.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            string result = JsonUtility.ToJson(new BuildResult { result = report.summary.result.ToString(), errors = report.summary.totalErrors, seconds = report.summary.totalTime.TotalSeconds }, true);
            File.WriteAllText("Builds/build-result.json", result);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException(result);
            Debug.Log("Tower Duel Windows build succeeded");
        }
        [Serializable] private sealed class BuildResult { public string result; public int errors; public double seconds; }

        [MenuItem("Tower Duel/Apply Web release settings")]
        public static void ConfigureWeb()
        {
            var target = NamedBuildTarget.WebGL;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Medium);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.wasm2023 = false;
            PlayerSettings.WebGL.template = "PROJECT:TowerDuel";
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSize;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
            AssetDatabase.SaveAssets();
            Debug.Log("Tower Duel Web settings saved: " + WebSettings());
        }

        public static string WebSettings() =>
            $"target={EditorUserBuildSettings.activeBuildTarget}; compression={PlayerSettings.WebGL.compressionFormat}; " +
            $"fallback={PlayerSettings.WebGL.decompressionFallback}; stripEngine={PlayerSettings.stripEngineCode}; " +
            $"stripping={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL)}; " +
            $"exceptions={PlayerSettings.WebGL.exceptionSupport}; optimization={UnityEditor.WebGL.UserBuildSettings.codeOptimization}; " +
            $"cache={PlayerSettings.WebGL.dataCaching}; symbols={PlayerSettings.WebGL.debugSymbolMode}; " +
            $"memory={PlayerSettings.WebGL.initialMemorySize}/{PlayerSettings.WebGL.maximumMemorySize}; " +
            $"growth={PlayerSettings.WebGL.memoryGrowthMode}; template={PlayerSettings.WebGL.template}";

        [MenuItem("Tower Duel/Build Web release player")]
        public static void BuildWeb()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new InvalidOperationException("Open this Editor with -buildTarget WebGL before building");
            Setup();
            ConfigureWeb();
            Directory.CreateDirectory("Builds/WebGL");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Scene }, locationPathName = "Builds/WebGL",
                target = BuildTarget.WebGL, options = BuildOptions.None
            });
            string result = JsonUtility.ToJson(new BuildResult { result = report.summary.result.ToString(), errors = report.summary.totalErrors, seconds = report.summary.totalTime.TotalSeconds }, true);
            File.WriteAllText("Builds/web-build-result.json", result);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException(result);
            var rules = DuelRules.Load();
            File.WriteAllText("Builds/WebGL/tower-duel-build.txt", $"TowerDuel protocol {rules.schema} / {rules.id}\n");
            Debug.Log("Tower Duel Web build succeeded: " + report.summary.totalSize + " bytes");
        }
    }
}
