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

        [MenuItem("Alebardium/UI Preview/Loadout Inventory (redesign)", priority = 200)]
        private static void LoadoutInventory() => Open("loadout-inventory");

        [MenuItem("Alebardium/UI Preview/Party (отряд, витрина)", priority = 201)]
        private static void Party() => Open("party");

        [MenuItem("Alebardium/UI Preview/New Game (mode, lobby)", priority = 210)]
        private static void NewGame() => Open("newgame");

        [MenuItem("Alebardium/UI Preview/Guild Select (дом забега)", priority = 211)]
        private static void GuildSelect() => Open("guilds");

        [MenuItem("Alebardium/UI Preview/Hub (двор гильдии, заглушка)", priority = 212)]
        private static void Hub() => Open("hub");

        [MenuItem("Alebardium/UI Preview/Component Gallery", priority = 220)]
        private static void Gallery() => Open("gallery");

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
