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
        // Темп по умолчанию. НЕ параметр конструктора: VContainer не понимает значений по умолчанию —
        // он пойдёт искать регистрацию float, не найдёт и уронит сборку всего, что зависит от такта
        // (карта переставала резолвиться целиком). Темп задаётся через SetBpm, позже — музыкальным слоем.
        private const float DefaultBpm = 84f;

        private float _bpm = DefaultBpm;

        public VisualTempo() { }

        /// <inheritdoc/>
        public float Bpm => _bpm;

        /// <inheritdoc/>
        public float BeatDuration => 60f / _bpm;

        // Сеттера темпа нет: пока его никто не звал, а когда темп начнёт задавать музыкальный слой,
        // менять будем реализацию за IVisualTempo, а не дописывать ручку в эту (аудит 2026-07-26).

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

    }
}
