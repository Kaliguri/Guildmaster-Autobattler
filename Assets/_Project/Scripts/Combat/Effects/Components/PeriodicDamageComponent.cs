using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Периодический урон (DoT). Потенция задаётся как <b>урон в секунду</b> (per-second rate) и
    /// масштабируется статами источника через <see cref="ScalableValue"/>; за один тик применяется
    /// <c>Potency × Interval × Stacks</c>. Total масштабируется числом тиков (длительностью), а не
    /// запекается — вики «11» §5.1.
    /// </summary>
    [Serializable]
    public sealed class PeriodicDamageComponent : IPeriodicComponent, IScalablePotency
    {
        [Tooltip("Интервал между тиками, сек.")]
        [SerializeField] private float _interval = 1f;

        [Tooltip("Урон В СЕКУНДУ (per-second rate). Скейлится статами источника.")]
        [SerializeField] private ScalableValue _damagePerSecond;

        [SerializeField] private DamageType _damageType = DamageType.Magic;

        public float Interval => _interval;
        public ScalableValue Potency => _damagePerSecond;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            float damage = ctx.Potency * ctx.Dt * ctx.Stacks;
            if (damage <= 0f) return;

            ctx.Combat.DealDamage(new DamageRequest(ctx.Source, ctx.Target, damage, _damageType, ctx.Combat.ArmorK));
        }
    }
}
