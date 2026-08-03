using Guildmaster.Data.Definitions;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Переводит «чем исполнен приём» (<see cref="CastSource"/> из данных навыка) в конкретные части
    /// ЭТОГО юнита. Навык называет роль, тело отвечает своей анатомией.
    /// </summary>
    /// <remarks>
    /// Отдельная чистая функция, а не метод вида: перевод зависит только от реестра частей и роли, и
    /// проверять его надо на расстановках, которых в префабах ещё нет — два кинжала, двуручное копьё,
    /// пустые руки. Через <c>UnitView</c> такой тест потребовал бы поднять весь боевой вид.
    /// </remarks>
    public static class CastGlowMask
    {
        /// <summary>
        /// Какие части зажечь. Пустой реестр или <see cref="CastSource.None"/> — пустая маска: приём
        /// подан другими средствами.
        /// </summary>
        public static PartMask Resolve(IUnitPartLookup parts, CastSource source)
        {
            if (parts == null || source == CastSource.None) return PartMask.Empty;

            UnitPart part;
            switch (source)
            {
                case CastSource.WholeBody:
                    return parts.Everything;

                case CastSource.Shield:
                    return parts.TryGetHeld(HeldKind.Shield, out part) ? part.Mask : PartMask.Empty;

                case CastSource.OffHand:
                    // Вторая рука: предмет в ней, а у безоружного — сам кулак.
                    return parts.TryGetStrikeSource(HandSlot.Left, out part) ? part.Mask : PartMask.Empty;

                case CastSource.BothHands:
                {
                    PartMask mask = PartMask.Empty;
                    if (parts.TryGetStrikeSource(HandSlot.Right, out part)) mask |= part.Mask;
                    if (parts.TryGetStrikeSource(HandSlot.Left, out part))  mask |= part.Mask;
                    return mask;
                }

                default:
                    return Usual(parts);
            }
        }

        /// <summary>
        /// Чем юнит бьёт обычно. Ведущая рука первая, но левша (оружие только в левой) обязан получить свой
        /// телеграф — поэтому <see cref="CastSource.Auto"/> терпим к стороне, в отличие от явных адресов.
        /// </summary>
        private static PartMask Usual(IUnitPartLookup parts)
        {
            if (parts.TryGetStrikeSource(HandSlot.Right, out UnitPart part) && part.IsHeld) return part.Mask;
            if (parts.TryGetHeld(HeldKind.Weapon, out UnitPart weapon)) return weapon.Mask;
            if (parts.TryGetStrikeSource(HandSlot.Right, out part)) return part.Mask;   // кулак ведущей
            return parts.TryGetStrikeSource(HandSlot.None, out part) ? part.Mask : PartMask.Empty;
        }
    }
}
