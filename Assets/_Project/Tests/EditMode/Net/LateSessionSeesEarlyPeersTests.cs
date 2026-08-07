using Guildmaster.Game.Session.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Сеанс, родившийся ПОЗЖЕ подключения напарника, всё равно его видит.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт между транспортом и сеансом, поэтому он в тесте.</b> Транспорт поднимается
    /// на входе в игру, а состав сеанса и гейт готовности рождаются вместе с сеансом — и пересоздаются
    /// на каждой смене режима (меню → кампания → площадка). Пока оба набирали участников ТОЛЬКО по
    /// событиям подключения, гость, пришедший в этот промежуток, не появлялся в списке никогда: хозяин
    /// не видел ни строки в панели участников, ни планки «(N/M)», и узнавал о напарнике лишь тогда,
    /// когда тот отключался (наход. Макса на живом прогоне 07.08.2026).
    /// <para>Комментарием такое не удержать: нарушить его можно из третьего файла — достаточно завести
    /// ещё одного слушателя состава и снова забыть спросить «а кто уже здесь».</para>
    /// </remarks>
    public sealed class LateSessionSeesEarlyPeersTests
    {
        [Test]
        public void Roster_CountsThePeerThatArrivedBeforeIt()
        {
            var net = new LoopbackNetwork();
            INetTransport host = net.CreateNode();

            // Гость приходит ДО того, как открылся сеанс: ровно так и бывает, когда хозяин ещё
            // выбирает дом в меню или переходит из кампании на площадку.
            net.CreateNode();
            net.PollAll();

            var roster = new HostSessionRoster(host, null, null, null);
            roster.Start();

            Assert.AreEqual(2, roster.Players.Count,
                "состав сеанса не увидел уже подключённого напарника — он не появится там никогда");
        }

        [Test]
        public void ReadyGate_RaisesTheBarForThePeerThatArrivedBeforeIt()
        {
            var net = new LoopbackNetwork();
            INetTransport host = net.CreateNode();

            net.CreateNode();
            net.PollAll();

            var gate = new HostSharedDecision(host, null);
            gate.Start();

            Assert.AreEqual(2, gate.Required,
                "планка согласия считает одного там, где играют двое: действие сработает, пока " +
                "напарник ещё смотрит на поле");
        }

        [Test]
        public void Transport_AnswersWhoIsAlreadyConnected()
        {
            var net = new LoopbackNetwork();
            INetTransport host = net.CreateNode();

            Assert.IsEmpty(host.ConnectedPeers, "в пустой сети подключённых нет");

            INetTransport guest = net.CreateNode();
            net.PollAll();

            Assert.AreEqual(1, host.ConnectedPeers.Count, "хозяин видит гостя");
            Assert.AreEqual(1, guest.ConnectedPeers.Count, "гость видит хозяина");
            Assert.AreEqual(NetPeer.HostPeerId, guest.ConnectedPeers[0]);

            guest.Shutdown();
            net.PollAll();

            Assert.IsEmpty(host.ConnectedPeers, "ушедший из состава пропал");
            Assert.IsEmpty(guest.ConnectedPeers, "закрытый узел не знает никого — как закрытый сокет");
        }
    }
}
