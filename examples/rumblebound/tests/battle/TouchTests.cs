using System;
using Rumblebound.Example.Battle;

public static class TouchTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Touch input: "+reason);}
    public static void Run()
    {
        var input=new BattleTouchInput();
        Require(input.Begin(false,11)&&input.Begin(true,12),"independent fingers must own both sticks");
        Require(!input.Begin(false,13)&&!input.Begin(true,11),"a second finger cannot steal a captured stick");
        input.Drag(false,11,1,1);Require(Math.Abs(Math.Sqrt(input.MoveX*input.MoveX+input.MoveZ*input.MoveZ)-1)<1e-9,"diagonal speed is bounded");
        input.Drag(false,13,-1,0);Require(input.MoveX>0&&input.MoveZ>0,"unowned movement ignored");
        input.Drag(true,12,-1,0);input.End(false,11);
        Require(input.MoveX==0&&input.MoveZ==0&&input.AimPointer==12,"movement release preserves aiming finger");
        input.End(true,12,true);Require(!input.TakeFire(),"cancelled aim must never attack");
        input.Begin(true,14);input.Drag(true,14,0,1);input.End(true,14);input.End(true,14);
        Require(input.TakeFire()&&!input.TakeFire(),"aim release emits exactly one attack");
        input.Begin(false,15);input.Drag(false,15,.05,0);Require(input.MoveX==0,"radial deadzone");
        input.Drag(false,15,.56,0);Require(Math.Abs(input.MoveX-.5)<1e-9,"analog movement scales after deadzone");
        input.Drag(false,15,double.NaN,0);Require(Math.Abs(input.MoveX-.5)<1e-9,"invalid samples do not poison movement");
        input.Dash();input.Reset();Require(input.MovePointer<0&&input.AimPointer<0&&input.MoveX==0&&!input.TakeFire()&&!input.TakeDash(),"menu/background reset clears captures and queued actions");
        input.End(true,14);Require(!input.TakeFire(),"late release after reset cannot fire");
        TapAndDrag();
        BasicAttackPad();
        RawMultitouch();
        Console.WriteLine("REMAKE_TOUCH PASS multitouch ownership, analog deadzone, bounded diagonals, release/cancel, one-shot attack, and background/menu reset");
    }
    private static void TapAndDrag()
    {
        var input=new BattleTouchInput();
        input.Begin(true,1);input.Drag(true,1,.8,-.5);input.Drag(true,1,.84,-.53);input.End(true,1);
        Require(input.TakeFire(out bool automatic)&&automatic,"off-centre tap with jitter must auto aim");
        Require(!input.TakeFire(out automatic)&&!automatic,"auto aim fires only once");
        input.Begin(true,2);input.Drag(true,2,0,0);input.Drag(true,2,.7,0);input.Drag(true,2,0,0);input.End(true,2);
        Require(input.TakeFire(out automatic)&&!automatic,"dragging back to centre must remain manually aimed");
        input.Begin(true,3);input.Drag(true,3,0,0);input.End(true,3);
        Require(input.TakeFire(out automatic)&&automatic,"next tap must forget previous drag");
        input.Begin(true,4);input.Drag(true,4,0,0);input.End(true,4,true);
        Require(!input.TakeFire(out automatic)&&!automatic,"cancelled tap must not auto fire");
        input.Begin(true,5);input.Drag(true,5,0,0);input.End(true,5);input.Reset();
        Require(!input.TakeFire(out automatic)&&!automatic,"reset clears pending auto fire");
        input.SetAim(3,4,.5);Require(Math.Abs(input.AimX-.6)<1e-9&&Math.Abs(input.AimZ-.8)<1e-9&&input.AimMagnitude==.5,"selected target preserves direction and ground range");
        Console.WriteLine("REMAKE_TAP_GESTURE PASS off-centre taps/jitter, manual drags including return to centre, cancellation, one-shot/reset and selected aim");
    }
    private static void BasicAttackPad(){
        var input=new BattleTouchInput();
        var move=new BattleTouchInput.Sample(1,BattleTouchInput.Target.Move,BattleTouchInput.Phase.Began,0,1);
        var attack=new BattleTouchInput.Sample(2,BattleTouchInput.Target.Attack,BattleTouchInput.Phase.Began,0,0);
        var skill=new BattleTouchInput.Sample(3,BattleTouchInput.Target.Aim,BattleTouchInput.Phase.Began,0,0);
        input.ApplyTouches(new[]{move,attack,skill},3);
        Require(input.MovePointer==1&&input.AttackHeld&&input.AttackPointer==2&&input.AimPointer==3,"move/basic/skill have separate owners");
        input.ApplyTouches(new[]{new BattleTouchInput.Sample(3,BattleTouchInput.Target.None,BattleTouchInput.Phase.Ended,1,0),new BattleTouchInput.Sample(2,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,1),new BattleTouchInput.Sample(1,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,1)},3);
        Require(input.AttackHeld&&input.AttackDragged&&input.AttackZ==1&&input.MoveZ==1&&input.TakeFire(),"skill release preserves held attack and movement");
        input.EndAttack(2);Require(input.TakeAttack()&&!input.TakeAttack()&&!input.AttackHeld,"basic tap is consumed once");
        input.BeginAttack(4);input.EndAttack(4,true);Require(!input.TakeAttack(),"cancel cannot attack");
        input.BeginAttack(5);input.Reset();Require(!input.AttackHeld&&!input.TakeAttack(),"background/menu clears held basic attack");
        Console.WriteLine("REMAKE_BASIC_TOUCH PASS independent move/basic/skill fingers, held drag target selection, one-shot release and cancellation");
    }
    private static void RawMultitouch()
    {
        var input=new BattleTouchInput();
        var begin=new BattleTouchInput.Sample(4,BattleTouchInput.Target.Move,BattleTouchInput.Phase.Began,0,1);
        var held=new BattleTouchInput.Sample(4,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,0,1);
        input.ApplyTouches(new[]{begin},1);
        for(int i=0;i<100;i++){
            int right=10+i;
            var aim=new BattleTouchInput.Sample(right,BattleTouchInput.Target.Aim,BattleTouchInput.Phase.Began,1,0);
            // Array order changes as other fingers are added and removed.
            input.ApplyTouches(new[]{aim,held},2);
            input.ApplyTouches(new[]{held,new BattleTouchInput.Sample(right,BattleTouchInput.Target.None,BattleTouchInput.Phase.Held,1,0)},2);
            input.ApplyTouches(new[]{new BattleTouchInput.Sample(right,BattleTouchInput.Target.None,BattleTouchInput.Phase.Ended,1,0),held},2);
            Require(input.TakeFire(out bool automatic)&&automatic&&!input.TakeFire(),"right-finger tap must auto fire once");
            input.ApplyTouches(new[]{held,new BattleTouchInput.Sample(right+1000,BattleTouchInput.Target.Dash,BattleTouchInput.Phase.Began)},2);
            Require(input.TakeDash()&&!input.TakeDash(),"dash must be a discrete action");
            input.ApplyTouches(new[]{held},1);
            Require(input.MovePointer==4&&input.MoveZ==1,"attack/dash/reordered touches must preserve stationary movement finger");
        }
        input.ApplyTouches(new[]{held,new BattleTouchInput.Sample(900,BattleTouchInput.Target.Aim,BattleTouchInput.Phase.Began,-1,0)},2);
        input.ApplyTouches(new[]{held},1);
        Require(input.MovePointer==4&&input.MoveZ==1&&!input.TakeFire(),"lost aiming touch must not stop movement or fire");
        input.ApplyTouches(Array.Empty<BattleTouchInput.Sample>(),0);
        Require(input.MovePointer<0&&input.MoveZ==0,"missing movement finger must stop safely");
        Console.WriteLine("REMAKE_MULTITOUCH PASS 100 held-move + aim/release + dash cycles with reordered fingers and missing/cancelled touch cleanup");
    }
}
