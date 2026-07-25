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
    /// Без слушателя UI завершается сразу (headless/тесты) — тот же publish/await-паттерн, что Outcome.
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
