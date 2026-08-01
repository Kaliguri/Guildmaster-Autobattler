using Guildmaster.Combat;
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
    /// </remarks>
    public sealed class BattleTapeBroadcast : ITickable
    {
        private readonly CombatSimulation _simulation;
        private readonly TapeStreamer     _streamer;

        private bool _flushed;

        public BattleTapeBroadcast(CombatSimulation simulation, TapeStreamer streamer)
        {
            _simulation = simulation;
            _streamer   = streamer;

            _simulation.OnBattleReset += HandleBattleReset;
        }

        /// <summary>
        /// Раздача открыта. Гейт существует ради ставок: пока они открыты, лента не уходит игрокам даже
        /// фрагментом (дизайн коопа). Закрывать её будет тот, кто ставками владеет, — здесь только ручка.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public void Tick()
        {
            if (!Enabled) return;

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
