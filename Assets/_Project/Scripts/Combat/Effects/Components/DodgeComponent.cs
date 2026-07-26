using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Изворотливость» (§9.3, §9.4, §10.5): pre-damage реактив с зарядами. Полностью отменяет входящую
    /// АВТОАТАКУ (<see cref="DamageRequest.IsAutoAttack"/>) — любую, даже слабую; урон способностей/DoT/шипов
    /// не гасит. Дополнительно фильтруется триггером блока F (из <c>self.Unit.Ai.PassiveTrigger</c>) и тратит
    /// один заряд; заряды восстанавливаются независимо. Состояние зарядов — per-effect в
    /// <see cref="RuntimeEffect.ChargeReadyTicks"/>.
    /// <para><b>Числа:</b> <c>_maxCharges</c> — сколько автоатак подряд можно отменить;
    /// <c>_rechargeSeconds</c> — за сколько восстанавливается ОДИН заряд. Величины урона здесь нет
    /// намеренно: уклонение не смягчает удар, а отменяет его целиком.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, до расчёта урона — отменённый удар не наносит
    /// ничего и не будит реактивы «на удар» (шипы об уклонившегося не колются).</para>
    /// </summary>
    [Serializable]
    public sealed class DodgeComponent : IPreDamageComponent, IStackableComponent
    {
        [Tooltip("Число зарядов негейта. Убийца = 1 (гейтит одну следующую атаку).")]
        [SerializeField] private int _maxCharges = 1;

        [Tooltip("Независимая перезарядка одного заряда, сек. Убийца = 5.")]
        [SerializeField] private float _rechargeSeconds = 5f;

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
            if (!incoming.IsAutoAttack) return; // «Изворотливость» уклоняется ТОЛЬКО от автоатак (не от способностей/DoT)

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
            AIProfile ai = self.Unit != null ? self.Unit.Ai : null;
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
