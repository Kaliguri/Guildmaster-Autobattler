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
        private readonly IPublisher<OpenNodeFarewellRequest> _farewellPub;

        public ShopFlow(ShopController shop, IPublisher<OpenShopRequest> openShopPub,
                        IPublisher<OpenNodeFarewellRequest> farewellPub = null)
        {
            _shop         = shop;
            _openShopPub  = openShopPub;
            _farewellPub  = farewellPub;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            _shop.Open();

            var tcs = new UniTaskCompletionSource();
            _openShopPub.Publish(new OpenShopRequest(_shop, () => tcs.TrySetResult(), ctx.Cancellation)); // ct → закрыть при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ctx.Cancellation);

            // Единый ритм конца узла: витрина закрылась не в мир, а в кадр-прощание, который держит экран,
            // пока игрок не пошёл дальше (QA #48/#49). Уводят с него кнопки бита поверх.
            _farewellPub?.Publish(new OpenNodeFarewellRequest(
                "ui.node.shop.title", "ui.node.shop.farewell", ctx.NodeCancellation));

            return EventResult.Completed;
        }
    }
}
