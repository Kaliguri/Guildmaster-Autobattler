using System;
using Guildmaster.Combat;
using Guildmaster.Core.Input;
using Guildmaster.Game.Services;
using VContainer.Unity;

namespace Guildmaster.Game.Input
{
    /// <summary>
    /// Связывает боевые действия ввода с симуляцией на время одного боя (вики «16» §4).
    /// Живёт в боевом скоупе: подписывается на паузу/смену скорости, на уничтожении скоупа — отписывается.
    /// Контекст ввода НЕ трогает — им единолично владеет навигатор (пересчитывает из фазы боя, снос K8).
    /// <para>Рестарт боя (R) и рестарт сцены (F5) — это dev-инструменты (см. DevTools), а не
    /// игровой ввод, поэтому их здесь нет.</para>
    /// </summary>
    public sealed class BattleInputController : IStartable, IDisposable
    {
        // Ступени игровой скорости, циклируются по «.». Пол slowmo/cinematic — отдельный слой (feel-хуки).
        private static readonly float[] SpeedSteps = { 1f, 2f, 3f };

        private readonly IInputService    _input;
        private readonly CombatSimulation _simulation;
        private readonly TimeScaleService _time;

        private int _speedIndex;

        public BattleInputController(IInputService input, CombatSimulation simulation, TimeScaleService time)
        {
            _input      = input;
            _simulation = simulation;
            _time       = time;
        }

        public void Start()
        {
            _input.PauseToggleRequested   += OnPauseToggle;
            _input.GameSpeedCycleRequested += OnGameSpeedCycle;
        }

        public void Dispose()
        {
            _input.PauseToggleRequested   -= OnPauseToggle;
            _input.GameSpeedCycleRequested -= OnGameSpeedCycle;
            // Контекст ввода не гасим — навигатор пересчитает из фазы None при выгрузке боя (K8).
            // Time.timeScale владеет TimeScaleService — он же вернёт его к 1 при разрушении скоупа.
        }

        // Space: пауза боя. SetPaused замораживает СИМУЛЯЦИЮ (её тик-счётчик идёт для будущих команд);
        // TimeScaleService.SetPaused обнуляет Time.timeScale → встаёт и презентация, и накопление тиков.
        // Камеры не касается: её пан на Time.unscaledDeltaTime, так что на паузе поле можно осмотреть.
        // MP-путь пойдёт через PauseCommand/ResumeCommand (NetworkCommandRelay) — здесь хост-локально.
        private void OnPauseToggle()
        {
            bool paused = !_simulation.IsPaused;
            _simulation.SetPaused(paused);
            _time.SetPaused(paused);
        }

        // «.»: циклическая смена скорости боя (1x → 2x → 3x → 1x). Только темп — детерминизм не трогает.
        private void OnGameSpeedCycle()
        {
            _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;
            _time.SetGameSpeed(SpeedSteps[_speedIndex]);
        }
    }
}
