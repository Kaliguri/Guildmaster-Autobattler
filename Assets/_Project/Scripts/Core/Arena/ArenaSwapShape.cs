using System;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// Форма перехода арены «А → Б» в цифровом стиле: три акта и разнобой клеток внутри них.
    /// Значения — доли, не секунды: одна и та же форма звучит одинаково на любой длительности,
    /// поэтому крутить темп можно одной ручкой (<see cref="DurationSeconds"/>), не ломая ритм.
    /// <list type="number">
    /// <item>Акт 1 (<see cref="DigitizeShare"/>) — реальный вид уходит в каркас.</item>
    /// <item>Акт 2 (остаток) — клетки поштучно меняют текстуру А на Б. Самый длинный.</item>
    /// <item>Акт 3 (<see cref="RestoreShare"/>) — каркас снимается, остаётся готовая арена.</item>
    /// </list>
    /// </summary>
    public readonly struct ArenaSwapShape
    {
        /// <summary>Вся анимация целиком, секунды нескалированного времени.</summary>
        public readonly float DurationSeconds;

        /// <summary>Доля акта 1 (уход в цифру). Короткий: это удар, а не процесс.</summary>
        public readonly float DigitizeShare;

        /// <summary>Доля акта 3 (возврат в реальность).</summary>
        public readonly float RestoreShare;

        /// <summary>
        /// Насколько разнесены МОМЕНТЫ старта соседних клеток внутри акта (0 — все разом, 0.9 — вразнобой
        /// на весь акт). Ноль превращает переход в один общий фейд и убивает эффект «перепрошивки».
        /// </summary>
        public readonly float CellSpread;

        /// <summary>Минимальная длительность щелчка одной клетки, доля акта.</summary>
        public readonly float CellDurationMin;

        /// <summary>Максимальная длительность щелчка одной клетки, доля акта. Разброс = «у каждой своя скорость».</summary>
        public readonly float CellDurationMax;

        /// <summary>
        /// Нарастание темпа в акте 2 (0 — ровно, 1 — вязкое начало и густой финал). Догрузки сбиваются
        /// к концу акта, и у фазы появляется внятный финал: без этого момент «всё, догрузилось» размазан,
        /// и акт 3 наступает по таймеру, а не по смыслу.
        /// </summary>
        public readonly float TailAcceleration;

        public ArenaSwapShape(float durationSeconds, float digitizeShare, float restoreShare,
                              float cellSpread, float cellDurationMin, float cellDurationMax,
                              float tailAcceleration)
        {
            DurationSeconds = durationSeconds < 0.1f ? 0.1f : durationSeconds;
            DigitizeShare   = Clamp(digitizeShare, 0.02f, 0.45f);
            RestoreShare    = Clamp(restoreShare,  0.02f, 0.45f);

            // Акты 1 и 3 не съедают акт 2: середина всегда остаётся главной по длине.
            float sides = DigitizeShare + RestoreShare;
            if (sides > 0.6f)
            {
                float k = 0.6f / sides;
                DigitizeShare *= k;
                RestoreShare  *= k;
            }

            CellSpread      = Clamp(cellSpread, 0f, 0.9f);
            CellDurationMin = Clamp(cellDurationMin, 0.02f, 1f);
            CellDurationMax = Math.Max(CellDurationMin, Clamp(cellDurationMax, 0.02f, 1f));
            TailAcceleration = Clamp(tailAcceleration, 0f, 1f);
        }

        /// <summary>
        /// Дефолт: 2с, длинная середина, заметный разнобой. Было 4.5с — и это читалось как ожидание:
        /// поле собиралось целиком уже к третьей секунде, а последние полсекунды под ним просто висел
        /// каркас. Поэтому вместе с длиной срезана и доля возврата (0.12 → 0.08): каркас должен уходить
        /// сразу за последней клеткой, а не после паузы.
        /// </summary>
        public static ArenaSwapShape Default { get; } =
            new ArenaSwapShape(2f, 0.12f, 0.08f, 0.62f, 0.10f, 0.34f, 0.55f);

        /// <summary>Момент конца акта 1 в общем прогрессе.</summary>
        public float DigitizeEnd => DigitizeShare;

        /// <summary>Момент начала акта 3 в общем прогрессе.</summary>
        public float RestoreStart => 1f - RestoreShare;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
