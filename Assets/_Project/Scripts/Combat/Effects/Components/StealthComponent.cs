using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Скрытность» (§9.6, §10.5): в начале боя (OnApply при выдаче пассива) и после СВОЕГО убийства
    /// (<see cref="CombatEvent.UnitKilled"/>, доставляется убийце) — накладывает на носителя баф
    /// скрытности (<see cref="_stealthBuff"/>).
    /// <para><b>Что даёт баф</b> — задано в самом бафе, не здесь: снижение получаемого урона и
    /// прибавку скорости держит его <c>StatModifierComponent</c>, а усиление следующей авто-атаки —
    /// <see cref="EmpowerNextAttackComponent"/>. Этот компонент отвечает только за то, КОГДА кит
    /// уходит в тень; активка «Уйти в тень» накладывает тот же баф за ману и потому не дублирует
    /// ни одного числа.</para>
    /// <para><b>Когда срабатывает:</b> в начале боя и после КАЖДОГО своего убийства — то есть кит
    /// вознаграждается за добивание, а не за отсиживание.</para>
    /// </summary>
    [Serializable]
    public sealed class StealthComponent : IReactiveComponent
    {
        [Tooltip("Баф скрытности: снижение получаемого урона, прибавка скорости, усиление следующего удара, тег Stealth.")]
        [SerializeField] private EffectData _stealthBuff;

        public CombatEvent Events => CombatEvent.UnitKilled;

        public void OnApply(in EffectContext ctx)  => Cloak(in ctx); // начало боя (выдача пассива)
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Событие доставляется убийце (carrier = Source), т.е. нашему носителю — рестелс после своего убийства.
            if (e.Type != CombatEvent.UnitKilled || e.Source != ctx.Target) return;
            Cloak(in ctx);
        }

        private void Cloak(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _stealthBuff == null) return;

            ctx.Combat.ApplyEffect(self, _stealthBuff, self);
        }
    }
}
