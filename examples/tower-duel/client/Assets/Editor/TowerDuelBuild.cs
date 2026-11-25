using System;
using System.IO;
using System.Linq;
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
            TowerDuelUnitBundleBuild.ResetManifest();
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
            TowerDuelSpriteContent.Prepare();
            TowerDuelContent.Prepare();
            TowerDuelVfxContent.Prepare();
            PrepareText();
            TowerDuelEnvironmentContent.Prepare();
            EditorSceneManager.OpenScene(Scene);
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
            BuildPreparedWindows();
        }
        public static void BuildPreparedWindows()
        {
            TowerDuelEnvironmentContent.Prepare();
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

        private static void AssertNoDuplicateWebArt()
        {
            string units=TowerDuelSpriteContent.UnitRoot;
            if (Directory.Exists(Root+"/B3/Units"))
                throw new InvalidOperationException("Legacy B3 atlas directory still exists in Resources; remove the duplicate after checking the new TowerDuelArt/Units directory.");
            foreach (string path in Directory.GetFiles(units,"definition.json",SearchOption.AllDirectories))
            {
                var definition=B3SpriteDefinition.Read(path);
                if (!definition.baked) continue;
                foreach (string clip in new[]{"idle","walk","attack"})
                {
                    string original=Path.Combine(Path.GetDirectoryName(path),clip+".png");
                    if (File.Exists(original))
                        throw new InvalidOperationException("Baked unit has duplicate runtime source atlas: "+original+
                            ". Archive it under art/b3-production/atlases or run the sprite baker.");
                }
            }
            foreach (string path in Directory.GetFiles(Root+"/Cards","*.png"))
                if (File.Exists(Path.Combine(Root+"/B3/Cards",Path.GetFileName(path))))
                    throw new InvalidOperationException("Duplicate legacy card art: "+path+
                        ". Archive the legacy copy outside Resources.");
        }

        [Serializable] private sealed class SizeEntry { public string path; public long bytes; }
        [Serializable] private sealed class WebSizeReport
        {
            public string builtAtUtc;
            public long buildReportBytes, actualBuildFilesBytes, startupTransferBytes, packedAssetBytes, unitBundleBytes;
            public SizeEntry[] files, assets;
        }
        private static void WriteWebSizeReport(BuildReport report,string output)
        {
            string absolute=Path.GetFullPath(output);
            var files=Directory.GetFiles(absolute,"*",SearchOption.AllDirectories)
                .Select(path=>new SizeEntry { path=path.Substring(absolute.Length+1).Replace('\\','/'), bytes=new FileInfo(path).Length })
                .OrderByDescending(x=>x.bytes).ThenBy(x=>x.path,StringComparer.Ordinal).ToArray();
            var assets=(report.packedAssets ?? Array.Empty<PackedAssets>()).SelectMany(x=>x.contents)
                .GroupBy(x=>string.IsNullOrEmpty(x.sourceAssetPath)?"(internal)":x.sourceAssetPath)
                .Select(g=>new SizeEntry { path=g.Key, bytes=g.Sum(x=>(long)x.packedSize) })
                .OrderByDescending(x=>x.bytes).ThenBy(x=>x.path,StringComparer.Ordinal).ToArray();
            var result=new WebSizeReport {
                builtAtUtc=DateTime.UtcNow.ToString("o"),
                buildReportBytes=(long)report.summary.totalSize,
                actualBuildFilesBytes=files.Sum(x=>x.bytes),
                startupTransferBytes=files.Where(x=>x.path=="index.html" || x.path=="Build/WebGL.loader.js" ||
                    x.path=="Build/WebGL.data.gz" || x.path=="Build/WebGL.wasm.gz" ||
                    x.path=="Build/WebGL.framework.js.gz").Sum(x=>x.bytes),
                unitBundleBytes=files.Where(x=>x.path.StartsWith("assets/bundles/",StringComparison.Ordinal)&&x.path.EndsWith(".bundle",StringComparison.Ordinal)).Sum(x=>x.bytes),
                packedAssetBytes=assets.Sum(x=>x.bytes),files=files,assets=assets
            };
            File.WriteAllText("Builds/web-build-size.json",JsonUtility.ToJson(result,true)+"\n");
            Debug.Log("Tower Duel Web startup files: "+result.startupTransferBytes+" bytes. Detailed asset report: Builds/web-build-size.json");
            foreach (var asset in assets.Take(10)) Debug.Log("Packed asset: "+asset.bytes+" bytes "+asset.path);
        }

        [MenuItem("Tower Duel/Build Web release player")]
        public static void BuildWeb()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new InvalidOperationException("Open this Editor with -buildTarget WebGL before building");
            AssertNoDuplicateWebArt();
            Setup();
            ConfigureWeb();
            const string output = "Builds/WebGL";
            File.Delete("Builds/web-build-size.json");
            var content = TowerDuelWebContent.BeginBuild(output, out var rules);
            BuildReport report;
            using (var bundles = TowerDuelUnitBundleBuild.Prepare())
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { Scene }, locationPathName = output,
                    target = BuildTarget.WebGL, options = BuildOptions.DetailedBuildReport
                });
                string result = JsonUtility.ToJson(new BuildResult { result = report.summary.result.ToString(), errors = report.summary.totalErrors, seconds = report.summary.totalTime.TotalSeconds }, true);
                File.WriteAllText("Builds/web-build-result.json", result);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException(result);
                bundles.AssertNoInlineAtlases(report);
                bundles.Publish(output);
                content.unitBundlesHash = TowerDuelUnitBundleBuild.Sha256(Path.Combine(output, "unit-bundles.json"));
                File.WriteAllText(Path.Combine(output, "tower-duel-build.txt"), $"TowerDuel protocol {rules.schema} / {rules.id}\n");
                WriteWebSizeReport(report, output);
            }
            content.Publish(output);
            Debug.Log("Tower Duel Web build succeeded: " + report.summary.totalSize + " bytes");
        }
    }
}
