using Guildmaster.Combat;

namespace Guildmaster.Presentation
{
    /// <summary>MessagePipe-сообщения от симуляции к слою презентации.</summary>

    // Событий «юнит появился» и «юнит погиб» здесь больше нет: их публиковал презентер, а не слушал
    // никто — вид спавна и смерти он же и играет сам, напрямую (аудит 2026-07-26, волна 2).

    /// <summary>
    /// Удар ПОКАЗАН. Несёт id и снятые с показанного тика числа, а не ссылки на живые юниты: сим ушёл
    /// вперёд на окно опережения, и позиция с HP живого юнита — это будущее, которого игрок пока не видел.
    /// </summary>
    public readonly struct DamageDealtEvent
    {
        public readonly int           SourceId;
        public readonly int           TargetId;

        /// <summary>Где цель была в показанном кадре — точка для тряски, баса, стингера.</summary>
        public readonly UnityEngine.Vector2 TargetPosition;

        /// <summary>MaxHP цели на показанном тике — знаменатель «веса удара».</summary>
        public readonly float         TargetMaxHp;

        public readonly DamageResult  Result;

        public DamageDealtEvent(
            int sourceId, int targetId, UnityEngine.Vector2 targetPosition, float targetMaxHp,
            DamageResult result)
        {
            SourceId       = sourceId;
            TargetId       = targetId;
            TargetPosition = targetPosition;
            TargetMaxHp    = targetMaxHp;
            Result         = result;
        }
    }

    public readonly struct BattleEndedEvent
    {
        public readonly BattleOutcome Outcome;
        public BattleEndedEvent(BattleOutcome outcome) => Outcome = outcome;
    }
}
