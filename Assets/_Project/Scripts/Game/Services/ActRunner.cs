using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Петля обхода акта (план [[act-map-run-loop]] §3.2, шаг A2). Поверх готового <c>GameFlow</c> (тот делегирует
    /// сюда): пока не пройден босс — выбрать доступный узел (<see cref="IMapNodeChooser"/>), резолвить его в
    /// <see cref="IEventFlow"/> (<see cref="INodeResolver"/>), исполнить, по результату — награда/продвижение/исход.
    /// Позиция и <c>Cleared</c> живут в <see cref="RunState.Map"/> и автосохраняются на каждом переходе (вики «7» §5).
    /// <para>Итог акта в терминах <see cref="EventResult"/>: <c>Completed</c> — босс пройден (акт выигран);
    /// <c>PlayerDefeated</c> — поражение (конец забега; пул перезапусков — C1); <c>Aborted</c> — сбой (пустая карта/тупик).</para>
    /// </summary>
    public sealed class ActRunner
    {
        private readonly INodeResolver      _resolver;
        private readonly IMapNodeChooser    _chooser;
        private readonly RunStateService    _runStates;
        private readonly IRunBeatStage      _beat;

        /// <param name="beat">
        /// Что делать с миром на стыках узлов (вернуть арену, встать в передышку, показать её кнопки).
        /// null = петля без мира (headless/тесты) — стыки просто не оформляются.
        /// </param>
        public ActRunner(INodeResolver resolver, IMapNodeChooser chooser, RunStateService runStates,
                         IRunBeatStage beat = null)
        {
            _resolver  = resolver;
            _chooser   = chooser;
            _runStates = runStates;
            _beat      = beat;
        }

        public async UniTask<EventResult> RunActAsync(RunContext ctx)
        {
            MapState map = ctx.RunState?.Map;
            if (map == null || map.Nodes.Length == 0)
            {
                Debug.LogWarning("[ActRunner] - карта не сгенерирована → Aborted");
                return EventResult.Aborted;
            }

            bool actEntry = true; // самый первый выбор акта — только он открывает карту сам

            // Время жизни ЭКРАНА узла (QA #49): экран ивента с текстом-результатом обязан пережить свой флоу и
            // всю передышку — гаснет он, только когда игрок вошёл в следующий узел. Поэтому токен переживает
            // итерацию цикла и отменяется в начале следующей, а не через using.
            CancellationTokenSource nodeCts = null;
            try
            {
                while (!MapTraversal.IsActComplete(map))
                {
                    IReadOnlyList<MapNode> available = MapTraversal.AvailableNext(map);
                    if (available.Count == 0)
                    {
                        Debug.LogWarning($"[ActRunner] - тупик на '{map.CurrentNodeId}' (нет доступных узлов) → Aborted");
                        return EventResult.Aborted;
                    }

                    // Пока игрок выбирает следующий узел, он стоит в живом мире (или на экране пройденного
                    // узла): арена/текст-результат, а в углу кнопки-шорткаты «Продолжить» (открыть карту) и
                    // «К построению». Кнопки снимаются, как только узел выбран. На входе в акт передышки нет —
                    // там сразу карта (игрок должен увидеть, куда идёт).
                    using var beatCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Cancellation);
                    if (!actEntry) _beat?.EnterRestBeat(beatCts.Token);

                    MapNode node;
                    try     { node = await _chooser.ChooseAsync(map, available, ctx.Cancellation, openMap: actEntry); }
                    finally { beatCts.Cancel(); } // узел выбран (или забег брошен) — кнопки бита уходят

                    actEntry = false;

                    // QA #37: отмена забега («В меню» из паузы) закрывает экран карты по токену. Это НЕ Aborted, а
                    // кооперативная отмена — бросаем OperationCanceledException, она всплывает сквозь петлю в
                    // GameFlow.RunGameAsync (catch → главное меню). Страховка на случай, если chooser вернул null
                    // по гонке закрытия, а не бросил сам. Различает «отмена» от «реально недоступный узел» (#37b).
                    if (ctx.Cancellation.IsCancellationRequested)
                    {
                        Debug.Log("[ActRunner] - выбор узла отменён (выход из забега) → OperationCanceled, не Aborted");
                        ctx.Cancellation.ThrowIfCancellationRequested();
                    }

                    if (node == null || !MapTraversal.CanEnter(map, node.Id))
                    {
                        Debug.LogWarning($"[ActRunner] - выбран недоступный узел '{node?.Id ?? "null"}' → Aborted " +
                                         $"(узел '{map.CurrentNodeId}', доступно {available.Count}; это НЕ отмена — реальный тупик/баг данных)");
                        return EventResult.Aborted;
                    }

                    IEventFlow flow = _resolver.Resolve(node, ctx);

                    // Игрок пошёл дальше → экран ПРОШЛОГО узла (текст-результат ивента) снимается, и только здесь.
                    nodeCts?.Cancel();
                    nodeCts?.Dispose();
                    nodeCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Cancellation);

                    _beat?.EnterNode(); // мир уходит на второй план: у узла свой экран (у боя — своя фаза)
                    EventResult result = await flow.Run(ctx.ForNode(nodeCts.Token));

                    if (result.Outcome == EventOutcome.Aborted)
                    {
                        Debug.LogWarning($"[ActRunner] - узел '{node.Id}' прерван → Aborted");
                        return EventResult.Aborted;
                    }

                    if (result.Outcome == EventOutcome.PlayerDefeated)
                    {
                        // Поражение = конец забега. Пул перезапусков-на-акт (реш. №65) появится на C1.
                        _runStates.Autosave();
                        Debug.Log($"[ActRunner] - поражение на узле '{node.Id}' → конец забега");
                        return EventResult.Defeated;
                    }

                    // Узел пройден (награда/золото — внутри самого flow) → продвижение и автосейв СРАЗУ, без
                    // ожидания кнопки (реш. Макса 2026-07-26): последняя награда выдана — значит игрок уже готов
                    // к следующему этапу. Дальше он стоит в передышке столько, сколько захочет (см. верх петли).
                    MapTraversal.Advance(map, node.Id);
                    _runStates.Autosave();
                }

                Debug.Log("[ActRunner] - босс пройден → акт выигран");
                return EventResult.Completed;
            }
            finally
            {
                // Акт кончился любым путём (босс, поражение, выход в меню) — экран последнего узла снять.
                nodeCts?.Cancel();
                nodeCts?.Dispose();
            }
        }
    }
}
