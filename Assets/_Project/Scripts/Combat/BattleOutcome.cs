namespace Guildmaster.Combat
{
    /// <summary>Итог боя, устанавливается <see cref="CombatSimulation"/> после гибели одной из сторон.</summary>
    public enum BattleOutcome
    {
        Ongoing  = 0,
        TeamAWins = 1,
        TeamBWins = 2,
        Draw      = 3,
    }
}
