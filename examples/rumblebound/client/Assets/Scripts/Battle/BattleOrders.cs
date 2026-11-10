using System;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    public sealed class BattleOrders
    {
        public string TargetID{get;private set;}
        public long TargetLife{get;private set;}
        public bool Chasing{get;private set;}
        public bool Moving{get;private set;}
        public bool AttackMoving{get;private set;}
        public double DestinationX=>path.Length>0?path[path.Length-1].x:goalX;
        public double DestinationZ=>path.Length>0?path[path.Length-1].z:goalZ;
        private double goalX,goalZ,nextPlan,lastGoalX,lastGoalZ;
        private ArenaPoint[] path=Array.Empty<ArenaPoint>();
        private int waypoint;
        public void Move(double x,double z){Clear();Moving=true;goalX=x;goalZ=z;}
        public void AttackMove(double x,double z){Move(x,z);AttackMoving=true;}
        public void Attack(PlayerState target,bool chase){Clear();if(target==null)return;TargetID=target.playerID;TargetLife=target.life;Chasing=chase;}
        public void Clear(){TargetID=null;TargetLife=0;Moving=Chasing=AttackMoving=false;path=Array.Empty<ArenaPoint>();waypoint=0;nextPlan=0;}
        public static bool Enemy(PlayerState own,PlayerState target,long tick)=>target!=null&&own!=null&&target.playerID!=own.playerID&&target.teamID!=own.teamID&&target.hp>0&&target.online&&!target.left&&target.protectedUntilTick<=tick;
        public static void Position(PlayerState p,bool own,GameMovement movement,out double x,out double z){if(movement!=null)movement.Position(p,own,out x,out z);else{x=p.x;z=p.z;}}
        public static bool InRange(GameContent content,PlayerState own,PlayerState target,GameMovement movement){
            if(target==null)return false;Position(own,true,movement,out double x,out double z);Position(target,false,movement,out double tx,out double tz);
            double range=content.Hero(own.heroID).basicRange;
            return (tx-x)*(tx-x)+(tz-z)*(tz-z)<=range*range&&!ArenaCollision.FirstMapHit(content.Map,x,z,tx-x,tz-z,0,true,out _,out _,out _);
        }
        public static PlayerState Nearest(GameContent content,Snapshot state,string id,GameMovement movement,double aimX=0,double aimZ=0){
            var own=Array.Find(state.players,p=>p.playerID==id);PlayerState result=null;double best=double.PositiveInfinity;
            if(own==null)return null;Position(own,true,movement,out double x,out double z);double aimLength=Math.Sqrt(aimX*aimX+aimZ*aimZ);
            foreach(var p in state.players){if(!Enemy(own,p,state.tick)||!InRange(content,own,p,movement))continue;
                Position(p,false,movement,out double tx,out double tz);double dx=tx-x,dz=tz-z,distance=Math.Sqrt(dx*dx+dz*dz),score=distance;
                if(aimLength>.1){double cosine=(dx*aimX+dz*aimZ)/Math.Max(.00001,distance*aimLength);if(cosine<.5)continue;score=(1-cosine)*100+distance;}
                if(score<best||score==best&&String.CompareOrdinal(p.playerID,result?.playerID)<0){best=score;result=p;}
            }return result;
        }
        public PlayerState Drive(GameContent content,Snapshot state,string id,GameMovement movement,double now,out double dx,out double dz){
            dx=dz=0;var own=Array.Find(state.players,p=>p.playerID==id);if(own==null||own.hp<=0||own.left){Clear();return null;}
            PlayerState target=null;
            if(AttackMoving){
                target=Array.Find(state.players,p=>p.playerID==TargetID&&p.life==TargetLife);
                if(!Enemy(own,target,state.tick)||!InRange(content,own,target,movement))target=Nearest(content,state,id,movement);
                TargetID=target?.playerID;TargetLife=target?.life??0;
                if(target!=null)return target;
            }
            if(!AttackMoving&&TargetID!=null){target=Array.Find(state.players,p=>p.playerID==TargetID&&p.life==TargetLife);
                if(!Enemy(own,target,long.MaxValue)){Clear();return null;}
                if(InRange(content,own,target,movement))return target.protectedUntilTick<=state.tick?target:null;
                if(!Chasing)return null;Position(target,false,movement,out goalX,out goalZ);
            }
            if(!Moving&&!Chasing)return null;
            Position(own,true,movement,out double x,out double z);
            if(now>=nextPlan&&(path.Length==0||Chasing&&(Math.Abs(goalX-lastGoalX)+Math.Abs(goalZ-lastGoalZ)>.4))){
                path=ArenaNavigation.Find(content.Map,x,z,goalX,goalZ,content.Rules.playerRadius);waypoint=0;nextPlan=now+.25;lastGoalX=goalX;lastGoalZ=goalZ;
            }
            while(waypoint<path.Length){dx=path[waypoint].x-x;dz=path[waypoint].z-z;double length=Math.Sqrt(dx*dx+dz*dz);
                if(length>.16){dx/=length;dz/=length;return null;}waypoint++;}
            dx=dz=0;if(Moving)Clear();return null;
        }
    }
}
