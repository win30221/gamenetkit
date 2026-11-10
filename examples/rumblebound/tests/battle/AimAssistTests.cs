using System;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;

public static class AimAssistTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Aim assist: "+reason);}
    private static PlayerState Player(string id,double x,double z,string team="red")=>new PlayerState{playerID=id,teamID=team,heroID="ranger",x=x,z=z,hp=100,online=true};
    public static void Run(GameContent source)
    {
        // Isolated verified-content copy; test geometry never changes live rules.
        var content=new GameContent(source.Config,new GameJson());
        content.Map.arenaHalfSize=content.Rules.arenaHalfSize=100;
        content.Map.obstacles=Array.Empty<ArenaObstacle>();
        var own=Player("own",0,0,"blue");
        var state=new Snapshot{tick=100,players=new[]{own}};
        bool Target(out PlayerState selected,out double x,out double z)=>BattleAimAssist.TryTarget(content,state,"own",null,out selected,out x,out z);
        var near=Player("near",0,2);var far=Player("far",3,0);var ally=Player("ally",.5,0,"blue");
        var dead=Player("dead",.6,0);dead.hp=0;
        var left=Player("left",.7,0);left.left=true;
        var offline=Player("offline",.8,0);offline.online=false;
        var protectedEnemy=Player("protected",.9,0);protectedEnemy.protectedUntilTick=101;
        state.players=new[]{own,far,ally,dead,left,offline,protectedEnemy,near};
        Require(Target(out var chosen,out double tx,out double tz)&&chosen==near&&tx==0&&tz==2,"nearest living hostile, filtering ally/dead/left/offline/protected/self");
        state.players=new[]{own,ally,dead,left,offline,protectedEnemy};
        Require(!Target(out chosen,out _,out _),"no valid target must return false");
        protectedEnemy.protectedUntilTick=state.tick;
        Require(Target(out chosen,out _,out _)&&chosen==protectedEnemy,"protection expires at the current tick");
        var a=Player("a",-2,0);var b=Player("b",2,0);
        state.players=new[]{b,own,a};Require(Target(out chosen,out _,out _)&&chosen==a,"equal distances use stable ID");
        state.players=new[]{a,own,b};Require(Target(out chosen,out _,out _)&&chosen==a,"wire order cannot change tied target");
        var ranger=content.Hero("ranger");
        double reach=content.Rules.playerRadius+content.Rules.projectileRadius+.05+ranger.projectileSpeed*ranger.projectileLifetimeTicks/content.Rules.tickRate;
        var edge=Player("edge",reach+.01,0);state.players=new[]{own,edge};
        Require(!Target(out _,out _,out _),"ranged target outside lifetime reach");
        edge.x=reach;Require(Target(out _,out _,out _),"ranged target at lifetime reach");
        content.Map.obstacles=new[]{new ArenaObstacle{id="cover",x=0,z=1,halfX=.4,halfZ=.2,blocksMovement=true,blocksProjectiles=true}};
        state.players=new[]{own,near,far};
        Require(Target(out chosen,out _,out _)&&chosen==far,"blocked nearest enemy must yield to reachable enemy");
        content.Map.obstacles[0].blocksProjectiles=false;
        Require(Target(out chosen,out _,out _)&&chosen==near,"movement-only cover does not block shots");
        content.Map.obstacles[0].blocksProjectiles=true;
        own.heroID="mage";
        Require(Target(out chosen,out tx,out tz)&&chosen==near&&tx==near.x&&tz==near.z,"ground cast may cross cover to a legal enemy point");
        near.z=1;state.players=new[]{own,near};Require(!Target(out _,out _,out _),"ground cast cannot land inside cover");
        content.Map.obstacles=Array.Empty<ArenaObstacle>();
        var mage=content.Hero("mage");
        near.z=mage.castRange+.8;Require(Target(out chosen,out tx,out tz)&&chosen==near&&tz<=mage.castRange&&Math.Abs(tz-mage.castRange)<.0001,"ground auto casts at outer edge when explosion reaches enemy");
        near.z=mage.castRange+mage.impactRadius+content.Rules.playerRadius+.01;Require(!Target(out _,out _,out _),"no ground auto cast when explosion cannot hit");
        near.z=content.Hero("mage").castRange;Require(Target(out _,out _,out _),"ground cast range boundary");
        Require(BattleAimAssist.GroundPoint(content,mage,0,0,100,100,out tx,out tz)&&Math.Abs(Math.Sqrt(tx*tx+tz*tz)-mage.castRange)<.0001,"manual diagonal target clamps radially");
        own.heroID="tank";
        near.z=content.Hero("tank").meleeRange+content.Rules.playerRadius+.01;Require(!Target(out _,out _,out _),"melee target outside target-disc reach");
        near.z-=.01;Require(Target(out _,out _,out _),"melee target disc reaches sector edge");
        own.hp=0;Require(!Target(out _,out _,out _),"dead owner cannot select targets");
        Console.WriteLine("REMAKE_AIM_ASSIST PASS nearest eligible enemy, stable ties, hero ranges, projectile/ground cover rules and no-target result");
    }
}
