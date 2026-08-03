namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Размерность каждого стата (вики «11. Стат-система» §3). Держится отдельной таблицей,
    /// а не полем <see cref="StatType"/>, потому что это знание о ПОКАЗЕ, а не о механике:
    /// симуляции безразлично, проценты там или секунды.
    /// </summary>
    public static class StatKinds
    {
        /// <summary>Как показывать этот стат игроку.</summary>
        public static ValueKind KindOf(StatType stat)
        {
            switch (stat)
            {
                // Доли 0..1 — показываются процентом.
                case StatType.HpRegenPct:
                case StatType.PhysPenPct:
                case StatType.MagicPenPct:
                case StatType.Lifesteal:
                    return ValueKind.Percent;

                // Множители вокруг 1.0 — эффективности.
                case StatType.DamageTakenEff:
                case StatType.HealShieldTakenEff:
                case StatType.DamageDealtEff:
                case StatType.HealShieldDealtEff:
                case StatType.ApplyBuffEff:
                case StatType.ApplyDebuffEff:
                case StatType.ReceiveBuffEff:
                case StatType.ReceiveDebuffEff:
                case StatType.CooldownEff:
                case StatType.ResourceGainEff:
                case StatType.SummonHealthEff:
                case StatType.SummonDamageEff:
                case StatType.Size:
                    return ValueKind.Multiplier;

                // Величина в секунду.
                case StatType.AttackSpeed:
                case StatType.HpRegenFlat:
                case StatType.ProjectileSpeed:
                case StatType.MoveSpeed:
                    return ValueKind.PerSecond;

                // Мировые единицы.
                case StatType.AttackRange:
                    return ValueKind.Distance;

                // Целочисленный счёт.
                case StatType.ProjectilePierce:
                    return ValueKind.Count;

                // Остальное — абсолютные величины (HP, урон, броня, пробивание, ресурс).
                default:
                    return ValueKind.Flat;
            }
        }
    }
}
