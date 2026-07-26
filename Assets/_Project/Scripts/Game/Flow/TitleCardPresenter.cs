using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ boot title card до главного меню и ожидание закрытия.</summary>
    public interface ITitleCardPresenter
    {
        UniTask ShowAsync();
    }

    /// <summary>
    /// Презентер boot title card: публикует <see cref="OpenTitleCardRequest"/> и ждёт dismiss.
    /// <para><b>Слушатель UI обязателен</b> — см. разбор у <see cref="MainMenuPresenter"/>. Это ПЕРВЫЙ await
    /// петли игры, поэтому без подписчика игра встаёт на чёрном экране ещё до главного меню.</para>
    /// </summary>
    public sealed class TitleCardPresenter : ITitleCardPresenter
    {
        private readonly IPublisher<OpenTitleCardRequest> _pub;

        public TitleCardPresenter(IPublisher<OpenTitleCardRequest> pub) => _pub = pub;

        public async UniTask ShowAsync()
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenTitleCardRequest(() => tcs.TrySetResult()));
            await tcs.Task;
        }
    }
}
