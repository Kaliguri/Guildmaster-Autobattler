using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Шаг узла по сети: что на экране у группы и из чего выбирают.
    /// </summary>
    /// <remarks>
    /// Формат ломается молча — обе стороны собираются порознь, — поэтому роундтрип и правило отказа
    /// живут в тесте. Особенно важен отказ на неизвестном виде шага: показать «примерно то же» значит
    /// увести гостя на другой экран и получить голос за вариант, которого он не видел.
    /// </remarks>
    public sealed class NodeStageTests
    {
        [Test]
        public void Stage_SurvivesTheRoundTrip()
        {
            var sent = new NodeStageState(NodeStageKind.Reward,
                new[] { "relic.ruby", "relic.iron", "relic.ash" });

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));

            Assert.AreEqual(sent, got, "витрина пережила дорогу целиком и в том же порядке");
            Assert.AreEqual(3, got.Options.Count);
            Assert.AreEqual("relic.iron", got.Options[1], "порядок вариантов — это порядок карточек");
        }

        [Test]
        public void Idle_MeansNoScreen()
        {
            var writer = new NetByteWriter(16);
            Assert.IsTrue(NodeStageCodec.TryRead(
                NodeStageCodec.Write(NodeStageState.Idle, writer), out NodeStageState got));

            Assert.AreEqual(NodeStageKind.None, got.Kind);
            Assert.IsEmpty(got.Options);
        }

        /// <summary>
        /// Признак «запас полон» едет вместе с витриной, а не считается на каждой стороне сама.
        /// </summary>
        /// <remarks>
        /// От него зависит текст ГОЛОСА («взять» против «взять взамен того-то»), а согласие сравнивает
        /// голоса побайтово. Пока гость считал его сам и всегда получал <c>false</c>, при полном запасе
        /// у владельца голоса не сходились никогда — витрина не закрывалась ни у кого, и забег вставал.
        /// </remarks>
        [Test]
        public void InventoryFull_TravelsWithTheShelf()
        {
            var sent = new NodeStageState(NodeStageKind.Reward, new[] { "relic.ruby" }, inventoryFull: true);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));

            Assert.IsTrue(got.InventoryFull, "запас полон — витрина предложит обмен, а не простое «взять»");
            Assert.AreEqual(sent, got, "шаги с разным признаком места — разные шаги");
        }

        [Test]
        public void UnknownKind_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(200); // вида шага с таким номером в этой сборке нет
            writer.WriteBool(false);
            writer.WriteByte(0);

            Assert.IsFalse(NodeStageCodec.TryRead(writer.WrittenSegment, out _));
        }

        [Test]
        public void TruncatedOptions_AreRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte((byte)NodeStageKind.Reward);
            writer.WriteBool(false);
            writer.WriteByte(3);            // обещали три варианта...
            writer.WriteString("relic.ruby"); // ...а прислали один

            Assert.IsFalse(NodeStageCodec.TryRead(writer.WrittenSegment, out _),
                "оборванная витрина — это расхождение версий, а не повод показать один вариант из трёх");
        }

        /// <summary>
        /// Элитка даёт две награды подряд, и вторая может выпасть тем же составом, что первая.
        /// </summary>
        /// <remarks>
        /// Между наградами экран закрывается, и владелец объявляет <c>Idle</c>. Без него второе
        /// объявление совпало бы с первым, было бы отброшено как повтор — и гость остался бы без второй
        /// витрины, потому что свою он уже закрыл по срабатыванию решения.
        /// </remarks>
        [Test]
        public void SameShelfTwice_IsAnnouncedAgainBecauseOfTheIdleBetween()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode  = net.CreateNode();
            INetTransport guestNode = net.CreateNode();
            net.PollAll();

            int announcements = 0;
            guestNode.MessageReceived += (from, message) =>
            {
                if (NetEnvelope.TryUnwrap(message, out NetChannel channel, out _) &&
                    channel == NetChannel.NodeStage) announcements++;
            };

            var stage = new HostNodeStage(hostNode);
            var shelf = new NodeStageState(NodeStageKind.Reward, new[] { "relic.ruby", "relic.iron" });

            stage.Announce(shelf);   // первая награда элитки
            stage.Clear();           // экран закрылся
            stage.Announce(shelf);   // вторая выпала тем же составом
            net.PollAll();

            Assert.AreEqual(3, announcements,
                "витрина, закрытие и вторая витрина — три объявления; без Idle между ними вторая " +
                "потерялась бы как повтор, и гость остался бы без экрана");
        }

        [Test]
        public void DifferentOptions_AreDifferentStages()
        {
            var first  = new NodeStageState(NodeStageKind.Reward, new[] { "relic.ruby" });
            var second = new NodeStageState(NodeStageKind.Reward, new[] { "relic.iron" });

            Assert.AreNotEqual(first, second,
                "сравнение по одному виду шага скрыло бы смену витрины — гость остался бы на прежней");
        }
    }
}
