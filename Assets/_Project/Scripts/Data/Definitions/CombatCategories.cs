namespace Guildmaster.Data.Definitions
{
    /// <summary>Тип урона. <see cref="True"/> игнорирует броню/резист (вики «11» §2).</summary>
    public enum DamageType
    {
        Physical = 0,
        Magic = 1,
        True = 2,
    }

    /// <summary>Способ доставки автоатаки (вики «11» §2).</summary>
    public enum AttackType
    {
        /// <summary>Ближний, урон мгновенно.</summary>
        Melee = 0,

        /// <summary>Снаряд до одной цели.</summary>
        Ranged = 1,

        /// <summary>Снаряд + AOE в точке попадания.</summary>
        ProjectileAoe = 2,

        /// <summary>Пробивающий снаряд (летит сквозь цели).</summary>
        ProjectilePierce = 3,
    }

    /// <summary>Тип ресурса геройской реликвии. Реген и модель восстановления — позже (вики «11» §2, §3.7).</summary>
    public enum ResourceType
    {
        None = 0,
        Mana = 1,
        Rage = 2,
    }

    /// <summary>
    /// Полярность таймированного эффекта. Определяет, какая пара эфф-эффектов длительности
    /// применяется (Apply/Receive × Buff/Debuff) — вики «11» §5.
    /// </summary>
    public enum EffectPolarity
    {
        Buff = 0,
        Debuff = 1,
        Neutral = 2,
    }
}
