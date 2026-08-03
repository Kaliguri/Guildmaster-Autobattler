using System.Collections.Generic;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Общая пауза боя (ТЗ кооп-вертикали, фаза Б): интент чей угодно, решение хостовое, состояние одно
    /// на всех, и видно, кто нажал.
    /// <para>Проверяется здесь именно <b>единственность состояния</b>: пауза, применённая у одного и не
    /// доехавшая до другого, — это два разных боя на двух экранах, а дизайн коопа обещает один
    /// («пауза общая и видно, кто её нажал»).</para>
    /// </summary>
    public sealed class BattleControlRelayTests
    {
        [Test]
        public void HostPauses_GuestFollows_AndSeesWho()
        {
            var net   = new LoopbackNetwork();
            var host  = new BattleControlRelay(net.CreateNode());
            INetTransport guestNode = net.CreateNode();
            var guest = new BattleControlRelay(guestNode);

            var seen = new List<(bool paused, int by)>();
            guest.PauseChanged += (paused, by) => seen.Add((paused, by));

            host.RequestPause(true);
            net.PollAll();

            Assert.IsTrue(guest.IsPaused, "Пауза доехала");
            Assert.AreEqual(NetPeer.HostPeerId, guest.PausedBy, "И вместе с ней автор");
            Assert.AreEqual(1, seen.Count, "Событие поднялось ровно раз");

            host.RequestPause(false);
            net.PollAll();

            Assert.IsFalse(guest.IsPaused);
            Assert.AreEqual(NetPeer.NoPeer, guest.PausedBy, "Снятая пауза автора не помнит");
        }

        // Отклик оптимистичный (решение 6 ТЗ): нажавший гость видит паузу немедленно, не дожидаясь
        // полного RTT. Иначе задержка кнопки равна дороге туда и обратно, а дизайн к «не успел» готов.
        [Test]
        public void GuestPauses_Locally_BeforeHostConfirms()
        {
            var net   = new LoopbackNetwork();
            var host  = new BattleControlRelay(net.CreateNode());
            var guest = new BattleControlRelay(net.CreateNode());

            guest.RequestPause(true);

            Assert.IsTrue(guest.IsPaused, "У себя — сразу");
            Assert.IsFalse(host.IsPaused, "У хоста — ещё нет: сообщение в дороге");

            net.PollAll();
            Assert.IsTrue(host.IsPaused, "Хост принял интент");

            net.PollAll();
            Assert.IsTrue(guest.IsPaused, "И подтвердил его обратно");
            Assert.AreEqual(1, guest.PausedBy, "Автор — гость, а не хост");
        }

        // Автором записывается ОТПРАВИТЕЛЬ, а не то, что он написал в пакете: «видно, кто нажал» —
        // дизайн-требование, и подмена автора не должна стоить одного байта.
        [Test]
        public void AuthorComesFromTheSender_NotFromThePayload()
        {
            var net       = new LoopbackNetwork();
            var hostNode  = net.CreateNode();
            var guestNode = net.CreateNode();

            var host = new BattleControlRelay(hostNode);
            _ = new BattleControlRelay(guestNode);

            // Гость представляется хостом: id 0 в поле автора.
            byte[] forged = { 0 /* intent */, 1 /* paused */, 0, 0, 0, 0 };
            byte[] envelope = null;
            guestNode.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.BattleControl, new System.ArraySegment<byte>(forged), ref envelope),
                NetDelivery.Reliable);
            net.PollAll();

            Assert.IsTrue(host.IsPaused, "Пауза принята — просить её вправе любой");
            Assert.AreEqual(1, host.PausedBy, "Но автор — тот, кто прислал");
        }

        // Состояние объявляет только хост. Иначе двое гостей, нажавших одновременно, начали бы
        // перезаписывать друг друга, и «кто нажал» зависело бы от порядка доставки.
        [Test]
        public void GuestState_IsIgnoredByOtherGuests()
        {
            var net    = new LoopbackNetwork();
            _ = net.CreateNode();                       // хост, молчит
            INetTransport guestA = net.CreateNode();
            INetTransport guestB = net.CreateNode();

            var b = new BattleControlRelay(guestB);

            byte[] state = { 1 /* state */, 1 /* paused */, 2, 0, 0, 0 };
            byte[] envelope = null;
            guestA.SendToAll(
                NetEnvelope.Wrap(NetChannel.BattleControl, new System.ArraySegment<byte>(state), ref envelope),
                NetDelivery.Reliable);
            net.PollAll();

            Assert.IsFalse(b.IsPaused, "Состояние не от хоста игнорируется");
        }

        // Повтор того же состояния не поднимает событие: подписчик — владелец показа, и лишний вызов
        // означал бы лишний щелчок звука и лишний кадр анимации на ровном месте.
        [Test]
        public void RepeatedState_DoesNotFireAgain()
        {
            var net   = new LoopbackNetwork();
            var host  = new BattleControlRelay(net.CreateNode());
            var guest = new BattleControlRelay(net.CreateNode());

            int fired = 0;
            guest.PauseChanged += (_, _) => fired++;

            host.RequestPause(true);
            net.PollAll();
            host.RequestPause(true);
            net.PollAll();

            Assert.AreEqual(1, fired, "Второе объявление того же состояния прошло молча");
        }

        [Test]
        public void Reset_ClearsPause_ForTheNextBattle()
        {
            var net  = new LoopbackNetwork();
            var host = new BattleControlRelay(net.CreateNode());

            host.RequestPause(true);
            host.Reset();

            Assert.IsFalse(host.IsPaused, "Пауза не переносится через границу боя");
            Assert.AreEqual(NetPeer.NoPeer, host.PausedBy);
        }
    }
}
