using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Кадры спрайт-анимации, извлечённые прямо из <see cref="AnimationClip"/> (object-reference кривая
    /// <c>SpriteRenderer.m_Sprite</c>). Позволяет проиграть клип в редакторе без рига/сцены — просто
    /// прокручивая спрайты по времени. Для не-спрайтовых клипов вернёт пусто.
    /// </summary>
    public static class ClipSpriteFrames
    {
        public readonly struct Frame
        {
            public readonly float Time;
            public readonly Sprite Sprite;
            public Frame(float time, Sprite sprite) { Time = time; Sprite = sprite; }
        }

        public static List<Frame> Extract(AnimationClip clip)
        {
            var result = new List<Frame>();
            if (clip == null) return result;

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.propertyName != "m_Sprite") continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null) continue;
                foreach (var k in keys)
                    if (k.value is Sprite s)
                        result.Add(new Frame(k.time, s));
                break; // первая спрайт-кривая
            }

            result.Sort((a, b) => a.Time.CompareTo(b.Time));
            return result;
        }

        /// <summary>Спрайт, активный на момент <paramref name="t"/> (последний кадр с time ≤ t).</summary>
        public static Sprite At(List<Frame> frames, float t)
        {
            if (frames == null || frames.Count == 0) return null;
            Sprite current = frames[0].Sprite;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Time <= t) current = frames[i].Sprite;
                else break;
            }
            return current;
        }
    }
}
