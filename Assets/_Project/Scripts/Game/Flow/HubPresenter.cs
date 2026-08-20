using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ Двора гильдии и ожидание, пока группа не сойдётся идти в забег.</summary>
    public interface IHubPresenter
    {
        /// <summary>Открыть двор выбранного дома. Возвращается, когда в забег согласились ВСЕ.</summary>
        UniTask ShowAsync();
    }

    /// <summary>
    /// Презентер Двора: объявляет шаг «двор» и ждёт, пока группа сойдётся идти в забег.
    /// </summary>
    /// <remarks>
    /// Имя дома берётся у службы профилей здесь, а не в UI: экран — разметка, а не владелец правил, и
    /// спрашивать про активную гильдию ему нечем.
    /// <para><b>Запрос экрана отсюда больше не публикуется</b> (09.08.2026): двор показывает общий для
    /// обеих ролей шов (<c>SessionStageScreens</c>), а этот презентер только ОБЪЯВЛЯЕТ шаг — как и
    /// всякий, кто ведёт узел. Пока запрос шёл прямо в UI, у гостя двор поднимался вторым путём, через
    /// <c>ActivityState.HubOpen</c>, и это был последний экран с двумя дорогами показа.</para>
    /// <para><b>В забег выходят вместе</b> (вердикт Макса 08.08.2026: «Надо вообще его сделать когда
    /// кликают оба, как с готовностью»). Кнопка не закрывает двор, а отправляет голос; закрывается
    /// двор объявлением — тем же самым у хозяина и у гостя. Пока кнопка закрывала экран сама, дать её
    /// гостю было нельзя: он ушёл бы со двора один, оставив напарника стоять.</para>
    /// <para><b>В соло не меняется ничего</b>: участник один, согласие срабатывает в тот же кадр.</para>
    /// </remarks>
    public sealed class HubPresenter : IHubPresenter
    {
        private readonly IProfileService     _profiles;
        private readonly Session.SessionHost _sessions;

        public HubPresenter(IProfileService profiles, Session.SessionHost sessions)
        {
            _profiles = profiles;
            _sessions = sessions;
        }

        public async UniTask ShowAsync()
        {
            var agreed = new UniTaskCompletionSource();
            Core.Net.ISharedDecision decision = _sessions?.Decision;
            Session.Net.HostSessionStage stage = _sessions?.SessionStage;

            // Ключ взводим ДО показа: иначе первый нажавший голосует в пустоту, а счёт на кнопке
            // появляется позже самой кнопки.
            decision?.Bind(Core.Net.DecisionKeys.RunStart, () => agreed.TrySetResult());

            // Двор ОБЪЯВЛЯЕТСЯ, а не публикуется напрямую: показывает его общий для обеих ролей шов,
            // и снимается он сменой шага. Пока петля публиковала запрос сама, у гостя двор поднимался
            // вторым путём (ActivityState.HubOpen) — и это был последний экран с двумя дорогами.
            stage?.Announce(Session.Net.SessionStageState.Hub(_profiles?.ActiveGuild.Name));

            try
            {
                await agreed.Task;
            }
            finally
            {
                decision?.Unbind(Core.Net.DecisionKeys.RunStart);
                stage?.Clear();
            }
        }
    }
}
