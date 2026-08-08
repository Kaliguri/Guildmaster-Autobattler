using Cysharp.Threading.Tasks;
using Guildmaster.Core.Net;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ экрана исхода забега (победа/поражение) и ожидание, куда группа пойдёт (план C2).</summary>
    public interface IOutcomePresenter
    {
        /// <summary>
        /// Показать исход и дождаться общего выхода: во двор или новый забег.
        /// <paramref name="ct"/> — токен забега: «В меню» отменяет его тем же путём, что и пауза, и
        /// ожидание кончается <see cref="System.OperationCanceledException"/>.
        /// </summary>
        UniTask<RunOutcomeChoice> ShowAsync(bool victory, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// Презентер исхода забега: объявляет шаг узла и ждёт, куда группа пойдёт дальше.
    /// </summary>
    /// <remarks>
    /// <b>Экран показывает не он</b>, а общий для обеих ролей потребитель (<c>NodeStageScreens</c>).
    /// Пока презентер публиковал экран сам, гость исхода забега не видел вовсе: петля акта собирается
    /// только владельцу (HARD-правило «равные игроки», 08.08.2026).
    /// <para><b>«Заново» и «во двор» — общий выбор, «в меню» — личный</b> (вердикт Макса 08.08.2026).
    /// Общие ходят ОДНИМ решением с вариантами: механизм держит один заказ за раз, и два решения
    /// подряд гасили бы голоса друг друга.</para>
    /// <para><b>«В меню» не заводит своего пути</b>: кнопка зовёт <c>IRunControl</c> — ровно то же, что
    /// «В главное меню» из паузы. У владельца это отменяет забег, у гостя — уводит из чужого сеанса;
    /// вызов один и тот же, а разница живёт там, где ей и место, — в сетевой части. Здесь ожидание
    /// кончается по токену, а не по третьему варианту решения.</para>
    /// </remarks>
    public sealed class OutcomePresenter : IOutcomePresenter
    {
        private readonly Session.SessionHost _sessions;

        public OutcomePresenter(Session.SessionHost sessions) => _sessions = sessions;

        public async UniTask<RunOutcomeChoice> ShowAsync(bool victory,
                                                        System.Threading.CancellationToken ct = default)
        {
            var chosen = new UniTaskCompletionSource<RunOutcomeChoice>();

            ISharedDecision decision = _sessions?.Decision;
            Session.Net.HostNodeStage stage = _sessions?.NodeStage;

            // Ключ взводим ДО объявления: гость получит экран и счёт одним разом, а не «сначала
            // кнопки, потом откуда-то счёт».
            decision?.Bind(DecisionKeys.RunAfter, option => chosen.TrySetResult(
                option == RunAfterOptions.Restart ? RunOutcomeChoice.Restart : RunOutcomeChoice.ToGuild));

            stage?.Announce(Session.Net.NodeStageState.Outcome(victory));

            try
            {
                return await chosen.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                decision?.Unbind(DecisionKeys.RunAfter);
                stage?.Clear();
            }
        }
    }
}
