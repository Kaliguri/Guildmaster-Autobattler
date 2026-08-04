using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Guildmaster.Build.Editor
{
    /// <summary>
    /// Сборка плеера из командной строки: то, что зовёт локальная выкладка в Steam
    /// (<c>scripts/steam-publish.ps1</c>).
    /// </summary>
    /// <remarks>
    /// <b>Зачем свой метод, если в CI собирает game-ci.</b> Тот генерирует такой же метод сам, внутри
    /// докер-образа; локально образа нет, а звать <c>BuildPipeline</c> из PowerShell нельзя. Здесь же
    /// живут две вещи, которые иначе разъехались бы между двумя дорогами сборки: список сцен и
    /// назначение версии.
    /// <para><b>Версия приходит аргументом и назначается ЗДЕСЬ,</b> потому что владелец версии релиза —
    /// тег, а не <c>ProjectSettings</c>. Правка безопасна: локальная выкладка гоняет теневой проект, у
    /// которого <c>ProjectSettings</c> — копия, а не ссылка на репозиторий.</para>
    /// <para><b>Сцены берутся из Build Settings</b> и только включённые: второй список сцен рядом
    /// разошёлся бы с настройками проекта на первой же новой сцене, и заметили бы это по чёрному
    /// экрану у игрока.</para>
    /// <para><b>Почему папка называется Player, а не Build:</b> корневой <c>.gitignore</c> прячет
    /// <c>[Bb]uild/</c> на любом уровне — это правило из шаблона Unity, и оно молча съело бы этот файл
    /// вместе с asmdef. Ошибка выглядела бы как «у меня работает, а из репозитория не собирается».</para>
    /// </remarks>
    public static class PlayerBuilder
    {

        /// <summary>
        /// Дефайн, включающий наши дев-инструменты в ОБЫЧНОЙ сборке: dev-консоль (F1), лог-консоль (F2)
        /// и диагностический лог за ними.
        /// </summary>
        /// <remarks>
        /// <b>Свой дефайн, а не <c>BuildOptions.Development</c></b> (04.08.2026). Билды из этого
        /// скрипта уходят в тестовую ветку Steam, где консоль нужна: без неё кооп нельзя ни включить
        /// в лог, ни посмотреть — а именно кооп ломается только на двух машинах. Development же тянет
        /// с собой водяной знак, подключение профайлера и другой профиль производительности: мы бы
        /// тестировали не ту игру, которую собираем.
        /// <para>Симптом, из-за которого это нашлось: у Макса в билде не работали F1, F2 и ~, а F3
        /// работал — витрина боёв подписана без условия, а консоли стояли под
        /// <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c>.</para>
        /// <para><b>Релизная сборка для игроков этого дефайна не получит</b> — она собирается не этим
        /// методом, и консоль в ней по-прежнему не открывается ничем.</para>
        /// </remarks>
        public const string DevToolsDefine = "GM_DEVTOOLS";
        private const string ArgOutput  = "-buildOutput";
        private const string ArgVersion = "-buildVersion";

        /// <summary>
        /// Собрать Windows-плеер. Зовётся как <c>-executeMethod Guildmaster.Build.Editor.PlayerBuilder.Windows64</c>.
        /// </summary>
        public static void Windows64()
        {
            int code = 1;
            try
            {
                code = Run();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerBuilder] Сборка упала: {e}");
            }
            finally
            {
                // Выходим сами и с честным кодом: без этого batchmode завершится нулём даже после
                // провала, и вызывающий скрипт погонит в Steam то, чего нет.
                EditorApplication.Exit(code);
            }
        }

        private static int Run()
        {
            string output = ArgValue(ArgOutput);
            if (string.IsNullOrEmpty(output))
            {
                Debug.LogError($"[PlayerBuilder] Не задан {ArgOutput} — собирать некуда.");
                return 1;
            }

            string version = ArgValue(ArgVersion);
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
                Debug.Log($"[PlayerBuilder] Версия сборки: {version}");
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[PlayerBuilder] В Build Settings нет ни одной включённой сцены — " +
                               "плеер запустится в пустоту.");
                return 1;
            }

            var options = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = output,
                target           = BuildTarget.StandaloneWindows64,
                targetGroup      = BuildTargetGroup.Standalone,
                options          = BuildOptions.None,
                extraScriptingDefines = new[] { DevToolsDefine },
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[PlayerBuilder] Итог: {summary.result}, {summary.totalSize / (1024 * 1024)} МБ, " +
                      $"{summary.totalTime.TotalSeconds:F0} с, ошибок {summary.totalErrors}");

            return summary.result == BuildResult.Succeeded ? 0 : 1;
        }

        /// <summary>Значение аргумента командной строки, идущее следом за его именем.</summary>
        private static string ArgValue(string name)
        {
            IReadOnlyList<string> args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Count - 1; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];

            return null;
        }
    }
}
