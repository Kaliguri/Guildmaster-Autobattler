using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ единой кнопки «Продолжить» и ожидание нажатия (план A4).</summary>
    public interface IContinuePresenter
    {
        /// <summary>Показать «Продолжить» (правый нижний угол) и дождаться нажатия. labelKey пуст → дефолт.</summary>
        UniTask WaitForContinueAsync(string labelKey = null);
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

        public async UniTask WaitForContinueAsync(string labelKey = null)
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenContinueRequest(labelKey, () => tcs.TrySetResult()));
            await tcs.Task;
        }
    }
}
