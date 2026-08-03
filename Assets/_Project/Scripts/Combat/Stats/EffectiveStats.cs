using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Эффективные статы юнита — один путь для боя и для всех инструментов, которые показывают числа.
    /// <para>Порядок каскада здесь ровно тот же, что делает <c>RuntimeUnitFactory.Create</c>:
    /// дефолты конфига → классовая база → скейлеры вида/подвида врага → модификаторы персоны (SO).
    /// Не включает статы от пассивных <c>GrantedEffects</c>: те живут в <c>EffectSystem</c> и появляются
    /// уже в бою.</para>
    /// <para>Собран как единственный владелец после аудита 2026-07-26 (R1-48/R1-72/R1-74, T-10): каскад был
    /// переписан в трёх местах, и балансный аудитор — тот, по чьим таблицам принимаются решения — пропускал
    /// классовый слой и слой вида, то есть показывал MaxHP и MoveSpeed, которых в игре не бывает.</para>
    /// </summary>
    public static class EffectiveStats
    {
        /// <summary>
        /// Собрать стат-блок юнита поверх дефолтов конфига. <paramref name="classConfig"/> может быть null
        /// (тогда классовый слой пропускается) — но для любых показываемых игроку или дизайнеру чисел его
        /// надо подавать, иначе они разойдутся с боем.
        /// </summary>
        public static Stats Build(UnitData data, StatsConfig config, ClassBalanceConfig classConfig)
            => Build(data, vessel: null, items: null, config, classConfig);

        /// <summary>
        /// Тот же каскад плюс два слоя, которые есть у собранного игроком кита: Судьба сосуда и
        /// статовые моды надетых предметов (включая баннеры команды).
        /// </summary>
        /// <remarks>
        /// Порядок групп значим и повторяет боевую сборку: класс и вид ложатся первыми, затем персона,
        /// затем Судьба, затем предметы. Перестановка меняет результат — <c>Override</c> побеждает
        /// последний, а <c>PercentMult</c> множит уже накопленное.
        /// </remarks>
        public static Stats Build(UnitData data, VesselData vessel, IReadOnlyList<ItemData> items,
                                  StatsConfig config, ClassBalanceConfig classConfig)
        {
            var stats = new Stats(config);

            ClassBaseline.Apply(stats, data, classConfig);
            EnemyScalers.Apply(stats, data);

            if (data != null && data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);

            // Судьба авторского «Сосуда» (у процедурных её нет — они приходят без ассета).
            if (vessel != null && vessel.FateModifiers != null && vessel.FateModifiers.Length > 0)
                stats.AddModifiersFrom(vessel, vessel.FateModifiers);

            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    ItemData item = items[i];
                    if (item != null && item.Mods != null && item.Mods.Length > 0)
                        stats.AddModifiersFrom(item, item.Mods);
                }

            return stats;
        }
    }
}
