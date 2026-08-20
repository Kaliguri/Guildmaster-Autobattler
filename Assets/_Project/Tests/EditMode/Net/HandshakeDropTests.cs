using Guildmaster.Data;
using Guildmaster.Net.Session;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Гость исчезает ровно посреди рукопожатия — самый частый способ уронить хост в P2P.
    /// </summary>
    /// <remarks>
    /// Проверяем узкое место сознательно: между «привет» и «добро пожаловать» хост держит обещание
    /// ответить конкретному пиру, а пира уже нет. В чужих проектах на этом падает именно хост — то
    /// есть человек, который ни при чём (замечание из разбора 04.08.2026).
    /// <para>Ожидание простое и потому проверяемое: <b>хост остаётся живым и не считает такого гостя
    /// принятым</b>. Ошибку в лог писать не за что — это не сбой, а ушедший человек.</para>
    /// </remarks>
    public sealed class HandshakeDropTests
    {
        private static ContentFingerprint Same =>
            new ContentFingerprint(contentHash: 777, contentCount: 3, schemaVersion: 1, gameVersion: "1.0");

        [Test]
        public void GuestDiesBetweenHelloAndWelcome_HostSurvives()
        {
            var net   = new LoopbackNetwork();
            INetTransport host  = net.CreateNode();
            INetTransport guest = net.CreateNode();

            var hostShake  = new CoopHandshake(host, Same);
            var guestShake = new CoopHandshake(guest, Same);

            int approved = 0;
            hostShake.GuestApproved += _ => approved++;

            int left = 0;
            host.PeerDisconnected += _ => left++;

            guestShake.SayHello();
            guest.Shutdown();          // гость исчез, «привет» уже в пути

            Assert.DoesNotThrow(() => net.PollAll(),
                "разбор чужого «привет» не имеет права уронить хост: отвечать некому — и только");

            // Принять и тут же потерять — нормальный порядок: «привет» пришёл раньше обрыва. Важно
            // другое — что обрыв ДОШЁЛ. Не дойди он, гость остался бы в составе призраком: ему слали бы
            // курсоры, а гейт готовности ждал бы его согласия вечно, и бой не начался бы уже никогда.
            Assert.AreEqual(1, approved, "«привет» успел прийти — хост честно ответил");
            Assert.AreEqual(1, left, "и уход дошёл следом: состав почистится, призрака не останется");
            Assert.IsTrue(host.IsRunning, "хост жив");
        }

        [Test]
        public void GuestDiesRightAfterWelcome_HostKeepsRunning()
        {
            var net   = new LoopbackNetwork();
            INetTransport host  = net.CreateNode();
            INetTransport guest = net.CreateNode();

            var hostShake  = new CoopHandshake(host, Same);
            var guestShake = new CoopHandshake(guest, Same);

            int approved = 0;
            hostShake.GuestApproved += _ => approved++;

            guestShake.SayHello();
            net.PollAll();                 // хост принял и ответил
            Assert.AreEqual(1, approved, "рукопожатие состоялось");

            guest.Shutdown();              // и тут же оборвался

            Assert.DoesNotThrow(() => net.PollAll(), "хост живёт дальше — он остался один, а не сломался");
            Assert.IsTrue(host.IsRunning);
        }
    }
}
