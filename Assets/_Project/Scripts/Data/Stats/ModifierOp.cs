namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Операция стат-модификатора. Формула сборки стата:
    /// <c>final = (base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>
    /// (вики «11. Стат-система» §1, «6. Боевая модель» §3).
    /// </summary>
    public enum ModifierOp
    {
        /// <summary>Плоская прибавка к базе (складывается).</summary>
        Flat = 0,

        /// <summary>Аддитивный процент: все слагаемые суммируются в одну скобку <c>(1 + Σ)</c>.</summary>
        PercentAdd = 1,

        /// <summary>Мультипликативный процент: каждый множитель отдельный <c>(1 + x)</c>, перемножаются.</summary>
        PercentMult = 2,
    }
}
