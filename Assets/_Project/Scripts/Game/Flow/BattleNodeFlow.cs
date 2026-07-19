using Cysharp.Threading.Tasks;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Обёртка боевого узла (план [[act-map-run-loop]]): прогоняет внутренний бой (<see cref="BattleFlow"/>) и при
    /// победе начисляет золото + катит награду тира. Держит награду В САМОМ УЗЛЕ (не в петле по типу карты) — так
    /// «?»-узел, роллящий бой, тоже получает награду, а <c>ActRunner</c> остаётся тонким. Поражение/прерывание —
    /// пробрасываются как есть.
    /// </summary>
    public sealed class BattleNodeFlow : IEventFlow
    {
        private readonly IEventFlow       _battle;
        private readonly RewardTier       _tier;
        private readonly IRewardPresenter _reward;
        private readonly RunStateService  _runStates;
        private readonly int              _rewardCount;

        public BattleNodeFlow(IEventFlow battle, RewardTier tier, IRewardPresenter reward, RunStateService runStates,
                              int rewardCount = 1)
        {
            _battle       = battle;
            _tier         = tier;
            _reward       = reward;
            _runStates    = runStates;
            _rewardCount  = rewardCount < 1 ? 1 : rewardCount;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            EventResult result = await _battle.Run(ctx);
            if (result.Outcome != EventOutcome.Completed) return result;

            _runStates.AwardBattleReward();               // +золото за победу (B1)
            for (int i = 0; i < _rewardCount; i++)        // элитка = 2 выбора подряд (B5)
                await _reward.PresentAsync(_tier, ctx.Cancellation); // ct → отмена забега размотает награду (QA #37)
            return result;
        }
    }
}
