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
    public sealed class HostSessionRoster : ISessionRoster, IStartable, IDisposable
    {
        private readonly INetTransport                 _transport;
        private readonly Guildmaster.Net.Session.SteamBootstrap _steam;
        private readonly GameConfig                    _config;

        private readonly List<SessionPlayer> _players = new List<SessionPlayer>(4);
        private readonly NetByteWriter       _writer  = new NetByteWriter(128);
        private byte[] _envelope;

        /// <summary>По скольким сторонам раскладываем. Одна — все свои; две — PvP.</summary>
        private int _sides = 1;

        public HostSessionRoster(INetTransport transport,
                                 Guildmaster.Net.Session.SteamBootstrap steam,
                                 GameConfig config)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _steam     = steam;
            _config    = config;
        }

        public IReadOnlyList<SessionPlayer> Players => _players;

        public int LocalId => _transport.IsRunning ? _transport.LocalPeerId : NetPeer.HostPeerId;

        public void Start()
        {
            Add(LocalId, _steam != null ? _steam.PlayerName : "Игрок");

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

        public void AssignSides(int sides)
        {
            int wanted = sides < 1 ? 1 : sides;
            if (wanted == _sides) return;

            _sides = wanted;
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
            var bytes = new NetByteReader(payload);
            string name = bytes.ReadString();
            if (string.IsNullOrWhiteSpace(name)) return;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Id != from) continue;

                SessionPlayer was = _players[i];
                _players[i] = new SessionPlayer(was.Id, name, was.Team, was.ColorIndex);
                Announce();
                return;
            }
        }

        private void Add(int peerId, string name)
        {
            if (TryGet(peerId, out _)) return;

            _players.Add(new SessionPlayer(peerId, name, TeamFor(_players.Count), _players.Count));
            Announce();
        }

        /// <summary>Пересадить всех по текущему порядку и числу сторон.</summary>
        private void Reseat()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                SessionPlayer was = _players[i];
                _players[i] = new SessionPlayer(was.Id, was.Name, TeamFor(i), i);
            }

            Announce();
        }

        /// <summary>
        /// Какая сторона достаётся месту <paramref name="seat"/>. При одной стороне у хозяина остаётся
        /// команда из конфига: это дев-ручка «за кого я играю», и кооп не повод её отменять.
        /// </summary>
        private int TeamFor(int seat)
        {
            if (_sides <= 1) return seat == 0 && _config != null ? _config.LocalPlayerTeam : 0;
            return seat % _sides;
        }

        private void Announce()
        {
            if (!_transport.IsRunning) return; // соло: объявлять некому

            _writer.Reset();
            _writer.WriteByte((byte)_players.Count);

            for (int i = 0; i < _players.Count; i++)
            {
                SessionPlayer player = _players[i];
                _writer.WriteByte((byte)player.Id);
                _writer.WriteByte((byte)player.Team);
                _writer.WriteByte((byte)player.ColorIndex);
                _writer.WriteString(player.Name);
            }

            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.SessionRoster, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }
    }
}
