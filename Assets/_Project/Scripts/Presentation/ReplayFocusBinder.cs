using System;
using Guildmaster.Combat.Tape;
using VContainer.Unity;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Мост камеры к ПОВТОРУ: на время жизни реплей-скоупа подаёт персистентной камере источник точек
    /// фокуса из ленты. Зеркало <see cref="BattleFocusBinder"/>, но точки берёт из показа
    /// (<see cref="BattleTapePlayback"/>), а не из симуляции — у повтора она простаивает.
    /// </summary>
    public sealed class ReplayFocusBinder : IStartable, IDisposable
    {
        private readonly CombatFocusTarget  _focus;
        private readonly BattleTapePlayback _playback;

        public ReplayFocusBinder(CombatFocusTarget focus, BattleTapePlayback playback)
        {
            _focus    = focus;
            _playback = playback;
        }

        public void Start() => _focus.SetSource(new TapeFocusPointSource(_playback));

        public void Dispose() => _focus.SetSource(null); // конец показа → камера ни за кем не следует
    }
}
