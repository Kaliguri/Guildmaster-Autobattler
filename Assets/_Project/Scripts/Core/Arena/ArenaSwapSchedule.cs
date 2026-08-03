using System;

namespace Guildmaster.Core.Arena
{
    /// <summary>Состояние одной клетки на данный момент перехода. Все три поля — 0..1.</summary>
    public readonly struct ArenaCellPhase
    {
        /// <summary>Акт 1: ушла ли клетка в каркас (0 — реальный вид, 1 — цифра).</summary>
        public readonly float Digitize;

        /// <summary>Акт 2: подгрузка новой текстуры (0 — ещё старая, 1 — уже новая).</summary>
        public readonly float Load;

        /// <summary>Акт 3: возврат в реальность (0 — ещё цифра, 1 — реальный вид).</summary>
        public readonly float Restore;

        public ArenaCellPhase(float digitize, float load, float restore)
        {
            Digitize = digitize;
            Load     = load;
            Restore  = restore;
        }

        /// <summary>Насколько клетка сейчас «в цифре»: поднялась в каркас и ещё не вернулась.</summary>
        public float Digital
        {
            get
            {
                float d = Digitize - Restore;
                return d < 0f ? 0f : (d > 1f ? 1f : d);
            }
        }

        /// <summary>Текстуру какого скина показывать: false — исходного (А), true — целевого (Б).</summary>
        public bool ShowsTarget => Load >= 0.5f;

        /// <summary>
        /// Ступень разрешения подгружаемой текстуры: 1 (один цвет на клетку), 2 (четверти) или 4 (как есть).
        /// Это и есть «прогрузка текстурок»: клетка проявляется не сразу целиком, а мипами.
        /// </summary>
        public int MipSteps
        {
            get
            {
                if (Load <= 0f || Load >= 1f) return 4;
                if (Load < 0.62f) return 1;   // только что переключилась — плоское пятно среднего цвета
                if (Load < 0.86f) return 2;   // четверти
                return 4;                     // полное разрешение
            }
        }
    }

    /// <summary>Акт перехода — для запроса «когда эта клетка его проходит» (<see cref="ArenaSwapSchedule.CrossTime"/>).</summary>
    public enum ArenaSwapAct
    {
        /// <summary>Уход в каркас.</summary>
        Digitize = 0,

        /// <summary>Смена текстуры на новую.</summary>
        Load = 1,

        /// <summary>Возврат в реальный вид.</summary>
        Restore = 2,
    }

    /// <summary>
    /// Расписание перехода: по общему прогрессу и координате клетки выдаёт фазы трёх актов.
    /// Чистая математика без Unity — гоняется тестами и, что важнее, ОДИН раз считается на клетку,
    /// а результат пекётся в карту для шейдера. Хеш здесь целочисленный, а не через <c>sin()</c>:
    /// шейдерный <c>sin</c> расходится с C# в младших разрядах, и каркас поехал бы мимо тайлов.
    /// </summary>
    public sealed class ArenaSwapSchedule
    {
        private readonly ArenaSwapShape _shape;

        public ArenaSwapSchedule(in ArenaSwapShape shape) => _shape = shape;

        public ArenaSwapShape Shape => _shape;

        /// <summary>Состояние клетки (<paramref name="cellX"/>, <paramref name="cellY"/>) в момент <paramref name="t"/> (0..1).</summary>
        public ArenaCellPhase Sample(float t, int cellX, int cellY)
        {
            t = Clamp01(t);
            return new ArenaCellPhase(
                ActPhase(t, 0f,                   _shape.DigitizeEnd,  cellX, cellY, 1, 0f),
                ActPhase(t, _shape.DigitizeEnd,   _shape.RestoreStart, cellX, cellY, 2, _shape.TailAcceleration),
                ActPhase(t, _shape.RestoreStart,  1f,                  cellX, cellY, 3, 0f));
        }

        /// <summary>
        /// Ход одного акта для одной клетки. Клетка стартует в свой момент (<c>start</c>) и идёт свою
        /// длительность (<c>dur</c>) — отсюда «везде сразу, но у каждой своя скорость». Границы подобраны
        /// так, что <c>start + dur ≤ 1</c>: к концу акта не остаётся недоигранных клеток.
        /// </summary>
        private float ActPhase(float t, float lo, float hi, int x, int y, int salt, float tail)
        {
            if (t <= lo) return 0f;
            if (t >= hi) return 1f;

            float g = (t - lo) / (hi - lo);
            if (tail > 0f) g = (float)Math.Pow(g, 1f + tail * 0.9f); // хвост догружается быстрее — у фазы есть финал

            float dur    = _shape.CellDurationMin +
                           Hash01(x, y, salt + 31) * (_shape.CellDurationMax - _shape.CellDurationMin);
            float start  = Hash01(x, y, salt) * _shape.CellSpread * (1f - dur);
            return Clamp01((g - start) / dur);
        }

        /// <summary>
        /// Момент (в общем прогрессе 0..1), когда клетка проходит середину указанного акта: переворачивается
        /// на новый тайл, уходит в каркас или возвращается в реальность.
        /// <para>Единственный источник этих моментов: по ним и подменяются тайлы, и рисуется каркас. Считай
        /// их порознь — оверлей однажды поедет мимо клеток, которые меняются, и никто не поймёт почему.</para>
        /// <para>Ход акта монотонен, поэтому момент ищется делением пополам — точнее и дешевле, чем гнать
        /// расписание мелкими шагами по времени.</para>
        /// </summary>
        public float CrossTime(ArenaSwapAct act, int cellX, int cellY)
        {
            float lo, hi;
            switch (act)
            {
                case ArenaSwapAct.Digitize: lo = 0f;                   hi = _shape.DigitizeEnd;  break;
                case ArenaSwapAct.Restore:  lo = _shape.RestoreStart;  hi = 1f;                  break;
                default:                    lo = _shape.DigitizeEnd;   hi = _shape.RestoreStart; break;
            }

            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Passed(act, Sample(mid, cellX, cellY))) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        private static bool Passed(ArenaSwapAct act, in ArenaCellPhase phase)
        {
            switch (act)
            {
                case ArenaSwapAct.Digitize: return phase.Digitize >= 0.5f;
                case ArenaSwapAct.Restore:  return phase.Restore  >= 0.5f;
                default:                    return phase.ShowsTarget;
            }
        }

        /// <summary>
        /// Детерминированный хеш клетки в [0,1). Целочисленный (Wang-подобный) — одинаков на любой
        /// платформе и в любом порядке обхода, в отличие от тригонометрических хешей.
        /// </summary>
        public static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x85EBCA6Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
