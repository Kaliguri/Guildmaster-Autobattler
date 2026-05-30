using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Поглощающий щит. Величина (скейлится статами источника) добавляется к <c>CurrentShield</c>
    /// при наложении и снимается остатком при истечении. На стаки — линейно ×Stacks.
    /// </summary>
    [Serializable]
    public sealed class ShieldComponent : IRuntimeEffectComponent, IScalablePotency
    {
        [Tooltip("Величина щита. Скейлится статами источника (напр. AbilityPower).")]
        [SerializeField] private ScalableValue _amount;

        public ScalableValue Potency => _amount;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Target.CurrentShield += ctx.Potency * ctx.Stacks;
        }

        public void OnExpire(in EffectContext ctx)
        {
            // Снимаем не больше, чем сейчас есть (часть могла быть поглощена уроном).
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield - ctx.Potency * ctx.Stacks);
        }
    }
}
