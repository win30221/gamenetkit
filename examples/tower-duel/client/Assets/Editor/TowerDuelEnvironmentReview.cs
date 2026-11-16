using System;
using System.Collections.Generic;
using System.IO;
using TowerDuel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TowerDuel.Editor
{
    // Disposable art composition only. Never edits the playable map or scene.
    public static class TowerDuelEnvironmentReview
    {
        private static readonly List<UnityEngine.Object> Owned = new();
        private static string Art => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/environment-b3-v1"));
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../artifacts/tower-duel/environment-b3-v1/review"));
        private static Material bodyMaterial, shadowMaterial;

        [MenuItem("Tower Duel/Capture B3 environment artwork")]
        public static string Capture()
        {
            if (Application.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Environment review requires a clean idle Editor");
            string previous = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                bodyMaterial = Own(new Material(Shader.Find("TowerDuel/B3Sprite")));
                bodyMaterial.SetFloat("_Baked", 1);
                bodyMaterial.SetFloat("_Enemy", 0);
                shadowMaterial = Resources.Load<Material>("TowerDuel/B3/BakedShadow");
                if (!shadowMaterial) throw new InvalidOperationException("Missing shared B3 ground shadow material");
                for (int seat = 0; seat < 2; seat++)
                {
                    var root = new GameObject("Disposable environment art view " + seat);
                    try
                    {
                        var camera = MakeCamera(root.transform, seat);
                        Build(root.transform, camera, seat);
                        Save(camera, Path.Combine(Output, seat == 0 ? "south-blue-view.png" : "north-red-view.png"), 1024, 1536);
                        camera.orthographicSize = 5.3f;
                        camera.transform.position = new Vector3(0, 1.3f, 0) - camera.transform.forward * 50;
                        Save(camera, Path.Combine(Output, seat == 0 ? "south-gates-detail.png" : "north-gates-detail.png"), 1440, 900);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                File.WriteAllText(Path.Combine(Output, "native-review.json"),
                    "{\n  \"engine\":\"Unity " + Application.unityVersion + "\",\n" +
                    "  \"pipeline\":\"URP native camera render\",\n" +
                    "  \"cameraPitch\":60,\n  \"mapWorldSize\":[20,30],\n" +
                    "  \"south\":{\"own\":\"blue-away\",\"enemy\":\"red-toward\",\"mud\":\"right\"},\n" +
                    "  \"north\":{\"own\":\"red-away\",\"enemy\":\"blue-toward\",\"mud\":\"left\"},\n" +
                    "  \"gameplayMapChanged\":false,\n  \"movementOrVisibilityRulesImplemented\":false\n}\n");
                return Output;
            }
            finally
            {
                foreach (var item in Owned) if (item) UnityEngine.Object.DestroyImmediate(item);
                Owned.Clear();
                if (string.IsNullOrEmpty(previous)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.OpenScene(previous);
            }
        }

        private static T Own<T>(T item) where T : UnityEngine.Object { Owned.Add(item); return item; }

        private static Camera MakeCamera(Transform root, int seat)
        {
            var go = new GameObject("Art camera");
            go.transform.SetParent(root, false); go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 17.1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.54f, .69f, .23f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.rotation = Quaternion.Euler(60, seat * 180, 0);
            camera.transform.position = new Vector3(0, 1.2f, 0) - camera.transform.forward * 50;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            return camera;
        }

        private static Texture2D Load(string relative)
        {
            var texture = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, false));
            if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(Art, relative))))
                throw new InvalidOperationException("Cannot decode " + relative);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static Sprite Frame(Texture2D texture, int column, int row, int width, int height, Vector2 pivot)
        {
            return Own(Sprite.Create(texture, new Rect(column*width, texture.height-(row+1)*height, width, height),
                                    pivot, width, 0, SpriteMeshType.FullRect));
        }

        private static float Width(Sprite sprite)
        {
            var rect = sprite.textureRect;
            var pixels = sprite.texture.GetPixels32();
            int min = (int)rect.width, max = 0;
            for (int y = (int)rect.y; y < rect.yMax; y++)
                for (int x = (int)rect.x; x < rect.xMax; x++)
                    if (pixels[y*sprite.texture.width+x].a >= 128) { min = Math.Min(min, x-(int)rect.x); max = Math.Max(max, x-(int)rect.x); }
            return (max-min+1)/rect.width;
        }

        private static SpriteRenderer Renderer(Transform root, string name, Sprite sprite, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        private static void Place(Transform root, Camera camera, Texture2D sheet, int frame, int columns, int rows,
                                  int width, int height, int baseline, Vector3 position, float visibleWidth, string name)
        {
            int col = frame % columns, row = frame / columns;
            var body = Frame(sheet, col, row, width, height, new Vector2(.5f, 1-baseline/(float)height));
            float scale = visibleWidth / Width(body);
            var renderer = Renderer(root, name, body, bodyMaterial);
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            renderer.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            renderer.transform.localScale = new Vector3(scale, scale/camera.transform.up.y, scale);
            var shadow = Renderer(root, name + " baked ground shadow",
                Frame(sheet, col, row+rows, width, height, new Vector2(.75f, .75f)), shadowMaterial);
            shadow.transform.SetPositionAndRotation(new Vector3(position.x, .008f, position.z), Quaternion.LookRotation(Vector3.down, forward));
            shadow.transform.localScale = Vector3.one * (scale*2.2f);
        }

        private static void Build(Transform root, Camera camera, int seat)
        {
            var ground = Load(seat == 0 ? "terrain/ground-south.png" : "terrain/ground-north.png");
            var sprite = Own(Sprite.Create(ground, new Rect(0,0,ground.width,ground.height), new Vector2(.5f,.5f), ground.width/20f));
            var floor = Renderer(root, "Separate ground artwork", sprite, bodyMaterial);
            floor.transform.rotation = Quaternion.LookRotation(Vector3.down, seat == 0 ? Vector3.forward : Vector3.back);
            var keep = Load("buildings/keep-static-baked.png");
            var tower = Load("buildings/tower-static-baked.png");
            for (int team = 0; team < 2; team++)
            {
                int frame = team*2 + (team == seat ? 0 : 1);
                float z = team == 0 ? -1 : 1;
                Place(root, camera, keep, frame, 2, 2, 512, 512, 484, new Vector3(0,.025f,z*11), 3.8f, "Keep " + team);
                foreach (float x in new[]{-5f,5f})
                    Place(root, camera, tower, frame, 2, 2, 512, 512, 484, new Vector3(x,.025f,z*7.7f), 2.6f, "Arrow tower " + team);
            }
            Place(root, camera, Load("wall/wall-gates-baked.png"), seat, 1, 2, 2048, 256, 236,
                  new Vector3(0,.03f,0), 20, "Neutral wall and two open gates");
            var props = Load("props/border-props-baked.png");
            for (int side = -1; side <= 1; side += 2)
            {
                Place(root,camera,props,0,2,3,384,384,354,new Vector3(side*9,.02f,-12.6f),2.8f,"Border tree");
                Place(root,camera,props,1,2,3,384,384,354,new Vector3(side*9.1f,.02f,12.6f),1.8f,"Border sapling");
                Place(root,camera,props,0,2,3,384,384,354,new Vector3(side*9,.02f,4.8f),2.5f,"Border tree");
                Place(root,camera,props,2,2,3,384,384,354,new Vector3(side*9,.02f,-5),1.1f,"Border bush");
                Place(root,camera,props,3,2,3,384,384,354,new Vector3(side*8.9f,.02f,-1.6f),.9f,"Boulder");
                Place(root,camera,props,4,2,3,384,384,354,new Vector3(side*8.1f,.02f,8.5f),1.05f,"Rock cluster");
            }
            Troop(root,camera,"guard",new Vector3(-2.2f,.035f,-2.1f),0);
            Troop(root,camera,"siege",new Vector3(.1f,.035f,-2.1f),0);
            Troop(root,camera,"bomber",new Vector3(2.2f,.035f,-2.1f),0);
            Troop(root,camera,"archer",new Vector3(-5,.035f,3.8f),1);
            Troop(root,camera,"charger",new Vector3(5,.035f,3.8f),1);
        }

        private static void Troop(Transform root, Camera camera, string kind, Vector3 position, int team)
        {
            var model = UnityEngine.Object.Instantiate(DuelSpriteLibrary.Prefab(kind), root, false);
            Vector3 heading = team == 0 ? Vector3.forward : Vector3.back;
            model.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
            var art = model.GetComponent<DuelUnitArt>(); DuelContactShadow.Attach(art);
            art.Sprites.SetTeam(team);
            var state = new EntityState {kind=kind,seat=team,dx=heading.x,dz=heading.z};
            art.Sprites.Present(state, DuelRules.Load().Find(kind), 90, 0, false, 1, 0);
            art.Sprites.AlignToCamera();
        }

        private static void Save(Camera camera, string path, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                camera.aspect = width/(float)height;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                RenderTexture.active=target; pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                if(pixels) UnityEngine.Object.DestroyImmediate(pixels);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
