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
    }
}
