using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Живой уголь» Хранителя углей (карточка [[the-hearth]]): реактив, кладущий стак
    /// <see cref="_selfEffect"/> НА СЕБЯ за собственное попадание подходящей школы. Зеркало
    /// <see cref="EmberIgniterComponent"/>, который тем же правилом греет цель.
    /// <para><b>Числа:</b> своих нет — сколько даёт стак и как он сходит, живёт в
    /// <see cref="_selfEffect"/>. Здесь только правило «попал своим огнём → согрелся сам».</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.DamageDealt"/> носителя, если школа и
    /// стихия удара совпали с фильтром (<c>None</c> в стихии = любая).</para>
    /// </summary>
    /// <remarks>
    /// Отдельный компонент, а не флаг «на себя» у игнитера: у них разные адресаты, а значит и разные
    /// поводы для отладки — «почему у врага нет углей» и «почему у меня нет углей» это два разных
    /// вопроса. Плюс кит может носить оба сразу, и тогда флаг пришлось бы выражать двумя копиями
    /// компонента с противоположным значением — то же самое, но менее читаемо.
    /// </remarks>
    [Serializable]
    public sealed class SelfStackOnDealtComponent : IReactiveComponent
    {
        [Tooltip("Эффект, стак которого носитель кладёт на СЕБЯ за своё попадание.")]
        [SerializeField] private EffectData _selfEffect;

        [Tooltip("Тип удара, который считается (Хранитель углей греется только собственным Огнём).")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        [Tooltip("Считать всю школу этого типа, а не только сам тип.")]
        [SerializeField] private bool _wholeSchool;

        [Tooltip("Только прямые удары (авто-атака и способности). Выкл = тики DoT тоже греют.")]
        [SerializeField] private bool _directHitsOnly = true;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_selfEffect == null) return;
            if (_directHitsOnly && !e.IsDirectHit) return;
            if (!DamageTypes.Matches(_damageType, _wholeSchool, e.DamageType)) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            ctx.Combat.ApplyEffect(self, _selfEffect, self);
        }
    }
}
