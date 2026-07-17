using System;
using Guildmaster.Balance;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Пункты меню стенда баланса (<c>Tools/Balance/*</c>). Каждый гоняет бенч и пишет CSV+MD в
    /// <c>BalanceReports/</c>, логирует пути и открывает папку. Ошибки ловятся и логируются, чтобы падение
    /// одного бенча не роняло редактор.
    /// </summary>
    internal static class BalanceMenu
    {
        [MenuItem("Tools/Balance/0. Audit Content", priority = 0)]
        private static void Audit() => RunReport("Аудит контента", ContentAuditor.Run);

        [MenuItem("Tools/Balance/1. DPS Bench (all relics)", priority = 20)]
        private static void Dps() => RunReport("DPS-бенч", DpsBench.Run);

        [MenuItem("Tools/Balance/1. Survivability Bench (all relics)", priority = 21)]
        private static void Survivability() => RunReport("Бенч выживаемости", SurvivabilityBench.Run);

        [MenuItem("Tools/Balance/2. Duel Matrix + Rating (all relics)", priority = 40)]
        private static void Duel() => RunReport("Дуэльная матрица + рейтинг", DuelMatrixBench.Run);

        [MenuItem("Tools/Balance/Run Selected Scenario", priority = 60)]
        private static void RunScenario()
        {
            var scenario = Selection.activeObject as BalanceScenarioData;
            if (scenario == null)
            {
                Debug.LogWarning("[SimBench] Выдели BalanceScenarioData-ассет в Project, затем запусти.");
                return;
            }
            RunReport("Сценарий «" + scenario.name + "»", () => ScenarioBench.Run(scenario));
        }

        [MenuItem("Tools/Balance/Run Selected Scenario", validate = true)]
        private static bool RunScenarioValidate() => Selection.activeObject is BalanceScenarioData;

        private static void RunReport(string title, Func<(string csv, string md)> action)
        {
            try
            {
                double t0 = EditorApplication.timeSinceStartup;
                (string csv, string md) = action();
                double secs = EditorApplication.timeSinceStartup - t0;
                Debug.Log($"[SimBench] {title}: готово за {secs:0.0} с.\nCSV: {csv}\nMD:  {md}");
                EditorUtility.RevealInFinder(md);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimBench] {title} — ошибка: {e}");
            }
        }
    }
}
