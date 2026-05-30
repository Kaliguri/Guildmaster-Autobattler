using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Накладывает стат-модификаторы на носителя на время жизни эффекта. Источник снятия —
    /// сам <see cref="RuntimeEffect"/>, поэтому моды уходят разом на <c>OnExpire</c>. При стаках
    /// величина линейно умножается на число стаков (вики «6» §5.5).
    /// </summary>
    [Serializable]
    public sealed class StatModifierComponent : IRuntimeEffectComponent
    {
        [Tooltip("Модификаторы, накладываемые на время эффекта. На каждый стак — линейно ×Stacks.")]
        [SerializeField] private StatModifier[] _modifiers;

        public void OnApply(in EffectContext ctx)
        {
            if (_modifiers == null || _modifiers.Length == 0 || ctx.Target?.Stats == null) return;

            StatModifier[] mods = ctx.Stacks == 1 ? _modifiers : ScaleByStacks(_modifiers, ctx.Stacks);
            ctx.Target.Stats.AddModifiersFrom(ctx.Effect, mods);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect);
        }

        private static StatModifier[] ScaleByStacks(StatModifier[] source, int stacks)
        {
            var scaled = new StatModifier[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                scaled[i] = new StatModifier(source[i].Stat, source[i].Op, source[i].Value * stacks);
            }
            return scaled;
        }
    }
}
