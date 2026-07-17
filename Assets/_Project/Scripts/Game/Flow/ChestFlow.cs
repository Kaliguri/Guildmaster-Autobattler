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

        public ChestFlow(IPublisher<OpenChestRequest> openChestPub, IRewardPresenter reward)
        {
            _openChestPub = openChestPub;
            _reward       = reward;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            var tcs = new UniTaskCompletionSource();
            _openChestPub.Publish(new OpenChestRequest(() => tcs.TrySetResult()));
            await tcs.Task;                       // игрок кликнул крышку

            await _reward.PresentAsync(RewardTier.Battle); // 1-из-3 реликвий
            return EventResult.Completed;
        }
    }
}
