using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реалтайм-пульс боевой симуляции: accumulator-паттерн на <c>Time.unscaledDeltaTime</c>.
    /// Время Unity читается ТОЛЬКО здесь — в <see cref="CombatSimulation"/> его нет.
    /// Реализует <see cref="IAsyncStartable"/> для авто-запуска через VContainer EntryPoint.
    /// <para><b>Тикает только тот, кто считает бой сам</b> — соло-игрок и хост. У гостя симуляции нет:
    /// он смотрит ленту, которую хост раздаёт чанками, и цикл у него сводится к одной строке
    /// (<see cref="GuestFrame"/>). Роль спрашивается каждый кадр, а не при регистрации: боевой скоуп
    /// поднимается на буте, когда сети ещё нет.</para>
    /// <para><b>Почему UNSCALED:</b> пауза и slowmo — свойства ПОКАЗА, а не просчёта («сим впереди,
    /// показ с лагом»). Масштабированное время тормозило бы и расчёт: в финальном slowmo просчёт полз
    /// бы вместе с картинкой, хотя именно запас впереди и позволяет режиссуре знать будущее. Показ свою
    /// долю кадра берёт от <c>Time.deltaTime</c> — там масштаб как раз нужен.</para>
    /// </summary>
    public sealed class CombatLoopService : IAsyncStartable
    {
        private readonly CombatSimulation _simulation;
        private readonly Combat.Tape.BattleTapeRecorder _tapeRecorder;
        private readonly Combat.Tape.BattleTapePlayback _playback;
        private readonly Data.Definitions.IBattleClock  _clock;
        private readonly Core.Net.IBattleAuthority      _authority;

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
            Data.Definitions.IBattleClock clock,
            Core.Net.IBattleAuthority authority)
        {
            _simulation   = simulation;
            _tapeRecorder = tapeRecorder;
            _playback     = playback;
            _clock        = clock;
            _authority    = authority;
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
                    // Гость своей симуляции не тикает вовсе (см. GuestFrame): ни одного шага, ни одного
                    // снятого кадра — иначе он затёр бы присланную ленту своей пустой ареной.
                    if (!_authority.SimulatesLocally)
                    {
                        GuestFrame();
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: cancellation);
                        continue;
                    }

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

                    // Просчёт живёт в НЕмасштабированном времени: пауза и slowmo его не касаются.
                    _accumulator += Time.unscaledDeltaTime;

                    // Убегать вперёд дальше окна снимков нельзя: вытесним кадр, который сейчас на
                    // экране. Так пауза (показ стоит, время просчёта идёт) не съедает картинку.
                    if (_playback.AtWindowLimit) _accumulator = 0f;

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
                           && !_playback.AtWindowLimit
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

        /// <summary>
        /// Кадр гостя: тика нет, есть только требование к отставанию показа.
        /// </summary>
        /// <remarks>
        /// <b>Отставание у гостя постоянное, без оглядки на фазу боя,</b> и это не упрощение. Лента
        /// гостя пополняется исключительно тогда, когда у хоста идёт бой: вне боя фронт хостовой ленты
        /// стоит на месте (кадр каждый раз перезаписывает один и тот же тик), раздавать нечего, и
        /// чанки не уходят. Значит растущая лента у гостя и есть «бой идёт», а спрашивать об этом
        /// местные часы бессмысленно — они у гостя ведутся его собственным флоу, которого нет.
        /// <para>Лаг тот же, что у хоста, намеренно: хост показывает <c>фронт − окно</c>, и гость,
        /// показывающий свой фронт вплотную, увидел бы бой на десять секунд раньше напарника. Общий
        /// лаг сводит обе картинки к одному моменту боя с точностью до задержки сети.</para>
        /// </remarks>
        private void GuestFrame()
            => _playback.SetTargetLead(Combat.Tape.BattleTapePlayback.LookaheadTicks);

        // Ручного Stop() здесь нет: цикл живёт ровно столько, сколько боевой скоуп, и завершается
        // токеном отмены. Прежняя ручка «остановить при выгрузке сцены» осталась от модели, где бой
        // был отдельной сценой; в persist-мире её не звал никто (аудит 2026-07-26).
    }
}
