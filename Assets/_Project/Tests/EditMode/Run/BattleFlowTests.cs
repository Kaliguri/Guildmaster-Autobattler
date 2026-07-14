using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Логика узла боя (план 11 §4 A2): маппинг <see cref="BattleOutcome"/> → <see cref="EventResult"/> и
    /// ретраи поражения. Проверяется на фейковых швах (без реальной сцены и симуляции).
    /// </summary>
    public sealed class BattleFlowTests
    {
        [Test]
        public void Win_FirstTry_Completed_NoRetries()
        {
            var session = new FakeSession(BattleOutcome.TeamAWins);
            var scenes  = new FakeScenes();
            var flow    = new BattleFlow(NewPreset(), scenes, session, maxRetries: 2);

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(0, session.RestartCount, "победа с первого раза не должна перезапускать бой");
            Assert.AreEqual(1, scenes.Loaded);
            Assert.AreEqual(1, scenes.Unloaded, "сцена должна выгружаться даже при успехе");
        }

        [Test]
        public void Loss_ThenWin_RetriesOnce_Completed()
        {
            var session = new FakeSession(BattleOutcome.TeamBWins, BattleOutcome.TeamAWins);
            var flow    = new BattleFlow(NewPreset(), new FakeScenes(), session, maxRetries: 2);

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(1, session.RestartCount, "одно поражение → один ретрай");
        }

        [Test]
        public void Loss_AllRetries_Defeated()
        {
            // 1 бой + 2 ретрая = 3 поражения подряд.
            var session = new FakeSession(BattleOutcome.TeamBWins, BattleOutcome.TeamBWins, BattleOutcome.TeamBWins);
            var flow    = new BattleFlow(NewPreset(), new FakeScenes(), session, maxRetries: 2);

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(2, session.RestartCount, "должно быть ровно maxRetries перезапусков");
        }

        [Test]
        public void Loss_NoRestartBinding_DefeatedImmediately()
        {
            var session = new FakeSession(BattleOutcome.TeamBWins) { CanRestart = false };
            var flow    = new BattleFlow(NewPreset(), new FakeScenes(), session, maxRetries: 2);

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(0, session.RestartCount, "некому перезапускать → без ретраев");
        }

        [Test]
        public void NullPreset_Aborted_NoSceneLoad()
        {
            var scenes = new FakeScenes();
            var flow   = new BattleFlow(null, scenes, new FakeSession(), maxRetries: 2);

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Aborted, result.Outcome);
            Assert.AreEqual(0, scenes.Loaded, "нет пресета — сцену грузить не должны");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EventResult Run(BattleFlow flow)
        {
            var ctx = new RunContext(new RunState(), new XorShiftRng(1),
                                     new SoloReadyGate(), new SoloPlayerIntentSource());
            return flow.Run(ctx).GetAwaiter().GetResult();
        }

        private static BattlePresetData NewPreset()
        {
            // Пустой пресет достаточен: BattleFlow читает только preset != null и preset.Id (для лога).
            return ScriptableObject.CreateInstance<BattlePresetData>();
        }

        private sealed class FakeScenes : ISceneLoader
        {
            public int Loaded;
            public int Unloaded;
            public UniTask LoadBattleAsync()   { Loaded++;   return UniTask.CompletedTask; }
            public UniTask UnloadBattleAsync() { Unloaded++; return UniTask.CompletedTask; }
        }

        private sealed class FakeSession : IBattleSession
        {
            private readonly Queue<BattleOutcome> _outcomes;
            public int  RestartCount;
            public bool CanRestart = true;

            public FakeSession(params BattleOutcome[] outcomes)
            {
                _outcomes = new Queue<BattleOutcome>(outcomes);
            }

            public void SetPending(BattlePresetData preset) { }
            public bool TryConsumePending(out BattlePresetData preset) { preset = null; return false; }

            public UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct)
            {
                // Пустая очередь = защитный дефолт (не должно случаться при корректной логике флоу).
                BattleOutcome outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : BattleOutcome.TeamBWins;
                return UniTask.FromResult(outcome);
            }

            public void ReportOutcome(BattleOutcome outcome) { }
            public void BindRestart(Action restart) { }
            public void UnbindRestart() { }

            public bool RequestRestart()
            {
                if (!CanRestart) return false;
                RestartCount++;
                return true;
            }
        }
    }
}
