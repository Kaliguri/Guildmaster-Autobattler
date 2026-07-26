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
    /// выбор. Настройки обрабатываются UI поверх меню (не завершают ожидание).
    /// <para><b>Слушатель UI обязателен.</b> Прежний докстринг обещал «без слушателя возвращает Quit, чтобы
    /// headless-запуск не завис» — обещания не существовало: <c>Publish</c> без подписчиков это no-op, TCS
    /// никто не завершает, таймаута нет. Единственный способ остаться здесь навсегда — ранний выход
    /// <c>UiRootBootstrap.Start</c>, который не регистрирует подписки; он теперь кричит ошибкой, а не
    /// предупреждением (аудит фолбэков 2026-07-26, п.3). Ложное обещание снято, чтобы следующий читатель
    /// не искал страховку, которой нет.</para>
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
