using System;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Activity
{
    /// <summary>
    /// Часы боя для тех, кто живёт дольше мероприятия: корневой UI, навигатор, звук забега. Делегирует
    /// часам текущего занятия, а вне занятия честно отвечает «боя нет».
    /// </summary>
    /// <remarks>
    /// <b>Тот же приём, что у кадра показа</b> (<c>StageFrameRouter</c>): владелец факта один, а
    /// потребители не знают, кто именно им отвечает сейчас. Без роутера у корневых объектов остались бы
    /// ссылки на часы мероприятия, которое давно кончилось, — и панель забега показывала бы фазу
    /// прошлого забега, потому что объект жив, а игра ушла дальше.
    /// <para><b>Событие смены фазы переподписывается на каждое занятие.</b> Роутер вечен, а часы под ним
    /// меняются; подписчикам об этом знать незачем, поэтому подписка на живые часы — его работа.</para>
    /// </remarks>
    public sealed class ActivityClockRouter : IBattleClock, IDisposable
    {
        private readonly ActivityHost _activities;

        private IBattleClock _bound;
        private BattlePhase  _lastPhase = BattlePhase.None;

        public ActivityClockRouter(ActivityHost activities) => _activities = activities;

        public BattlePhase Phase
        {
            get
            {
                Rebind();
                return _bound?.Phase ?? BattlePhase.None;
            }
        }

        public float ElapsedSeconds
        {
            get
            {
                Rebind();
                return _bound?.ElapsedSeconds ?? 0f;
            }
        }

        public event Action PhaseChanged;

        public void RequestStart()
        {
            Rebind();
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"роутер часов: старт → часы занятия {(_bound == null ? "ОТСУТСТВУЮТ (занятия нет?)" : "есть")}");
            _bound?.RequestStart();
        }

        public void Dispose() => Unbind();

        /// <summary>
        /// Догнать смену занятия. Спрашиваем по обращению, а не тикаем своим циклом: у роутера нет
        /// собственного времени, и заводить его ради переподписки значило бы завести второй ритм.
        /// </summary>
        private void Rebind()
        {
            IBattleClock live = _activities.Clock;
            if (ReferenceEquals(live, _bound)) return;

            Unbind();
            _bound = live;
            if (_bound != null) _bound.PhaseChanged += OnPhaseChanged;

            // Смена занятия сама по себе меняет фазу для наблюдателя: было «идёт бой» — стало «боя нет».
            OnPhaseChanged();
        }

        private void Unbind()
        {
            if (_bound != null) _bound.PhaseChanged -= OnPhaseChanged;
            _bound = null;
        }

        private void OnPhaseChanged()
        {
            BattlePhase phase = _bound?.Phase ?? BattlePhase.None;
            if (phase == _lastPhase) return;

            _lastPhase = phase;
            PhaseChanged?.Invoke();
        }
    }
}
