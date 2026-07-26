using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Оплот» (§9.3, §10.3): pre-damage реактив с зарядами. Перед входящим уроном — если выполнен
    /// триггер блока F (читается из <c>self.Unit.Ai.PassiveTrigger</c>) и есть готовый заряд —
    /// накладывает на носителя таймированный щит (<see cref="_shieldEffect"/>), который тут же
    /// поглощает триггер-удар. Состояние зарядов — per-effect в
    /// <see cref="RuntimeEffect.ChargeReadyTicks"/> (сверка с текущим тиком, без декрементов),
    /// как у <see cref="DodgeComponent"/>.
    /// </summary>
    /// <remarks>
    /// Щит намеренно короткий (0.4 с в ассете), а зарядов несколько: тогда «Оплот» гасит ровно те
    /// удары, ради которых поднялся, и его сила снова определяется величиной щита, а не тем, сколько
    /// ударов успело прилететь за время его жизни (замер 2026-07-26: при 2-секундном щите правка
    /// величины не меняла размен один-на-один вовсе).
    /// </remarks>
    [Serializable]
    public sealed class BulwarkComponent : IPreDamageComponent, IStackableComponent
    {
        [Tooltip("Число зарядов щита. Защитник = 2 (заряды восстанавливаются независимо).")]
        [SerializeField] private int _maxCharges = 1;

        [Tooltip("Независимая перезарядка ОДНОГО заряда, сек (стартует ПОСЛЕ срабатывания). Защитник = 5.")]
        [SerializeField] private float _internalCooldownSeconds = 4f;

        [Tooltip("Таймированный щит-эффект, накладываемый на носителя при срабатывании (величина — в его MissingHpShieldComponent).")]
        [SerializeField] private EffectData _shieldEffect;

        public void OnApply(in EffectContext ctx)
        {
            // Заряды стартуют готовыми (readyTick = 0 ≤ любого CurrentTick).
            ctx.Effect.ChargeReadyTicks = new int[Mathf.Max(1, _maxCharges)];
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ трогает заряды: их число фиксировано, а per-charge таймеры уже живут в
            // ctx.Effect.ChargeReadyTicks. Дефолтный OnExpire→OnApply дал бы бесплатный рефилл
            // всех зарядов на каждый стак (та же гоча, что у «Изворотливости»).
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _shieldEffect == null) return;

            if (!TriggerMet(self, in incoming)) return;

            int[] charges = ctx.Effect.ChargeReadyTicks;
            if (charges == null) return;

            int now = ctx.Combat.CurrentTick;
            int rechargeTicks = Mathf.Max(1, Mathf.RoundToInt(_internalCooldownSeconds * SimConstants.TickRate));

            for (int i = 0; i < charges.Length; i++)
            {
                if (charges[i] > now) continue; // заряд ещё перезаряжается

                charges[i] = now + rechargeTicks;
                ctx.Combat.ApplyEffect(self, _shieldEffect, self);
                return;
            }
            // нет готовых зарядов — удар проходит как есть
        }

        /// <summary>
        /// Триггер блока F: None — никогда; AnyHit/Always — на любой удар; OnHitAbovePctMaxHp —
        /// на удар выше порога (по сырому урону) ИЛИ смертельный (сырой ≥ текущего HP — «всегда при смертельном»).
        /// </summary>
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
