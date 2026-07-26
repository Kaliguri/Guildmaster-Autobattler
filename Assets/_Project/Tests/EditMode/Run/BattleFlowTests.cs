using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Players;
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
    /// перезапуски поражения из пула акта (реш. №65, колбэк). Проверяется на фейковых швах (без сцены и симуляции).
    /// <para>Отдельно закреплено главное: победа — это победа МОЕЙ команды, а не команды с номером 0.
    /// Один и тот же исход боя для клиента из другой команды означает поражение (шов под PvP).</para>
    /// </summary>
    public sealed class BattleFlowTests
    {
        private const int MyTeam    = 0;
        private const int EnemyTeam = 1;

        [Test]
        public void Win_FirstTry_Completed_NoRetries()
        {
            var session = new FakeSession(BattleOutcome.Win(MyTeam));
            var flow    = new BattleFlow(NewPreset(), session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(0, session.RestartCount, "победа с первого раза не должна перезапускать бой");
            Assert.AreEqual(1, session.LaunchCount, "бой запускается один раз (launch в живом скоупе, persist-мир)");
        }

        [Test]
        public void Loss_ThenWin_RetriesOnce_Completed()
        {
            var session = new FakeSession(BattleOutcome.Win(EnemyTeam), BattleOutcome.Win(MyTeam));
            var flow    = new BattleFlow(NewPreset(), session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(1, session.RestartCount, "одно поражение → один ретрай");
        }

        [Test]
        public void Loss_AllRetries_Defeated()
        {
            // 1 бой + 2 ретрая = 3 поражения подряд.
            var session = new FakeSession(BattleOutcome.Win(EnemyTeam), BattleOutcome.Win(EnemyTeam),
                                          BattleOutcome.Win(EnemyTeam));
            var flow    = new BattleFlow(NewPreset(), session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(2, session.RestartCount, "должно быть ровно 2 перезапуска (пул акта)");
        }

        [Test]
        public void Draw_CountsAsDefeat_AndRetries()
        {
            // Ничья победой не считается ни для кого → для игрока это поражение.
            var session = new FakeSession(BattleOutcome.Draw, BattleOutcome.Win(MyTeam));
            var flow    = new BattleFlow(NewPreset(), session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(1, session.RestartCount, "ничья = не-победа → ретрай");
        }

        [Test]
        public void SameOutcome_IsWinOrLoss_DependingOnMyTeam()
        {
            // Один и тот же исход: победила команда 1. Для клиента из команды 1 — победа, из команды 0 — поражение.
            BattleOutcome outcome = BattleOutcome.Win(EnemyTeam);

            var winner = new BattleFlow(NewPreset(), new FakeSession(outcome),
                                        Player(EnemyTeam), Restarts(0));
            var loser  = new BattleFlow(NewPreset(), new FakeSession(outcome),
                                        Player(MyTeam), Restarts(0));

            Assert.AreEqual(EventOutcome.Completed,     Run(winner).Outcome, "для команды-победителя это победа");
            Assert.AreEqual(EventOutcome.PlayerDefeated, Run(loser).Outcome, "для другой команды тот же бой — поражение");
        }

        [Test]
        public void Loss_NoRestartBinding_DefeatedImmediately()
        {
            var session = new FakeSession(BattleOutcome.Win(EnemyTeam)) { CanRestart = false };
            var flow    = new BattleFlow(NewPreset(), session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(0, session.RestartCount, "некому перезапускать → без ретраев");
        }

        [Test]
        public void NullPreset_Aborted_NoLaunch()
        {
            var session = new FakeSession();
            var flow    = new BattleFlow(null, session, Player(MyTeam), Restarts(2));

            EventResult result = Run(flow);

            Assert.AreEqual(EventOutcome.Aborted, result.Outcome);
            Assert.AreEqual(0, session.LaunchCount, "нет пресета — бой запускать не должны");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EventResult Run(BattleFlow flow)
        {
            var ctx = new RunContext(new RunState(), new XorShiftRng(1),
                                     new SoloReadyGate(), new SoloPlayerIntentSource());
            return flow.Run(ctx).GetAwaiter().GetResult();
        }

        private static ILocalPlayer Player(int team) => new FakeLocalPlayer(team);

        // Фейковый пул перезапусков акта: возвращает true (списывает попытку) n раз, затем false.
        private static Func<bool> Restarts(int n)
        {
            int left = n;
            return () => { if (left <= 0) return false; left--; return true; };
        }

        private static BattlePresetData NewPreset()
        {
            // Пустой пресет достаточен: BattleFlow читает только preset != null и preset.Id (для лога).
            return ScriptableObject.CreateInstance<BattlePresetData>();
        }

        private sealed class FakeLocalPlayer : ILocalPlayer
        {
            public FakeLocalPlayer(int team) => Team = team;
            public int Team { get; }
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

            // Persist-мир: launch боя в живом скоупе (заменил связку SetPending + загрузка боевой сцены).
            public int  LaunchCount;
            public bool CanLaunch = true;
            public void BindLaunch(Action<BattlePresetData> launch) { }
            public void UnbindLaunch() { }
            public bool RequestLaunch(BattlePresetData preset)
            {
                if (!CanLaunch) return false;
                LaunchCount++;
                return true;
            }

            // Persist-мир: сброс во вне-боевое состояние после боя.
            public int  ResetCount;
            public void BindReset(Action reset) { }
            public void UnbindReset() { }
            public bool RequestReset() { ResetCount++; return true; }

            public UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct)
            {
                // Пустая очередь = защитный дефолт (не должно случаться при корректной логике флоу).
                BattleOutcome outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : BattleOutcome.Draw;
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

            public bool RestartInPlace() => false; // dev-хоткей R, во флоу-тестах не задействован

            // Часы/фаза/старт панели (план 12 Фаза 2) — не задействованы в этих тестах.
            public BattlePhase Phase => BattlePhase.None;
            public event Action PhaseChanged { add { } remove { } }
            public float ElapsedSeconds => 0f;
            public void SetPhase(BattlePhase phase) { }
            public void BindClock(Func<float> elapsedSeconds) { }
            public void UnbindClock() { }
            public void BindStart(Action start) { }
            public void UnbindStart() { }
            public void RequestStart() { }
        }
    }
}
