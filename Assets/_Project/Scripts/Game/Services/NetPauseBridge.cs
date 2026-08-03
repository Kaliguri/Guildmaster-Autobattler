using System;
using Guildmaster.Core.Audio;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Единственное место, где объявленная пауза становится настоящей: состояние приходит от
    /// <see cref="BattleControlRelay"/>, применяет его <see cref="TimeScaleService"/>.
    /// </summary>
    /// <remarks>
    /// <b>Зачем прослойка.</b> Релей живёт в сетевом слое и про показ не знает ничего — он объявляет,
    /// что бой стоит, и кто это сделал. <see cref="TimeScaleService"/> живёт в игровом и не должен
    /// знать про сеть. Мост сводит их, и именно поэтому путь применения паузы ровно один — что в
    /// соло, что в коопе, что при нажатии напарника. Второй путь означал бы два источника правды о
    /// том, стоит ли бой.
    /// <para><b>Щелчок подтверждения звучит здесь,</b> а не у того, кто нажал: пауза, поставленная
    /// напарником, — такое же событие, и молчать на неё нельзя. Звук нужен ещё и потому, что пауза
    /// глушит боевую шину питчем, и без него нажатие не подтверждается ничем.</para>
    /// <para><b>Границу боя пауза не переходит.</b> На смене фазы состояние сбрасывается у обоих
    /// владельцев сразу: у релея — чтобы следующий интент не оказался «уже в этом состоянии» и не
    /// потерялся молча, у времени — чтобы новый бой не начался замороженным.</para>
    /// </remarks>
    public sealed class NetPauseBridge : IStartable, IDisposable
    {
        private readonly BattleControlRelay _relay;
        private readonly TimeScaleService   _time;
        private readonly IBattleClock       _clock;
        private readonly IAudioService      _audio;

        public NetPauseBridge(BattleControlRelay relay, TimeScaleService time, IBattleClock clock,
                              IAudioService audio)
        {
            _relay = relay;
            _time  = time;
            _clock = clock;
            _audio = audio;
        }

        public void Start()
        {
            _relay.PauseChanged += HandlePauseChanged;
            if (_clock != null) _clock.PhaseChanged += HandlePhaseChanged;
        }

        public void Dispose()
        {
            _relay.PauseChanged -= HandlePauseChanged;
            if (_clock != null) _clock.PhaseChanged -= HandlePhaseChanged;
        }

        // Автор паузы здесь не нужен: его показывает интерфейс («паузу поставил Х»), а времени
        // всё равно, чьё нажатие его остановило.
        private void HandlePauseChanged(bool paused, int _)
        {
            _time.SetPaused(paused);
            _audio?.Play(paused ? "ui.pause.ui" : "ui.resume.ui");
        }

        private void HandlePhaseChanged()
        {
            _relay.Reset();
            _time.SetPaused(false);
        }
    }
}
