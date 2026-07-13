using System;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Одна ступень режиссёрской cinematic-секвенции (<see cref="TimeScaleService.PlayCinematicSequence"/>).
    /// <see cref="Ramp"/>=false — мгновенно уйти в <see cref="Factor"/> и держать <see cref="Duration"/> сек;
    /// <see cref="Ramp"/>=true — плавно перейти от значения предыдущей ступени к <see cref="Factor"/> за
    /// <see cref="Duration"/> по <see cref="Curve"/> (null = линейно). Всё на unscaled-времени.
    /// </summary>
    public readonly struct CinematicSegment
    {
        public readonly float Factor;
        public readonly float Duration;
        public readonly bool  Ramp;
        public readonly AnimationCurve Curve;

        public CinematicSegment(float factor, float duration, bool ramp = false, AnimationCurve curve = null)
        {
            Factor   = Mathf.Clamp(factor, 0f, 4f);
            Duration = Mathf.Max(0f, duration);
            Ramp     = ramp;
            Curve    = curve;
        }
    }

    /// <summary>
    /// Единственный писатель <see cref="UnityEngine.Time.timeScale"/> в бою. Компонует три
    /// независимых источника масштаба времени, чтобы они не перетирали друг друга:
    /// <list type="bullet">
    /// <item><b>GameSpeed</b> — выбор игрока (1x / 2x / 3x), переживает slowmo-моменты;</item>
    /// <item><b>Cinematic</b> — короткие режиссёрские замедления (slowmo на значимый удар/смерть),
    /// их будет ставить слой feel-хуков поверх симуляции;</item>
    /// <item><b>Paused</b> — жёсткий стоп, перекрывает всё (timeScale = 0).</item>
    /// </list>
    /// Итог: <c>paused ? 0 : GameSpeed * Cinematic</c>. Симуляция следует автоматически —
    /// <see cref="CombatLoopService"/> копит <see cref="Time.deltaTime"/> (уже масштабированный),
    /// как и анимации/партиклы на scaled-времени. Камера-пан на <c>unscaledDeltaTime</c> остаётся
    /// отзывчивой на паузе (вики «16»). Детерминизм цел: тики те же, меняется лишь сколько
    /// реального времени приходится на тик.
    /// </summary>
    public sealed class TimeScaleService : ITickable, IDisposable
    {
        private float _gameSpeed = 1f;
        private float _cinematic = 1f;
        private bool  _paused;

        // Профиль cinematic-пульса: держим глубину _cinematicFrom _holdRemaining секунд, затем возвращаемся
        // к 1 за _releaseDuration по кривой _releaseCurve. Всё на unscaled-времени (иначе slowmo тормозит
        // собственный отпуск). _cinematicActive=false — пульса нет.
        private float          _cinematicFrom;
        private float          _holdRemaining;
        private float          _releaseDuration;
        private float          _releaseElapsed;
        private AnimationCurve _releaseCurve;
        private bool           _cinematicActive;

        // Многоступенчатая режиссёрская секвенция (финишер-концовка): последовательность сегментов на
        // unscaled-времени. Ступень с Ramp=false — мгновенно уйти в Factor и держать Duration; Ramp=true —
        // плавно перейти от предыдущего к Factor за Duration по Curve. Перебивает одиночный пульс.
        private CinematicSegment[] _segments;
        private int                _segIndex;
        private float              _segElapsed;
        private float              _segFrom;
        private bool               _sequenceActive;

        /// <summary>Игровая скорость (выбор игрока), без учёта паузы и cinematic.</summary>
        public float GameSpeed => _gameSpeed;

        /// <summary>Кинематографический множитель (slowmo-моменты), 1 = нет.</summary>
        public float Cinematic => _cinematic;

        /// <summary>Стоит ли жёсткая пауза.</summary>
        public bool IsPaused => _paused;

        /// <summary>Итоговый масштаб, записанный в <see cref="Time.timeScale"/> (0 на паузе).</summary>
        public float Effective => _paused ? 0f : _gameSpeed * _cinematic;

        /// <summary>Задать игровую скорость. Пол 0.01 — чтобы не поймать нечаянный стоп мимо паузы.</summary>
        public void SetGameSpeed(float speed)
        {
            _gameSpeed = Mathf.Max(0.01f, speed);
            Apply();
        }

        /// <summary>Задать cinematic-множитель напрямую (0..4): &lt;1 — slowmo. Отменяет активный пульс.</summary>
        public void SetCinematic(float factor)
        {
            _cinematic = Mathf.Clamp(factor, 0f, 4f);
            _cinematicActive = false;
            Apply();
        }

        /// <summary>
        /// Кинематографический пульс: мгновенно уйти в slowmo (<paramref name="factor"/>), ДЕРЖАТЬ его
        /// <paramref name="holdSeconds"/>, затем вернуться к 1 за <paramref name="releaseSeconds"/> по форме
        /// <paramref name="releaseCurve"/> (норм. время 0→1 → доля возврата 0→1; null = линейно). Всё на
        /// unscaled-времени. Момент режиссуры (килл / финишер-концовка). Новый пульс перебивает текущий.
        /// </summary>
        public void CinematicPulse(float factor, float holdSeconds, float releaseSeconds, AnimationCurve releaseCurve = null)
        {
            _cinematicFrom   = Mathf.Clamp(factor, 0f, 4f);
            _cinematic       = _cinematicFrom;
            _holdRemaining   = Mathf.Max(0f, holdSeconds);
            _releaseDuration = Mathf.Max(0.0001f, releaseSeconds);
            _releaseElapsed  = 0f;
            _releaseCurve    = releaseCurve;
            _cinematicActive = true;
            Apply();
        }

        /// <summary>
        /// Запустить многоступенчатую cinematic-секвенцию (напр. финишер: пауза → slowmo death → slowmo shatter →
        /// возврат). Проигрывается по <see cref="CinematicSegment"/> на unscaled-времени. Перебивает пульс/секвенцию.
        /// </summary>
        public void PlayCinematicSequence(CinematicSegment[] segments)
        {
            if (segments == null || segments.Length == 0) return;
            _segments        = segments;
            _segIndex        = 0;
            _segElapsed      = 0f;
            _segFrom         = _cinematic;
            _sequenceActive  = true;
            _cinematicActive = false; // секвенция перекрывает одиночный пульс
            if (!_segments[0].Ramp) _cinematic = _segments[0].Factor; // мгновенная ступень — сразу в Factor
            Apply();
        }

        /// <summary>VContainer-тик: держит глубину, затем ведёт возврат к 1 по кривой (unscaled — не тормозит сам себя).</summary>
        public void Tick()
        {
            if (_sequenceActive) { TickSequence(); return; }
            if (!_cinematicActive) return;

            float dt = Time.unscaledDeltaTime;
            if (_holdRemaining > 0f)
            {
                _holdRemaining -= dt; // держим _cinematicFrom (уже применён)
                return;
            }

            _releaseElapsed += dt;
            float t = Mathf.Clamp01(_releaseElapsed / _releaseDuration);
            float k = _releaseCurve != null ? Mathf.Clamp01(_releaseCurve.Evaluate(t)) : t; // доля возврата к норме
            _cinematic = Mathf.Lerp(_cinematicFrom, 1f, k);
            if (t >= 1f) { _cinematic = 1f; _cinematicActive = false; }
            Apply();
        }

        // Ступени секвенции на unscaled-времени: держим/рампим текущую, по истечении — к следующей; конец — стоп.
        private void TickSequence()
        {
            CinematicSegment seg = _segments[_segIndex];
            _segElapsed += Time.unscaledDeltaTime;

            if (seg.Ramp)
            {
                float t = seg.Duration > 0f ? Mathf.Clamp01(_segElapsed / seg.Duration) : 1f;
                float k = seg.Curve != null ? Mathf.Clamp01(seg.Curve.Evaluate(t)) : t;
                _cinematic = Mathf.Lerp(_segFrom, seg.Factor, k);
            }
            else
            {
                _cinematic = seg.Factor; // держим ступень
            }
            Apply();

            if (_segElapsed >= seg.Duration)
            {
                _segIndex++;
                _segElapsed = 0f;
                _segFrom = _cinematic;
                if (_segIndex >= _segments.Length)
                {
                    _sequenceActive = false;
                    _segments = null; // конец: _cinematic остаётся на значении последней ступени (обычно 1)
                }
            }
        }

        /// <summary>Поставить/снять жёсткую паузу (перекрывает game speed и cinematic).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            Apply();
        }

        private void Apply() => Time.timeScale = Effective;

        /// <summary>Вернуть глобальный timeScale к 1 при выгрузке боя — иначе мир останется замороженным.</summary>
        public void Dispose() => Time.timeScale = 1f;
    }
}
