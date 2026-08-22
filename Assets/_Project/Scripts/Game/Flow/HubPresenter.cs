using Cysharp.Threading.Tasks;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ Двора гильдии и ожидание, пока группа не сойдётся идти в забег.</summary>
    public interface IHubPresenter
    {
        /// <summary>
        /// Открыть двор выбранного дома. Возвращается, когда в забег согласились ВСЕ; отмена
        /// <paramref name="leaving"/> означает, что игрок ушёл со двора, и бросает
        /// <see cref="System.OperationCanceledException"/>.
        /// </summary>
        UniTask ShowAsync(System.Threading.CancellationToken leaving = default);
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
        // Имя акта живёт ключом в конфиге акта: двор говорит, ГДЕ стоит забег, а не «IN PROGRESS».
        private readonly Guildmaster.Guild.ActConfig _actConfig;

        public HubPresenter(IProfileService profiles, Session.SessionHost sessions,
                            Guildmaster.Guild.ActConfig actConfig = null)
        {
            _profiles  = profiles;
            _sessions  = sessions;
            _actConfig = actConfig;
        }

        public async UniTask ShowAsync(System.Threading.CancellationToken leaving = default)
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
            (int act, int level) where = WhereWeStand();
            stage?.Announce(Session.Net.SessionStageState.Hub(
                _profiles?.ActiveGuild.Name, where.act, where.level, _actConfig?.TitleKey));

            try
            {
                // Ждём либо согласия группы, либо ухода со двора: отменённый токен бросает
                // OperationCanceledException, и её ловит верхняя петля — как и всякий выход в меню.
                await agreed.Task.AttachExternalCancellation(leaving);
            }
            finally
            {
                decision?.Unbind(Core.Net.DecisionKeys.RunStart);
                stage?.Clear();
            }
        }

        /// <summary>
        /// Где стоит забег: номер акта (1..N) и ступень маршрута. Забега нет — нули, и двор просто
        /// промолчит о месте, вместо того чтобы называть «Акт 0».
        /// </summary>
        /// <remarks>
        /// Ступень берём у ТЕКУЩЕГО узла карты, а не считаем пройденные: узел знает свой этаж сам, и
        /// счёт по пройденным разошёлся бы с картой на первой же развилке.
        /// </remarks>
        private (int Act, int Level) WhereWeStand()
        {
            Guildmaster.Guild.RunState run = _sessions?.Run?.Current;
            if (run == null) return (0, 0);

            int act = run.CurrentActIndex + 1;

            Guildmaster.Guild.MapState map = run.Map;
            if (map == null || map.Nodes == null) return (act, 0);

            for (int i = 0; i < map.Nodes.Length; i++)
                if (map.Nodes[i] != null && map.Nodes[i].Id == map.CurrentNodeId)
                    return (act, map.Nodes[i].Floor + 1);

            return (act, 0);
        }
    }
}
