using System;
using Realtime.Core;
using Realtime.Prediction;
using Realtime.Correction;

namespace Realtime.Examples.InterestLesson
{
    public sealed class PredictedPlayer
    {
        private readonly MotionModel model=new MotionModel();
        private IPrediction<MotionState,Drive> predictor;
        private ICorrectionPolicy policy=new ExponentialCorrection(.12,2);
        private readonly MotionState authority=new MotionState();
        private double offsetX,offsetZ,lastTime=double.NaN;
        private long tick;
        public bool Ready {get;private set;}
        public string Mode {get;private set;}="prediction";
        public string Correction {get;private set;}="smooth";
        public double X=>Math.Clamp(predictor.State.X+offsetX,-8,8);
        public double Z=>Math.Clamp(predictor.State.Z+offsetZ,-8,8);
        public double PredictedX=>predictor.State.X;
        public double PredictedZ=>predictor.State.Z;
        public double AuthorityX=>authority.X;
        public double AuthorityZ=>authority.Z;
        public double Offset=>Math.Sqrt(offsetX*offsetX+offsetZ*offsetZ);
        public double LastError {get;private set;}
        public long Corrections {get;private set;}
        public PredictionMetrics Metrics=>predictor.Metrics;
        public PredictedPlayer(){predictor=Create();}
        private IPrediction<MotionState,Drive> Create()=>Mode=="prediction"
            ? (IPrediction<MotionState,Drive>)new ReplayPrediction<MotionState,Drive>(model,30,.8,true)
            : new AuthorityPrediction<MotionState,Drive>(model);
        public void SetMode(string mode,double now)
        {
            if(mode!="authority" && mode!="prediction")throw new ArgumentException("mode");
            Mode=mode;predictor=Create();offsetX=offsetZ=0;
            if(Ready)predictor.Reconcile(authority,new PredictionAck {ResetHistory=true,ServerTick=tick},now);
            lastTime=double.NaN;
        }
        public void SetCorrection(string correction)
        {
            if(correction!="snap" && correction!="smooth")throw new ArgumentException("correction");
            Correction=correction;policy=correction=="snap"?(ICorrectionPolicy)new SnapCorrection():new ExponentialCorrection(.12,2);
            offsetX=offsetZ=0;
        }
        public void SentInput(long sequence,double now){if(Ready)predictor.SentInput(sequence,now);}
        public void Advance(double now,Drive input)
        {
            if(!Ready)return;
            double dt=double.IsNaN(lastTime)?0:Math.Max(0,now-lastTime);lastTime=now;
            double decay=policy.Decay(dt);offsetX*=decay;offsetZ*=decay;
            predictor.Advance(now,input);
        }
        public void Reconcile(PlayerState player,long serverTick,double now,bool reset)
        {
            reset|=!Ready;
            double beforeX=predictor.State.X,beforeZ=predictor.State.Z;
            authority.X=player.x;authority.Z=player.z;tick=serverTick;
            double applied=player.ack==0?0:Math.Clamp(serverTick-player.inputTick+1,0,15);
            predictor.Reconcile(authority,new PredictionAck {Sequence=player.ack,ServerTick=serverTick,AppliedInputTicks=applied,ResetHistory=reset},now);
            double errorX=beforeX-predictor.State.X,errorZ=beforeZ-predictor.State.Z;
            LastError=reset?0:Math.Sqrt(errorX*errorX+errorZ*errorZ);
            if(!reset && LastError>.01)Corrections++;
            double displayError=Math.Sqrt((offsetX+errorX)*(offsetX+errorX)+(offsetZ+errorZ)*(offsetZ+errorZ));
            if(!reset && Mode=="prediction" && policy.PreserveOffset(displayError,false)){offsetX+=errorX;offsetZ+=errorZ;}
            else offsetX=offsetZ=0;
            Ready=true;
            if(reset){lastTime=double.NaN;Corrections=0;}
        }
        public void Clear()
        {Ready=false;predictor=Create();authority.X=authority.Z=offsetX=offsetZ=LastError=0;tick=Corrections=0;lastTime=double.NaN;}
    }
}
