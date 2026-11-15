using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TowerDuel.Solo.Editor
{
    public static class TowerDuelSoloBuild
    {
        [MenuItem("Tower Duel Solo/Build optional Windows test player")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before building.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.StandaloneWindows64)throw new InvalidOperationException("Open this Editor with -buildTarget Win64.");
            string output=Path.GetFullPath("../Builds/Solo");Directory.CreateDirectory(output);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/Scenes/TowerDuel.unity"},locationPathName=Path.Combine(output,"TowerDuel.exe"),
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development,
                extraScriptingDefines=new[]{"TOWER_DUEL_SOLO"}
            });
            File.WriteAllText(Path.Combine(output,"build-result.json"),JsonUtility.ToJson(new Result{
                result=result.summary.result.ToString(),errors=result.summary.totalErrors,seconds=result.summary.totalTime.TotalSeconds},true));
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Solo test Player build failed.");
            Debug.Log("Optional solo test Player built without regenerating the arena or normal Player.");
        }
        [Serializable] private sealed class Result {public string result;public int errors;public double seconds;}

        [MenuItem("Tower Duel Solo/Build optional WebGL test player")]
        public static void BuildWeb()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorUserBuildSettings.activeBuildTarget!=BuildTarget.WebGL)
                throw new InvalidOperationException("Open an idle Editor with -buildTarget WebGL.");
            if(PlayerSettings.GetScriptingBackend(NamedBuildTarget.WebGL)!=ScriptingImplementation.IL2CPP||PlayerSettings.WebGL.compressionFormat!=WebGLCompressionFormat.Gzip)
                throw new InvalidOperationException("Use the project's existing Web release settings before building.");
            UnityEditor.WebGL.UserBuildSettings.codeOptimization=UnityEditor.WebGL.WasmCodeOptimization.DiskSize;
            string output=Path.GetFullPath("../Builds/SoloWeb/WebGL");Directory.CreateDirectory(output);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/Scenes/TowerDuel.unity"},locationPathName=output,target=BuildTarget.WebGL,
                options=BuildOptions.None,extraScriptingDefines=new[]{"TOWER_DUEL_SOLO"}
            });
            File.WriteAllText(Path.Combine(output,"../build-result.json"),JsonUtility.ToJson(new Result{
                result=result.summary.result.ToString(),errors=result.summary.totalErrors,seconds=result.summary.totalTime.TotalSeconds},true));
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Solo WebGL build failed.");
            string index=Path.Combine(output,"index.html");
            string html=File.ReadAllText(index).Replace("<title>雙橋對決 · Tower Duel</title>","<title>雙橋對決 · 單人測試</title>")
                .Replace("<h1>雙橋對決</h1>","<h1>單人測試場</h1>")
                .Replace("與朋友開啟同一網址，以房間碼加入對戰。","一人即可開始，選擇電腦對戰或自由測試。")
                .Replace("</head>","<meta name=\"tower-duel-mode\" content=\"solo-plugin\"></head>");
            File.WriteAllText(index,html);
            File.WriteAllText(Path.Combine(output,"solo-build.txt"),"TowerDuel Solo protocol 3 / eight-cards-1\n");
            Debug.Log("Optional solo WebGL built separately from the multiplayer Player: "+output);
        }

        public static void VerifyNormalBuild()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before verification.");
            string output=Path.GetFullPath("../../../artifacts/tower-duel/solo-isolation");Directory.CreateDirectory(output);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/Scenes/TowerDuel.unity"},locationPathName=Path.Combine(output,"TowerDuel.exe"),
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development,
                extraScriptingDefines=Array.Empty<string>()
            });
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Normal Player isolation build failed.");
            string assembly=Path.Combine(output,"TowerDuel_Data/Managed/Assembly-CSharp.dll");
            string metadata=System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(assembly));
            if(!metadata.Contains("TowerDuelClient")||metadata.Contains("TowerDuelSoloPlugin"))throw new InvalidOperationException("Normal Player unexpectedly includes the solo adapter.");
            File.WriteAllText(Path.Combine(output,"isolation-result.json"),"{\"passed\":true,\"normalPlayerIncludesSolo\":false}");
            Debug.Log("PASS: normal Player builds successfully without the optional solo adapter.");
        }
    }
}
