namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Класс кита реликвии (GDD 5) — БОЕВАЯ характеристика: обычный кит, проклятый (сила ценой платы),
    /// божественный. Это НЕ шанс выпадения — за него отвечает <see cref="DropRarity"/>.
    /// </summary>
    public enum KitPower
    {
        Common = 0,
        Cursed = 1,
        Divine = 2,
    }

    /// <summary>
    /// Редкость ВЫПАДЕНИЯ реликвии (экономика забега): как часто она встречается в наградах и магазине.
    /// Ортогональна <see cref="KitPower"/> — божественный кит может быть частым, а обычный редким.
    /// </summary>
    public enum DropRarity
    {
        Trash  = 0,  // мусорная — частый наполнитель витрины
        Common = 1,  // обычная — основа пула
        Unique = 2,  // уникальная — редкий приз (ramp 10/20/100% за бой/элиту/босса)
    }

    /// <summary>
    /// Ось информационного тега — она же порядок чтения карточки (ГДД <c>unit-tag-glossary</c>).
    /// Осей ровно четыре, пятой «прочее» нет: тег, которому не нашлось оси, — это тег, которому не
    /// нашлось смысла (решение Макса 2026-07-26).
    /// </summary>
    public enum TagCategory
    {
        Role = 0,        // танк / дамагер / хилер / контроль
        DamageType = 1,  // физ / маг
        Playstyle = 2,   // агрессивный / кайт / оборонительный
        Mechanic = 3,    // особые механики (стелс, метка…)
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
