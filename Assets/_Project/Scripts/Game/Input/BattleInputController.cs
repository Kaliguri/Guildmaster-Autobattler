using System;
using Guildmaster.Combat;
using Guildmaster.Core.Input;
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
        }

        // Space: локальный toggle паузы (как dev-консоль). MP-путь идёт через PauseCommand/
        // ResumeCommand в NetworkCommandRelay — здесь хост-локально, поэтому SetPaused напрямую.
        private void OnPauseToggle() => _simulation.SetPaused(!_simulation.IsPaused);
    }
}
