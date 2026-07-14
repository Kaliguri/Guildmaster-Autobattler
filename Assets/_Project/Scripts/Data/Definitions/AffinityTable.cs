namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Таблица сродств: множитель урона по паре (сродство урона × тип существа цели).
    /// Это ДИЗАЙН-ПРАВИЛА, а не числа баланса юнитов, поэтому таблица статическая и детерминированная —
    /// никакого SO в тике сима (ГДД «8» §«Школа vs сродство»).
    /// <para>
    /// Правила: Нежить и Конструкты иммунны к Яду; Нежить и Демоны уязвимы к Свету; Живое уязвимо к Тьме.
    /// Всё остальное — нейтрально (×1). Множитель НЕ гасится бронёй и применяется в том числе к True-урону.
    /// </para>
    /// </summary>
    public static class AffinityTable
    {
        /// <summary>Множитель для «уязвим» (+30%).</summary>
        public const float VulnerableMult = 1.3f;

        /// <summary>Множитель для «иммунен» (урон полностью гасится).</summary>
        public const float ImmuneMult = 0f;

        /// <summary>Нейтральная пара — урон без изменений.</summary>
        public const float NeutralMult = 1f;

        /// <summary>Множитель урона со сродством <paramref name="affinity"/> по цели типа <paramref name="target"/>.</summary>
        public static float Multiplier(DamageAffinity affinity, CreatureType target)
        {
            switch (affinity)
            {
                case DamageAffinity.Poison:
                    // Яд не действует на то, у чего нет живой физиологии.
                    return target == CreatureType.Undead || target == CreatureType.Construct
                        ? ImmuneMult
                        : NeutralMult;

                case DamageAffinity.Light:
                    return target == CreatureType.Undead || target == CreatureType.Demon
                        ? VulnerableMult
                        : NeutralMult;

                case DamageAffinity.Dark:
                    return target == CreatureType.Living
                        ? VulnerableMult
                        : NeutralMult;

                default:
                    return NeutralMult;
            }
        }
    }
}
