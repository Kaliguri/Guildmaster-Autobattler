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
    public sealed class SessionStageTests
    {
        [Test]
        public void Stage_SurvivesTheRoundTrip()
        {
            SessionStageState sent = SessionStageState.Reward(
                new[] { "relic.ruby", "relic.iron", "relic.ash" }, inventoryFull: false);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));

            Assert.AreEqual(sent, got, "витрина пережила дорогу целиком и в том же порядке");
            Assert.IsTrue(got.TryOpenReward(out RewardStage shelf));
            Assert.AreEqual(3, shelf.Options.Count);
            Assert.AreEqual("relic.iron", shelf.Options[1], "порядок вариантов — это порядок карточек");
        }

        [Test]
        public void Idle_MeansNoScreen()
        {
            var writer = new NetByteWriter(16);
            Assert.IsTrue(SessionStageCodec.TryRead(
                SessionStageCodec.Write(SessionStageState.Idle, writer), out SessionStageState got));

            Assert.AreEqual(SessionStageKind.None, got.Kind);
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
            SessionStageState sent = SessionStageState.Reward(new[] { "relic.ruby" }, inventoryFull: true);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));

            Assert.IsTrue(got.TryOpenReward(out RewardStage shelf));
            Assert.IsTrue(shelf.InventoryFull, "запас полон — витрина предложит обмен, а не простое «взять»");
            Assert.AreEqual(sent, got, "шаги с разным признаком места — разные шаги");
        }

        [Test]
        public void UnknownKind_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(200); // вида экрана с таким номером в этой сборке нет
            writer.WriteUShort(0); // коробки нет
            writer.WriteBool(false);

            Assert.IsFalse(SessionStageCodec.TryRead(writer.WrittenSegment, out _));
        }

        [Test]
        public void TruncatedOptions_AreRefused()
        {
            var box = new NetByteWriter(64);
            box.WriteBool(false);
            box.WriteByte(3);              // обещали три варианта...
            box.WriteString("relic.ruby"); // ...а прислали один

            var writer = new NetByteWriter(64);
            writer.WriteByte((byte)SessionStageKind.Reward);
            writer.WriteUShort((ushort)box.Length);
            for (int i = 0; i < box.Length; i++) writer.WriteByte(box.WrittenSegment.Array[i]);
            writer.WriteBool(false);

            Assert.IsFalse(SessionStageCodec.TryRead(writer.WrittenSegment, out _),
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
                    channel == NetChannel.SessionStage) announcements++;
            };

            var stage = new HostSessionStage(hostNode);
            SessionStageState shelf = SessionStageState.Reward(new[] { "relic.ruby", "relic.iron" }, inventoryFull: false);

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
            SessionStageState first  = SessionStageState.Reward(new[] { "relic.ruby" }, inventoryFull: false);
            SessionStageState second = SessionStageState.Reward(new[] { "relic.iron" }, inventoryFull: false);

            Assert.AreNotEqual(first, second,
                "сравнение по одному виду шага скрыло бы смену витрины — гость остался бы на прежней");
        }

        [Test]
        public void NodeEnd_CarriesTheFarewellOfTheNodeThatRanIt()
        {
            SessionStageState sent = SessionStageState.Idle.EndingNode(
                "ui.node.chest.title", "ui.node.chest.farewell");

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));

            Assert.IsTrue(got.Rest.Ended);
            Assert.AreEqual("ui.node.chest.title",    got.Rest.TitleKey);
            Assert.AreEqual("ui.node.chest.farewell", got.Rest.BodyKey, "второй ключ — тело, а не заголовок");
            Assert.IsTrue(got.Rest.HasFarewell);
        }

        /// <summary>
        /// Бой кончается без кадра-прощания, и это не «пустая строка вместо ключа», а отдельный случай.
        /// </summary>
        [Test]
        public void NodeEndWithoutKeys_IsJustTheButtons()
        {
            SessionStageState sent = SessionStageState.Idle.EndingNode();

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));

            Assert.IsTrue(got.Rest.Ended);
            Assert.IsFalse(got.Rest.HasFarewell, "провожать нечего — исход боя показан своим экраном");
            Assert.AreNotEqual(sent, SessionStageState.Idle.EndingNode("ui.a", "ui.b"),
                "конец узла с кадром и без — разные шаги, иначе кадр молча не доехал бы");
        }

        /// <summary>
        /// Кнопки «дальше» ложатся ПОВЕРХ экрана узла, а не вместо него.
        /// </summary>
        /// <remarks>
        /// У текстового события под ними остаётся само событие с текстом результата (QA #49). Пока конец
        /// узла был отдельным видом экрана, он этот текст стирал.
        /// </remarks>
        [Test]
        public void NodeEnd_KeepsTheScreenUnderneath()
        {
            SessionStageState sent = SessionStageState.TextEvent("event.crossroads", gold: 40).EndingNode();

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));

            Assert.IsTrue(got.Rest.Ended, "узел пройден — кнопки на месте");
            Assert.IsTrue(got.TryOpenTextEvent(out TextEventStage ev), "и событие под ними тоже");
            Assert.AreEqual("event.crossroads", ev.EventId);
            Assert.AreEqual(40, ev.Gold);
        }

        [Test]
        public void TextEvent_CarriesGoldAlongWithTheEvent()
        {
            SessionStageState sent = SessionStageState.TextEvent("event.crossroads", gold: 137);

            var writer = new NetByteWriter(64);
            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in sent, writer), out SessionStageState got));
            Assert.IsTrue(got.TryOpenTextEvent(out TextEventStage ev));

            Assert.AreEqual("event.crossroads", ev.EventId);
            Assert.AreEqual(137, ev.Gold, "от золота зависит, какие варианты ответа живые");
        }

        [Test]
        public void Outcome_SurvivesTheRoundTrip()
        {
            var writer = new NetByteWriter(16);
            SessionStageState win = SessionStageState.Outcome(victory: true);

            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in win, writer), out SessionStageState got));
            Assert.IsTrue(got.TryOpenOutcome(out OutcomeStage outcome));
            Assert.IsTrue(outcome.Victory);

            Assert.AreNotEqual(win, SessionStageState.Outcome(victory: false), "победа и поражение — разные шаги");
        }

        /// <summary>
        /// Двор едет тем же каналом, что и экраны узла, и несёт имя дома.
        /// </summary>
        /// <remarks>
        /// <b>Инвариант между ролями, поэтому он в тесте.</b> Двор был последним экраном с ДВУМЯ
        /// дорогами показа: петля владельца публиковала запрос напрямую, а гость поднимал экран по
        /// <c>ActivityState.HubOpen</c> — и имени дома при этом не получал вовсе, потому что второй
        /// путь его не вёз. Ровно та форма, из-за которой разъезжались экраны узла (09.08.2026).
        /// </remarks>
        [Test]
        public void Hub_SurvivesTheRoundTripWithItsName()
        {
            var writer = new NetByteWriter(64);
            SessionStageState hub = SessionStageState.Hub("Дом Алебардиум");

            Assert.IsTrue(SessionStageCodec.TryRead(SessionStageCodec.Write(in hub, writer),
                                                    out SessionStageState got));
            Assert.IsTrue(got.TryOpenHub(out HubStage box), "двор не разобрался обратно");
            Assert.AreEqual("Дом Алебардиум", box.GuildName,
                "имя дома не доехало — у гостя на экране снова будет пустое место");
        }

        /// <summary>Коробку двора не открыть на чужом виде: вид решает, что разбирать.</summary>
        [Test]
        public void Hub_DoesNotOpenOnAnotherKind()
        {
            SessionStageState outcome = SessionStageState.Outcome(victory: true);

            Assert.IsFalse(outcome.TryOpenHub(out _), "исход забега разобрался как двор");
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
            SessionStageState outcome = SessionStageState.Outcome(victory: true);

            Assert.IsFalse(outcome.TryOpenTextEvent(out _), "исход — не текстовое событие");
            Assert.IsFalse(outcome.TryOpenReward(out _),    "исход — не витрина");
            Assert.IsTrue(outcome.TryOpenOutcome(out _));
        }

        /// <summary>Вид без коробки приехал с хвостом — это чужая версия, а не «лишние байты».</summary>
        [Test]
        public void EmptyKindWithPayload_IsRefused()
        {
            var box = new NetByteWriter(64);
            box.WriteString("а тут вдруг что-то лежит");

            var writer = new NetByteWriter(64);
            writer.WriteByte((byte)SessionStageKind.Chest);
            writer.WriteUShort((ushort)box.Length);
            for (int i = 0; i < box.Length; i++) writer.WriteByte(box.WrittenSegment.Array[i]);
            writer.WriteBool(false);

            Assert.IsFalse(SessionStageCodec.TryRead(writer.WrittenSegment, out _));
        }
    }
}
