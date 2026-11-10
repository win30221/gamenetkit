using System;
using UnityEngine;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleClient
    {
        public BattleOrders Orders{get;}=new BattleOrders();
        public string CurrentTargetID{get;private set;}
        private Vector2 lastMoveDirection=Vector2.up;
        private int previousMovePointer=-1,previousAttackPointer=-1;
        private string controlMatch;
        private uint controlGeneration;
        private double nextMouseOrder;
        private Vector2 facingDirection=Vector2.up;
        private string attackFacingID;
        private long attackFacingLife;
        private double attackFacingUntil,castFacingUntil;
        private bool skillHeld,dashHeld;
        private bool attackMoveArmed;
        public bool AttackMoveArmed=>attackMoveArmed;
        private Vector2 skillMouseStart,dashMouseStart,basicMouseStart;
        private BattlePreviewKind previewKind;
        private bool previewSelecting;
        private bool releasedBasicPending;
        private double releasedBasicUntil;
        public Vector2 FacingPoint{get;private set;}
        public BattlePreviewKind PreviewKind{get{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(ReviewPreview.HasValue)return ReviewPreview.Value;
#endif
            return previewKind;}}
        public bool PreviewSelecting{get{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(ReviewPreview.HasValue)return ReviewSelecting;
#endif
            return previewSelecting;}}
        private PlayerState Own=>Flow?.Battle.State.Latest==null?null:Array.Find(Flow.Battle.State.Latest.players,p=>p.playerID==Flow.Battle.State.PlayerID);
        public void MoveOrder(Vector2 destination){Orders.Move(destination.x,destination.y);attackMoveArmed=false;releasedBasicPending=false;attackFacingID=null;attackFacingUntil=castFacingUntil=0;CancelAttack();}
        public void ArmAttackMove(){if(Flow.Battle.CanControl&&!BattleHud.BlocksGameplay)attackMoveArmed=true;}
        public void AttackMoveOrder(Vector2 destination){MoveOrder(destination);Orders.AttackMove(destination.x,destination.y);}
        public void AttackOrder(PlayerState target){attackMoveArmed=false;Orders.Attack(target,true);CancelAttack();}
        public bool SkillOrder(Vector2 point){
            var own=Own;if(own==null||own.fireReadyTick>Flow.Battle.State.Tick)return false;
            var hero=Flow.Content.Hero(own.heroID);
            if(hero?.attackKind=="ground"){
                BattleOrders.Position(own,true,Flow.Battle.Movement,out double x,out double z);
                if(!BattleAimAssist.GroundPoint(Flow.Content,hero,x,z,point.x,point.y,out double px,out double pz))return false;
                point=new Vector2((float)px,(float)pz);
            }
            bool queued=Flow.AimAction("skill",point.x,point.y,Now);if(queued)FaceCast(point);return queued;
        }
        public bool DashOrder(Vector2 point){
            var own=Own;if(own==null||own.dashReadyTick>Flow.Battle.State.Tick||own.attackStopUntilTick>Flow.Battle.State.Tick&&own.attackReleaseTick==0)return false;
            if(!Flow.AimAction("dash",point.x,point.y,Now))return false;
            Orders.Clear();attackMoveArmed=false;releasedBasicPending=false;CurrentTargetID=null;attackFacingID=null;FaceCast(point);return true;
        }
        private void FaceCast(Vector2 point){var own=Own;if(own==null)return;BattleOrders.Position(own,true,Flow.Battle.Movement,out double x,out double z);var direction=point-new Vector2((float)x,(float)z);if(direction.sqrMagnitude>.001f)facingDirection=direction.normalized;castFacingUntil=Now+.35;}
        private void UpdateFacing(Vector2 drive){
            var own=Own;if(own==null)return;var battle=Flow.Battle;
            BattleOrders.Position(own,true,battle.Movement,out double x,out double z);
            if(Now>=castFacingUntil){
                string targetID=own.attackReleaseTick>battle.State.Tick?own.attackTargetID:Now<attackFacingUntil?attackFacingID:null;
                long life=own.attackReleaseTick>battle.State.Tick?own.attackTargetLife:attackFacingLife;
                var target=Array.Find(battle.State.Latest.players,p=>p.playerID==targetID&&p.life==life&&p.hp>0&&!p.left);
                if(target!=null){BattleOrders.Position(target,false,battle.Movement,out double tx,out double tz);var direction=new Vector2((float)(tx-x),(float)(tz-z));if(direction.sqrMagnitude>.001f)facingDirection=direction.normalized;}
                else if(PreviewKind==BattlePreviewKind.Skill&&PreviewSelecting){var direction=Aim-new Vector2((float)x,(float)z);if(direction.sqrMagnitude>.001f)facingDirection=direction.normalized;}
                else if(drive.sqrMagnitude>.001f)facingDirection=drive.normalized;
            }
            FacingPoint=new Vector2((float)x,(float)z)+facingDirection;
        }
        private void CancelAttack(){if(Flow.Battle.CanControl&&(Own?.attackReleaseTick>Flow.Battle.State.Tick||Flow.Battle.HasPendingAction))Flow.Battle.QueueAction("cancel",0,1,Now);}
        private bool BasicAttack(PlayerState target){
            var own=Own;if(!BattleOrders.Enemy(own,target,Flow.Battle.State.Tick))return false;CurrentTargetID=target.playerID;
            attackFacingID=target.playerID;attackFacingLife=target.life;attackFacingUntil=Now+.35;
            if(own.basicReadyTick>Flow.Battle.State.Tick||own.attackReleaseTick>0||own.attackStopUntilTick>Flow.Battle.State.Tick||Flow.Battle.HasPendingAction)return false;
            BattleOrders.Position(own,true,Flow.Battle.Movement,out double x,out double z);
            BattleOrders.Position(target,false,Flow.Battle.Movement,out double tx,out double tz);
            return Flow.Battle.QueueAction("attack",tx-x,tz-z,Now,false,0,0,target.playerID,target.life);
        }
        private Vector2 ReadControls()
        {
            if(Verification)return Vector2.zero;
            if(!focused||!Flow.Battle.CanControl||BattleHud.BlocksGameplay){TouchInput.Reset();Orders.Clear();attackMoveArmed=false;releasedBasicPending=false;CurrentTargetID=null;previewKind=BattlePreviewKind.None;previewSelecting=skillHeld=dashHeld=false;previousMovePointer=previousAttackPointer=-1;return Vector2.zero;}
            var state=Flow.Battle.State.Latest;var own=Own;
            if(own==null||own.hp<=0||own.left){TouchInput.Reset();Orders.Clear();CurrentTargetID=null;return Vector2.zero;}
            if(controlMatch!=state.matchID||controlGeneration!=state.generation){Orders.Clear();lastMoveDirection=facingDirection=Vector2.up;attackFacingID=null;attackFacingUntil=castFacingUntil=0;controlMatch=state.matchID;controlGeneration=state.generation;}
            return BattleHud.TouchMode?ReadTouchControls(own):ReadMouseControls(own);
        }
        private Vector2 ReadTouchControls(PlayerState own)
        {
            var battle=Flow.Battle;var state=battle.State.Latest;
            previewKind=TouchInput.AimPointer>=0?BattlePreviewKind.Skill:TouchInput.AttackHeld?BattlePreviewKind.Basic:TouchInput.DashPointer>=0?BattlePreviewKind.Dash:BattlePreviewKind.None;
            previewSelecting=previewKind==BattlePreviewKind.Skill?TouchInput.AimDragged:previewKind==BattlePreviewKind.Basic&&TouchInput.AttackDragged;
            var drive=new Vector2((float)TouchInput.MoveX,(float)TouchInput.MoveZ);
            if(drive.sqrMagnitude>.001f)lastMoveDirection=drive.normalized;
            if(TouchInput.MovePointer>=0&&previousMovePointer<0)CancelAttack();
            previousMovePointer=TouchInput.MovePointer;
            TouchInput.FollowMovement();
            BattleOrders.Position(own,true,battle.Movement,out double x,out double z);
            var hero=Flow.Content.Hero(own.heroID);
            double range=hero.attackKind=="ground"?hero.castRange*TouchInput.AimMagnitude:6;
            Aim=new Vector2((float)(x+TouchInput.AimX*range),(float)(z+TouchInput.AimZ*range));
            if(TouchInput.TakeFire(out bool automatic)){
                bool cast=!automatic||hero.attackKind!="ground";
                if(automatic&&BattleAimAssist.TryTarget(Flow.Content,state,battle.State.PlayerID,battle.Movement,out _,out double tx,out double tz)){
                    double dx=tx-x,dz=tz-z;TouchInput.SetAim(dx,dz,hero.attackKind=="ground"?Math.Sqrt(dx*dx+dz*dz)/hero.castRange:1);Aim=new Vector2((float)tx,(float)tz);
                    cast=true;
                }
                if(cast)SkillOrder(Aim);
            }
            bool tapped=TouchInput.TakeAttack();
            if(tapped){releasedBasicPending=true;releasedBasicUntil=Now+.25;}
            if(Now>releasedBasicUntil)releasedBasicPending=false;
            if(!TouchInput.AttackHeld&&!tapped&&Orders.TargetID==null)CurrentTargetID=null;
            if(TouchInput.AttackPointer>=0&&previousAttackPointer<0)Orders.Clear();
            previousAttackPointer=TouchInput.AttackPointer;
            if(TouchInput.AttackHeld||releasedBasicPending){
                PlayerState target;
                if(TouchInput.AttackDragged){
                    target=BattleOrders.Nearest(Flow.Content,state,battle.State.PlayerID,battle.Movement,TouchInput.AttackX,TouchInput.AttackZ);
                    if(target?.playerID!=Orders.TargetID){if(target?.playerID!=CurrentTargetID)CancelAttack();Orders.Attack(target,false);}
                }else{
                    target=Orders.Drive(Flow.Content,state,battle.State.PlayerID,battle.Movement,Now,out _,out _);
                    if(target==null)target=BattleOrders.Nearest(Flow.Content,state,battle.State.PlayerID,battle.Movement);
                }
                CurrentTargetID=target?.playerID;
                if(target!=null){
                    if(TouchInput.AimPointer<0){BattleOrders.Position(target,false,battle.Movement,out double tx,out double tz);Aim=new Vector2((float)tx,(float)tz);}
                    if(BasicAttack(target)||own.attackReleaseTick>0&&own.attackTargetID==target.playerID&&!battle.HasPendingAction)releasedBasicPending=false;
                }
            }
            if(TouchInput.TakeDash())DashOrder(new Vector2((float)x+lastMoveDirection.x,(float)z+lastMoveDirection.y));
            return drive;
        }
        private Vector2 ReadMouseControls(PlayerState own)
        {
            var battle=Flow.Battle;var state=battle.State.Latest;
            if(Input.GetKeyDown(KeyCode.A))ArmAttackMove();
            bool overWorld=!BattleHud.PointerOverPanel&&presentation.Camera!=null&&presentation.Camera.pixelRect.Contains(Input.mousePosition);
            if(overWorld){
                var ray=presentation.Camera.ScreenPointToRay(Input.mousePosition);
                if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance)){var point=ray.GetPoint(distance);Aim=new Vector2(point.x,point.z);}
                if(attackMoveArmed&&Input.GetMouseButtonDown(0))AttackMoveOrder(Aim);
                if(Input.GetMouseButton(1)&&Now>=nextMouseOrder){
                    nextMouseOrder=Now+.1;var enemy=PickEnemy();
                    if(enemy!=null){if(Orders.TargetID!=enemy.playerID||Orders.TargetLife!=enemy.life)AttackOrder(enemy);}
                    else MoveOrder(Aim);
                }
            }
            bool q=Input.GetKey(KeyCode.Q),e=Input.GetKey(KeyCode.E)||Input.GetKey(KeyCode.LeftShift);
            if(q&&!skillHeld)skillMouseStart=Input.mousePosition;
            if(e&&!dashHeld)dashMouseStart=Input.mousePosition;
            if(Input.GetMouseButtonDown(1))basicMouseStart=Input.mousePosition;
            if(!q&&skillHeld)SkillOrder(Aim);
            if(!e&&dashHeld)DashOrder(Aim);
            skillHeld=q;dashHeld=e;
            previewKind=q?BattlePreviewKind.Skill:e?BattlePreviewKind.Dash:attackMoveArmed||Input.GetMouseButton(1)?BattlePreviewKind.Basic:BattlePreviewKind.None;
            var start=q?skillMouseStart:e?dashMouseStart:basicMouseStart;
            previewSelecting=((Vector2)Input.mousePosition-start).sqrMagnitude>64||previewKind==BattlePreviewKind.Basic&&Orders.TargetID!=null;
            if(Input.GetKeyDown(KeyCode.S)||Input.GetKeyDown(KeyCode.Escape)){Orders.Clear();attackMoveArmed=false;CancelAttack();}
            var target=Orders.Drive(Flow.Content,state,battle.State.PlayerID,battle.Movement,Now,out double dx,out double dz);
            CurrentTargetID=Orders.TargetID;if(target!=null)BasicAttack(target);
            var drive=new Vector2((float)dx,(float)dz);
            if(drive.sqrMagnitude>.001f)lastMoveDirection=drive.normalized;
            return drive;
        }
        private PlayerState PickEnemy(){
            PlayerState selected=null;float nearest=28*28;var own=Own;
            foreach(var p in Flow.Battle.State.Latest.players){
                if(!BattleOrders.Enemy(own,p,long.MaxValue))continue;
                BattleOrders.Position(p,false,Flow.Battle.Movement,out double x,out double z);
                var point=presentation.Camera.WorldToScreenPoint(new Vector3((float)x,.6f,(float)z));
                float distance=((Vector2)point-(Vector2)Input.mousePosition).sqrMagnitude;
                if(point.z>0&&distance<nearest){nearest=distance;selected=p;}
            }return selected;
        }
    }
}
