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
    /// <remarks>
    /// <b>Маркеров может быть несколько — это Атака из нескольких Ударов</b> (2026-07-30/6). Каждый
    /// маркер = одно разрешение контакта: свой расчёт урона, своё попадание, свой on-hit. Позиции
    /// считаются в нормированном времени клипа и умножаются на длительность свинга, поэтому серия
    /// сжимается вместе с ним; раздвижку до зазора в тик и клампы делает <c>AttackTiming</c>, а не
    /// авторинг — свинг сжимается рантайм-скоростью атаки, и валидатор клипа этого не увидел бы.
    /// <para>Правила серии, которые держит уже не этот класс: рефанд кулдауна положен только свингу без
    /// единого контакта, промах считается состоявшимся контактом, каст между контактами не втискивается
    /// (<c>tech/00-meta/tech-debt</c> §3.9).</para>
    /// </remarks>
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

        /// <summary>
        /// Сколько контактов размечено в клипе. <c>0</c> = клипа или маркеров нет.
        /// </summary>
        public static int MarkerCount(AnimationClip clip)
        {
            if (clip == null) return 0;
            AnimationEvent[] events = clip.events;
            int n = 0;
            for (int i = 0; i < events.Length; i++)
                if (events[i].functionName == MarkerFunction) n++;
            return n;
        }

        /// <summary>
        /// Нормированные позиции ВСЕХ маркеров (0..1) по возрастанию времени, дописанные в
        /// <paramref name="result"/>. Возвращает их число.
        /// </summary>
        /// <remarks>
        /// Порядок событий в клипе Unity держит отсортированным по времени, но полагаться на это здесь
        /// нельзя: маркеры расставляются и руками, и генератором Animation Lab, а порядок контактов —
        /// это порядок ударов. Поэтому сортировка своя, вставкой: маркеров единицы.
        /// </remarks>
        public static int MarkerNormalizedAll(AnimationClip clip, System.Collections.Generic.List<float> result)
        {
            if (result == null) return 0;
            result.Clear();
            if (clip == null || clip.length <= 0f) return 0;

            AnimationEvent[] events = clip.events;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].functionName != MarkerFunction) continue;

                float t = Mathf.Clamp01(events[i].time / clip.length);
                int at = result.Count;
                while (at > 0 && result[at - 1] > t) at--;
                result.Insert(at, t);
            }
            return result.Count;
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
