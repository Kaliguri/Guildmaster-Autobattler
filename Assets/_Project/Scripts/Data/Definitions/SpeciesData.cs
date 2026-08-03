using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Вид (или Подвид) врага — стат-скейл-слой каскада (ГДД «Combat - Stats» §Каскад врага, решение
    /// 2026-07-24). Несёт множители HP/скорости (и др.), общие для всех юнитов вида: Гоблины
    /// ×0.4 HP, ×1.1 скорость. Тот же контейнер используется и для Подвида (Северные гоблины и т.п.).
    /// </summary>
    /// <remarks>
    /// Каскад врага: <c>StatsConfig → Класс(база) → Вид(скейл) → Подвид(скейл) → EnemyData(флэт)</c>.
    /// Скейлы добавляются в <c>Stats</c> группами ПОСЛЕ классовой базы и ДО стат-блока юнита —
    /// обычно <see cref="ModifierOp.PercentMult"/> (перемножаются поверх базы класса). Фракция-пул к
    /// этому каскаду не относится (она — ось генерации волн), <see cref="CreatureType"/> — тоже
    /// отдельная ось (сродства).
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Species", fileName = "Species")]
    public sealed class SpeciesData : ContentDefinition
    {
        [Tooltip("Стат-скейлы вида поверх классовой базы. Обычно PercentMult (Гоблины: MaxHP ×0.4 = PercentMult -0.6; MoveSpeed ×1.1 = PercentMult +0.1).")]
        [SerializeField] private StatModifier[] _scalers = Array.Empty<StatModifier>();

        public StatModifier[] Scalers => _scalers;
    }
}
