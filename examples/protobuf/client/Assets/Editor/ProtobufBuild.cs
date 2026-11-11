using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProtobufBuild
{
    public static void Verify() { CompatibilityCheck.Run(); Debug.Log("PROTOBUF_EDITOR_COMPATIBILITY PASS"); }
    public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/ProtobufExample.exe", false);
    public static void WindowsIl2Cpp() => Build(BuildTarget.StandaloneWindows64, "Builds/WindowsIL2CPP/ProtobufExample.exe");
    public static void Web() => Build(BuildTarget.WebGL, "Builds/WebGL");
    private static void Build(BuildTarget target, string output, bool il2cpp = true)
    {
        Verify();
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Protobuf.unity");
        PlayerSettings.companyName = "GameNetKit"; PlayerSettings.productName = "Protobuf Example";
        PlayerSettings.runInBackground = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        var namedTarget = target == BuildTarget.WebGL ? NamedBuildTarget.WebGL : NamedBuildTarget.Standalone;
        PlayerSettings.SetApiCompatibilityLevel(namedTarget, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetScriptingBackend(namedTarget, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Minimal);
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) throw new Exception("Unlit/Color shader unavailable");
        bool found = false;
        for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
        if (!found) { int i = shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; graphics.ApplyModifiedPropertiesWithoutUndo(); }
        AssetDatabase.SaveAssets();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/Protobuf.unity" }, target = target,
            locationPathName = output, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Protobuf Player build failed");
        Debug.Log("PROTOBUF_BUILD PASS " + target);
    }
}
