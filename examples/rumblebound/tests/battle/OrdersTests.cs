using System;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;

public static class OrdersTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Orders: "+reason);}
    public static void Run(GameContent source){
        var content=new GameContent(source.Config,new GameJson());
        content.Map.arenaHalfSize=content.Rules.arenaHalfSize=30;
        content.Map.obstacles=new[]{new ArenaObstacle{id="wall",x=0,z=0,halfX=.6,halfZ=3,blocksMovement=true,blocksProjectiles=true}};
        var path=ArenaNavigation.Find(content.Map,-4,0,4,0,content.Rules.playerRadius);
        Require(path.Length>=2,"point movement must navigate around cover");
        double x=-4,z=0;
        foreach(var p in path){Require(ArenaCollision.CircleClear(content.Map,p.x,p.z,content.Rules.playerRadius)&&ArenaNavigation.Clear(content.Map,x,z,p.x,p.z,content.Rules.playerRadius),"every navigation edge must be physically clear");x=p.x;z=p.z;}
        Require(Math.Abs(x-4)<.01&&Math.Abs(z)<.01,"navigation reaches exact legal point");
        path=ArenaNavigation.Find(content.Map,-4,0,0,0,content.Rules.playerRadius);
        Require(path.Length>0&&ArenaCollision.CircleClear(content.Map,path[path.Length-1].x,path[path.Length-1].z,content.Rules.playerRadius),"blocked destination resolves to legal point");
        content.Map.obstacles=Array.Empty<ArenaObstacle>();
        var own=new PlayerState{playerID="own",heroID="ranger",teamID="blue",hp=100,online=true};
        var enemy=new PlayerState{playerID="enemy",heroID="tank",teamID="red",x=8,hp=100,online=true,life=2};
        var state=new Snapshot{tick=100,players=new[]{own,enemy}};
        var orders=new BattleOrders();orders.Attack(enemy,true);
        Require(orders.Drive(content,state,"own",null,1,out double dx,out double dz)==null&&dx>.9,"out-of-range click chases");
        own.x=3;
        Require(orders.Drive(content,state,"own",null,2,out dx,out dz)==enemy&&dx==0&&dz==0,"chase stops in attack range");
        enemy.x=15;
        Require(orders.Drive(content,state,"own",null,2.5,out dx,out dz)==null&&dx>.9&&orders.TargetID==enemy.playerID,"locked target leaving range resumes chase");
        Require(BattleOrders.NearestToPoint(content,state,"own",null,12,0)==enemy,"preview selects enemy beyond current attack range near destination");
        own.x=10;
        Require(orders.Drive(content,state,"own",null,2.8,out dx,out dz)==enemy&&dx==0,"chase resumes attacks only on reaching range");
        enemy.life++;
        Require(orders.Drive(content,state,"own",null,3,out _,out _)==null&&orders.TargetID==null,"old target life is never reacquired implicitly");
        own.x=0;enemy.x=3;var other=new PlayerState{playerID="north",heroID="mage",teamID="red",z=2,hp=100,online=true};state.players=new[]{own,enemy,other};
        Require(BattleOrders.Nearest(content,state,"own",null)==other,"basic auto-target is nearest");
        Require(BattleOrders.Nearest(content,state,"own",null,1,0)==enemy,"drag selects enemy in requested direction");
        orders.Move(5,0);Require(orders.Drive(content,state,"own",null,4,out dx,out dz)==null&&dx>.9,"ground click overrides attack");
        enemy.x=10;other.z=10;orders.AttackMove(15,0);
        Require(orders.Drive(content,state,"own",null,5,out dx,out dz)==null&&dx>.9&&orders.AttackMoving,"attack-move starts toward ground destination");
        enemy.x=5;
        Require(orders.Drive(content,state,"own",null,6,out dx,out dz)==enemy&&dx==0&&dz==0&&orders.Moving,"attack-move acquires hostile in range and keeps destination");
        Require(orders.DestinationX==15&&orders.DestinationZ==0,"attack-move replaced final destination with enemy");
        enemy.hp=0;
        Require(orders.Drive(content,state,"own",null,7,out dx,out dz)==null&&dx>.9&&orders.TargetID==null,"attack-move resumes after target dies");
        enemy.hp=100;enemy.x=20;
        Require(orders.Drive(content,state,"own",null,8,out dx,out dz)==null&&dx>.9,"attack-move does not chase out-of-range enemy away from route");
        orders.Move(2,0);Require(!orders.AttackMoving,"right-click movement cancels attack-move");
        orders.AttackMove(2,0);orders.Clear();Require(!orders.Moving&&!orders.AttackMoving,"stop/dash clears attack-move");
        Console.WriteLine("REMAKE_ORDERS PASS swept obstacle navigation, blocked goal, chase/range, target-life isolation, nearest/directional lock and move override");
    }
}
