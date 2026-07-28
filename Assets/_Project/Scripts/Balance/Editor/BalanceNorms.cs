using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Снимок КЛАССОВЫХ НОРМ — линейка, по которой сайт отчётов читает замеры. Для каждого кита пишет
    /// его боевой класс, ожидаемые по классу DPS / EHP / время до смерти и фактический MaxHP.
    /// </summary>
    /// <remarks>
    /// Отдельный снимок, а не колонки внутри каждого бенча: норма — свойство КОНТЕНТА, а не результат
    /// прогона. Считает её один владелец (<see cref="ClassBalanceConfig"/>), пишет одно место, читают
    /// все таблицы сайта — колонка «норма» появляется рядом с любой метрикой, у которой она есть, и не
    /// дублируется по бенчам. Формат — та же таблица, что у прочих отчётов: сборщик сайта узнаёт снимок
    /// по <c>kind</c> и раскладывает его в нормы, а не во вкладку режима.
    /// <para>Числа норм — дизайнерские, живут в ассете <c>ClassBalanceConfig</c> и правятся там. Код
    /// здесь только считает и записывает.</para>
    /// </remarks>
    public static class BalanceNorms
    {
        private const string Kind = "balance_norms";
        private const ulong Seed = 1UL;

        public static (string csv, string md) Run()
        {
            StatsConfig stats = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            List<RelicData> relics = BalanceAssets.LoadRelics();

            if (classes == null)
            {
                Debug.LogError("[SimBench] ClassBalanceConfig не найден — норм не будет, коридоры на сайте не появятся.");
                return (null, null);
            }

            float armorK = stats != null ? stats.ArmorConstantK : 100f;
            float band = classes.BandWidth;

            var headers = new List<string>
            {
                "Relic", "Class", "Band", "MaxHP", "HP_norm", "DPS_norm", "EHP_norm", "TTD_solo_norm", "TTD_focus3_norm",
            };
            var table = new List<IReadOnlyList<object>>();

            foreach (RelicData relic in relics)
            {
                UnitClass unitClass = relic.CombatClass;
                float ehpNorm = classes.GetEhpNorm(unitClass, armorK);
                (float hpMult, float _) = classes.GetMultipliers(unitClass);

                table.Add(new object[]
                {
                    relic.name,
                    unitClass.ToString(),
                    band,
                    ActualMaxHp(stats, relic),
                    classes.BaseHp * hpMult,
                    classes.GetDpsNorm(unitClass),
                    ehpNorm,
                    ehpNorm / SurvivabilityBench.RefDps,
                    ehpNorm / (3f * SurvivabilityBench.RefDps),
                });
            }

            string notes =
                $"**Классовые нормы** — чего ждём от кита по его роли, коридор ±{band * 100f:0}%. " +
                "Норма DPS/HP растёт из ClassBalanceConfig (эталон Брузера × множитель класса); EHP_norm — " +
                "голый запас прочности класса против физики (HP × (1 + физброня/K)), БЕЗ лечения, щитов и " +
                "уклонений: разрыв с замеренным EHP — это ровно вклад механик кита. " +
                "MaxHP — фактическое здоровье собранного юнита: расхождение с HP_norm значит, что стат-блок " +
                "персоны перекрывает классовую базу. TTD-нормы — EHP_norm, поделённый на урон эталонных " +
                $"атакующих ({SurvivabilityBench.RefDps:0} DPS каждый).";

            string csv = ReportWriter.WriteCsv(Kind, headers, table);
            string md = ReportWriter.WriteMarkdown(Kind, "SimBench — классовые нормы", headers, table, notes);
            ReportWriter.WriteJson(Kind, "SimBench — классовые нормы", headers, table, notes);
            return (csv, md);
        }

        /// <summary>
        /// Фактический MaxHP кита — через боевую фабрику, а не чтением стат-блока: только так учтётся весь
        /// каскад (класс → персона → пассивки, доложенные при рождении).
        /// </summary>
        private static float ActualMaxHp(StatsConfig stats, RelicData relic)
        {
            var env = new SimEnvironment(Seed, stats);
            RuntimeUnit unit = env.Real(relic, null, 0, Vector2.zero);
            return unit != null ? unit.Stats.Get(StatType.MaxHP) : 0f;
        }
    }
}
