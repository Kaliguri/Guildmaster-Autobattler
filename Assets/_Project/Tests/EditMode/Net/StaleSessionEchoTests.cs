using System;
using System.Collections.Generic;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Эхо мёртвой сессии не должно разбирать живую.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт между транспортом, сеансом и составом, поэтому он в тесте.</b> Steam
    /// досказывает за закрытым соединением: <c>OnDisconnected</c> приходит не в момент закрытия, а на
    /// ближайшем <c>RunCallbacks</c> — уже после того, как на месте старого сокета подняли новый.
    /// <para>Стоило это двух симптомов подряд на прогоне вдвоём 08.08.2026. Игрок побывал гостем, потом
    /// поднял свой хостинг — и получил «пир 0 отключился» от прошлого соединения. Номер один, смысл
    /// разный: у гостя ноль означал чужого хозяина, у хозяина ноль означает его самого. Состав честно
    /// вычеркнул хозяина, и сеанс остался без него — «нас нет в составе сеанса (наш номер 0,
    /// участников 1)», а за ним посыпались стороны, панель участников и права на кнопки.</para>
    /// <para>Само поколение сессии проверяется только со Steam на связи, поэтому здесь стоит второй
    /// рубеж — тот, что верен независимо от транспорта: <b>от себя отключиться нельзя</b>.</para>
    /// </remarks>
    public sealed class StaleSessionEchoTests
    {
        [Test]
        public void HostRoster_SurvivesTheEchoOfItsOwnPeerNumber()
        {
            var net = new LoopbackNetwork();
            var echo = new EchoingTransport(net.CreateNode());

            var roster = new HostSessionRoster(echo, null, null, null);
            roster.Start();

            Assert.AreEqual(1, roster.Players.Count, "хозяин обязан быть в собственном составе");

            // Хвост прошлой сессии: там ноль означал ХОЗЯИНА, здесь тот же ноль означает нас самих.
            echo.EchoDisconnect(NetPeer.HostPeerId);

            Assert.IsTrue(roster.TryGet(roster.LocalId, out _),
                "хозяин вычеркнул себя из состава по эху мёртвой сессии — дальше у сеанса нет ни " +
                "сторон, ни участников, ни прав");
        }

        [Test]
        public void HostRoster_StillDropsTheGuestThatActuallyLeft()
        {
            var net = new LoopbackNetwork();
            var echo = new EchoingTransport(net.CreateNode());

            net.CreateNode();
            net.PollAll();

            var roster = new HostSessionRoster(echo, null, null, null);
            roster.Start();

            Assert.AreEqual(2, roster.Players.Count, "гость виден в составе");

            echo.EchoDisconnect(1);

            Assert.AreEqual(1, roster.Players.Count,
                "защита себя не должна прикрывать чужой уход — иначе ушедший останется в списке навсегда");
        }

        /// <summary>
        /// Транспорт, которому можно сказать «а теперь скажи, что пир отключился» — ровно так за
        /// закрытым сокетом досказывает Steam.
        /// </summary>
        private sealed class EchoingTransport : INetTransport
        {
            private readonly INetTransport _inner;

            public EchoingTransport(INetTransport inner) => _inner = inner;

            public void EchoDisconnect(int peerId) => PeerDisconnected?.Invoke(peerId);

            public bool StartHost()                => _inner.StartHost();
            public bool Connect(ulong hostAddress) => _inner.Connect(hostAddress);
            public void SetLocalPeerId(int peerId) => _inner.SetLocalPeerId(peerId);

            public bool IsRunning               => _inner.IsRunning;
            public IReadOnlyList<int> ConnectedPeers => _inner.ConnectedPeers;
            public int  LocalPeerId             => _inner.LocalPeerId;
            public bool IsHost                  => _inner.IsHost;
            public int  MaxReliableMessageBytes => _inner.MaxReliableMessageBytes;

            // Подключение и сообщения идут насквозь: обёртка умеет ровно одно — досказать за мёртвым.
            public event Action<int> PeerConnected
            {
                add    => _inner.PeerConnected += value;
                remove => _inner.PeerConnected -= value;
            }

            public event Action<int> PeerDisconnected;

            public event Action<int, ArraySegment<byte>> MessageReceived
            {
                add    => _inner.MessageReceived += value;
                remove => _inner.MessageReceived -= value;
            }

            public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery) =>
                _inner.Send(peerId, payload, delivery);

            public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery) =>
                _inner.SendToAll(payload, delivery);

            public void Poll()     => _inner.Poll();
            public void Shutdown() => _inner.Shutdown();
        }
    }
}
