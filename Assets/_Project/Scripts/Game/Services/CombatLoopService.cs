using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реалтайм-пульс боевой симуляции: accumulator-паттерн на <c>Time.deltaTime</c>.
    /// <c>Time.deltaTime</c> используется ТОЛЬКО здесь — в <see cref="CombatSimulation"/> его нет.
    /// Реализует <see cref="IAsyncStartable"/> для авто-запуска через VContainer EntryPoint.
    /// Тикует только хост (в мультиплеере); клиент применяет команды и следит за checksum.
    /// (вики «10» §5.1).
    /// </summary>
    public sealed class CombatLoopService : IAsyncStartable
    {
        private readonly CombatSimulation _simulation;

        private float _accumulator;
        private bool  _running;

        public CombatLoopService(CombatSimulation simulation)
        {
            _simulation = simulation;
        }

        /// <summary>
        /// Запускает тиковый цикл. Останавливается когда бой завершён или скоуп уничтожен.
        /// </summary>
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            _running     = true;
            _accumulator = 0f;

            while (_running && _simulation.Outcome == BattleOutcome.Ongoing
                             && !cancellation.IsCancellationRequested)
            {
                _accumulator += Time.deltaTime;

                while (_accumulator >= SimConstants.TickDelta)
                {
                    _simulation.Tick(SimConstants.TickDelta);
                    _accumulator -= SimConstants.TickDelta;

                    if (_simulation.Outcome != BattleOutcome.Ongoing) break;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: cancellation);
            }

            _running = false;
        }

        /// <summary>Остановить цикл принудительно (например, при выгрузке сцены).</summary>
        public void Stop() => _running = false;
    }
}
