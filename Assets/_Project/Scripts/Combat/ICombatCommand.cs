using Guildmaster.Core.Simulation;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Расширяет <see cref="ISimCommand"/> слоем Combat: добавляет метод применения к симуляции.
    /// Все мутации симуляции во время боя входят через упорядоченные команды на границе тика —
    /// основа host-authoritative мультиплеера и реплеев (вики «10» §5.6).
    /// </summary>
    public interface ICombatCommand : ISimCommand
    {
        /// <summary>Применить команду к симуляции. Вызывается на границе нужного тика.</summary>
        void Apply(CombatSimulation sim);
    }
}
