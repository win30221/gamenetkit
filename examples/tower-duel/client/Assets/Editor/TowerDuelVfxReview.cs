using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TowerDuel.Editor
{
    // Disposable native-camera captures of the actual arena and imported particle assets.
    public static class TowerDuelVfxReview
    {
        [MenuItem("Tower Duel/Capture combat effects review")]
        public static string Capture()
        {
            if(Application.isPlaying||SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Capture requires an idle Editor with a clean scene.");
            string previous=SceneManager.GetActiveScene().path;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../artifacts/tower-duel/vfx/native"));
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/Scenes/TowerDuel.unity");
            var root=new GameObject("Disposable combat review");
            DuelFeedbackPool feedback=null;DuelParticleEffect fire=null,frost=null;
            try
            {
                var camera=Camera.main;
                camera.orthographic=true;camera.orthographicSize=8.4f;
                camera.transform.rotation=Quaternion.Euler(60,0,0);
                camera.transform.position=new Vector3(0,0,-.8f)-camera.transform.forward*40;
                var battlefield = UnityEngine.Object.FindAnyObjectByType<DuelBattlefieldPreview>().Environment;
                battlefield.SetView(camera, 0);
                Unit(root.transform,"guard",new Vector3(-5,.16f,-1.8f),false);
                Unit(root.transform,"swords",new Vector3(-5,.16f,.1f),true);
                Unit(root.transform,"archer",new Vector3(-4,.06f,-5.1f),false);
                Unit(root.transform,"bomber",new Vector3(5,.06f,-4.5f),false);
                Unit(root.transform,"charger",new Vector3(5,.16f,-.2f),true);
                Unit(root.transform,"guard",new Vector3(4.3f,.16f,1),true);
                feedback=new DuelFeedbackPool(root.transform);
                fire=new DuelParticleEffect(DuelFeedbackPool.Load("Fireball"),root.transform,67);
                frost=new DuelParticleEffect(DuelFeedbackPool.Load("Frost"),root.transform,89);
                frost.Place(new Vector3(5,.24f,-.4f),Quaternion.identity,2.8f);frost.Present(1.3f);
                fire.Place(new Vector3(-4.3f,2.8f,-.8f),Quaternion.identity,1);fire.Present(.45f);
                Save(camera,output+"/fireball-and-frost.png");
                fire.Clear();
                feedback.Play("Explosion",new Vector3(-5,.2f,0),Vector3.zero,2.2f,100,106);
                feedback.Play("Slash",new Vector3(4.8f,.8f,-.2f),Vector3.back,1.7f,100,102);
                feedback.Play("HeavyHit",new Vector3(5,.9f,-1.2f),Vector3.zero,1,100,103);
                Save(camera,output+"/combat-impact.png");
                feedback.Reset();
                camera.orthographicSize=5.6f;
                camera.transform.position=new Vector3(-5,0,8)-camera.transform.forward*40;
                foreach (var tower in DuelMap.Load().towers)
                    if (tower.seat == 1 && tower.kind == "tower" && tower.x < 0)
                        battlefield.Building(tower.id).SetActive(false);
                feedback.Play("Collapse",new Vector3(-5,.8f,8.5f),Vector3.zero,1.1f,100,108);
                Save(camera,output+"/tower-collapse.png");
                return output;
            }
            finally
            {
                feedback?.Dispose();fire?.Dispose();frost?.Dispose();UnityEngine.Object.DestroyImmediate(root);
                if(string.IsNullOrEmpty(previous))EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                else EditorSceneManager.OpenScene(previous);
            }
        }
        private static void Unit(Transform parent,string kind,Vector3 position,bool enemy)
        {
            var model=UnityEngine.Object.Instantiate(DuelSpriteLibrary.Prefab(kind),parent,false);
            model.transform.SetPositionAndRotation(position,Quaternion.Euler(0,enemy?180:0,0));
            var art=model.GetComponent<DuelUnitArt>();
            var card = DuelRules.Load().Find(kind);
            var entity = new EntityState { kind=kind, seat=enemy?1:0, dz=enemy?-1:1, target=99, attackTick=100 };
            art.Sprites.SetTeam(entity.seat);
            art.Sprites.Present(entity, card, 100 + card.windupTicks * .5, 0, false, 1, 0);
            art.Sprites.AlignToCamera();
        }
        private static void Save(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(1200,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var prior=RenderTexture.active;
            Texture2D pixels=null;
            try
            {
                camera.aspect=1.2f;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=prior;if(pixels)UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.ReleaseTemporary(target);}
        }
    }
}
