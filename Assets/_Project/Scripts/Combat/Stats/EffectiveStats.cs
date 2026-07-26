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
        {
            var stats = new Stats(config);

            ClassBaseline.Apply(stats, data, classConfig);
            EnemyScalers.Apply(stats, data);

            if (data != null && data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);

            return stats;
        }
    }
}
