using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rumblebound.Example.Battle.Unity
{
    public static class BattleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if(SceneManager.GetActiveScene().name!="Battle"||Object.FindAnyObjectByType<BattleClient>()!=null)return;
            var root=new GameObject("Rumblebound battle",typeof(UnityGameApi),typeof(BattlePresentation),typeof(BattleClient),typeof(BattleFiles),typeof(BattleHud));
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-rf-battle-smoke")>=0)root.AddComponent<BattlePlayerVerification>();
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-rf-touch-smoke")>=0)root.AddComponent<BattleTouchVerification>();
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-rf-combat-smoke")>=0)root.AddComponent<BattleCombatVerification>();
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-rf-hud-review")>=0)root.AddComponent<BattleHudVerification>();
#endif
        }
    }
}
