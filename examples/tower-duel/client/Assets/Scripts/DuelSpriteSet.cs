using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDuel
{
    public enum DuelSpriteClip { Idle, Walk, Attack, Death }

    public sealed class DuelSpriteSet : ScriptableObject
    {
        public string Kind;
        public Texture2D Idle, Walk, Attack;
        public int TileSize = 192, Baseline = 176, ReferenceHeight = 160;
        public int WalkFrames = 6, AttackFrames = 6, ImpactFrame = 3;
        public float UnitHeight = 2.1f, WorldHeight = 1.05f;
        public float TeamMaskTop = 2f;
        public bool Baked;

        private Sprite[][] idle, walk, attack, idleShadow, walkShadow, attackShadow;
        public Sprite Frame(DuelSpriteClip clip, int direction, int frame)
        {
            // Runtime-created Sprite objects are destroyed when leaving Play Mode,
            // while the loaded ScriptableObject can remain in the Editor cache.
            if (idle == null || !idle[0][0])
            {
                idle = Slice(Idle, 1); walk = Slice(Walk, WalkFrames); attack = Slice(Attack, AttackFrames);
            }
            var frames = clip == DuelSpriteClip.Walk ? walk : clip == DuelSpriteClip.Attack ? attack : idle;
            return frames[Mathf.Clamp(direction, 0, 7)][Mathf.Clamp(frame, 0, frames[0].Length - 1)];
        }
        public Sprite ShadowFrame(DuelSpriteClip clip,int direction,int frame)
        {
            if(!Baked)return Frame(clip,direction,frame);
            if(idleShadow==null||!idleShadow[0][0])
            {
                idleShadow=Slice(Idle,1,true);walkShadow=Slice(Walk,WalkFrames,true);attackShadow=Slice(Attack,AttackFrames,true);
            }
            var frames=clip==DuelSpriteClip.Walk?walkShadow:clip==DuelSpriteClip.Attack?attackShadow:idleShadow;
            return frames[Mathf.Clamp(direction,0,7)][Mathf.Clamp(frame,0,frames[0].Length-1)];
        }
        private Sprite[][] Slice(Texture2D texture, int columns,bool shadow=false)
        {
            if (!texture || texture.width != TileSize * columns || texture.height != TileSize * (Baked?16:8))
                throw new InvalidOperationException("Invalid B3 sprite atlas: " + Kind + "/" + (texture ? texture.name : "missing"));
            var result = new Sprite[8][];
            var pivot = shadow?new Vector2(.75f,.75f):new Vector2(.5f, 1 - Baseline / (float)TileSize);
            for (int direction = 0; direction < 8; direction++)
            {
                result[direction] = new Sprite[columns];
                for (int frame = 0; frame < columns; frame++)
                {
                    var rect = new Rect(frame * TileSize, ((Baked&&!shadow?8:0)+7-direction) * TileSize, TileSize, TileSize);
                    var sprite = Sprite.Create(texture, rect, pivot, TileSize, 0, SpriteMeshType.FullRect);
                    sprite.name = Kind + "/" + texture.name + "/" + direction + "/" + frame;
                    result[direction][frame] = sprite;
                }
            }
            return result;
        }
        private void OnDisable()=>ClearFrames();
        public void ClearFrames()
        {
            foreach (var clip in new[] { idle, walk, attack, idleShadow, walkShadow, attackShadow })
                if (clip != null) foreach (var direction in clip) foreach (var sprite in direction)
                    if (sprite) { if (Application.isPlaying) Destroy(sprite); else DestroyImmediate(sprite); }
            idle = walk = attack = idleShadow = walkShadow = attackShadow = null;
        }
    }

    public static class DuelSpriteLibrary
    {
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Texture2D> Cards = new Dictionary<string, Texture2D>();
        private static GameObject singlePoseTemplates;
        private static readonly Dictionary<string, Sprite> SinglePoses = new Dictionary<string, Sprite>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache()
        {
            if(singlePoseTemplates) Release(singlePoseTemplates);
            foreach(var sprite in SinglePoses.Values)Release(sprite);
            SinglePoses.Clear();
            singlePoseTemplates=null;
            Prefabs.Clear();
            Cards.Clear();
        }
        private static void Release(UnityEngine.Object item)
        {
            if(!item)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
        private static GameObject CreateSinglePoseTemplate(string kind)
        {
            float height=kind switch { "falcon"=>.95f, "spearman"=>1.6f, "griffin"=>1.4f, "standard"=>1.65f, _=>0 };
            if(height==0)return null;
            var sprite=SinglePose(kind,height);
            if(!sprite)return null;
            if(!singlePoseTemplates)
            {
                singlePoseTemplates=new GameObject("Battle sprite templates"){hideFlags=HideFlags.HideAndDontSave};
                singlePoseTemplates.SetActive(false);
            }
            var prefab=new GameObject(kind){hideFlags=HideFlags.HideAndDontSave};
            prefab.transform.SetParent(singlePoseTemplates.transform,false);
            prefab.AddComponent<DuelUnitArt>().Height=height;
            var renderer=new GameObject("Facing").AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(prefab.transform,false);
            renderer.sprite=sprite;
            renderer.sharedMaterial=Resources.Load<Material>("TowerDuel/B3/Sprite");
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return prefab;
        }
        internal static Sprite SinglePose(string kind,float height)
        {
            if(SinglePoses.TryGetValue(kind,out var sprite)&&sprite)return sprite;
            var texture=Resources.Load<Texture2D>("TowerDuel/B3/Cards/"+kind);
            if(!texture)return null;
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),texture.height/height,0,SpriteMeshType.FullRect);
            SinglePoses[kind]=sprite;
            return sprite;
        }
        public static GameObject Prefab(string kind)
        {
            if (!Prefabs.TryGetValue(kind, out var prefab))
            {
                prefab = Resources.Load<GameObject>("TowerDuel/B3/Prefabs/" + kind);
                if(!prefab)prefab=CreateSinglePoseTemplate(kind);
                Prefabs.Add(kind, prefab);
            }
            return prefab;
        }
        public static bool Converted(string kind) => Prefab(kind) && Prefab(kind).GetComponent<DuelUnitArt>().Sprites;
        public static Texture2D Card(string kind)
        {
            if (!Cards.TryGetValue(kind, out var texture))
            {
                texture = Resources.Load<Texture2D>("TowerDuel/B3/Cards/" + kind);
                if (!texture) texture = Resources.Load<Texture2D>("TowerDuel/Cards/" + kind);
                Cards.Add(kind, texture);
            }
            return texture;
        }
    }
}
