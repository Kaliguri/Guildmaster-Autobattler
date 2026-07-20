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
        [MenuItem("Alebardium/Balance/0. Audit Content", priority = 100)]
        private static void Audit() => RunReport("Аудит контента", ContentAuditor.Run);

        [MenuItem("Alebardium/Balance/1. DPS Bench (all relics)", priority = 120)]
        private static void Dps() => RunReport("DPS-бенч", DpsBench.Run);

        [MenuItem("Alebardium/Balance/1. Survivability Bench (all relics)", priority = 121)]
        private static void Survivability() => RunReport("Бенч выживаемости", SurvivabilityBench.Run);

        [MenuItem("Alebardium/Balance/2. Duel Matrix + Rating (all relics)", priority = 140)]
        private static void Duel() => RunReport("Дуэльная матрица + рейтинг", DuelMatrixBench.Run);

        [MenuItem("Alebardium/Balance/Run Selected Scenario", priority = 160)]
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

        [MenuItem("Alebardium/Balance/Run Selected Scenario", validate = true)]
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
