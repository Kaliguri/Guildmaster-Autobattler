using System;
using Guildmaster.Core.Net;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Принял приглашение посреди своей игры — рвём то, что играли, и уходим в гости.
    /// </summary>
    /// <remarks>
    /// <b>Почему это не «сначала выйди в меню, потом жми».</b> Возврат в меню здесь не костыль, а
    /// честное описание того, что обязано произойти: свой сеанс закрывается, мир сбрасывается, забег
    /// уходит в сейв. Разница лишь в том, показывать ли меню по дороге, — и показывать его незачем
    /// (решение обсуждено с Максом 04.08.2026: «Мб перед этим проигрываются действия в духе „вернуться
    /// в главное меню“... Но мб можно и без таких костылей»).
    /// <para>Поэтому здесь только разрыв: цикл игры сам увидит, что сессия уже гостевая, и уйдёт в
    /// чужую игру, минуя меню (см. <c>GameFlow</c>, ветка «уже идём к кому-то»).</para>
    /// <para><b>Забег не теряется:</b> его пишет автосейв по ходу, и вернуться в него можно после. Это
    /// же сказано игроку в диалоге разрыва — «я нажал и потерял три акта» не должно быть возможным
    /// даже как испуг.</para>
    /// </remarks>
    public sealed class CoopJoinInterrupt : IStartable, IDisposable
    {
        private readonly ICoopSessionControl   _coop;
        private readonly Core.Flow.IRunControl _runControl;

        private CoopSessionState _last;

        public CoopJoinInterrupt(ICoopSessionControl coop, Core.Flow.IRunControl runControl)
        {
            _coop       = coop;
            _runControl = runControl;
        }

        public void Start()
        {
            if (_coop == null) return;

            _last = _coop.State;
            _coop.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            if (_coop == null) return;

            _coop.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(CoopSessionState state)
        {
            CoopSessionState was = _last;
            _last = state;

            // Интересует ровно один переход: мы были не в гостях — и вдруг подключаемся. Всё остальное
            // (стали хостом, вернулись в offline, дошли от Connecting до Connected) рвать нечего.
            if (state != CoopSessionState.Connecting) return;
            if (was == CoopSessionState.Connecting || was == CoopSessionState.Connected) return;

            // Ничего не играем — рвать нечего, цикл и так стоит в меню.
            _runControl?.RequestReturnToMainMenu();
        }
    }
}
