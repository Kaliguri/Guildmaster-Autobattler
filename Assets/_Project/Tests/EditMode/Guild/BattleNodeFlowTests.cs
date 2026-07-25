using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Обёртка боевого узла <see cref="BattleNodeFlow"/> (план act-map-run-loop, B4-рефактор): при победе — золото
    /// + награда тира; при поражении/прерывании — проброс без награды.
    /// </summary>
    public sealed class BattleNodeFlowTests
    {
        private RunStateService _runStates;

        private RunContext Ctx()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            _runStates = new RunStateService(new MemSave(), config);
            _runStates.NewRun(1L, Array.Empty<RosterSlot>());
            return new RunContext(_runStates.Current, new XorShiftRng(1), new SoloReadyGate(),
                                  new SoloPlayerIntentSource());
        }

        [Test]
        public void Win_AwardsGold_AndPresentsReward()
        {
            var ctx = Ctx();
            int before = _runStates.Gold;
            var reward = new CountingReward();
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Completed), RewardTier.Battle, reward, _runStates,
                                          new ImmediateContinue(), postWinDelaySeconds: 0f);

            EventResult result = flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.Completed, result.Outcome);
            Assert.AreEqual(before + 20, _runStates.Gold, "Победа даёт +20 (GameConfig.BattleGoldReward код-дефолт).");
            Assert.AreEqual(1, reward.Calls);
            Assert.AreEqual(RewardTier.Battle, reward.LastTier);
        }

        [Test]
        public void EliteWin_PresentsReward_Twice()
        {
            var ctx = Ctx();
            var reward = new CountingReward();
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Completed), RewardTier.Elite, reward, _runStates,
                                          new ImmediateContinue(), rewardCount: 2, postWinDelaySeconds: 0f);

            flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(2, reward.Calls, "Элитка — два выбора реликвии подряд (награда ×2).");
        }

        [Test]
        public void Defeat_NoGold_NoReward_PassesThrough()
        {
            var ctx = Ctx();
            int before = _runStates.Gold;
            var reward = new CountingReward();
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Defeated), RewardTier.Elite, reward, _runStates,
                                          new ImmediateContinue(), postWinDelaySeconds: 0f);

            EventResult result = flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(before, _runStates.Gold);
            Assert.AreEqual(0, reward.Calls);
        }

        [Test]
        public void Win_ResetsArena_OnlyAfterReward()
        {
            var ctx = Ctx();
            var session = new CountingSession();
            var reward = new ResetSpyReward(session);
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Completed), RewardTier.Battle, reward, _runStates,
                                          new ImmediateContinue(), session, postWinDelaySeconds: 0f);

            flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(0, reward.ResetsSeenAtReward, "Пока игрок выбирает награду, поле боя ещё живое.");
            Assert.AreEqual(1, session.ResetCount, "Уход с узла возвращает арену во вне-боевое состояние.");
        }

        [Test]
        public void Defeat_ResetsArena_Too()
        {
            var ctx = Ctx();
            var session = new CountingSession();
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Defeated), RewardTier.Battle, new CountingReward(),
                                          _runStates, new ImmediateContinue(), session, postWinDelaySeconds: 0f);

            flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(1, session.ResetCount, "Поражение тоже уводит с узла — арена не должна залипнуть.");
        }

        private sealed class FixedFlow : IEventFlow
        {
            private readonly EventResult _r;
            public FixedFlow(EventResult r) => _r = r;
            public UniTask<EventResult> Run(RunContext ctx) => UniTask.FromResult(_r);
        }

        private sealed class CountingReward : IRewardPresenter
        {
            public int Calls { get; private set; }
            public RewardTier LastTier { get; private set; }
            public UniTask PresentAsync(RewardTier tier, CancellationToken ct = default) { Calls++; LastTier = tier; return UniTask.CompletedTask; }
        }

        // Headless-мост «К наградам»: резолвит мгновенно (в бою эту кнопку показывает UI).
        private sealed class ImmediateContinue : IContinuePresenter
        {
            public UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default) => UniTask.CompletedTask;
        }

        // Награда, которая подсматривает, успела ли арена очиститься до её показа (не должна).
        private sealed class ResetSpyReward : IRewardPresenter
        {
            private readonly CountingSession _session;
            public ResetSpyReward(CountingSession session) => _session = session;
            public int ResetsSeenAtReward { get; private set; }
            public UniTask PresentAsync(RewardTier tier, CancellationToken ct = default)
            {
                ResetsSeenAtReward = _session.ResetCount;
                return UniTask.CompletedTask;
            }
        }

        // Мост в боевой скоуп: узлу от него нужна только чистка арены, остальное — пустые тела.
        private sealed class CountingSession : IBattleSession
        {
            public int ResetCount { get; private set; }
            public bool RequestReset() { ResetCount++; return true; }

            public void SetPending(BattlePresetData preset) { }
            public bool TryConsumePending(out BattlePresetData preset) { preset = null; return false; }
            public void BindLaunch(Action<BattlePresetData> launch) { }
            public void UnbindLaunch() { }
            public bool RequestLaunch(BattlePresetData preset) => false;
            public void BindReset(Action reset) { }
            public void UnbindReset() { }
            public UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct) => UniTask.FromResult(BattleOutcome.Draw);
            public void ReportOutcome(BattleOutcome outcome) { }
            public void BindRestart(Action restart) { }
            public void UnbindRestart() { }
            public bool RequestRestart() => false;
            public bool RestartInPlace() => false;
            public void SetPhase(BattlePhase phase) => Phase = phase;
            public void BindClock(Func<float> elapsedSeconds) { }
            public void UnbindClock() { }
            public void BindStart(Action start) { }
            public void UnbindStart() { }
            public BattlePhase Phase { get; private set; } = BattlePhase.None;
            public event Action PhaseChanged { add { } remove { } }
            public float ElapsedSeconds => 0f;
            public void RequestStart() { }
        }

        private sealed class MemSave : ISaveService
        {
            private readonly Dictionary<string, object> _s = new();
            public void Save<T>(string key, T value) => _s[key] = value;
            public T Load<T>(string key) => _s.TryGetValue(key, out var v) ? (T)v : default;
            public bool Exists(string key) => _s.ContainsKey(key);
            public void Delete(string key) => _s.Remove(key);
        }
    }
}
