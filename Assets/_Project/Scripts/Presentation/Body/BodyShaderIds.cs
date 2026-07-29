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

        /// <summary>
        /// Разложить состояние кадра в property block одной части. Шаг текселя считается по ТЕКСТУРЕ ЭТОЙ
        /// части: и голограмма, и контур ищут по нему край силуэта, а у составного тела части приходят с
        /// разных атласов — общий шаг дал бы контур разной толщины на руке и на мече.
        /// </summary>
        public static void Write(MaterialPropertyBlock mpb, in BodyVisualState state, Sprite sprite)
        {
            mpb.SetFloat(FlashAmount, state.Flash);
            mpb.SetColor(FlashColor, state.FlashColor);
            mpb.SetFloat(Holo, state.Holo);
            mpb.SetFloat(Outline, state.Outline);

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
