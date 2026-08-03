using System;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Вампиризм: исцеляет носителя на долю нанесённого им урона. Реактивный — слушает
    /// <see cref="CombatEvent.DamageDealt"/> своего носителя (вики «6» §5.5).
    /// <para><b>Числа:</b> <c>_fraction</c> — доля нанесённого урона, возвращаемая себе (0.1 = 10%).
    /// В отличие от «Целебного света» лечит ТОЛЬКО носителя и не ищет раненых рядом.</para>
    /// <para><b>Когда срабатывает:</b> на каждом нанесённом уроне носителя — включая тики DoT и
    /// ответки, если их не отфильтровал сам эффект.</para>
    /// </summary>
    /// <remarks>
    /// ⚠️ ИЗБЫТОЧЕН под моделью «эффекты кормят статы» (решение 2026-06-19, см. вики
    /// «Гайд по архитектуре кода» → 07 §1). Канонический путь вампиризма теперь — стат
    /// <c>Lifesteal</c>, читаемый в <c>CombatSimulation.DealDamage</c>; временный «вампирик-бафф»
    /// делается эффектом со <c>StatModifierComponent(+Lifesteal)</c>. Этот реактивный компонент
    /// лечит НАПРЯМУЮ, минуя стат — НЕ вешать его вместе со стат-вампиризмом на одного юнита
    /// (двойной учёт). Оставлен временно (на нём держится <c>ReactiveEffectTests</c>); удаление +
    /// миграция теста — когда начнётся авторинг контента (техдолг 07 §3.7).
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
