using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ Двора гильдии и ожидание, пока группа не сойдётся идти в забег.</summary>
    public interface IHubPresenter
    {
        /// <summary>Открыть двор выбранного дома. Возвращается, когда в забег согласились ВСЕ.</summary>
        UniTask ShowAsync();
    }

    /// <summary>
    /// Презентер Двора — тем же образцом, что <see cref="MainMenuPresenter"/> и
    /// <see cref="ProfilePresenter"/>: публикует запрос и ждёт ответа.
    /// </summary>
    /// <remarks>
    /// Имя дома берётся у службы профилей здесь, а не в UI: экран — разметка, а не владелец правил, и
    /// спрашивать про активную гильдию ему нечем.
    /// <para><b>В забег выходят вместе</b> (вердикт Макса 08.08.2026: «Надо вообще его сделать когда
    /// кликают оба, как с готовностью»). Кнопка не закрывает двор, а отправляет голос; закрывается
    /// двор объявлением — тем же самым у хозяина и у гостя. Пока кнопка закрывала экран сама, дать её
    /// гостю было нельзя: он ушёл бы со двора один, оставив напарника стоять.</para>
    /// <para><b>В соло не меняется ничего</b>: участник один, согласие срабатывает в тот же кадр.</para>
    /// </remarks>
    public sealed class HubPresenter : IHubPresenter
    {
        private readonly IPublisher<OpenHubRequest> _pub;
        private readonly IProfileService            _profiles;
        private readonly Session.SessionHost        _sessions;

        public HubPresenter(IPublisher<OpenHubRequest> pub, IProfileService profiles,
                            Session.SessionHost sessions)
        {
            _pub      = pub;
            _profiles = profiles;
            _sessions = sessions;
        }

        public async UniTask ShowAsync()
        {
            var agreed = new UniTaskCompletionSource();
            Core.Net.ISharedDecision decision = _sessions?.Decision;

            // Срок жизни двора: сошлись — экран снят. Кнопка его больше не закрывает, поэтому
            // закрывает объявление, и у владельца это тот же момент, что у гостя.
            var open = new System.Threading.CancellationTokenSource();

            // Ключ взводим ДО показа: иначе первый нажавший голосует в пустоту, а счёт на кнопке
            // появляется позже самой кнопки.
            decision?.Bind(Core.Net.DecisionKeys.RunStart, () => agreed.TrySetResult());

            _pub.Publish(new OpenHubRequest(
                _profiles?.ActiveGuild.Name,
                () => decision?.ToggleLocal(),
                open.Token));

            try
            {
                await agreed.Task;
            }
            finally
            {
                decision?.Unbind(Core.Net.DecisionKeys.RunStart);
                open.Cancel();
                open.Dispose();
            }
        }
    }
}
