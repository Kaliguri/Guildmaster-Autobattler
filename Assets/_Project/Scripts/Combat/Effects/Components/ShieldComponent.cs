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
    public sealed class ShieldComponent : IStackableComponent, IScalablePotency
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

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак добавляет только вклад НОВЫХ стаков (дельту), не трогая уже поглощённую уроном
            // часть пула. Дефолтный OnExpire→OnApply тут пере-вычитал бы: OnExpire с новым Stacks
            // и Mathf.Max клампит остаток в ноль, съедая частично израсходованный щит (07 §3.8 B1).
            float delta = ctx.Potency * (ctx.Stacks - previousStacks);
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield + delta);
        }
    }
}
