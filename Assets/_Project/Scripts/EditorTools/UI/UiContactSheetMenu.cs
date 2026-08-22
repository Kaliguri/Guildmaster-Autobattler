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

            Run(UiContactSheetRunner.Job.ContactSheet);
        }

        /// <summary>
        /// Лестница громкости: одна панель на трёх насыщенностях фона.
        /// </summary>
        /// <remarks>
        /// Отдельным пунктом, а не страницей листа: лист отвечает на «как выглядит элемент», а это —
        /// на «насколько звонко звучит интерфейс в целом». Вопросы разные, и смешивать их в одном
        /// прогоне значит каждый раз снимать лишнее.
        /// </remarks>
        [MenuItem("Alebardium/UI/Colour Ladder", priority = 101)]
        public static void CaptureLadder()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[ColourLadder] Нужен play mode: панель интерфейса живёт только в игре.");
                return;
            }

            Run(UiContactSheetRunner.Job.ColourLadder);
        }

        /// <summary>
        /// Лестница глубины: три светлоты фона, и на каждой — кнопка на фоне против кнопки на своей
        /// ступени.
        /// </summary>
        /// <remarks>
        /// Отдельным пунктом от лестницы громкости: та крутит НАСЫЩЕННОСТЬ и отвечает «насколько
        /// звонко», эта крутит СВЕТЛОТУ и отвечает «различимы ли слои». Вопросы соседние, но ответы
        /// у них разные, и мешать их в одном кадре значит не получить ни одного.
        /// </remarks>
        [MenuItem("Alebardium/UI/Lightness Ladder", priority = 102)]
        public static void CaptureLightnessLadder()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[LightnessLadder] Нужен play mode: панель интерфейса живёт только в игре.");
                return;
            }

            Run(UiContactSheetRunner.Job.LightnessLadder);
        }

        /// <summary>
        /// Кадры экранов: по одному снимку на каждый экран каталога превью.
        /// </summary>
        /// <remarks>
        /// Отдельным пунктом от контактного листа, потому что вопрос другой. Лист отвечает «как
        /// выглядит элемент», этот прогон — «как выглядит экран целиком»: разбор 23.08.2026 показал,
        /// что регрессии уровня экрана (пропал задник, разъехалась метрика, не отработан реф) не
        /// ловятся ни одним статическим гейтом и видны только на кадре.
        /// </remarks>
        [MenuItem("Alebardium/UI/Screen Sheet", priority = 103)]
        public static void CaptureScreens()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[ScreenSheet] Нужен play mode: экраны снимаются на живой панели, " +
                               "поверх настоящего задника.");
                return;
            }

            Run(UiContactSheetRunner.Job.ScreenSheet);
        }

        private static void Run(UiContactSheetRunner.Job job)
        {
            var host = new GameObject("~UiContactSheetRunner") { hideFlags = HideFlags.HideAndDontSave };
            host.AddComponent<UiContactSheetRunner>().Begin(job);
        }
    }
}
