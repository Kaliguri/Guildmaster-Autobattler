using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Вход стенда из командной строки: <c>Unity -batchmode -executeMethod
    /// Guildmaster.Balance.Editor.BalanceCli.Run</c>. Гоняет круг бенчей без открытого редактора и
    /// возвращает код выхода, по которому видно, состоялся прогон или нет.
    /// </summary>
    /// <remarks>
    /// <para>Зачем: прогон, который умеет только человек в открытом редакторе, нельзя ни повторить, ни
    /// поставить в CI, ни прогнать, пока в редакторе идёт другая работа. Обёртка вокруг
    /// <see cref="BalanceRound"/> — состав круга здесь не дублируется.</para>
    /// <para>Аргумент <c>-benches</c> принимает список ключей через запятую или <c>all</c>. Неизвестный
    /// ключ роняет прогон с кодом 2: заказали одно, померили другое — худший исход для стенда.</para>
    /// <para>Код выхода: 0 — весь круг прошёл, 1 — хотя бы один бенч упал, 2 — не разобраны аргументы.
    /// Сайт собирается ОДИН раз в конце (а не после каждого бенча, как из меню): в пакетном прогоне
    /// пересобирать его одиннадцать раз бессмысленно.</para>
    /// </remarks>
    public static class BalanceCli
    {
        private const string BenchesArg = "-benches";

        public static void Run()
        {
            IReadOnlyList<BalanceRound.Step> steps;
            string requested = ArgValue(BenchesArg);
            try
            {
                steps = BalanceRound.Select(requested);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[BalanceCli] {e.Message}");
                EditorApplication.Exit(2);
                return;
            }

            Debug.Log($"[BalanceCli] Круг из {steps.Count} бенчей: {requested ?? "all"}.");

            var failed = new List<string>();
            foreach (BalanceRound.Step step in steps)
            {
                try
                {
                    double t0 = EditorApplication.timeSinceStartup;
                    (string csv, string md) = step.Run();
                    double secs = EditorApplication.timeSinceStartup - t0;
                    Debug.Log($"[BalanceCli] {step.Title}: готово за {secs:0.0} с.\nCSV: {csv}\nMD:  {md}");
                }
                catch (Exception e)
                {
                    // Круг не прерываем: остальные линзы всё ещё дадут сравнимые числа, а упавший бенч
                    // виден и в логе, и в коде выхода.
                    failed.Add(step.Key);
                    Debug.LogError($"[BalanceCli] {step.Title} — ошибка: {e}");
                }
            }

            BalanceSite.Rebuild();

            if (failed.Count > 0)
            {
                Debug.LogError($"[BalanceCli] Упали бенчи: {string.Join(", ", failed)}.");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[BalanceCli] Круг прогнан целиком.");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Разбор одного боя лентой событий: <c>-executeMethod …BalanceCli.Trace -assets "Ranger,GoblinRaid"</c>.
        /// Имена — ассетов (реликвия, энкаунтер, сценарий) в любом порядке; из редактора то же делает
        /// выделение в Project.
        /// </summary>
        public static void Trace()
        {
            string names = ArgValue("-assets");
            if (string.IsNullOrWhiteSpace(names))
            {
                Debug.LogError("[BalanceCli] Трейсу нужен -assets: имена реликвии и энкаунтера " +
                               "(или двух реликвий, или сценария) через запятую.");
                EditorApplication.Exit(2);
                return;
            }

            var found = new List<UnityEngine.Object>();
            foreach (string raw in names.Split(',', ' ', ';'))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                UnityEngine.Object asset = BalanceAssets.ResolveTraceAsset(name);
                if (asset == null)
                {
                    Debug.LogError($"[BalanceCli] Не найден ассет «{name}» (искали среди реликвий, " +
                                   "энкаунтеров и сценариев).");
                    EditorApplication.Exit(2);
                    return;
                }

                found.Add(asset);
            }

            try
            {
                string md = TraceBench.RunSelection(found.ToArray());
                if (md == null) { EditorApplication.Exit(1); return; }

                Debug.Log($"[BalanceCli] Лента боя записана.\nMD:  {md}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BalanceCli] Трейс боя — ошибка: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Значение аргумента командной строки («-benches dps,duel») или null, если не задан.</summary>
        private static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
