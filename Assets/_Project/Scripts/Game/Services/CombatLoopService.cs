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
    public sealed class CombatLoopService : IAsyncStartable, ISimInterpolation
    {
        private readonly CombatSimulation _simulation;
        private readonly Combat.Tape.BattleTapeRecorder _tapeRecorder;

        private float _accumulator;
        private bool  _running;

        /// <summary>
        /// Доля шага, накопленная сверх последнего тика. Аккумулятор здесь — единственный, кто знает,
        /// сколько времени прошло с прошлого шага, поэтому и долю отдаёт он. Презентация её только
        /// читает (см. <see cref="ISimInterpolation"/>).
        /// </summary>
        public float Alpha => Mathf.Clamp01(_accumulator / SimConstants.TickDelta);

        public CombatLoopService(CombatSimulation simulation, Combat.Tape.BattleTapeRecorder tapeRecorder)
        {
            _simulation   = simulation;
            _tapeRecorder = tapeRecorder;
        }

        /// <summary>
        /// Запускает тиковый цикл. Останавливается когда бой завершён или скоуп уничтожен.
        /// </summary>
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            _running     = true;
            _accumulator = 0f;

            try
            {
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
                        // Кадр ленты снимается сразу за тиком: состояние на юнитах — ровно то, что
                        // этот тик досчитал. Показ читает ленту, а не живой сим (§7.2 ТЗ).
                        _tapeRecorder.CaptureTick();
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
            }
            catch (System.OperationCanceledException)
            {
                // Штатное завершение: боевой скоуп выгружен или выход из play отменил токен.
                // UniTask.Yield бросает OCE по отмене — это не ошибка, глушим (иначе VContainer
                // EntryPointExceptionHandler залогирует её красным как падение EntryPoint).
            }
            finally
            {
                _running = false;
            }
        }

        // Ручного Stop() здесь нет: цикл живёт ровно столько, сколько боевой скоуп, и завершается
        // токеном отмены. Прежняя ручка «остановить при выгрузке сцены» осталась от модели, где бой
        // был отдельной сценой; в persist-мире её не звал никто (аудит 2026-07-26).
    }
}
