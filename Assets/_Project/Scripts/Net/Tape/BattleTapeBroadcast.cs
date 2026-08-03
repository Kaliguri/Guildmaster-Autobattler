using Guildmaster.Combat;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Провод между живым боем и раздачей: раз в кадр отдаёт стримеру всё, что симуляция уже досчитала,
    /// а на конце боя дожимает хвост.
    /// </summary>
    /// <remarks>
    /// <b>Почему отдельный класс, а не вызов из <c>CombatLoopService</c>.</b> Цикл тика — владелец
    /// пульса симуляции, и знать про сеть ему незачем: сегодня раздача идёт всем, завтра она
    /// закрывается на время ставок, и каждое такое правило добавляло бы ветку в горячий цикл. Здесь же
    /// оно живёт одно и целиком.
    /// <para><b>Раздаём по последний ЗАВЕРШЁННЫЙ тик</b> — тот же, под которым лента снимает кадр
    /// (<c>CurrentTick - 1</c>). Тик, который сейчас считается, кадра ещё не имеет, и попросить его
    /// значило бы регулярно писать пустые чанки.</para>
    /// <para><b>Гость от этого не увидит будущее.</b> Хост уезжает вперёд на окно опережения, но у
    /// гостя свой <c>BattleTapePlayback</c> с тем же лагом: он копит ленту и показывает её позже. Это и
    /// есть «сим впереди, показ с лагом», просто половины разнесены по машинам.</para>
    /// <para><b>Собирается только у владельца сеанса</b> (см. <c>CombatLifetimeScope</c>): гость
    /// раздавал бы обратно ленту, которую ему же и прислали. Соло — тот же владелец, только без
    /// поднятого транспорта, и нарезки чанков он не платит: раздача выходит первой строкой тика.</para>
    /// </remarks>
    public sealed class BattleTapeBroadcast : ITickable, System.IDisposable
    {
        private readonly CombatSimulation _simulation;
        private readonly TapeStreamer     _streamer;
        private readonly INetTransport    _transport;

        private bool _flushed;

        public BattleTapeBroadcast(CombatSimulation simulation, TapeStreamer streamer,
                                   INetTransport transport)
        {
            _simulation = simulation;
            _streamer   = streamer;
            _transport  = transport;

            _simulation.OnBattleReset += HandleBattleReset;
        }

        public void Tick()
        {
            // Соединения нет — раздавать некому. Это и есть соло: одно ветвление вместо нарезки чанков,
            // которые никто не примет.
            if (!_transport.IsRunning) return;

            int readyThrough = _simulation.CurrentTick - 1;
            if (readyThrough < 0) return;

            if (_simulation.Outcome == BattleOutcome.Ongoing)
            {
                _streamer.Pump(readyThrough);
                _flushed = false;
                return;
            }

            // Бой кончился: хвост короче чанка иначе остался бы у хоста, а в нём едет исход. Один раз —
            // после боя арена живёт дальше (мир не выгружается), и Flush каждый кадр слал бы пустоту.
            if (_flushed) return;
            _streamer.Flush(readyThrough);
            _flushed = true;
        }

        public void Dispose() => _simulation.OnBattleReset -= HandleBattleReset;

        // Dev-рестарт боя на месте: у хоста лента чистится, тики и номера чанков идут заново — значит и
        // раздача обязана начать с нуля, иначе гость примет старые номера за дубли и не увидит новый бой.
        private void HandleBattleReset()
        {
            _streamer.Reset();
            _flushed = false;
        }
    }
}
