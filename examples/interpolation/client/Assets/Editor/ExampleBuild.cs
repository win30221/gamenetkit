using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Realtime.Examples.InterpolationLesson.Editor
{
    public static class ExampleBuild
    {
        public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/InterpolationDemo.exe");
        public static void Web()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Build(BuildTarget.WebGL, "Builds/WebGL");
        }

        private static void Build(BuildTarget target, string output)
        {
            ExampleVerification.Run();
            // Materials are created at runtime, so preserve their shader explicitly.
            var shader = Shader.Find("Standard");
            if (shader == null) throw new Exception("Standard shader is unavailable");
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            bool included = false;
            for (int i = 0; i < shaders.arraySize; i++)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) included = true;
            if (!included) { int i = shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; graphics.ApplyModifiedPropertiesWithoutUndo(); }
            PlayerSettings.companyName = "RealtimeFramework";
            PlayerSettings.productName = "Interpolation Lesson";
            PlayerSettings.runInBackground = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            if (target == BuildTarget.StandaloneWindows64)
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/Interpolation.unity" }, target = target,
                locationPathName = output, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Example build failed");
            Debug.Log("RF_INTERPOLATION_BUILD PASS " + target);
        }
    }
}
