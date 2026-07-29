using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Стат-модификатор, который СПАДАЕТ к нулю за время жизни эффекта: в момент наложения работает в
    /// полную величину, к последнему тику — почти не работает («Стальной вихрь» Копейщика: замедление
    /// на 80%, ровно за секунду сходящее до 0%).
    /// <para><b>Числа:</b> <c>_modifiers</c> — правки статов в ПОЛНУЮ силу, то есть какими они будут в
    /// первый тик. Дальше каждая домножается на долю оставшейся длительности: у секундного эффекта на
    /// половине пути от −80% остаётся −40%.</para>
    /// <para><b>Чем отличается от <see cref="StatModifierComponent"/>:</b> тот держит величину ровной
    /// всю длительность и снимает её щелчком в конце. Здесь щелчка нет — дебаф отпускает постепенно, и
    /// это читается игроком как «вырывается из вихря», а не «вдруг отпустило».</para>
    /// <para><b>Когда срабатывает:</b> при наложении и затем КАЖДЫЙ сим-тик (интервал = шаг тика).
    /// Пересчёт — снятие своей группы и наложение новой; обе правки отложены до конца тика, поэтому
    /// закон видимости эффектов соблюдён так же, как у обычного модификатора.</para>
    /// <para><b>Готча:</b> доля берётся от <see cref="RuntimeEffect.FullDurationTicks"/>, поэтому у
    /// ПОСТОЯННОГО эффекта (пассивки) затухать нечему — там компонент ведёт себя как обычный
    /// модификатор и держит полную величину. Ставить его на пассивку бессмысленно, но не опасно.</para>
    /// </summary>
    [Serializable]
    public sealed class DecayingStatModifierComponent : IPeriodicComponent
    {
        [Tooltip("Модификаторы в ПОЛНУЮ силу (первый тик). Далее домножаются на долю остатка длительности.")]
        [SerializeField] private StatModifier[] _modifiers;

        /// <summary>Пересчёт каждый сим-тик: спад должен быть плавным, а не ступенчатым.</summary>
        public float Interval => SimConstants.TickDelta;

        public void OnApply(in EffectContext ctx) => Reapply(in ctx);

        public void OnExpire(in EffectContext ctx) => ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);

        public void OnTick(in EffectContext ctx) => Reapply(in ctx);

        // Снять свою группу и положить пересчитанную. Порядок внутри отложенной очереди сохраняется
        // (Stats.Commit идёт по списку), поэтому пара «снять → наложить» даёт ровно новую величину.
        private void Reapply(in EffectContext ctx)
        {
            if (_modifiers == null || _modifiers.Length == 0 || ctx.Target?.Stats == null) return;

            float scale = RemainingFraction(ctx.Effect) * ctx.Stacks;

            ctx.Target.Stats.RemoveModifiersFrom(ctx.Effect, deferred: true);
            ctx.Target.Stats.AddModifiersFrom(ctx.Effect, Scale(_modifiers, scale), deferred: true);
        }

        // Доля оставшейся длительности [0..1]. Постоянный эффект затухать не может — у него полная сила.
        private static float RemainingFraction(RuntimeEffect effect)
        {
            if (effect == null || effect.IsPermanent) return 1f;
            if (effect.FullDurationTicks <= 0) return 0f;

            float fraction = effect.RemainingTicks / (float)effect.FullDurationTicks;
            return fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
        }

        private static StatModifier[] Scale(StatModifier[] source, float scale)
        {
            var scaled = new StatModifier[source.Length];
            for (int i = 0; i < source.Length; i++)
                scaled[i] = new StatModifier(source[i].Stat, source[i].Op, source[i].Value * scale);
            return scaled;
        }
    }
}
