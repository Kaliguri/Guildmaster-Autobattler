using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Скрытность» (§9.6, §10.5): в начале боя (OnApply при выдаче пассива) и после СВОЕГО убийства
    /// (<see cref="CombatEvent.UnitKilled"/>, доставляется убийце) — накладывает на носителя баф-эффект
    /// стелса (<see cref="_stealthBuff"/>: DamageTakenEff−, MoveSpeed+, тег Stealth) и взводит
    /// усиление следующей авто-атаки (<see cref="RuntimeUnit.EmpowerDamageMult"/>). Первая авто-атака
    /// применяет множитель и снимает стелс (AutoAttackSystem, §9.6).
    /// <para><b>Числа:</b> <c>_empowerMult</c> — во сколько раз сильнее удар из скрытности
    /// (2 = вдвое, разовый); <c>_stealthBuff</c> — сам баф скрытности (меньше получаемого урона,
    /// больше скорости, тег Stealth) — его величины живут в том эффекте.</para>
    /// <para><b>Когда срабатывает:</b> в начале боя и после КАЖДОГО своего убийства — то есть кит
    /// вознаграждается за добивание, а не за отсиживание.</para>
    /// </summary>
    [Serializable]
    public sealed class StealthComponent : IReactiveComponent
    {
        [Tooltip("Баф стелса (DamageTakenEff−, MoveSpeed+, тег Stealth), снимаемый первой авто-атакой.")]
        [SerializeField] private EffectData _stealthBuff;

        [Tooltip("Множитель урона усиленной первой авто-атаки.")]
        [SerializeField] private float _empowerMult = 2f;

        [Tooltip("Сколько ед. брони игнорирует удар из скрытности (20 у Убийцы). 0 = бьёт по обычной броне.")]
        [SerializeField] private float _empowerFlatPen = 20f;

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
            if (self == null || self.IsDead) return;

            if (_stealthBuff != null) ctx.Combat.ApplyEffect(self, _stealthBuff, self);
            self.EmpowerDamageMult = _empowerMult;
            self.EmpowerFlatPen    = _empowerFlatPen;
            self.BlinkBehindOnNextAttack = true; // удар из скрытности блинкует убийцу за спину цели (§10.5)
        }
    }
}
