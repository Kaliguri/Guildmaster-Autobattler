using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Одна часть тела юнита глазами презентации: чем она рисуется, какую кость рига несёт, на какой стороне
    /// стоит и — если это предмет в руке — чем является и в каком хвате сидит.
    /// </summary>
    /// <remarks>
    /// Предмет в руке — не отдельная сущность рядом с частями тела, а частный случай части: клинок рисуется
    /// таким же рендерером под такой же костью, просто кость висит в хвате. Держать их двумя списками
    /// значило бы дважды описывать одно тело.
    /// </remarks>
    public readonly struct UnitPart
    {
        /// <summary>Индекс части в теле — он же номер её бита в <see cref="PartMask"/>.</summary>
        public readonly int Index;

        /// <summary>Чем часть рисуется.</summary>
        public readonly SpriteRenderer Renderer;

        /// <summary>Имя кости рига, которую рисует часть: <c>Head</c>, <c>Arm_Down</c>, <c>Sword</c>.</summary>
        public readonly string Bone;

        /// <summary>Сторона тела: у парных конечностей — своя, у торса и головы <see cref="BodySide.None"/>.</summary>
        public readonly BodySide Side;

        /// <summary>Хват, в котором сидит предмет. У части тела — <see cref="HandSlot.None"/>.</summary>
        public readonly HandSlot Slot;

        /// <summary>Чем предмет является в бою. У части тела — <see cref="HeldKind.None"/>.</summary>
        public readonly HeldKind Kind;

        /// <summary>
        /// Кисть: кость, под которой висит хват. Она же источник безоружного удара — кулак у бойца с пустыми
        /// руками. Признак структурный, а не по списку имён: «рука» — это то, что может держать предмет.
        /// </summary>
        public readonly bool IsHand;

        public UnitPart(int index, SpriteRenderer renderer, string bone, BodySide side,
            HandSlot slot, HeldKind kind, bool isHand)
        {
            Index    = index;
            Renderer = renderer;
            Bone     = bone;
            Side     = side;
            Slot     = slot;
            Kind     = kind;
            IsHand   = isHand;
        }

        /// <summary>Предмет в руке, а не часть тела.</summary>
        public bool IsHeld => Kind != HeldKind.None;

        /// <summary>Маска из одной этой части — то, что уезжает в состояние тела как «светится вот это».</summary>
        public PartMask Mask => PartMask.Single(Index);

        public override string ToString() =>
            IsHeld ? $"[{Index}] {Bone} ({Kind}, {Slot})" : $"[{Index}] {Bone}{RigNaming.SideSuffix(Side)}";
    }
}
