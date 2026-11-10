using System;
using Realtime.Core;

namespace Realtime.Examples.InterestLesson
{
    public sealed class MotionState { public double X, Z; }
    public struct Drive { public double X, Z; public Drive(double x,double z){X=x;Z=z;} }
    // Application-owned rules, matching server/world.go. No Unity objects.
    public sealed class MotionModel : IPredictionModel<MotionState,Drive>
    {
        public MotionState CreateState()=>new MotionState();
        public void Copy(MotionState source,MotionState target){target.X=source.X;target.Z=source.Z;}
        public void Step(MotionState state,Drive input,double seconds,double tick)
        {
            double length=Math.Sqrt(input.X*input.X+input.Z*input.Z),scale=length>1?1/length:1;
            state.X=Math.Clamp(state.X+input.X*scale*4*seconds,-8,8);
            state.Z=Math.Clamp(state.Z+input.Z*scale*4*seconds,-8,8);
        }
    }
}
