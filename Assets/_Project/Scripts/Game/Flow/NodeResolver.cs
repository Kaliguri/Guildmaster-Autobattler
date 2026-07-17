using Cysharp.Threading.Tasks;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>Фабрика: узел карты → полиморфный <see cref="IEventFlow"/> для его исполнения.</summary>
    public interface INodeResolver
    {
        IEventFlow Resolve(MapNode node, RunContext ctx);
    }

    /// <summary>
    /// Резолвер узлов карты (план [[act-map-run-loop]] §3.2): таблица <see cref="MapNodeType"/> → <see cref="IEventFlow"/>.
    /// Новый тип узла = новая ветка здесь + сам flow; центральный switch петли (<c>ActRunner</c>) не трогается.
    /// <para>Контент узла берётся по <see cref="MapNode.PayloadId"/> (если задан), иначе случайно из пула контент-БД
    /// (детерминировано через <see cref="RunContext.Rng"/>). A2: Shop/Chest/«?» — заглушки <see cref="CompletedStubFlow"/>
    /// (реализуются на фазах B2-B4); Elite делит боевой пул с Battle до появления elite-пресетов (B5).</para>
    /// </summary>
    public sealed class NodeResolver : INodeResolver
    {
        private readonly IContentDatabase   _content;
        private readonly ISceneLoader       _scenes;
        private readonly IBattleSession     _session;
        private readonly ILocalPlayer       _localPlayer;
        private readonly EventEffectApplier _eventEffects;
        private readonly ShopController     _shop;
        private readonly IPublisher<OpenTextEventRequest> _openEventPub;
        private readonly IPublisher<OpenShopRequest>      _openShopPub;

        public NodeResolver(IContentDatabase content, ISceneLoader scenes, IBattleSession session,
                            ILocalPlayer localPlayer, EventEffectApplier eventEffects, ShopController shop,
                            IPublisher<OpenTextEventRequest> openEventPub, IPublisher<OpenShopRequest> openShopPub)
        {
            _content      = content;
            _scenes       = scenes;
            _session      = session;
            _localPlayer  = localPlayer;
            _eventEffects = eventEffects;
            _shop         = shop;
            _openEventPub = openEventPub;
            _openShopPub  = openShopPub;
        }

        public IEventFlow Resolve(MapNode node, RunContext ctx)
        {
            switch (node.Type)
            {
                case MapNodeType.Battle:
                case MapNodeType.Elite:
                case MapNodeType.Boss:
                {
                    BattlePresetData preset = PickContent<BattlePresetData>(node, ctx);
                    if (preset == null)
                    {
                        Debug.LogWarning($"[NodeResolver] - нет BattlePresetData в контент-БД для '{node.Id}' → заглушка");
                        return new CompletedStubFlow(node.Type);
                    }
                    return new BattleFlow(preset, _scenes, _session, _localPlayer);
                }

                case MapNodeType.TextEvent:
                {
                    TextEventData ev = PickContent<TextEventData>(node, ctx);
                    if (ev == null)
                    {
                        Debug.LogWarning($"[NodeResolver] - нет TextEventData в контент-БД для '{node.Id}' → заглушка");
                        return new CompletedStubFlow(node.Type);
                    }
                    return new TextEventFlow(ev, _openEventPub, _eventEffects);
                }

                case MapNodeType.Shop:
                    return new ShopFlow(_shop, _openShopPub);

                // Сундук/«?» ещё не реализованы — проходим как no-op (фазы B3-B4).
                case MapNodeType.Chest:
                case MapNodeType.Unknown:
                    return new CompletedStubFlow(node.Type);

                default:
                    return new CompletedStubFlow(node.Type);
            }
        }

        /// <summary>Контент по payload-id, иначе случайный из пула типа T (детерминировано через RNG). null = пул пуст.</summary>
        private T PickContent<T>(MapNode node, RunContext ctx) where T : ContentDefinition
        {
            if (!string.IsNullOrEmpty(node.PayloadId) && _content.TryGet<T>(node.PayloadId, out T byId))
                return byId;

            var all = _content.All<T>();
            return all.Count == 0 ? null : all[ctx.Rng.NextInt(0, all.Count)];
        }
    }

    /// <summary>Узел-заглушка: сразу <see cref="EventResult.Completed"/> (плейсхолдер для ещё не готовых типов).</summary>
    public sealed class CompletedStubFlow : IEventFlow
    {
        private readonly MapNodeType _type;
        public CompletedStubFlow(MapNodeType type) => _type = type;

        public UniTask<EventResult> Run(RunContext ctx)
        {
            Debug.Log($"[CompletedStubFlow] - узел '{_type}' пройден-заглушкой (реализация на фазе B)");
            return UniTask.FromResult(EventResult.Completed);
        }
    }
}
