#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    // Explicit test flag; routes UI pointer events through the normal client,
    // prediction and server input path rather than overriding its movement.
    public sealed class BattleTouchVerification:MonoBehaviour
    {
        private BattleClient client;
        private GameFlow Flow=>client.Flow;
        private bool finished;
        private void Awake(){client=GetComponent<BattleClient>();Application.logMessageReceived+=Log;}
        private void Log(string message,string stack,LogType type){if(!finished&&(type==LogType.Exception||type==LogType.Error)){finished=true;Application.Quit(1);}}
        private IEnumerator Until(Func<bool> condition,string name)
        {
            Debug.Log("RF_TOUCH_PLAYER STEP "+name);
            double end=client.Now+25;
            while(!condition()){if(client.Now>end)throw new Exception("RF_TOUCH_PLAYER FAIL "+name+": "+Flow?.Notice+" "+Flow?.Battle.Status);yield return null;}
        }
        private Rumblebound.Gameplay.PlayerState Own{
            get{
                if(Flow.Battle.State.Latest==null)throw new Exception("RF_TOUCH_PLAYER FAIL lost session: "+Flow.Notice+" / "+Flow.Battle.Status+" / "+Flow.Battle.ValidationFailure);
                return Flow.Battle.State.Latest.players.First(p=>p.playerID==Flow.Battle.State.PlayerID);
            }
        }
        private static void Require(bool value,string reason){if(!value)throw new Exception("RF_TOUCH_PLAYER FAIL "+reason);}
        private static void Pointer(VisualElement pad,EventType type,Vector2 position)
        {
            var native=new Event{type=type,mousePosition=position,button=0};
            if(type==EventType.MouseDown){using var evt=PointerDownEvent.GetPooled(native);pad.SendEvent(evt);}
            else if(type==EventType.MouseUp){using var evt=PointerUpEvent.GetPooled(native);pad.SendEvent(evt);}
            else{using var evt=PointerMoveEvent.GetPooled(native);pad.SendEvent(evt);}
        }
        private IEnumerator Start()
        {
            yield return Until(()=>Flow?.Content!=null&&!Flow.Busy,"configuration");
            Require(Flow.Guest(),"guest start");yield return Until(()=>Flow.Identity.IsAuthenticated&&!Flow.Busy,"guest reply");
            Require(Flow.Practice("ranger","tank","idle"),"practice start");
            yield return Until(()=>BattleHud.TouchCanPlay&&!Flow.Busy,"playable");
            var root=GetComponent<UIDocument>().rootVisualElement;
            var move=root.Q<VisualElement>("touchMove");var aim=root.Q<VisualElement>("touchAim");
            yield return Until(()=>move.worldBound.width>50&&move.resolvedStyle.display==DisplayStyle.Flex,"touch layout");
            yield return Until(()=>Flow.Battle.State.Latest.players.All(p=>p.protectedUntilTick<=Flow.Battle.State.Tick),"spawn protection expires");
            var enemy=Flow.Battle.State.Latest.players.First(p=>p.teamID!=Own.teamID);
            string enemyID=enemy.playerID;int health=enemy.hp;
            // Initial facing is north; the idle enemy is east. A centre tap has
            // no manual direction and must choose the enemy through normal input.
            Require(client.TouchInput.AimZ>.9,"initial aim for automatic targeting test");
            Pointer(aim,EventType.MouseDown,aim.worldBound.center);Pointer(aim,EventType.MouseUp,aim.worldBound.center);
            yield return Until(()=>Flow.Battle.State.Latest.players.First(p=>p.playerID==enemyID).hp<health,"UI tap automatically aims and damages enemy");
            Require(client.TouchInput.AimX>.9,"tap did not preserve selected aim direction");
            yield return Until(()=>Own.fireReadyTick<=Flow.Battle.State.Tick,"tap cooldown");
            double start=Own.z;Vector2 center=move.worldBound.center;
            center+=new Vector2(18,12);
            Pointer(move,EventType.MouseDown,center);
            Require(client.TouchInput.MoveX==0&&client.TouchInput.MoveZ==0,"off-centre contact must start neutral");
            Pointer(move,EventType.MouseDrag,center+Vector2.down*8);
            Require(Math.Abs(client.TouchInput.MoveZ-1)<1e-6,"short drag must use fixed full speed");
            Pointer(move,EventType.MouseDrag,center+Vector2.down*45);
            Require(client.TouchInput.MoveZ>.5,"UI pointer did not set movement");
            yield return Until(()=>Own.z>start+.45,"authoritative touch movement");
            Pointer(move,EventType.MouseUp,center+Vector2.down*45);
            Require(client.TouchInput.MovePointer<0&&client.TouchInput.MoveZ==0,"release did not stop");
            yield return new WaitForSeconds(.6f);double stopped=Own.z;
            yield return new WaitForSeconds(.35f);Require(Math.Abs(Own.z-stopped)<.08,"released movement remained held");
            long confirmations=Flow.Battle.ConfirmedActions;
            center=aim.worldBound.center;Pointer(aim,EventType.MouseDown,center);Pointer(aim,EventType.MouseDrag,center+Vector2.right*45);Pointer(aim,EventType.MouseUp,center+Vector2.right*45);
            yield return Until(()=>Flow.Battle.ConfirmedActions>confirmations&&Flow.Battle.LastActionCode=="fired","release attack confirmation");
            yield return Until(()=>Own.attackStopUntilTick<=Flow.Battle.State.Tick,"attack recovery before dash");
            var dash=root.Q<VisualElement>("touchDash");Pointer(dash,EventType.MouseDown,dash.worldBound.center);
            Pointer(dash,EventType.MouseUp,dash.worldBound.center);
            yield return Until(()=>Flow.Battle.LastActionCode=="dashed","dash confirmation");
            yield return Until(()=>Own.dashEndTick<=Flow.Battle.State.Tick,"dash recovery");
            var hud=GetComponent<BattleHud>();
            // Walk parallel to the nearby cover, away from walls; this tests
            // retained input rather than the server's collision constraints.
            var held=new BattleTouchInput.Sample(7,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,.5);
            hud.VerificationTouches=new[]{new BattleTouchInput.Sample(7,BattleTouchInput.Target.Move,BattleTouchInput.Phase.Began,0,.5)};
            yield return null;
            hud.VerificationTouches=new[]{held};
            yield return Until(()=>client.TouchInput.MovePointer==7,"raw movement finger");
            for(int i=0;i<3;i++){
                // Reverse between attacks so the retained finger does not walk
                // into the large arena's north ridge during a cooldown.
                double direction=i%2==0?.5:-.5;
                held=new BattleTouchInput.Sample(7,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,direction);
                hud.VerificationTouches=new[]{held};
                yield return Until(()=>{
                    if(Math.Abs(Own.z)>5)direction=Own.z>0?-.5:.5;
                    held=new BattleTouchInput.Sample(7,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,direction);
                    hud.VerificationTouches=new[]{held};
                    return Own.fireReadyTick<=Flow.Battle.State.Tick;
                },"held move attack cooldown");
                hud.VerificationTouches=new[]{new BattleTouchInput.Sample(19,BattleTouchInput.Target.Aim,BattleTouchInput.Phase.Began,0,1),held};yield return null;
                hud.VerificationTouches=new[]{held,new BattleTouchInput.Sample(19,BattleTouchInput.Target.None,BattleTouchInput.Phase.Ended,0,1)};yield return null;
                hud.VerificationTouches=new[]{held};
                yield return Until(()=>Own.attackStopUntilTick>Flow.Battle.State.Tick,"held move attack accepted");
                yield return Until(()=>Own.attackStopUntilTick<=Flow.Battle.State.Tick,"held move attack recovery");
                double before=Own.z;
                yield return Until(()=>direction>0?Own.z>before+.15:Own.z<before-.15,"walking resumes without lifting finger");
                Require(client.TouchInput.MovePointer==7&&Math.Abs(client.TouchInput.MoveZ)>.4,"attack lost stationary left finger");
            }
            // Unrelated pointer capture loss is irrelevant to a physical touch.
            move.CapturePointer(7);yield return null;
            aim.CapturePointer(7);yield return null;
            aim.ReleasePointer(7);
            yield return null;Require(client.TouchInput.MovePointer==7,"UI capture loss cleared physical touch");
            hud.VerificationTouches=Array.Empty<BattleTouchInput.Sample>();yield return null;
            hud.VerificationTouches=null;yield return null;
            center=move.worldBound.center;Pointer(move,EventType.MouseDown,center+Vector2.down*45);
            using(var escape=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None))root.SendEvent(escape);
            yield return Until(()=>BattleHud.BlocksGameplay,"menu open");yield return null;
            Require(client.TouchInput.MovePointer<0&&client.TouchInput.MoveZ==0,"menu retained touch capture");
            Require(root.Q<VisualElement>("touchControls").resolvedStyle.display==DisplayStyle.None,"menu retained touch overlay");
            yield return Until(()=>!Flow.Busy,"leave readiness");Require(Flow.Leave(),"leave");yield return Until(()=>Flow.Room==null&&!Flow.Busy,"leave reply");
            finished=true;Debug.Log("RF_TOUCH_PLAYER PASS UI tap auto-aim damages enemy, manual drag attack, sampled multi-finger movement/release/dash, sustained movement across attacks/capture loss and menu reset through normal Unity client/server");Application.Quit(0);
        }
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
