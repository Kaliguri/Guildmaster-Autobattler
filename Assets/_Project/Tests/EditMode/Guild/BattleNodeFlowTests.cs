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
            var config = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), config, new FixedProfileService());
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

        // Headless-мост к награде: резолвит мгновенно (в игре эту кнопку показывает UI).
        private sealed class ImmediateContinue : IContinuePresenter
        {
            public UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default) => UniTask.CompletedTask;
            public void ShowRestBeat(Action onContinue, Action onFormation, CancellationToken ct) { }
        }

    }
}
