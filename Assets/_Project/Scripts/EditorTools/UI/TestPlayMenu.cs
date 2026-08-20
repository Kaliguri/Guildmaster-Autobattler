using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Guildmaster.UI.EditorTools
{
    /// <summary>
    /// Дев-инструменты «пощупать игру вживую на своём экране» — под меню Alebardium/Test.
    /// Нужны, чтобы оценивать масштаб UI не арифметикой соотношений, а собственным опытом на мониторе.
    /// </summary>
    internal static class TestPlayMenu
    {
        private const string OutputDir = "Builds/Test";
        private const string ExeName = "Guildmaster-Test.exe";

        /// <summary>
        /// Собирает Windows-плеер из сцен Build Settings и сразу запускает его — единственный честный
        /// способ ощутить реальный размер интерфейса на своём мониторе, а не в Game View.
        /// Билд идёт в Builds/Test (в .gitignore). Development — ради быстрых пересборок и консоли.
        /// </summary>
        /// <remarks>
        /// Режим окна тул НЕ выставляет, хотя пункт меню обещает fullscreen: он берётся из
        /// <c>ProjectSettings</c> (сейчас там полноэкранное окно) и дальше может быть переписан игровым
        /// <c>IDisplayService</c> из <c>Local/machine.json</c> — то есть с чужой машины прилетит чужой
        /// режим. Если билд стартовал окном, дело в одном из этих двух мест, а не в сборке.
        /// </remarks>
        [MenuItem("Alebardium/Test/Build & Run (Windows, fullscreen)", priority = 500)]
        private static void BuildAndRun()
        {
            var scenes = Array.ConvertAll(
                Array.FindAll(EditorBuildSettings.scenes, s => s.enabled && !string.IsNullOrEmpty(s.path)),
                s => s.path);

            if (scenes.Length == 0)
            {
                Debug.LogError("[TestBuild] В Build Settings нет включённых сцен — собирать нечего.");
                return;
            }

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AutoRunPlayer,
            };

            var summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[TestBuild] OK: {summary.totalSize / (1024 * 1024)} MB → {options.locationPathName}");
            else
                Debug.LogError($"[TestBuild] FAILED: {summary.result} ({summary.totalErrors} errors)");
        }

        /// <summary>
        /// Разворачивает Game View на всё окно редактора (скрывает панели) и обратно. Быстрая грубая
        /// прикидка между билдами — но с полоской тулбара Unity сверху, не настоящий фулскрин.
        /// Горячая клавиша: Ctrl+Shift+G.
        /// </summary>
        [MenuItem("Alebardium/Test/Toggle Maximized Game View %#g", priority = 501)]
        private static void ToggleMaximizedGameView()
        {
            var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                Debug.LogError("[TestBuild] Не нашёл тип UnityEditor.GameView.");
                return;
            }

            var gameView = EditorWindow.GetWindow(gameViewType, false, "Game", true);
            gameView.maximized = !gameView.maximized;
        }
    }
}
