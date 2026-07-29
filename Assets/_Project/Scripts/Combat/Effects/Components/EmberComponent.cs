using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Угли» (карточка [[burn]]): стаки на ЦЕЛИ, каждый усиливает входящий по ней урон огнём.
    /// Потолка стаков нет — затяжной размен должен окупаться (решение 2026-07-26/4). Сдерживает их
    /// не потолок, а сход: пока подпитка идёт, стаки держатся; как только огонь прекратился, они
    /// уходят по одному со всё возрастающей скоростью.
    /// <para><b>Числа:</b> <c>_fireDamagePerStack</c> — прибавка к входящему ОГНЕННОМУ урону за стак
    /// (0.01 = +1%, множители складываются: 10 угольков = +10%); <c>_graceSeconds</c> — сколько стаки
    /// держатся без подпитки, прежде чем начать осыпаться; <c>_firstDecaySeconds</c> — пауза перед
    /// первым уходящим стаком; <c>_decayFalloff</c> — во сколько раз короче пауза перед каждым
    /// следующим (0.75 = на четверть быстрее); <c>_minDecaySeconds</c> — предел ускорения схода.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage (усиливает огонь по носителю) и каждый тик (ведёт
    /// сход). Одно очищение уносит не всё: цена в стаках живёт в самом ассете эффекта.</para>
    /// </summary>
    /// <remarks>
    /// Stateless, как и положено компоненту: таймер схода живёт в <see cref="RuntimeEffect.TimerTick"/>
    /// и <see cref="RuntimeEffect.TimerIntervalTicks"/>. Эффект должен быть **бессрочным**
    /// (<c>baseDuration = -1</c>) — иначе он истечёт целиком вместо постепенного схода — и
    /// стакающимся (<c>StackRule.Stack</c>): каждый уголёк добавляет стак и отодвигает сход.
    /// </remarks>
    [Serializable]
    public sealed class EmberComponent : IPreDamageComponent, IPeriodicComponent, IStackableComponent
    {
        [Tooltip("Прибавка к входящему урону ОГНЁМ за каждый стак (0.01 = +1%).")]
        [SerializeField] private float _fireDamagePerStack = 0.01f;

        [Tooltip("Сколько секунд после последнего уголька стаки держатся нетронутыми.")]
        [SerializeField] private float _graceSeconds = 5f;

        [Tooltip("Интервал первого схода после льготного окна, сек.")]
        [SerializeField] private float _firstDecaySeconds = 1f;

        [Tooltip("Во сколько раз укорачивается интервал каждого следующего схода (0.75 = на четверть быстрее).")]
        [SerializeField] private float _decayFalloff = 0.75f;

        [Tooltip("Нижняя граница интервала схода, сек — быстрее стаки не осыпаются.")]
        [SerializeField] private float _minDecaySeconds = 0.25f;

        /// <summary>Тикаем часто: сход управляется собственным таймером, период — лишь разрешение опроса.</summary>
        public float Interval => _minDecaySeconds;

        public void OnApply(in EffectContext ctx) => ResetDecay(in ctx);

        public void OnExpire(in EffectContext ctx) { }

        /// <summary>Новый уголёк лёг сверху — сход откладывается и начинает отсчёт заново (канон [[burn]]).</summary>
        public void OnStacksChanged(int previousStacks, in EffectContext ctx) => ResetDecay(in ctx);

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (!incoming.IsFire) return; // «Угли» усиливают только огонь — любой, от кого бы он ни шёл

            int stacks = ctx.Stacks;
            if (stacks <= 0 || _fireDamagePerStack <= 0f) return;

            result.AddMultiplier(1f + _fireDamagePerStack * stacks);
        }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            int now = ctx.Combat.CurrentTick;
            if (!eff.IsTimerDue(now)) return; // ещё держатся

            // Сошёл один стак; следующий уйдёт быстрее — вплоть до нижней границы.
            eff.RemoveStacks(1);
            if (eff.Stacks <= 0)
            {
                eff.EndDuration(); // стаков не осталось — эффект снимается штатно
                return;
            }

            // Перевзвод на СВОЙ интервал, а укорачивание — уже для следующего раза: почему именно так,
            // сказано в RuntimeEffect.RescheduleTimer (иначе первая секунда молча станет 0.75).
            int minTicks     = Mathf.Max(1, Mathf.RoundToInt(_minDecaySeconds * SimConstants.TickRate));
            int nextInterval = Mathf.RoundToInt(eff.TimerIntervalTicks * _decayFalloff);
            eff.RescheduleTimer(now, Mathf.Max(minTicks, nextInterval));
        }

        private void ResetDecay(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            int now = ctx.Combat.CurrentTick;

            int graceTicks = Mathf.Max(1, Mathf.RoundToInt(_graceSeconds * SimConstants.TickRate));
            int firstTicks = Mathf.Max(1, Mathf.RoundToInt(_firstDecaySeconds * SimConstants.TickRate));
            eff.ScheduleTimer(now + graceTicks, firstTicks);
        }
    }
}
