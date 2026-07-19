using System;
using System.Collections.Generic;
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
    /// Петля акта <see cref="ActRunner"/> (план act-map-run-loop §3.2) на фейковых швах — без сцен/UI/боя.
    /// Закрепляет: проход до босса, остановка на поражении без сдвига позиции, автосейв на переходах, защита
    /// от пустой карты. Награда/золото боя проверяются отдельно (BattleNodeFlowTests).
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

        private ActRunner NewRunner(INodeResolver resolver) =>
            new ActRunner(resolver, new ImmediateContinue(), new AutoFirstNodeChooser(), _runStates);

        [Test]
        public void RunAct_EmptyMap_ReturnsAborted()
        {
            _runStates.NewRun(1L, Array.Empty<RosterSlot>()); // без BeginAct → карта пустая
            var ctx = new RunContext(_runStates.Current, new XorShiftRng(1), new SoloReadyGate(),
                                     new SoloPlayerIntentSource());
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed));

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();
            Assert.AreEqual(EventOutcome.Aborted, result.Outcome);
        }

        [Test]
        public void RunAct_WalksToBoss_ReturnsCompleted()
        {
            var ctx = NewRunWithMap();
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed));

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.IsTrue(MapTraversal.IsActComplete(ctx.RunState.Map));
            Assert.AreEqual(MapNodeType.Boss, MapTraversal.Current(ctx.RunState.Map).Type);
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
            var runner = NewRunner(resolver);

            EventResult result = runner.RunActAsync(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.IsFalse(MapTraversal.IsActComplete(ctx.RunState.Map));
            Assert.AreEqual(startId, ctx.RunState.Map.CurrentNodeId, "Поражение до Advance — позиция не сдвинулась.");
        }

        [Test]
        public void RunAct_CancelledDuringNodeChoice_ThrowsOperationCanceled_NotAborted()
        {
            // QA #37: «В главное меню» из паузы отменяет забег. Петля должна ВСПЛЫТЬ OperationCanceledException
            // (→ GameFlow.RunGameAsync ловит → главное меню), а НЕ вернуть Aborted — прежде закрытие карты в
            // null трактовалось как Aborted и роняло play каскадом (снос K11/K12). Регресс на корень бага.
            var cts = new System.Threading.CancellationTokenSource();
            _runStates.NewRun(4242L, Array.Empty<RosterSlot>());
            _runStates.BeginAct();
            var ctx = new RunContext(_runStates.Current, new XorShiftRng(1), new SoloReadyGate(),
                                     new SoloPlayerIntentSource(), cts.Token);
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed));

            cts.Cancel(); // забег прерван из меню до/во время выбора узла

            Assert.Throws<OperationCanceledException>(
                () => runner.RunActAsync(ctx).GetAwaiter().GetResult(),
                "Отмена забега = OperationCanceled, не Aborted.");
        }

        [Test]
        public void RunAct_Autosaves_DuringTraversal()
        {
            var ctx = NewRunWithMap();
            _save.Clear(); // сбросить сейв после BeginAct, чтобы проверить именно автосейв петли
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed));

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

        private sealed class ImmediateContinue : IContinuePresenter
        {
            public UniTask WaitForContinueAsync(string labelKey = null, System.Threading.CancellationToken ct = default)
                => UniTask.CompletedTask;
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
