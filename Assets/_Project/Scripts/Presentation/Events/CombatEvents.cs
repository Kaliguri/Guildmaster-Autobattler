using Guildmaster.Combat;

namespace Guildmaster.Presentation
{
    /// <summary>MessagePipe-сообщения от симуляции к слою презентации.</summary>

    // Событий «юнит появился» и «юнит погиб» здесь больше нет: их публиковал презентер, а не слушал
    // никто — вид спавна и смерти он же и играет сам, напрямую (аудит 2026-07-26, волна 2).

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
