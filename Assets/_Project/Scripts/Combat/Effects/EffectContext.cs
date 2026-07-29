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

        /// <summary>
        /// Доля этого прохода в тике периодики (0..1). Эффект живёт одним экземпляром на цели, но
        /// поддерживать его могут несколько юнитов — тогда тик прогоняется по вкладчикам, и каждый
        /// получает свою долю урона на СВОЙ счёт (реш. Макса 2026-07-26). Один вкладчик — доля 1,
        /// то есть поведение ровно прежнее.
        /// </summary>
        public readonly float Share;

        /// <summary>
        /// Читать ЖИВОЕ число стаков вместо снимка начала тика. Ставится только на пути пересчёта
        /// stateful-вклада (<c>OnStacksChanged</c>): набранный стак обязан сразу дорастить щит и
        /// стат-модификатор, иначе прибавка не случится никогда — второго вызова не будет.
        /// </summary>
        private readonly bool _liveStacks;

        public EffectContext(
            RuntimeUnit target,
            RuntimeUnit source,
            ICombatContext combat,
            RuntimeEffect effect,
            float potency,
            float dt,
            float share = 1f,
            bool liveStacks = false)
        {
            Target      = target;
            Source      = source;
            Combat      = combat;
            Effect      = effect;
            Potency     = potency;
            Dt          = dt;
            Share       = share;
            _liveStacks = liveStacks;
        }

        /// <summary>Детерминированный RNG боя (через шов).</summary>
        public IRngService Rng => Combat.Rng;

        /// <summary>
        /// Число стаков эффекта (≥ 1) — СНИМОК на начало тика, потому что стаками компонент влияет на
        /// мир, а закон видимости не пускает внутритиковые правки в исход (см.
        /// <see cref="RuntimeEffect.StacksAtTickStart"/>). Живое значение отдаётся только пути
        /// пересчёта вклада — там оно и нужно.
        /// </summary>
        public int Stacks
        {
            get
            {
                if (Effect == null) return 1;
                return _liveStacks ? Effect.Stacks : Effect.VisibleStacks;
            }
        }
    }
}
