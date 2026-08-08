using Cysharp.Threading.Tasks;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «сундук» (план [[act-map-run-loop]] §4 B3): показывает фасад сундука, ждёт, пока
    /// группа сойдётся его открыть, затем катит награду 1-из-3 через <see cref="IRewardPresenter"/>
    /// (переиспользуем витрину). Возвращает <see cref="EventResult.Completed"/>.
    /// </summary>
    /// <remarks>
    /// <b>Крышку открывают все вместе</b>, потому что за ней общая награда: разреши мы первому нажавшему
    /// — он решал бы за группу, когда ей смотреть витрину. Механизм тот же, что у самой награды, и цена
    /// та же: в соло участник один, и согласие срабатывает в тот же кадр.
    /// </remarks>
    public sealed class ChestFlow : IEventFlow
    {
        private readonly IRewardPresenter _reward;
        private readonly Core.Net.ISharedDecision _decision;
        private readonly Session.Net.HostNodeStage _stage;

        public ChestFlow(IRewardPresenter reward, Core.Net.ISharedDecision decision,
                         Session.Net.HostNodeStage stage = null)
        {
            _reward   = reward;
            _decision = decision;
            _stage    = stage;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            var opened = new UniTaskCompletionSource();

            // Ключ взводим ДО объявления: гость получит сундук и счёт одним разом, а не «сначала
            // крышка, потом откуда-то счёт».
            _decision?.Bind(Core.Net.DecisionKeys.ChestOpen, () => opened.TrySetResult());
            _stage?.Announce(Session.Net.NodeStageState.Chest);

            try
            {
                await opened.Task.AttachExternalCancellation(ctx.Cancellation);
            }
            finally
            {
                _decision?.Unbind(Core.Net.DecisionKeys.ChestOpen);
            }

            await _reward.PresentAsync(RewardTier.Battle, ctx.Cancellation); // 1-из-3 реликвий

            // Единый ритм конца узла (QA #48/#49): награда выдана → кадр-прощание держит экран до
            // следующего узла. Кнопки «дальше» приходят тем же шагом.
            _stage?.Announce(Session.Net.NodeStageState.Idle.EndingNode(
                "ui.node.chest.title", "ui.node.chest.farewell"));

            return EventResult.Completed;
        }
    }
}
