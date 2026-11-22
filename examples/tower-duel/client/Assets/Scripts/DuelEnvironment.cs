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
        private readonly Dictionary<Texture2D,Material> bodyTextures=new();
        private readonly Dictionary<Texture2D,Material> shadowTextures=new();
        private readonly Dictionary<string,DuelEnvironmentAtlas> atlases=new();
        private readonly Dictionary<long,Item> buildings=new();
        private readonly List<Item> items=new();
        private readonly List<Sprite> owned=new();
        private readonly SpriteRenderer ground, meadow;
        private readonly Sprite[] groundViews=new Sprite[2];
        private readonly List<Bounds> wallBounds=new();
        private int seat=-1;
        public int BuildingCount=>buildings.Count;
        public int ViewSeat=>seat;
        public Transform Root=>root;

        public DuelEnvironment(Transform parent,DuelMap map)
        {
            root=new GameObject("B3 wall gates battlefield").transform;root.SetParent(parent,false);
            bodyMaterial=Resources.Load<Material>("TowerDuel/B3/Sprite");
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
                meadow.transform.localScale=new Vector3(22.5f,22.5f,1);
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
            BuildWall(map);
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
        private void BuildWall(DuelMap map)
        {
            if(!map.HasWall)return;
            float thickness=map.wallHalfWidth*2;
            var atlas=atlases["wall-gates"];
            var texture=atlas.body[0].texture;
            var stoneTop=Resources.Load<Texture2D>("TowerDuel/B3/Environment/side-wall-art");
            if(!stoneTop)throw new InvalidOperationException("Missing B3 side-wall art");

            // Original wall artwork: first opaque row 25, foot baseline 236.
            // The roof joins the masonry just under the projecting battlements.
            const float paintedHeight=211f;
            float roofHeight=map.wallHeight*(atlas.baseline-100)/paintedHeight;
            float lintelHeight=map.wallHeight*(atlas.baseline-62)/paintedHeight;
            int[] cuts={16,391,649,1399,1657,2032};
            float[] gates=(float[])map.bridges.Clone();
            Array.Sort(gates);
            float[] edges={-map.width/2,gates[0]-map.bridgeHalfWidth,gates[0]+map.bridgeHalfWidth,
                gates[1]-map.bridgeHalfWidth,gates[1]+map.bridgeHalfWidth,map.width/2};
            var cap=WallSprite(stoneTop,new Rect(0,0,stoneTop.width,stoneTop.height),new Vector2(.5f,.5f),stoneTop.width);
            var masonry=WallSprite(texture,new Rect(754,texture.height-216,52,75),new Vector2(.5f,0));
            var gold=WallSprite(texture,new Rect(468,texture.height-84,96,14),new Vector2(.5f,.5f));
            for(int i=0;i<5;i++)
            {
                float width=edges[i+1]-edges[i];
                float x=(edges[i]+edges[i+1])/2;
                bool gate=i%2!=0;
                for(int view=0;view<2;view++)
                {
                    int slice=view==0?i:4-i;
                    var rect=new Rect(cuts[slice],texture.height-(view+1)*atlas.height,cuts[slice+1]-cuts[slice],atlas.height);
                    var sprite=WallSprite(texture,rect,new Vector2(.5f,1-atlas.baseline/(float)atlas.height));
                    var face=Renderer(root,gate?"Open gate face":"Castle wall face",sprite,bodyMaterial);
                    face.transform.localPosition=new Vector3(x,.02f,(view==0?-1:1)*map.wallHalfWidth);
                    face.transform.localRotation=Quaternion.Euler(0,view*180,0);
                    face.transform.localScale=new Vector3(width/rect.width,map.wallHeight/paintedHeight,1);
                }
                if(gate)
                {
                    var lintel=Renderer(root,"Gate lintel art",gold,bodyMaterial);
                    lintel.transform.localPosition=new Vector3(x,.02f+lintelHeight,0);
                    lintel.transform.localRotation=Quaternion.Euler(90,0,0);
                    lintel.transform.localScale=new Vector3(width/gold.rect.width,thickness/gold.rect.height,1);
                    continue;
                }
                PlaceWallTop("Castle wall top",cap,new Vector3(x,.02f+roofHeight,0),thickness,width,Vector3.right);
                foreach(float edge in new[]{edges[i],edges[i+1]})
                    PlaceWallMasonry("Wall end art",masonry,new Vector3(edge,.02f,0),thickness,roofHeight,90);
                wallBounds.Add(new Bounds(new Vector3(x,.02f+map.wallHeight/2,0),new Vector3(width,map.wallHeight,thickness)));
            }
            foreach(int side in new[]{-1,1})
            {
                float x=side*(map.width/2+thickness/2);
                PlaceWallTop("Arena side wall",cap,new Vector3(x,.02f+roofHeight,0),thickness,map.length,Vector3.forward);
                foreach(int end in new[]{-1,1})
                    PlaceWallMasonry("Side wall end art",masonry,new Vector3(x,.02f,end*map.length/2),thickness,roofHeight,0);
                wallBounds.Add(new Bounds(new Vector3(x,.02f+map.wallHeight/2,0),new Vector3(thickness,map.wallHeight,map.length)));
            }
        }
        private Sprite WallSprite(Texture2D texture,Rect rect,Vector2 pivot,float pixelsPerUnit=1)
        {
            var sprite=Sprite.Create(texture,rect,pivot,pixelsPerUnit,0,SpriteMeshType.FullRect);
            owned.Add(sprite);
            return sprite;
        }
        private void PlaceWallTop(string name,Sprite sprite,Vector3 at,float width,float length,Vector3 along)
        {
            // Each strip draws one sprite rectangle with its own bound texture.
            int count=Mathf.Max(1,Mathf.CeilToInt(length/sprite.bounds.size.y));
            float stripLength=length/count;
            var rotation=Quaternion.LookRotation(Vector3.down,along);
            for(int i=0;i<count;i++)
            {
                var renderer=Renderer(root,name,sprite,bodyMaterial);
                renderer.transform.localPosition=at+along*(stripLength*(i+.5f)-length/2);
                renderer.transform.localRotation=rotation;
                renderer.transform.localScale=new Vector3(width/sprite.bounds.size.x,stripLength/sprite.bounds.size.y,1);
            }
        }
        private void PlaceWallMasonry(string name,Sprite sprite,Vector3 at,float width,float height,float yaw)
        {
            var renderer=Renderer(root,name,sprite,bodyMaterial);
            renderer.transform.localPosition=at;
            renderer.transform.localRotation=Quaternion.Euler(0,yaw,0);
            renderer.transform.localScale=new Vector3(width/sprite.rect.width,height/sprite.rect.height,1);
            renderer.color=new Color(.78f,.72f,.68f);
        }
        public bool WallOccludes(Camera camera,Vector3 point)
        {
            // ScreenPointToRay works for both the orthographic arena and editor views.
            var ray=camera.ScreenPointToRay(camera.WorldToScreenPoint(point));
            float length=Vector3.Dot(point-ray.origin,ray.direction);
            foreach(var wall in wallBounds)
                if(wall.IntersectRay(ray,out float distance)&&distance<length-.001f)return true;
            return false;
        }
        private Sprite Frame(Texture2D texture,int col,int row,DuelEnvironmentAtlas atlas,Vector2 pivot)
        {
            var sprite=Sprite.Create(texture,new Rect(col*atlas.width,texture.height-(row+1)*atlas.height,atlas.width,atlas.height),pivot,atlas.width,0,SpriteMeshType.FullRect);
            owned.Add(sprite);return sprite;
        }
        private Material BoundMaterial(Texture2D texture,Material template)
        {
            bool body=template==bodyMaterial;
            var cache=body?bodyTextures:shadowTextures;
            if(cache.TryGetValue(texture,out var material))return material;
            material=new Material(template){name=template.name+" / "+texture.name};
            if(body)
            {
                // Environment atlases must not depend on SpriteRenderer's shared
                // _MainTex state. Keep the atlas binding on this material.
                material.SetTexture("_B3EnvironmentTex",texture);
                material.SetFloat("_B3UseEnvironmentTex",1);
                material.SetFloat("_Baked",1);
                material.SetFloat("_Enemy",0);
            }
            else material.SetTexture("_MainTex",texture);
            cache.Add(texture,material);
            return material;
        }
        private SpriteRenderer Renderer(Transform parent,string name,Sprite sprite,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=BoundMaterial(sprite.texture,material);
            renderer.shadowCastingMode=ShadowCastingMode.Off;return renderer;
        }
        private Item Place(string atlasID,int frame,Vector3 position,float width,string name)
        {
            var item=new Item{Root=new GameObject(name).transform,Atlas=atlases[atlasID],Width=width};
            item.Root.SetParent(root,false);item.Root.position=position;
            item.Body=Renderer(item.Root,"Body",item.Atlas.body[frame],bodyMaterial);
            item.Shadow=Renderer(item.Root,"Paired ground shadow",item.Atlas.shadow[frame],shadowMaterial);
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
                seat=viewSeat;ground.sprite=groundViews[seat];
                ground.sharedMaterial=BoundMaterial(ground.sprite.texture,bodyMaterial);
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
            foreach(var material in bodyTextures.Values)Release(material);
            foreach(var material in shadowTextures.Values)Release(material);
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
