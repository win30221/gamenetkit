using System;

namespace Rumblebound.Example.Battle
{
    // Per-pointer ownership keeps independent fingers from stealing either stick.
    public sealed class BattleTouchInput
    {
        public enum Target { None, Move, Aim, Dash, Attack }
        public enum Phase { Began, Held, Ended, Cancelled }
        public struct Sample
        {
            public int Finger;
            public Target Control;
            public Phase State;
            public double X,Z;
            public Sample(int finger,Target control,Phase state,double x=0,double z=0){Finger=finger;Control=control;State=state;X=x;Z=z;}
        }
        public int MovePointer { get; private set; } = -1;
        public int AimPointer { get; private set; } = -1;
        public int AttackPointer { get; private set; } = -1;
        public int DashPointer { get; private set; } = -1;
        public bool AimDragged=>aimDragged;
        public bool AttackHeld=>AttackPointer>=0;
        public bool AttackDragged{get;private set;}
        public double AttackX{get;private set;}
        public double AttackZ{get;private set;}
        private bool attackTap,attackStarted;
        private double attackStartX,attackStartZ;
        public double MoveX { get; private set; }
        public double MoveZ { get; private set; }
        public double AimX { get; private set; }
        public double AimZ { get; private set; } = 1;
        public double AimMagnitude { get; private set; } = 1;
        // Coordinates are in stick-radius units. Small finger jitter remains a
        // tap even when contact starts away from the centre of the attack pad.
        private const double AimDragThreshold = .25;
        private bool fire, dash, autoAim, aimStarted, aimDragged;
        private double aimStartX, aimStartZ;
        // A complete frame is indexed by stable finger IDs, never touch-array
        // indices or UI pointer capture. Other fingers ending cannot release move.
        public void ApplyTouches(Sample[] samples,int count)
        {
            if(samples==null||count<0||count>samples.Length)throw new ArgumentException("Invalid touch frame");
            for(int i=0;i<count;i++){
                var sample=samples[i];
                if(sample.State==Phase.Began){
                    if(sample.Control==Target.Move)Begin(false,sample.Finger);
                    else if(sample.Control==Target.Aim)Begin(true,sample.Finger);
                    else if(sample.Control==Target.Dash)BeginDash(sample.Finger);
                    else if(sample.Control==Target.Attack)BeginAttack(sample.Finger);
                }
                bool ending=sample.State==Phase.Ended||sample.State==Phase.Cancelled;
                if(sample.Finger==MovePointer){Drag(false,sample.Finger,sample.X,sample.Z);if(ending)End(false,sample.Finger,true);}
                if(sample.Finger==AimPointer){Drag(true,sample.Finger,sample.X,sample.Z);if(ending)End(true,sample.Finger,sample.State==Phase.Cancelled);}
                if(sample.Finger==AttackPointer){DragAttack(sample.Finger,sample.X,sample.Z);if(ending)EndAttack(sample.Finger,sample.State==Phase.Cancelled);}
                if(sample.Finger==DashPointer&&ending)EndDash(sample.Finger);
            }
            bool movePresent=false,aimPresent=false,attackPresent=false,dashPresent=false;
            for(int i=0;i<count;i++){
                if(samples[i].State==Phase.Ended||samples[i].State==Phase.Cancelled)continue;
                movePresent|=samples[i].Finger==MovePointer;
                aimPresent|=samples[i].Finger==AimPointer;
                attackPresent|=samples[i].Finger==AttackPointer;
                dashPresent|=samples[i].Finger==DashPointer;
            }
            if(!movePresent)End(false,MovePointer,true);
            if(!aimPresent)End(true,AimPointer,true);
            if(!attackPresent)EndAttack(AttackPointer,true);
            if(!dashPresent)EndDash(DashPointer);
        }
        public bool Begin(bool aiming,int pointer)
        {
            if(pointer<0||pointer==MovePointer||pointer==AimPointer||pointer==AttackPointer||pointer==DashPointer)return false;
            if(aiming){if(AimPointer>=0)return false;AimPointer=pointer;aimStarted=aimDragged=false;}
            else{if(MovePointer>=0)return false;MovePointer=pointer;}
            return true;
        }
        public void Drag(bool aiming,int pointer,double x,double z)
        {
            if(pointer<0||pointer!=(aiming?AimPointer:MovePointer)||double.IsNaN(x+z)||double.IsInfinity(x+z))return;
            double length=Math.Sqrt(x*x+z*z);
            double magnitude=Math.Min(1,Math.Max(0,(length-.12)/.88));
            if(aiming){
                if(!aimStarted){aimStarted=true;aimStartX=x;aimStartZ=z;}
                double dx=x-aimStartX,dz=z-aimStartZ;
                if(dx*dx+dz*dz>AimDragThreshold*AimDragThreshold)aimDragged=true;
                if(magnitude>0){AimX=x/length;AimZ=z/length;AimMagnitude=magnitude;}
            }
            else{MoveX=length>.12?x/length:0;MoveZ=length>.12?z/length:0;}
        }
        public void End(bool aiming,int pointer,bool cancelled=false)
        {
            if(pointer<0||pointer!=(aiming?AimPointer:MovePointer))return;
            if(aiming){AimPointer=-1;if(!cancelled){fire=true;autoAim=!aimDragged;}}
            else{MovePointer=-1;MoveX=MoveZ=0;}
        }
        public void FollowMovement()
        {
            double length=Math.Sqrt(MoveX*MoveX+MoveZ*MoveZ);
            if(AimPointer<0&&length>0&&!fire){AimX=MoveX/length;AimZ=MoveZ/length;AimMagnitude=1;}
        }
        public void Dash()=>dash=true;
        public bool BeginDash(int pointer){if(pointer<0||pointer==MovePointer||pointer==AimPointer||pointer==AttackPointer||DashPointer>=0)return false;DashPointer=pointer;dash=true;return true;}
        public void EndDash(int pointer){if(pointer==DashPointer)DashPointer=-1;}
        public bool BeginAttack(int pointer){if(pointer<0||pointer==MovePointer||pointer==AimPointer||pointer==DashPointer||AttackPointer>=0)return false;AttackPointer=pointer;attackStarted=AttackDragged=false;AttackX=AttackZ=0;return true;}
        public void DragAttack(int pointer,double x,double z){
            if(pointer<0||pointer!=AttackPointer||double.IsNaN(x+z)||double.IsInfinity(x+z))return;
            if(!attackStarted){attackStarted=true;attackStartX=x;attackStartZ=z;}
            double dx=x-attackStartX,dz=z-attackStartZ;if(dx*dx+dz*dz>AimDragThreshold*AimDragThreshold)AttackDragged=true;
            double length=Math.Max(1,Math.Sqrt(x*x+z*z));AttackX=x/length;AttackZ=z/length;
        }
        public void EndAttack(int pointer,bool cancelled=false){if(pointer<0||pointer!=AttackPointer)return;AttackPointer=-1;if(!cancelled)attackTap=true;}
        public bool TakeAttack(){bool value=attackTap;attackTap=false;return value;}
        public void SetAim(double x,double z,double magnitude=1)
        {
            double length=Math.Sqrt(x*x+z*z);
            if(double.IsNaN(length)||double.IsInfinity(length)||length<1e-6||double.IsNaN(magnitude)||double.IsInfinity(magnitude))return;
            AimX=x/length;AimZ=z/length;AimMagnitude=Math.Min(1,Math.Max(0,magnitude));
        }
        public bool TakeFire(out bool automatic){bool value=fire;automatic=value&&autoAim;fire=autoAim=false;return value;}
        public bool TakeFire()=>TakeFire(out _);
        public bool TakeDash(){bool value=dash;dash=false;return value;}
        public void Reset(){MovePointer=AimPointer=AttackPointer=DashPointer=-1;MoveX=MoveZ=AimX=AttackX=AttackZ=0;AimZ=AimMagnitude=1;fire=dash=autoAim=aimStarted=aimDragged=attackTap=attackStarted=AttackDragged=false;}
    }
}
