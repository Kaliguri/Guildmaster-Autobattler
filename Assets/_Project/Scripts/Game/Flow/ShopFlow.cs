using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «магазин» (план [[act-map-run-loop]] §4 B2): открывает витрину (<see cref="IShopController.Open"/>
    /// через <see cref="ShopController"/>), публикует запрос в UI, ждёт, пока игрок уйдёт, и возвращает
    /// <see cref="EventResult.Completed"/>. Покупки/реролл/продажа идут через контроллер прямо во время показа.
    /// </summary>
    public sealed class ShopFlow : IEventFlow
    {
        private readonly ShopController _shop;
        private readonly IPublisher<OpenShopRequest> _openShopPub;

        public ShopFlow(ShopController shop, IPublisher<OpenShopRequest> openShopPub)
        {
            _shop        = shop;
            _openShopPub = openShopPub;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            _shop.Open();

            var tcs = new UniTaskCompletionSource();
            _openShopPub.Publish(new OpenShopRequest(_shop, () => tcs.TrySetResult(), ctx.Cancellation)); // ct → закрыть при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ctx.Cancellation);

            return EventResult.Completed;
        }
    }
}
