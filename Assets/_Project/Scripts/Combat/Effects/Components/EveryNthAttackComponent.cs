using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Каждая N-я Атака особая» (карточка [[the-draugr]]): пассивка считает Атаки носителя и взводит
    /// <see cref="_charge"/> так, чтобы усиленной оказалась именно N-я. Что делает усиленный удар —
    /// множитель, толчок, лишние стаки — живёт в самом заряде.
    /// <para><b>Числа:</b> <c>_period</c> — каждая какая Атака особая (Драугр = 3). Больше здесь ничего нет
    /// намеренно: сила принадлежит заряду, и балансится там же, где у всех прочих зарядов.</para>
    /// <para><b>Когда срабатывает:</b> на завершённой Атаке носителя
    /// (<see cref="CombatEvent.AttackCompleted"/>). Комбо порвалось — гасит взведённое: серия начинается
    /// заново, и особой снова будет N-я.</para>
    /// </summary>
    /// <remarks>
    /// <b>Счёт детерминированный, без шанса</b> — правило нулевого выходного рандома. Взводим НА
    /// ПРЕДЫДУЩЕЙ Атаке (когда счётчик подошёл к <c>_period − 1</c>), потому что усилить уже нанесённый
    /// удар нельзя: цифры снимаются до прилёта. Поэтому первая особая Атака — ровно третья, а не
    /// четвёртая, и дальше каждые три.
    /// <para><b>Счётчик — общий счётчик серии</b> (<see cref="RuntimeUnit.ComboAttacks"/>), а не свой
    /// <c>RuntimeEffect.Counter</c> (вердикт Макса 2026-08-01). Свой пришлось бы сбрасывать на разрыве
    /// Комбо руками, и забытый сброс ниоткуда не виден: кит продолжал бы счёт с того места, где его
    /// застанили. Заодно счёт идёт по Атакам, а не по Ударам: у многоударного кита событие урона приходит
    /// на каждый Удар, и «каждая третья» превратилась бы в «каждую полторы».</para>
    /// <para><b>Промах Атаку засчитывает:</b> считается путь взмаха, а не попадание, поэтому уклонениями
    /// «съесть» особую Атаку нельзя.</para>
    /// </remarks>
    [Serializable]
    public sealed class EveryNthAttackComponent : IReactiveComponent
    {
        [Tooltip("Каждая какая Атака особая. Драугр = 3.")]
        [Min(2)]
        [SerializeField] private int _period = 3;

        [Tooltip("Заряд, который взводится перед особой Атакой (несёт множитель и лишние стаки).")]
        [SerializeField] private EffectData _charge;

        public CombatEvent Events => CombatEvent.AttackCompleted | CombatEvent.ComboBroken;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_charge == null) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            if (e.Type == CombatEvent.ComboBroken) { ctx.Combat.RemoveEffect(self, _charge); return; }

            int period = _period < 2 ? 2 : _period;
            if (self.ComboAttacks % period != period - 1) return;

            ctx.Combat.ApplyEffect(self, _charge, self);
        }
    }
}
