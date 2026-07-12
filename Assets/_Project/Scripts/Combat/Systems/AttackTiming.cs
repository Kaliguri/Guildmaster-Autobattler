using System;
using Guildmaster.Core.Simulation;

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
        /// Восстановление (хвост-бэксвинг) после удара в тиках из секунд. Детерминированное округление
        /// (<see cref="MidpointRounding.AwayFromZero"/>), как у интервала. ≤ 0 сек → 0 тиков (нет восстановления).
        /// </summary>
        public static int RecoveryTicks(float seconds)
        {
            if (seconds <= 0f) return 0;
            int ticks = (int)Math.Round(SimConstants.TickRate * seconds, MidpointRounding.AwayFromZero);
            return ticks < 0 ? 0 : ticks;
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
