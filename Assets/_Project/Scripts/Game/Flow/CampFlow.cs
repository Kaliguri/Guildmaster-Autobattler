using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «привал»: отряд привозит бюджет действий (<see cref="CampSession"/>) и тратит его на
    /// несколько трат ПОДРЯД, пока не уйдёт сам или пока бюджет не кончится. Этим привал и отличается от
    /// текстового ивента: там выбор один и он же выход, здесь выбор повторяемый, а выход — отдельное решение.
    /// <para>Свой эффект пока есть у одного действия — «снять последствие»; остальные тратят бюджет
    /// вхолостую и обзаведутся механиками позже, не трогая ни петлю, ни резолвер.</para>
    /// </summary>
    public sealed class CampFlow : IEventFlow
    {
        private readonly IPublisher<OpenCampRequest> _openCampPub;
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;
        private readonly Session.Net.HostSessionStage _stage;

        public CampFlow(IPublisher<OpenCampRequest> openCampPub,
                        Guildmaster.Guild.Commands.IRunCommands commands,
                        Session.Net.HostSessionStage stage = null)
        {
            _openCampPub = openCampPub;
            _commands    = commands;
            _stage       = stage;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            var session = new CampSession(effect: Perform);
            var tcs     = new UniTaskCompletionSource();

            _openCampPub.Publish(new OpenCampRequest(session, () => tcs.TrySetResult(), ctx.Cancellation)); // ct → закрыть при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ctx.Cancellation); // игрок ушёл с привала

            // Единый ритм конца узла (QA #48/#49): привал сворачивается в кадр-прощание до следующего узла.
            _stage?.Announce(Session.Net.SessionStageState.Idle.EndingNode(
                "ui.node.camp.title", "ui.node.camp.farewell"));

            return EventResult.Completed;
        }

        /// <summary>
        /// Исполнить действие привала. Сегодня своё дело есть у «снять последствие»: рана уходит
        /// бесплатно — за неё уже заплачено действием отряда, а не золотом (ГДД
        /// <c>injuries-mettle</c> §Как снимается).
        /// </summary>
        /// <remarks>
        /// Прочие действия возвращают <c>true</c> и тратят бюджет вхолостую: их механик ещё нет, а
        /// отказывать в кнопке значило бы соврать про то, чего привал не умеет. Появится механика —
        /// появится ветка, и ни петля, ни экран об этом не узнают.
        /// </remarks>
        private bool Perform(CampAction action, int slotIndex, string consequenceId)
        {
            if (action != CampAction.Cleanse) return true;
            if (slotIndex < 0 || string.IsNullOrEmpty(consequenceId)) return false;

            _commands.HealInjury(slotIndex, consequenceId, payGold: false);
            return true;
        }
    }
}
