using System;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ главного меню и ожидание выбора Начать/Продолжить/Ристалище/Выход (план D1).</summary>
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
    public sealed class MainMenuPresenter : IMainMenuPresenter, IDisposable
    {
        private readonly IPublisher<OpenMainMenuRequest> _pub;
        private readonly IDisposable _provingSubscription;

        // Ожидание выбора в текущем показе меню — по нему запрос Ристалища «нажимает кнопку» за игрока.
        private UniTaskCompletionSource<MainMenuChoice> _pending;

        // Запрос пришёл, когда меню не на экране (шёл забег). Держим до ближайшего показа: иначе команда
        // из боя терялась бы молча — Publish без слушателя это no-op, а цикл возвращается в меню позже.
        private bool _provingGroundsPending;

        public MainMenuPresenter(IPublisher<OpenMainMenuRequest> pub,
                                 ISubscriber<Core.Flow.OpenProvingGroundsRequest> provingSub)
        {
            _pub = pub;
            _provingSubscription = provingSub?.Subscribe(_ => OnProvingGroundsRequested());
        }

        public void Dispose() => _provingSubscription?.Dispose();

        public async UniTask<MainMenuChoice> ShowAsync(bool hasSave)
        {
            // Запрос пришёл раньше показа (команда из забега) — отдаём исход сразу, меню не мелькает.
            if (_provingGroundsPending)
            {
                _provingGroundsPending = false;
                return MainMenuChoice.ProvingGrounds;
            }

            var tcs = new UniTaskCompletionSource<MainMenuChoice>();
            _pending = tcs;
            _pub.Publish(new OpenMainMenuRequest(hasSave, c => tcs.TrySetResult(c), null));

            MainMenuChoice choice = await tcs.Task;
            _pending = null;
            return choice;
        }

        private void OnProvingGroundsRequested()
        {
            // Меню на экране — завершаем его тем же исходом, что дала бы кнопка. Нет — запоминаем.
            if (_pending != null && _pending.TrySetResult(MainMenuChoice.ProvingGrounds)) return;
            _provingGroundsPending = true;
        }
    }
}
