using UnityEngine;

namespace TowerDuel
{
    [ExecuteAlways]
    public sealed class DuelBattlefieldPreview : MonoBehaviour
    {
#if UNITY_EDITOR
        private DuelEnvironment preview;

        private void OnEnable()
        {
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayMode;
            UnityEditor.EditorApplication.delayCall += Show;
        }

        public DuelEnvironment Environment { get { Show(); return preview; } }

        private void Show()
        {
            if (!this || !isActiveAndEnabled || preview != null ||
                UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.BuildPipeline.isBuildingPlayer || !gameObject.scene.isLoaded) return;

            preview = new DuelEnvironment(transform, DuelMap.Load());
            foreach (var item in preview.Root.GetComponentsInChildren<Transform>(true))
                item.gameObject.hideFlags = HideFlags.DontSave;
        }

        private void HandlePlayMode(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode) Clear();
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) Show();
        }

        private void Clear()
        {
            preview?.Dispose();
            preview = null;
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.delayCall -= Show;
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayMode;
            Clear();
        }
#endif
    }
}
