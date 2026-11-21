using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace TowerDuel.Editor
{
    public sealed class TowerDuelBalanceWindow : EditorWindow
    {
        [SerializeField] private DuelRules draft;
        [SerializeField] private string originalJson, originalDraft;
        internal static string ExampleRoot => Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        internal static string CanonicalPath => Path.Combine(Application.dataPath,"Resources/TowerDuel/rules.json");
        internal static string ClientPath => CanonicalPath;
        private const string LayoutPath="Assets/Editor/TowerDuelBalance.uxml";

        [MenuItem("Tower Duel/Balance settings · 數值調整")]
        public static void Open() => GetWindow<TowerDuelBalanceWindow>("雙橋數值調整");

        private void OnEnable()
        {
            minSize=new Vector2(760,540);
            Undo.undoRedoPerformed+=Rebuild;
            EditorApplication.playModeStateChanged+=PlayModeChanged;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed-=Rebuild;
            EditorApplication.playModeStateChanged-=PlayModeChanged;
        }
        private void OnFocus(){if(rootVisualElement.Q<Label>("serverLabel")!=null)RefreshState();}
        private void PlayModeChanged(PlayModeStateChange state)=>Rebuild();
        public void CreateGUI()
        {
            try {if(draft==null)LoadDraft();Rebuild();}
            catch(Exception error){rootVisualElement.Add(new Label(error.Message));}
        }
        private void LoadDraft()
        {
            originalJson=File.ReadAllText(CanonicalPath);
            draft=JsonUtility.FromJson<DuelRules>(originalJson);
            originalDraft=Serialize(draft);
            hasUnsavedChanges=false;
        }
        private void Rebuild()
        {
            if(draft==null)return;
            var root=rootVisualElement;root.Clear();
            var layout=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            if(!layout)throw new InvalidOperationException("找不到數值調整視窗版面。");
            layout.CloneTree(root);
            root.Q<Button>("reloadButton").clicked+=()=>{LoadDraft();Rebuild();};
            root.Q<Button>("saveButton").clicked+=()=>Run(SaveDraft);
            root.Q<Button>("restartButton").clicked+=()=>Run(()=>{SaveDraft();TowerDuelBalanceServer.Restart();RefreshState();ShowMessage("共用規則已儲存，本機後端已套用新數值。");});
            root.Q<Button>("stopButton").clicked+=()=>Run(()=>{TowerDuelBalanceServer.Stop();RefreshState();});
            root.Q<Button>("sceneButton").clicked+=()=>{
                if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene("Assets/Scenes/TowerDuel.unity");
            };
            root.Q<Label>("revisionLabel").text="共用規則："+draft.id+" · 正常對局 "+(draft.duration*1000)+" ms · 每次儲存自動更新規則雜湊";
            var towers=root.Q("towerFields");
            Integer(towers,"前塔血量","towerHP",draft.towerHP,v=>draft.towerHP=v);
            Integer(towers,"主堡血量","keepHP",draft.keepHP,v=>draft.keepHP=v);
            Integer(towers,"塔共用每擊傷害","towerDamage",draft.towerDamage,v=>draft.towerDamage=v);
            Milliseconds(towers,"塔共用攻擊間隔（ms）","towerInterval",()=>draft.towerCooldownTicks,v=>draft.towerCooldownTicks=v);
            var table=root.Q("troopTable");var heading=new VisualElement();heading.AddToClassList("balance-row");heading.AddToClassList("balance-heading");
            string[] titles={"兵種","費用","生命","每擊傷害","攻擊間隔(ms)","每秒傷害*","移速","攻擊距離","部署(ms)"};
            for(int i=0;i<titles.Length;i++){var label=new Label(titles[i]);label.AddToClassList(i==0?"balance-name":"balance-cell");heading.Add(label);}table.Add(heading);
            foreach(var card in draft.cards)
            {
                if(card.IsSpell){Spell(root.Q("spellFields"),card);continue;}
                var row=new VisualElement();row.AddToClassList("balance-row");table.Add(row);
                var name=new Label(card.name);name.AddToClassList("balance-name");row.Add(name);
                Number(row,null,card.id+"Cost",card.cost,v=>card.cost=v);
                Integer(row,null,card.id+"HP",card.hp,v=>card.hp=v);
                Integer(row,null,card.id+"Damage",card.damage,v=>card.damage=v);
                Milliseconds(row,null,card.id+"Interval",()=>card.cooldown,v=>card.cooldown=v);
                var dps=new Label{name=card.id+"Dps"};dps.AddToClassList("balance-cell");dps.AddToClassList("balance-dps");row.Add(dps);
                Number(row,null,card.id+"Speed",card.speed,v=>card.speed=v);
                Number(row,null,card.id+"Range",card.range,v=>card.range=v);
                Milliseconds(row,null,card.id+"Deploy",()=>card.deployTicks,v=>card.deployTicks=v);
                var details=new Foldout{text=card.name+" · "+card.description,value=false};details.AddToClassList("balance-detail");root.Q("troopDetails").Add(details);
                var fields=new VisualElement();fields.AddToClassList("balance-fields");details.Add(fields);
                Milliseconds(fields,"攻擊前搖（ms）",card.id+"Windup",()=>card.windupTicks,v=>card.windupTicks=v);
                Number(fields,"索敵距離",card.id+"Aggro",card.aggro,v=>card.aggro=v);
                var count=new DropdownField("每次出兵數量",new List<string>{"1","3"},card.count==3?1:0){name=card.id+"Count"};
                count.RegisterValueChangedCallback(e=>Change(()=>card.count=e.newValue=="3"?3:1));fields.Add(count);
                if(card.id=="bomber")Number(fields,"爆炸傷害半徑",card.id+"Splash",card.splash,v=>card.splash=v);
                if(card.id=="charger")
                {
                    Number(fields,"衝鋒累積距離",card.id+"ChargeDistance",card.chargeDistance,v=>card.chargeDistance=v);
                    Number(fields,"衝鋒傷害倍率",card.id+"ChargeMultiplier",card.chargeMultiplier,v=>card.chargeMultiplier=v);
                }
                fields.Add(new Label("目標："+(card.target=="buildings"?"僅建築":"部隊與建築")));
            }
            table.Add(new Label("* 每秒傷害為單體持續攻擊的估算；小隊顯示全員總和，不含衝鋒倍率或範圍多目標。"));
            RefreshState();
        }
        private void Spell(VisualElement parent,CardDefinition card)
        {
            var foldout=new Foldout{text=card.name,value=true};foldout.AddToClassList("balance-detail");parent.Add(foldout);
            var fields=new VisualElement();fields.AddToClassList("balance-fields");foldout.Add(fields);
            Number(fields,"費用",card.id+"Cost",card.cost,v=>card.cost=v);
            Number(fields,"作用半徑",card.id+"Radius",card.radius,v=>card.radius=v);
            Milliseconds(fields,"施放延遲（ms）",card.id+"Delay",()=>card.delayTicks,v=>card.delayTicks=v);
            Milliseconds(fields,"持續時間（ms）",card.id+"Duration",()=>card.durationTicks,v=>card.durationTicks=v);
            if(card.id=="fireball")
            {
                Integer(fields,"傷害",card.id+"Damage",card.damage,v=>card.damage=v);
                Number(fields,"對塔傷害比例（%）",card.id+"TowerScale",card.towerScale*100,v=>card.towerScale=v/100);
            }
            else Number(fields,"剩餘移速／攻速（%）",card.id+"Slow",card.slow*100,v=>card.slow=v/100);
        }
        private void Integer(VisualElement parent,string label,string name,int value,Action<int> set)
        {
            var field=new IntegerField(label){name=name,value=value,isDelayed=true};
            if(label==null)field.AddToClassList("balance-cell");
            field.RegisterValueChangedCallback(e=>Change(()=>set(e.newValue)));parent.Add(field);
        }
        private void Number(VisualElement parent,string label,string name,float value,Action<float> set)
        {
            var field=new FloatField(label){name=name,value=value,isDelayed=true};
            if(label==null)field.AddToClassList("balance-cell");
            field.RegisterValueChangedCallback(e=>Change(()=>set(e.newValue)));parent.Add(field);
        }
        private void Milliseconds(VisualElement parent,string label,string name,Func<int> get,Action<int> set)
        {
            var field=new IntegerField(label){name=name,value=ToMilliseconds(get()),isDelayed=true};
            if(label==null)field.AddToClassList("balance-cell");
            field.RegisterValueChangedCallback(e=>{Change(()=>set(Mathf.RoundToInt(e.newValue*30f/1000)));field.SetValueWithoutNotify(ToMilliseconds(get()));});parent.Add(field);
        }
        internal static int ToMilliseconds(int ticks)=>Mathf.RoundToInt(ticks*1000f/30);
        private void Change(Action edit){Undo.RecordObject(this,"調整雙橋對決數值");edit();RefreshState();}
        private void RefreshState()
        {
            if(draft==null)return;
            var root=rootVisualElement;
            string error=Validate(draft);
            hasUnsavedChanges=Serialize(draft)!=originalDraft;
            bool playing=EditorApplication.isPlayingOrWillChangePlaymode;
            root.Q("towerFields").SetEnabled(!playing);root.Q("troopTable").SetEnabled(!playing);
            root.Q("troopDetails").SetEnabled(!playing);root.Q("spellFields").SetEnabled(!playing);
            root.Q<Button>("saveButton").SetEnabled(error==null&&!playing);
            root.Q<Button>("restartButton").SetEnabled(error==null&&!playing);
            root.Q<Button>("stopButton").SetEnabled(!playing&&TowerDuelBalanceServer.IsRunning);
            root.Q<Button>("sceneButton").SetEnabled(!playing);
            root.Q<Button>("reloadButton").text=hasUnsavedChanges?"捨棄並重新載入":"重新載入";
            root.Q<Label>("serverLabel").text=TowerDuelBalanceServer.Status;
            foreach(var card in draft.cards)
            {
                var label=root.Q<Label>(card.id+"Dps");if(label!=null)label.text=card.cooldown>0?(card.damage*card.count*30f/card.cooldown).ToString("0.0"):"—";
            }
            ShowMessage(playing?"請先停止 Play 再調整數值。":error??(hasUnsavedChanges?"有尚未儲存的修改。":"設定已與檔案同步。"),error!=null);
        }
        private void Run(Action action){try{action();}catch(Exception error){ShowMessage(error.Message,true);Debug.LogWarning("Tower Duel balance: "+error.Message);}}
        private void ShowMessage(string message,bool error=false){var label=rootVisualElement.Q<Label>("messageLabel");label.text=message;label.EnableInClassList("invalid",error);}
        private void SaveDraft()
        {
            string error=Validate(draft);if(error!=null)throw new InvalidOperationException(error);
            string json=Serialize(draft);
            WriteRules(CanonicalPath,originalJson,json);
            originalJson=json;originalDraft=json;
            AssetDatabase.ImportAsset("Assets/Resources/TowerDuel/rules.json",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            foreach(var card in draft.cards)
            {
                if(card.IsSpell)continue;
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Resources/TowerDuel/Units/"+card.id+".controller");
                if(!controller)continue;
                foreach(var child in controller.layers[0].stateMachine.states)if(child.state.name=="Attack"&&child.state.motion is AnimationClip clip)
                {child.state.speed=clip.length/(card.cooldown/30f);EditorUtility.SetDirty(child.state);}
            }
            AssetDatabase.SaveAssets();RefreshState();ShowMessage("共用規則已儲存；Go 後端與 Unity 共用此檔，本機後端重啟後套用新數值。");
        }
        public override void SaveChanges(){SaveDraft();base.SaveChanges();}
        internal static string Serialize(DuelRules rules)=>JsonUtility.ToJson(rules,true)+"\n";
        internal static void WriteRules(string path,string expectedJson,string json)
        {
            if(File.ReadAllText(path)!=expectedJson)
                throw new InvalidOperationException("設定檔已由其他地方修改；請先重新載入，避免覆蓋新內容。");
            var encoding=new UTF8Encoding(false);
            File.WriteAllText(path,json,encoding);
        }
        internal static string Validate(DuelRules rules)
        {
            if(rules==null||rules.schema!=3||string.IsNullOrWhiteSpace(rules.id)||rules.duration!=180||rules.cards==null||rules.cards.Length!=8)return "共用規則格式不正確。";
            if(rules.towerHP<=0||rules.keepHP<=0||rules.towerDamage<0||rules.towerCooldownTicks<1)return "塔的血量與攻擊間隔必須大於零，傷害不可為負。";
            var ids=new HashSet<string>();
            foreach(var c in rules.cards)
            {
                if(c==null||string.IsNullOrEmpty(c.id)||!ids.Add(c.id))return "兵種／法術代碼不正確。";
                string prefix=c.name+"：";
                if(!DuelMap.Finite(c.cost)||c.cost<=0||c.cost>10)return prefix+"費用需大於 0 且不超過 10。";
                if(c.IsSpell)
                {
                    if(!DuelMap.Finite(c.radius)||c.radius<=0||c.radius>5||c.damage<0||!DuelMap.Finite(c.slow)||c.slow<0||c.slow>=1||!DuelMap.Finite(c.towerScale)||c.towerScale<0||c.towerScale>1||(c.damage==0&&c.slow==0))return prefix+"請檢查半徑、傷害及比例。";
                    if(c.delayTicks<0||c.delayTicks>90||c.durationTicks<1||c.durationTicks>300)return prefix+"施放延遲需為 0～3000 ms，持續時間需大於 0 且不超過 10000 ms。";
                }
                else
                {
                    if(c.count!=1&&c.count!=3)return prefix+"每次出兵數量需為 1 或 3。";
                    if(c.hp<=0||c.damage<=0||c.cooldown<1)return prefix+"血量、傷害及攻擊間隔需大於零。";
                    if(c.windupTicks<1||c.windupTicks>=c.cooldown)return prefix+"前搖需大於零，且短於攻擊間隔。";
                    if(c.deployTicks<0||c.deployTicks>300)return prefix+"部署時間需為 0～10000 ms。";
                    if(!DuelMap.Finite(c.speed)||c.speed<=0||!DuelMap.Finite(c.range)||c.range<=0||!DuelMap.Finite(c.aggro)||c.aggro<c.range)return prefix+"移速與射程需大於零，索敵距離不可小於射程。";
                    if(!DuelMap.Finite(c.splash)||c.splash<0||c.splash>5||!DuelMap.Finite(c.chargeDistance)||c.chargeDistance<0||!DuelMap.Finite(c.chargeMultiplier)||c.chargeMultiplier<0||c.chargeMultiplier>4||(c.chargeDistance>0&&c.chargeMultiplier<1))return prefix+"請檢查範圍傷害與衝鋒設定。";
                }
            }
            return null;
        }
    }

    [InitializeOnLoad]
    internal static class TowerDuelBalanceServer
    {
        [Serializable] internal sealed class Record {public int pid,port;public string startTicks,executable,rulesHash,error;}
        private static string Key=>"TowerDuel.BalanceServer."+TowerDuelBalanceWindow.ExampleRoot;
        private static string Executable=>Path.Combine(TowerDuelBalanceWindow.ExampleRoot,"Builds/BalanceServer/18110/tower-duel-balance-server.exe");
        static TowerDuelBalanceServer(){EditorApplication.quitting+=()=>{try{Stop();}catch(Exception error){Debug.LogWarning(error.Message);}};}
        private static Process Owned(out Record record)
        {
            record=null;string saved=SessionState.GetString(Key,"");if(saved=="")return null;
            try
            {
                record=JsonUtility.FromJson<Record>(saved);var process=Process.GetProcessById(record.pid);
                if(!process.HasExited&&process.StartTime.ToUniversalTime().Ticks.ToString()==record.startTicks&&string.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(Executable),StringComparison.OrdinalIgnoreCase))return process;
                process.Dispose();
            }
            catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            SessionState.EraseString(Key);return null;
        }
        internal static bool IsRunning {get{using(var process=Owned(out _))return process!=null;}}
        internal static string Status
        {
            get
            {
                using(var process=Owned(out var record))
                {
                    if(process==null)return "本機後端：未啟動 · http://127.0.0.1:18110";
                    using(var hash=SHA256.Create())
                    {
                        string current=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(TowerDuelBalanceWindow.CanonicalPath))).Replace("-","").ToLowerInvariant();
                        return "本機後端：運作中 · "+(current==record.rulesHash?"已套用儲存的數值":"有新數值等待重啟套用")+" · http://127.0.0.1:18110";
                    }
                }
            }
        }
        internal static void Stop()
        {
            using(var process=Owned(out _))if(process!=null){process.Kill();if(!process.WaitForExit(5000))throw new InvalidOperationException("本機後端尚未停止，請稍後再試。");}
            SessionState.EraseString(Key);
        }
        internal static void Restart()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("請先停止 Play 再重啟本機後端。");
            Stop();
            string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell/7/pwsh.exe");
            if(!File.Exists(shell))
            {
                shell="powershell.exe";
                foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
                {
                    if(string.IsNullOrWhiteSpace(directory))continue;
                    string candidate=Path.Combine(directory.Trim('"'),"pwsh.exe");
                    if(File.Exists(candidate)){shell=candidate;break;}
                }
            }
            string script=Path.Combine(TowerDuelBalanceWindow.ExampleRoot,"scripts/Start-BalanceServer.ps1");
            string resultPath=Path.Combine(TowerDuelBalanceWindow.ExampleRoot,"Builds/BalanceServer/18110/launch-result.json");
            if(File.Exists(resultPath))File.Delete(resultPath);
            var info=new ProcessStartInfo(shell,"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \""+script+"\""){
                UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=TowerDuelBalanceWindow.ExampleRoot};
            EditorUtility.DisplayProgressBar("雙橋對決本機後端","正在建置後端並載入共用規則…",.5f);
            try
            {
                using(var build=Process.Start(info))
                {
                    if(!build.WaitForExit(60000)){build.Kill();throw new InvalidOperationException("本機後端建置超時；請查看 Builds/BalanceServer/18110/build.log。");}
                    var record=File.Exists(resultPath)?JsonUtility.FromJson<Record>(File.ReadAllText(resultPath)):null;
                    if(build.ExitCode!=0)throw new InvalidOperationException(record?.error??"後端啟動失敗，請查看 Builds/BalanceServer/18110 的日誌。");
                    if(record==null||record.pid<=0||record.port!=18110||!string.Equals(Path.GetFullPath(record.executable),Path.GetFullPath(Executable),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("後端啟動結果不正確。");
                    SessionState.SetString(Key,JsonUtility.ToJson(record));
                    if(!IsRunning)throw new InvalidOperationException("本機後端啟動後立即停止，請查看 server-error.log。");
                }
            }
            finally{EditorUtility.ClearProgressBar();}
        }
    }
}
