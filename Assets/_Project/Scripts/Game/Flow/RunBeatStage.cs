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
        private readonly IBattleSession     _session;
        private readonly IContinuePresenter _continue;
        private readonly IPublisher<SetWorldMapRequest>  _mapPub;
        private readonly IPublisher<SetFormationRequest> _formationPub;

        public RunBeatStage(IBattleSession session, IContinuePresenter continuePresenter,
                            IPublisher<SetWorldMapRequest> mapPub, IPublisher<SetFormationRequest> formationPub)
        {
            _session      = session;
            _continue     = continuePresenter;
            _mapPub       = mapPub;
            _formationPub = formationPub;
        }

        public void EnterRestBeat(CancellationToken ct)
        {
            _session.RequestReset();                        // мир возвращается (сейчас — мгновенно)
            _session.SetPhase(BattlePhase.Interlude);       // мир на экране → задник UI запрещён
            _continue.ShowRestBeat(
                onContinue:  () => _mapPub?.Publish(new SetWorldMapRequest(true)),
                onFormation: () => _formationPub?.Publish(new SetFormationRequest(true)),
                ct: ct);
        }

        public void EnterNode() => _session.SetPhase(BattlePhase.None);
    }
}
