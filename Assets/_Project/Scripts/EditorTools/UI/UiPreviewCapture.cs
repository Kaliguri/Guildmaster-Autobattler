using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.UI.EditorTools
{
    /// <summary>
    /// Превью UITK-экрана в PNG одной командой — без ручного танца Play → скриншот → стоп.
    /// UITK рисуется ТОЛЬКО в Play (edit-mode offscreen-рендер недоступен: рантайм-рендер-коллбэки
    /// UITK живут лишь в плей-режиме), поэтому тул входит в Play, ждёт несколько кадров на билд/рендер,
    /// снимает Game view в фиксированный файл и выходит. Состояние держим в <see cref="SessionState"/>,
    /// чтобы пережить domain reload при входе в Play. Файл на диске корректен по цвету (в отличие от
    /// засвеченного inline-превью MCP) — читать его напрямую.
    /// <para>Использование: сделать нужную UI-сцену активной → <c>Tools/Guildmaster/UI/Capture Preview</c>
    /// (или Ctrl+Shift+U) → читать <see cref="OutputPath"/>.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class UiPreviewCapture
    {
        /// <summary>Куда пишется снимок (относительно корня проекта). Цвет корректный.</summary>
        public const string OutputPath = "Assets/Screenshots/ui-preview.png";

        private const string PendingKey = "gm.uiPreview.pending";
        private const string FrameKey = "gm.uiPreview.frame";
        private const int WaitBeforeCapture = 10; // кадров на билд + первый рендер UITK
        private const int WaitAfterCapture = 4;    // кадров на асинхронную запись файла

        static UiPreviewCapture()
        {
            // Пере-подписка после каждого domain reload (в т.ч. при входе в Play).
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        [MenuItem("Tools/Guildmaster/UI/Capture Preview %#u")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[UiPreview] Уже в Play (или переход) — сначала останови Play.");
                return;
            }

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            SessionState.SetBool(PendingKey, true);
            SessionState.SetInt(FrameKey, 0);
            Debug.Log("[UiPreview] Вход в Play для снимка → " + OutputPath);
            EditorApplication.EnterPlaymode();
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying) return; // ждём фактического входа в Play

            int frame = SessionState.GetInt(FrameKey, 0);
            SessionState.SetInt(FrameKey, frame + 1);

            if (frame == WaitBeforeCapture)
            {
                ScreenCapture.CaptureScreenshot(OutputPath, 1);
                Debug.Log("[UiPreview] Снято → " + OutputPath);
            }
            else if (frame >= WaitBeforeCapture + WaitAfterCapture)
            {
                SessionState.EraseBool(PendingKey);
                SessionState.EraseInt(FrameKey);
                EditorApplication.ExitPlaymode();
                Debug.Log("[UiPreview] Готово, выход из Play.");
            }
        }
    }
}
