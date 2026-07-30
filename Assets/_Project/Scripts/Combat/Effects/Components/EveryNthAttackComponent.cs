using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Каждая N-я атака особая» (карточка [[the-draugr]]): пассивка считает авто-атаки носителя и
    /// взводит <see cref="_charge"/> так, чтобы усиленным оказался именно N-й удар. Что делает усиленный
    /// удар — множитель, толчок, лишние стаки — живёт в самом заряде.
    /// <para><b>Числа:</b> <c>_period</c> — каждый какой удар особый (Драугр = 3). Больше здесь ничего нет
    /// намеренно: сила принадлежит заряду, и балансится там же, где у всех прочих зарядов.</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.DamageDealt"/> носителя от авто-атаки.
    /// Счётчик — <see cref="RuntimeEffect.Counter"/>, то есть per-unit и на один бой.</para>
    /// </summary>
    /// <remarks>
    /// <b>Счёт детерминированный, без шанса</b> — правило нулевого выходного рандома. Взводим НА
    /// ПРЕДЫДУЩЕМ ударе (когда счётчик подошёл к <c>_period − 1</c>), потому что усилить уже нанесённый
    /// удар нельзя: цифры снимаются до прилёта. Поэтому первый особый удар — ровно третий, а не
    /// четвёртый, и дальше каждые три.
    /// <para><b>Промах в счёт не идёт:</b> событие приходит только на состоявшийся урон, значит удар,
    /// от которого уклонились, цикл не двигает. Это осознанно — иначе противник мог бы «съедать» особый
    /// удар уклонениями.</para>
    /// </remarks>
    [Serializable]
    public sealed class EveryNthAttackComponent : IReactiveComponent
    {
        [Tooltip("Каждый какой удар особый. Драугр = 3.")]
        [Min(2)]
        [SerializeField] private int _period = 3;

        [Tooltip("Заряд, который взводится перед особым ударом (несёт множитель и лишние стаки).")]
        [SerializeField] private EffectData _charge;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_charge == null || !e.IsAutoAttack) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            RuntimeEffect eff = ctx.Effect;
            eff.Counter++;

            int period = _period < 2 ? 2 : _period;
            if (eff.Counter % period != period - 1) return;

            ctx.Combat.ApplyEffect(self, _charge, self);
        }
    }
}
