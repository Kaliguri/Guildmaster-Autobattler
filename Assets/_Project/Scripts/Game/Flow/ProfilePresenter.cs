using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ экрана профиля и ожидание, пока игрок его закроет.</summary>
    public interface IProfilePresenter
    {
        /// <summary>Открыть профиль по желанию игрока (уйти можно в любой момент).</summary>
        UniTask ShowAsync();

        /// <summary>
        /// Убедиться, что профиль есть: если нет — показать экран без выхода и ждать, пока игрок заведёт
        /// слот. Профиль уже есть — возвращается сразу, экран не мелькает.
        /// </summary>
        UniTask RequireAsync();
    }

    /// <summary>
    /// Презентер профиля — тем же образцом, что <see cref="MainMenuPresenter"/>: публикует запрос и ждёт
    /// ответа UI.
    /// </summary>
    /// <remarks>
    /// <b>Обязательный показ идёт ПЕРЕД главным меню, а не внутри него.</b> Профиль — это «кем я
    /// захожу»: спрашивать его после того, как игрок выбрал режим и дом, значит спрашивать посреди
    /// другого решения. К тому же дом живёт ВНУТРИ профиля, и выбирать его раньше слота попросту нечем.
    /// </remarks>
    public sealed class ProfilePresenter : IProfilePresenter
    {
        private readonly IPublisher<OpenProfileRequest> _pub;
        private readonly IProfileService                _profiles;

        public ProfilePresenter(IPublisher<OpenProfileRequest> pub, IProfileService profiles)
        {
            _pub      = pub;
            _profiles = profiles;
        }

        public UniTask ShowAsync() => Open(required: false);

        public UniTask RequireAsync()
        {
            if (_profiles != null && _profiles.HasActiveProfile) return UniTask.CompletedTask;
            return Open(required: true);
        }

        private UniTask Open(bool required)
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenProfileRequest(required, () => tcs.TrySetResult()));
            return tcs.Task;
        }
    }
}
