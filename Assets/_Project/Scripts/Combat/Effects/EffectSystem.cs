using Guildmaster.Combat.Effects;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Владелец жизненного цикла эффектов: наложение (длительность, стакинг, потенция, маска тегов),
    /// тик таймеров и периодики, истечение (Stage 4). Вызывается из <see cref="CombatSimulation"/>
    /// (вики «12» §3.3, §6).
    /// </summary>
    public sealed class EffectSystem
    {
        /// <summary>
        /// Наложить эффект на цель. Резолвит длительность (эфф-эффективности), потенцию (снимок
        /// статов источника), обрабатывает стакинг, добавляет <see cref="RuntimeEffect"/> и зовёт
        /// <c>OnApply</c> компонентов. Прямое добавление в <c>ActiveEffects</c> безопасно: тик
        /// (Stage 4) итерирует по копии-буферу.
        /// </summary>
        public void Apply(RuntimeUnit target, EffectData def, RuntimeUnit source, ICombatContext combat)
        {
            if (def == null || target == null || target.IsDead) return;

            RuntimeEffect existing = FindEffect(target, def);
            if (existing != null)
            {
                ApplyStacking(existing, def, source, target, combat);
                return;
            }

            int componentCount = def.Components?.Length ?? 0;
            var effect = new RuntimeEffect
            {
                Def           = def,
                Source        = source,
                Stacks        = 1,
                ScaledPotency = new float[componentCount],
                TickTimer     = new float[componentCount],
            };

            int ticks = ResolveDurationTicks(def, source, target);
            effect.RemainingTicks    = ticks;
            effect.FullDurationTicks = ticks;

            // Снимок потенции на компонент из статов источника на момент наложения.
            for (int i = 0; i < componentCount; i++)
            {
                if (def.Components[i] is IScalablePotency scalable && source != null)
                {
                    effect.ScaledPotency[i] = scalable.Potency.Resolve(source.Stats);
                }
            }

            target.ActiveEffects.Add(effect);
            target.EffectTagMask |= def.Tags;

            for (int i = 0; i < componentCount; i++)
            {
                if (def.Components[i] is IRuntimeEffectComponent rc)
                {
                    rc.OnApply(MakeContext(target, source, combat, effect, i, 0f));
                }
            }
        }

        /// <summary>Длительность эффекта в тиках. -1 = постоянный, 0 = мгновенный, иначе с учётом эфф-эффективностей.</summary>
        public static int ResolveDurationTicks(EffectData def, RuntimeUnit source, RuntimeUnit target)
        {
            float seconds = def.BaseDuration;
            if (seconds < 0f)  return -1;
            if (seconds == 0f) return 0;

            float mult = DurationMultiplier(def.Polarity, source, target);
            int ticks = Mathf.RoundToInt(seconds * mult * SimConstants.TickRate);
            return Mathf.Max(1, ticks);
        }

        // --- Приватные ---

        private static EffectContext MakeContext(
            RuntimeUnit target, RuntimeUnit source, ICombatContext combat, RuntimeEffect effect, int componentIndex, float dt)
        {
            float potency = effect.ScaledPotency != null && componentIndex < effect.ScaledPotency.Length
                ? effect.ScaledPotency[componentIndex]
                : 0f;
            return new EffectContext(target, source, combat, effect, potency, dt);
        }

        private static RuntimeEffect FindEffect(RuntimeUnit target, EffectData def)
        {
            var effects = target.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Def == def) return effects[i];
            }
            return null;
        }

        private void ApplyStacking(
            RuntimeEffect existing, EffectData def, RuntimeUnit source, RuntimeUnit target, ICombatContext combat)
        {
            bool stacksChanged = false;

            switch (def.Stacking)
            {
                case StackRule.None:
                    return;

                case StackRule.Stack:
                    stacksChanged = TryAddStack(existing, def);
                    break;

                case StackRule.Refresh:
                    RefreshDuration(existing, def, source, target);
                    break;

                case StackRule.StackAndRefresh:
                    stacksChanged = TryAddStack(existing, def);
                    RefreshDuration(existing, def, source, target);
                    break;
            }

            // Стак изменил число — переоценить stateful-вклад компонентов под новый Stacks.
            if (stacksChanged) Reapply(existing, target, combat);
        }

        private static bool TryAddStack(RuntimeEffect effect, EffectData def)
        {
            if (effect.Stacks >= def.MaxStacks) return false;
            effect.Stacks++;
            return true;
        }

        private static void RefreshDuration(RuntimeEffect effect, EffectData def, RuntimeUnit source, RuntimeUnit target)
        {
            int ticks = ResolveDurationTicks(def, source, target);
            effect.RemainingTicks    = ticks;
            effect.FullDurationTicks = ticks;
        }

        private void Reapply(RuntimeEffect effect, RuntimeUnit target, ICombatContext combat)
        {
            IEffectComponent[] components = effect.Def.Components;
            if (components == null) return;

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IRuntimeEffectComponent rc)
                {
                    EffectContext ctx = MakeContext(target, effect.Source, combat, effect, i, 0f);
                    rc.OnExpire(in ctx);
                    rc.OnApply(in ctx);
                }
            }
        }

        private static float DurationMultiplier(EffectPolarity polarity, RuntimeUnit source, RuntimeUnit target)
        {
            if (source == null || target == null) return 1f;

            switch (polarity)
            {
                case EffectPolarity.Buff:
                    return source.Stats.Get(StatType.ApplyBuffEff) * target.Stats.Get(StatType.ReceiveBuffEff);
                case EffectPolarity.Debuff:
                    return source.Stats.Get(StatType.ApplyDebuffEff) * target.Stats.Get(StatType.ReceiveDebuffEff);
                default:
                    return 1f; // Neutral — длительность не скейлится (вики «12» §9)
            }
        }
    }
}
