using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Петля акта <see cref="ActRunner"/> (план act-map-run-loop §3.2, A2) на фейковых швах — без сцен/UI/боя.
    /// Закрепляет: проход до босса, награда только за боевые узлы, остановка на поражении, автосейв на переходах,
    /// защита от пустой карты.
    /// </summary>
    public sealed class ActRunnerTests
    {
        private RunStateService _runStates;
        private InMemorySave    _save;

        [SetUp]
        public void SetUp()
        {
            _save = new InMemorySave();
            var config = ScriptableObject.CreateInstance<GameConfig>();
            _runStates = new RunStateService(_save, config);
        }

        private RunContext NewRunWithMap(long seed = 4242L)
        {
            _runStates.NewRun(seed, Array.Empty<RosterSlot>());
            _runStates.BeginAct(); // реальная карта из под-сида
            return new RunContext(_runStates.Current, new XorShiftRng(1), new SoloReadyGate(),
                                  new SoloPlayerIntentSource());
        }

        private ActRunner NewRunner(INodeResolver resolver, IRewardPresenter reward) =>
            new ActRunner(resolver, reward, new AutoFirstNodeChooser(), _runStates);

        private static bool IsBattleish(MapNodeType t) =>
            t == MapNodeType.Battle || t == MapNodeType.Elite || t == MapNodeType.Boss;

        [Test]
        public void RunAct_EmptyMap_ReturnsAborted()
        {
            _runStates.NewRun(1L, Array.Empty<RosterSlot>()); // без BeginAct → карта пустая
            var ctx = new RunContext(_runStates.Current, new XorShiftRng(1), new SoloReadyGate(),
                                     new SoloPlayerIntentSource());
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed), new CountingReward());

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();
            Assert.AreEqual(EventOutcome.Aborted, result.Outcome);
        }

        [Test]
        public void RunAct_WalksToBoss_ReturnsCompleted()
        {
            var ctx = NewRunWithMap();
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed), new CountingReward());

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.IsTrue(MapTraversal.IsActComplete(ctx.RunState.Map));
            Assert.AreEqual(MapNodeType.Boss, MapTraversal.Current(ctx.RunState.Map).Type);
        }

        [Test]
        public void RunAct_PresentsReward_OnlyForBattleNodes()
        {
            var ctx = NewRunWithMap();
            var reward = new CountingReward();
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed), reward);

            runner.RunActAsync(ctx).GetAwaiter().GetResult();

            // Награда выдаётся ровно за пройденные боевые узлы (Battle/Elite/Boss), исключая Start.
            int expected = ctx.RunState.Map.Nodes.Count(n =>
                n.Cleared && n.Type != MapNodeType.Start && IsBattleish(n.Type));
            Assert.AreEqual(expected, reward.TotalCalls, "Награда — только за боевые узлы на пройденном пути.");
            Assert.Greater(expected, 0, "На пути до босса есть хотя бы один боевой узел (сам босс).");
        }

        [Test]
        public void RunAct_Defeat_StopsAndReturnsDefeated()
        {
            var ctx = NewRunWithMap();
            string startId = ctx.RunState.Map.CurrentNodeId;

            // Первый же исполненный узел проигран.
            bool first = true;
            var resolver = new StubResolver(_ =>
            {
                if (!first) return EventResult.Completed;
                first = false;
                return EventResult.Defeated;
            });
            var runner = NewRunner(resolver, new CountingReward());

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.IsFalse(MapTraversal.IsActComplete(ctx.RunState.Map));
            Assert.AreEqual(startId, ctx.RunState.Map.CurrentNodeId, "Поражение до Advance — позиция не сдвинулась.");
        }

        [Test]
        public void RunAct_Autosaves_DuringTraversal()
        {
            var ctx = NewRunWithMap();
            _save.Clear(); // сбросить сейв после BeginAct, чтобы проверить именно автосейв петли
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed), new CountingReward());

            runner.RunActAsync(ctx).GetAwaiter().GetResult();
            Assert.IsTrue(_save.Exists("run"), "Петля автосохраняет забег на переходах.");
        }

        // ── Фейковые швы ──────────────────────────────────────────────

        private sealed class StubResolver : INodeResolver
        {
            private readonly Func<MapNode, EventResult> _result;
            public StubResolver(Func<MapNode, EventResult> result) => _result = result;
            public IEventFlow Resolve(MapNode node, RunContext ctx) => new StubFlow(_result(node));
        }

        private sealed class StubFlow : IEventFlow
        {
            private readonly EventResult _result;
            public StubFlow(EventResult result) => _result = result;
            public UniTask<EventResult> Run(RunContext ctx) => UniTask.FromResult(_result);
        }

        private sealed class CountingReward : IRewardPresenter
        {
            public int TotalCalls { get; private set; }
            public UniTask PresentAsync(RewardTier tier) { TotalCalls++; return UniTask.CompletedTask; }
        }

        private sealed class InMemorySave : ISaveService
        {
            private readonly Dictionary<string, object> _store = new Dictionary<string, object>();
            public void Save<T>(string key, T value) => _store[key] = value;
            public T Load<T>(string key) => _store.TryGetValue(key, out var v) ? (T)v : default;
            public bool Exists(string key) => _store.ContainsKey(key);
            public void Delete(string key) => _store.Remove(key);
            public void Clear() => _store.Clear();
        }
    }
}
