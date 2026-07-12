using System;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
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

        // Возврат cinematic к 1 после пульса: линейный, на unscaled-времени (иначе slowmo тормозит
        // собственный отпуск). 0 = возврата нет (cinematic держится, пока не задан новый).
        private float _cinematicRecoverPerSec;

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

        /// <summary>Задать cinematic-множитель (0..4): &lt;1 — slowmo, &gt;1 — ускорение момента.</summary>
        public void SetCinematic(float factor)
        {
            _cinematic = Mathf.Clamp(factor, 0f, 4f);
            _cinematicRecoverPerSec = 0f; // ручная установка не возвращается сама
            Apply();
        }

        /// <summary>
        /// Кинематографический пульс: мгновенно уйти в slowmo (<paramref name="factor"/>) и линейно вернуться
        /// к 1 за <paramref name="recoverSeconds"/> (на unscaled-времени). Момент режиссуры на значимое событие
        /// (килл/конец боя). Новый пульс перебивает текущий.
        /// </summary>
        public void CinematicPulse(float factor, float recoverSeconds)
        {
            _cinematic = Mathf.Clamp(factor, 0f, 4f);
            _cinematicRecoverPerSec = recoverSeconds > 0f ? (1f - _cinematic) / recoverSeconds : 0f;
            Apply();
        }

        /// <summary>VContainer-тик: ведёт возврат cinematic к 1 после пульса (unscaled — не тормозит сам себя).</summary>
        public void Tick()
        {
            if (_cinematicRecoverPerSec <= 0f || _cinematic >= 1f) return;
            _cinematic = Mathf.Min(1f, _cinematic + _cinematicRecoverPerSec * Time.unscaledDeltaTime);
            if (_cinematic >= 1f) _cinematicRecoverPerSec = 0f;
            Apply();
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
