using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Чистая детерминированная арифметика тайминга авто-атаки (вики «14»).
    /// Целочисленная по построению — float участвует только в конверсии <c>AttackSpeed → intervalTicks</c>
    /// с явной политикой округления (<see cref="MidpointRounding.AwayFromZero"/>), чтобы исключить
    /// banker's rounding и рассинхрон чек-суммы между сборками/платформами.
    /// <para>
    /// Модель «фикс. длительность свинга + кламп»:
    /// <code>
    /// intervalTicks       = max(1, round(TickRate / AttackSpeed))
    /// attackDurationTicks = min(MaxAttackAnimTicks, intervalTicks)
    /// windupTicks         = clamp((hitFrame * attackDurationTicks) / frameCount, MinWindupTicks, intervalTicks − 1)
    /// </code>
    /// </para>
    /// </summary>
    public static class AttackTiming
    {
        /// <summary>
        /// Интервал между атаками в сим-тиках. Пол <c>≥ 1</c> закрывает край «очень высокий AttackSpeed»
        /// (иначе деление/кламп вырождаются). <paramref name="attackSpeed"/> ≤ 0 → максимально редкая атака.
        /// </summary>
        public static int IntervalTicks(float attackSpeed)
        {
            if (attackSpeed <= 0f) return int.MaxValue;
            int ticks = (int)Math.Round(SimConstants.TickRate / attackSpeed, MidpointRounding.AwayFromZero);
            return ticks < 1 ? 1 : ticks;
        }

        /// <summary>
        /// Длительность свинга в тиках = <c>min(MaxAttackAnimTicks, intervalTicks)</c>.
        /// </summary>
        public static int AttackDurationTicks(int intervalTicks)
        {
            return intervalTicks < SimConstants.MaxAttackAnimTicks ? intervalTicks : SimConstants.MaxAttackAnimTicks;
        }

        /// <summary>
        /// Тики замаха до кадра контакта. Целочисленное деление = floor (детерминированно).
        /// Кламп: нижний — <see cref="SimConstants.MinWindupTicks"/> (телеграф-пол), верхний — <c>intervalTicks − 1</c>
        /// (удар не совпадает с тиком старта следующей атаки). Пустой клип (<paramref name="frameCount"/> ≤ 0)
        /// или <paramref name="hitFrame"/> ≤ 0 → нижний кламп.
        /// </summary>
        /// <summary>
        /// Дополнительное восстановление (сверх анимационного доигрыша) в тиках из секунд. Детерминированное
        /// округление (<see cref="MidpointRounding.AwayFromZero"/>), как у интервала. ≤ 0 сек → 0 тиков.
        /// </summary>
        public static int RecoveryTicks(float seconds)
        {
            if (seconds <= 0f) return 0;
            int ticks = (int)Math.Round(SimConstants.TickRate * seconds, MidpointRounding.AwayFromZero);
            return ticks < 0 ? 0 : ticks;
        }

        /// <summary>
        /// Хвост-доигрыш клипа удара после кадра контакта = <c>attackDurationTicks − windupTicks</c>.
        /// Это «задняя половина» анимации свинга: урон уже нанесён (на кадре контакта), но юнит всё ещё
        /// доигрывает движение и остаётся «занят» (рут либо штраф скорости). Выводится из той же модели,
        /// что и <see cref="WindupTicks"/>, поэтому автоматически масштабируется со скоростью атаки —
        /// в отличие от абсолютных секунд не «расклеивается» при баффах скорости.
        /// <para>
        /// Нет реального клипа (<paramref name="frameCount"/> ≤ 0 или <paramref name="hitFrame"/> ≤ 0) →
        /// windup был чистым телеграфом (пол <see cref="SimConstants.MinWindupTicks"/>), доигрывать нечего → 0.
        /// Вычитание (а не независимая пропорция «хвостовых кадров») сознательно: оно поглощает округление
        /// windup, поэтому <c>windup + доигрыш = attackDurationTicks</c> ровно, без фантомного зазора.
        /// </para>
        /// </summary>
        public static int FollowThroughTicks(int hitFrame, int frameCount, int intervalTicks, int windupTicks)
        {
            if (frameCount <= 0 || hitFrame <= 0) return 0;
            int tail = AttackDurationTicks(intervalTicks) - windupTicks;
            return tail < 0 ? 0 : tail;
        }

        /// <summary>
        /// Тики замаха, которые получит СЛЕДУЮЩИЙ свинг этого юнита при текущей скорости атаки и клипе.
        /// Свёртка <see cref="IntervalTicks"/> + <see cref="WindupTicks"/> над данными юнита — чтобы гейт
        /// атаки и движение (предсказание «докрутит ли замах», <c>CombatPositioning.CanLandWindup</c>)
        /// считали ту же длину, что и <c>AutoAttackSystem.EnterWindup</c>. Пустой визуал → нижний кламп.
        /// </summary>
        public static int WindupTicksFor(RuntimeUnit unit)
        {
            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = IntervalTicks(attackSpeed);

            UnitVisual visual = unit.Unit != null ? unit.Unit.Visual : null;
            int frameCount = visual != null ? visual.AttackFrameCount : 0;
            int hitFrame   = visual != null ? visual.AttackHitFrame  : 0;

            return WindupTicks(hitFrame, frameCount, intervalTicks);
        }

        public static int WindupTicks(int hitFrame, int frameCount, int intervalTicks)
        {
            int upper = intervalTicks - 1;
            if (upper < 0) upper = 0;

            int lower = SimConstants.MinWindupTicks;
            if (lower > upper) lower = upper;   // очень короткий интервал: пол не может превысить потолок

            int raw;
            if (frameCount <= 0 || hitFrame <= 0)
            {
                raw = 0;
            }
            else
            {
                int durationTicks = AttackDurationTicks(intervalTicks);
                int clampedHit = hitFrame < frameCount ? hitFrame : frameCount; // hitFrame не больше числа кадров
                raw = (clampedHit * durationTicks) / frameCount;
            }

            if (raw < lower) raw = lower;
            if (raw > upper) raw = upper;
            return raw;
        }
    }
}
