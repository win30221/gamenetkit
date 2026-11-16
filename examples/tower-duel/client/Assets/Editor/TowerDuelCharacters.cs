using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TowerDuel.Editor
{
    public static class TowerDuelCharacters
    {
        private const string Source="Assets/TowerDuelArt";
        private const string Runtime="Assets/Resources/TowerDuel/Units";
        private static readonly Dictionary<string,Material> Materials=new Dictionary<string,Material>();
        [MenuItem("Tower Duel/Prepare animated characters")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Runtime+"/Clips");Directory.CreateDirectory(Runtime+"/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string name in new[]{"Knight","Ranger","Rogue","Rogue_Hooded","Mage","Barbarian"})ImportModel(Source+"/Characters/"+name+".fbx",true,null);
            var knight=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Characters/Knight.fbx");
            var avatar=knight.GetComponent<Animator>()?.avatar;
            if(!avatar||!avatar.isValid)throw new InvalidOperationException("Knight Generic avatar is invalid");
            foreach(string name in new[]{"General","MovementBasic","CombatMelee","CombatRanged"})ImportModel(Source+"/Animations/Rig_Medium_"+name+".fbx",true,avatar);
            foreach(string name in new[]{"sword_1handed","shield_square","bow_withString","arrow_bow","smokebomb","sword_2handed","axe_2handed"})ImportModel(Source+"/Equipment/"+name+".fbx",false,null);
            foreach(string name in new[]{"knight","ranger","rogue","mage","barbarian"})PrepareMaterial(name);
            var idle=Clip("General","Idle_A",true);
            var walk=Clip("MovementBasic","Walking_A",true);
            var quick=Clip("MovementBasic","Running_A",true);
            var death=Clip("General","Death_A",false);
            var shieldAttack=Clip("CombatMelee","Melee_Block_Attack",false);
            var slash=Clip("CombatMelee","Melee_1H_Attack_Slice_Horizontal",false);
            var aim=Clip("CombatRanged","Ranged_Bow_Aiming_Idle",true);
            var shoot=Clip("CombatRanged","Ranged_Bow_Release",false);
            var cast=Clip("CombatRanged","Ranged_Magic_Shoot",false);
            var chop=Clip("CombatMelee","Melee_2H_Attack_Chop",false);
            var thrust=Clip("CombatMelee","Melee_2H_Attack_Stab",false);
            var rules=DuelRules.Load();
            var audits=new List<UnitAudit>();
            audits.Add(Prefab("guard","Knight","knight",1.80f,Controller("guard",idle,walk,shieldAttack,death,rules.Find("guard"))));
            audits.Add(Prefab("archer","Ranger","ranger",1.70f,Controller("archer",aim,walk,shoot,death,rules.Find("archer"))));
            audits.Add(Prefab("swords","Rogue","rogue",1.55f,Controller("swords",idle,quick,slash,death,rules.Find("swords"))));
            audits.Add(Prefab("bomber","Mage","mage",1.65f,Controller("bomber",idle,walk,cast,death,rules.Find("bomber"))));
            audits.Add(Prefab("charger","Rogue_Hooded","rogue",1.75f,Controller("charger",idle,quick,thrust,death,rules.Find("charger"))));
            audits.Add(Prefab("siege","Barbarian","barbarian",2.0f,Controller("siege",idle,walk,chop,death,rules.Find("siege"))));
            var arrow=new GameObject("Arrow");
            Attach(arrow.transform,"arrow_bow","ranger");
            PrefabUtility.SaveAsPrefabAsset(arrow,Runtime+"/arrow.prefab");UnityEngine.Object.DestroyImmediate(arrow);
            var bomb=new GameObject("Bomb");Attach(bomb.transform,"smokebomb","mage");
            Bounds bombBounds=bomb.GetComponentsInChildren<Renderer>()[0].bounds;foreach(var renderer in bomb.GetComponentsInChildren<Renderer>())bombBounds.Encapsulate(renderer.bounds);
            bomb.transform.localScale=Vector3.one*(.45f/Mathf.Max(.001f,bombBounds.size.magnitude));
            PrefabUtility.SaveAsPrefabAsset(bomb,Runtime+"/bomb.prefab");UnityEngine.Object.DestroyImmediate(bomb);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds");
            File.WriteAllText("Builds/character-audit.json",JsonUtility.ToJson(new CharacterAudit{units=audits.ToArray(),clips=new[]{idle.name,walk.name,quick.name,death.name,shieldAttack.name,slash.name,aim.name,shoot.name,cast.name,chop.name,thrust.name}},true));
            Debug.Log("Prepared six equipped KayKit units and eleven animation clips");
            TowerDuelSpriteContent.Prepare();
        }
        private static void ImportModel(string path,bool animated,Avatar sourceAvatar)
        {
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(!importer)throw new InvalidOperationException("Missing character source "+path);
            bool changed=importer.importAnimation!=animated||importer.bakeAxisConversion!=true||importer.importCameras||importer.importLights;
            importer.importAnimation=animated;importer.bakeAxisConversion=true;importer.importCameras=false;importer.importLights=false;
            importer.importBlendShapes=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            if(animated)
            {
                var setup=sourceAvatar?ModelImporterAvatarSetup.CopyFromOther:ModelImporterAvatarSetup.CreateFromThisModel;
                changed|=importer.animationType!=ModelImporterAnimationType.Generic||importer.avatarSetup!=setup||importer.sourceAvatar!=sourceAvatar||importer.motionNodeName!="root";
                importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=setup;importer.sourceAvatar=sourceAvatar;importer.motionNodeName="root";
            }
            if(changed)importer.SaveAndReimport();
        }
        private static void PrepareMaterial(string name)
        {
            string texturePath=Source+"/Textures/"+name+"_texture.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(importer.maxTextureSize!=512||!importer.mipmapEnabled||!importer.sRGBTexture)
            {importer.maxTextureSize=512;importer.mipmapEnabled=true;importer.sRGBTexture=true;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();}
            string path=Runtime+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.name=name;material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.12f);
            EditorUtility.SetDirty(material);Materials[name]=material;
        }
        private static AnimationClip Clip(string group,string name,bool loop)
        {
            string source=Source+"/Animations/Rig_Medium_"+group+".fbx";
            var clips=AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            var original=clips.FirstOrDefault(c=>c.name==name||c.name.EndsWith("|"+name));
            if(!original)throw new InvalidOperationException("Missing clip "+name+"; found "+string.Join(",",clips.Select(c=>c.name)));
            string path=Runtime+"/Clips/"+name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(!clip){clip=UnityEngine.Object.Instantiate(original);clip.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(clip,path);}
            else EditorUtility.CopySerialized(original,clip);
            clip.name=name;clip.hideFlags=HideFlags.None;clip.legacy=false;
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
            AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            if(clip.length<=0||AnimationUtility.GetCurveBindings(clip).Length==0)throw new InvalidOperationException("Empty animation "+name);
            EditorUtility.SetDirty(clip);return clip;
        }
        private static AnimatorController Controller(string name,AnimationClip idle,AnimationClip walk,AnimationClip attack,AnimationClip death,CardDefinition card)
        {
            string path=Runtime+"/"+name+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.parameters=new[]{new AnimatorControllerParameter{name="Speed",type=AnimatorControllerParameterType.Float},new AnimatorControllerParameter{name="Strike",type=AnimatorControllerParameterType.Trigger},new AnimatorControllerParameter{name="Dead",type=AnimatorControllerParameterType.Bool}};
            var machine=controller.layers[0].stateMachine;
            AnimatorState State(string stateName){return machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==stateName)??machine.AddState(stateName);}
            var move=State("Move");var strike=State("Attack");var dead=State("Death");
            var blend=move.motion as BlendTree;
            if(!blend){blend=new BlendTree{name="Locomotion"};AssetDatabase.AddObjectToAsset(blend,controller);}
            blend.blendType=BlendTreeType.Simple1D;blend.blendParameter="Speed";blend.useAutomaticThresholds=false;
            blend.children=new[]{new ChildMotion{motion=idle,threshold=0,timeScale=1},new ChildMotion{motion=walk,threshold=1,timeScale=1}};
            move.motion=blend;strike.motion=attack;dead.motion=death;strike.speed=attack.length/(card.cooldown/30f);dead.speed=death.length/1.0f;
            foreach(var state in new[]{move,strike,dead})foreach(var transition in state.transitions)state.RemoveTransition(transition);
            foreach(var transition in machine.anyStateTransitions)machine.RemoveAnyStateTransition(transition);
            var toDeath=machine.AddAnyStateTransition(dead);toDeath.AddCondition(AnimatorConditionMode.If,0,"Dead");toDeath.hasExitTime=false;toDeath.duration=.06f;toDeath.canTransitionToSelf=false;
            var toAttack=machine.AddAnyStateTransition(strike);toAttack.AddCondition(AnimatorConditionMode.If,0,"Strike");toAttack.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");toAttack.hasExitTime=false;toAttack.duration=.04f;toAttack.canTransitionToSelf=true;
            var exit=strike.AddTransition(move);exit.hasExitTime=true;exit.exitTime=.98f;exit.duration=.05f;
            machine.defaultState=move;EditorUtility.SetDirty(controller);EditorUtility.SetDirty(blend);return controller;
        }
        private static UnitAudit Prefab(string kind,string source,string material,float height,AnimatorController controller)
        {
            var root=new GameObject(kind);
            try
            {
                var facing=new GameObject("Facing").transform;facing.SetParent(root.transform,false);facing.localRotation=Quaternion.Euler(0,180,0);
                var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Characters/"+source+".fbx"),facing,false);model.name="Model";
                var renderers=model.GetComponentsInChildren<Renderer>();
                Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers){bounds.Encapsulate(renderer.bounds);renderer.sharedMaterial=Materials[material];}
                if(bounds.size.y<=.01f)throw new InvalidOperationException("Invalid character bounds");
                float scale=height/bounds.size.y;model.transform.localScale*=scale;model.transform.localPosition+=Vector3.up*(-bounds.min.y*scale);
                var animator=model.GetComponent<Animator>();if(!animator)animator=model.AddComponent<Animator>();
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.updateMode=AnimatorUpdateMode.UnscaledTime;
                Transform Bone(string name){var bone=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);if(!bone)throw new InvalidOperationException("Missing bone "+name);return bone;}
                if(kind=="guard"){Attach(Bone("handslot.r"),"sword_1handed","knight");Attach(Bone("handslot.l"),"shield_square","knight");}
                else if(kind=="archer")Attach(Bone("handslot.l"),"bow_withString","ranger");
                else if(kind=="bomber")Attach(Bone("handslot.r"),"smokebomb","mage");
                else if(kind=="charger")Attach(Bone("handslot.r"),"sword_2handed","knight");
                else if(kind=="siege")Attach(Bone("handslot.r"),"axe_2handed","barbarian");
                else Attach(Bone("handslot.r"),"sword_1handed","knight");
                var art=root.AddComponent<DuelUnitArt>();art.Animator=animator;art.WalkProbe=Bone("upperleg.l");art.AttackProbe=Bone("upperarm.r");art.DeathProbe=Bone("hips");art.Height=height;
                var marker=new GameObject("Team marker");marker.transform.SetParent(root.transform,false);marker.transform.localPosition=Vector3.up*.02f;
                var filter=marker.AddComponent<MeshFilter>();filter.sharedMesh=MarkerMesh();var markerRenderer=marker.AddComponent<MeshRenderer>();markerRenderer.sharedMaterial=Resources.Load<Material>("TowerDuel/Materials/Blue");markerRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;art.TeamMarker=markerRenderer;
                int tris=root.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r=>r.sharedMesh.triangles.Length/3)+root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
                if(tris>10000)throw new InvalidOperationException(kind+" exceeds character geometry budget: "+tris);
                PrefabUtility.SaveAsPrefabAsset(root,Runtime+"/"+kind+".prefab");
                return new UnitAudit{kind=kind,height=height,triangles=tris,renderers=root.GetComponentsInChildren<Renderer>().Length,avatar=animator.avatar.name};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Attach(Transform bone,string name,string material)
        {
            var equipment=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Equipment/"+name+".fbx"),bone,false);
            // Preserve the FBX's 100x scale and axis conversion on its root node.
            equipment.name=name;equipment.transform.localPosition=Vector3.zero;
            foreach(var renderer in equipment.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=Materials[material];
            foreach(var collider in equipment.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(collider);
        }
        private static Mesh MarkerMesh()
        {
            string path=Runtime+"/TeamMarker.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
            var vertices=new Vector3[17];var triangles=new int[48];
            for(int i=0;i<16;i++){float a=i*Mathf.PI*2/16;vertices[i+1]=new Vector3(Mathf.Cos(a)*.38f,0,Mathf.Sin(a)*.38f);triangles[i*3]=0;triangles[i*3+1]=(i+1)%16+1;triangles[i*3+2]=i+1;}
            mesh=new Mesh{name="Team marker",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        [Serializable] private sealed class UnitAudit {public string kind,avatar;public float height;public int triangles,renderers;}
        [Serializable] private sealed class CharacterAudit {public UnitAudit[] units;public string[] clips;}
    }
}
