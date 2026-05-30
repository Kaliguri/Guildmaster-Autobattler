using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Владелец жизненного цикла эффектов: наложение (длительность, стакинг, потенция, маска тегов),
    /// тик таймеров и периодики, истечение с корректным teardown. Вызывается из
    /// <see cref="CombatSimulation"/> (вики «12» §3.3, §3.5, §6).
    /// </summary>
    public sealed class EffectSystem
    {
        // Переиспользуемые буферы — итерируем по копии refs, чтобы Apply/Dispel во время
        // тика не ломали коллекцию (вики «6» §7: добавления/удаления вне итерации).
        private readonly List<RuntimeEffect> _tickBuffer = new List<RuntimeEffect>();

        /// <summary>
        /// Шаг всех эффектов на всех юнитах: периодика → countdown длительности → истечение.
        /// Вставляется в тик-цикл перед DeathSystem (DoT может добить).
        /// </summary>
        public void Tick(IReadOnlyList<RuntimeUnit> units, ICombatContext combat, float dt)
        {
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];
                if (unit.IsDead || unit.ActiveEffects.Count == 0) continue;

                _tickBuffer.Clear();
                _tickBuffer.AddRange(unit.ActiveEffects);

                for (int e = 0; e < _tickBuffer.Count; e++)
                {
                    RuntimeEffect eff = _tickBuffer[e];
                    // Мог быть снят диспелом/реапплаем в этом же тике.
                    if (!unit.ActiveEffects.Contains(eff)) continue;

                    TickPeriodic(unit, eff, combat);
                    if (unit.IsDead) break;

                    if (!eff.IsPermanent)
                    {
                        eff.RemainingTicks--;
                        if (eff.RemainingTicks <= 0) Expire(unit, eff, combat);
                    }
                }

                if (!unit.IsDead) RecomputeControl(unit);
            }
        }

        /// <summary>Пересобрать флаги контроля из активных <see cref="ControlComponent"/> (перекрытие без счётчиков).</summary>
        private static void RecomputeControl(RuntimeUnit unit)
        {
            bool canAct = true, canMove = true, canCast = true;

            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int e = 0; e < effects.Count; e++)
            {
                IEffectComponent[] comps = effects[e].Def.Components;
                if (comps == null) continue;

                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is ControlComponent control)
                    {
                        if (control.PreventAct)  canAct  = false;
                        if (control.PreventMove) canMove = false;
                        if (control.PreventCast) canCast = false;
                    }
                }
            }

            unit.CanAct  = canAct;
            unit.CanMove = canMove;
            unit.CanCast = canCast;
        }

        /// <summary>
        /// Наложить эффект на цель. Резолвит длительность (эфф-эффективности), потенцию (снимок
        /// статов источника), обрабатывает стакинг, добавляет <see cref="RuntimeEffect"/> и зовёт
        /// <c>OnApply</c> компонентов. Мгновенный эффект (BaseDuration = 0) не персистится.
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
                PeriodicTicks = new int[componentCount],
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

            bool instant = ticks == 0;
            if (!instant)
            {
                target.ActiveEffects.Add(effect);
                target.EffectTagMask |= def.Tags;
            }

            for (int i = 0; i < componentCount; i++)
            {
                if (def.Components[i] is IRuntimeEffectComponent rc)
                {
                    rc.OnApply(MakeContext(target, source, combat, effect, i, 0f));
                }
            }
        }

        /// <summary>
        /// Принудительно снять эффект с корректным <c>OnExpire</c> и пересборкой маски тегов.
        /// Используется диспелом (Stage 7).
        /// </summary>
        public void Remove(RuntimeUnit unit, RuntimeEffect effect, ICombatContext combat)
        {
            Expire(unit, effect, combat);
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

        private static void TickPeriodic(RuntimeUnit unit, RuntimeEffect eff, ICombatContext combat)
        {
            IEffectComponent[] comps = eff.Def.Components;
            if (comps == null) return;

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is IPeriodicComponent periodic && periodic.Interval > 0f)
                {
                    int intervalTicks = Mathf.Max(1, Mathf.RoundToInt(periodic.Interval * SimConstants.TickRate));
                    if (++eff.PeriodicTicks[i] >= intervalTicks)
                    {
                        eff.PeriodicTicks[i] = 0;
                        // Dt = Interval: компонент считает применяемое как Potency × Dt × Stacks
                        // (per-second rate → за период; total масштабируется числом тиков, вики «11» §5.1).
                        periodic.OnTick(MakeContext(unit, eff.Source, combat, eff, i, periodic.Interval));
                        if (unit.IsDead) return;
                    }
                }
            }
        }

        private void Expire(RuntimeUnit unit, RuntimeEffect eff, ICombatContext combat)
        {
            IEffectComponent[] comps = eff.Def.Components;
            if (comps != null)
            {
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] is IRuntimeEffectComponent rc)
                    {
                        rc.OnExpire(MakeContext(unit, eff.Source, combat, eff, i, 0f));
                    }
                }
            }

            unit.ActiveEffects.Remove(eff);
            RebuildTagMask(unit);
        }

        private static void RebuildTagMask(RuntimeUnit unit)
        {
            EffectTag mask = EffectTag.None;
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++) mask |= effects[i].Def.Tags;
            unit.EffectTagMask = mask;
        }

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
            List<RuntimeEffect> effects = target.ActiveEffects;
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
