using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Маркеры-как-данные на <see cref="AnimationClip"/> (вики «13» §3.1, «14»). Маркер — это
    /// <see cref="AnimationEvent"/> с <c>functionName == "Marker"</c>; всё читается в рантайме
    /// (<c>clip.events/length/frameRate</c>), без editor-API. Источник тайминга удара авто-атаки:
    /// сим берёт <see cref="UnitVisual.AttackFrameCount"/>/<see cref="UnitVisual.AttackHitFrame"/>,
    /// которые выводятся отсюда. <c>Animator.fireEvents=false</c> — маркеры никогда не колбэчат.
    /// </summary>
    public static class ClipMarkers
    {
        public const string MarkerFunction = "Marker";

        /// <summary>Время первого маркера (сек) или <c>-1</c>, если маркера нет.</summary>
        public static float FirstMarkerTime(AnimationClip clip)
        {
            if (clip == null) return -1f;
            AnimationEvent[] events = clip.events;
            for (int i = 0; i < events.Length; i++)
                if (events[i].functionName == MarkerFunction) return events[i].time;
            return -1f;
        }

        /// <summary>Число кадров клипа = <c>round(length × frameRate)</c>. 0 если клип пуст.</summary>
        public static int FrameCount(AnimationClip clip) =>
            clip == null ? 0 : Mathf.Max(0, Mathf.RoundToInt(clip.length * clip.frameRate));

        /// <summary>Кадр первого маркера = <c>round(time × frameRate)</c>. 0 если клипа/маркера нет.</summary>
        public static int HitFrame(AnimationClip clip)
        {
            float t = FirstMarkerTime(clip);
            return t < 0f || clip == null ? 0 : Mathf.RoundToInt(t * clip.frameRate);
        }

        /// <summary>Нормированное время маркера (0..1) в клипе. 0 если клипа/маркера/длины нет.</summary>
        public static float MarkerNormalized(AnimationClip clip)
        {
            float t = FirstMarkerTime(clip);
            return clip != null && t >= 0f && clip.length > 0f ? Mathf.Clamp01(t / clip.length) : 0f;
        }
    }
}
