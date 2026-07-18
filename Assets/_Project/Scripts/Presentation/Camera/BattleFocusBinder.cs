using System;
using Guildmaster.Combat;
using VContainer.Unity;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Боевой мост камеры к симуляции: живёт один бой (EntryPoint боевого скоупа). На старте
    /// подаёт персистентной камере боевой источник точек фокуса (живые юниты этого боя) через
    /// <see cref="CombatFocusTarget.SetSource"/>, на конце — возвращает пустой (камера стоит вне
    /// боя). Сам <see cref="CombatFocusTarget"/> живёт в персистентном WorldLifetimeScope и
    /// резолвится из предка; боевой скоуп лишь переключает ему источник по времени жизни боя.
    /// </summary>
    public sealed class BattleFocusBinder : IStartable, IDisposable
    {
        private readonly CombatFocusTarget _focus;
        private readonly CombatSimulation _simulation;

        public BattleFocusBinder(CombatFocusTarget focus, CombatSimulation simulation)
        {
            _focus = focus;
            _simulation = simulation;
        }

        public void Start() => _focus.SetSource(new CombatFocusPointSource(_simulation));

        public void Dispose() => _focus.SetSource(null); // конец боя → камера ни за кем не следует
    }
}
