namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Состояние кадровой анимации вида юнита. Живёт в Data, т.к. <see cref="UnitVisual"/> (Data) от него зависит.
    /// Порядок стабилен (не использовать как сериализуемый индекс контента).
    /// </summary>
    public enum UnitAnimationState
    {
        Idle   = 0,
        Run    = 1,
        Attack = 2,
        Death  = 3,

        /// <summary>Разбег к далёкой цели: бег с занесённым клинком. Признак — <c>IsSprinting</c> симуляции.</summary>
        Sprint = 4,

        /// <summary>
        /// Удар с разбега — первый свинг после прибытия. Отдельное состояние, а не вариант атаки:
        /// у него свой клип, начинающийся из позы бега, и свой (более короткий) замах.
        /// </summary>
        AttackCharge = 5,

        /// <summary>Оглушён: контроль вывел юнита из строя, оружие опущено. Признак — <c>CanAct = false</c>.</summary>
        Stun = 6,
    }
}
