namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Режим выбора цели (один дропдаун, не упорядоченный список — решение Макса, вики «13» §2.1).
    /// Используется и авто-атакой (<see cref="AIProfile.AutoAttackTargeting"/>), и активками
    /// (<see cref="AbilityData"/>). Скоринг и тай-брейки — §4.2 (тай-брейк всегда по <c>Id</c>, без RNG).
    /// </summary>
    public enum TargetingMode
    {
        /// <summary>Ближайший враг (скор = dist²). Дефолт.</summary>
        Nearest = 0,

        /// <summary>Враг с наименьшим абсолютным HP (добить).</summary>
        LowestHpFlat = 1,

        /// <summary>Враг с наименьшим HP% (CurrentHP / MaxHP).</summary>
        LowestHpPercent = 2,

        /// <summary>Враг с наибольшим HP (танк/жирная цель).</summary>
        HighestHp = 3,

        /// <summary>Предпочесть врага с тегом <see cref="AIProfile.TargetTag"/> (Marked/Frozen); иначе ближайший.</summary>
        PreferTagged = 4,

        /// <summary>Оценочный «главный ДПС»: AutoAttackDamage × AttackSpeed × DamageDealtEff (Защитнику, §2.1). Без истории урона.</summary>
        HighestThreat = 5,

        /// <summary>Ближайший союзник (хил/бафф-режим).</summary>
        AllyNearest = 6,

        /// <summary>Союзник с наименьшим HP% (хилер).</summary>
        AllyLowestHpPercent = 7,

        /// <summary>Избегать врага с тегом <see cref="AIProfile.TargetTag"/> (Frozen у Криоманта — не тратить АА на уже замороженного); иначе ближайший. Зеркало <see cref="PreferTagged"/>.</summary>
        PreferUntagged = 8,
    }
}
