using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TowerDuel.Editor
{
    public static class TowerDuelSpriteReview
    {
        [Serializable] private sealed class LightingProbe { public string view,kind;public bool baked;public int sampleX,sampleY,footX,footY,teamMaskTopY,leftX,rightX,shadeY;public Vector3 contactPosition; }
        private const string ResumeKey="TowerDuel.Resume.v3",ReviewKey="TowerDuel.B3Review.";
        public static void PrepareUI()
        {
            if(Application.isPlaying||SceneManager.GetActiveScene().isDirty||SessionState.GetBool(ReviewKey+"active",false))
                throw new InvalidOperationException("UI review requires an idle clean Editor and no existing review");
            SessionState.SetString(ReviewKey+"scene",SceneManager.GetActiveScene().path);
            SessionState.SetBool(ReviewKey+"hadResume",PlayerPrefs.HasKey(ResumeKey));
            SessionState.SetString(ReviewKey+"resume",PlayerPrefs.GetString(ResumeKey,""));
            SessionState.SetBool(ReviewKey+"active",true);PlayerPrefs.DeleteKey(ResumeKey);
            EditorSceneManager.OpenScene("Assets/Scenes/TowerDuel.unity");
        }
        public static void ShowUI(string kind="guard")
        {
            var client=UnityEngine.Object.FindAnyObjectByType<TowerDuelClient>();
            if(!Application.isPlaying||!client||client.Session==null)throw new InvalidOperationException("Wait for the review client to initialize");
            client.enabled=false;
            var ui=client.GetComponent<UIDocument>().rootVisualElement;
            foreach(string name in new[]{"lobbyLayer","waitingLayer","resultLayer"})ui.Q(name).style.display=DisplayStyle.None;
            ui.Q("dockFrame").style.display=DisplayStyle.Flex;
            ui.Q("dock").style.display=DisplayStyle.Flex;ui.Q("topHud").style.display=DisplayStyle.Flex;
            var shown=new System.Collections.Generic.HashSet<string>();
            for(int i=0;i<4;i++)shown.Add(client.Rules.cards[i].id);
            if(!shown.Contains(kind)){shown.Remove(client.Rules.cards[3].id);shown.Add(kind);}
            for(int i=0;i<client.Rules.cards.Length;i++)
            {
                var card=ui.Q<Button>(client.Rules.cards[i].id+"Card");
                card.style.display=shown.Contains(client.Rules.cards[i].id)?DisplayStyle.Flex:DisplayStyle.None;card.SetEnabled(true);
                card.EnableInClassList("selected",client.Rules.cards[i].id==kind);
            }
            var next=ui.Q("nextCardArt");next.style.backgroundImage=DuelSpriteLibrary.Card(kind);
            next.AddToClassList("b3-next");next.Q(className:"b3-card-frame").style.display=DisplayStyle.Flex;
            ui.Q<Label>("nextCardLabel").text="下一張\n"+client.Rules.Find(kind).name;
        }
        public static void FinishUI()
        {
            if(!SessionState.GetBool(ReviewKey+"active",false))return;
            if(SessionState.GetBool(ReviewKey+"hadResume",false))PlayerPrefs.SetString(ResumeKey,SessionState.GetString(ReviewKey+"resume",""));
            else PlayerPrefs.DeleteKey(ResumeKey);
            string scene=SessionState.GetString(ReviewKey+"scene","");
            SessionState.EraseString(ReviewKey+"resume");SessionState.SetBool(ReviewKey+"active",false);
            if(string.IsNullOrEmpty(scene))EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            else EditorSceneManager.OpenScene(scene);
        }
        [MenuItem("Tower Duel/Capture B3 sprite review")]
        public static string Capture() => Capture("guard");
        public static string Capture(string kind)
        {
            if (Application.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("B3 review requires an idle Editor with a clean scene");
            string previous = SceneManager.GetActiveScene().path;
            if(!DuelSpriteLibrary.Converted(kind))throw new InvalidOperationException("Complete and prepare the B3 unit first: "+kind);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../artifacts/tower-duel/sprites-b3/"+(kind=="guard"?"native":"native-"+kind)));
            string companion=kind=="guard"?"archer":"guard";
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/Scenes/TowerDuel.unity");
            var root = new GameObject("Disposable B3 review");
            try
            {
                var camera = Camera.main;
                camera.orthographic = true; camera.orthographicSize = 5.2f;
                camera.transform.rotation = Quaternion.Euler(60, 0, 0);
                UnityEngine.Object.FindAnyObjectByType<DuelBattlefieldPreview>().Environment.SetView(camera, 0);
                DuelLighting.Apply(camera);
                camera.transform.position = new Vector3(-4, 0, -1)-camera.transform.forward*40;
                var headings = new[] { Vector3.back, new Vector3(-1,0,-1), Vector3.left, new Vector3(-1,0,1),
                    Vector3.forward, new Vector3(1,0,1), Vector3.right, new Vector3(1,0,-1) };
                for (int i = 0; i < 8; i++)
                    Spawn(root.transform, camera, kind, Ground(-7+(i%4)*2, -3+(i/4)*3), headings[i], i%2, 90);
                Spawn(root.transform, camera, companion, Ground(-4.5f,-6), Vector3.forward, 0, 90);
                Save(camera, output+"/"+kind+"-eight-directions.png", 1200, 1000);
                for (int i=root.transform.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                long impact=100+DuelRules.Load().Find(kind).windupTicks;
                Spawn(root.transform, camera, kind, Ground(-5,-2.2f), Vector3.forward, 0, impact);
                Spawn(root.transform, camera, kind, Ground(-5,-.3f), Vector3.back, 1, impact);
                Spawn(root.transform, camera, companion, new Vector3(-4,.06f,-4), Vector3.forward, 0, 90);
                Spawn(root.transform, camera, "siege", Ground(-6,1.4f), Vector3.back, 1, 90);
                camera.orthographicSize = 4.5f;
                Save(camera, output+"/"+kind+"-mixed-bridge.png", 1200, 1000);
                Spawn(root.transform, camera, kind, Ground(-5,-9.55f), Vector3.forward, 0, 90);
                Spawn(root.transform, camera, kind, Ground(-5,-5.85f), Vector3.back, 1, 90);
                camera.transform.position = new Vector3(-5,0,-7.7f)-camera.transform.forward*40;
                camera.orthographicSize = 3.4f;
                Save(camera, output+"/"+kind+"-tower-occlusion.png", 1000, 1000);
                for (int i=root.transform.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                CaptureLighting(root.transform,camera,output,kind);
                for (int i=root.transform.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                CaptureMotion(root.transform,camera,output,kind);
                return output;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (string.IsNullOrEmpty(previous)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.OpenScene(previous);
            }
        }
        private static Vector3 Ground(float x,float z) => new EntityState {x=x,z=z}.Position;
        public static string CaptureLineup()
        {
            if(Application.isPlaying||SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Lineup requires a clean idle Editor");
            string previous=SceneManager.GetActiveScene().path;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../artifacts/tower-duel/sprites-b3/scale-lighting-review"));
            Directory.CreateDirectory(output);EditorSceneManager.OpenScene("Assets/Scenes/TowerDuel.unity");
            var root=new GameObject("Disposable B3 scale lineup");
            try
            {
                var camera=Camera.main;camera.orthographic=true;camera.orthographicSize=4.4f;
                camera.transform.rotation=Quaternion.Euler(60,0,0);
                UnityEngine.Object.FindAnyObjectByType<DuelBattlefieldPreview>().Environment.SetView(camera, 0);
                camera.transform.position=new Vector3(0,1.6f,-3.5f)-camera.transform.forward*40;
                DuelLighting.Apply(camera);
                string[] kinds={"guard","archer","swords","bomber","charger","siege"};
                for(int i=0;i<kinds.Length;i++)Spawn(root.transform,camera,kinds[i],Ground((i-2.5f)*2.6f,-3.5f),Vector3.back,0,90);
                Save(camera,output+"/six-character-lineup.png",1400,700);
                return output;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if(string.IsNullOrEmpty(previous))EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                else EditorSceneManager.OpenScene(previous);
            }
        }
        private static void CaptureLighting(Transform parent,Camera camera,string output,string kind)
        {
            float visualHeight=DuelSpriteLibrary.Prefab(kind).GetComponent<DuelUnitArt>().Sprites.Set.WorldHeight;
            camera.orthographicSize=Mathf.Max(1.15f,visualHeight*1.15f);
            var model=Spawn(parent,camera,kind,Ground(-5,-3.5f),Vector3.back,0,90);
            var unit=model.GetComponent<DuelSpriteUnit>();
            var definition=DuelRules.Load().Find(kind);
            var sun=DuelLighting.Apply(camera);
            float intensity=sun.intensity;Color color=sun.color;var shadows=sun.shadows;
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="Disposable light blocker";blocker.transform.SetParent(parent,false);
            blocker.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;blocker.transform.localScale=Vector3.one*1.8f;
            var samples=new System.Collections.Generic.List<string>();
            try
            {
                foreach(float yaw in new[]{0f,180f})
                {
                    camera.transform.rotation=Quaternion.Euler(60,yaw,0);
                    camera.transform.position=model.transform.position+Vector3.up*(visualHeight*.8f)-camera.transform.forward*20;
                    var entity=new EntityState {kind=kind,dx=-camera.transform.forward.x,dz=-camera.transform.forward.z};
                    unit.Present(entity,definition,90,0,false,1,0);
                    DuelLighting.Apply(camera);unit.AlignToCamera();
                    blocker.SetActive(false);sun.intensity=intensity;sun.color=Color.white;sun.shadows=LightShadows.Hard;
                    string suffix=yaw==0?"blue-view":"red-view";
                    Save(camera,output+"/lighting-"+suffix+"-day.png",600,600);
                    var lightRotation=sun.transform.rotation;
                    var mirroredRay=camera.transform.InverseTransformDirection(sun.transform.forward);mirroredRay.x=-mirroredRay.x;
                    sun.transform.rotation=Quaternion.LookRotation(camera.transform.TransformDirection(mirroredRay),Vector3.up);
                    Save(camera,output+"/lighting-"+suffix+"-reverse.png",600,600);
                    sun.transform.rotation=lightRotation;
                    unit.SetTeam(1);Save(camera,output+"/lighting-"+suffix+"-enemy.png",600,600);unit.SetTeam(0);
                    unit.Present(entity,definition,90,0,false,1,1);
                    Save(camera,output+"/lighting-"+suffix+"-hit.png",600,600);
                    unit.Present(entity,definition,90,0,false,1,0);
                    Save(camera,output+"/lighting-"+suffix+"-recovered.png",600,600);
                    unit.ShadowRenderer.enabled=false;
                    Save(camera,output+"/lighting-"+suffix+"-no-shadow.png",600,600);
                    unit.ShadowRenderer.enabled=true;
                    var contact=model.GetComponentInChildren<DuelContactShadow>();
                    bool hadContact=contact.Renderer.enabled;
                    contact.Renderer.enabled=false;
                    Save(camera,output+"/lighting-"+suffix+"-no-contact.png",600,600);
                    contact.Renderer.enabled=hadContact;
                    sun.intensity=0;
                    Save(camera,output+"/lighting-"+suffix+"-ambient.png",600,600);
                    sun.intensity=intensity;sun.color=new Color(1,.4f,.15f);
                    Save(camera,output+"/lighting-"+suffix+"-warm.png",600,600);
                    sun.color=Color.white;
                    blocker.transform.position=model.transform.position+Vector3.up*.65f-sun.transform.forward*2.8f;
                    blocker.SetActive(true);
                    Save(camera,output+"/lighting-"+suffix+"-shadow.png",600,600);
                    var point=camera.WorldToViewportPoint(model.transform.position+Vector3.up*.65f);
                    var foot=camera.WorldToViewportPoint(model.transform.position);
                    var teamTop=camera.WorldToViewportPoint(unit.Renderer.transform.TransformPoint(new Vector3(0,unit.Set.TeamMaskTop,0)));
                    float h=unit.Set.ReferenceHeight/(float)unit.Set.TileSize;
                    var left=camera.WorldToViewportPoint(unit.Renderer.transform.TransformPoint(new Vector3(-h*.18f,h*.65f,0)));
                    var right=camera.WorldToViewportPoint(unit.Renderer.transform.TransformPoint(new Vector3(h*.18f,h*.65f,0)));
                    samples.Add(JsonUtility.ToJson(new LightingProbe {view=suffix,kind=kind,baked=unit.Set.Baked,sampleX=Mathf.RoundToInt(point.x*600),sampleY=Mathf.RoundToInt((1-point.y)*600),footX=Mathf.RoundToInt(foot.x*600),footY=Mathf.RoundToInt((1-foot.y)*600),teamMaskTopY=Mathf.RoundToInt((1-teamTop.y)*600),leftX=Mathf.RoundToInt(left.x*600),rightX=Mathf.RoundToInt(right.x*600),shadeY=Mathf.RoundToInt((1-left.y)*600),contactPosition=contact.transform.position}));
                }
                File.WriteAllText(output+"/lighting-probes.json","["+string.Join(",",samples)+"]");
            }
            finally { sun.intensity=intensity;sun.color=color;sun.shadows=shadows; }
        }
        private static void CaptureMotion(Transform parent,Camera camera,string output,string kind)
        {
            camera.transform.rotation=Quaternion.Euler(60,0,0);camera.orthographicSize=1.65f;
            camera.transform.position=new Vector3(-5,.75f,-3.5f)-camera.transform.forward*20;
            DuelLighting.Apply(camera);
            var definition=DuelRules.Load().Find(kind);
            var models=new[] {Spawn(parent,camera,kind,Ground(-5.7f,-3.5f),Vector3.forward,0,90),
                Spawn(parent,camera,kind,Ground(-4.3f,-3.5f),Vector3.back,1,90)};
            string folder=output+"/motion";Directory.CreateDirectory(folder);
            var set=models[0].GetComponent<DuelSpriteUnit>().Set;
            double walkFps=set.LoopFps(DuelSpriteClip.Walk);
            foreach(string clip in new[]{"walk","attack"})
            {
                for(int i=0;i<models.Length;i++)
                    models[i].GetComponent<DuelSpriteUnit>().Present(new EntityState{kind=kind,seat=i,dz=i==0?1:-1},definition,1000,0,false,1,0);
                int count=clip=="walk"?set.WalkFrames:set.AttackFrames;
                for(int frame=0;frame<count;frame++)
                {
                    double tick=clip=="walk"?1000+frame*30d/walkFps+.001:
                        frame<set.ImpactFrame?1100+frame*Math.Max(1,definition.windupTicks)/(double)set.ImpactFrame+.001:
                        1100+definition.windupTicks+(frame-set.ImpactFrame)*Math.Max(1,definition.cooldown-definition.windupTicks)/(double)(set.AttackFrames-set.ImpactFrame)+.001;
                    for(int i=0;i<models.Length;i++)
                    {
                        var entity=new EntityState {kind=kind,seat=i,dz=i==0?1:-1,target=clip=="attack"?99:0,
                            attackTick=clip=="attack"?1100:0,hitTick=clip=="attack"&&frame>=set.ImpactFrame?1100+definition.windupTicks:0};
                        var unit=models[i].GetComponent<DuelSpriteUnit>();
                        unit.Present(entity,definition,tick,clip=="walk"?1:0,false,1,0);unit.AlignToCamera();
                        if(unit.FrameIndex!=frame)throw new InvalidOperationException("Review motion missed "+clip+" frame "+frame);
                    }
                    Save(camera,folder+"/"+clip+"-"+frame+".png",720,600);
                }
            }
            File.WriteAllText(folder+"/timing.json","{\"windupTicks\":"+definition.windupTicks+",\"cooldownTicks\":"+definition.cooldown+
                ",\"walkFrames\":"+set.WalkFrames+",\"attackFrames\":"+set.AttackFrames+
                ",\"impactFrame\":"+set.ImpactFrame+",\"walkFps\":"+walkFps.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
        }
        private static GameObject Spawn(Transform parent, Camera camera, string kind, Vector3 position, Vector3 heading, int seat, long tick)
        {
            var prefab = DuelSpriteLibrary.Prefab(kind);
            if (!prefab) throw new InvalidOperationException("Missing B3 unit: "+kind);
            var model = UnityEngine.Object.Instantiate(prefab, parent, false);
            model.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
            var art = model.GetComponent<DuelUnitArt>();
            DuelContactShadow.Attach(art);
            if (art.Sprites)
            {
                art.Sprites.SetTeam(seat);
                var definition=DuelRules.Load().Find(kind);
                var state = new EntityState { kind=kind,seat=seat,dx=heading.x,dz=heading.z,target=tick>=100?99:0,
                    attackTick=tick>=100?100:0,hitTick=tick>=100+definition.windupTicks?100+definition.windupTicks:0 };
                art.Sprites.Present(state, definition, tick, 0, false, 1, 0);
                art.Sprites.AlignToCamera();
            }
            else { art.Animator.Rebind(); art.Animator.Play("Move",0,.3f); art.Animator.Update(.001f); }
            return model;
        }
        private static void Save(Camera camera, string path, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                camera.aspect = width/(float)height;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active = target;
                pixels = new Texture2D(width,height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; if(pixels) UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
        }
    }
}
