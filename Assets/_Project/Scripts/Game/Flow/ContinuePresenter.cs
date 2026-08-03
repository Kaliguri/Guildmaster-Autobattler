using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ кнопок «бита» между узлами и (для гейта) ожидание нажатия (план A4).</summary>
    public interface IContinuePresenter
    {
        /// <summary>Гейт: показать одну кнопку и ЖДАТЬ нажатия. labelKey пуст → дефолт «Продолжить».
        /// <paramref name="ct"/> прерывает ожидание при выходе из забега (QA #18).</summary>
        UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default);

        /// <summary>
        /// Передышка: показать две кнопки и СРАЗУ вернуть управление — узел уже засчитан, петля ничего не ждёт.
        /// Экран живёт, пока <paramref name="ct"/> не отменят (узел выбран / забег брошен); нажатие любой
        /// кнопки тоже его снимает. Оба места достижимы и табами — кнопки лишь короткий путь.
        /// </summary>
        void ShowRestBeat(Action onContinue, Action onFormation, CancellationToken ct);
    }

    /// <summary>
    /// Презентер кнопок бита (план [[act-map-run-loop]] §4 A4): публикует <see cref="OpenContinueRequest"/>
    /// в UI. Гейт ждёт нажатия, передышка — нет. Без слушателя UI (нет CoreScene/роутера) гейт завершается
    /// сразу — петля не виснет (headless/тесты используют фейк).
    /// </summary>
    public sealed class ContinuePresenter : IContinuePresenter
    {
        // Подписи кнопок бита (таблица UI): RU заполнен, остальные локали — прочерк до перевода.
        private const string ContinueKey  = "ui.beat.continue";
        private const string FormationKey = "ui.beat.formation";

        private readonly IPublisher<OpenContinueRequest> _pub;

        public ContinuePresenter(IPublisher<OpenContinueRequest> pub) => _pub = pub;

        public async UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default)
        {
            var tcs = new UniTaskCompletionSource();
            string key = string.IsNullOrEmpty(labelKey) ? ContinueKey : labelKey;
            _pub.Publish(new OpenContinueRequest(key, () => tcs.TrySetResult(), ct)); // ct → закрыть экран при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ct);
        }

        public void ShowRestBeat(Action onContinue, Action onFormation, CancellationToken ct) =>
            _pub.Publish(new OpenContinueRequest(ContinueKey, onContinue, ct, onFormation, FormationKey));
    }
}
