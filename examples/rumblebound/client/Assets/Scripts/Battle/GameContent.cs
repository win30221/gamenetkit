using System;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    public interface IGameJson
    {
        T Read<T>(string source);
        string Write<T>(T value);
    }
    public sealed class GameContent
    {
        private readonly IGameJson json;
        public LabConfig Config{get;}
        public Rules Rules=>Config.rules;
        public ArenaMapDefinition Map=>Config.map;
        public GameContent(LabConfig config,IGameJson json)
        {
            this.json=json??throw new ArgumentNullException(nameof(json));
            if(config==null)throw new ArgumentNullException(nameof(config));
            Config=json.Read<LabConfig>(json.Write(config));
            var r=Config.rules;
            if(r==null||r.protocol!=5)throw new InvalidOperationException("Unsupported game protocol");
            bool Positive(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>0;
            if(r==null||r.protocol<=0||string.IsNullOrEmpty(r.content)||r.tickRate!=30||r.snapshotRate!=15||r.maxPlayers<1||r.maxPlayers>8||r.inputTimeoutTicks<1||r.inputTimeoutTicks>120||!Positive(r.speed)||r.speed>100||!Positive(r.playerRadius)||r.playerRadius>2||r.maxProjectiles<1||r.maxProjectiles>256||r.maxActiveGroundCasts<1||r.maxActiveGroundCasts>64||r.heroes==null||r.heroes.Length!=3)throw new InvalidOperationException("Unsupported game rules");
            Config.map=ArenaCollision.ParseVerifiedMap(Config.mapJSON,Config.mapHash,json.Read<ArenaMapDefinition>,r.playerRadius);
            if(Math.Abs(Config.map.arenaHalfSize-r.arenaHalfSize)>1e-8||r.collision==null||r.collision.iterations<1||r.collision.iterations>64)throw new InvalidOperationException("Map/rules geometry mismatch");
            foreach(string id in new[]{"ranger","tank","mage"}){
                var h=Hero(id);if(h==null||!Positive(h.speed)||h.speed>100||h.maxHP<1||h.maxHP>10000)throw new InvalidOperationException("Invalid hero definition");
            }
        }
        public HeroDefinition Hero(string id){foreach(var h in Rules.heroes)if(h!=null&&h.heroID==id)return h;return null;}
        public Rules ForHero(string id)
        {
            var r=json.Read<Rules>(json.Write(Rules));var h=Hero(id);if(h==null)return r;
            r.maxHP=h.maxHP;r.speed=h.speed;r.projectileDamage=h.projectileDamage;r.projectileSpeed=h.projectileSpeed;
            r.projectileLifetimeTicks=h.projectileLifetimeTicks;r.fireCooldownTicks=h.fireCooldownTicks;r.dashSpeed=h.dashSpeed;r.dashCooldownTicks=h.dashCooldownTicks;return r;
        }
    }
    [Serializable] public sealed class BattleSettings
    {
        public string Transport="websocket";
        public string Prediction=BattleNetcode.DefaultPrediction,Correction=BattleNetcode.DefaultPrediction=="authority"?"snap":BattleNetcode.DefaultCorrection,Timeline=BattleNetcode.DefaultMode,Clock=BattleNetcode.ClockFactory==null?"snapshot":"disciplined";
        public ServerNetOptions Server=new ServerNetOptions();
        public bool SpeculativeActions;
        // DelaySeconds keeps its original receive-only meaning for saved settings.
        public double DelaySeconds,ReceiveJitterSeconds,ReceiveLossProbability;
        public double SendDelaySeconds,SendJitterSeconds,SendLossProbability;
        public BattleSettings Copy()=>new BattleSettings{Transport=Transport,Prediction=Prediction,Correction=Correction,Timeline=Timeline,Clock=Clock,SpeculativeActions=SpeculativeActions,DelaySeconds=DelaySeconds,
            ReceiveJitterSeconds=ReceiveJitterSeconds,ReceiveLossProbability=ReceiveLossProbability,SendDelaySeconds=SendDelaySeconds,SendJitterSeconds=SendJitterSeconds,SendLossProbability=SendLossProbability,
            Server=new ServerNetOptions{codec=Server.codec,interest=Server.interest,lag=Server.lag,quantize=Server.quantize,history=Server.history,radius=Server.radius}};
        private static bool InRange(double value,double maximum)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0&&value<=maximum;
        public void Validate()
        {
            if(Transport!="websocket"&&Transport!="webtransport")throw new InvalidOperationException("Unknown transport");
            if(!BattleNetcode.Predictions.ContainsKey(Prediction)||!BattleNetcode.Corrections.ContainsKey(Correction)||!BattleNetcode.Timelines.Contains(Timeline)||Clock!="snapshot"&&Clock!="disciplined"||Clock=="disciplined"&&BattleNetcode.ClockFactory==null||!BattleNetcode.Predictions[Prediction].Enabled&&BattleNetcode.CreateCorrection(Correction).Smooths)throw new InvalidOperationException("Unavailable presentation setting");
            if(!InRange(DelaySeconds,.3)||!InRange(ReceiveJitterSeconds,.3)||!InRange(SendDelaySeconds,.3)||!InRange(SendJitterSeconds,.3)||!InRange(ReceiveLossProbability,1)||!InRange(SendLossProbability,1)||BattleNetcode.ConditionerFactory==null&&(DelaySeconds>0||ReceiveJitterSeconds>0||ReceiveLossProbability>0||SendDelaySeconds>0||SendJitterSeconds>0||SendLossProbability>0))throw new InvalidOperationException("Invalid network simulation setting");
            if(Server==null||Server.codec!="json"&&Server.codec!="delta"||Server.interest!="all"&&Server.interest!="radius"||Server.lag!="off"&&Server.lag!="melee"||Server.lag!="off"&&!Server.history||double.IsNaN(Server.radius)||Server.radius<4||Server.radius>40||Server.codec=="delta"&&BattleNetcode.DecoderFactory==null||SpeculativeActions&&BattleNetcode.ActionsFactory==null)throw new InvalidOperationException("Invalid server feature selection");
        }
    }
}
