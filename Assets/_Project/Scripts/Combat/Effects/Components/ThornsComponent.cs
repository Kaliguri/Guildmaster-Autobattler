using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Шипы: отражает долю полученного урона обратно атакующему. Реактивный — слушает
    /// <see cref="CombatEvent.DamageTaken"/> своего носителя (вики «6» §5.5).
    /// </summary>
    /// <remarks>
    /// Отражённый урон идёт обычным <see cref="ICombatContext.DealDamage"/> и сам порождает
    /// события — взаимные шипы пинг-понгуют, но дренаж очереди капается в
    /// <c>CombatSimulation</c> (детерминированно завершается, без рекурсии).
    /// </remarks>
    [Serializable]
    public sealed class ThornsComponent : IReactiveComponent
    {
        [Tooltip("Доля полученного урона, отражаемая атакующему (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _reflectFraction = 0.15f;

        [SerializeField] private DamageType _damageType = DamageType.Magic;

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Носитель (ctx.Target) получил урон; источник e.Source — атакующий.
            RuntimeUnit attacker = e.Source;
            if (attacker == null || attacker.IsDead) return;

            float reflected = e.Amount * _reflectFraction * ctx.Stacks;
            if (reflected <= 0f) return;

            ctx.Combat.DealDamage(new DamageRequest(ctx.Target, attacker, reflected, _damageType, ctx.Combat.ArmorK));
        }
    }
}
