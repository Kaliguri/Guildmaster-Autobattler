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
        // Полный круг — то, что скилл называет «прогони баланс»: сравнение честно только на одних линзах.
        // Состав и порядок берутся из BalanceRound, того же, что читает командная строка.
        [MenuItem("Alebardium/Balance/Полный круг — все бенчи по порядку", priority = 90)]
        private static void FullRound()
        {
            var failed = new System.Collections.Generic.List<string>();
            foreach (BalanceRound.Step step in BalanceRound.Steps)
            {
                try
                {
                    double t0 = EditorApplication.timeSinceStartup;
                    (string csv, string md) = step.Run();
                    Debug.Log($"[SimBench] {step.Title}: готово за {EditorApplication.timeSinceStartup - t0:0.0} с." +
                              $"\nCSV: {csv}\nMD:  {md}");
                }
                catch (Exception e)
                {
                    failed.Add(step.Key);
                    Debug.LogError($"[SimBench] {step.Title} — ошибка: {e}");
                }
            }

            // Сайт собирается один раз в конце: одиннадцать пересборок подряд не дают ничего нового.
            BalanceSite.Rebuild();
            if (failed.Count > 0) Debug.LogError($"[SimBench] Круг неполный, упали: {string.Join(", ", failed)}.");
            else Debug.Log("[SimBench] Круг прогнан целиком.");
        }

        [MenuItem("Alebardium/Balance/0. Audit Content", priority = 100)]
        private static void Audit() => RunReport("Аудит контента", ContentAuditor.Run);

        [MenuItem("Alebardium/Balance/0. Class Norms (линейка коридоров)", priority = 101)]
        private static void Norms() => RunReport("Классовые нормы", BalanceNorms.Run);

        [MenuItem("Alebardium/Balance/0. Content Cards (имена, описания, способности)", priority = 102)]
        private static void Cards() => RunReport("Карточки контента", ContentCards.Run);

        // PvE-линза идёт ПЕРЕД круговыми форматами намеренно: игра — PvE, и «прошёл ли бой» это главный
        // вопрос, а «кто сильнее в зеркале» — вспомогательный.
        [MenuItem("Alebardium/Balance/1. Encounter Bench — PvE (отряд против энкаунтеров)", priority = 110)]
        private static void Encounters() => RunReport("Энкаунтеры (PvE)", EncounterBench.Run);

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

        // Разбор одного боя событиями: выделение решает, какой это бой (мементо + энкаунтер, две
        // мементо или сценарий). Отвечает на «почему», на который агрегатные таблицы не отвечают.
        [MenuItem("Alebardium/Balance/Трейс выделенного — лента одного боя", priority = 181)]
        private static void Trace()
        {
            try
            {
                string md = TraceBench.RunSelection(Selection.objects);
                if (md != null) Debug.Log($"[SimBench] Лента боя записана:\n{md}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimBench] Трейс боя — ошибка: {e}");
            }
        }

        [MenuItem("Alebardium/Balance/Трейс выделенного — лента одного боя", validate = true)]
        private static bool TraceValidate() => TraceBench.CanTrace(Selection.objects);

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
