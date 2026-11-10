using System;
using System.Text.Json;
using System.Threading.Tasks;
using Rumblebound.Example.Battle;

public sealed class GameJson:IGameJson
{
    private readonly JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
    public T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,options);
    public string Write<T>(T value)=>JsonSerializer.Serialize(value,options);
}
public static class RemovalProgram
{
    public static async Task<int> Main(string[] args)
    {
        try{
            var selection=new BattleSettings();selection.Validate();
            if(selection.Prediction!="authority"||selection.Correction!="snap"||selection.Timeline!="latest"||selection.Clock!="snapshot"||BattleNetcode.PlaybackFactory!=null||BattleNetcode.DecoderFactory!=null||BattleNetcode.ActionsFactory!=null)throw new Exception("Core fallbacks not selected");
            await FlowTests.Run(args[0]);
            Console.WriteLine("REMAKE_REMOVAL PASS all ten optional package sources physically absent from isolated tree; authority/snap/latest/snapshot fallbacks, actual two-player rooms/loading/reconnect, mage practice/AI and logout");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
