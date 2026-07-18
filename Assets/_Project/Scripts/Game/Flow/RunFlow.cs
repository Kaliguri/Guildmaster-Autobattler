using Cysharp.Threading.Tasks;
using Guildmaster.Core.Random;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>Итог узла карты (полиморфного <see cref="IEventFlow"/>).</summary>
    public enum EventOutcome
    {
        Completed,       // узел пройден (бой выигран, ивент разрешён, покупка сделана)
        PlayerDefeated,  // финальное поражение в бою (после ретраев) — конец забега
        Aborted,         // прервано/ошибка (сцена/скоуп не поднялись)
    }

    /// <summary>Результат исполнения узла карты.</summary>
    public readonly struct EventResult
    {
        public readonly EventOutcome Outcome;
        public EventResult(EventOutcome outcome) => Outcome = outcome;

        public static readonly EventResult Completed = new EventResult(EventOutcome.Completed);
        public static readonly EventResult Defeated  = new EventResult(EventOutcome.PlayerDefeated);
        public static readonly EventResult Aborted   = new EventResult(EventOutcome.Aborted);
    }

    /// <summary>
    /// Полиморфный узел забега (вики «7» §3, план 11 §3.2): бой / текст-ивент / магазин / риск — каждый
    /// новый тип реализует этот интерфейс, центральный switch не трогаем. Хост исполняет, клиенты шлют
    /// интенты через <see cref="RunContext.Intents"/> (соло — локально).
    /// </summary>
    public interface IEventFlow
    {
        UniTask<EventResult> Run(RunContext ctx);
    }

    /// <summary>
    /// Контекст исполнения узла (план 11 §3.2): durable-состояние забега + RNG + сетевые швы. UI-сервисы
    /// добавляются по мере надобности (награды/ивенты/магазин — A3/B3).
    /// </summary>
    public sealed class RunContext
    {
        public RunState            RunState  { get; }
        public IRngService         Rng       { get; }
        public IReadyGate          ReadyGate { get; }
        public IPlayerIntentSource Intents   { get; }

        public RunContext(RunState runState, IRngService rng, IReadyGate readyGate, IPlayerIntentSource intents)
        {
            RunState  = runState;
            Rng       = rng;
            ReadyGate = readyGate;
            Intents   = intents;
        }
    }
}
