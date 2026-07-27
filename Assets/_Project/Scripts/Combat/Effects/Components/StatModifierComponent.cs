using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Накладывает стат-модификаторы на носителя на время жизни эффекта. Источник снятия —
    /// сам <see cref="RuntimeEffect"/>, поэтому моды уходят разом на <c>OnExpire</c>. При стаках
    /// величина линейно умножается на число стаков (вики «6» §5.5).
    /// <para><b>Числа:</b> <c>_modifiers</c> — список правок статов, у каждой свой стат, операция
    /// (плоско / процент / перекрыть) и величина. Это рабочая лошадь всех бафов и дебафов: замедление,
    /// разгон темпа, прибавка брони и запаса — всё делается здесь, а не отдельными компонентами.</para>
    /// <para><b>Когда срабатывает:</b> моды висят, пока висит эффект; при смене числа стаков вклад
    /// пересчитывается целиком.</para>
    /// </summary>
    [Serializable]
    public sealed class StatModifierComponent : IRuntimeEffectComponent
    {
        [Tooltip("Модификаторы, накладываемые на время эффекта. На каждый стак — линейно ×Stacks.")]
        [SerializeField] private StatModifier[] _modifiers;

        /// <para><b>Готча:</b> правка стата ОТЛОЖЕНА до конца тика (<c>Stats.Commit</c>) — так велит закон
        /// видимости эффектов. Наложенный этим тиком баф или ослабление вступает в силу со следующего, и
        /// потому одинаково для всех: иначе исход решало бы то, чей ход в обходе списка раньше.</para>
        public void OnApply(in EffectContext ctx)
        {
            if (_modifiers == null || _modifiers.Length == 0 || ctx.Target?.Stats == null) return;

            StatModifier[] mods = ctx.Stacks == 1 ? _modifiers : ScaleByStacks(_modifiers, ctx.Stacks);
            ctx.Target.Stats.AddModifiersFrom(ctx.Effect, mods, deferred: true);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
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
