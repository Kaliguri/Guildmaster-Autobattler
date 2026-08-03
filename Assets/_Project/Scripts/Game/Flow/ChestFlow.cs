using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «сундук» (план [[act-map-run-loop]] §4 B3): показывает фасад сундука, ждёт клик по крышке, затем
    /// катит награду 1-из-3 через <see cref="IRewardPresenter"/> (переиспользуем витрину). Возвращает
    /// <see cref="EventResult.Completed"/>. Единая кнопка «Продолжить» после — от петли акта (A4).
    /// </summary>
    public sealed class ChestFlow : IEventFlow
    {
        private readonly IPublisher<OpenChestRequest> _openChestPub;
        private readonly IRewardPresenter _reward;
        private readonly IPublisher<OpenNodeFarewellRequest> _farewellPub;

        public ChestFlow(IPublisher<OpenChestRequest> openChestPub, IRewardPresenter reward,
                         IPublisher<OpenNodeFarewellRequest> farewellPub = null)
        {
            _openChestPub = openChestPub;
            _reward       = reward;
            _farewellPub  = farewellPub;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            var tcs = new UniTaskCompletionSource();
            _openChestPub.Publish(new OpenChestRequest(() => tcs.TrySetResult(), ctx.Cancellation)); // ct → закрыть при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ctx.Cancellation); // игрок кликнул крышку

            await _reward.PresentAsync(RewardTier.Battle, ctx.Cancellation); // 1-из-3 реликвий

            // Единый ритм конца узла (QA #48/#49): награда выдана → кадр-прощание держит экран до следующего узла.
            _farewellPub?.Publish(new OpenNodeFarewellRequest(
                "ui.node.chest.title", "ui.node.chest.farewell", ctx.NodeCancellation));

            return EventResult.Completed;
        }
    }
}
