using System;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Вампиризм: исцеляет носителя на долю нанесённого им урона. Реактивный — слушает
    /// <see cref="CombatEvent.DamageDealt"/> своего носителя (вики «6» §5.5).
    /// </summary>
    /// <remarks>
    /// Доля авторится на компоненте (самодостаточно и тестируемо). Стат <c>Lifesteal</c> остаётся
    /// объявленным; его автоматическая привязка к пути урона — отдельное решение позже.
    /// </remarks>
    [Serializable]
    public sealed class LifestealComponent : IReactiveComponent
    {
        [Tooltip("Доля нанесённого урона, возвращаемая в HP (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _fraction = 0.1f;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Носитель (ctx.Target) = тот, кто нанёс урон. Исцеляем его.
            float heal = e.Amount * _fraction * ctx.Stacks;
            if (heal <= 0f) return;

            ctx.Combat.Heal(ctx.Target, heal, ctx.Target);
        }
    }
}
