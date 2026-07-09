using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Оплот» (§9.3, §10.3): pre-damage реактив. Перед входящим уроном — если выполнен триггер
    /// блока F (читается из <c>self.Relic.Ai.PassiveTrigger</c>) и истёк внутренний кулдаун —
    /// накладывает на носителя таймированный щит (<see cref="_shieldEffect"/>), который тут же
    /// поглощает триггер-удар. Внутренний КД хранится per-effect в
    /// <see cref="RuntimeEffect.ReactiveReadyTick"/> (сверка с текущим тиком, без декрементов).
    /// </summary>
    [Serializable]
    public sealed class BulwarkComponent : IPreDamageComponent
    {
        [Tooltip("Внутренний кулдаун между поднятиями щита, сек (иначе блокирует каждый удар). Защитник ≈ 7.")]
        [SerializeField] private float _internalCooldownSeconds = 7f;

        [Tooltip("Таймированный щит-эффект, накладываемый на носителя при срабатывании (величина — в его MissingHpShieldComponent).")]
        [SerializeField] private EffectData _shieldEffect;

        // Pre-damage реактив: рабочих OnApply/OnExpire нет (щит живёт отдельным эффектом).
        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _shieldEffect == null) return;

            // Внутренний кулдаун (§9.3): абсолютный «готов с тика».
            if (ctx.Combat.CurrentTick < ctx.Effect.ReactiveReadyTick) return;

            if (!TriggerMet(self, in incoming)) return;

            ctx.Combat.ApplyEffect(self, _shieldEffect, self);

            int cdTicks = Mathf.Max(1, Mathf.RoundToInt(_internalCooldownSeconds * SimConstants.TickRate));
            ctx.Effect.ReactiveReadyTick = ctx.Combat.CurrentTick + cdTicks;
        }

        /// <summary>
        /// Триггер блока F: None — никогда; AnyHit/Always — на любой удар; OnHitAbovePctMaxHp —
        /// на удар выше порога (по сырому урону) ИЛИ смертельный (сырой ≥ текущего HP — «всегда при смертельном»).
        /// </summary>
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
                    float threshold = (ai != null ? ai.PassiveThresholdPct : 0.2f) * self.Stats.Get(StatType.MaxHP);
                    return req.RawDamage > threshold || req.RawDamage >= self.CurrentHP;

                default:
                    return false;
            }
        }
    }
}
