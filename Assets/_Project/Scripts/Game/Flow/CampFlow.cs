using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «привал»: отряд привозит бюджет действий (<see cref="CampSession"/>) и тратит его на
    /// несколько трат ПОДРЯД, пока не уйдёт сам или пока бюджет не кончится. Этим привал и отличается от
    /// текстового ивента: там выбор один и он же выход, здесь выбор повторяемый, а выход — отдельное решение.
    /// <para>Действия пока без эффектов (см. <see cref="CampSession"/>) — узел стоит на карте и тратит бюджет,
    /// сами механики навешиваются позже, не трогая ни петлю, ни резолвер.</para>
    /// </summary>
    public sealed class CampFlow : IEventFlow
    {
        private readonly IPublisher<OpenCampRequest> _openCampPub;

        public CampFlow(IPublisher<OpenCampRequest> openCampPub) => _openCampPub = openCampPub;

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            var session = new CampSession();
            var tcs     = new UniTaskCompletionSource();

            _openCampPub.Publish(new OpenCampRequest(session, () => tcs.TrySetResult(), ctx.Cancellation)); // ct → закрыть при отмене (QA #37)
            await tcs.Task.AttachExternalCancellation(ctx.Cancellation); // игрок ушёл с привала

            return EventResult.Completed;
        }
    }
}
