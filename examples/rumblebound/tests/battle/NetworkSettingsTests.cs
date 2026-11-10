using System;
using Rumblebound.Example.Battle;

public static class NetworkSettingsTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Network settings: "+reason);}
    private static void Reject(BattleSettings settings,string reason)
    {
        try{settings.Validate();}catch(InvalidOperationException){return;}
        throw new Exception("Network settings accepted "+reason);
    }
    public static void Run()
    {
        var defaults=new BattleSettings();defaults.Validate();
        Require(defaults.DelaySeconds==0&&defaults.ReceiveJitterSeconds==0&&defaults.ReceiveLossProbability==0&&defaults.SendDelaySeconds==0&&defaults.SendJitterSeconds==0&&defaults.SendLossProbability==0,"default connections must remain unconditioned");
        var controls=new (string Name,double Max,Action<BattleSettings,double> Set)[]{
            ("receive delay",.3,(s,v)=>s.DelaySeconds=v),
            ("receive jitter",.3,(s,v)=>s.ReceiveJitterSeconds=v),
            ("receive loss",1,(s,v)=>s.ReceiveLossProbability=v),
            ("send delay",.3,(s,v)=>s.SendDelaySeconds=v),
            ("send jitter",.3,(s,v)=>s.SendJitterSeconds=v),
            ("send loss",1,(s,v)=>s.SendLossProbability=v)
        };
        foreach(var control in controls){
            var candidate=new BattleSettings();control.Set(candidate,0);candidate.Validate();
            control.Set(candidate,control.Max);
            if(BattleNetcode.ConditionerFactory==null)Reject(candidate,control.Name+" without the optional module");else candidate.Validate();
            foreach(double value in new[]{-.000001,control.Max+.000001,double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
                control.Set(candidate,value);Reject(candidate,control.Name+" outside its finite range");
            }
        }
        var configured=new BattleSettings{DelaySeconds=.21,ReceiveJitterSeconds=.03,ReceiveLossProbability=.2,SendDelaySeconds=.065,SendJitterSeconds=.012,SendLossProbability=.7};
        var copy=configured.Copy();
        Require(copy.DelaySeconds==.21&&copy.ReceiveJitterSeconds==.03&&copy.ReceiveLossProbability==.2&&copy.SendDelaySeconds==.065&&copy.SendJitterSeconds==.012&&copy.SendLossProbability==.7,"copy must retain independent directions and all loss/jitter controls");
        copy.Server.radius=20;copy.SendDelaySeconds=.1;copy.ReceiveJitterSeconds=.1;
        Require(configured.Server.radius!=20&&configured.SendDelaySeconds==.065&&configured.ReceiveJitterSeconds==.03,"editing a copied draft must not change the applied selection");
        var json=new GameJson();var legacy=json.Read<BattleSettings>("{\"DelaySeconds\":0.15}");
        Require(legacy.DelaySeconds==.15&&legacy.SendDelaySeconds==0&&legacy.SendJitterSeconds==0&&legacy.SendLossProbability==0&&legacy.ReceiveJitterSeconds==0&&legacy.ReceiveLossProbability==0,"legacy DelaySeconds must still enable receive delay only");
        var restored=json.Read<BattleSettings>(json.Write(configured));
        Require(restored.DelaySeconds==.21&&restored.ReceiveJitterSeconds==.03&&restored.ReceiveLossProbability==.2&&restored.SendDelaySeconds==.065&&restored.SendJitterSeconds==.012&&restored.SendLossProbability==.7,"settings serialization lost independent network controls");
        Console.WriteLine("REMAKE_NETWORK_SETTINGS PASS independent send/receive settings, finite bounds, isolated drafts and legacy receive-only delay");
    }
}
