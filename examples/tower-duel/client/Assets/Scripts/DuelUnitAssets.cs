using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace TowerDuel
{
    [Serializable]
    internal sealed class DuelUnitBundleRecord
    {
        public string kind,sha256;
        public long bytes;
        public uint crc;
        public string RelativePath => "assets/bundles/"+sha256+"/"+kind+".bundle";
    }

    [Serializable]
    internal sealed class DuelUnitBundleManifest
    {
        public int version;
        public DuelUnitBundleRecord[] bundles;
    }

    // The prefabs, animation metadata, materials and shaders stay in the Player.
    // Only the three atlas textures of each form are streamed. No runtime objects
    // may be spawned before all legal battle forms have their textures.
    public sealed class DuelUnitAssets
    {
        private readonly Dictionary<string,DuelSpriteSet> sets=new Dictionary<string,DuelSpriteSet>(StringComparer.Ordinal);
        private readonly List<DuelUnitBundleRecord> pending=new List<DuelUnitBundleRecord>();
        private int total,loaded;
        private string priority,error;
        private bool loading,valid;
        public bool Ready {get;private set;}
        public bool CanStart=>valid&&!loading&&!Ready&&pending.Count>0;
        public bool CanRetry=>CanStart&&!string.IsNullOrEmpty(error);
        public string Status=>Ready?"":!string.IsNullOrEmpty(error)?error:
            "正在下載戰鬥資源 "+loaded+"/"+total+"…";

        public DuelUnitAssets(DuelRules rules)
        {
            try
            {
                foreach(var set in Resources.LoadAll<DuelSpriteSet>("TowerDuel/B3"))
                    sets.Add(set.Kind,set);
                foreach(var card in rules.cards)
                    if(!card.IsSpell&&!sets.ContainsKey(card.id))
                        throw new InvalidOperationException("Missing unit metadata "+card.id);
                var receipt=Resources.Load<TextAsset>("TowerDuel/WebUnitBundles");
                var manifest=receipt?JsonUtility.FromJson<DuelUnitBundleManifest>(receipt.text):null;
                if(manifest==null||manifest.version==0)
                {
                    foreach(var set in sets.Values)
                        if(!set.HasAtlases)throw new InvalidOperationException("Missing local atlas "+set.Kind);
                    Ready=true;valid=true;return;
                }
                if(manifest.version!=1||manifest.bundles==null||manifest.bundles.Length!=sets.Count)
                    throw new InvalidOperationException("Invalid unit bundle manifest");
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var record in manifest.bundles)
                {
                    if(record==null||string.IsNullOrEmpty(record.kind)||string.IsNullOrEmpty(record.sha256)||
                        !Regex.IsMatch(record.kind,@"\A[a-z0-9-]+\z")||
                        !Regex.IsMatch(record.sha256,@"\A[0-9a-f]{64}\z")||record.bytes<=0||
                        !sets.ContainsKey(record.kind)||!seen.Add(record.kind)||sets[record.kind].HasAtlases)
                        throw new InvalidOperationException("Invalid or duplicate remote unit entry");
                    pending.Add(record);
                }
                total=pending.Count;valid=true;
            }
            catch(Exception ex)
            {
                Debug.LogError("Unit bundle setup: "+ex);
                error="戰鬥資源版本不相容，請重新整理遊戲。";
            }
        }

        public bool IsLoaded(string kind)=>sets.TryGetValue(kind,out var set)&&set.HasAtlases;
        public void Prioritize(string kind){if(!IsLoaded(kind))priority=kind;}

        private bool Attach(DuelUnitBundleRecord record,Texture2D idle,Texture2D walk,Texture2D attack)
        {
            var set=sets[record.kind];
            int height=set.TileSize*(set.Baked?16:8);
            if(!idle||!walk||!attack||
               idle.width!=set.TileSize*set.FrameCount(DuelSpriteClip.Idle)||idle.height!=height||
               walk.width!=set.TileSize*set.WalkFrames||walk.height!=height||
               attack.width!=set.TileSize*set.AttackFrames||attack.height!=height)return false;
            set.ClearFrames();set.Idle=idle;set.Walk=walk;set.Attack=attack;
            return true;
        }

        public IEnumerator Preload()
        {
            if(!CanStart)yield break;
            loading=true;error=null;
            try
            {
                while(pending.Count>0)
                {
                    int index=pending.FindIndex(x=>x.kind==priority);
                    if(index<0)index=0;
                    var record=pending[index];priority=null;
                    bool success=false;string failure="";
                    string url=new Uri(new Uri(Application.absoluteURL),"/"+record.RelativePath).AbsoluteUri;
                    for(int attempt=0;attempt<3&&!success;attempt++)
                    {
                        using(var request=UnityWebRequestAssetBundle.GetAssetBundle(url,record.crc))
                        {
                            request.timeout=45;
                            yield return request.SendWebRequest();
                            if(request.result!=UnityWebRequest.Result.Success)
                                failure=request.error;
                            else
                            {
                                AssetBundle bundle=null;
                                try {bundle=DownloadHandlerAssetBundle.GetContent(request);}
                                catch(Exception ex){failure=ex.Message;}
                                if(bundle)
                                {
                                    try
                                    {
                                        var idle=bundle.LoadAssetAsync<Texture2D>("idle");
                                        yield return idle;
                                        var walk=bundle.LoadAssetAsync<Texture2D>("walk");
                                        yield return walk;
                                        var attack=bundle.LoadAssetAsync<Texture2D>("attack");
                                        yield return attack;
                                        success=Attach(record,idle.asset as Texture2D,walk.asset as Texture2D,attack.asset as Texture2D);
                                        if(!success)failure="Atlas dimensions or asset names do not match";
                                    }
                                    finally {bundle.Unload(!success);}
                                }
                                else if(string.IsNullOrEmpty(failure))failure="Missing AssetBundle";
                            }
                        }
                        if(!success&&attempt<2)yield return new WaitForSecondsRealtime(attempt+1);
                    }
                    if(!success)
                    {
                        Debug.LogError("Unit bundle "+record.kind+": "+failure);
                        error="戰鬥資源下載失敗，請點選重試。";
                        yield break;
                    }
                    pending.RemoveAt(index);loaded++;
                    yield return null;
                }
                Ready=true;
            }
            finally
            {
                loading=false;
                if(!Ready&&string.IsNullOrEmpty(error))error="戰鬥資源載入失敗，請點選重試。";
            }
        }
    }
}
