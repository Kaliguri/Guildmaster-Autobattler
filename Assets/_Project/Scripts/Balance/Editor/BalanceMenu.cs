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

        [MenuItem("Alebardium/Balance/0. Class Norms (линейка коридоров)", priority = 101)]
        private static void Norms() => RunReport("Классовые нормы", BalanceNorms.Run);

        [MenuItem("Alebardium/Balance/0. Content Cards (имена, описания, способности)", priority = 102)]
        private static void Cards() => RunReport("Карточки контента", ContentCards.Run);

        [MenuItem("Alebardium/Balance/1. DPS Bench (all relics)", priority = 120)]
        private static void Dps() => RunReport("DPS-бенч", DpsBench.Run);

        [MenuItem("Alebardium/Balance/1. Survivability Bench (all relics)", priority = 121)]
        private static void Survivability() => RunReport("Бенч выживаемости", SurvivabilityBench.Run);

        [MenuItem("Alebardium/Balance/2. Duel Matrix + Rating — 1v1", priority = 140)]
        private static void Duel() => RunReport("Дуэли 1v1", DuelMatrixBench.Run);

        [MenuItem("Alebardium/Balance/2. Duel Matrix + Rating — 3v3 (trio)", priority = 141)]
        private static void TrioDuel() => RunReport("Тройки 3v3", DuelMatrixBench.RunTrio);

        [MenuItem("Alebardium/Balance/2. Duel Matrix + Rating — 4v4 (squad)", priority = 142)]
        private static void SquadDuel() => RunReport("Отряды 4v4", DuelMatrixBench.RunSquad);

        [MenuItem("Alebardium/Balance/3. Squad Swap — who to field (4v4)", priority = 160)]
        private static void SquadSwap() => RunReport("Замена в живом отряде", SquadSwapBench.Run);

        // Слэши в имени пункта Unity считает разделителями подменю — здесь они рвали пункт на три
        // вложенных уровня («2v2 » → « 4v4 » → « 6v6)»). Разделяем форматы точкой.
        [MenuItem("Alebardium/Balance/3. Pair Synergy (2v2 · 4v4 · 6v6)", priority = 161)]
        private static void PairSynergy() => RunReport("Синергия пар", PairSynergyBench.Run);

        [MenuItem("Alebardium/Balance/Run Selected Scenario", priority = 180)]
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

                // Спутники кладутся в КАЖДЫЙ прогон: линейка коридоров обязана быть той же версии, что и
                // замеры (иначе сайт сравнит сегодняшние числа с нормой, снятой до правки классового
                // профиля), а карточки — той же версии, что и контент (иначе кит переименован, а сайт
                // зовёт его по-старому). Оба дешёвые, поэтому проще писать всегда, чем угадывать когда.
                if (action != BalanceNorms.Run) BalanceNorms.Run();
                if (action != ContentCards.Run) ContentCards.Run();

                // Сайт отчётов пересобирается сам после каждого прогона — иначе он молча показывал бы
                // вчерашние числа. Отказ сборщика громкий, но прогон из-за него не считается неудачным.
                BalanceSite.Rebuild();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimBench] {title} — ошибка: {e}");
            }
        }
    }
}
