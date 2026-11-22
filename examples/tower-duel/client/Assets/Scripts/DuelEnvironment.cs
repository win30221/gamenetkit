using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel
{
    [Serializable] public sealed class DuelEnvironmentDefinition { public DuelEnvironmentAtlas[] atlases; }
    [Serializable] public sealed class DuelEnvironmentAtlas
    {
        public string id,texture;
        public int width,height,columns,rows,baseline;
        public float[] frameWidths;
        [NonSerialized] public Sprite[] body,shadow;
    }

    // The same authored colour and paired ground frames are used for both
    // teams. Camera changes select a true front/back view, never flip lighting.
    public sealed class DuelEnvironment : IDisposable
    {
        private sealed class Item
        {
            public Transform Root;
            public SpriteRenderer Body,Shadow;
            public DuelEnvironmentAtlas Atlas;
            public float Width,Scale;
            public int Frame;
            public EntityState Building;
            public int Health=-1;
            public double Hit=double.NegativeInfinity;
            public readonly MaterialPropertyBlock Properties=new();
        }
        private readonly Transform root;
        private readonly Material bodyMaterial,shadowMaterial;
        private readonly Dictionary<string,DuelEnvironmentAtlas> atlases=new();
        private readonly Dictionary<long,Item> buildings=new();
        private readonly List<Item> items=new();
        private readonly List<Sprite> owned=new();
        private readonly SpriteRenderer ground, meadow;
        private readonly Sprite[] groundViews=new Sprite[2];
        private readonly Item wall;
        private int seat=-1;
        public int BuildingCount=>buildings.Count;
        public int ViewSeat=>seat;
        public Transform Root=>root;

        public DuelEnvironment(Transform parent,DuelMap map)
        {
            root=new GameObject("B3 wall gates battlefield").transform;root.SetParent(parent,false);
            bodyMaterial=new Material(Resources.Load<Material>("TowerDuel/B3/Sprite"));
            bodyMaterial.SetFloat("_Baked",1);bodyMaterial.SetFloat("_Enemy",0);
            shadowMaterial=Resources.Load<Material>("TowerDuel/B3/BakedShadow");
            var asset=Resources.Load<TextAsset>("TowerDuel/B3/Environment/definition");
            if(!asset)throw new InvalidOperationException("Prepare B3 environment content before playing");
            foreach(var atlas in JsonUtility.FromJson<DuelEnvironmentDefinition>(asset.text).atlases)
            {
                var texture=Resources.Load<Texture2D>(atlas.texture);
                if(!texture)throw new InvalidOperationException("Missing environment atlas "+atlas.texture);
                int count=atlas.frameWidths.Length;atlas.body=new Sprite[count];atlas.shadow=new Sprite[count];
                for(int i=0;i<count;i++)
                {
                    atlas.body[i]=Frame(texture,i%atlas.columns,i/atlas.columns,atlas,new Vector2(.5f,1-atlas.baseline/(float)atlas.height));
                    atlas.shadow[i]=Frame(texture,i%atlas.columns,i/atlas.columns+atlas.rows,atlas,new Vector2(.75f,.75f));
                }
                atlases.Add(atlas.id,atlas);
            }
            var meadowTex=Resources.Load<Texture2D>("TowerDuel/B3/Environment/meadow-flat");
            if(meadowTex)
            {
                var meadowSprite=Sprite.Create(meadowTex,new Rect(0,0,meadowTex.width,meadowTex.height),new Vector2(.5f,.5f),meadowTex.width/4f,0,SpriteMeshType.FullRect);
                owned.Add(meadowSprite);
                meadow=Renderer(root,"Meadow Backdrop",meadowSprite,bodyMaterial);
                meadow.drawMode=SpriteDrawMode.Tiled;
                meadow.size=new Vector2(90,90);
                meadow.transform.localPosition=new Vector3(0,-.005f,0);
            }
            for(int i=0;i<2;i++)
            {
                var texture=Resources.Load<Texture2D>("TowerDuel/B3/Environment/ground-"+(i==0?"south":"north"));
                if(!texture)throw new InvalidOperationException("Missing ground view");
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),texture.width/20f,0,SpriteMeshType.FullRect);
                groundViews[i]=sprite;owned.Add(sprite);
            }
            ground=Renderer(root,"Ground",groundViews[0],bodyMaterial);
            foreach(var entity in map.towers)
            {
                var item=Place(entity.kind=="keep"?"keep-static":"tower-static",entity.seat*2,entity.Position,
                               entity.kind=="keep"?3.8f:2.6f,entity.kind+" "+entity.id);
                item.Building=entity;buildings.Add(entity.id,item);
            }
            wall=Place("wall-gates",0,new Vector3(0,.02f,0),20,"Neutral wall and open gates");
            for(int side=-1;side<=1;side+=2)
            {
                Place("border-props",0,new Vector3(side*9,.02f,-12.6f),2.8f,"Border tree");
                Place("border-props",1,new Vector3(side*9.1f,.02f,12.6f),1.8f,"Border sapling");
                Place("border-props",0,new Vector3(side*9,.02f,4.8f),2.5f,"Border tree");
                Place("border-props",2,new Vector3(side*9,.02f,-5),1.1f,"Border bush");
                Place("border-props",3,new Vector3(side*8.9f,.02f,-1.6f),.9f,"Border stone");
                Place("border-props",4,new Vector3(side*8.1f,.02f,8.5f),1.05f,"Border rocks");

                Place("border-props",0,new Vector3(side*10.8f,.02f,-.2f),3.2f,"Wall terminus tree");
                Place("border-props",2,new Vector3(side*11.2f,.02f,1.5f),1.6f,"Wall terminus bush");
                Place("border-props",4,new Vector3(side*10.5f,.02f,-1.8f),1.1f,"Wall rocks");

                Place("border-props",0,new Vector3(side*13.5f,.02f,-10.5f),3.3f,"Outer tree");
                Place("border-props",1,new Vector3(side*14.2f,.02f,-4.5f),2.0f,"Outer sapling");
                Place("border-props",0,new Vector3(side*13.8f,.02f,6.2f),3.4f,"Outer tree");
                Place("border-props",2,new Vector3(side*14.5f,.02f,11.8f),2.0f,"Outer bush");
                Place("border-props",3,new Vector3(side*13.2f,.02f,-.8f),1.0f,"Outer stone");

                Place("border-props",0,new Vector3(side*17.5f,.02f,-13.5f),3.6f,"Far tree");
                Place("border-props",0,new Vector3(side*18.0f,.02f,-6.0f),3.5f,"Far tree");
                Place("border-props",1,new Vector3(side*17.8f,.02f,1.0f),2.3f,"Far sapling");
                Place("border-props",0,new Vector3(side*18.2f,.02f,9.0f),3.6f,"Far tree");
                Place("border-props",2,new Vector3(side*18.5f,.02f,-1.2f),2.0f,"Far bush");
                Place("border-props",0,new Vector3(side*22.5f,.02f,-7.5f),3.9f,"Deep forest tree");
                Place("border-props",0,new Vector3(side*22.0f,.02f,5.5f),3.8f,"Deep forest tree");
            }
            if(Camera.main)SetView(Camera.main,0);
        }
        private Sprite Frame(Texture2D texture,int col,int row,DuelEnvironmentAtlas atlas,Vector2 pivot)
        {
            var sprite=Sprite.Create(texture,new Rect(col*atlas.width,texture.height-(row+1)*atlas.height,atlas.width,atlas.height),pivot,atlas.width,0,SpriteMeshType.FullRect);
            owned.Add(sprite);return sprite;
        }
        private static SpriteRenderer Renderer(Transform parent,string name,Sprite sprite,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;return renderer;
        }
        private Item Place(string atlasID,int frame,Vector3 position,float width,string name)
        {
            var item=new Item{Root=new GameObject(name).transform,Atlas=atlases[atlasID],Width=width};
            item.Root.SetParent(root,false);item.Root.position=position;
            item.Body=Renderer(item.Root,"Body",null,bodyMaterial);
            item.Shadow=Renderer(item.Root,"Paired ground shadow",null,shadowMaterial);
            SetFrame(item,frame);items.Add(item);return item;
        }
        private static void SetFrame(Item item,int frame)
        {
            item.Frame=frame;item.Body.sprite=item.Atlas.body[frame];item.Shadow.sprite=item.Atlas.shadow[frame];
            item.Scale=item.Width/item.Atlas.frameWidths[frame];
        }
        public GameObject Building(long id)=>buildings[id].Root.gameObject;
        public int BuildingFrame(long id)=>buildings[id].Frame;
        public Vector3 Muzzle(long id)
        {
            var item=buildings[id];bool away=item.Frame%2==0;
            float y=item.Building.kind=="keep"?(away?120:196):(away?50:205);
            return item.Body.transform.TransformPoint(new Vector3(0,(item.Atlas.baseline-y)/item.Atlas.width,-.01f));
        }
        public void SetView(Camera camera,int viewSeat)
        {
            if(seat!=viewSeat)
            {
                seat=viewSeat;ground.sprite=groundViews[seat];SetFrame(wall,seat);
                foreach(var item in buildings.Values)SetFrame(item,item.Building.seat*2+(item.Building.seat==seat?0:1));
            }
            var forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized;
            ground.transform.rotation=Quaternion.LookRotation(Vector3.down,forward);
            if(meadow)meadow.transform.rotation=Quaternion.LookRotation(Vector3.down,forward);
            foreach(var item in items)
            {
                item.Body.transform.rotation=Quaternion.LookRotation(forward);
                item.Body.transform.localScale=new Vector3(item.Scale,item.Scale/Mathf.Max(.15f,camera.transform.up.y),item.Scale);
                item.Shadow.transform.rotation=Quaternion.LookRotation(Vector3.down,forward);
                item.Shadow.transform.localPosition=new Vector3(0,.002f,0);
                item.Shadow.transform.localScale=Vector3.one*(item.Scale*2.2f);
            }
        }
        public void Present(EntityState entity,double tick)
        {
            var item=buildings[entity.id];
            if(item.Health>=0&&entity.hp<item.Health)item.Hit=tick;
            item.Health=entity.hp;var properties=item.Properties;
            properties.SetFloat("_Baked",1);properties.SetFloat("_Enemy",0);
            properties.SetFloat("_Hit",Mathf.Clamp01(1-(float)(tick-item.Hit)/4));
            item.Body.SetPropertyBlock(properties);
        }
        public void Dispose()
        {
            foreach(var sprite in owned) Release(sprite);
            Release(bodyMaterial);
            if(root) Release(root.gameObject);
        }

        private static void Release(UnityEngine.Object item)
        {
            if (!item) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
        public void Reset()
        {
            foreach(var item in buildings.Values)
            {
                item.Health=-1;item.Hit=double.NegativeInfinity;item.Properties.SetFloat("_Hit",0);
                item.Body.SetPropertyBlock(item.Properties);item.Root.gameObject.SetActive(true);
            }
        }
    }
}
