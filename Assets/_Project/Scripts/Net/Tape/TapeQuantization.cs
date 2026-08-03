using UnityEngine;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Как боевые числа превращаются в короткие целые для отправки. Владелец шага квантования один: и
    /// писатель, и читатель берут его отсюда, иначе картинка у гостя разъехалась бы на масштабе.
    /// <para><b>Квантуются только СНИМКИ — то, из чего рисуется кадр.</b> Числа событий (урон, лечение)
    /// едут полным <c>float</c>: их пара сотен на бой, они попадают в цифры на экране и в аудит, и
    /// экономить на них нечего.</para>
    /// </summary>
    public static class TapeQuantization
    {
        /// <summary>
        /// Единиц на мировую единицу для позиции: 1/256 ≈ 0.004 — меньше пикселя при нашем масштабе
        /// спрайтов, то есть глазом неотличимо. Диапазон ±128 мировых единиц покрывает любую арену с
        /// запасом.
        /// </summary>
        public const float PositionScale = 256f;

        /// <summary>Максимальное значение шкал HP/щита/ресурса при квантовании в <c>ushort</c>.</summary>
        public const int MaxScalar = ushort.MaxValue;

        public static short PackPosition(float world)
        {
            int q = Mathf.RoundToInt(world * PositionScale);
            if (q > short.MaxValue) q = short.MaxValue;
            if (q < short.MinValue) q = short.MinValue;
            return (short)q;
        }

        public static float UnpackPosition(short packed) => packed / PositionScale;

        /// <summary>
        /// Величина шкалы (HP, щит, ресурс, размер тела ×100, дальность ×100) в <c>ushort</c>. Округление
        /// к ближайшему, отрицательное — в ноль: полоска, ушедшая ниже нуля, всё равно рисуется пустой.
        /// </summary>
        public static ushort PackScalar(float value, float scale = 1f)
        {
            int q = Mathf.RoundToInt(value * scale);
            if (q < 0) q = 0;
            if (q > MaxScalar) q = MaxScalar;
            return (ushort)q;
        }

        public static float UnpackScalar(ushort packed, float scale = 1f) => packed / scale;

        /// <summary>Доля [0..1] в один байт — этого хватает всему, что смешивает клипы (разгон).</summary>
        public static byte PackUnit(float value01)
        {
            int q = Mathf.RoundToInt(Mathf.Clamp01(value01) * 255f);
            return (byte)q;
        }

        public static float UnpackUnit(byte packed) => packed / 255f;

        /// <summary>Тик-счётчик (замах, кулдаун, доигрыш) в <c>ushort</c> с потолком, а не с обрывом.</summary>
        public static ushort PackTicks(int ticks)
        {
            if (ticks < 0) return 0;
            return ticks > MaxScalar ? (ushort)MaxScalar : (ushort)ticks;
        }

        /// <summary>Масштаб для размеров тела и радиусов: сотые доли мировой единицы.</summary>
        public const float SizeScale = 100f;
    }
}
