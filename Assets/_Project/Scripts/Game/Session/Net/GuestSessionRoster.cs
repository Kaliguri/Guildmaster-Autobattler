using System;
using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина состава сеанса: представляется именем и показывает таблицу, присланную хостом.
    /// </summary>
    /// <remarks>
    /// <b>Гость состав не ведёт.</b> Кто в сессии и кто за какую сторону играет, знает хост — здесь
    /// только последняя услышанная таблица. Своё мнение о составе означало бы, что в PvP каждый клиент
    /// решает сам, кто ему противник.
    /// <para><b>Имя отправляется по подключению, а не в конструкторе:</b> в момент рождения объекта
    /// соединения может ещё не быть, и сообщение ушло бы в никуда — ровно тот класс ошибок, где «работает
    /// со второго раза».</para>
    /// </remarks>
    public sealed class GuestSessionRoster : ISessionRoster, IStartable, IDisposable
    {
        private readonly INetTransport                          _transport;
        private readonly Guildmaster.Net.Session.SteamBootstrap _steam;

        private readonly List<SessionPlayer> _players  = new List<SessionPlayer>(4);
        private readonly List<SessionPlayer> _incoming = new List<SessionPlayer>(4);
        private readonly NetByteWriter       _writer   = new NetByteWriter(64);
        private byte[] _envelope;

        public GuestSessionRoster(INetTransport transport, Guildmaster.Net.Session.SteamBootstrap steam)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _steam     = steam;
        }

        public IReadOnlyList<SessionPlayer> Players => _players;

        public int LocalId => _transport.LocalPeerId;

        public void Start()
        {
            _transport.MessageReceived += OnMessage;
            _transport.PeerConnected   += OnPeerConnected;

            if (_transport.IsRunning) SayName();
        }

        public void Dispose()
        {
            _transport.MessageReceived -= OnMessage;
            _transport.PeerConnected   -= OnPeerConnected;
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

        /// <summary>Раздача сторон — работа хоста. У гостя вызов законен и не делает ничего.</summary>
        public void AssignSides(int sides) { }

        private void OnPeerConnected(int peerId)
        {
            if (peerId == NetPeer.HostPeerId) SayName();
        }

        private void SayName()
        {
            _writer.Reset();
            _writer.WriteString(_steam != null ? _steam.PlayerName : "Игрок");

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.SessionRoster, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.SessionRoster) return;

            // Состав объявляет только хост: таблица от другого гостя показала бы нам чужую выдумку.
            if (from != NetPeer.HostPeerId) return;

            var bytes = new NetByteReader(payload);

            // Разбираем в сторону и подменяем только целиком. Битая таблица — расхождение версий, и
            // половина состава хуже прежней целой: по половине мы решили бы, что кто-то вышел, и
            // перестали бы показывать его курсор.
            _incoming.Clear();

            try
            {
                int count = bytes.ReadByte();

                for (int i = 0; i < count; i++)
                {
                    int id    = bytes.ReadByte();
                    int team  = bytes.ReadByte();
                    int color = bytes.ReadByte();
                    string name = bytes.ReadString();

                    _incoming.Add(new SessionPlayer(id, name, team, color));
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            _players.Clear();
            _players.AddRange(_incoming);
        }
    }
}
