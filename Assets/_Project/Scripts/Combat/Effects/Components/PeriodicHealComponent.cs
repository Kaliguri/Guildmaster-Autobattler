using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Периодическое исцеление (HoT). Потенция — <b>хил в секунду</b>; за тик применяется
    /// <c>Potency × Interval × Stacks</c> через <see cref="ICombatContext.Heal"/> (там действуют
    /// HealShield-эффективности). Total масштабируется длительностью (вики «11» §5.1).
    /// <para><b>Числа:</b> <c>_healPerSecond</c> — лечение В СЕКУНДУ (не за тик), масштабируется
    /// статами источника; <c>_interval</c> — как часто капает, секунды. Частота не меняет суммарный
    /// хил, только его дробность: реже, но крупнее — заметнее глазу и уязвимее к добиванию между тиками.</para>
    /// <para><b>Когда срабатывает:</b> каждые <c>_interval</c> секунд, пока эффект висит.</para>
    /// </summary>
    [Serializable]
    public sealed class PeriodicHealComponent : IPeriodicComponent, IScalablePotency
    {
        [Tooltip("Интервал между тиками, сек.")]
        [SerializeField] private float _interval = 1f;

        [Tooltip("Исцеление В СЕКУНДУ (per-second rate). Скейлится статами источника.")]
        [SerializeField] private ScalableValue _healPerSecond;

        [Tooltip("Насколько каждый СЛЕДУЮЩИЙ стак слабее предыдущего (0.3 = на 30%). 0 = стаки складываются " +
                 "линейно, как раньше. Убывание нужно там, где первый стак должен быть весомым, а гора " +
                 "стаков — не превращаться в неубиваемость: «Грибной покров» Друида (вердикт Макса 04.08).")]
        [Range(0f, 0.9f)]
        [SerializeField] private float _stackFalloffPct;

        public float Interval => _interval;
        public ScalableValue Potency => _healPerSecond;

        /// <summary>
        /// Во сколько «полных стаков» превращаются <paramref name="stacks"/> с учётом убывания: первый
        /// идёт целиком, каждый следующий — со скидкой. Считается циклом, а не степенью: стаков единицы,
        /// а <c>Mathf.Pow</c> в детерминированной симуляции — лишний источник расхождения между платформами.
        /// </summary>
        public static float EffectiveStacks(int stacks, float falloff)
        {
            if (stacks <= 0) return 0f;
            if (falloff <= 0f) return stacks;

            float weight = 1f;
            float total = 0f;
            for (int i = 0; i < stacks; i++)
            {
                total += weight;
                weight *= 1f - falloff;
            }
            return total;
        }

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            // Share — доля вкладчика (см. PeriodicDamageComponent): хил тоже засчитывается тому,
            // кто держит эффект, а суммарная величина за тик не меняется.
            float heal = ctx.Potency * ctx.Dt * EffectiveStacks(ctx.Stacks, _stackFalloffPct) * ctx.Share;
            if (heal <= 0f) return;

            ctx.Combat.Heal(ctx.Target, heal, ctx.Source);
        }
    }
}
