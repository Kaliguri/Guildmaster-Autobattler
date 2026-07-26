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

        private SpyBeat _beat;

        private ActRunner NewRunner(INodeResolver resolver)
        {
            _beat = new SpyBeat();
            return new ActRunner(resolver, new AutoFirstNodeChooser(), _runStates, _beat);
        }

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
        public void RunAct_RestBeat_BetweenNodes_NotOnActEntry()
        {
            var ctx = NewRunWithMap();
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed));

            runner.RunActAsync(ctx).GetAwaiter().GetResult();

            // Вход в акт открывает карту сразу — передышки там нет; дальше она между каждой парой узлов.
            Assert.AreEqual(_beat.NodeEntries - 1, _beat.RestBeats,
                "Передышка положена на каждом стыке узлов, кроме входа в акт.");
            Assert.Greater(_beat.RestBeats, 0, "Между узлами игрок обязан оказаться в живом мире.");
        }

        [Test]
        public void RunAct_NodeScreenLives_UntilPlayerEntersNextNode()
        {
            // QA #49: экран узла (текст-прощание ивента) обязан пережить свой флоу и всю передышку — гаснет
            // он, только когда игрок вошёл в СЛЕДУЮЩИЙ узел. Живёт это на токене узла: пока идёт свой узел,
            // токен цел; отменяется он на входе в следующий.
            var ctx = NewRunWithMap();
            var tokens = new List<System.Threading.CancellationToken>();
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed, tokens));

            runner.RunActAsync(ctx).GetAwaiter().GetResult();

            Assert.Greater(tokens.Count, 1, "Для проверки нужно хотя бы два пройденных узла.");
            for (int i = 0; i < tokens.Count - 1; i++)
                Assert.IsTrue(tokens[i].IsCancellationRequested,
                    $"Экран узла [{i}] обязан сняться, когда игрок вошёл в следующий.");
            Assert.IsTrue(tokens[tokens.Count - 1].IsCancellationRequested,
                "Последний экран снимается вместе с концом акта.");
        }

        [Test]
        public void RunAct_NodeToken_StillAlive_WhileItsOwnNodeRuns()
        {
            // Обратная половина того же контракта: во время СВОЕГО узла токен не должен быть отменён —
            // иначе экран гас бы прямо под руками игрока, ещё до выбора варианта.
            var ctx = NewRunWithMap();
            bool aliveDuringOwnNode = true;
            var runner = NewRunner(new StubResolver(_ => EventResult.Completed, onRun: nodeCtx =>
            {
                if (nodeCtx.NodeCancellation.IsCancellationRequested) aliveDuringOwnNode = false;
            }));

            runner.RunActAsync(ctx).GetAwaiter().GetResult();
            Assert.IsTrue(aliveDuringOwnNode, "Пока идёт свой узел, его экран снимать нельзя.");
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
            private readonly List<System.Threading.CancellationToken> _tokens; // токены узлов (QA #49)
            private readonly Action<RunContext> _onRun;

            public StubResolver(Func<MapNode, EventResult> result,
                                List<System.Threading.CancellationToken> tokens = null,
                                Action<RunContext> onRun = null)
            {
                _result = result;
                _tokens = tokens;
                _onRun  = onRun;
            }

            public IEventFlow Resolve(MapNode node, RunContext ctx) => new StubFlow(_result(node), _tokens, _onRun);
        }

        private sealed class StubFlow : IEventFlow
        {
            private readonly EventResult _result;
            private readonly List<System.Threading.CancellationToken> _tokens;
            private readonly Action<RunContext> _onRun;

            public StubFlow(EventResult result, List<System.Threading.CancellationToken> tokens = null,
                            Action<RunContext> onRun = null)
            {
                _result = result;
                _tokens = tokens;
                _onRun  = onRun;
            }

            public UniTask<EventResult> Run(RunContext ctx)
            {
                _tokens?.Add(ctx.NodeCancellation);
                _onRun?.Invoke(ctx);
                return UniTask.FromResult(_result);
            }
        }

        // Стыки узлов: считаем возвраты мира и входы в узел, чтобы петля не «забыла» вернуть арену.
        private sealed class SpyBeat : IRunBeatStage
        {
            public int RestBeats;
            public int NodeEntries;
            public void EnterRestBeat(System.Threading.CancellationToken ct) => RestBeats++;
            public void EnterNode() => NodeEntries++;
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
