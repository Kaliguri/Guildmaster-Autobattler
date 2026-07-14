using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Входные данные пайплайна урона. Чистая структура — никакого состояния или ссылок на сервисы
    /// (вики «10» §5.4). <see cref="DamagePipeline.Execute"/> мутирует HP/Shield цели.
    /// </summary>
    public readonly struct DamageRequest
    {
        /// <summary>Источник урона (для чтения DamageDealtEff, PhysPen/ElementalPen и lifesteal).</summary>
        public readonly RuntimeUnit Source;

        /// <summary>Цель урона.</summary>
        public readonly RuntimeUnit Target;

        /// <summary>Базовый урон до модификаторов пайплайна.</summary>
        public readonly float RawDamage;

        /// <summary>Школа урона — определяет, какая броня используется (Physical/Elemental/True).</summary>
        public readonly DamageSchool School;

        /// <summary>Константа K из StatsConfig (mult = K / (K + effArmor)).</summary>
        public readonly float ArmorK;

        /// <summary>Это урон АВТОАТАКИ (мили single/линия или снаряд-автоатака)? Урон способностей/DoT/шипов = false.
        /// Пре-дамаг реактивы, завязанные именно на автоатаку (напр. «Изворотливость» убийцы), гейтятся по этому флагу.</summary>
        public readonly bool IsAutoAttack;

        /// <summary>Сродство урона (Яд/Свет/Тьма). Бронёй не гасится — множитель по типу существа цели (<see cref="AffinityTable"/>).</summary>
        public readonly DamageAffinity Affinity;

        public DamageRequest(
            RuntimeUnit source,
            RuntimeUnit target,
            float rawDamage,
            DamageSchool school,
            float armorK,
            bool isAutoAttack = false,
            DamageAffinity affinity = DamageAffinity.None)
        {
            Source       = source;
            Target       = target;
            RawDamage    = rawDamage;
            School       = school;
            ArmorK       = armorK;
            IsAutoAttack = isAutoAttack;
            Affinity     = affinity;
        }
    }
}
