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

        /// <summary>
        /// Эта часть — РАБОЧАЯ часть предмета (<c>UnitHeldItem.ReachPart</c>): клинок меча, полотно щита,
        /// наконечник копья. По ней и только по ней меряется вылет.
        /// </summary>
        /// <remarks>
        /// Предмет из нескольких кусков даёт СТОЛЬКО ЖЕ частей с типом «оружие»: рукоять, гарда и клинок все
        /// сидят под костью-хватом. Без этого признака запрос «чем бьют» возвращал первую по порядку
        /// отрисовки — у сторибучного меча это РУКОЯТЬ, и кончиком оружия оказывалась точка в паре
        /// сантиметров от кулака. Дуга за клинком выходила размером с ладонь, а форма удара — из кулака
        /// (найдено 04.08.2026). Офлайн-замер этой ошибки не знал: `RigProfile` спрашивает `ReachPart`
        /// с самого начала, и гизмо рисовало правду, которой в игре не было.
        /// </remarks>
        public readonly bool IsReach;

        public UnitPart(int index, SpriteRenderer renderer, string bone, BodySide side,
            HandSlot slot, HeldKind kind, bool isHand, bool isReach = false)
        {
            Index    = index;
            Renderer = renderer;
            Bone     = bone;
            Side     = side;
            Slot     = slot;
            Kind     = kind;
            IsHand   = isHand;
            IsReach  = isReach;
        }

        /// <summary>Предмет в руке, а не часть тела.</summary>
        public bool IsHeld => Kind != HeldKind.None;

        /// <summary>Маска из одной этой части — то, что уезжает в состояние тела как «светится вот это».</summary>
        public PartMask Mask => PartMask.Single(Index);

        public override string ToString() =>
            // Сторону дописывать не надо: она уже в имени кости (LowerArm_R), иначе вышло бы "LowerArm_R_R".
            IsHeld ? $"[{Index}] {Bone} ({Kind}, {Slot})" : $"[{Index}] {Bone}";
    }
}
