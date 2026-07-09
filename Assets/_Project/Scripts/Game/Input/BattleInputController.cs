using System;
using Guildmaster.Combat;
using Guildmaster.Core.Input;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Input
{
    /// <summary>
    /// Связывает боевые действия ввода с симуляцией на время одного боя (вики «16» §4).
    /// Живёт в боевом скоупе: на старте ставит контекст <see cref="InputContext.Combat"/> и
    /// подписывается на паузу, на уничтожении скоупа — отписывается и гасит контекст.
    /// <para>Рестарт боя (R) и рестарт сцены (F5) — это dev-инструменты (см. DevTools), а не
    /// игровой ввод, поэтому их здесь нет.</para>
    /// </summary>
    public sealed class BattleInputController : IStartable, IDisposable
    {
        private readonly IInputService    _input;
        private readonly CombatSimulation _simulation;

        public BattleInputController(IInputService input, CombatSimulation simulation)
        {
            _input      = input;
            _simulation = simulation;
        }

        public void Start()
        {
            _input.SetContext(InputContext.Combat);
            _input.PauseToggleRequested += OnPauseToggle;
        }

        public void Dispose()
        {
            _input.PauseToggleRequested -= OnPauseToggle;
            _input.SetContext(InputContext.None);
            Time.timeScale = 1f; // страховка: если скоуп рушится на паузе (напр. R), не оставить мир замороженным
        }

        // Space: пауза боя. SetPaused замораживает СИМУЛЯЦИЮ; Time.timeScale = 0 замораживает и
        // ПРЕЗЕНТАЦИЮ (анимации, всплывающие цифры — всё на Time.deltaTime). Камеры не касается:
        // её пан идёт на Time.unscaledDeltaTime, так что на паузе поле можно осмотреть.
        // MP-путь пойдёт через PauseCommand/ResumeCommand (NetworkCommandRelay) — здесь хост-локально.
        private void OnPauseToggle()
        {
            bool paused = !_simulation.IsPaused;
            _simulation.SetPaused(paused);
            Time.timeScale = paused ? 0f : 1f;
        }
    }
}
