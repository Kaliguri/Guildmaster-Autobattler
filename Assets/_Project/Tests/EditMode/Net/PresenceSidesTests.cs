using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Core.Players;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using Guildmaster.Net.Presence;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Курсор виден только своей стороне, и решается это НА ОТПРАВКЕ.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между составом сеанса и присутствием, и проверяется он именно доставкой: тест
    /// смотрит, что противнику пакет не уехал, а не что клиент его не нарисовал. Проверка отрисовки
    /// прошла бы и на реализации, которая честно рассылает всё подряд, — то есть на дырявой.
    /// <para>Второй здешний инвариант — «сторона у каждого своя»: пока «моя команда» была нулём, оба
    /// клиента в PvP считали своей одну и ту же сторону.</para>
    /// </remarks>
    public sealed class PresenceSidesTests
    {
        [Test]
        public void OpponentCursor_NeverLeavesTheHost()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            INetTransport foeNode  = net.CreateNode(); // место 1 → сторона 1
            INetTransport allyNode = net.CreateNode(); // место 2 → сторона 0, как у хоста
            net.PollAll();

            roster.SplitBetweenSides(true); // PvP: стороны чередуются по местам

            Assert.AreEqual(1, TeamOf(roster, 1), "второй вошедший играет за другую сторону");
            Assert.AreEqual(0, TeamOf(roster, 2), "третий возвращается на сторону хоста");

            var presence = new HostPresence(hostNode, new FixedPointer(new Vector2(3f, 4f)), roster);
            presence.Start();

            int toFoe = 0, toAlly = 0;
            foeNode.MessageReceived  += (from, message) => { if (IsPresence(message)) toFoe++; };
            allyNode.MessageReceived += (from, message) => { if (IsPresence(message)) toAlly++; };

            presence.Tick();  // хост двинул своим курсором и раздал присутствие
            net.PollAll();

            Assert.AreEqual(0, toFoe, "курсор хоста не должен доезжать до противника ВООБЩЕ");
            Assert.AreEqual(1, toAlly, "союзник курсор хоста видит");
        }

        [Test]
        public void OneSide_EveryoneSeesEveryone()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            INetTransport first  = net.CreateNode();
            INetTransport second = net.CreateNode();
            net.PollAll();

            roster.SplitBetweenSides(false); // кампания: сторона одна на всех

            var presence = new HostPresence(hostNode, new FixedPointer(new Vector2(1f, 1f)), roster);
            presence.Start();

            int toFirst = 0, toSecond = 0;
            first.MessageReceived  += (from, message) => { if (IsPresence(message)) toFirst++; };
            second.MessageReceived += (from, message) => { if (IsPresence(message)) toSecond++; };

            presence.Tick();
            net.PollAll();

            Assert.AreEqual(1, toFirst,  "в кампании все свои");
            Assert.AreEqual(1, toSecond, "и второму тоже");
        }

        [Test]
        public void GuestCursor_IsForwardedToHisSideOnly()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            INetTransport speaker = net.CreateNode(); // место 1 → сторона 1
            INetTransport foe     = net.CreateNode(); // место 2 → сторона 0
            INetTransport mate    = net.CreateNode(); // место 3 → сторона 1, вместе с говорящим
            net.PollAll();

            roster.SplitBetweenSides(true);

            var presence = new HostPresence(hostNode, new FixedPointer(Vector2.zero, available: false), roster);
            presence.Start();

            // Гость шлёт своё присутствие ровно так, как это делает GuestPresence.
            speaker.Send(NetPeer.HostPeerId, Packet(new PresenceState(1, 1, new Vector2(2f, 2f), Vector2.zero)),
                         NetDelivery.Unreliable);
            net.PollAll();

            int toFoe = 0, toMate = 0;
            foe.MessageReceived  += (from, message) => { if (IsPresence(message)) toFoe++; };
            mate.MessageReceived += (from, message) => { if (IsPresence(message)) toMate++; };

            presence.Tick();
            net.PollAll();

            Assert.AreEqual(0, toFoe,  "чужой курсор не пересылается противнику");
            Assert.AreEqual(1, toMate, "союзнику говорящего — пересылается");
        }

        [Test]
        public void LeavingPlayer_LosesHisSeatAndColor()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            net.CreateNode();
            INetTransport leaving = net.CreateNode();
            net.PollAll();

            Assert.AreEqual(3, roster.Players.Count);

            leaving.Shutdown();
            net.PollAll();

            Assert.AreEqual(2, roster.Players.Count, "вышедший перестаёт быть участником");
            Assert.IsFalse(roster.TryGet(2, out _), "и его место освобождается");
        }

        [Test]
        public void CursorOfPlayerWhoChangedSides_IsForgotten()
        {
            var cursors = new PresenceCursors();
            var roster  = new StubRoster(localId: 0, localTeam: 0);
            roster.Set(1, team: 0);

            cursors.Push(new PresenceState(1, 1, new Vector2(5f, 5f), Vector2.zero), now: 0f);
            cursors.Sample(0.05f, roster, localPlayerId: 0);
            Assert.AreEqual(1, cursors.Count, "союзника видно");

            roster.Set(1, team: 1); // матч сменился, вчерашний союзник теперь напротив

            cursors.Sample(0.1f, roster, localPlayerId: 0);
            Assert.AreEqual(0, cursors.Count,
                "курсор сменившего сторону обязан исчезнуть, иначе он застынет на арене навсегда");
        }

        [Test]
        public void OwnCursor_IsNotDrawnByTheNetwork()
        {
            var cursors = new PresenceCursors();
            var roster  = new StubRoster(localId: 0, localTeam: 0);
            roster.Set(0, team: 0);

            cursors.Push(new PresenceState(0, 1, new Vector2(1f, 1f), Vector2.zero), now: 0f);
            cursors.Sample(0.05f, roster, localPlayerId: 0);

            Assert.AreEqual(0, cursors.Count, "свой курсор рисует система, а не эхо из сети");
        }

        // ── вспомогательное ──────────────────────────────────────────────────

        private static int TeamOf(ISessionRoster roster, int playerId) =>
            roster.TryGet(playerId, out SessionPlayer player) ? player.Team : -1;

        private static bool IsPresence(ArraySegment<byte> message) =>
            NetEnvelope.TryUnwrap(message, out NetChannel channel, out _) && channel == NetChannel.Presence;

        private static ArraySegment<byte> Packet(in PresenceState state)
        {
            var writer = new NetByteWriter(64);
            PresenceCodec.Write(writer, new List<PresenceState> { state });

            byte[] envelope = null;
            return NetEnvelope.Wrap(NetChannel.Presence, writer.WrittenSegment, ref envelope);
        }

        /// <summary>Указатель, который всегда в одной точке: тесту важна доставка, а не движение мыши.</summary>
        private sealed class FixedPointer : IPointerWorld
        {
            private readonly Vector2 _position;

            public FixedPointer(Vector2 position, bool available = true)
            {
                _position   = position;
                IsAvailable = available;
            }

            public Vector2 Position   => _position;
            public bool    IsAvailable { get; }
        }

        /// <summary>Состав, который правится прямо в тесте.</summary>
        private sealed class StubRoster : ISessionRoster
        {
            private readonly List<SessionPlayer> _players = new List<SessionPlayer>();

            public StubRoster(int localId, int localTeam)
            {
                LocalId = localId;
                Set(localId, localTeam);
            }

            public IReadOnlyList<SessionPlayer> Players => _players;

            public int LocalId { get; }

            public void Set(int playerId, int team)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i].Id != playerId) continue;
                    _players[i] = new SessionPlayer(playerId, $"p{playerId}", team, playerId);
                    return;
                }

                _players.Add(new SessionPlayer(playerId, $"p{playerId}", team, playerId));
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

            public void SplitBetweenSides(bool split) { }
        }
    }
}
