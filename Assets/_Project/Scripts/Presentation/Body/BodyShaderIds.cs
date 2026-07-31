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
