using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ Двора гильдии и ожидание, пока игрок не уйдёт в забег.</summary>
    public interface IHubPresenter
    {
        /// <summary>Открыть двор выбранного дома. Возвращается, когда игрок нажал «Начать забег».</summary>
        UniTask ShowAsync();
    }

    /// <summary>
    /// Презентер Двора — тем же образцом, что <see cref="MainMenuPresenter"/> и
    /// <see cref="ProfilePresenter"/>: публикует запрос и ждёт ответа UI.
    /// </summary>
    /// <remarks>
    /// Имя дома берётся у службы профилей здесь, а не в UI: экран — разметка, а не владелец правил, и
    /// спрашивать про активную гильдию ему нечем.
    /// </remarks>
    public sealed class HubPresenter : IHubPresenter
    {
        private readonly IPublisher<OpenHubRequest> _pub;
        private readonly IProfileService            _profiles;

        public HubPresenter(IPublisher<OpenHubRequest> pub, IProfileService profiles)
        {
            _pub      = pub;
            _profiles = profiles;
        }

        public UniTask ShowAsync()
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenHubRequest(_profiles?.ActiveGuild.Name, () => tcs.TrySetResult()));
            return tcs.Task;
        }
    }
}
