namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Канонический список боевых статов (ровно 30). Источник правды — вики
    /// «11. Стат-система (полный список статов)». Все значения присвоены явно и стабильны:
    /// Unity сериализует enum по int, поэтому порядок/номера менять НЕЛЬЗЯ (сломает .asset).
    /// </summary>
    /// <remarks>
    /// Фаза 1 реально использует 19 статов (авто-атаки без эффектов/способностей/ресурсов) —
    /// помечены [Ф1]. Остальные объявлены сразу (список финализирован), но дефолты/клампы/логика
    /// для них подключаются в своих фазах.
    /// </remarks>
    public enum StatType
    {
        // --- §3.1 Выживаемость ---
        MaxHP = 0,              // [Ф1]
        HpRegenFlat = 1,        // [Ф1]
        HpRegenPct = 2,         // [Ф1]
        PhysArmor = 3,          // [Ф1]
        MagicArmor = 4,         // [Ф1]
        DamageTakenEff = 5,     // [Ф1] PercentMult, старт 1.0
        HealShieldTakenEff = 6, // Ф2

        // --- §3.2 Атака ---
        AutoAttackDamage = 7,   // [Ф1]
        AttackSpeed = 8,        // [Ф1] атак/сек, клампится из StatsConfig
        AttackRange = 9,        // [Ф1]
        AbilityPower = 10,      // Ф2
        PhysPen = 11,           // [Ф1] плоское пробивание
        PhysPenPct = 12,        // [Ф1] % пробивание [0,1]
        MagicPen = 13,          // [Ф1]
        MagicPenPct = 14,       // [Ф1]
        DamageDealtEff = 15,    // [Ф1] PercentMult, старт 1.0
        Lifesteal = 16,         // [Ф1] % от нанесённого урона

        // --- §3.3 Исцеление / щиты ---
        HealShieldDealtEff = 17, // Ф2

        // --- §3.4 Снаряд ---
        ProjectileSpeed = 18,   // [Ф1]
        ProjectilePierce = 19,  // [Ф1] кол-во целей, целое

        // --- §3.5 Подвижность ---
        MoveSpeed = 20,         // [Ф1]
        Size = 21,              // [Ф1] старт 1.0

        // --- §3.6 Эффективность эффектов (масштабируют ТОЛЬКО длительность) ---
        ApplyBuffEff = 22,      // Ф2
        ApplyDebuffEff = 23,    // Ф2
        ReceiveBuffEff = 24,    // Ф2
        ReceiveDebuffEff = 25,  // Ф2 (= tenacity)
        CooldownEff = 26,       // Ф2

        // --- §3.7 Ресурс (только геройские реликвии) ---
        MaxResource = 27,       // Ф2
        StartResource = 28,     // Ф2 (init-only)
        ResourceGainEff = 29,   // Ф2
    }
}
