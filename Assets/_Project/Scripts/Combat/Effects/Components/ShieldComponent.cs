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

        [Tooltip("Прибавка к прочности, когда щит наложен НА СЕБЯ (0.5 = +50%). Монах воды: свою воду он " +
                 "держит крепче, чем чужую. 0 = без разницы, кому.")]
        [SerializeField] private float _selfBonusPct;

        public ScalableValue Potency => _amount;

        /// <summary>
        /// Прочность одного стака для этой пары «источник → носитель»: базовая потенция, плюс надбавка,
        /// если щит выдан самому себе. Считается в одном месте, потому что путь наложения и путь рестака
        /// обязаны видеть одно и то же число — иначе стак дорастит щит не на ту величину.
        /// </summary>
        private float PerStack(in EffectContext ctx)
        {
            bool onSelf = ReferenceEquals(ctx.Source, ctx.Target);
            return onSelf ? ctx.Potency * (1f + _selfBonusPct) : ctx.Potency;
        }

        public void OnApply(in EffectContext ctx)
        {
            float amount = PerStack(in ctx) * ctx.Stacks;
            ctx.Target.CurrentShield += amount;

            // Размер щита записывается в сам эффект, а не остаётся выводимым из контекста: реактивам
            // (взрыв щита, M17) потенция недоступна — они приходят другим путём, без снимка статов.
            ctx.Effect.HoldShield(amount);
        }

        public void OnExpire(in EffectContext ctx)
        {
            // Снимаем ровно то, что этот эффект держал, а не пересчитываем формулу заново: у величины
            // один владелец (§HeldShield). Пересчёт врал бы всякий раз, когда стаки менялись в этом же
            // тике, — ctx.Stacks отдаёт снимок начала тика, а держали мы уже другое число.
            // Не больше, чем сейчас есть: часть могла быть поглощена уроном.
            float held = ctx.Effect.ReleaseHeldShield();
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield - held);
        }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак добавляет только вклад НОВЫХ стаков (дельту), не трогая уже поглощённую уроном
            // часть пула. Дефолтный OnExpire→OnApply тут пере-вычитал бы: OnExpire с новым Stacks
            // и Mathf.Max клампит остаток в ноль, съедая частично израсходованный щит (07 §3.8 B1).
            float delta = PerStack(in ctx) * (ctx.Stacks - previousStacks);
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield + delta);
            // Держим ровно дельту сверх прежнего: щит вырос вместе со стаками, но уже поглощённую
            // уроном часть пула это не возвращает.
            ctx.Effect.AddHeldShield(delta);
        }
    }
}
