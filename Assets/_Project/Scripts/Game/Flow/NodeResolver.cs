using System.Collections.Generic;
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
        private readonly IBattleSession     _session;
        private readonly ILocalPlayer       _localPlayer;
        private readonly EventEffectApplier _eventEffects;
        private readonly ShopController     _shop;
        private readonly IRewardPresenter   _reward;
        private readonly RunStateService    _runStates;
        private readonly IContinuePresenter _continue;
        private readonly IPublisher<OpenTextEventRequest> _openEventPub;
        private readonly IPublisher<OpenShopRequest>      _openShopPub;
        private readonly IPublisher<OpenChestRequest>     _openChestPub;
        private readonly IPublisher<OpenCampRequest>      _openCampPub;
        private readonly IPublisher<OpenNodeFarewellRequest> _farewellPub; // кадр-прощание узла (QA #48/#49)

        public NodeResolver(IContentDatabase content, IBattleSession session,
                            ILocalPlayer localPlayer, EventEffectApplier eventEffects, ShopController shop,
                            IRewardPresenter reward, RunStateService runStates, IContinuePresenter continuePresenter,
                            IPublisher<OpenTextEventRequest> openEventPub, IPublisher<OpenShopRequest> openShopPub,
                            IPublisher<OpenChestRequest> openChestPub, IPublisher<OpenCampRequest> openCampPub,
                            IPublisher<OpenNodeFarewellRequest> farewellPub)
        {
            _openCampPub  = openCampPub;
            _farewellPub  = farewellPub;
            _content      = content;
            _session      = session;
            _localPlayer  = localPlayer;
            _eventEffects = eventEffects;
            _shop         = shop;
            _reward       = reward;
            _runStates    = runStates;
            _continue     = continuePresenter;
            _openEventPub = openEventPub;
            _openShopPub  = openShopPub;
            _openChestPub = openChestPub;
        }

        public IEventFlow Resolve(MapNode node, RunContext ctx)
        {
            switch (node.Type)
            {
                case MapNodeType.Battle:
                case MapNodeType.Elite:
                case MapNodeType.Boss:
                {
                    bool wantElite = node.Type == MapNodeType.Elite;
                    BattlePresetData preset = PickBattlePreset(node, ctx, wantElite);
                    if (preset == null)
                    {
                        Debug.LogWarning($"[NodeResolver] - нет BattlePresetData в контент-БД для '{node.Id}' → заглушка");
                        return new CompletedStubFlow(node.Type);
                    }

                    // Забег деплоит СВОЮ гильдию (уточн. Макса): ростер — из RunState (4 сосуда + надетые релики),
                    // враги — из выбранного пресета, режим — Free (фаза расстановки + «Начать» перед боем).
                    // Пустая гильдия (dev/тест без ростера) → берём пресет как есть (канон-ростер, его режим).
                    BattlePresetData effective = preset;
                    PlayerSlot[] guildRoster = GuildRoster.Resolve(_runStates.Current, _content);
                    if (guildRoster.Length > 0)
                    {
                        ItemData[] party = GuildRoster.ResolveItems(_runStates.Current.PartyItemIds, _content);
                        effective = BattlePresetData.CreateRuntime(
                            preset.Encounter, guildRoster, DeploymentMode.Free, party, preset.IsElite,
                            $"battle.run.{node.Id}");
                    }

                    var battle = new BattleFlow(effective, _session, _localPlayer,
                                                () => _runStates.TrySpendRestart()); // пул перезапусков акта (C1)
                    int rewardCount = wantElite ? 2 : 1;   // элитка — два выбора реликвии подряд (B5)
                    // Сессия + способ дождаться нового приговора: dev-R после конца боя откатывает узел
                    // к бою, снимая с него награду и мост к ней.
                    return new BattleNodeFlow(battle, TierFor(node.Type), _reward, _runStates, _continue, rewardCount,
                                              session: _session, awaitReplayOutcome: battle.AwaitReplayOutcome);
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
                    return new ShopFlow(_shop, _openShopPub, _farewellPub);

                case MapNodeType.Chest:
                    return new ChestFlow(_openChestPub, _reward, _farewellPub);

                case MapNodeType.Camp:
                    return new CampFlow(_openCampPub, _farewellPub);

                // «?»: тип роллится на входе, делегируем себе же (B4).
                case MapNodeType.Unknown:
                    return new RandomEventFlow(this);

                default:
                    return new CompletedStubFlow(node.Type);
            }
        }

        /// <summary>Тир награды по боевому типу узла.</summary>
        private static RewardTier TierFor(MapNodeType type) => type switch
        {
            MapNodeType.Elite => RewardTier.Elite,
            MapNodeType.Boss  => RewardTier.Boss,
            _                 => RewardTier.Battle,
        };

        /// <summary>Контент по payload-id, иначе случайный из пула типа T (детерминировано через RNG). null = пул пуст.</summary>
        private T PickContent<T>(MapNode node, RunContext ctx) where T : ContentDefinition
        {
            if (!string.IsNullOrEmpty(node.PayloadId) && _content.TryGet<T>(node.PayloadId, out T byId))
                return byId;

            var all = _content.All<T>();
            return all.Count == 0 ? null : all[ctx.Rng.NextInt(0, all.Count)];
        }

        /// <summary>
        /// Боевой пресет для узла: по payload-id, иначе случайный из пула нужного вида (элитный/обычный). Если
        /// элит-пресетов ещё нет (ассеты — контент B5), откатываемся на обычные (элитка = обычный бой + награда ×2).
        /// </summary>
        private BattlePresetData PickBattlePreset(MapNode node, RunContext ctx, bool wantElite)
        {
            if (!string.IsNullOrEmpty(node.PayloadId) && _content.TryGet<BattlePresetData>(node.PayloadId, out var byId))
                return byId;

            var all = _content.All<BattlePresetData>();
            if (all.Count == 0) return null;

            var pool = new List<BattlePresetData>(all.Count);
            foreach (var p in all)
                if (p != null && p.IsElite == wantElite) pool.Add(p);

            if (pool.Count == 0)
            {
                // Говорим В ЛЮБОМ случае, а не только для Elite: у Boss `wantElite` = false, поэтому финал
                // акта откатывался на случайный обычный бой совершенно молча (аудит фолбэков 2026-07-26, п.2).
                // Сам откат — контентная дыра (нет ни одного _isElite-пресета), её закрывает авторинг.
                Debug.LogWarning($"[NodeResolver] - узел '{node.Type}': нет пресетов вида " +
                                 $"{(wantElite ? "элитный" : "обычный")} → беру случайный из всех");
                return all[ctx.Rng.NextInt(0, all.Count)];
            }
            return pool[ctx.Rng.NextInt(0, pool.Count)];
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
