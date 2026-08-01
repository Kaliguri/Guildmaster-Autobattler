using System;
using System.Collections.Generic;
using System.Text;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Шов транспорта (ТЗ кооп-вертикали §4.2): loopback даёт двух и более пиров в одном процессе, а
    /// chaos-обёртка портит канал ПО СИДУ — задержка, разброс (он же переупорядочивание), потеря
    /// ненадёжного, дубли.
    /// <para>Главное, что здесь проверяется, — не «байты доехали», а <b>воспроизводимость</b>: один сид
    /// даёт одну и ту же историю канала. На этом стоит вся отладка коопа, иначе баг ловится ожиданием,
    /// пока сеть «поведёт себя так же».</para>
    /// </summary>
    public sealed class TransportSeamTests
    {
        [Test]
        public void Loopback_DeliversOnPoll_NotAtSendTime()
        {
            var net  = new LoopbackNetwork();
            INetTransport host  = net.CreateNode();
            INetTransport guest = net.CreateNode();

            var got = new List<string>();
            guest.MessageReceived += (from, payload) => got.Add(Text(payload));

            host.SendToAll(Bytes("привет"), NetDelivery.Reliable);
            Assert.AreEqual(0, got.Count,
                "До Poll ничего не пришло: доставка «когда придёт» сделала бы тест недетерминированным");

            guest.Poll();
            Assert.AreEqual(new[] { "привет" }, got);
        }

        [Test]
        public void Loopback_AnnouncesPeersToBothSides()
        {
            var net = new LoopbackNetwork();
            INetTransport host = net.CreateNode();

            var hostSaw = new List<int>();
            host.PeerConnected += id => hostSaw.Add(id);

            INetTransport guest = net.CreateNode();
            var guestSaw = new List<int>();
            guest.PeerConnected += id => guestSaw.Add(id);

            net.PollAll();

            Assert.AreEqual(new[] { 1 }, hostSaw, "Хост узнал о госте");
            Assert.AreEqual(new[] { NetPeer.HostPeerId }, guestSaw,
                "И гость о хосте — иначе «кто в сессии» зависело бы от порядка входа");
            Assert.IsTrue(host.IsHost, "Первый узел — хост");
            Assert.IsFalse(guest.IsHost);
        }

        [Test]
        public void Loopback_TellsEveryoneWhoLeft()
        {
            var net = new LoopbackNetwork();
            INetTransport host  = net.CreateNode();
            INetTransport guest = net.CreateNode();
            net.PollAll();

            var lost = new List<int>();
            host.PeerDisconnected += id => lost.Add(id);

            guest.Shutdown();
            net.PollAll();

            Assert.AreEqual(new[] { 1 }, lost, "Уход гостя виден хосту");
            Assert.IsFalse(guest.IsRunning);
        }

        // Предел проверяет НАШ код: на релизе Steam проглотил бы такое сообщение молча, и искать причину
        // пришлось бы по отсутствию картинки у гостя.
        [Test]
        public void Loopback_RefusesAnOversizedReliableMessage_Loudly()
        {
            var net = new LoopbackNetwork();
            INetTransport host = net.CreateNode();
            net.CreateNode();

            var tooBig = new ArraySegment<byte>(new byte[host.MaxReliableMessageBytes + 1]);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => host.SendToAll(tooBig, NetDelivery.Reliable),
                "Сообщение сверх предела — громкий отказ, а не тишина");
        }

        // Копия при отправке: у отправителя буфер переиспользуемый, и без копии получатель прочитал бы
        // уже перезаписанные байты — баг, который выглядит как порча данных в сети.
        [Test]
        public void Loopback_CopiesThePayload_SoAReusedBufferCannotCorruptIt()
        {
            var net = new LoopbackNetwork();
            INetTransport host  = net.CreateNode();
            INetTransport guest = net.CreateNode();

            string received = null;
            guest.MessageReceived += (from, payload) => received = Text(payload);

            byte[] scratch = Encoding.UTF8.GetBytes("первое");
            host.SendToAll(new ArraySegment<byte>(scratch), NetDelivery.Reliable);

            for (int i = 0; i < scratch.Length; i++) scratch[i] = (byte)'X';   // отправитель переиспользовал буфер
            guest.Poll();

            Assert.AreEqual("первое", received, "Получатель видит то, что отправляли, а не то, что стало с буфером");
        }

        // ═══ Chaos: главное обещание — воспроизводимость ═══

        [Test]
        public void Chaos_SameSeed_GivesTheSameHistory()
        {
            string first  = RunChaos(seed: 12345UL);
            string second = RunChaos(seed: 12345UL);
            string other  = RunChaos(seed: 999UL);

            Assert.AreEqual(first, second,
                "Один сид — одна история канала: только так падение воспроизводится вызовом теста");
            Assert.AreNotEqual(first, other, "Другой сид — другая история, иначе сид ничего не значит");
        }

        [Test]
        public void Chaos_DelaysDelivery_ByWholePollSteps()
        {
            var net = new LoopbackNetwork();
            INetTransport hostInner = net.CreateNode();
            INetTransport guest     = net.CreateNode();

            var profile = new ChaosProfile { MinDelaySteps = 2, MaxDelaySteps = 2 };
            var host = new ChaosTransport(hostInner, profile, seed: 1UL);

            int got = 0;
            guest.MessageReceived += (from, payload) => got++;

            host.SendToAll(Bytes("раз"), NetDelivery.Reliable);

            host.Poll(); guest.Poll();
            Assert.AreEqual(0, got, "Первый шаг: сообщение ещё в пути");
            Assert.AreEqual(1, host.InFlight);

            host.Poll(); guest.Poll();
            Assert.AreEqual(1, got, "Через две задержки — дошло");
            Assert.AreEqual(0, host.InFlight);
        }

        [Test]
        public void Chaos_NeverDropsReliable_ButMayDropUnreliable()
        {
            var profile = new ChaosProfile
            {
                MinDelaySteps        = 0,
                MaxDelaySteps        = 0,
                UnreliableLossChance = 1f,     // теряем ВСЁ ненадёжное
            };

            var net = new LoopbackNetwork();
            INetTransport hostInner = net.CreateNode();
            INetTransport guest     = net.CreateNode();
            var host = new ChaosTransport(hostInner, profile, seed: 7UL);

            int got = 0;
            guest.MessageReceived += (from, payload) => got++;

            host.SendToAll(Bytes("курсор"), NetDelivery.Unreliable);
            host.Poll(); guest.Poll();
            Assert.AreEqual(0, got, "Ненадёжное вправе потеряться — на этом стоит присутствие");

            host.SendToAll(Bytes("команда"), NetDelivery.Reliable);
            host.Poll(); guest.Poll();
            Assert.AreEqual(1, got,
                "Надёжное не теряется никогда: доставку обеспечивает транспорт, и наш код на неё рассчитывает");
        }

        [Test]
        public void Chaos_CanDuplicate_SoIdempotencyHasSomethingToCatch()
        {
            var profile = new ChaosProfile
            {
                MinDelaySteps   = 1,
                MaxDelaySteps   = 4,
                DuplicateChance = 1f,          // дублируем ВСЁ
            };

            var net = new LoopbackNetwork();
            INetTransport hostInner = net.CreateNode();
            INetTransport guest     = net.CreateNode();
            var host = new ChaosTransport(hostInner, profile, seed: 3UL);

            int got = 0;
            guest.MessageReceived += (from, payload) => got++;

            host.SendToAll(Bytes("команда"), NetDelivery.Reliable);
            for (int step = 0; step < 8; step++) { host.Poll(); guest.Poll(); }

            Assert.AreEqual(2, got,
                "Копия приходит отдельной посылкой со своей задержкой — именно такой дубль и даёт реконнект");
        }

        // Прогон с шумом: возвращает историю приёма строкой, чтобы её можно было сравнить целиком.
        private static string RunChaos(ulong seed)
        {
            var net = new LoopbackNetwork();
            INetTransport hostInner = net.CreateNode();
            INetTransport guest     = net.CreateNode();
            var host = new ChaosTransport(hostInner, ChaosProfile.Typical, seed);

            var log = new StringBuilder();
            guest.MessageReceived += (from, payload) => log.Append(Text(payload)).Append(';');

            for (int i = 0; i < 30; i++)
            {
                host.SendToAll(Bytes(i.ToString()), i % 3 == 0 ? NetDelivery.Unreliable : NetDelivery.Reliable);
                host.Poll();
                guest.Poll();
            }
            for (int i = 0; i < 10; i++) { host.Poll(); guest.Poll(); }   // дать хвосту дойти

            return log.ToString();
        }

        private static ArraySegment<byte> Bytes(string text) =>
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));

        private static string Text(ArraySegment<byte> payload) =>
            Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count);
    }
}
