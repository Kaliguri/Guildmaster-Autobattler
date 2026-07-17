using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ главного меню и ожидание выбора Начать/Продолжить/Выход (план D1).</summary>
    public interface IMainMenuPresenter
    {
        UniTask<MainMenuChoice> ShowAsync(bool hasSave);
    }

    /// <summary>
    /// Презентер главного меню (план [[act-map-run-loop]] §4 D1): публикует <see cref="OpenMainMenuRequest"/> и ждёт
    /// выбор. Настройки обрабатываются UI поверх меню (не завершают ожидание). Без слушателя UI возвращает Quit,
    /// чтобы headless-запуск не завис.
    /// </summary>
    public sealed class MainMenuPresenter : IMainMenuPresenter
    {
        private readonly IPublisher<OpenMainMenuRequest> _pub;

        public MainMenuPresenter(IPublisher<OpenMainMenuRequest> pub) => _pub = pub;

        public async UniTask<MainMenuChoice> ShowAsync(bool hasSave)
        {
            var tcs = new UniTaskCompletionSource<MainMenuChoice>();
            _pub.Publish(new OpenMainMenuRequest(hasSave, c => tcs.TrySetResult(c), null));
            return await tcs.Task;
        }
    }
}
