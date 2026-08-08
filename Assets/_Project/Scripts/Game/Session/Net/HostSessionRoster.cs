using System;
using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Владельческая половина состава сеанса: ведёт список участников, раздаёт стороны и цвета, объявляет
    /// таблицу всем.
    /// </summary>
    /// <remarks>
    /// <b>Список ведёт транспорт, а не догадка.</b> Участники приходят и уходят по событиям подключения,
    /// плюс мы сами. Ровно та же линия, что у гейта готовности: спрашивать «кто в сессии» у чего-то,
    /// кроме соединения, значит держать второе мнение, которое разойдётся при первом же выходе игрока.
    /// <para><b>Сторона назначается по порядку входа</b> — до тех пор, пока не появится лобби PvP с
    /// выбором команды (заявка Макса 03.08.2026). Чередование выбрано вместо «первый против всех»,
    /// потому что оно единственное предсказуемо при трёх и четырёх игроках.</para>
    /// <para><b>Имя гостя приходит от него самого.</b> Steam знает ник по SteamId, но транспорт наружу
    /// личности не отдаёт, и учить его этому ради подписи у курсора дороже, чем одно сообщение при входе.</para>
    /// </remarks>
    public sealed class HostSessionRoster : ISessionRoster, IStartable, ITickable, IDisposable
    {
        private readonly INetTransport  _transport;
        private readonly Guildmaster.Core.Players.IPlatformIdentity _platform;
        private readonly Guildmaster.Core.Persistence.IProfileService _profiles;

        // Где мы сами. Спрашивается каждый кадр, объявляется только на смену — как и у гостя.
        private readonly ILocalWhereabouts _where;
        private PlayerWhere _myWhere = PlayerWhere.Unknown;

        private readonly List<SessionPlayer> _players = new List<SessionPlayer>(4);
        private readonly NetByteWriter       _writer  = new NetByteWriter(128);
        private byte[] _envelope;

        /// <summary>Разводим ли участников по разным сторонам. Заказывает мероприятие; по умолчанию нет.</summary>
        private bool _split;

        /// <summary>
        /// Какой цвет игрок хотел бы. Пожелание, а не назначение: в одной сессии цвета обязаны быть
        /// разными (ГДД, кооп-кластер), а выбирают их в профиле порознь и заранее.
        /// </summary>
        private readonly Dictionary<int, int> _wanted = new Dictionary<int, int>(4);

        /// <summary>
        /// Кого посадили на сторону явно. Это назначение, а не вывод из режима: оно переживает вход и
        /// выход других участников, потому что пересадка иначе затирала бы его чужим порядком входа.
        /// </summary>
        private readonly Dictionary<int, int> _seated = new Dictionary<int, int>(4);

        public HostSessionRoster(INetTransport transport,
                                 Guildmaster.Core.Players.IPlatformIdentity platform,
                                 Guildmaster.Core.Persistence.IProfileService profiles,
                                 ILocalWhereabouts where = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _platform  = platform;
            _profiles  = profiles;
            _where     = where;
        }

        /// <summary>
        /// Своё место тоже видно остальным: хозяин — такой же участник списка.
        /// </summary>
        /// <remarks>
        /// Спрашиваем каждый кадр, объявляем только на смену. Место меняется редко, а таблица состава
        /// идёт надёжным каналом и стоит дороже пакета присутствия — слать её покадрово значило бы
        /// платить за факт, который не менялся.
        /// </remarks>
        public void Tick()
        {
            PlayerWhere now = _where?.Current ?? PlayerWhere.Unknown;
            if (now == _myWhere) return;

            _myWhere = now;
            Reseat(); // место лежит в таблице, а её объявляет пересадка
        }

        /// <summary>
        /// Кем мы играем: ник, цвет и скин курсора берутся из профиля — там их выбрал игрок. Ник может
        /// быть «из Steam», и решает это тоже профиль, а не мы.
        /// </summary>
        private Guildmaster.Core.Persistence.ProfileIdentity MyIdentity =>
            _profiles?.Identity ?? default;

        private string MyName =>
            MyIdentity.ResolveName(_platform != null ? _platform.PlayerName : "Игрок");

        public IReadOnlyList<SessionPlayer> Players => _players;

        public int LocalId => _transport.IsRunning ? _transport.LocalPeerId : NetPeer.HostPeerId;

        public void Start()
        {
            Add(LocalId, MyName);

            // Кто-то мог подключиться ДО нас: транспорт поднимается на входе в игру, а сеанс рождается
            // позже и пересоздаётся на каждой смене режима. Пока состав набирался только по событиям,
            // уже пришедший напарник не появлялся в списке НИКОГДА — и хозяин узнавал о нём лишь тогда,
            // когда тот отключался (наход. Макса 07.08.2026).
            IReadOnlyList<int> already = _transport.ConnectedPeers;
            for (int i = 0; i < already.Count; i++) Add(already[i], $"Игрок {already[i] + 1}");

            _transport.PeerConnected    += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.MessageReceived  += OnMessage;
        }

        public void Dispose()
        {
            _transport.PeerConnected    -= OnPeerConnected;
            _transport.PeerDisconnected -= OnPeerDisconnected;
            _transport.MessageReceived  -= OnMessage;
        }

        public bool TryGet(int playerId, out SessionPlayer player)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Id != playerId) continue;

                player = _players[i];
                return true;
            }

            player = default;
            return false;
        }

        public bool SharesTeamWithLocal(int playerId) =>
            TryGet(playerId, out SessionPlayer them) && TryGet(LocalId, out SessionPlayer me) &&
            them.Team == me.Team;

        public void SplitBetweenSides(bool split)
        {
            if (split == _split) return;

            _split = split;

            // Мероприятие сменилось целиком, значит прежние ручные посадки к нему не относятся:
            // посаженный в PvP на вторую сторону остался бы в кампании противником своей же группе.
            _seated.Clear();
            Reseat();
        }

        /// <inheritdoc />
        public void Seat(int playerId, int team)
        {
            if (!TryGet(playerId, out _)) return; // сажать некого: такого участника в сеансе нет

            _seated[playerId] = team;
            Reseat();
        }

        private void OnPeerConnected(int peerId)
        {
            // Имя пока неизвестно — гость представится сам следующим сообщением. Ставить прочерк нельзя:
            // до его сообщения участник уже виден в списке, и безымянная строка читается как сбой.
            Add(peerId, $"Игрок {peerId + 1}");
        }

        private void OnPeerDisconnected(int peerId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Id != peerId) continue;

                _players.RemoveAt(i);
                break;
            }

            // Назначение уходит вместе с участником: вернувшись, он получит тот же номер пира, и старая
            // посадка досталась бы ему молча — как чужое наследство.
            _seated.Remove(peerId);
            _wanted.Remove(peerId);

            // Стороны и цвета пересчитываются от порядка в списке, а ушедший его сдвинул. Оставить как
            // есть значило бы дыру в цветах и, в PvP на троих, две стороны против одной.
            Reseat();
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.SessionRoster) return;

            // На этом канале гость говорит ровно одно: как его зовут. Объявленную таблицу шлём только мы,
            // и прилететь она к нам не может — принимать её тут значило бы верить чужому составу.
            if (!SessionRosterCodec.TryReadIntro(payload, out SessionIntro intro))
            {
                // Версия и отпечаток контента сверены рукопожатием, поэтому нечитаемое представление —
                // это НАША поломка формата, а не «чужая сборка». Молчание тут уже стоило целого состава.
                Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Session,
                    $"хозяин: представление пира {from} не разобралось — формат канала состава разъехался");
                return;
            }

            if (string.IsNullOrWhiteSpace(intro.Name)) return;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Id != from) continue;

                SessionPlayer was = _players[i];
                _wanted[from] = intro.WantedColorIndex;
                _players[i] = new SessionPlayer(was.Id, intro.Name, was.Team, was.ColorIndex,
                                                intro.CursorSkinId, intro.Where);
                Reseat(); // цвет мог освободиться или, наоборот, столкнуться с чужим пожеланием
                return;
            }
        }

        private void Add(int peerId, string name)
        {
            if (TryGet(peerId, out _)) return;

            string skin = peerId == LocalId ? MyIdentity.CursorSkinId : string.Empty;
            if (peerId == LocalId) _wanted[peerId] = MyIdentity.ColorIndex;

            _players.Add(new SessionPlayer(peerId, name, StartingTeamFor(_players.Count),
                                           _players.Count, skin));
            Reseat();
        }

        /// <summary>
        /// Пересадить всех: стороны по порядку входа, цвета — по пожеланиям, но без повторов.
        /// </summary>
        /// <remarks>
        /// Кто пришёл раньше, тот и оставляет за собой желаемый цвет; опоздавшему выдаётся ближайший
        /// свободный. Отказать во входе или пустить двоих одним цветом нельзя: первое — наказание за
        /// совпадение вкусов, второе убивает единственную функцию мейн-цвета.
        /// </remarks>
        private void Reseat()
        {
            var taken = new HashSet<int>();

            for (int i = 0; i < _players.Count; i++)
            {
                SessionPlayer was = _players[i];

                int wanted = _wanted.TryGetValue(was.Id, out int w) ? w : i;
                int colour = taken.Contains(wanted) ? FirstFree(taken) : wanted;
                taken.Add(colour);

                // Посаженного руками не трогаем: начальная рассадка — умолчание для тех, кого не сажали.
                int team = _seated.TryGetValue(was.Id, out int seated) ? seated : StartingTeamFor(i);

                _players[i] = new SessionPlayer(was.Id, was.Name, team, colour, was.CursorSkinId,
                                                was.Id == LocalId ? _myWhere : was.Where);
            }

            Announce();
        }

        private static int FirstFree(HashSet<int> taken)
        {
            for (int c = 0; ; c++)
                if (!taken.Contains(c)) return c;
        }

        /// <summary>
        /// Какая сторона достаётся месту <paramref name="seat"/> ПО УМОЛЧАНИЮ — до того, как кого-то
        /// посадили руками (<see cref="Seat"/>).
        /// </summary>
        /// <remarks>
        /// <b>Не делим — значит все на ОДНОЙ стороне, нулевой.</b> Дев-ручка «за кого я играю»
        /// (<c>GameConfig.LocalPlayerTeam</c>) отсюда снята 08.08.2026 вместе с самим полем: сторона
        /// перестала быть настройкой и стала назначением, у которого один владелец — этот состав.
        /// Пока ручка была, она успела дважды солгать — сперва разводила хозяина и гостя по разным
        /// сторонам в кампании, потом подменяла собой пустой состав у гостя.
        /// </remarks>
        private int StartingTeamFor(int seat) => _split ? seat % 2 : 0;

        private void Announce()
        {
            if (!_transport.IsRunning) return; // соло: объявлять некому

            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.SessionRoster,
                                 SessionRosterCodec.WriteTable(_players, _writer), ref _envelope),
                NetDelivery.Reliable);
        }
    }
}
