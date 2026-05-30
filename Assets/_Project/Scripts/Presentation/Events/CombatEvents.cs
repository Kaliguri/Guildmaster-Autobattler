using Guildmaster.Combat;

namespace Guildmaster.Presentation
{
    /// <summary>MessagePipe-сообщения от симуляции к слою презентации.</summary>

    public readonly struct UnitSpawnedEvent
    {
        public readonly RuntimeUnit Unit;
        public UnitSpawnedEvent(RuntimeUnit unit) => Unit = unit;
    }

    public readonly struct UnitDiedEvent
    {
        public readonly RuntimeUnit Unit;
        public UnitDiedEvent(RuntimeUnit unit) => Unit = unit;
    }

    public readonly struct DamageDealtEvent
    {
        public readonly RuntimeUnit   Source;
        public readonly RuntimeUnit   Target;
        public readonly DamageResult  Result;
        public DamageDealtEvent(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            Source = source; Target = target; Result = result;
        }
    }

    public readonly struct BattleEndedEvent
    {
        public readonly BattleOutcome Outcome;
        public BattleEndedEvent(BattleOutcome outcome) => Outcome = outcome;
    }
}
