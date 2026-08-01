using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Имена параметров шейдера тела (<c>Guildmaster/Sprite/HitFlash</c>) в одном месте: их пишут обе
    /// реализации тела, и разъехавшееся имя не ломает компиляцию — оно молча перестаёт что-либо красить.
    /// </summary>
    internal static class BodyShaderIds
    {
        public static readonly int FlashAmount    = Shader.PropertyToID("_FlashAmount");
        public static readonly int FlashColor     = Shader.PropertyToID("_FlashColor");
        public static readonly int Holo           = Shader.PropertyToID("_Holo");
        public static readonly int HoloColor      = Shader.PropertyToID("_HoloColor");
        public static readonly int HoloAlpha      = Shader.PropertyToID("_HoloAlpha");
        public static readonly int HoloScanScale  = Shader.PropertyToID("_HoloScanScale");
        public static readonly int HoloScanAmount = Shader.PropertyToID("_HoloScanAmount");
        public static readonly int HoloTexel      = Shader.PropertyToID("_HoloTexel");
        public static readonly int Outline        = Shader.PropertyToID("_Outline");
        public static readonly int OutlineColor   = Shader.PropertyToID("_OutlineColor");
        public static readonly int GlowAmount     = Shader.PropertyToID("_GlowAmount");
        public static readonly int GlowColor      = Shader.PropertyToID("_GlowColor");
        public static readonly int GlowFlatness   = Shader.PropertyToID("_GlowShapeKeep");

        public static readonly int CutCount       = Shader.PropertyToID("_CutCount");
        public static readonly int CutGlow        = Shader.PropertyToID("_CutGlow");
        public static readonly int CutColor       = Shader.PropertyToID("_CutColor");
        public static readonly int CutWidth       = Shader.PropertyToID("_CutWidth");

        private static readonly int[] CutSlots =
        {
            Shader.PropertyToID("_Cut0"),
            Shader.PropertyToID("_Cut1"),
            Shader.PropertyToID("_Cut2"),
        };

        /// <summary>
        /// Сколько порезов рисует ОДНА часть. Тело держит <see cref="BodyCutLedger.Limit"/> ран на всех, но
        /// на одном предплечье их столько не наберётся — они расходятся по силуэту.
        /// </summary>
        /// <remarks>
        /// Ровно три, потому что порезы едут ОТДЕЛЬНЫМИ векторами, а не массивом: массив в property block
        /// выключает SRP Batcher для всего материала, а этот материал стоит на каждой части каждого юнита.
        /// Цена лимита — четвёртая рана на одной части не показывается; цена массива — батчинг всей арены.
        /// </remarks>
        public const int MaxCutsPerPart = 3;

        /// <summary>
        /// Разложить порезы ОДНОЙ части в property block. Порезы приходят в локальных координатах части,
        /// поэтому едут вместе с ней: рана на предплечье остаётся на предплечье, а не висит в воздухе.
        /// </summary>
        /// <param name="mpb">Блок этой части.</param>
        /// <param name="buffer">Буфер записей: xy — место, z — угол, w — длина.</param>
        /// <param name="glow">Яркости, по записи на порез.</param>
        /// <param name="count">Сколько записей заполнено.</param>
        /// <param name="colour">Цвет вскрытого — красный у всех, включая костяных и конструктов.</param>
        /// <param name="width">Полуширина линии пореза в локальных единицах части.</param>
        public static void WriteCuts(MaterialPropertyBlock mpb, Vector4[] buffer, float[] glow, int count,
            Color colour, float width)
        {
            count = Mathf.Clamp(count, 0, MaxCutsPerPart);
            mpb.SetFloat(CutCount, count);
            if (count <= 0) return;

            for (int i = 0; i < count; i++) mpb.SetVector(CutSlots[i], buffer[i]);

            mpb.SetVector(CutGlow, new Vector4(
                count > 0 ? glow[0] : 0f,
                count > 1 ? glow[1] : 0f,
                count > 2 ? glow[2] : 0f,
                0f));
            mpb.SetColor(CutColor, colour);
            mpb.SetFloat(CutWidth, width);
        }

        /// <summary>
        /// Разложить состояние кадра в property block одной части. Шаг текселя считается по ТЕКСТУРЕ ЭТОЙ
        /// части: и голограмма, и контур ищут по нему край силуэта, а у составного тела части приходят с
        /// разных атласов — общий шаг дал бы контур разной толщины на руке и на мече.
        /// <para><paramref name="partGlows"/> решает вызывающий (тело знает роль своей части): свечение
        /// адресное, поэтому здесь оно НЕ выводится из маски — часть либо светится, либо нет.</para>
        /// </summary>
        public static void Write(MaterialPropertyBlock mpb, in BodyVisualState state, Sprite sprite, bool partGlows)
        {
            mpb.SetFloat(FlashAmount, state.Flash);
            mpb.SetColor(FlashColor, state.FlashColor);
            mpb.SetFloat(Holo, state.Holo);
            mpb.SetFloat(Outline, state.Outline);

            // Свечение части: сила ненулевая только если ЭТА часть в маске приёма. Не-источники получают 0
            // явно — иначе оружие, засветившееся кадром раньше, осталось бы гореть, когда приём прошёл.
            float glow = partGlows && state.HasGlow ? state.Glow : 0f;
            mpb.SetFloat(GlowAmount, glow);
            if (glow > 0.0001f)
            {
                mpb.SetColor(GlowColor, state.GlowColor);
                // Плоскость свечения приходит из feel-конфига и живёт в состоянии кадра, а не в материале:
                // материал один на всех юнитов, а ручка должна крутиться на лету при play-QA.
                mpb.SetFloat(GlowFlatness, state.GlowFlatness);
            }

            bool needsTexel = state.Outline > 0.0001f || state.Holo > 0.0001f;
            if (needsTexel)
            {
                Texture tex = sprite != null ? sprite.texture : null;
                Vector2 texel = tex != null
                    ? new Vector2(1f / tex.width, 1f / tex.height)
                    : new Vector2(0.01f, 0.01f);
                mpb.SetVector(HoloTexel, texel);
            }

            if (state.Outline > 0.0001f) mpb.SetColor(OutlineColor, state.OutlineColor);

            if (state.Holo > 0.0001f)
            {
                mpb.SetColor(HoloColor, state.HoloColor);
                mpb.SetFloat(HoloAlpha, state.HoloAlpha);
                mpb.SetFloat(HoloScanScale, state.HoloScanScale);
                mpb.SetFloat(HoloScanAmount, state.HoloScanAmount);
            }
        }
    }
}
