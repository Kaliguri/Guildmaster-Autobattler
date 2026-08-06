using Guildmaster.DevTools;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.UI.EditorTools
{
    /// <summary>Пункт меню, снимающий контактный лист состояний интерфейса.</summary>
    /// <remarks>
    /// Лист снимается ТОЛЬКО из play: вне игры живой панели не существует, а снимок с изолированного
    /// стенда отвечает на другой вопрос (решение Макса 05.08.2026). Поэтому вне play пункт не молчит
    /// и не делает вид, что сработал, — он объясняет, чего не хватает.
    /// </remarks>
    public static class UiContactSheetMenu
    {
        [MenuItem("Alebardium/UI/Contact Sheet", priority = 100)]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[ContactSheet] Нужен play mode: войди в игру, встань на любой экран и " +
                               "позови пункт снова. Вне play панели интерфейса не существует.");
                return;
            }

            var host = new GameObject("~UiContactSheetRunner") { hideFlags = HideFlags.HideAndDontSave };
            host.AddComponent<UiContactSheetRunner>().Begin();
        }
    }
}
