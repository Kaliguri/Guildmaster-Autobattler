using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Completed), RewardTier.Battle, reward, _runStates);

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
                                          rewardCount: 2);

            flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(2, reward.Calls, "Элитка — два выбора реликвии подряд (награда ×2).");
        }

        [Test]
        public void Defeat_NoGold_NoReward_PassesThrough()
        {
            var ctx = Ctx();
            int before = _runStates.Gold;
            var reward = new CountingReward();
            var flow = new BattleNodeFlow(new FixedFlow(EventResult.Defeated), RewardTier.Elite, reward, _runStates);

            EventResult result = flow.Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(EventOutcome.PlayerDefeated, result.Outcome);
            Assert.AreEqual(before, _runStates.Gold);
            Assert.AreEqual(0, reward.Calls);
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
