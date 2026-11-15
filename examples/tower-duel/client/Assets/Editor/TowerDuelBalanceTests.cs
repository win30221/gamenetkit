using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel.Editor
{
    public static class TowerDuelBalanceTests
    {
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        public static string Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Run balance editor tests outside Play Mode.");
            var window=ScriptableObject.CreateInstance<TowerDuelBalanceWindow>();
            try
            {
                window.Show();
                window.CreateGUI();
                var root=window.rootVisualElement;
                var rules=(DuelRules)typeof(TowerDuelBalanceWindow).GetField("draft",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                Require(root.Q("troopTable").Query<VisualElement>(className:"balance-row").ToList().Count==7,"The comparison table did not contain six troops and its header.");
                var deploy=root.Q<IntegerField>("guardDeploy");
                Require(deploy!=null&&Mathf.Abs(deploy.value-TowerDuelBalanceWindow.ToMilliseconds(rules.Find("guard").deployTicks))<.0001f,"Troop deployment was not displayed in milliseconds.");
                int editedTicks=rules.Find("guard").deployTicks==45?60:45;
                deploy.value=TowerDuelBalanceWindow.ToMilliseconds(editedTicks);
                Require(rules.Find("guard").deployTicks==editedTicks&&window.hasUnsavedChanges,"Editing milliseconds did not update the per-card tick value or dirty state: ticks="+rules.Find("guard").deployTicks+" dirty="+window.hasUnsavedChanges);
                var windup=root.Q<IntegerField>("guardWindup");int oldWindup=windup.value;
                windup.value=5000;
                Require(!root.Q<Button>("saveButton").enabledSelf&&root.Q<Label>("messageLabel").text.Contains("前搖"),"An invalid attack windup was not explained and blocked.");
                windup.value=oldWindup;
                Require(root.Q<Button>("saveButton").enabledSelf,"A corrected value did not re-enable saving.");
                string priorDps=root.Q<Label>("guardDps").text;
                int editedDamage=rules.Find("guard").damage+32;
                root.Q<IntegerField>("guardDamage").value=editedDamage;
                Require(root.Q<Label>("guardDps").text!=priorDps,"DPS comparison did not refresh after damage changed.");
                Require(TowerDuelBalanceWindow.Validate(rules)==null,"The editable round-trip configuration failed validation.");
                string output=Path.GetFullPath(Path.Combine(TowerDuelBalanceWindow.ExampleRoot,"../../artifacts/tower-duel/balance"));
                Directory.CreateDirectory(output);
                string server=Path.Combine(output,"rules-roundtrip.json"),client=Path.Combine(output,"client-roundtrip.json");
                File.WriteAllText(server,"server-before");File.WriteAllText(client,"client-before");
                string serialized=TowerDuelBalanceWindow.Serialize(rules);
                TowerDuelBalanceWindow.WritePair(server,client,"server-before","client-before",serialized);
                Require(File.ReadAllText(server)==File.ReadAllText(client),"Saving produced different client and backend rules.");
                var restored=JsonUtility.FromJson<DuelRules>(File.ReadAllText(server));
                Require(restored.Find("guard").deployTicks==editedTicks&&restored.Find("guard").damage==editedDamage&&restored.towerHP==rules.towerHP,"Saved shared rules lost a changed stat.");
                File.AppendAllText(client,"\n");bool conflict=false;
                try{TowerDuelBalanceWindow.WritePair(server,client,serialized,serialized,serialized);}
                catch(InvalidOperationException){conflict=true;}
                Require(conflict&&File.ReadAllText(server)==serialized,"Saving overwrote a concurrent edit or partly changed the shared rules.");
                File.WriteAllText(client,serialized);
                string result="TOWER_DUEL_BALANCE_TESTS PASS: UI fields, millisecond conversion, validation, DPS comparison, shared-file round trip and external-edit protection";
                File.WriteAllText(Path.Combine(output,"editor-result.json"),JsonUtility.ToJson(new Result{passed=true,result=result},true));
                return result;
            }
            finally{Undo.ClearUndo(window);window.DiscardChanges();window.Close();}
        }
        [Serializable] private sealed class Result {public bool passed;public string result;}

        public static string RunManagedBackend()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode&&!TowerDuelBalanceServer.IsRunning,"Backend test requires an idle Editor without its own managed server.");
            string original=File.ReadAllText(TowerDuelBalanceWindow.CanonicalPath);
            try
            {
                TowerDuelBalanceServer.Restart();
                Require(TowerDuelBalanceServer.IsRunning&&TowerDuelBalanceServer.Status.Contains("已套用"),"The editor did not own a running backend with current rules.");
                string key="TowerDuel.BalanceServer."+TowerDuelBalanceWindow.ExampleRoot;
                var first=JsonUtility.FromJson<TowerDuelBalanceServer.Record>(SessionState.GetString(key,""));
                using(var http=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{UseProxy=false}))
                {
                    http.Timeout=TimeSpan.FromSeconds(3);
                    var config=JsonUtility.FromJson<ServerConfig>(http.GetStringAsync("http://127.0.0.1:18110/api/config").GetAwaiter().GetResult());
                    Require(config.rulesHash==DuelRules.Load().Hash&&config.cards.Length==8,"Go did not load exactly the same rules as Unity.");
                }
                TowerDuelBalanceServer.Restart();
                var second=JsonUtility.FromJson<TowerDuelBalanceServer.Record>(SessionState.GetString(key,""));
                Require(TowerDuelBalanceServer.IsRunning&&first.pid!=second.pid,"Restart did not replace the editor-owned backend.");
                TowerDuelBalanceServer.Stop();
                Require(!TowerDuelBalanceServer.IsRunning&&File.ReadAllText(TowerDuelBalanceWindow.CanonicalPath)==original,"Backend controls leaked a process or changed game values.");
                string result="TOWER_DUEL_BALANCE_BACKEND PASS: build, start, matching Unity/Go rules hash, restart and owned-process stop";
                string output=Path.GetFullPath(Path.Combine(TowerDuelBalanceWindow.ExampleRoot,"../../artifacts/tower-duel/balance"));
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"backend-result.json"),JsonUtility.ToJson(new Result{passed=true,result=result},true));
                return result;
            }
            finally{TowerDuelBalanceServer.Stop();}
        }
        [Serializable] private sealed class ServerConfig {public string rulesHash;public CardDefinition[] cards;}
    }
}
