using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Изворотливость» (§9.3, §9.4, §10.5): pre-damage реактив с зарядами. По триггеру блока F
    /// (из <c>self.Relic.Ai.PassiveTrigger</c>) полностью отменяет входящий удар, тратя один заряд;
    /// заряды восстанавливаются независимо. Тип источника урона не важен (решение Макса — негейтит
    /// любой следующий удар). Состояние зарядов — per-effect в <see cref="RuntimeEffect.ChargeReadyTicks"/>.
    /// </summary>
    [Serializable]
    public sealed class DodgeComponent : IPreDamageComponent, IStackableComponent
    {
        [Tooltip("Число зарядов негейта.")]
        [SerializeField] private int _maxCharges = 2;

        [Tooltip("Независимая перезарядка одного заряда, сек.")]
        [SerializeField] private float _rechargeSeconds = 8f;

        public void OnApply(in EffectContext ctx)
        {
            // Инициализируем заряды готовыми (readyTick = 0 ≤ любого CurrentTick).
            ctx.Effect.ChargeReadyTicks = new int[Mathf.Max(1, _maxCharges)];
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ трогает заряды: их число фиксировано (_maxCharges), а per-charge таймеры
            // перезарядки уже живут в ctx.Effect.ChargeReadyTicks. Дефолтный OnExpire→OnApply здесь
            // обнулил бы массив — бесплатный рефилл всех зарядов негейта на каждый стак (07 §3.8 B2).
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return; // уже отменён другим компонентом

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;
            if (!TriggerMet(self, in incoming)) return;

            int[] charges = ctx.Effect.ChargeReadyTicks;
            if (charges == null) return;

            int now = ctx.Combat.CurrentTick;
            int rechargeTicks = Mathf.Max(1, Mathf.RoundToInt(_rechargeSeconds * SimConstants.TickRate));

            for (int i = 0; i < charges.Length; i++)
            {
                if (charges[i] <= now) // заряд готов
                {
                    charges[i] = now + rechargeTicks;
                    result.Negated = true;
                    return;
                }
            }
            // нет готовых зарядов — удар проходит
        }

        /// <summary>Триггер блока F: None — никогда; AnyHit/Always — любой удар; OnHitAbovePctMaxHp — выше порога ИЛИ смертельный.</summary>
        private static bool TriggerMet(RuntimeUnit self, in DamageRequest req)
        {
            AIProfile ai = self.Relic != null ? self.Relic.Ai : null;
            PassiveTrigger trigger = ai != null ? ai.PassiveTrigger : PassiveTrigger.AnyHit;

            switch (trigger)
            {
                case PassiveTrigger.None:
                    return false;
                case PassiveTrigger.AnyHit:
                case PassiveTrigger.Always:
                    return true;
                case PassiveTrigger.OnHitAbovePctMaxHp:
                    float threshold = (ai != null ? ai.PassiveThresholdPct : AIProfile.DefaultPassiveThresholdPct) * self.Stats.Get(StatType.MaxHP);
                    return req.RawDamage > threshold || req.RawDamage >= self.CurrentHP;
                default:
                    return false;
            }
        }
    }
}
