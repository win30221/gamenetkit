#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle.Unity
{
    // Exercises the normal command and UI paths against an isolated real API.
    public sealed class BattleCombatVerification:MonoBehaviour
    {
        private BattleClient client;
        private GameFlow Flow=>client.Flow;
        private PlayerState Own{get{if(Flow.Battle.State.Latest==null)throw new Exception("RF_COMBAT_PLAYER FAIL lost session: "+Flow.Notice+" / "+Flow.Battle.Status+" / "+Flow.Battle.ValidationFailure);return Flow.Battle.State.Latest.players.First(p=>p.playerID==Flow.Battle.State.PlayerID);}}
        private PlayerState Enemy=>Flow.Battle.State.Latest.players.First(p=>p.teamID!=Own.teamID);
        private bool finished;
        private void Awake(){client=GetComponent<BattleClient>();Application.logMessageReceived+=Log;}
        private void Log(string message,string stack,LogType kind){if(!finished&&(kind==LogType.Exception||kind==LogType.Error)){finished=true;Application.Quit(1);}}
        private static void Require(bool value,string reason){if(!value)throw new Exception("RF_COMBAT_PLAYER FAIL "+reason);}
        private IEnumerator Until(Func<bool> condition,string stage){Debug.Log("RF_COMBAT_PLAYER STEP "+stage);double end=client.Now+25;while(!condition()){if(client.Now>end)throw new Exception("RF_COMBAT_PLAYER FAIL "+stage+": "+Flow?.Notice+" / "+Flow?.Battle.Status+" / "+Flow?.Battle.LastActionCode+" / "+Flow?.Battle.ValidationFailure);yield return null;}}
        private IEnumerator Practice(){Require(Flow.Practice("ranger","tank","idle"),"practice");yield return Until(()=>BattleHud.TouchCanPlay&&!Flow.Busy,"playable practice");yield return Until(()=>Enemy.protectedUntilTick<=Flow.Battle.State.Tick,"spawn protection");}
        private static void Pointer(VisualElement element,EventType kind,Vector2 position){var native=new Event{type=kind,mousePosition=position,button=0};
            if(kind==EventType.MouseDown){using var evt=PointerDownEvent.GetPooled(native);element.SendEvent(evt);}else if(kind==EventType.MouseUp){using var evt=PointerUpEvent.GetPooled(native);element.SendEvent(evt);}else{using var evt=PointerMoveEvent.GetPooled(native);element.SendEvent(evt);}}
        private IEnumerator Start(){
            yield return Until(()=>Flow?.Content!=null&&!Flow.Busy,"configuration");Require(Flow.Content.Map.arenaHalfSize==30,"large map config");
            Flow.Selection.Server.codec="delta";
            Require(Flow.Guest(),"guest");yield return Until(()=>Flow.Identity.IsAuthenticated&&!Flow.Busy,"guest reply");
            yield return Practice();
            client.MoveOrder(new Vector2(-10,5));
            yield return null;yield return null;
            Require(client.Presentation.DestinationVisible,"point movement has no final destination marker");
            yield return Until(()=>Own.x<-9.5&&Own.z>4.5,"point movement around cover");
            yield return Until(()=>client.Presentation.Camera.transform.position.x<-8,"camera follows sampled player");
            int hp=Enemy.hp;client.ArmAttackMove();Require(client.AttackMoveArmed,"A targeting not armed");client.AttackMoveOrder(new Vector2(8,0));
            yield return Until(()=>Own.attackReleaseTick-Flow.Battle.State.Tick>=3,"chase into range and wind up");
            Require(client.Orders.AttackMoving&&client.Orders.TargetID==Enemy.playerID&&client.Presentation.DestinationVisible,"attack-move failed to acquire enemy along path");
            long release=Own.attackReleaseTick;
            client.MoveOrder(new Vector2((float)Own.x-1,(float)Own.z+1));
            yield return Until(()=>Flow.Battle.State.Tick>release+2&&Own.attackReleaseTick==0,"move cancels windup");
            Require(!client.Orders.AttackMoving,"move retained attack-move");
            Require(Enemy.hp==hp,"cancelled windup dealt damage");
            client.AttackOrder(Enemy);yield return Until(()=>Enemy.hp<hp,"locked basic damage");
            long ready=Own.basicReadyTick;double oldZ=Own.z;
            client.MoveOrder(new Vector2((float)Own.x,(float)Own.z+1));
            yield return Until(()=>Own.z>oldZ+.25,"move cancels backswing");Require(Own.basicReadyTick==ready,"backswing reset cooldown");
            client.Orders.Clear();Require(client.SkillOrder(new Vector2((float)Enemy.x,(float)Enemy.z)),"skill input");
            yield return Until(()=>Flow.Battle.LastActionCode=="fired"&&Own.fireReadyTick>Flow.Battle.State.Tick,"independent direction skill");
            Require(Own.basicReadyTick==ready,"skill changed basic cooldown");
            yield return Until(()=>Own.attackStopUntilTick<=Flow.Battle.State.Tick,"skill recovery");
            client.MoveOrder(new Vector2((float)Own.x,(float)Own.z+12));double moveStart=Own.z;
            yield return Until(()=>Own.z>moveStart+.25,"move before backward dash");
            Require(client.DashOrder(new Vector2((float)Own.x,(float)Own.z-4)),"cursor direction dash");
            Require(!client.Orders.Moving&&client.Orders.TargetID==null,"dash retained previous move/attack order");
            yield return Until(()=>Own.dashEndTick>Flow.Battle.State.Tick&&Own.dashZ<-.9,"desktop backward dash direction");
            yield return Until(()=>Own.dashEndTick<=Flow.Battle.State.Tick,"dash ends");yield return new WaitForSeconds(.25f);double dashStop=Own.z;
            yield return new WaitForSeconds(.35f);Require(Math.Abs(Own.z-dashStop)<.08,"old point movement resumed after dash");
            Require(Flow.Battle.FrameAck>0,"delta snapshots not exercised");
            yield return Until(()=>!Flow.Busy,"leave ready");Require(Flow.Leave(),"leave");yield return Until(()=>Flow.Room==null&&!Flow.Busy,"left");
            var hud=GetComponent<BattleHud>();hud.VerificationTouches=Array.Empty<BattleTouchInput.Sample>();
            yield return Practice();yield return Until(()=>BattleHud.TouchMode,"touch mode");hud.VerificationTouches=null;yield return null;yield return null;
            var root=GetComponent<UIDocument>().rootVisualElement;var attack=root.Q<VisualElement>("touchAttack");
            yield return Until(()=>attack.resolvedStyle.display==DisplayStyle.Flex&&attack.worldBound.width>50,"basic touch layout");
            Require(!client.Presentation.AttackAimVisible&&!client.Presentation.TargetAimVisible,"idle retained attack preview");
            hp=Enemy.hp;Pointer(attack,EventType.MouseDown,attack.worldBound.center);yield return null;
            Require(client.PreviewKind==BattlePreviewKind.Basic&&!client.PreviewSelecting&&client.Presentation.AttackAimVisible&&!client.Presentation.TargetAimVisible,"basic press must show only range");
            Pointer(attack,EventType.MouseDrag,attack.worldBound.center+Vector2.right*40);yield return null;
            Require(client.PreviewSelecting&&client.Presentation.TargetAimVisible,"basic drag did not reveal selected target");
            Pointer(attack,EventType.MouseUp,attack.worldBound.center+Vector2.right*40);
            yield return Until(()=>Enemy.hp<hp,"tap basic button damages enemy");
            yield return new WaitForSeconds(.25f);
            Require(client.Presentation.TryActorFacing(Own.playerID,out var facing)&&facing.x>.9&&Own.facingX>.9,"basic attack facing was overwritten by idle skill aim");
            Require(!client.Presentation.AttackAimVisible,"released basic retained range preview");
            hp=Enemy.hp;
            var move=new BattleTouchInput.Sample(11,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,.25);
            var held=new BattleTouchInput.Sample(12,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,0);
            hud.VerificationTouches=new[]{new BattleTouchInput.Sample(11,BattleTouchInput.Target.Move,BattleTouchInput.Phase.Began,0,.25),new BattleTouchInput.Sample(12,BattleTouchInput.Target.Attack,BattleTouchInput.Phase.Began)};yield return null;
            hud.VerificationTouches=new[]{held,move};double start=Own.z;
            yield return new WaitForSeconds(1.4f);
            Require(Enemy.hp==hp,"held basic must not deal damage before release");
            hud.VerificationTouches=new[]{move,new BattleTouchInput.Sample(12,BattleTouchInput.Target.None,BattleTouchInput.Phase.Ended,0,0)};yield return null;
            hud.VerificationTouches=Array.Empty<BattleTouchInput.Sample>();yield return null;
            yield return Until(()=>Enemy.hp<hp,"released basic starts attack");
            Require(client.TouchInput.MovePointer<0&&client.TouchInput.AttackPointer<0&&Own.z>start+.2,"preview lost movement or release retained a finger");
            hud.VerificationTouches=new[]{new BattleTouchInput.Sample(11,BattleTouchInput.Target.Move,BattleTouchInput.Phase.Began,0,.25)};yield return null;
            hud.VerificationTouches=new[]{move};yield return null;
            yield return Until(()=>Own.attackReleaseTick==0&&Own.attackStopUntilTick<=Flow.Battle.State.Tick,"basic release recovery");
            hud.VerificationTouches=new[]{move,new BattleTouchInput.Sample(13,BattleTouchInput.Target.Aim,BattleTouchInput.Phase.Began,0,0)};yield return null;
            Require(client.PreviewKind==BattlePreviewKind.Skill&&!client.PreviewSelecting&&client.Presentation.AttackAimVisible&&!client.Presentation.TargetAimVisible,"skill press must show only range");
            hud.VerificationTouches=new[]{move,new BattleTouchInput.Sample(13,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,1,0)};yield return null;
            Require(client.PreviewSelecting&&client.Presentation.TargetAimVisible,"skill drag did not reveal direction");
            hud.VerificationTouches=new[]{move,new BattleTouchInput.Sample(13,BattleTouchInput.Target.None,BattleTouchInput.Phase.Ended,1,0)};yield return null;
            hud.VerificationTouches=new[]{move};yield return Until(()=>Own.fireReadyTick>Flow.Battle.State.Tick,"drag direction skill");
            Require(client.TouchInput.MovePointer==11,"skill lost movement");
            yield return Until(()=>Own.attackStopUntilTick<=Flow.Battle.State.Tick,"touch skill recovery");
            hud.VerificationTouches=new[]{move,new BattleTouchInput.Sample(14,BattleTouchInput.Target.Dash,BattleTouchInput.Phase.Began)};yield return null;hud.VerificationTouches=new[]{move};
            yield return Until(()=>Own.dashEndTick>Flow.Battle.State.Tick&&Own.dashZ>.9&&Math.Abs(Own.dashX)<.1,"touch dash follows movement");
            hud.VerificationTouches=Array.Empty<BattleTouchInput.Sample>();yield return null;hud.VerificationTouches=null;yield return null;
            Require(!client.TouchInput.AttackHeld&&client.TouchInput.MovePointer<0,"missing fingers retained controls");
            using(var escape=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None))root.SendEvent(escape);
            yield return Until(()=>BattleHud.BlocksGameplay,"menu reset");Require(!client.TouchInput.AttackHeld&&client.Orders.TargetID==null,"menu retained orders");
            using(var escape=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None))root.SendEvent(escape);
            yield return Until(()=>!BattleHud.BlocksGameplay,"menu resumed");
            yield return Until(()=>!Flow.Busy,"finish ready");Flow.Leave();yield return Until(()=>Flow.Room==null&&!Flow.Busy,"finished practice");
            Require(Flow.Practice("mage","tank","idle"),"mage practice");yield return Until(()=>BattleHud.TouchCanPlay&&!Flow.Busy,"mage playable");
            Require(client.SkillOrder(new Vector2((float)Own.x,(float)Own.z+100)),"out-of-range manual mage input");
            yield return Until(()=>(Flow.Battle.State.Latest.groundCasts?.Length??0)>0,"clamped mage cast");
            var cast=Flow.Battle.State.Latest.groundCasts[0];double range=Flow.Content.Hero("mage").castRange;
            Require(Math.Abs(Math.Sqrt(Math.Pow(cast.x-Own.x,2)+Math.Pow(cast.z-Own.z,2))-range)<.01,"mage cast did not land on outer edge");
            yield return Until(()=>!Flow.Busy,"mage leave ready");Flow.Leave();yield return Until(()=>Flow.Room==null&&!Flow.Busy,"mage left");
            finished=true;Debug.Log("RF_COMBAT_PLAYER PASS navigation/destination/camera, attack-move acquisition, basic facing and damage, windup/cooldown, backward dash cancels orders, contextual previews, independent skill/multitouch and clamped mage cast");Application.Quit(0);
        }
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
