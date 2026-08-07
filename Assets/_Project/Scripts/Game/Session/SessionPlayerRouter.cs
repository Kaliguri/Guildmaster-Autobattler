using System;
using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Ответ на вопросы «кто с нами играет» и «за какую сторону играю я» для тех, кто переживает сеансы:
    /// корневой UI, показ, бой. Делегирует составу текущего сеанса, а вне сеанса отвечает одиночкой.
    /// </summary>
    /// <remarks>
    /// Того же образца роутер, что <see cref="SessionRunRouter"/> и <c>ActivityClockRouter</c>, и по той
    /// же причине: долгожитель не может держать прямую ссылку на объект из уехавшего вниз скоупа — она
    /// пережила бы своего владельца и отвечала бы про сеанс, который давно кончился.
    /// <para><b>Одна дверь к двум фактам.</b> «Моя команда» — это моя строка в составе сеанса, а не
    /// второе мнение рядом с ним. Пока их было два (<c>SoloLocalPlayer</c> из конфига и захардкоженный
    /// ноль в расстановке), они и расходились: в PvP оба игрока считали своей одну и ту же сторону.</para>
    /// </remarks>
    public sealed class SessionPlayerRouter : ILocalPlayer, ISessionRoster
    {
        private static readonly IReadOnlyList<SessionPlayer> Nobody = Array.Empty<SessionPlayer>();

        private readonly SessionHost _sessions;
        private readonly GameConfig  _config;

        public SessionPlayerRouter(SessionHost sessions, GameConfig config)
        {
            _sessions = sessions;
            _config   = config;
        }

        /// <summary>Сторона локального игрока. Вне сеанса — дев-ручка из конфига.</summary>
        public int Team
        {
            get
            {
                ISessionRoster roster = _sessions?.Roster;
                if (roster != null && roster.TryGet(roster.LocalId, out SessionPlayer me)) return me.Team;

                return _config != null ? _config.LocalPlayerTeam : 0;
            }
        }

        public IReadOnlyList<SessionPlayer> Players => _sessions?.Roster?.Players ?? Nobody;

        public int LocalId => _sessions?.Roster?.LocalId ?? 0;

        public bool TryGet(int playerId, out SessionPlayer player)
        {
            ISessionRoster roster = _sessions?.Roster;
            if (roster != null) return roster.TryGet(playerId, out player);

            player = default;
            return false;
        }

        /// <summary>
        /// Вне сеанса своим считается только сам игрок: показывать чужое присутствие некому, и ответ
        /// «да» здесь означал бы курсор от участника, которого не существует.
        /// </summary>
        public bool SharesTeamWithLocal(int playerId)
        {
            ISessionRoster roster = _sessions?.Roster;
            return roster != null ? roster.SharesTeamWithLocal(playerId) : playerId == LocalId;
        }

        public void SplitBetweenSides(bool split) => _sessions?.Roster?.SplitBetweenSides(split);
    }
}
