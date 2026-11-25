using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Realtime.Core;
using UnityEngine;

namespace TowerDuel.Editor
{
    // Run in an empty Play Mode scene; transport arrivals still pass through the actual SDK inbox.
    public static class TowerDuelNetworkTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string ResumeKey="TowerDuel.Resume.v3";
        private static readonly MethodInfo Pump=typeof(DuelSession).GetMethod("Pump",Private,null,new[]{typeof(double)},null);
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private).GetValue(owner);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
        private static void Advance(DuelSession session,double now)=>Pump.Invoke(session,new object[]{now});

        private sealed class Fixture : IDisposable
        {
            public readonly DuelMap Map=DuelMap.Load();
            public readonly DuelRules Rules=DuelRules.Load();
            public readonly DuelSession Session;
            public double Now;
            public ReconnectPolicy Recovery=>Field<ReconnectPolicy>(Session,"recovery");
            public Fixture()
            {
                Session=new DuelSession(Map,Rules);
                // Failed retries throw before opening a socket, keeping timer tests deterministic.
                Set(Session,"server","ftp://invalid");
                Set(Session,"playerName","Network regression");
                Set(Session,"callback","TowerDuelNetworkTests");
            }
            public DuelMessage State(long tick,string type="snapshot",string match="network-test",EffectState[] effects=null,float x=0)
            {
                var towers=new EntityState[Map.towers.Length];
                for(int i=0;i<towers.Length;i++)
                {
                    var t=Map.towers[i];
                    towers[i]=new EntityState{id=t.id,seat=t.seat,kind=t.kind,x=t.x,z=t.z,radius=t.radius,hp=100,maxHP=100};
                }
                return new DuelMessage{type=type,protocol=3,room="TESTAA",match=match,mapHash=Map.Hash,rulesHash=Rules.Hash,
                    seat=0,tick=tick,tickRate=30,snapshotRate=15,phase="playing",energy=10,resumeToken=new string('a',43),
                    players=new[]{new PlayerState{seat=0,connected=true},new PlayerState{seat=1,connected=true}},towers=towers,
                    units=new[]{new EntityState{id=100,seat=0,kind="guard",x=x,z=-3,radius=.32f,hp=100,maxHP=100}},
                    hand=new[]{Rules.cards[0].id,Rules.cards[1].id,Rules.cards[2].id,Rules.cards[3].id},nextCard=Rules.cards[4].id,
                    effects=effects??Array.Empty<EffectState>()};
            }
            public void Deliver(DuelMessage message)
            {
                // Capture time before arrival so assertions do not depend on Editor frame speed.
                Now=DuelSession.Now;
                Session.Transport.Receive(Session.Transport.Epoch,JsonUtility.ToJson(message));
                Advance(Session,Now);
            }
            public void Error(string code)=>Deliver(new DuelMessage{type="error",code=code});
            public void Dispose()=>Session.Dispose();
        }

        public static string Run()
        {
            Require(Application.isPlaying,"Run TowerDuelNetworkTests in an empty Play Mode scene.");
            bool hadResume=PlayerPrefs.HasKey(ResumeKey);
            string savedResume=PlayerPrefs.GetString(ResumeKey,"");
            try
            {
                BackoffAndExpiry();
                RestoreAndPendingAction();
                MultiplePendingDeployments();
                IndependentDeploymentPreviews();
                CancellationAndFatalErrors();
                NearestDeployment();
                SharedEffectTimeline();
                FrostBoundaryAtRenderTick();
                StalledTimelineAndMatchReset();
                RangedWindupAndRelease();
                CosmeticImpactDeduplication();
                DeploymentWaitAndRecovery();
                FixedBattlefieldView();
                MobileBattlefieldFraming();
                return "TOWER_DUEL_NETWORK_TESTS PASS: SDK backoff/expiry, saved-seat restore, pending-action recovery, concurrent deployment/energy reservation, independent previews, cancellation, nearest legal deployment, shared effect timing, rendered frost boundary, snapshot stalls, match resets, ranged windup/release, cosmetic impact deduplication and deployment readiness/recovery";
            }
            finally
            {
                if(hadResume)PlayerPrefs.SetString(ResumeKey,savedResume);else PlayerPrefs.DeleteKey(ResumeKey);
                PlayerPrefs.Save();
            }
        }

        private static void MobileBattlefieldFraming()
        {
            var map=DuelMap.Load();
            // Horizontal fit may not drop playable outer columns for phones.
            foreach(var resolution in new[]{
                new Vector2(320,780),new Vector2(355,780),
                new Vector2(390,844),new Vector2(815,895)})
            {
                float size=TowerDuelClient.HorizontalFitSize(map.width*.5f,resolution.x,resolution.y);
                float halfViewWidth=size*resolution.x/resolution.y;
                Require(halfViewWidth>=map.width*.5f,
                    "Narrow screen cropped legal placement coordinates: "+resolution);
            }
            // A side notch reduces safe width independently of screen width.
            float withNotch=TowerDuelClient.HorizontalFitSize(map.width*.5f,295,780);
            Require(withNotch*295f/780f>=map.width*.5f,
                "Horizontal safe-area cropping bypassed the camera fit");
            Require(Mathf.Abs(TowerDuelClient.VisualScaleForSize(18f)-1f)<.001f,
                "Large-screen sprite presentation changed");
            Require(TowerDuelClient.VisualScaleForSize(26f)>1f&&
                    TowerDuelClient.VisualScaleForSize(26f)<=1.20f,
                "Narrow-screen sprite compensation out of bounds");
        }

        private static void FixedBattlefieldView()
        {
            using(var f=new Fixture())
            {
                Require(f.Map.HasWall&&f.Map.id=="wall-gates-1","Canonical wall map was not imported");
                float halfWidth=f.Map.width/2;
                float gateHalf=f.Map.bridgeHalfWidth;
                Require(f.Map.width==20&&gateHalf==1&&-f.Map.bridges[0]-gateHalf==5&&f.Map.bridges[1]-gateHalf==5&&
                        f.Map.bridges[0]-gateHalf+halfWidth==3&&halfWidth-f.Map.bridges[1]-gateHalf==3,
                        "Expected two-cell gates and five central wall cells on each side");
                foreach(var tower in f.Map.towers)
                    if(tower.kind=="tower")Require(tower.x==f.Map.bridges[tower.x<0?0:1],"Front tower must line up with its gate");
                var root=new GameObject("Fixed battlefield regression");DuelPresentation presentation=null;
                GameObject temporaryCamera=null;var camera=Camera.main;
                if(!camera){temporaryCamera=new GameObject("Environment test camera");camera=temporaryCamera.AddComponent<Camera>();temporaryCamera.tag="MainCamera";}
                var oldRotation=camera.transform.rotation;
                try
                {
                    presentation=new DuelPresentation(root.transform,f.Map,f.Rules);
                    Require(presentation.Environment!=null&&presentation.Environment.BuildingCount==6,"B3 buildings not active");
                    var sideWallBounds=new Bounds?[2];
                    foreach(var sprite in presentation.Environment.Root.GetComponentsInChildren<SpriteRenderer>())
                    {
                        bool overlay=sprite.name=="Paired ground shadow"||sprite.name=="Wall ground shadow";
                        var textureSlot=overlay?"_B3ShadowTex":"_B3EnvironmentTex";
                        Require(sprite.sharedMaterial.GetTexture(textureSlot)==sprite.sprite.texture,
                                "Each environment sprite must sample its own texture");
                        Require(sprite.drawMode==SpriteDrawMode.Simple,"Battlefield art must use single sprite strips");
                        if(sprite.name!="Arena side wall")continue;
                        var bounds=sprite.bounds;
                        int side=bounds.center.x<0?0:1;
                        if(sideWallBounds[side].HasValue)
                        {
                            var total=sideWallBounds[side].Value;
                            total.Encapsulate(bounds);
                            sideWallBounds[side]=total;
                        }
                        else sideWallBounds[side]=bounds;
                    }
                    for(int side=0;side<2;side++)
                    {
                        Require(sideWallBounds[side].HasValue,"Expected a wall at each side of the arena");
                        var bounds=sideWallBounds[side].Value;
                        var innerEdge=side==0?bounds.max.x:bounds.min.x;
                        Require(Mathf.Abs(Mathf.Abs(innerEdge)-f.Map.width/2)<.001f&&
                                Mathf.Abs(bounds.size.z-f.Map.length)<.001f,
                                "Side wall must span the outside edge without taking deployable space");
                    }
                    int wallFaces=0,wallShadows=0;
                    float wallTopWidth=0;
                    foreach(var wall in presentation.Environment.Root.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if(wall.name=="Castle wall face")
                        {
                            wallFaces++;
                            Require(Mathf.Abs(Mathf.Abs(wall.transform.localPosition.z)-f.Map.wallHalfWidth)<.001f,
                                    "Wall face must stand at a solid wall edge");
                        }
                        else if(wall.name=="Castle wall top")
                        {
                            wallTopWidth+=wall.bounds.size.x;
                            Require(Mathf.Abs(wall.bounds.size.z-f.Map.wallHalfWidth*2)<.001f,
                                    "Wall top must span the full wall thickness");
                            foreach(float gate in f.Map.bridges)
                                Require(gate+gateHalf<=wall.bounds.min.x+.001f||gate-gateHalf>=wall.bounds.max.x-.001f,
                                        "Solid wall top must not cover a gate");
                        }
                        Require(wall.name!="Gate lintel art"&&wall.name!="Open gate face",
                                "A roofless gate must not have a lintel or an arch");
                        if(wall.name=="Wall ground shadow")
                        {
                            wallShadows++;
                            foreach(float gate in f.Map.bridges)
                                Require(gate+f.Map.bridgeHalfWidth<=wall.bounds.min.x+.001f||
                                        gate-f.Map.bridgeHalfWidth>=wall.bounds.max.x-.001f,
                                        "Wall shadows must leave both gates clear");
                        }
                    }
                    Require(wallFaces==6&&Mathf.Abs(wallTopWidth-(f.Map.width-4*gateHalf))<.001f,
                            "Expected paired wall faces and masonry tops spanning all three solid sections");
                    Require(wallShadows==5,"Three wall sections and two side walls must cast shadows");

                    Require(presentation.Environment.Root.GetComponentsInChildren<MeshRenderer>().Length==0,
                            "Battlefield walls must use B3 sprite art");


                    var rotation=Quaternion.Euler(60,0,0);
                    camera.transform.rotation=rotation;
                    var cameraPosition=camera.transform.position;
                    var ground=presentation.Environment.Root.Find("Ground");
                    var groundBounds=ground.GetComponent<SpriteRenderer>().bounds;
                    Require(Mathf.Abs(groundBounds.size.x-f.Map.width)<.001f&&Mathf.Abs(groundBounds.size.z-f.Map.length)<.001f,
                            "Ground art must cover the entire playable map");
                    var groundRotation=ground.rotation;
                    var groundPosition=ground.position;
                    // A 180-degree seat transform must swap lanes as well as near/far sides.
                    // The same transform maps a clicked view point back to its server position.
                    var asymmetric=new Vector3(f.Map.bridges[1],1.5f,4);
                    Require(DuelPresentation.MirrorDepth(asymmetric,0)==asymmetric&&
                            DuelPresentation.MirrorDepth(asymmetric,1)==new Vector3(-asymmetric.x,asymmetric.y,-asymmetric.z)&&
                            DuelPresentation.MirrorDepth(DuelPresentation.MirrorDepth(asymmetric,1),1)==asymmetric,
                            "Seat-one view must reverse both lanes and round-trip input without changing height");
                    for(int seat=0;seat<2;seat++)
                    {
                        presentation.Environment.SetView(camera,seat);
                        Require(Quaternion.Angle(camera.transform.rotation,rotation)<.01f&&camera.transform.position==cameraPosition&&
                                ground.position==groundPosition&&ground.rotation==groundRotation,
                                "Changing seats must preserve camera and ground transforms");
                        foreach(var tower in f.Map.towers)
                        {
                            bool own=tower.seat==seat;
                            var point=presentation.Environment.Building(tower.id).transform.position;
                            Require(point==DuelPresentation.MirrorDepth(tower.Position,seat)&&(point.z<0)==own&&
                                    presentation.Environment.BuildingFrame(tower.id)==(own?0:3),
                                    "Own buildings must be blue on the near side with seat-one lanes reversed");
                        }
                    }
                    var state=f.State(100,"welcome");
                    state.seat=1;state.units[0].seat=1;state.units[0].x=f.Map.bridges[1];state.units[0].z=4;
                    f.Deliver(state);presentation.Update(f.Session);
                    var nearPoint=new Vector3(-f.Map.bridges[1],0,-4);
                    var serverPoint=DuelPresentation.MirrorDepth(nearPoint,state.seat);
                    Require(serverPoint==new Vector3(f.Map.bridges[1],0,4)&&DuelPresentation.MirrorDepth(serverPoint,state.seat)==nearPoint&&
                            f.Map.CanDeploy(state.seat,serverPoint,state,f.Rules.Find("guard")),
                            "Seat one must deploy to its authoritative half in the opposite visual lane");
                    var unit=root.transform.Find("Battle visuals/guard 100");
                    Require(unit&&unit.position.x==nearPoint.x&&unit.position.z==nearPoint.z,
                            "Seat one unit must render on the near side with its lane reversed");
                }
                finally
                {
                    presentation?.Dispose();camera.transform.rotation=oldRotation;
                    if(temporaryCamera)UnityEngine.Object.Destroy(temporaryCamera);
                    UnityEngine.Object.Destroy(root);
                }
            }
        }
        private static void BackoffAndExpiry()
        {
            using(var f=new Fixture())
            {
                f.Deliver(f.State(100,"welcome"));
                f.Error("connection_closed");
                Require(f.Session.Recovering&&!f.Session.CanAct,"Welcome did not arm recovery.");
                double deadline=f.Recovery.Deadline,next=f.Recovery.Next;
                Require(Math.Abs(next-f.Now-.5)<1e-6,"Initial retry did not use the SDK delay.");
                Advance(f.Session,next-.001);
                Require(f.Recovery.Attempts==0,"Retried before the SDK schedule.");
                foreach(double delay in new[]{1d,2d,3d,3d})
                {
                    next=f.Recovery.Next;
                    Advance(f.Session,next);
                    Require(Math.Abs(f.Recovery.Next-next-delay)<1e-6,"Failed retry did not use capped exponential backoff.");
                    Require(f.Recovery.Deadline==deadline,"A failed attempt extended the recovery window.");
                }
                // The window also expires while waiting for a handshake, before its own timeout.
                Set(f.Session,"<Connecting>k__BackingField",true);
                Advance(f.Session,deadline);
                Require(f.Session.State==null&&!f.Session.Recovering&&!f.Session.Connecting,"Recovery did not expire at its deadline.");
                Require(!f.Recovery.Armed&&double.IsPositiveInfinity(f.Recovery.Next),"Expiry left a retry armed.");
            }
        }

        private static void RestoreAndPendingAction()
        {
            using(var f=new Fixture())
            {
                f.Deliver(f.State(100,"welcome"));
                Require(f.Session.Deploy(new Vector3(-5,0,-3)),"Could not create a pending deployment.");
                long action=FirstPending(f.Session).action;
                int oldEpoch=f.Session.Transport.Epoch;
                f.Error("connection_closed");
                f.Session.Transport.Receive(oldEpoch,JsonUtility.ToJson(f.State(500,"welcome")));
                Advance(f.Session,f.Now);
                Require(f.Session.Recovering&&f.Session.State.tick==100,"An old transport callback replaced the recovering state.");
                var welcome=f.State(120,"welcome");welcome.actionWatermark=action;
                f.Deliver(welcome);
                Require(!f.Session.Recovering&&f.Session.Resumes==1&&f.Session.Pending,"Resume lost the pending action or failed to reset recovery.");
                var result=new DuelMessage{type="action",match=welcome.match,action=action,accepted=true};
                f.Deliver(result);f.Deliver(result);
                Require(!f.Session.Pending&&f.Session.Accepted==1,"A repeated confirmation was presented more than once.");
                Require(f.Session.Deploy(new Vector3(-5,0,-3))&&FirstPending(f.Session).action>action,"Resume reused an acknowledged action ID.");
            }
            using(var restored=new Fixture())
            {
                Require(restored.Session.TryRestore("ftp://invalid","TowerDuelNetworkTests"),"Saved-seat restoration did not start.");
                Require(restored.Session.Recovering&&restored.Recovery.Armed,"Saved credentials did not arm the SDK policy.");
                restored.Deliver(restored.State(140,"welcome"));
                Require(restored.Session.Resumes==1&&!restored.Session.Recovering&&restored.Recovery.Attempts==0,"Restored welcome did not reset retry state.");
            }
        }

        private static DeployMessage FirstPending(DuelSession session)
        {
            foreach(var item in Field<SortedDictionary<long,DeployMessage>>(session,"pending").Values)return item;
            throw new InvalidOperationException("Expected a pending deployment");
        }
        private static void MultiplePendingDeployments()
        {
            using(var f=new Fixture())
            {
                var welcome=f.State(100,"welcome");f.Deliver(welcome);
                var queue=Field<SortedDictionary<long,DeployMessage>>(f.Session,"pending");
                string[] hand=(string[])welcome.hand.Clone();
                Vector3 Point(string card)
                {
                    Require(f.Map.TryNearestDeployment(0,new Vector3(-4.5f,0,-3.5f),f.Session.State,f.Rules.Find(card),out var point),"No legal test point for "+card);
                    return point;
                }
                float firstThree=0;
                for(int i=0;i<3;i++)
                {
                    firstThree+=f.Rules.Find(hand[i]).cost;
                    Require(f.Session.Deploy(Point(hand[i]),hand[i]),"An independent release was blocked by an earlier pending action");
                }
                Require(queue.Count==3&&Mathf.Abs(f.Session.AvailableEnergy-(10-firstThree))<.001f,"Pending actions did not reserve their own energy");
                Require(!f.Session.Deploy(Point(hand[0]),hand[0]),"The same card was queued twice");
                Require(!f.Session.Deploy(Point(hand[3]),hand[3]),"An unaffordable fourth action was queued");

                var actions=new List<long>(queue.Keys);
                var next=typeof(DuelSession).GetMethod("NextDeployment",Private);
                Set(f.Session,"sentThrough",0L);
                for(int i=0;i<actions.Count;i++)
                {
                    var action=(DeployMessage)next.Invoke(f.Session,new object[]{f.Now});
                    Require(action.action==actions[i],"Deployment send order was not monotonic");
                    Set(f.Session,"sentThrough",action.action);
                }
                Set(f.Session,"retryAt",f.Now+1);
                Require(next.Invoke(f.Session,new object[]{f.Now})==null,"In-flight actions were resent immediately");
                Require(((DeployMessage)next.Invoke(f.Session,new object[]{f.Now+2})).action==actions[0],"Retry did not restart at the oldest pending action");

                // A snapshot can include a processed action before its reliable reply arrives.
                // Regeneration then makes a fourth distinct release affordable.
                var snapshot=f.State(300);
                snapshot.actionWatermark=actions[0];snapshot.handVersion=1;
                snapshot.hand=new[]{welcome.nextCard,hand[1],hand[2],hand[3]};
                snapshot.nextCard=f.Rules.cards[5].id;snapshot.energy=10;
                f.Deliver(snapshot);
                Require(Mathf.Abs(f.Session.AvailableEnergy-(10-firstThree+f.Rules.Find(hand[0]).cost))<.001f,"Snapshot double-charged acknowledged energy");
                Require(f.Session.Deploy(Point(hand[3]),hand[3])&&queue.Count==4,"Fourth pending action was not supported when energy permitted");
                actions=new List<long>(queue.Keys);
                f.Error("connection_closed");
                var resumed=f.State(320,"welcome");resumed.actionWatermark=actions[2];resumed.handVersion=3;
                resumed.hand=new[]{f.Rules.cards[4].id,f.Rules.cards[5].id,f.Rules.cards[6].id,hand[3]};
                resumed.nextCard=f.Rules.cards[7].id;resumed.energy=5;
                f.Deliver(resumed);
                Require(queue.Count==4&&!f.Session.Recovering,"Reconnection discarded outstanding actions");
                Require(Mathf.Abs(f.Session.AvailableEnergy-(5-f.Rules.Find(hand[3]).cost))<.001f,"Recovered watermark reserved already processed energy");
                foreach(long action in actions)
                {
                    var result=new DuelMessage{type="action",match=welcome.match,action=action,accepted=true};
                    f.Deliver(result);f.Deliver(result);
                }
                Require(!f.Session.Pending&&f.Session.Accepted==4,"Independent confirmations were lost or counted twice");
            }
        }
        private static void IndependentDeploymentPreviews()
        {
            using(var f=new Fixture())
            {
                var root=new GameObject("Multi-touch preview regression");
                try
                {
                    using(var presentation=new DuelPresentation(root.transform,f.Map,f.Rules))
                    {
                        var previews=new DuelDeploymentPreview[4];
                        string[] hand={"guard","archer","swords","frost"};
                        for(int i=0;i<previews.Length;i++)
                        {
                            previews[i]=presentation.AcquirePreview();
                            var card=f.Rules.Find(hand[i]);var point=new Vector3(-4.5f+i*2,0,-3.5f);
                            presentation.Preview(point,true,true,card.id,0,DuelRules.Formation(0,point,card),card.IsSpell?card.radius:card.BodyRadius,null,previews[i]);
                            Require(previews[i].ActiveKind==card.id,"Independent pointer preview used the wrong card");
                        }
                        presentation.ReleasePreview(previews[1]);
                        Require(previews[1].ActiveKind==null&&previews[0].ActiveKind==hand[0]&&previews[2].ActiveKind==hand[2]&&previews[3].ActiveKind==hand[3],"Cancelling one preview hid another pointer");
                        var reused=presentation.AcquirePreview();
                        Require(reused==previews[1],"Cancelled preview was not reusable");
                        presentation.ReleasePreview(reused);
                        for(int i=0;i<previews.Length;i++)presentation.ReleasePreview(previews[i]);
                        foreach(var preview in previews)Require(preview.ActiveKind==null,"Preview remained after cancellation");
                    }
                }
                finally{UnityEngine.Object.Destroy(root);}
            }
        }

        private static void CancellationAndFatalErrors()
        {
            using(var f=new Fixture())
            {
                f.Error("connection_closed");
                Require(!f.Session.Recovering&&!f.Recovery.Armed,"An initial failure without a seat started automatic retries.");
                f.Deliver(f.State(100,"welcome"));f.Error("connection_closed");
                f.Session.Disconnect();Advance(f.Session,f.Now+60);
                Require(!f.Session.Recovering&&!f.Recovery.Armed&&f.Recovery.Attempts==0,"Manual disconnect restarted recovery.");
                f.Deliver(f.State(200,"welcome"));f.Error("connection_closed");f.Error("rules_mismatch");
                Require(f.Session.State==null&&!f.Session.Recovering&&!f.Recovery.Armed,"An incompatible server kept retrying.");
            }
        }

        private static void NearestDeployment()
        {
            using(var f=new Fixture())foreach(int seat in new[]{0,1})
            {
                var state=f.State(100);state.seat=seat;
                float forward=seat==0?1:-1;
                float home=-(Mathf.Ceil(f.Map.BarrierHalfWidth+.40f-.5f-1e-4f)+.5f);
                var guard=f.Rules.Find("guard");var squad=f.Rules.Find("swords");var spell=f.Rules.Find("fireball");
                void Expect(float x,float z,CardDefinition card,float targetX,float targetZ)
                {
                    Require(f.Map.TryNearestDeployment(seat,new Vector3(x,0,z*forward),state,card,out var p),"No legal drag destination for "+card.id);
                    Require(Mathf.Abs(p.x-targetX)<.001f&&Mathf.Abs(p.z-targetZ*forward)<.001f,"Wrong nearest destination for "+card.id+": "+p);
                    Require(f.Map.CanDeploy(seat,p,state,card),"Nearest destination contains an illegal formation member.");
                }
                Expect(-4.2f,-3.8f,guard,-4.5f,-3.5f);
                // The raw aim may cross the wall or doorway, but the resolved formation must remain legal.
                foreach(float x in new[]{0f,f.Map.bridges[0],f.Map.bridges[1]})
                    foreach(float z in new[]{-.6f,-.5f,-.2f,0f,.2f,.5f,.6f})
                    {
                        var aim=new Vector3(x,0,z*forward);
                        foreach(var card in new[]{guard,squad})
                        {
                            Require(!f.Map.CanDeploy(seat,aim,state,card),"Raw aim at the wall/doorway must remain illegal");
                            Require(f.Map.TryNearestDeployment(seat,aim,state,card,out var resolved),"Crossing the wall/doorway lost the deployment preview");
                            Require(f.Map.CanDeploy(seat,resolved,state,card),"Wall/doorway aim resolved to an illegal formation");
                            Require(resolved.z*forward<0,"Intact enemy towers must not unlock enemy placement");
                        }
                    }
                Require(Mathf.Abs(f.Map.SnapDeployment(Vector3.zero,guard).x)==.5f,
                        "A map with an even width must have no cell centred on the seam");
                Expect(-4.2f,3.2f,guard,-4.5f,home);
                Expect(-6.7f,-7.7f,guard,-7.5f,-7.5f);
                Expect(30,-3.2f,guard,9.5f,-3.5f);
                Expect(30,-3.2f,squad,8.5f,-3.5f);
                Expect(-4.2f,-1.7f,squad,-4.5f,Mathf.Min(-1.5f,home));
                foreach(var tower in state.towers)if(tower.seat==seat&&tower.kind=="tower"&&tower.x<0)tower.hp=0;
                Expect(-4.2f,3.2f,guard,-4.5f,home);
                foreach(var tower in state.towers)if(tower.seat!=seat&&tower.kind=="tower"&&tower.x<0)tower.hp=0;
                Expect(-4.2f,3.2f,guard,-4.5f,3.5f);
                Require(!f.Map.CanDeploy(seat,new Vector3(0,0,3*forward),state,guard),
                        "A single destroyed tower must not unlock the centre seam");
                Expect(4.2f,3.2f,guard,f.Map.HasWall?4.5f:-.5f,f.Map.HasWall?home:3.5f);
                Expect(-4.2f,5.4f,squad,-4.5f,f.Map.HasWall?3.5f:4.5f);
                state=f.State(102);state.seat=seat;
                Expect(-4.2f,3.2f,guard,-4.5f,home);
                Expect(-4.24f,3.16f,spell,-4.24f,3.16f);
                Expect(30,40,spell,10,15);
                state.energy=0;
                Expect(-4.2f,-.7f,guard,-4.5f,home);
                f.Deliver(state);
                Require(!f.Session.Deploy(new Vector3(-4.5f,0,-2.5f*forward)),"Nearest destination bypassed the energy check.");
                Require(!f.Map.TryNearestDeployment(seat,new Vector3(float.NaN,0,0),state,guard,out _),"Nonfinite aim resolved to a cell.");
                Require(!f.Map.TryNearestDeployment(seat,new Vector3(0,0,float.PositiveInfinity),state,spell,out _),"Nonfinite spell aim resolved to a point.");
                var blocked=new DuelMap{width=1,length=1,deploymentCellSize=1,riverHalfWidth=1.5f};
                Require(!blocked.TryNearestDeployment(seat,Vector3.zero,state,guard,out _),"A map with no legal cell produced a destination.");
            }
        }

        private static void DeploymentWaitAndRecovery()
        {
            using(var f=new Fixture())
            {
                var root=new GameObject("Deployment time regression");
                DuelPresentation presentation=null;
                try
                {
                    foreach(var tower in f.Map.towers)
                    {
                        string color=tower.seat==0?"blue":"red";
                        new GameObject("SM_"+color+(tower.kind=="keep"?"_MainCastle":"_Tower_"+(tower.x<0?"-5":"5"))).transform.SetParent(root.transform,false);
                    }
                    presentation=new DuelPresentation(root.transform,f.Map,f.Rules);
                    DuelMessage Deploy(long tick,string type="snapshot",float x=0,long attack=0)
                    {
                        var state=f.State(tick,type,x:x);
                        state.units[0].readyTick=130;
                        state.units[0].attackTick=attack;
                        return state;
                    }
                    f.Deliver(Deploy(100,"welcome"));presentation.Update(f.Session);
                    var visuals=Field<IDictionary>(presentation,"units");
                    var art=(DuelUnitArt)visuals[100L].GetType().GetField("Art").GetValue(visuals[100L]);
                    f.Deliver(Deploy(128,x:.2f));presentation.Update(f.Session);
                    if(art.Animator)art.Animator.Update(.1f);
                    bool idle=art.Sprites?art.Sprites.Clip==DuelSpriteClip.Idle:art.Animator.GetFloat("Speed")==0;
                    Require(idle&&(long)visuals[100L].GetType().GetField("Attack").GetValue(visuals[100L])==0,"A pushed deploying troop walked or attacked.");
                    Advance(f.Session,f.Now+5);presentation.Update(f.Session);
                    Require(f.Session.State.units[0].IsDeploying(f.Session.RenderTick),"A stalled timeline completed deployment using local wall time.");
                    f.Deliver(Deploy(129,"welcome",x:.2f));presentation.Update(f.Session);
                    Require(f.Session.State.units[0].readyTick==130&&f.Session.State.units[0].IsDeploying(f.Session.RenderTick),"Recovery reset or skipped the remaining deployment time.");
                    f.Deliver(Deploy(134,x:.3f,attack:130));Advance(f.Session,f.Now+.2);presentation.Update(f.Session);
                    Require(!f.Session.State.units[0].IsDeploying(f.Session.RenderTick)&&(long)visuals[100L].GetType().GetField("Attack").GetValue(visuals[100L])==130,"A ready troop did not start its authoritative attack.");
                }
                finally{presentation?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
            }
        }

        private static void RangedWindupAndRelease()
        {
            foreach(string kind in new[]{"archer","bomber"})
            {
                using(var f=new Fixture())
                {
                    var root=new GameObject("Attack windup regression");
                    DuelPresentation presentation=null;
                    try
                    {
                        foreach(var tower in f.Map.towers)
                        {
                            string color=tower.seat==0?"blue":"red";
                            string name="SM_"+color+(tower.kind=="keep"?"_MainCastle":"_Tower_"+(tower.x<0?"-5":"5"));
                            new GameObject(name).transform.SetParent(root.transform,false);
                        }
                        presentation=new DuelPresentation(root.transform,f.Map,f.Rules);
                        DuelMessage Attack(long tick,string type="snapshot",long hit=0)
                        {
                            var state=f.State(tick,type);
                            state.units=new[]{
                                new EntityState{id=100,seat=0,kind=kind,x=0,z=-5,radius=.32f,hp=90,maxHP=90,attackTick=100,hitTick=hit,hitTarget=hit>0?200:0,target=hit>0?201:200},
                                new EntityState{id=200,seat=1,kind="guard",x=0,z=-3,radius=.32f,hp=200,maxHP=220},
                                new EntityState{id=201,seat=1,kind="guard",x=3,z=-5,radius=.32f,hp=220,maxHP=220}};
                            return state;
                        }
                        var frost=new EffectState{id=900,kind="frost",seat=1,x=0,z=-5,radius=2.8f,start=90,impact=90,end=200};
                        var initial=Attack(100,"welcome");initial.effects=new[]{frost};
                        f.Deliver(initial);presentation.Update(f.Session);
                        Require(presentation.ArrowsFired==0,"Windup launched a projectile for "+kind);
                        var visuals=Field<IDictionary>(presentation,"units");
                        var art=(DuelUnitArt)visuals[100L].GetType().GetField("Art").GetValue(visuals[100L]);
                        if(art.Sprites&&f.Session.RenderTick<100)
                            Require(art.Sprites.Clip==DuelSpriteClip.Idle,"Sprite anticipated an attack before its render tick for "+kind);
                        Advance(f.Session,f.Now+.2);presentation.Update(f.Session);
                        if(art.Animator)art.Animator.Update(.05f);
                        bool attacking=art.Sprites?art.Sprites.Clip==DuelSpriteClip.Attack:art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash("Attack")||art.Animator.IsInTransition(0);
                        Require(attacking,"Windup did not start the attack animation for "+kind);
                        var slowed=Attack(102);slowed.units[0].slowed=true;
                        slowed.effects=new[]{frost};
                        f.Deliver(slowed);presentation.Update(f.Session);
                        Require(Mathf.Abs((art.Sprites?art.Sprites.PlaybackSpeed:art.Animator.speed)-f.Rules.Find("frost").slow)<.0001f,"Frost did not slow the windup animation.");
                        long hitTick=100+f.Rules.Find(kind).windupTicks;
                        f.Deliver(Attack(hitTick,hit:hitTick));presentation.Update(f.Session);
                        Require(presentation.ArrowsFired==1,"Confirmed release did not launch a projectile for "+kind);
                        var arrows=Field<IList>(presentation,"arrows");
                        var destination=(Vector3)arrows[0].GetType().GetField("To").GetValue(arrows[0]);
                        Require(Mathf.Abs(destination.x)<.001f,"Projectile used a new target instead of the confirmed hit target.");
                        if(art.Sprites)
                        {
                            double released=(double)arrows[0].GetType().GetField("Started").GetValue(arrows[0]);
                            // Observe() advances to the transport's arrival time, which can
                            // follow the fixture's pre-serialization timestamp. Start there
                            // so Editor serialization time cannot leave us before release.
                            double observedAt=Math.Max(f.Now,Field<double>(f.Session,"lastReceive"));
                            Advance(f.Session,observedAt+Math.Max(0,(released-f.Session.RenderTick+.001)/30));presentation.Update(f.Session);
                            Require(art.Sprites.FrameIndex==art.Sprites.Set.ImpactFrame&&art.Sprites.Clip==DuelSpriteClip.Attack,$"Release pose did not match projectile departure for {kind}: tick={f.Session.RenderTick:F4}, departure={released:F4}, clip={art.Sprites.Clip}, frame={art.Sprites.FrameIndex}");
                            var projectile=(Transform)arrows[0].GetType().GetField("Root").GetValue(arrows[0]);
                            Require(projectile.gameObject.activeSelf,"Release pose appeared without a visible projectile for "+kind);
                        }
                        presentation.Update(f.Session);
                        f.Deliver(Attack(hitTick+2,hit:hitTick));presentation.Update(f.Session);
                        Require(presentation.ArrowsFired==1,"Repeated snapshots duplicated a projectile.");

                        presentation.Reset();
                        var winding=Attack(200,"welcome");winding.units[0].attackTick=200;
                        f.Deliver(winding);presentation.Update(f.Session);
                        var dead=Attack(202);dead.units=new[]{dead.units[1],dead.units[2]};
                        f.Deliver(dead);presentation.Update(f.Session);
                        dead=Attack(220);dead.units=new[]{dead.units[1],dead.units[2]};
                        f.Deliver(dead);presentation.Update(f.Session);
                        Require(presentation.ArrowsFired==0&&Field<IList>(presentation,"arrows").Count==0&&presentation.DeathCount==1,"Death during windup left a delayed projectile for "+kind);

                        presentation.Reset();
                        f.Deliver(Attack(300,"welcome",hitTick));presentation.Update(f.Session);
                        Require(presentation.ArrowsFired==0,"Restoring an actor replayed an old projectile.");
                    }
                    finally{presentation?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
                }
            }
        }

        private static void CosmeticImpactDeduplication()
        {
            using(var f=new Fixture())
            {
                var root=new GameObject("Cosmetic impact regression");
                DuelPresentation presentation=null;
                try
                {
                    foreach(var tower in f.Map.towers)
                    {
                        string color=tower.seat==0?"blue":"red";
                        new GameObject("SM_"+color+(tower.kind=="keep"?"_MainCastle":"_Tower_"+(tower.x<0?"-5":"5"))).transform.SetParent(root.transform,false);
                    }
                    presentation=new DuelPresentation(root.transform,f.Map,f.Rules);
                    DuelMessage Message(long tick,string type="snapshot",long hit=0)
                    {
                        var state=f.State(tick,type);
                        state.units=new[]{
                            new EntityState{id=100,seat=0,kind="guard",x=0,z=-3,radius=.32f,hp=100,maxHP=100,hitTick=hit,hitTarget=hit>0?200:0},
                            new EntityState{id=200,seat=1,kind="guard",x=0,z=-2,radius=.32f,hp=100,maxHP=100}};
                        return state;
                    }
                    f.Deliver(Message(100,"welcome"));presentation.Update(f.Session);
                    Require(presentation.Effects.FeedbackPlayed==0,"Restoring a roster played old hit feedback.");
                    f.Deliver(Message(106,hit:102));presentation.Update(f.Session);
                    Require(presentation.Effects.FeedbackPlayed==2,"Melee contact did not emit one slash and one impact.");
                    presentation.Update(f.Session);f.Deliver(Message(108,hit:102));presentation.Update(f.Session);
                    Require(presentation.Effects.FeedbackPlayed==2,"Repeated snapshots duplicated melee feedback.");
                    var broken=Message(110,hit:102);broken.towers[0].hp=0;
                    f.Deliver(broken);presentation.Update(f.Session);
                    Require(presentation.Effects.FeedbackPlayed==3,"Destroying a tower did not emit collapse feedback.");
                    presentation.Update(f.Session);Require(presentation.Effects.FeedbackPlayed==3,"Repeated tower state replayed destruction.");
                    presentation.Reset();Require(presentation.Effects.ActiveFeedback==0,"Reset retained combat feedback.");
                }
                finally{presentation?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
            }
        }

        private static EffectState Fireball(long start,long impact,long end)=>new EffectState{id=900,kind="fireball",seat=0,x=-5,z=-3,radius=2.2f,start=start,impact=impact,end=end};
        private static IDictionary Visuals(DuelEffects effects)=>Field<IDictionary>(effects,"visuals");
        private static void WithEffects(Action<DuelEffects,Func<int>> test)
        {
            var root=new GameObject("Network test effects");
            int impacts=0;
            var effects=new DuelEffects(root.transform,_=>impacts++);
            try{test(effects,()=>impacts);}
            finally{effects.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }

        private static void SharedEffectTimeline()
        {
            using(var f=new Fixture())WithEffects((effects,impacts)=>
            {
                var spell=Fireball(102,106,110);
                f.Deliver(f.State(100,"welcome"));effects.Update(f.Session);
                f.Deliver(f.State(104,effects:new[]{spell},x:4));effects.Update(f.Session);
                Require(Visuals(effects).Count==0&&impacts()==0,"An effect appeared before its render-time start.");
                f.Deliver(f.State(106,effects:new[]{spell},x:6));effects.Update(f.Session);
                Require(Visuals(effects).Count==1&&impacts()==0,"The fireball impacted before the shared render tick.");
                var position=f.Session.Sample(f.Session.State.units[0]);
                Require(Math.Abs(position.x-2)<.001,"Soldiers did not sample the same delayed render tick.");
                f.Deliver(f.State(110,x:10));effects.Update(f.Session);
                Require(f.Session.State.effects.Length==0&&Visuals(effects).Count==1,"A newer roster removed the spell before its rendered lifetime ended.");
                Require(impacts()==1&&Math.Abs(f.Session.RenderTick-106)<.001,"Impact sound did not follow the SDK render tick.");
                effects.Update(f.Session);
                Require(impacts()==1,"Impact sound repeated on another rendered frame.");
                Advance(f.Session,f.Now+.2);effects.Update(f.Session);
                Require(Visuals(effects).Count==0&&impacts()==1,"An expired effect or its impact survived past the shared timeline.");
            });
        }

        private static void FrostBoundaryAtRenderTick()
        {
            using(var f=new Fixture())
            {
                var frost=new EffectState{id=900,kind="frost",seat=1,x=0,z=-3,radius=2.8f,start=80,impact=90,end=146};
                void Deliver(long tick,float x,bool slowed,string type="snapshot")
                {
                    var state=f.State(tick,type,effects:new[]{frost},x:x);
                    state.units[0].slowed=slowed;f.Deliver(state);
                }
                bool Slowed()=>f.Session.SampleSlowed(f.Session.State.units[0]);
                Deliver(100,3.3f,true,"welcome");
                Require(!Slowed(),"A soldier outside frost inherited a newer slowed flag or body padding.");
                Deliver(104,2.94f,true);
                Require(!Slowed(),"Frost started before the rendered footprint entered its circle.");
                Deliver(106,2.74f,true);
                Require(Mathf.Abs(f.Session.Sample(f.Session.State.units[0]).x-3.12f)<.001f&&Slowed(),"The frost circle boundary did not include the rendered footprint.");
                Deliver(110,3.4f,false);
                Require(Slowed(),"A newer snapshot cleared frost before the rendered footprint left.");
                Deliver(114,4,false);
                Require(!Slowed(),"Frost remained after the rendered footprint left its circle.");
                Deliver(118,0,true);
                Deliver(122,0,true);
                Require(Slowed(),"A soldier could not reenter an active frost circle.");
                frost.seat=0;Deliver(126,0,true);Deliver(130,0,true);
                Require(!Slowed(),"Rendered frost affected the caster's soldier.");
                frost.seat=1;frost.impact=140;
                Deliver(134,0,true);Deliver(138,0,true);
                Require(!Slowed(),"Rendered frost began before its authoritative impact tick.");
                Deliver(144,0,true);Require(Slowed(),"Rendered frost did not begin at impact.");
                Deliver(150,0,true);
                Require(!Slowed(),"Rendered frost survived its authoritative end tick.");
            }
        }

        private static void StalledTimelineAndMatchReset()
        {
            using(var f=new Fixture())WithEffects((effects,impacts)=>
            {
                f.Deliver(f.State(100,"welcome",effects:new[]{Fireball(90,102,114)}));
                Advance(f.Session,f.Now+1);effects.Update(f.Session);
                Require(f.Session.RenderTick<=100&&impacts()==0,"A stalled snapshot stream extrapolated a spell impact past authority.");
                f.Error("connection_closed");
                double frozen=f.Session.RenderTick;
                Advance(f.Session,f.Now+.1);effects.Update(f.Session);
                Require(f.Session.RenderTick==frozen&&impacts()==0,"Effects advanced while the session was recovering.");
                f.Deliver(f.State(110,"welcome"));effects.Update(f.Session);
                Require(f.Session.SampleEffects().Count==0&&Visuals(effects).Count==0,"Resume retained pre-welcome effects.");
                f.Deliver(f.State(0,"welcome","new-match"));
                Require(f.Session.RenderTick<0&&f.Session.SampleEffects().Count==0,"Rematch did not reset the timeline to a fresh tick domain.");
                f.Deliver(f.State(10,match:"new-match",effects:new[]{Fireball(0,8,20)}));effects.Update(f.Session);
                var ended=f.State(12,match:"new-match");ended.phase="ended";f.Deliver(ended);effects.Update(f.Session);
                Require(Visuals(effects).Count==0,"Match end left buffered spell visuals active.");
            });
        }
    }
}
