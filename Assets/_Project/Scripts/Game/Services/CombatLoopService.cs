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
        private readonly Combat.Tape.BattleTapeRecorder _tapeRecorder;
        private readonly Combat.Tape.BattleTapePlayback _playback;
        private readonly Data.Definitions.IBattleClock  _clock;

        private float _accumulator;
        private bool  _running;

        /// <summary>
        /// Максимум тиков разгона за кадр. Разгон нужен, чтобы сим ушёл вперёд показа: тикая ровно по
        /// реальному времени, он никуда бы не уехал и никакого «знания будущего» не появилось.
        /// Тот же смысл, что у анти-лавины, но с другой стороны: не догнать прошлое, а набрать запас.
        /// </summary>
        private const int MaxLeadTicksPerFrame = 30;

        public CombatLoopService(
            CombatSimulation simulation,
            Combat.Tape.BattleTapeRecorder tapeRecorder,
            Combat.Tape.BattleTapePlayback playback,
            Data.Definitions.IBattleClock clock)
        {
            _simulation   = simulation;
            _tapeRecorder = tapeRecorder;
            _playback     = playback;
            _clock        = clock;
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
                    // Лаг — только на показ БОЯ. Мир, карта, расстановка идут в реальном времени: там
                    // игрок нажимает сам и обязан видеть результат немедленно (уточнение Макса 2026-07-29).
                    bool fighting = _clock != null && _clock.Phase == Data.Definitions.BattlePhase.Fighting;
                    _playback.SetTargetLead(fighting ? Combat.Tape.BattleTapePlayback.LookaheadTicks : 0);

                    if (_simulation.Outcome != BattleOutcome.Ongoing)
                    {
                        _accumulator = 0f;
                        // Бой не идёт, но юниты на арене стоят (мир, расстановка) — показ читает ленту,
                        // поэтому кадр состояния всё равно нужен, иначе арена окажется пустой.
                        _tapeRecorder.CaptureCurrentState();
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
                        _tapeRecorder.CaptureCurrentState();
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

                    // Разгон: гоним сим ВПЕРЁД показа, пока не набран запас. Это и есть механизм лага —
                    // показ идёт в реальном времени, а сим уходит от него на окно опережения и потому
                    // знает будущее. Бюджет на кадр держит разгон незаметным для кадровой частоты.
                    int leadTicks = 0;
                    while (leadTicks < MaxLeadTicksPerFrame
                           && !_playback.HasFullLead
                           && _simulation.Outcome == BattleOutcome.Ongoing)
                    {
                        _simulation.Tick(SimConstants.TickDelta);
                        _tapeRecorder.CaptureCurrentState();
                        leadTicks++;
                    }

                    // Кадр состояния — в любом случае, раз в кадр рендера. Тик мог не наступить вовсе
                    // (пауза в расстановке, нехватка накопленного времени), а состояние при этом
                    // меняется: игрок двигает юнитов сам. Без этого лента оставалась бы пустой.
                    _tapeRecorder.CaptureCurrentState();

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
