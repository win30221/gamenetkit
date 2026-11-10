using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MinimalBuild
{
    public static void Windows() { Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Minimal.exe"); }
    public static void Web() { PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; Build(BuildTarget.WebGL, "Builds/WebGL"); }
    private static void Build(BuildTarget target, string output)
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Minimal.unity");
        PlayerSettings.companyName = "RealtimeFramework"; PlayerSettings.productName = "Minimal SDK Example";
        PlayerSettings.runInBackground = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Minimal.unity" }, target = target, locationPathName = output, options = BuildOptions.Development });
        if (result.summary.result != BuildResult.Succeeded) throw new Exception("Minimal build failed");
        Debug.Log("RF_MINIMAL_BUILD PASS " + target);
    }
}
