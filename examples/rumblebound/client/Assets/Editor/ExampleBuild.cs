using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rumblebound.Example.Editor
{
    public static class ExampleBuild
    {
        public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/RumbleboundExample.exe");
        public static void WithoutOptional() => Build(BuildTarget.StandaloneWindows64, "Builds/NoOptionalNetcode/RumbleboundExample.exe");
        public static void Web()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Build(BuildTarget.WebGL, "Builds/WebGL");
        }

        private static void Build(BuildTarget target, string output)
        {
            BattleVerification.Run();
            // Materials are created at runtime, so preserve their shader explicitly.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("URP Lit shader is unavailable");
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            bool included = false;
            for (int i = 0; i < shaders.arraySize; i++)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) included = true;
            if (!included) { int i = shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; graphics.ApplyModifiedPropertiesWithoutUndo(); }
            PlayerSettings.companyName = "RealtimeFramework";
            PlayerSettings.productName = "Rumblebound Example";
            PlayerSettings.runInBackground = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            if (target == BuildTarget.StandaloneWindows64)
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/Battle.unity" }, target = target,
                locationPathName = output, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Example build failed");
            var stamp=target==BuildTarget.WebGL?System.IO.Path.Combine(output,"rf-battle-build.txt"):System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output),"rf-battle-build.txt");
            System.IO.File.WriteAllText(stamp,"Rumblebound.Example.Battle v1\n");
            Debug.Log("RF_EXAMPLE_BUILD PASS " + target);
        }
    }
}
