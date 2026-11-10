using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Realtime.Examples.Presentation.Editor
{
    public static class LessonBuild
    {
        public static void Run(string name,string marker,BuildTarget target)
        {
            const string scene="Assets/Scenes/Lesson.unity";
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(scene)==null)throw new Exception("Lesson scene is missing");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scene,true)};
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders=graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach(string shaderName in new[]{"Standard","Sprites/Default"}){
                var shader=Shader.Find(shaderName);if(shader==null)throw new Exception("Missing shader: "+shaderName);
                bool found=false;for(int i=0;i<shaders.arraySize;i++)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader)found=true;
                if(!found)shaders.GetArrayElementAtIndex(shaders.arraySize++).objectReferenceValue=shader;
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            PlayerSettings.companyName="RealtimeFramework";PlayerSettings.productName=name;
            PlayerSettings.runInBackground=true;PlayerSettings.insecureHttpOption=InsecureHttpOption.DevelopmentOnly;
            if(target==BuildTarget.StandaloneWindows64)PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            else PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Disabled;
            string path=target==BuildTarget.WebGL?"Builds/WebGL":"Builds/Windows/"+name+"Demo.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},target=target,locationPathName=path,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Lesson build failed: "+report.summary.result);
            Debug.Log(marker+" "+target);
        }
    }
}
