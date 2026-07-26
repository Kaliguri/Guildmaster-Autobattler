using System;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Шипы: отражает долю полученного урона обратно атакующему. Реактивный — слушает
    /// <see cref="CombatEvent.DamageTaken"/> своего носителя (вики «6» §5.5).
    /// <para><b>Числа:</b> <c>_reflectFraction</c> — доля ПОЛУЧЕННОГО урона, возвращаемая обидчику
    /// (0.15 = 15%); <c>_damageSchool</c> и <c>_affinity</c> — тип ответки. Отличие от
    /// <see cref="ArmorThornsComponent"/>: там урон считается от БРОНИ носителя и бьёт всех вокруг,
    /// здесь — доля конкретного удара и только тому, кто ударил.</para>
    /// <para><b>Когда срабатывает:</b> на каждом полученном уроне. Ответка помечена как реактивная,
    /// поэтому чужие шипы на неё не отвечают — бесконечного пинг-понга нет.</para>
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

        [Tooltip("Школа отражённого урона.")]
        [FormerlySerializedAs("_damageType")]
        [SerializeField] private DamageSchool _damageSchool = DamageSchool.Magical;

        [Tooltip("Сродство отражённого урона.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

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

            ctx.Combat.DealDamage(new DamageRequest(ctx.Target, attacker, reflected, _damageSchool, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Reactive, affinity: _affinity));
        }
    }
}
