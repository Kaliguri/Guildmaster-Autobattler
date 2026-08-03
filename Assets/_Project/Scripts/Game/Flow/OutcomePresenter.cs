using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ экрана исхода забега (победа/поражение) и ожидание «В меню» (план C2).</summary>
    public interface IOutcomePresenter
    {
        UniTask ShowAsync(bool victory);
    }

    /// <summary>
    /// Презентер исхода забега (план [[act-map-run-loop]] §4 C2): публикует <see cref="OpenOutcomeRequest"/> и ждёт
    /// «В меню». Тот же publish/await-паттерн, что награда.
    /// <para><b>Слушатель UI обязателен</b> — см. разбор у <see cref="MainMenuPresenter"/>.</para>
    /// </summary>
    public sealed class OutcomePresenter : IOutcomePresenter
    {
        private readonly IPublisher<OpenOutcomeRequest> _pub;

        public OutcomePresenter(IPublisher<OpenOutcomeRequest> pub) => _pub = pub;

        public async UniTask ShowAsync(bool victory)
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenOutcomeRequest(victory, () => tcs.TrySetResult()));
            await tcs.Task;
        }
    }
}
