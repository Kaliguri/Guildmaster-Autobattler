#if MCP_FOR_UNITY
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Guildmaster.Balance.Editor.Agent
{
    /// <summary>
    /// Балансный стенд как инструмент агента: прогон круга бенчей (<c>balance_bench</c>) и разбор
    /// одного боя лентой событий (<c>battle_trace</c>).
    /// </summary>
    /// <remarks>
    /// <para>Зачем отдельный вход, когда есть меню и командная строка. Меню агент дёргает через
    /// <c>execute_menu_item</c> — а тот умеет только НАЖАТЬ: ни выбрать бенчи, ни получить ответ,
    /// остаётся гадать по консоли, его ли это строка. Командную строку из открытого редактора не
    /// позвать вовсе. Тул закрывает обе дыры: параметры на входе, структурированный ответ на выходе.</para>
    /// <para><b>Не обёртка над <see cref="BalanceCli"/>, а его ровесник.</b> CLI заканчивает работу
    /// через <c>EditorApplication.Exit</c> — позвать его из редактора значит убить редактор вместе с
    /// несохранённой работой Макса. Поэтому оба входа стоят на одном ядре (<see cref="BalanceRound"/>,
    /// <see cref="TraceBench"/>), а состав круга не дублируется ни здесь, ни там.</para>
    /// <para>Прогон идёт синхронно и может занять минуты. Поллинг сознательно не включаем: круг
    /// пишет отчёты по ходу, и оборванный поллингом прогон оставил бы половину отчётов свежими, а
    /// половину — вчерашними. Это худший исход для стенда: числа сравнимы на вид и несравнимы по
    /// сути.</para>
    /// </remarks>
    [McpForUnityTool("balance_bench",
        Description = "Прогнать бенчи балансного стенда Guildmaster и вернуть пути к отчётам. " +
                      "Параметр benches — ключи через запятую или all.")]
    public static class BalanceBenchTool
    {
        /// <summary>
        /// Схема параметров для агента. Имена свойств уходят в схему <b>как есть</b>
        /// (<c>ToolDiscoveryService.ExtractParameters</c> берёт <c>prop.Name</c> без приведения к
        /// snake_case), поэтому пишутся строчными — иначе агент увидит <c>Benches</c>.
        /// </summary>
        public class Parameters
        {
            [ToolParameter("Ключи бенчей через запятую или 'all'. Пусто — весь круг.",
                Required = false, DefaultValue = "all")]
            public string benches { get; set; }

            [ToolParameter("Пересобрать сайт отчётов после круга.",
                Required = false, DefaultValue = "true")]
            public bool rebuild_site { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string requested = @params?["benches"]?.ToString();
            bool rebuildSite = @params?["rebuild_site"]?.ToObject<bool?>() ?? true;

            IReadOnlyList<BalanceRound.Step> steps;
            try
            {
                steps = BalanceRound.Select(requested);
            }
            catch (ArgumentException e)
            {
                // Заказали одно, померили другое — худший исход для стенда, поэтому неизвестный ключ
                // роняет прогон, а не выкидывается молча.
                return new ErrorResponse($"unknown_bench: {e.Message}", new { known = BalanceRound.Keys });
            }

            var reports = new List<object>(steps.Count);
            var failed = new List<string>();

            foreach (BalanceRound.Step step in steps)
            {
                try
                {
                    double t0 = EditorApplication.timeSinceStartup;
                    (string csv, string md) = step.Run();
                    reports.Add(new
                    {
                        key = step.Key,
                        title = step.Title,
                        seconds = Math.Round(EditorApplication.timeSinceStartup - t0, 1),
                        csv,
                        md
                    });
                }
                catch (Exception e)
                {
                    // Круг не прерываем: остальные линзы всё ещё дадут сравнимые числа. Тот же выбор,
                    // что и в BalanceCli, — иначе один упавший бенч обесценил бы весь прогон.
                    failed.Add(step.Key);
                    reports.Add(new { key = step.Key, title = step.Title, error = e.Message });
                }
            }

            if (rebuildSite) BalanceSite.Rebuild();

            var data = new { requested = requested ?? "all", reports, failed };
            return failed.Count > 0
                ? new ErrorResponse($"benches_failed: {string.Join(", ", failed)}", data)
                : new SuccessResponse($"Круг из {steps.Count} бенчей прогнан.", data);
        }
    }

    /// <inheritdoc cref="BalanceBenchTool"/>
    [McpForUnityTool("battle_trace",
        Description = "Разобрать один бой Guildmaster лентой событий по тикам. " +
                      "Параметр assets — имена реликвии и энкаунтера (или двух реликвий, или сценария).")]
    public static class BattleTraceTool
    {
        /// <inheritdoc cref="BalanceBenchTool.Parameters"/>
        public class Parameters
        {
            [ToolParameter("Имена ассетов через запятую в любом порядке: реликвия + энкаунтер, " +
                           "две реликвии или один сценарий.")]
            public string assets { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string names = @params?["assets"]?.ToString();
            if (string.IsNullOrWhiteSpace(names))
                return new ErrorResponse("assets_required: нужны имена реликвии и энкаунтера " +
                                         "(или двух реликвий, или сценария) через запятую.");

            var found = new List<UnityEngine.Object>();
            foreach (string raw in names.Split(',', ' ', ';'))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                UnityEngine.Object asset = BalanceAssets.ResolveTraceAsset(name);
                if (asset == null)
                    return new ErrorResponse($"asset_not_found: «{name}» — искали среди реликвий, " +
                                             "энкаунтеров и сценариев.");

                found.Add(asset);
            }

            try
            {
                string md = TraceBench.RunSelection(found.ToArray());
                if (md == null)
                    return new ErrorResponse("trace_refused: из этого набора ассетов бой не собирается.");

                return new SuccessResponse("Лента боя записана.", new { md });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"trace_failed: {e.Message}");
            }
        }
    }
}
#endif
