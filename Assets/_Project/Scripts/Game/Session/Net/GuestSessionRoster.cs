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
    public sealed class GuestSessionRoster : ISessionRoster, IStartable, ITickable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly Guildmaster.Core.Players.IPlatformIdentity _platform;
        private readonly Guildmaster.Core.Persistence.IProfileService _profiles;
        // Где мы сейчас. Спрашивается каждый кадр, отправляется только на смену: место меняется редко,
        // а таблица состава идёт надёжным каналом и стоит дороже пакета присутствия.
        private readonly ILocalWhereabouts _where;

        private PlayerWhere _lastSaid = PlayerWhere.Unknown;

        private readonly List<SessionPlayer> _players  = new List<SessionPlayer>(4);
        private readonly List<SessionPlayer> _incoming = new List<SessionPlayer>(4);
        private readonly NetByteWriter       _writer   = new NetByteWriter(64);
        private byte[] _envelope;

        public GuestSessionRoster(INetTransport transport,
                                  Guildmaster.Core.Players.IPlatformIdentity platform,
                                  Guildmaster.Core.Persistence.IProfileService profiles,
                                  ILocalWhereabouts where = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _platform  = platform;
            _profiles  = profiles;
            _where     = where;
        }

        /// <summary>Сменили место — сказать об этом. Молчание оставило бы нас там, где нас уже нет.</summary>
        public void Tick()
        {
            if (!_transport.IsRunning) return;

            PlayerWhere now = _where?.Current ?? PlayerWhere.Unknown;
            if (now == _lastSaid) return;

            _lastSaid = now;
            SayName();
        }

        public IReadOnlyList<SessionPlayer> Players => _players;

        public int LocalId => _transport.LocalPeerId;

        public void Start()
        {
            _transport.MessageReceived += OnMessage;
            _transport.PeerConnected   += OnPeerConnected;

            if (!_transport.IsRunning) return;

            _lastSaid = _where?.Current ?? PlayerWhere.Unknown;
            SayName();
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
        public void SplitBetweenSides(bool split) { }

        private void OnPeerConnected(int peerId)
        {
            if (peerId == NetPeer.HostPeerId) SayName();
        }

        private void SayName()
        {
            // Ник из профиля: игрок мог выбрать свой вместо Steam-имени, и хост обязан увидеть тот же.
            // Цвет едет ПОЖЕЛАНИЕМ: занять его может кто-то раньше нас, и решает это хост — иначе двое
            // пришли бы одним цветом, а весь смысл мейн-цвета в том, что «чей это» читается мгновенно.
            Guildmaster.Core.Persistence.ProfileIdentity identity = _profiles?.Identity ?? default;
            var intro = new SessionIntro(
                identity.ResolveName(_platform != null ? _platform.PlayerName : "Игрок"),
                identity.ColorIndex,
                identity.CursorSkinId,
                _lastSaid);

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.SessionRoster,
                                 SessionRosterCodec.WriteIntro(intro, _writer), ref _envelope),
                NetDelivery.Reliable);
        }

        private void OnMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.SessionRoster) return;

            // Состав объявляет только хост: таблица от другого гостя показала бы нам чужую выдумку.
            if (from != NetPeer.HostPeerId) return;

            // Разбираем в сторону и подменяем только целиком — это делает кодек. Половина состава хуже
            // прежней целой: по половине мы решили бы, что кто-то вышел, и перестали бы показывать его
            // курсор.
            if (!SessionRosterCodec.TryReadTable(payload, _incoming))
            {
                // Версия и отпечаток контента сверены рукопожатием, поэтому нечитаемая таблица — это
                // НАША поломка формата, а не «чужая сборка». Ровно на этом молчании состав однажды
                // перестал доезжать вовсе.
                Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Session,
                    "гость: таблица состава не разобралась — формат канала состава разъехался");
                return;
            }

            _players.Clear();
            _players.AddRange(_incoming);
        }
    }
}
