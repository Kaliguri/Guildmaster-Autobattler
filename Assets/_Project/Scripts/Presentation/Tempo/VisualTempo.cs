using UnityEngine;

namespace Guildmaster.Presentation.Tempo
{
    /// <summary>
    /// Такт от часов — временный источник, пока музыка не научится его задавать. Считает по unscaled-времени:
    /// метроном обязан идти и на паузе боя, иначе на паузе карта замирает и выглядит сломанной.
    /// </summary>
    /// <remarks>
    /// Когда появится музыкальный слой, эту реализацию сменит та, что читает позицию трека из FMOD, —
    /// потребители (<see cref="IVisualTempo"/>) не изменятся.
    /// </remarks>
    public sealed class VisualTempo : IVisualTempo
    {
        private float _bpm;

        public VisualTempo(float bpm = 84f) => _bpm = Mathf.Max(1f, bpm);

        /// <inheritdoc/>
        public float Bpm => _bpm;

        /// <inheritdoc/>
        public float BeatDuration => 60f / _bpm;

        /// <summary>Сменить темп (позже это будет делать музыкальный слой).</summary>
        public void SetBpm(float bpm) => _bpm = Mathf.Max(1f, bpm);

        /// <inheritdoc/>
        public float Phase(float division = 1f)
        {
            float length = BeatDuration * Mathf.Max(0.01f, division);
            return Mathf.Repeat(Time.unscaledTime, length) / length;
        }

        /// <inheritdoc/>
        public float Swell(float division = 1f)
        {
            // Синус, приведённый к 0..1: плавный вдох-выдох ровно за долю.
            return 0.5f - 0.5f * Mathf.Cos(Phase(division) * Mathf.PI * 2f);
        }

        /// <inheritdoc/>
        public float Heartbeat(float division = 1f)
        {
            float t = Phase(division);

            // «Тук-тук … пауза»: два толчка в первой трети доли, дальше тишина. Ровный синус читается как
            // дыхание, а сердце — это именно двойной удар с покоем между циклами.
            const float firstAt  = 0.00f;
            const float secondAt = 0.16f;
            const float width    = 0.11f;

            float beat = Mathf.Max(Thump(t, firstAt, width), Thump(t, secondAt, width * 0.85f) * 0.75f);
            return Mathf.Clamp01(beat);
        }

        // Одиночный толчок: быстрый подъём и чуть более медленный спад вокруг точки center.
        private static float Thump(float t, float center, float width)
        {
            float d = Mathf.Abs(t - center);
            if (d > width) return 0f;
            float k = 1f - d / width;
            return k * k;
        }
    }
}
