using System.Threading;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Что происходит с МИРОМ на стыках узлов забега. Петля (<see cref="Guildmaster.Game.Services.ActRunner"/>)
    /// решает «узел кончился / начинается следующий», а как при этом выглядит арена и что предложено игроку —
    /// знает эта штука. Шов держит петлю чистой и позволяет прогонять её в тестах без мира.
    /// </summary>
    public interface IRunBeatStage
    {
        /// <summary>
        /// Узел пройден: вернуть мир (враги и трупы прочь, отряд в своё построение), встать в передышку и
        /// показать её кнопки. Петля НЕ ждёт этих кнопок — узел уже засчитан.
        /// <paramref name="ct"/> снимает кнопки, когда игрок выбрал следующий узел.
        /// </summary>
        void EnterRestBeat(CancellationToken ct);

        /// <summary>Игрок входит в узел: мира на первом плане больше нет (у боя своя фаза, он поставит её сам).</summary>
        void EnterNode();
    }

    /// <summary>
    /// Соло-реализация: возврат мира — команда в живой боевой скоуп (<see cref="IBattleSession.RequestReset"/>),
    /// кнопки — через <see cref="IContinuePresenter"/>, а сами кнопки лишь публикуют интенты, которые и так
    /// висят на табах: «Продолжить» = открыть карту, «К построению» = встать в расстановку на боевой арене.
    /// <para>Возврат мира сегодня мгновенный. По замыслу (Макс, 2026-07-26) это анимация — трупы тают, отряд
    /// возвращается на места, а «К построению» проигрывает её ×3; шов под скорость появится вместе с ней.</para>
    /// </summary>
    public sealed class RunBeatStage : IRunBeatStage
    {
        private readonly IBattleSession _session;
        private readonly Session.Net.HostNodeStage _stage;

        public RunBeatStage(IBattleSession session, Session.Net.HostNodeStage stage)
        {
            _session = session;
            _stage   = stage;
        }

        /// <summary>
        /// Передышка ОБЪЯВЛЯЕТСЯ, а не показывается отсюда.
        /// </summary>
        /// <remarks>
        /// Раньше петля публиковала экран напрямую — и он существовал только у владельца, потому что
        /// петля собирается только ему. Теперь шаг узла объявлен обоим, а показывает его общий
        /// потребитель (<c>NodeStageScreens</c>), одинаково у хозяина и гостя (HARD-правило «равные
        /// игроки», 08.08.2026).
        /// <para>Кнопки при этом остались тем же, чем были, — шорткатами к табам: петля их не ждёт,
        /// узел уже засчитан, и каждый жмёт свою для себя.</para>
        /// </remarks>
        public void EnterRestBeat(CancellationToken ct)
        {
            _session.RequestReset();                        // мир возвращается (сейчас — мгновенно)
            _session.SetPhase(BattlePhase.Interlude);       // мир на экране → задник UI запрещён

            // Кнопки навешиваются на ТЕКУЩИЙ шаг, а не подменяют его: под ними остаётся то, что оставил
            // узел, — кадр-прощание сундука, текстовое событие с результатом, арена после боя. Объяви
            // петля свой шаг — она стёрла бы экран, про который ничего не знает.
            if (_stage != null) _stage.Announce(_stage.Current.EndingNode());

            // Узел выбран (или забег отменён) — снимаем шаг у ВСЕХ: подключившийся следом иначе
            // увидел бы передышку, которой уже нет.
            if (ct.CanBeCanceled) ct.Register(() => _stage?.Clear());
        }

        public void EnterNode()
        {
            _stage?.Clear();
            _session.SetPhase(BattlePhase.None);
        }
    }
}
