using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDuel.Editor
{
    public static class TowerDuelSpriteTests
    {
        private static void Require(bool value, string reason)
        {
            if (!value) throw new InvalidOperationException(reason);
        }
        private static void CheckVariableClips()
        {
            var set=ScriptableObject.CreateInstance<DuelSpriteSet>();
            Texture2D idle=null,walk=null,attack=null;
            try
            {
                set.Kind="variable-clips-test";set.TileSize=8;set.Baseline=6;set.ReferenceHeight=4;set.Baked=true;
                set.IdleFrames=3;set.WalkFrames=5;set.AttackFrames=8;set.ImpactFrame=4;
                set.IdleFps=5;set.WalkFps=10;
                set.Idle=idle=new Texture2D(24,128,TextureFormat.RGBA32,false);
                set.Walk=walk=new Texture2D(40,128,TextureFormat.RGBA32,false);
                set.Attack=attack=new Texture2D(64,128,TextureFormat.RGBA32,false);
                foreach(var clip in new[]{DuelSpriteClip.Idle,DuelSpriteClip.Walk,DuelSpriteClip.Attack})
                {
                    int count=set.FrameCount(clip);
                    for(int direction=0;direction<8;direction++)for(int frame=0;frame<count;frame++)
                    {
                        var body=set.Frame(clip,direction,frame);var shadow=set.ShadowFrame(clip,direction,frame);
                        Require(body.rect.x==frame*8&&shadow.rect.x==body.rect.x,"Variable clip lost a column");
                        Require(body.rect.y-shadow.rect.y==64,"Variable clip lost its shadow pairing");
                    }
                    if(clip==DuelSpriteClip.Attack)continue;
                    float fps=set.LoopFps(clip);
                    Require(set.LoopFrame(clip,0)==0&&set.LoopFrame(clip,1.01/fps)==1,"Configured loop FPS ignored");
                    Require(set.LoopFrame(clip,(count+.01)/fps)==0,"Configured loop count did not wrap");
                }
                Require(set.FrameCount(DuelSpriteClip.Idle)==3&&set.FrameCount(DuelSpriteClip.Attack)==8,
                    "Independent clip frame counts were lost");
            }
            finally
            {
                set.ClearFrames();UnityEngine.Object.DestroyImmediate(set);
                if(idle)UnityEngine.Object.DestroyImmediate(idle);
                if(walk)UnityEngine.Object.DestroyImmediate(walk);
                if(attack)UnityEngine.Object.DestroyImmediate(attack);
            }
        }

        public static string Run()
        {
            Require(Application.isPlaying, "Run B3 sprite tests in an empty Play Mode scene");
            var root = new GameObject("B3 sprite regression");
            try
            {
                var camera = new GameObject("B3 regression camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform); camera.tag = "MainCamera";
                camera.transform.rotation = Quaternion.Euler(60, 0, 0);
                camera.orthographic=true;
                var sun=new GameObject("B3 regression sun").AddComponent<Light>();
                sun.transform.SetParent(root.transform);sun.type=LightType.Directional;
                foreach(float yaw in new[]{0f,180f})
                {
                    camera.transform.rotation=Quaternion.Euler(60,yaw,0);
                    camera.transform.position=-camera.transform.forward*20;
                    var light=DuelLighting.Apply(camera);
                    var ray=light.transform.forward;ray.y=0;
                    var origin=camera.WorldToViewportPoint(Vector3.zero);
                    var shadow=camera.WorldToViewportPoint(ray);
                    Require(shadow.x<origin.x&&shadow.y<origin.y,"Cast shadow was not lower-left for view "+yaw);
                }
                camera.transform.rotation=Quaternion.Euler(60,0,0);
                var headings = new[] { Vector3.back, new Vector3(-1,0,-1), Vector3.left, new Vector3(-1,0,1),
                    Vector3.forward, new Vector3(1,0,1), Vector3.right, new Vector3(1,0,-1) };
                CheckVariableClips();
                int converted = 0;
                foreach (var card in DuelRules.Load().cards)
                {
                    if (card.IsSpell) continue;
                    Require(DuelSpriteLibrary.Converted(card.id), "Missing B3 troop prefab: " + card.id);
                    converted++;
                    var model = UnityEngine.Object.Instantiate(DuelSpriteLibrary.Prefab(card.id), root.transform, false);
                    var art = model.GetComponent<DuelUnitArt>(); var unit = art.Sprites;
                    Require(unit && !art.Animator, "Converted unit still relies on a 3D Animator: " + card.id);
                    Require(model.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0, "Converted prefab still contains a rigged model");
                    Require(unit.Renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"Sprite cast a second, detached shadow map silhouette");
                    Require(unit.Renderer.receiveShadows,"B3 sprite did not receive building shadows");
                    Require(!art.TeamMarker.enabled,"Permanent team disc was still visible");
                    Require(unit.Renderer.transform.localPosition==Vector3.zero,"Sprite feet were lifted from their anchor");
                    var contact=DuelContactShadow.Attach(art);
                    Require(contact.Renderer.enabled!=unit.Set.Baked&&contact.Renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"Foot contact was missing or duplicated over the baked sheet");
                    contact.SetOpacity(.4f);
                    var contactBlock=new MaterialPropertyBlock();contact.Renderer.GetPropertyBlock(contactBlock);
                    Require(Mathf.Abs(contactBlock.GetFloat("_Opacity")-.4f)<.001f,"Contact shadow did not fade with the corpse");
                    var entity = new EntityState { id = 10, kind = card.id, seat = 0, hp = 100, target = 0 };
                    unit.SetTeam(0);
                    unit.Present(entity,card,39,0,false,1,1);
                    var hitBlock=new MaterialPropertyBlock();unit.Renderer.GetPropertyBlock(hitBlock);
                    Require(hitBlock.GetFloat("_Hit")>.99f,"Damaged sprite did not flash red");
                    unit.Present(entity,card,39,0,false,1,0);unit.Renderer.GetPropertyBlock(hitBlock);
                    Require(hitBlock.GetFloat("_Hit")<.001f,"Damage flash did not return to normal");
                    for (int direction = 0; direction < 8; direction++)
                    {
                        entity.dx = headings[direction].x; entity.dz = headings[direction].z;
                        unit.Present(entity, card, 40+direction, 0, false, 1, 0); unit.AlignToCamera();
                        Require(unit.DirectionIndex == direction, "Wrong camera-relative facing " + direction + ": " + card.id);
                        Require(!unit.Renderer.flipX && !unit.Renderer.flipY, "Asymmetric equipment was mirrored");
                        var sprite = unit.Renderer.sprite;
                        Require(sprite && sprite.rect.width == unit.Set.TileSize, "Missing directional idle sprite");
                        Require(unit.ShadowMatchesFrame,"Ground shadow did not follow the animation frame");
                        var shadowBlock=new MaterialPropertyBlock();unit.ShadowRenderer.GetPropertyBlock(shadowBlock);
                        if(unit.Set.Baked)
                        {
                            Require(sprite.texture.height==unit.Set.TileSize*16,"Baked sheet lost its eight paired shadow rows");
                            Require(unit.ShadowRenderer.sprite.texture==sprite.texture&&unit.ShadowRenderer.sprite.rect.y<unit.Set.TileSize*8,"Baked shadow was not read from the same sheet");
                            Require(Mathf.Abs(unit.ShadowRenderer.transform.position.y-model.transform.position.y-.002f)<.0001f,"Baked shadow plane detached from the feet");
                            Require(Mathf.Abs(unit.ShadowRenderer.transform.forward.y)>.999f,"Baked shadow was standing upright");
                        }
                        else Require(Mathf.Abs(shadowBlock.GetFloat("_GroundHeight")-model.transform.position.y-.002f)<.0001f,"Ground shadow plane detached from the feet");
                        Require(Mathf.Abs(sprite.pivot.y-(unit.Set.TileSize-unit.Set.Baseline)) < .01f, "Foot pivot drifted");
                    }
                    camera.transform.rotation = Quaternion.Euler(60, 180, 0);
                    Require(DuelSpriteUnit.Direction(Vector3.back, camera) == 4, "Opposite player camera did not invert facing");
                    camera.transform.rotation = Quaternion.Euler(60, 0, 0);
                    entity.dx = 0; entity.dz = 1;
                    unit.Present(entity, card, 60, 1, false, 1, 0);
                    var walk = unit.Renderer.sprite;
                    double nextWalkTick=60+30d/unit.Set.LoopFps(DuelSpriteClip.Walk)+.001;
                    unit.Present(entity, card, nextWalkTick, 1, false, 1, 0);
                    Require(unit.Clip == DuelSpriteClip.Walk && walk != unit.Renderer.sprite, "Walk frames did not advance");
                    walk = unit.Renderer.sprite;
                    unit.Present(entity, card, nextWalkTick, 1, false, 1, 0);
                    Require(walk == unit.Renderer.sprite, "A stalled render tick advanced a sprite");

                    entity.attackTick = 100; entity.target = 200; entity.hitTick = 0;
                    unit.Present(entity, card, 100, 0, false, 1, 0);
                    Require(unit.Clip == DuelSpriteClip.Attack && unit.FrameIndex < unit.Set.ImpactFrame, "Attack skipped anticipation");
                    unit.Present(entity, card, 100+card.windupTicks-.01, 0, false, 1, 0);
                    Require(unit.FrameIndex < unit.Set.ImpactFrame, "Contact frame appeared before authority hit");
                    entity.hitTick = 100+card.windupTicks;
                    unit.Present(entity, card, entity.hitTick, 0, false, 1, 0);
                    Require(unit.FrameIndex == unit.Set.ImpactFrame, "Contact frame missed the server hit tick");
                    entity.attackTick = 200; entity.hitTick = 0;
                    unit.Present(entity, card, 200+card.windupTicks, 0, false, .55f, 0);
                    Require(unit.FrameIndex < unit.Set.ImpactFrame, "Frost caused an early contact frame");
                    unit.Present(entity, card, 205, 1, true, 1, 0);
                    Require(unit.Clip == DuelSpriteClip.Idle, "Deploying unit walked or attacked");
                    entity.attackTick = 300; entity.target = 200;
                    unit.Present(entity, card, 300, 0, false, 1, 0);
                    entity.target = 201;
                    unit.Present(entity, card, 302, 0, false, 1, 0);
                    Require(unit.Clip == DuelSpriteClip.Idle, "Cancelled target kept its windup");
                    entity.attackTick = 303;
                    unit.Present(entity, card, 303, 0, false, 1, 0);
                    Require(unit.Clip == DuelSpriteClip.Attack, "Replacement target did not start fresh anticipation");
                    if(card.id=="archer"||card.id=="bomber")
                    {
                        entity.attackTick=400;entity.hitTick=400+card.windupTicks;
                        double released=entity.hitTick-3;
                        unit.Present(entity,card,released-.01,0,false,1,0,released);
                        Require(unit.FrameIndex<unit.Set.ImpactFrame,"Ranged unit released before its projectile");
                        unit.Present(entity,card,released,0,false,1,0,released);
                        Require(unit.FrameIndex==unit.Set.ImpactFrame,"Ranged release waited for projectile impact");
                    }
                    if (card.initialAir || card.transformAfterTicks > 0)
                    {
                        var groundSet = unit.Set;
                        entity.airborne = true;
                        model.transform.position = entity.Position;
                        unit.Present(entity, card, 450, 0, false, 1, 0);
                        var hovering = unit.Renderer.sprite;
                        var hoverClip=unit.Set.FrameCount(DuelSpriteClip.Idle)>1?DuelSpriteClip.Idle:DuelSpriteClip.Walk;
                        unit.Present(entity, card, 450+30d/unit.Set.LoopFps(hoverClip)+.001, 0, false, 1, 0);
                        unit.AlignToCamera();
                        Require(unit.Clip == hoverClip && unit.Renderer.sprite != hovering, "Stationary flyer did not flap: " + card.id);
                        Require(card.transformAfterTicks == 0 || unit.Set != groundSet, "Airborne transition retained the ground sprite set: " + card.id);
                        Require(unit.ShadowMatchesFrame && Mathf.Abs(unit.ShadowRenderer.transform.position.y
                            - DuelMap.GroundHeight(model.transform.position.z) - .002f) < .0001f, "Airborne shadow floated above the battlefield");
                    }
                    unit.BeginDeath(500); unit.PresentDeath(500.4);
                    Require(unit.Opacity > .1f && unit.Opacity < .9f, "Death did not visibly dissolve");
                    unit.PresentDeath(501);
                    Require(unit.Opacity < .001f, "Death left an opaque sprite behind");
                    Require(DuelSpriteLibrary.Card(card.id) && DuelSpriteLibrary.Card(card.id).height >= 256, "Missing matching B3 card art");
                    UnityEngine.Object.DestroyImmediate(model);
                }
                Require(converted > 0, "No B3 characters were available to verify");
                var prefabs = new Dictionary<string, GameObject> { { "guard", DuelSpriteLibrary.Prefab("guard") } };
                using (var preview = new DuelDeploymentPreview(root.transform, prefabs))
                {
                    preview.Show("guard", 0, Vector3.zero, true, true, new[] { Vector3.zero }, 0, false);
                    var ghost = root.GetComponentInChildren<DuelSpriteUnit>();
                    Require(ghost && Mathf.Abs(ghost.Opacity-.42f) < .001f, "Sprite deployment preview lost transparency");
                    Require(ghost.Renderer.sharedMaterial.GetFloat("_ZWrite") == 0, "Transparent preview wrote depth");
                    preview.Show("guard", 1, Vector3.zero, true, false, new[] { Vector3.zero }, 0, false);
                    var block = new MaterialPropertyBlock(); ghost.Renderer.GetPropertyBlock(block);
                    Require(block.GetColor("_BaseColor").r > block.GetColor("_BaseColor").g, "Invalid deployment was not marked red");
                    preview.Hide(); Require(preview.ActiveGhosts == 0, "Hidden sprite preview remained active");
                }
                return "TOWER_DUEL_B3_TESTS PASS: " + converted + " converted units; eight directions, both cameras, anchors, independent clip counts and loop FPS, walk/stall, authority contact, frost, deployment, cancellation, death and matching cards";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
