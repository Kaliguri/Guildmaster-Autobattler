using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Пересобирает локальный сайт отчётов (<c>scripts/balance-site.py</c>) после прогона бенча
    /// и умеет открыть его в браузере.
    /// </summary>
    /// <remarks>
    /// Сборка живёт в питоне, а не в C#: вёрстку и подачу править приходится часто, и делать это
    /// через рекомпиляцию редактора — наказание. Плата за выбор — внешняя зависимость, поэтому
    /// отказ здесь ГРОМКИЙ: не нашли интерпретатор или скрипт упал — пишем в консоль. Тихо
    /// не пересобравшийся отчёт хуже отсутствующего: он показывает вчерашние числа как сегодняшние.
    /// </remarks>
    internal static class BalanceSite
    {
        private const string ScriptRelPath = "scripts/balance-site.py";
        private const int TimeoutMs = 60_000;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;
        private static string ScriptPath => Path.Combine(ProjectRoot, ScriptRelPath.Replace('/', Path.DirectorySeparatorChar));
        private static string IndexPath => Path.Combine(ProjectRoot, "BalanceReports", "site", "index.html");

        [MenuItem("Alebardium/Balance/Отчёт — пересобрать сайт", priority = 200)]
        public static void RebuildMenu()
        {
            if (Rebuild()) Debug.Log($"[BalanceSite] Сайт собран: {IndexPath}");
        }

        [MenuItem("Alebardium/Balance/Отчёт — открыть", priority = 201)]
        public static void Open()
        {
            if (!File.Exists(IndexPath) && !Rebuild()) return;

            if (File.Exists(IndexPath)) Application.OpenURL(new Uri(IndexPath).AbsoluteUri);
            else Debug.LogError($"[BalanceSite] Нечего открывать: {IndexPath} не появился после сборки.");
        }

        /// <summary>
        /// Собрать сайт. Возвращает false и жалуется в консоль, если не вышло — вызывающему бенчу
        /// падать из-за этого не нужно, его собственные CSV и Markdown уже записаны.
        /// </summary>
        public static bool Rebuild()
        {
            if (!File.Exists(ScriptPath))
            {
                Debug.LogError($"[BalanceSite] Не найден сборщик {ScriptRelPath} — сайт не пересобран.");
                return false;
            }

            foreach (string exe in new[] { "python", "python3", "py" })
            {
                if (TryRun(exe, out bool ok)) return ok;
            }

            Debug.LogError("[BalanceSite] Python не найден (пробовали python, python3, py) — сайт не пересобран. " +
                           "Отчёты CSV/Markdown записаны как обычно.");
            return false;
        }

        /// <summary>Запустить интерпретатор. <c>false</c> — такого исполняемого файла нет, пробуем следующий.</summary>
        private static bool TryRun(string exe, out bool success)
        {
            success = false;
            try
            {
                var psi = new ProcessStartInfo(exe, $"\"{ScriptPath}\"")
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using Process p = Process.Start(psi);
                if (p == null) return false;

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(TimeoutMs))
                {
                    p.Kill();
                    Debug.LogError($"[BalanceSite] Сборщик не уложился в {TimeoutMs / 1000} с и был прерван.");
                    return true;   // интерпретатор нашёлся — перебирать остальные незачем
                }

                if (p.ExitCode == 0)
                {
                    success = true;
                    if (!string.IsNullOrWhiteSpace(stdout)) Debug.Log($"[BalanceSite] {stdout.Trim()}");
                }
                else
                {
                    Debug.LogError($"[BalanceSite] Сборщик вернул код {p.ExitCode}.\n{stderr.Trim()}\n{stdout.Trim()}");
                }

                return true;
            }
            catch (Exception)
            {
                // Такого интерпретатора в PATH нет — это не ошибка, просто пробуем следующее имя.
                return false;
            }
        }
    }
}
