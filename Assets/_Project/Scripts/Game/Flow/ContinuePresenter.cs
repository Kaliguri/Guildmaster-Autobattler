using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ единой кнопки «Продолжить» и ожидание нажатия (план A4).</summary>
    public interface IContinuePresenter
    {
        /// <summary>Показать «Продолжить» (правый нижний угол) и дождаться нажатия. labelKey пуст → дефолт.
        /// <paramref name="ct"/> прерывает ожидание при выходе из забега (QA #18).</summary>
        UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default);
    }

    /// <summary>
    /// Презентер кнопки «Продолжить» (план [[act-map-run-loop]] §4 A4): публикует <see cref="OpenContinueRequest"/>
    /// в UI и ждёт нажатия. Даёт петле акта консистентный «бит» «узел разрешён → Продолжить → карта». Без слушателя
    /// UI (нет CoreScene/роутера) завершается сразу — петля не виснет (headless/тесты используют фейк).
    /// </summary>
    public sealed class ContinuePresenter : IContinuePresenter
    {
        private readonly IPublisher<OpenContinueRequest> _pub;

        public ContinuePresenter(IPublisher<OpenContinueRequest> pub) => _pub = pub;

        public async UniTask WaitForContinueAsync(string labelKey = null, CancellationToken ct = default)
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenContinueRequest(labelKey, () => tcs.TrySetResult(), ct)); // ct → закрыть экран при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ct);
        }
    }
}
