using System.Collections.Generic;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Запрос к частям тела юнита: «что у тебя в правой руке», «дай щит», «дай голову», «дай правую ногу»,
    /// «чем ты сейчас ударишь». Отвечает тело — через шов <see cref="IUnitBodyVisual.Parts"/>.
    /// </summary>
    /// <remarks>
    /// Шов нужен потому, что тела два и части у них устроены по-разному: у скелетного юнита их полтора
    /// десятка со своими костями и хватами, у покадрового — один спрайт, который сам себе и тело, и оружие.
    /// Потребители (свечение каста, будущий доворот прицела, попадание в конкретную часть) не должны это
    /// различать.
    /// </remarks>
    public interface IUnitPartLookup
    {
        /// <summary>Все части тела в порядке отрисовки. Индекс части — номер её бита в <see cref="PartMask"/>.</summary>
        IReadOnlyList<UnitPart> Parts { get; }

        /// <summary>Все части тела одной маской — «светись целиком».</summary>
        PartMask Everything { get; }

        /// <summary>
        /// Предмет в этом хвате. Двуручный отвечает на любую руку. Пустая рука — false.
        /// </summary>
        bool TryGetHeld(HandSlot slot, out UnitPart part);

        /// <summary>
        /// Первый предмет этого типа: «дай щит», «дай оружие». У бойца с двумя кинжалами вернёт основной —
        /// правый, потому что части перебираются в порядке тела, а не по стороне.
        /// </summary>
        bool TryGetHeld(HeldKind kind, out UnitPart part);

        /// <summary>
        /// Часть тела по кости и стороне: <c>("Head", None)</c>, <c>("Leg_Boots", Right)</c>. Сторона
        /// <see cref="BodySide.None"/> означает «любая», иначе часть ищется строго на своей стороне.
        /// </summary>
        bool TryGetBone(string boneName, BodySide side, out UnitPart part);

        /// <summary>
        /// Чем юнит исполняет приём этой рукой: предмет в хвате, а если рука пуста — сама кисть (кулак).
        /// Тело из одного спрайта отвечает собой целиком.
        /// </summary>
        /// <remarks>
        /// Деградация на кисть — не подмена авторского значения, а сам дизайн: «кулаки» это тоже оружие,
        /// и безоружный удар обязан иметь источник свечения, иначе телеграф пропадёт у целого архетипа.
        /// </remarks>
        bool TryGetStrikeSource(HandSlot slot, out UnitPart part);
    }
}
