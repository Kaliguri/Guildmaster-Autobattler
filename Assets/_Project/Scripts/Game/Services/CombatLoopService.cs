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

            // Цикл живёт всю жизнь боевого скоупа. Тикает только при активном бою (Outcome == Ongoing);
            // после конца боя простаивает (не копит время), а dev-рестарт на месте (ResetBattle → Ongoing)
            // сам возобновляет тик — без перезапуска цикла и без перезагрузки сцены.
            while (_running && !cancellation.IsCancellationRequested)
            {
                if (_simulation.Outcome != BattleOutcome.Ongoing)
                {
                    _accumulator = 0f;
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: cancellation);
                    continue;
                }

                _accumulator += Time.deltaTime;

                // Анти-лавина: не больше N догоняющих тиков за кадр. Иначе один долгий кадр
                // (GC/загрузка/alt-tab) копит время → десятки тиков → ещё больший подвис.
                int ticksThisFrame = 0;
                while (_accumulator >= SimConstants.TickDelta
                       && ticksThisFrame < SimConstants.MaxCatchUpTicksPerFrame)
                {
                    _simulation.Tick(SimConstants.TickDelta);
                    _accumulator -= SimConstants.TickDelta;
                    ticksThisFrame++;

                    if (_simulation.Outcome != BattleOutcome.Ongoing) break;
                }

                // Упёрлись в кап, а долг ещё есть — отбрасываем остаток, чтобы не копить лавину.
                if (ticksThisFrame >= SimConstants.MaxCatchUpTicksPerFrame
                    && _accumulator > SimConstants.TickDelta)
                {
                    _accumulator = 0f;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: cancellation);
            }

            _running = false;
        }

        /// <summary>Остановить цикл принудительно (например, при выгрузке сцены).</summary>
        public void Stop() => _running = false;
    }
}
