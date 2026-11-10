using System;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // This only selects a normal aim point. Cooldown, hit tests and damage stay
    // on the authority, through the same action path as a manually aimed shot.
    public static class BattleAimAssist
    {
        public static bool GroundPoint(GameContent content,HeroDefinition hero,double x,double z,double tx,double tz,out double px,out double pz){
            px=tx;pz=tz;
            if(hero==null||hero.attackKind!="ground"||double.IsNaN(x+z+tx+tz)||double.IsInfinity(x+z+tx+tz))return false;
            double dx=tx-x,dz=tz-z,length=Math.Sqrt(dx*dx+dz*dz);
            if(length>hero.castRange){double scale=(hero.castRange-.00001)/length;px=x+dx*scale;pz=z+dz*scale;}
            return Math.Abs(px)<=content.Rules.arenaHalfSize&&Math.Abs(pz)<=content.Rules.arenaHalfSize&&ArenaCollision.CircleClear(content.Map,px,pz,0);
        }
        public static bool TryTarget(GameContent content,Snapshot state,string playerID,GameMovement movement,
            out PlayerState target,out double targetX,out double targetZ)
        {
            target=null;targetX=targetZ=0;
            if(content==null||state?.players==null)return false;
            var own=Array.Find(state.players,p=>p.playerID==playerID);
            if(own==null||own.hp<=0||own.left||!own.online)return false;
            var hero=content.Hero(own.heroID);if(hero==null)return false;
            var rules=content.Rules;
            bool ground=hero.attackKind=="ground",melee=hero.attackKind=="melee";
            double reach=ground?hero.castRange+hero.impactRadius+rules.playerRadius:melee?hero.meleeRange+rules.playerRadius:
                rules.playerRadius+rules.projectileRadius+.05+hero.projectileSpeed*hero.projectileLifetimeTicks/rules.tickRate;
            Sample(own,true,out double x,out double z);
            double nearest=double.PositiveInfinity;
            foreach(var candidate in state.players){
                if(candidate.playerID==playerID||candidate.teamID==own.teamID||candidate.hp<=0||candidate.left||
                    !candidate.online||candidate.protectedUntilTick>state.tick)continue;
                Sample(candidate,false,out double tx,out double tz);
                double dx=tx-x,dz=tz-z,distance=dx*dx+dz*dz;
                if(double.IsNaN(distance)||double.IsInfinity(distance)||distance>reach*reach)continue;
                double px=tx,pz=tz;
                if(ground){
                    if(!GroundPoint(content,hero,x,z,tx,tz,out px,out pz))continue;
                    double missX=tx-px,missZ=tz-pz,hitRadius=hero.impactRadius+rules.playerRadius;
                    if(missX*missX+missZ*missZ>hitRadius*hitRadius)continue;
                }else if(ArenaCollision.FirstMapHit(content.Map,x,z,dx,dz,melee?0:rules.projectileRadius,true,out _,out _,out _))continue;
                // Stable selection for equal distances, independent of wire order.
                if(distance>nearest||distance==nearest&&target!=null&&String.CompareOrdinal(candidate.playerID,target.playerID)>=0)continue;
                nearest=distance;target=candidate;targetX=px;targetZ=pz;
            }
            return target!=null;

            void Sample(PlayerState player,bool local,out double px,out double pz)
            {
                if(movement!=null)movement.Position(player,local,out px,out pz);
                else{px=player.x;pz=player.z;}
            }
        }
    }
}
