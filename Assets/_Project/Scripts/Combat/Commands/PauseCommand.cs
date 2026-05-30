namespace Guildmaster.Combat.Commands
{
    /// <summary>Ставит симуляцию боя на паузу на указанном тике.</summary>
    public sealed class PauseCommand : ICombatCommand
    {
        public int TargetTick { get; }

        public PauseCommand(int targetTick) => TargetTick = targetTick;

        public void Apply(CombatSimulation sim) => sim.SetPaused(true);
    }
}
