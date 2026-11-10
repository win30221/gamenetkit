using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rumblebound.Gameplay;
using Rumblebound.Presentation;

namespace Rumblebound.Example.Battle.Unity
{
    // All movement is sampled by GameProjection/GameMovement. Views never move
    // physics bodies, decide hits, or write back into an authoritative state.
    public sealed partial class BattlePresentation:MonoBehaviour
    {
        private ArenaStage stage;
        private string stageMapHash;
        private GameContent content;
        private GameObject covers;
        private readonly Dictionary<string,MiniActorView> actors=new Dictionary<string,MiniActorView>();
        private readonly Dictionary<string,string> heroes=new Dictionary<string,string>();
        private readonly Dictionary<string,ActorView> actorSources=new Dictionary<string,ActorView>();
        private readonly HashSet<string> alive=new HashSet<string>();
        private readonly List<string> removedActors=new List<string>();
        private ActorView[] presentedRoster;
        private readonly Dictionary<string,LineRenderer> outlines=new Dictionary<string,LineRenderer>();
        private readonly Dictionary<long,LineRenderer> actionRings=new Dictionary<long,LineRenderer>();
        private readonly HashSet<long> pendingActions=new HashSet<long>();
        private readonly List<long> removedActions=new List<long>();
        private readonly CombatFeedbackTracker feedback=new CombatFeedbackTracker();
        private CombatEffects effects;
        private CombatAudio audioBus;
        private PointEffects point;
        private GroundCastPresentation casts;
        private ProjectilePresentation shots;
        private AttackRangePresentation attackRange;
        private LineRenderer castRange,castTarget;
        private Material friendly,enemy;
        private WorldView current;
        private BattleClient client;
        private string match;
        private uint generation;
        private long tick=-1;
        private double now;
        public Camera Camera=>stage?.Camera;
        public WorldView World=>current;
        public bool TryActorBounds(string id,out Bounds bounds){bounds=default;return actors.TryGetValue(id,out var actor)&&actor!=null&&actor.TryGetVisualBounds(out bounds);}
        public CombatAudio Audio=>audioBus;
        public int ActorCount=>actors.Count;
        public int CoverCount=>covers==null?0:covers.transform.childCount;
        public int ProjectileInstances=>shots?.AllocatedCount??0;
        public int GroundInstances=>casts?.ActiveCount??0;
        public bool AttackAimVisible=>attackRange?.Visible==true||castRange!=null&&castRange.gameObject.activeSelf||inputRange!=null&&inputRange.gameObject.activeSelf;
        public bool TargetAimVisible=>attackRange?.Visible==true||castTarget!=null&&castTarget.gameObject.activeSelf||targetRing!=null&&targetRing.gameObject.activeSelf||dashDirection!=null&&dashDirection.gameObject.activeSelf;
        public bool TryActorFacing(string id,out Vector3 facing){facing=Vector3.zero;if(!actors.TryGetValue(id,out var actor))return false;facing=actor.transform.forward;return true;}
        private void Start(){client=GetComponent<BattleClient>();cameraZoom=Mathf.Clamp(PlayerPrefs.GetFloat("Rumblebound.CameraZoom.v4",8),5,10);}
        public void Configure(GameContent definition)
        {
            if(ReferenceEquals(content,definition))return;content=definition;
            if(stage!=null&&stageMapHash!=definition.Config.mapHash){stage.Dispose();stage=new ArenaStage(transform,definition.Map);cameraValid=false;}
            stageMapHash=definition.Config.mapHash;
            if(stage==null){
                stage=new ArenaStage(transform,content.Map);effects=new CombatEffects(transform);audioBus=new CombatAudio(transform);point=new PointEffects(transform);casts=new GroundCastPresentation();
                friendly=ArenaStage.CreateMaterial(new Color(.3f,.85f,.95f));enemy=ArenaStage.CreateMaterial(new Color(1,.45f,.3f));
                shots=new ProjectilePresentation(CreateArrow,ProjectilePresentation.ArrowHeight,true);
                shots.ContactShown+=Contact;
                casts.ImpactShown+=impact=>{foreach(string id in impact.targets??Array.Empty<string>())if(actors.TryGetValue(id,out var actor))actor.FlashHit(now);};
            }
            stage.Camera.orthographicSize=cameraZoom;
            if(covers!=null)Destroy(covers);covers=new GameObject("Shared authoritative covers");covers.transform.SetParent(transform,false);
            foreach(var obstacle in content.Map.obstacles){
                var holder=new GameObject(obstacle.id);holder.transform.SetParent(covers.transform,false);
                ArenaStage.PlaceArt(holder.transform,"Arena/"+obstacle.visual,Vector3.zero,90);
                Bounds Bounds(){var renderers=holder.GetComponentsInChildren<Renderer>();if(renderers.Length==0)throw new InvalidOperationException("Missing cover art");var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
                var bounds=Bounds();holder.transform.localScale=new Vector3((float)(2*obstacle.halfX)/bounds.size.x,(float)obstacle.height/bounds.size.y,(float)(2*obstacle.halfZ)/bounds.size.z);
                bounds=Bounds();holder.transform.position+=new Vector3((float)obstacle.x-bounds.center.x,-bounds.min.y,(float)obstacle.z-bounds.center.z);
                if(holder.GetComponentsInChildren<Collider>().Length!=0)throw new InvalidOperationException("Cover art must not simulate collisions");
            }
        }
        public static Color TeamColor(string team)
        {
            if(team=="t2")return new Color(1,.55f,.36f);if(team=="t3")return new Color(.8f,.62f,1);if(team=="t4")return new Color(.88f,.9f,.34f);return new Color(.31f,.83f,1);
        }
        public void Present(WorldView world,Vector2 aim,bool showOutlines,double time)
        {
            current=world;now=time;
            if(stage==null)return;
            Rect viewport=BattleHud.ArenaPixels;
            stage.Camera.rect=new Rect(viewport.x/Screen.width,viewport.y/Screen.height,viewport.width/Screen.width,viewport.height/Screen.height);
            if(world.snapshot==null){ClearMatch();return;}
            if(match!=world.snapshot.matchID||generation!=world.generation){ClearMatch();match=world.snapshot.matchID;generation=world.generation;}
            if(!ReferenceEquals(presentedRoster,world.players))ReconcileActors(world.players);
            float playbackSpeed=client.Flow.Playback.Active?(client.Flow.Playback.Clock.Playing?(float)client.Flow.Playback.Clock.Speed:0):1;
            casts.SetPlaybackSpeed(playbackSpeed);point.SetPlaybackSpeed(playbackSpeed);
            foreach(var item in world.players){
                var p=item.state;var actor=actors[p.playerID];
                actor.SetPlaybackSpeed(playbackSpeed);
                PresentBasicAnimation(p,actor,item.own,world.tick);
                actor.Present(p,TeamColor(p.teamID),true,world.tick,content.Rules.tickRate,item.own&&WasPreviewed(p.fireActionID),time);
                actor.RenderPose(new Vector3((float)item.x,0,(float)item.z),item.own?(Vector2?)(client.FacingPoint-new Vector2((float)item.x,(float)item.z)):null,time);
                var outline=outlines[p.playerID];
                outline.gameObject.SetActive(showOutlines&&p.hp>0&&!p.left);GroundCastPresentation.PlaceCircle(outline,new Vector3((float)p.x,.05f,(float)p.z),(float)content.Rules.playerRadius);
            }
            var self=world.players.FirstOrDefault(p=>p.own)?.state;
            PresentAim(world,aim);
            pendingActions.Clear();foreach(var cue in world.actions){if(cue.kind!="ground"&&cue.kind!="dash"||cue.status!="pending")continue;pendingActions.Add(cue.id);if(!actionRings.TryGetValue(cue.id,out var ring)){ring=GroundCastPresentation.Circle("Pending action "+cue.id,new Color(1,.8f,.3f,.5f),.025f);ring.transform.SetParent(transform,false);actionRings[cue.id]=ring;}GroundCastPresentation.PlaceCircle(ring,new Vector3((float)cue.x,.07f,(float)cue.z),(float)cue.radius);}
            removedActions.Clear();foreach(long id in actionRings.Keys)if(!pendingActions.Contains(id))removedActions.Add(id);
            foreach(long id in removedActions){DestroyLine(actionRings[id]);actionRings.Remove(id);}
            if(tick!=world.tick&&!world.frozen){
                tick=world.tick;feedback.Accept(world.snapshot,time,content.Rules.tickRate,content.Rules);
                foreach(var p in feedback.Shots)if(p.playerID!=world.playerID||!WasPreviewed(p.fireActionID))Play("shot",p.playerID,p.x,p.z,p.facingX,p.facingZ);
                foreach(var e in feedback.MeleeEvents){
                    if(e.kind=="basic_swing")continue;
                    if(e.kind=="basic_hit"){PresentBasicHit(e);continue;}
                    if(e.kind=="melee_swing"&&e.ownerID==world.playerID&&WasPreviewed(e.actionID))continue;
                    Play(e.kind=="melee_hit"||e.kind=="basic_hit"?"player":e.kind=="basic_swing"?"shot":e.kind,e.ownerID,e.x,e.z,e.dx,e.dz);
                    if((e.kind=="melee_hit"||e.kind=="basic_hit")&&e.targetID!=null&&actors.TryGetValue(e.targetID,out var hit))hit.FlashHit(time);
                }
                shots.Accept(world.snapshot.projectiles,self?.teamID,content.Rules.maxProjectiles,friendly,enemy,world.snapshot.projectileEnds,content.Rules.projectileRadius);
                casts.Accept(world.snapshot,content.Rules.tickRate,content.Rules.maxActiveGroundCasts,self?.teamID,time);
            }
            FollowCamera(world,client.FacingPoint);
            PresentLock(world);
            PresentDestination(world);
            RenderBasicTraces();
            RenderFire();
            var state=world.snapshot.match;
            if(state!=null)point.Present(state.point,state.pointStatus,TeamColor(state.scoringTeamID),(float)content.Rules.controlPointRadius,state.phase=="ended",state.pointStatus=="scoring");
            if(!world.frozen){shots.Render(client.Flow.Playback.Active?client.Flow.Playback.Movement:client.Flow.Battle.Movement);casts.Render(time);point.Tick();effects.Render(time);}
        }
        private bool WasPreviewed(long id)=>client?.Flow!=null&&!client.Flow.Playback.Active&&client.Flow.Battle.WasActionPresented(id);
        private void RemoveActor(string id)
        {
            Destroy(actors[id].gameObject);actors.Remove(id);heroes.Remove(id);actorSources.Remove(id);basicAnimations.Remove(id);
            if(outlines.TryGetValue(id,out var line)){DestroyLine(line);outlines.Remove(id);}
        }
        private void ReconcileActors(ActorView[] players)
        {
            alive.Clear();
            foreach(var item in players){
                var p=item.state;alive.Add(p.playerID);
                if(actorSources.TryGetValue(p.playerID,out var previous)&&(previous.state.entityID!=p.entityID||previous.state.life!=p.life||previous.state.heroID!=p.heroID||item.incarnation!=0&&previous.incarnation!=item.incarnation))RemoveActor(p.playerID);
                if(!actors.ContainsKey(p.playerID)){
                    actors[p.playerID]=MiniActorView.Create(p,transform);heroes[p.playerID]=p.heroID;
                    var outline=GroundCastPresentation.Circle("Authority "+p.playerID,item.own?Color.blue:Color.gray,.025f);outline.transform.SetParent(transform,false);outlines[p.playerID]=outline;
                }
                actorSources[p.playerID]=item;
            }
            removedActors.Clear();foreach(string id in actors.Keys)if(!alive.Contains(id))removedActors.Add(id);
            foreach(string id in removedActors)RemoveActor(id);
            presentedRoster=players;
        }
        private void Play(string kind,string owner,double x,double z,double dx,double dz,long speculative=0)
        {
            var position=actors.TryGetValue(owner??"",out var actor)?actor.transform.position:new Vector3((float)x,0,(float)z);position.y=ProjectilePresentation.ArrowHeight;
            var hero=current?.players.FirstOrDefault(p=>p.state.playerID==owner)?.state.heroID;
            var rules=content.Hero(hero);var direction=new Vector2((float)dx,(float)dz);
            effects.Play(kind,position,direction,now,(float)(rules?.meleeRange??1.25),(float)(rules?.meleeArcDegrees??120),speculative);
            audioBus.Play(kind,position,owner==current?.playerID,now,speculative);
        }
        private void Contact(ProjectileEnd end)
        {
            if(!feedback.CanPresent(end))return;
            FireImpact(end);
            var position=new Vector3((float)end.x,ProjectilePresentation.ArrowHeight,(float)end.z);
            effects.Play(end.kind,position,Vector2.right,now);audioBus.Play(end.kind,position,end.ownerID==current?.playerID||end.targetID==current?.playerID,now);
            if(end.targetID!=null&&actors.TryGetValue(end.targetID,out var hit))hit.FlashHit(now);
        }
        public void Preview(long id,string kind,double dx,double dz,bool ground,double targetX,double targetZ)
        {
            if(current==null||content==null)return;
            if((kind=="fire"||kind=="skill"||kind=="attack")&&actors.TryGetValue(current.playerID,out var actor)){actor.PredictAttack();if(kind=="attack")return;var hero=current.players.First(p=>p.own).state.heroID;Play(content.Hero(hero)?.attackKind=="melee"?"melee_swing":"shot",current.playerID,0,0,dx,dz,id);}
        }
        public void Resolve(long id,bool accepted)
        {
            effects?.ResolveSpeculative(id,accepted);audioBus?.ResolveSpeculative(id,accepted);
            if(!accepted&&current!=null&&actors.TryGetValue(current.playerID,out var actor))actor.RejectPredictedAttack();
        }
        private static void DestroyLine(LineRenderer line){if(line==null)return;Destroy(line.sharedMaterial);Destroy(line.gameObject);}
        private void ClearMatch()
        {
            ClearFire();
            ClearBasics();
            if(destinationMarker!=null)destinationMarker.gameObject.SetActive(false);
            HideAim();
            foreach(var actor in actors.Values)Destroy(actor.gameObject);actors.Clear();heroes.Clear();actorSources.Clear();alive.Clear();removedActors.Clear();presentedRoster=null;foreach(var line in outlines.Values)DestroyLine(line);outlines.Clear();
            pendingActions.Clear();removedActions.Clear();feedback.Clear();shots?.Clear();casts?.Clear();point?.Clear();effects?.Clear();audioBus?.Clear();tick=-1;match=null;generation=0;
            foreach(var line in actionRings.Values)DestroyLine(line);actionRings.Clear();
        }
        private void OnDestroy(){ClearMatch();DisposeFire();DisposeBasics();DestroyLine(destinationMarker);attackRange?.Dispose();DestroyLine(castRange);DestroyLine(castTarget);DestroyLine(targetRing);DestroyLine(inputRange);DestroyLine(dashDirection);shots?.Dispose();casts?.Dispose();point?.Dispose();effects?.Dispose();audioBus?.Dispose();stage?.Dispose();if(friendly!=null)Destroy(friendly);if(enemy!=null)Destroy(enemy);}
    }
}
