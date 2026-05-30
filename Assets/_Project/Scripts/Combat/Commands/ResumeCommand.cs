namespace Guildmaster.Combat.Commands
{
    /// <summary>Снимает симуляцию боя с паузы на указанном тике.</summary>
    public sealed class ResumeCommand : ICombatCommand
    {
        public int TargetTick { get; }

        public ResumeCommand(int targetTick) => TargetTick = targetTick;

        public void Apply(CombatSimulation sim) => sim.SetPaused(false);
    }
}
