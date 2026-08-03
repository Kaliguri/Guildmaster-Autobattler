using System;
using Guildmaster.Core.Net;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Вход гостем: как только хост нас принял, уводит игрока из главного меню в чужую игру.
    /// </summary>
    /// <remarks>
    /// <b>Почему на «принят», а не на «соединились».</b> Между этими событиями стоит рукопожатие —
    /// версия сборки и отпечаток контента. Уйди мы из меню раньше, отказ («у вас другая версия
    /// контента») застал бы игрока уже в пустом мире, и вернуть его было бы некуда.
    /// <para><b>Сам сеанс открывает верхний цикл игры</b>, а не этот мост: сеанс — это жизнь, у которой
    /// есть начало и конец, и открывать её должен тот, кто доведёт её до конца. Мост только приносит
    /// новость.</para>
    /// <para><b>Живёт в корне,</b> потому что приглашение приходит когда угодно: в меню, на заставке,
    /// между забегами. Сеанса в этот момент может не быть вовсе.</para>
    /// </remarks>
    public sealed class CoopGuestEntry : IStartable, IDisposable
    {
        private readonly ICoopSessionControl _coop;
        private readonly UI.MenuRouter       _menus;

        public CoopGuestEntry(ICoopSessionControl coop, UI.MenuRouter menus)
        {
            _coop  = coop  ?? throw new ArgumentNullException(nameof(coop));
            _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        }

        public void Start() => _coop.StateChanged += HandleStateChanged;

        public void Dispose() => _coop.StateChanged -= HandleStateChanged;

        private void HandleStateChanged(CoopSessionState state)
        {
            if (state != CoopSessionState.Connected) return;

            _menus.TryLeaveMainMenuForCoopGuest();
        }
    }
}
