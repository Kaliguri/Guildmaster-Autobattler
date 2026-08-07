using System.Threading;
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
        public ISharedDecision          ReadyGate { get; }
        public IPlayerIntentSource Intents   { get; }

        /// <summary>Токен отмены забега (QA #18 «В главное меню»): взводится <c>GameFlow</c>, прерывает
        /// висящие await'ы петли (выбор узла/«Продолжить»/исход боя). default = без отмены (dev/тесты).</summary>
        public CancellationToken   Cancellation { get; }

        /// <summary>
        /// Токен ЖИЗНИ УЗЛА: взводится петлей акта на входе в узел и снимается только когда игрок вошёл
        /// в СЛЕДУЮЩИЙ (QA #49). Нужен экранам, которые обязаны пережить собственный флоу: текст-результат
        /// ивента остаётся на экране всю передышку, а не гаснет по клику. Всегда «не длиннее» забега
        /// (linked); default = <see cref="Cancellation"/> для dev-разрезов без петли.
        /// </summary>
        public CancellationToken   NodeCancellation { get; }

        public RunContext(RunState runState, IRngService rng, ISharedDecision readyGate, IPlayerIntentSource intents,
                          CancellationToken cancellation = default, CancellationToken? nodeCancellation = null)
        {
            RunState         = runState;
            Rng              = rng;
            ReadyGate        = readyGate;
            Intents          = intents;
            Cancellation     = cancellation;
            NodeCancellation = nodeCancellation ?? cancellation;
        }

        /// <summary>Тот же контекст с собственным временем жизни узла — то, что петля даёт исполняемому флоу.</summary>
        public RunContext ForNode(CancellationToken nodeCancellation) =>
            new RunContext(RunState, Rng, ReadyGate, Intents, Cancellation, nodeCancellation);
    }
}
