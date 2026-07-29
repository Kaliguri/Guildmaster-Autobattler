using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Поглощающий щит. Величина (скейлится статами источника) добавляется к <c>CurrentShield</c>
    /// при наложении и снимается остатком при истечении. На стаки — линейно ×Stacks.
    /// <para><b>Числа:</b> <c>_amount</c> — сколько урона поглотит щит, в единицах HP. Всё остальное
    /// решает сам эффект: длительность (короткий щит гасит один удар, длинный копит поглощение),
    /// стакинг (стаки складываются линейно) и стойкость к снятию.</para>
    /// <para><b>Когда срабатывает:</b> при наложении — величина считается один раз и не
    /// пересчитывается; при истечении неизрасходованный остаток снимается.</para>
    /// </summary>
    [Serializable]
    public sealed class ShieldComponent : IStackableComponent, IScalablePotency
    {
        [Tooltip("Величина щита. Скейлится статами источника (напр. AbilityPower).")]
        [SerializeField] private ScalableValue _amount;

        public ScalableValue Potency => _amount;

        public void OnApply(in EffectContext ctx)
        {
            float amount = ctx.Potency * ctx.Stacks;
            ctx.Target.CurrentShield += amount;

            // Размер щита записывается в сам эффект, а не остаётся выводимым из контекста: реактивам
            // (взрыв щита, M17) потенция недоступна — они приходят другим путём, без снимка статов.
            ctx.Effect.PendingShield = amount;
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
            ctx.Effect.PendingShield = ctx.Potency * ctx.Stacks;   // размер вырос вместе со стаками
        }
    }
}
