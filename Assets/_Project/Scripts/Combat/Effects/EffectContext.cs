using Guildmaster.Core.Random;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Контекст, передаваемый компоненту эффекта (<c>in</c>). Даёт доступ к цели, источнику,
    /// шву влияния на мир (<see cref="ICombatContext"/>), снимку потенции для текущего компонента
    /// и числу стаков. Так компоненты остаются stateless и юнит-тестируемы с мок-контекстом
    /// (вики «6» §5.1, «12» §3.2).
    /// </summary>
    public readonly struct EffectContext
    {
        /// <summary>Носитель эффекта.</summary>
        public readonly RuntimeUnit Target;

        /// <summary>Кто наложил эффект.</summary>
        public readonly RuntimeUnit Source;

        /// <summary>Шов влияния на симуляцию (урон/хил/спавн/запрос/наложение).</summary>
        public readonly ICombatContext Combat;

        /// <summary>Рантайм-экземпляр эффекта (стаки, длительность).</summary>
        public readonly RuntimeEffect Effect;

        /// <summary>Снимок потенции ИМЕННО этого компонента (per-second rate / величина), резолв при наложении.</summary>
        public readonly float Potency;

        /// <summary>Шаг времени, сек. Для периодики применяемое = <see cref="Potency"/> × Interval.</summary>
        public readonly float Dt;

        public EffectContext(
            RuntimeUnit target,
            RuntimeUnit source,
            ICombatContext combat,
            RuntimeEffect effect,
            float potency,
            float dt)
        {
            Target  = target;
            Source  = source;
            Combat  = combat;
            Effect  = effect;
            Potency = potency;
            Dt      = dt;
        }

        /// <summary>Детерминированный RNG боя (через шов).</summary>
        public IRngService Rng => Combat.Rng;

        /// <summary>Число стаков эффекта (≥ 1).</summary>
        public int Stacks => Effect != null ? Effect.Stacks : 1;
    }
}
