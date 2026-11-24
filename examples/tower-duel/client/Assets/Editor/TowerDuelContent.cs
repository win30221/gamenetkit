using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel.Editor
{
    public static class TowerDuelContent
    {
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Resources/TowerDuel/FX");
            Color[] colors={new Color(1,.24f,.035f),new Color(1,.82f,.22f),new Color(.32f,.85f,1),new Color(.2f,.24f,.32f)};
            string[] names={"Fire","Gold","Ice","Smoke"};
            for(int i=0;i<names.Length;i++)
            {
                string path="Assets/Resources/TowerDuel/FX/"+names[i]+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
                material.SetColor("_BaseColor",colors[i]);EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            PrepareAudio();
            PreparePreviewMaterial();
            ImportCardArt();
        }
        private static void PrepareAudio()
        {
            const string path="Assets/Resources/TowerDuel/Audio/DuelMixer.mixer";Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(path))return;
            Type type=null;foreach(var candidate in TypeCache.GetTypesDerivedFrom<UnityEngine.Audio.AudioMixer>())if(candidate.FullName=="UnityEditor.Audio.AudioMixerController"){type=candidate;break;}
            if(type==null)throw new InvalidOperationException("Unity Editor audio module is unavailable");
            var mixer=(UnityEngine.Audio.AudioMixer)type.GetMethod("CreateMixerControllerAtPath",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{path});
            var master=type.GetProperty("masterGroup").GetValue(mixer);
            var create=type.GetMethod("CreateNewGroup",new[]{typeof(string),typeof(bool)});
            if(create==null)throw new InvalidOperationException("Pinned Unity Editor mixer API changed");
            var children=Array.CreateInstance(master.GetType(),2);
            int index=0;foreach(string bus in new[]{"UI","SFX"}){var group=(UnityEngine.Object)create.Invoke(mixer,new object[]{bus,false});if(!AssetDatabase.Contains(group))AssetDatabase.AddObjectToAsset(group,mixer);children.SetValue(group,index++);}
            master.GetType().GetProperty("children").SetValue(master,children);
            EditorUtility.SetDirty((UnityEngine.Object)master);EditorUtility.SetDirty(mixer);AssetDatabase.SaveAssets();
        }
        private static void PreparePreviewMaterial()
        {
            const string path="Assets/Resources/TowerDuel/Materials/Preview.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_AlphaClip",0);
            material.SetFloat("_SrcBlend",(int)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha",(int)BlendMode.One);material.SetFloat("_DstBlendAlpha",(int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",(int)CullMode.Back);
            material.SetOverrideTag("RenderType","Transparent");material.renderQueue=(int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetShaderPassEnabled("ShadowCaster",false);material.SetShaderPassEnabled("DepthOnly",false);
            material.SetColor("_BaseColor",new Color(1,1,1,.42f));EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
        }
        private static void ImportCardArt()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var card in DuelRules.Load().cards)
            {
                const string root = "Assets/Resources/TowerDuel/";
                string path = root + "B3/Cards/" + card.id + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer)
                {
                    path = root + "Cards/" + card.id + ".png";
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (!importer) throw new InvalidOperationException("Missing illustrated card art: " + card.id);

                }
                // Legacy spell portraits must retain the same lossless native
                // resolution as B3 troop portraits, including NPOT dimensions.
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
                importer.alphaIsTransparency=false;importer.mipmapEnabled=false;importer.isReadable=false;
                importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.ClearPlatformTextureSettings("Standalone");
                importer.ClearPlatformTextureSettings("WebGL");
                importer.SaveAndReimport();
                var image=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(!image||image.width<256||image.height<256)throw new InvalidOperationException("Card art is still a prototype thumbnail: "+card.id);
            }
        }
    }
}
