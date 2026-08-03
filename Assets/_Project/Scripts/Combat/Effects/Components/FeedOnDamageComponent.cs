using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Кровавый обмен» Геоманта (карточка [[the-cairn]]): носитель исцеляется на долю урона, который
    /// ЕГО подходящий источник наносит врагам в его радиусе.
    /// <para><b>Числа:</b> <c>_healShare</c> — доля урона, уходящая в лечение (0.15 = 15%);
    /// <c>_radius</c> — насколько далеко он это чувствует; фильтры <c>_sourceKind</c> и <c>_damageType</c>
    /// отвечают на вопрос «какой именно урон его кормит».</para>
    /// <para><b>Когда срабатывает:</b> на любом уроне по врагу, попадающем под фильтры, если враг в радиусе.
    /// Отсюда роль кита в драфте: он тем сильнее, чем больше в гильдии источников кровотечения.</para>
    /// </summary>
    /// <remarks>
    /// <b>Расхождение с карточкой, названное вслух:</b> она обещает лечение от ЛЮБЫХ кровотечений в
    /// радиусе, включая союзничьи. Сейчас кормят только свои. Причина в шве событий: «по цели попали»
    /// доставляется самой цели, а не наблюдателям, поэтому чужой урон носитель не видит. Честное «любые»
    /// требует широковещательного события (как <c>AbilityCast</c>) либо радиусного опроса на каждом тике —
    /// и то и другое стоит дороже, чем весит сегодняшняя синергия: второго источника кровотечения в
    /// ростере всё равно нет.
    /// <para><b>Прежнее ограничение снято 2026-07-30.</b> Здесь было записано, что «кровотечение»
    /// описывается как «периодический физический», и потому яд Клыка тоже кормит. Это перестало быть
    /// правдой: фильтр стоит на КОНКРЕТНОМ типе урона (<c>Bleed</c>) при <c>_wholeSchool = false</c>, а
    /// боевое событие тип урона несёт. Кровь и яд — разные типы, и отдельный тег эффекта для их
    /// различения не нужен (возражение Макса: «зачем тег, если есть тип урона»).</para>
    /// </remarks>
    [Serializable]
    public sealed class FeedOnDamageComponent : IReactiveComponent
    {
        [Tooltip("Доля чужого урона, уходящая носителю в лечение (0.15 = 15%).")]
        [SerializeField] private float _healShare = 0.15f;

        [Tooltip("Радиус, в котором носитель чувствует этот урон, мировые единицы.")]
        [SerializeField] private float _radius = 6f;

        [Tooltip("Вид источника, который кормит: Periodic = тики DoT (кровотечение).")]
        [SerializeField] private DamageSourceKind _sourceKind = DamageSourceKind.Periodic;

        [Tooltip("Кормиться любым видом источника, а не только выбранным выше. Нужно там, где школа урона " +
                 "И ЕСТЬ вся идентичность: кровавый поток Десятины бьёт типом Кровотечение напрямую, то " +
                 "есть видом источника он авто-атака, а по природе — то же самое кровотечение.")]
        [SerializeField] private bool _anySourceKind;

        [Tooltip("Школа урона, который кормит.")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        [Tooltip("Кормиться всей школой этого типа, а не только самим типом.")]
        [SerializeField] private bool _wholeSchool = true;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_healShare <= 0f || e.Amount <= 0f) return;
            if (!_anySourceKind && e.SourceKind != _sourceKind) return;
            if (!DamageTypes.Matches(_damageType, _wholeSchool, e.DamageType)) return;

            RuntimeUnit self = ctx.Target;
            RuntimeUnit victim = e.Target;
            if (self == null || self.IsDead || victim == null) return;

            // Кормит только урон по ВРАГАМ носителя: истекающий кровью союзник его не лечит.
            if (victim.Team == self.Team) return;

            if ((victim.Position - self.Position).sqrMagnitude > _radius * _radius) return;

            ctx.Combat.Heal(self, e.Amount * _healShare, self);
        }
    }
}
