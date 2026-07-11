namespace Guildmaster.Data.Definitions
{
    /// <summary>Редкость реликвии (GDD 5). У вертикального среза — все Common.</summary>
    public enum RelicRarity
    {
        Common = 0,
        Cursed = 1,
        Divine = 2,
    }

    /// <summary>Категория информационного тега для группировки в UI (вики «13» §3.0).</summary>
    public enum TagCategory
    {
        Role = 0,        // танк / дамагер / хилер / контроль
        DamageType = 1,  // физ / маг
        Playstyle = 2,   // агрессивный / кайт / оборонительный
        Mechanic = 3,    // особые механики (стелс, метка…)
        Other = 4,
    }

    /// <summary>Полярность трейта «Сосуда» (GDD: выбор 1 из 3, вики «13» §3.2).</summary>
    public enum TraitPolarity
    {
        Positive = 0,
        Negative = 1,
    }

    /// <summary>Вид последствия боя (GDD 9): травма снимается лечением, закалка постоянна (вики «13» §3.2).</summary>
    public enum ConsequencePolarity
    {
        Injury = 0,  // травма — снимается «Лечением» (HealCostGold)
        Mettle = 1,  // закалка — постоянна
    }

    /// <summary>Скоуп предмета (вики «13» §3.2): на персонажа или на всю команду/бой.</summary>
    public enum ItemScope
    {
        Vessel = 0,  // на персонажа, до N слотов (GameConfig.VesselItemSlots)
        Party = 1,   // на всю команду/бой
    }

    /// <summary>На кого действует модификатор забега (вики «13» §3.2, заготовка ascension/pact).</summary>
    public enum RunModTarget
    {
        Players = 0,
        Enemies = 1,
        Economy = 2,
        Rules = 3,
    }
}
