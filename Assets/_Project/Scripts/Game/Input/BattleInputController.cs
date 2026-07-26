using System;
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
        private readonly TimeScaleService _time;
        private readonly Core.Audio.IAudioService _audio;

        private int _speedIndex;

        public BattleInputController(IInputService input, TimeScaleService time, Core.Audio.IAudioService audio)
        {
            _input = input;
            _time  = time;
            _audio = audio;
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

        // Space: пауза ИГРОКА. Владелец у неё один — TimeScaleService: он обнуляет Time.timeScale, а вместе с
        // ним и Time.deltaTime, из которого CombatLoopService копит тики, — так что симуляция встаёт сама.
        // Камеры не касается: её пан на Time.unscaledDeltaTime, поле можно осмотреть на паузе.
        //
        // Симуляции здесь НЕ трогаем, хотя раньше трогали. CombatSimulation.SetPaused — другой факт: «сим
        // заморожен сценарием» (расстановка, передышка), и владеют им BattleBootstrap с DeploymentController.
        // Пока тумблер дёргал оба и читал состояние у СИМА, они расходились после каждого ResetBattle (сим
        // сбрасывает свою паузу сам): Space снимал паузу расстановки и оживлял отряд вне боя, а после
        // рестарта — ставил паузу вместо того, чтобы снять (аудит 2026-07-26, T-4).
        private void OnPauseToggle()
        {
            bool paused = !_time.IsPaused;
            _time.SetPaused(paused);
            // Пауза глушит боевую шину питчем (TimeScale→0.05), поэтому щелчок нужен UI-шине: иначе
            // нажатие Space не подтверждается ничем.
            _audio?.Play(paused ? "ui.pause.ui" : "ui.resume.ui");
        }

        // «.»: циклическая смена скорости боя (1x → 2x → 3x → 1x). Только темп — детерминизм не трогает.
        private void OnGameSpeedCycle()
        {
            _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;
            _time.SetGameSpeed(SpeedSteps[_speedIndex]);
            _audio?.Play("ui.speed.ui");
        }
    }
}
