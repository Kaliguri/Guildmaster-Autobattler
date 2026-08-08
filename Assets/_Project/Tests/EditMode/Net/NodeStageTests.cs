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
            NodeStageState sent = NodeStageState.Reward(
                new[] { "relic.ruby", "relic.iron", "relic.ash" }, inventoryFull: false);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));

            Assert.AreEqual(sent, got, "витрина пережила дорогу целиком и в том же порядке");
            Assert.IsTrue(got.TryOpenReward(out RewardStage shelf));
            Assert.AreEqual(3, shelf.Options.Count);
            Assert.AreEqual("relic.iron", shelf.Options[1], "порядок вариантов — это порядок карточек");
        }

        [Test]
        public void Idle_MeansNoScreen()
        {
            var writer = new NetByteWriter(16);
            Assert.IsTrue(NodeStageCodec.TryRead(
                NodeStageCodec.Write(NodeStageState.Idle, writer), out NodeStageState got));

            Assert.AreEqual(NodeStageKind.None, got.Kind);
            Assert.IsFalse(got.TryOpenReward(out _), "на пустом шаге витрины нет");
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
            NodeStageState sent = NodeStageState.Reward(new[] { "relic.ruby" }, inventoryFull: true);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));

            Assert.IsTrue(got.TryOpenReward(out RewardStage shelf));
            Assert.IsTrue(shelf.InventoryFull, "запас полон — витрина предложит обмен, а не простое «взять»");
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
            NodeStageState shelf = NodeStageState.Reward(new[] { "relic.ruby", "relic.iron" }, inventoryFull: false);

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
            NodeStageState first  = NodeStageState.Reward(new[] { "relic.ruby" }, inventoryFull: false);
            NodeStageState second = NodeStageState.Reward(new[] { "relic.iron" }, inventoryFull: false);

            Assert.AreNotEqual(first, second,
                "сравнение по одному виду шага скрыло бы смену витрины — гость остался бы на прежней");
        }

        [Test]
        public void Interlude_CarriesTheFarewellOfTheNodeThatEnded()
        {
            NodeStageState sent = NodeStageState.Interlude("ui.node.chest.title", "ui.node.chest.farewell");

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));
            Assert.IsTrue(got.TryOpenInterlude(out InterludeStage rest));

            Assert.AreEqual("ui.node.chest.title",    rest.TitleKey);
            Assert.AreEqual("ui.node.chest.farewell", rest.BodyKey, "второй ключ — тело, а не заголовок");
            Assert.IsTrue(rest.HasFarewell);
        }

        /// <summary>
        /// Бой кончается без кадра-прощания, и это не «пустая строка вместо ключа», а отдельный случай.
        /// </summary>
        [Test]
        public void InterludeWithoutKeys_IsJustTheButtons()
        {
            NodeStageState sent = NodeStageState.Interlude();

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));
            Assert.IsTrue(got.TryOpenInterlude(out InterludeStage rest));

            Assert.IsFalse(rest.HasFarewell, "провожать нечего — исход боя показан своим экраном");
            Assert.AreNotEqual(sent, NodeStageState.Interlude("ui.a", "ui.b"),
                "передышка с кадром и без — разные шаги, иначе кадр молча не доехал бы");
        }

        [Test]
        public void TextEvent_CarriesGoldAlongWithTheEvent()
        {
            NodeStageState sent = NodeStageState.TextEvent("event.crossroads", gold: 137);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in sent, writer), out NodeStageState got));
            Assert.IsTrue(got.TryOpenTextEvent(out TextEventStage ev));

            Assert.AreEqual("event.crossroads", ev.EventId);
            Assert.AreEqual(137, ev.Gold, "от золота зависит, какие варианты ответа живые");
        }

        [Test]
        public void Outcome_SurvivesTheRoundTrip()
        {
            var writer = new NetByteWriter(16);
            NodeStageState win = NodeStageState.Outcome(victory: true);

            Assert.IsTrue(NodeStageCodec.TryRead(NodeStageCodec.Write(in win, writer), out NodeStageState got));
            Assert.IsTrue(got.TryOpenOutcome(out OutcomeStage outcome));
            Assert.IsTrue(outcome.Victory);

            Assert.AreNotEqual(win, NodeStageState.Outcome(victory: false), "победа и поражение — разные шаги");
        }

        /// <summary>
        /// Коробку чужого вида не открыть: показ обязан упереться в вид, а не разобрать байты наугад.
        /// </summary>
        /// <remarks>
        /// Ровно этим и опасен общий мешок полей: у прощания и у события там лежали бы две строки, и
        /// «заголовок с телом» разобрались бы как «id события с чем-то» без единой жалобы.
        /// </remarks>
        [Test]
        public void BoxOfAnotherKind_DoesNotOpen()
        {
            NodeStageState rest = NodeStageState.Interlude("ui.a", "ui.b");

            Assert.IsFalse(rest.TryOpenTextEvent(out _), "конец узла — не текстовое событие");
            Assert.IsFalse(rest.TryOpenReward(out _),    "конец узла — не витрина");
            Assert.IsTrue(rest.TryOpenInterlude(out _));
        }

        /// <summary>Вид без коробки приехал с хвостом — это чужая версия, а не «лишние байты».</summary>
        [Test]
        public void EmptyKindWithPayload_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte((byte)NodeStageKind.Chest);
            writer.WriteString("а тут вдруг что-то лежит");

            Assert.IsFalse(NodeStageCodec.TryRead(writer.WrittenSegment, out _));
        }
    }
}
