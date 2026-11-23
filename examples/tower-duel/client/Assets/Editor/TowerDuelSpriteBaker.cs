using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TowerDuel.Editor
{
    // Offline export only. Runtime plays the finished color and ground frames.
    public static class TowerDuelSpriteBaker
    {
        private const string Root="Assets/Resources/TowerDuel/B3/Units";
        public const float ShadowScale=2f;
        private const float ShadowX=-.65f, ShadowZ=-.75055535f, ShadowOpacity=.26f;
        [Serializable] private sealed class Definition
        {
            public string kind, groundKind;
            public int tileSize,baseline,referenceHeight,walkFrames,attackFrames,impactFrame;
            public float unitHeight,worldHeight,teamMaskTop;
            public bool baked;
        }
        [MenuItem("Tower Duel/Bake fixed-light sprite sheets")]
        public static string BakeAll()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play Mode before baking sprite assets");
            int count=0;
            foreach(string folder in Directory.GetDirectories(Root))
            {
                string path=folder+"/definition.json";
                if(!File.Exists(path))continue;
                var definition=JsonUtility.FromJson<Definition>(File.ReadAllText(path));
                foreach(string clip in new[]{"idle","walk","attack"})Bake(folder,clip,definition);
                definition.baked=true;
                File.WriteAllText(path,JsonUtility.ToJson(definition,true)+"\n");
                string config=Path.GetFullPath("../art/b3-production/"+definition.kind+".json");
                if (File.Exists(config))
                {
                    string text=File.ReadAllText(config);
                    text=Regex.IsMatch(text,"\"baked\"\\s*:")
                        ?Regex.Replace(text,"(\"baked\"\\s*:)\\s*(true|false)","$1 true")
                        :text.Replace("\"kind\":","\"baked\": true, \"kind\":");
                    File.WriteAllText(config,text);
                }
                count++;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return "Baked "+count+" characters: upper eight rows are lit bodies; lower eight rows are matching ground shadows";
        }
        private static void Bake(string folder,string clip,Definition definition)
        {
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            Texture2D output=null;
            try
            {
                if(!source.LoadImage(File.ReadAllBytes(folder+"/"+clip+".png")))throw new InvalidOperationException("Cannot read source atlas");
                int tile=definition.tileSize,w=source.width,h=source.height;
                if(h!=tile*8||w%tile!=0)throw new InvalidOperationException("Unexpected source grid: "+folder+"/"+clip);
                var original=source.GetPixels32();var pixels=new Color32[w*h*2];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    pixels[(y+h)*w+x]=Lit(original[y*w+x],x%tile,y%tile,definition);
                for(int row=0;row<8;row++)for(int column=0;column<w/tile;column++)
                {
                    var alpha=new float[tile*tile];
                    for(int y=0;y<tile;y++)for(int x=0;x<tile;x++)
                    {
                        float u=((x+.5f)/tile-.75f)*ShadowScale;
                        float v=((y+.5f)/tile-.75f)*ShadowScale;
                        float bodyY=v/ShadowZ;
                        float bodyX=u-ShadowX*bodyY;
                        float sx=(bodyX+.5f)*tile-.5f;
                        float sy=bodyY*tile+tile-definition.baseline-.5f;
                        alpha[y*tile+x]=SampleAlpha(original,w,tile,column,row,sx,sy)*ShadowOpacity;
                    }
                    // Small fixed soft edge, exported once rather than blurred in game.
                    for(int y=0;y<tile;y++)for(int x=0;x<tile;x++)
                    {
                        float sum=0;
                        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                            if(x+dx>=0&&x+dx<tile&&y+dy>=0&&y+dy<tile)
                                sum+=alpha[(y+dy)*tile+x+dx]*(dx==0?2:1)*(dy==0?2:1)/16f;
                        pixels[(row*tile+y)*w+column*tile+x]=new Color32(7,10,17,(byte)Mathf.RoundToInt(sum*255));
                    }
                }
                output=new Texture2D(w,h*2,TextureFormat.RGBA32,false,true);output.SetPixels32(pixels);output.Apply();
                File.WriteAllBytes(folder+"/"+clip+"-baked.png",output.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(source);if(output)UnityEngine.Object.DestroyImmediate(output); }
        }
        private static Color32 Lit(Color32 color,int x,int y,Definition d)
        {
            if(color.a==0)return color;
            float nx=Mathf.Clamp((x+.5f-d.tileSize*.5f)/(d.referenceHeight*.31f),-1,1);
            float ny=Mathf.Clamp((y+.5f-d.tileSize+d.baseline-d.referenceHeight*.46f)/(d.referenceHeight*.55f),-1,1);
            float amount=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.45f,.45f,nx*.75f+ny*.25f));
            Color light=Color.Lerp(new Color(.82f,.85f,.94f),new Color(1.025f,1.015f,.99f),amount);
            float ink=Mathf.SmoothStep(0,1,Mathf.InverseLerp(30,85,Mathf.Max(color.r,Mathf.Max(color.g,color.b))));
            light=Color.Lerp(Color.white,light,ink);
            return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(color.r*light.r),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(color.g*light.g),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(color.b*light.b),0,255),color.a);
        }
        private static float SampleAlpha(Color32[] pixels,int width,int tile,int column,int row,float x,float y)
        {
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);
            float fx=x-ix,fy=y-iy;
            float Read(int px,int py)=>px<0||py<0||px>=tile||py>=tile?0:pixels[(row*tile+py)*width+column*tile+px].a/255f;
            return Mathf.Lerp(Mathf.Lerp(Read(ix,iy),Read(ix+1,iy),fx),Mathf.Lerp(Read(ix,iy+1),Read(ix+1,iy+1),fx),fy);
        }
    }
}
