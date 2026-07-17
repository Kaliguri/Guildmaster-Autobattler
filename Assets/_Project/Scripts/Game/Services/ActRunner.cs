using System.Collections.Generic;
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
        private readonly INodeResolver     _resolver;
        private readonly IRewardPresenter  _reward;
        private readonly IContinuePresenter _continue;
        private readonly IMapNodeChooser   _chooser;
        private readonly RunStateService   _runStates;

        public ActRunner(INodeResolver resolver, IRewardPresenter reward, IContinuePresenter continuePresenter,
                         IMapNodeChooser chooser, RunStateService runStates)
        {
            _resolver  = resolver;
            _reward    = reward;
            _continue  = continuePresenter;
            _chooser   = chooser;
            _runStates = runStates;
        }

        public async UniTask<EventResult> RunActAsync(RunContext ctx)
        {
            MapState map = ctx.RunState?.Map;
            if (map == null || map.Nodes.Length == 0)
            {
                Debug.LogWarning("[ActRunner] - карта не сгенерирована → Aborted");
                return EventResult.Aborted;
            }

            while (!MapTraversal.IsActComplete(map))
            {
                IReadOnlyList<MapNode> available = MapTraversal.AvailableNext(map);
                if (available.Count == 0)
                {
                    Debug.LogWarning($"[ActRunner] - тупик на '{map.CurrentNodeId}' (нет доступных узлов) → Aborted");
                    return EventResult.Aborted;
                }

                MapNode node = await _chooser.ChooseAsync(map, available);
                if (node == null || !MapTraversal.CanEnter(map, node.Id))
                {
                    Debug.LogWarning($"[ActRunner] - выбран недоступный узел '{node?.Id ?? "null"}' → Aborted");
                    return EventResult.Aborted;
                }

                IEventFlow flow = _resolver.Resolve(node, ctx);
                EventResult result = await flow.Run(ctx);

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

                // Узел пройден: награда (для боевых) → единая кнопка «Продолжить» → продвижение позиции, автосейв.
                RewardTier? tier = RewardTierFor(node.Type);
                if (tier.HasValue) await _reward.PresentAsync(tier.Value);

                await _continue.WaitForContinueAsync();

                MapTraversal.Advance(map, node.Id);
                _runStates.Autosave();
            }

            Debug.Log("[ActRunner] - босс пройден → акт выигран");
            return EventResult.Completed;
        }

        /// <summary>Тир награды по типу узла; null = узел награду не даёт (обрабатывает свой flow сам).</summary>
        private static RewardTier? RewardTierFor(MapNodeType type) => type switch
        {
            MapNodeType.Battle => RewardTier.Battle,
            MapNodeType.Elite  => RewardTier.Elite,
            MapNodeType.Boss   => RewardTier.Boss,
            _                  => null,
        };
    }
}
