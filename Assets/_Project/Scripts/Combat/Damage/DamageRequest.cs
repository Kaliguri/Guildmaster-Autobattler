using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Входные данные пайплайна урона. Чистая структура — никакого состояния или ссылок на сервисы
    /// (вики «10» §5.4). <see cref="DamagePipeline.Execute"/> мутирует HP/Shield цели.
    /// </summary>
    public readonly struct DamageRequest
    {
        /// <summary>Источник урона (для чтения DamageDealtEff, PhysPen/MagicPen и lifesteal).</summary>
        public readonly RuntimeUnit Source;

        /// <summary>Цель урона.</summary>
        public readonly RuntimeUnit Target;

        /// <summary>Базовый урон до модификаторов пайплайна.</summary>
        public readonly float RawDamage;

        /// <summary>Тип урона определяет, какая броня используется (Physical/Magic/True).</summary>
        public readonly DamageType DamageType;

        /// <summary>Константа K из StatsConfig (mult = K / (K + effArmor)).</summary>
        public readonly float ArmorK;

        public DamageRequest(
            RuntimeUnit source,
            RuntimeUnit target,
            float rawDamage,
            DamageType damageType,
            float armorK)
        {
            Source     = source;
            Target     = target;
            RawDamage  = rawDamage;
            DamageType = damageType;
            ArmorK     = armorK;
        }
    }
}
