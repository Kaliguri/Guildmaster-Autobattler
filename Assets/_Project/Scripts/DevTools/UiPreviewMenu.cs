#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Пункты меню UI-стенда (<c>Tools/UI Preview/*</c>): выставляют цель в <c>SessionState</c>, грузят сцену
    /// <c>UiPreview.unity</c> и входят в Play — <see cref="UiPreviewHost"/> собирает выбранный экран через
    /// <see cref="UiPreviewCatalog"/> со стендовыми данными (реальный контент, без боя/бута). Editor-only.
    /// </summary>
    internal static class UiPreviewMenu
    {
        private const string ScenePath = "Assets/_Project/Scenes/UiPreview.unity";
        private const string TargetKey = "gm.uiPreview.target";

        [MenuItem("Tools/UI Preview/Loadout Inventory (redesign)", priority = 0)]
        private static void LoadoutInventory() => Open("loadout-inventory");

        [MenuItem("Tools/UI Preview/Component Gallery", priority = 20)]
        private static void Gallery() => Open("gallery");

        [MenuItem("Tools/UI Preview/Loadout Hub (legacy)", priority = 21)]
        private static void LoadoutHub() => Open("loadout-hub");

        private static void Open(string id)
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            SessionState.SetString(TargetKey, id);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
