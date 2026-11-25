using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TowerDuel.Editor
{
    public static class TowerDuelEnvironmentContent
    {
        [Serializable] private sealed class Manifest { public SourceAsset[] assets; }
        [Serializable] private sealed class SourceAsset { public string id,path;public Technical technical; }
        [Serializable] private sealed class Technical
        {
            public int[] tileSize; public int columns,bodyRows;
            public float[] bodyPivot;public Frame[] frames;
        }
        [Serializable] private sealed class Frame { public int[] bodyBounds; }
        [MenuItem("Tower Duel/Prepare B3 environment content")]
        public static string Prepare()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play Mode before importing environment art");
            const string target="Assets/Resources/TowerDuel/B3/Environment";
            string source=Path.GetFullPath("../art/environment-b3-v1");
            Directory.CreateDirectory(target);
            var list=new List<DuelEnvironmentAtlas>();
            foreach(var asset in JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(source,"manifest.json"))).assets)
            {
                string file=Path.GetFileName(asset.path),path=target+"/"+file;
                File.Copy(Path.Combine(source,asset.path),path,true);
                if(asset.technical.tileSize==null||asset.technical.tileSize.Length!=2)continue;
                var technical=asset.technical;var widths=new float[technical.frames.Length];
                for(int i=0;i<widths.Length;i++)widths[i]=(technical.frames[i].bodyBounds[2]-technical.frames[i].bodyBounds[0])/(float)technical.tileSize[0];
                list.Add(new DuelEnvironmentAtlas{id=asset.id,texture="TowerDuel/B3/Environment/"+Path.GetFileNameWithoutExtension(file),
                    width=technical.tileSize[0],height=technical.tileSize[1],columns=technical.columns,rows=technical.bodyRows,
                    baseline=Mathf.RoundToInt((1-technical.bodyPivot[1])*technical.tileSize[1]),frameWidths=widths});
            }
            File.WriteAllText(target+"/definition.json",JsonUtility.ToJson(new DuelEnvironmentDefinition{atlases=list.ToArray()},true)+"\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string path in Directory.GetFiles(target,"*.png"))
            {
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(!importer)throw new InvalidOperationException("Missing environment texture importer: "+path);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
                importer.mipmapEnabled=false;importer.isReadable=false;importer.wrapMode=TextureWrapMode.Clamp;
                importer.filterMode=FilterMode.Bilinear;importer.npotScale=TextureImporterNPOTScale.None;
                importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;
                TowerDuelSpriteContent.ConfigureWebTexture(importer);
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();return "B3 environment imported: 6 textures and four atlases";
        }
    }
}
