using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Кровавый обмен» Геоманта (карточка [[the-cairn]]): носитель исцеляется на долю урона, который
    /// ЕГО подходящий источник наносит врагам в его радиусе.
    /// <para><b>Числа:</b> <c>_healShare</c> — доля урона, уходящая в лечение (0.15 = 15%);
    /// <c>_radius</c> — насколько далеко он это чувствует; фильтры <c>_sourceKind</c> и <c>_school</c>
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
    /// <para><b>Известное ограничение фильтра.</b> Боевое событие несёт только вид источника, школу и
    /// стихию — ни сродства, ни тегов эффекта в нём нет. Поэтому «кровотечение» описывается как
    /// «периодический физический урон», и ЯД КЛЫКА под это описание тоже попадает: он физический и тоже
    /// тикает. То есть Геомант сейчас кормится и от яда, чего карточка не обещала. Развести их можно
    /// только полем «теги эффекта-источника» в событии — до тех пор это названная неточность, а не
    /// замысел.</para>
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

        [Tooltip("Школа урона, который кормит.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Physical;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_healShare <= 0f || e.Amount <= 0f) return;
            if (e.SourceKind != _sourceKind || e.School != _school) return;

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
