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
    /// (0.15 = 15%); <c>_damageType</c> — тип ответки. Отличие от
    /// <see cref="ArmorThornsComponent"/>: там урон считается от БРОНИ носителя и бьёт всех вокруг,
    /// здесь — доля конкретного удара и только тому, кто ударил.</para>
    /// <para><b>Когда срабатывает:</b> ТОЛЬКО на прямом попадании — авто-атака или способность.
    /// Тик яда, горение и чужая ответка шипов проходят мимо: шипы отвечают удару, а не тлеющему
    /// урону, и шипы не отвечают шипам (решение Макса 2026-07-27).</para>
    /// </summary>
    /// <remarks>
    /// Гейт по <see cref="CombatEventData.IsDirectHit"/> — он же закрывает пинг-понг взаимных шипов
    /// в корне: ответка помечена <see cref="DamageSourceKind.Reactive"/> и второго круга не порождает.
    /// Раньше это утверждала только докстринга, а гейта в коде не было — цепочка реально ходила по
    /// кругу и гасла лишь капом. Порог значимости и кап раундов остались страховкой на будущий контент.
    /// </remarks>
    [Serializable]
    public sealed class ThornsComponent : IReactiveComponent
    {
        [Tooltip("Доля полученного урона, отражаемая атакующему (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _reflectFraction = 0.15f;

        [Tooltip("Школа отражённого урона.")]
        [FormerlySerializedAs("_damageType")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Шипы отвечают только удару: ни DoT, ни чужая ответка их не будят.
            if (!e.IsDirectHit) return;

            // Носитель (ctx.Target) получил урон; источник e.Source — атакующий.
            RuntimeUnit attacker = e.Source;
            if (attacker == null || attacker.IsDead) return;

            float reflected = e.Amount * _reflectFraction * ctx.Stacks;
            if (reflected <= 0f) return;

            ctx.Combat.DealDamage(new DamageRequest(ctx.Target, attacker, reflected, _damageType, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Reactive));
        }
    }
}
