using Guildmaster.Data;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Session.Net;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Забег в коопе: у гостя состояние ПРИСЛАННОЕ, а изменения он просит сделать хоста.
    /// <para>Обещание здесь одно и держит всю гостевую ветку сеанса: <b>состояние у гостя такое же, как
    /// у хоста, и получено оно ровно одним путём</b> — снимком. Заведись второй путь (например,
    /// применение лога команд у себя), и расходиться они начали бы на первом же изменении, прошедшем
    /// мимо шины, — а такие изменения сегодня есть и записаны долгом (транзакции магазина и наград).</para>
    /// </summary>
    public sealed class GuestRunSyncTests
    {
        private LoopbackNetwork _net;
        private INetTransport   _hostNode;
        private INetTransport   _guestNode;

        private RunStateService   _hostRun;
        private RunCommandBus     _hostBus;
        private RunStateBroadcast _broadcast;

        private GuestRunState     _guestRun;
        private RemoteRunCommands _guestCommands;

        [SetUp]
        public void SetUp()
        {
            _net       = new LoopbackNetwork();
            _hostNode  = _net.CreateNode();
            _guestNode = _net.CreateNode();

            GameConfig config = GameConfig.CreateDefault();
            _hostRun = new RunStateService(new InMemorySaveService(), config, new FixedProfileService(), content: null);
            _hostRun.NewDefaultRun(1L);

            _hostBus   = new RunCommandBus(new RunCommandApplier(_hostRun), new RunCommandLog());
            _broadcast = new RunStateBroadcast(_hostNode, _hostBus, _hostRun);

            _guestRun      = new GuestRunState(_guestNode);
            _guestCommands = new RemoteRunCommands(_guestNode);
        }

        [TearDown]
        public void TearDown()
        {
            _broadcast.Dispose();
            _guestRun.Dispose();
        }

        // ═══ Главное обещание: у гостя то же состояние ═══

        [Test]
        public void HostChange_ReachesTheGuest()
        {
            _hostBus.AddGold(140);
            _broadcast.Tick();
            _net.PollAll();

            Assert.IsNotNull(_guestRun.Current, "Снимок забега доехал");
            Assert.AreEqual(_hostRun.Current.Gold, _guestRun.Current.Gold, "Золото у гостя такое же");
        }

        [Test]
        public void SeveralChangesInOneFrame_CostOneSnapshot()
        {
            _hostBus.AddGold(10);
            _hostBus.SetSlotPosition(0, new Vector2(-4f, 1f));
            _hostBus.SetSlotRelic(1, "relic.knight");
            _broadcast.Tick();
            _net.PollAll();

            Assert.AreEqual(1, _broadcast.SnapshotsSent,
                "Серия правок в одном кадре — один снимок: промежуточные состояния никому не нужны");
            Assert.AreEqual(new Vector2(-4f, 1f), _guestRun.Current.Guild[0].SavedPosition,
                "И доехало при этом последнее состояние, а не первое");
        }

        [Test]
        public void NoChange_SendsNothing()
        {
            _broadcast.Tick();
            _broadcast.Tick();
            _net.PollAll();

            Assert.AreEqual(0, _broadcast.SnapshotsSent, "Молчащий забег не гоняет снимки впустую");
        }

        // ═══ Обратная сторона: интент гостя ═══

        [Test]
        public void GuestIntent_IsAppliedByTheHost_AndComesBackAsSnapshot()
        {
            int before = _hostRun.Current.Gold;

            _guestCommands.AddGold(75);
            _net.PollAll();          // интент доехал до хоста и применился
            _broadcast.Tick();
            _net.PollAll();          // снимок вернулся гостю

            Assert.AreEqual(before + 75, _hostRun.Current.Gold, "Хост применил чужой интент");
            Assert.AreEqual(1, _broadcast.IntentsAccepted);
            Assert.AreEqual(_hostRun.Current.Gold, _guestRun.Current.Gold, "И вернул гостю результат");
        }

        /// <summary>
        /// Локально у гостя не меняется НИЧЕГО: держателя, который умеет меняться, у него нет вовсе.
        /// Ответ «принято локально» здесь честно отрицательный — вызывающие его уже читают.
        /// </summary>
        [Test]
        public void GuestIntent_ChangesNothingLocally()
        {
            Assert.IsFalse(_guestCommands.SetSlotPosition(0, new Vector2(1f, 1f)),
                "Гость не применяет команду у себя");
            Assert.IsFalse(_guestCommands.RequestSave(),
                "И сохранять ему нечего: забег не его, пишет его хост");
            Assert.IsNull(_guestRun.Current, "До первого снимка забега у гостя нет — и это законно");
        }

        // ═══ Идемпотентность и подлог: то, ради чего у команды есть автор и номер ═══

        [Test]
        public void SameIntentTwice_IsAppliedOnce()
        {
            var gold = new RunCommand(RunCommandKind.AddGold, playerId: _guestNode.LocalPeerId,
                sequence: 3, clientTimeMs: 1000, amount: 50);

            int before = _hostRun.Current.Gold;

            Assert.IsTrue(_hostBus.Submit(in gold),  "Первый раз применяется");
            Assert.IsFalse(_hostBus.Submit(in gold), "Повтор после реконнекта — не второе списание");
            Assert.AreEqual(before + 50, _hostRun.Current.Gold);
        }

        [Test]
        public void IntentFromSomeoneElsesName_IsRejected()
        {
            var writer = new Guildmaster.Net.NetByteWriter(64);
            var forged = new RunCommand(RunCommandKind.AddGold, playerId: 777, sequence: 0,
                clientTimeMs: 1000, amount: 9999);

            byte[] envelope = null;
            _guestNode.Send(NetPeer.HostPeerId,
                Guildmaster.Net.NetEnvelope.Wrap(Guildmaster.Net.NetChannel.RunCommand,
                    RunCommandCodec.Write(in forged, writer), ref envelope),
                NetDelivery.Reliable);

            int before = _hostRun.Current.Gold;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("от имени"));
            _net.PollAll();

            Assert.AreEqual(before, _hostRun.Current.Gold,
                "Автор команды — тот, кто её прислал: подменить его не стоит одного поля");
        }

        // ═══ Кодек ═══

        [Test]
        public void Command_SurvivesTheRoundTrip()
        {
            var sent = new RunCommand(RunCommandKind.SetSlotPosition, playerId: 2, sequence: 11,
                clientTimeMs: 1_700_000_000_123L, slotIndex: 3, amount: -7, text: "relic.knight",
                x: -4.25f, y: 2.5f);

            var writer = new Guildmaster.Net.NetByteWriter(64);
            Assert.IsTrue(RunCommandCodec.TryRead(RunCommandCodec.Write(in sent, writer), out RunCommand got));

            Assert.AreEqual(sent.Kind,         got.Kind);
            Assert.AreEqual(sent.PlayerId,     got.PlayerId);
            Assert.AreEqual(sent.Sequence,     got.Sequence);
            Assert.AreEqual(sent.ClientTimeMs, got.ClientTimeMs, "Штамп времени переживает восемь байт");
            Assert.AreEqual(sent.SlotIndex,    got.SlotIndex);
            Assert.AreEqual(sent.Amount,       got.Amount);
            Assert.AreEqual(sent.Text,         got.Text);
            Assert.AreEqual(sent.X,            got.X);
            Assert.AreEqual(sent.Y,            got.Y);
        }

        /// <summary>
        /// Снимок едет тем же DTO, что и сейв, и по тем же правилам. Готча, ради которой это проверяется
        /// отдельно: <c>Vector2</c> без своего конвертера уводит Newtonsoft в рекурсию — то есть вешает
        /// отправку на первой же позиции слота, а не «слегка меняет JSON».
        /// </summary>
        [Test]
        public void Snapshot_KeepsPositionsAndRoster()
        {
            _hostRun.Current.Guild[2].SavedPosition = new Vector2(-3.5f, 4.25f);

            RunState copy = RunSnapshotCodec.Read(RunSnapshotCodec.Write(_hostRun.Current));

            Assert.IsNotNull(copy);
            Assert.AreEqual(_hostRun.Current.Seed,        copy.Seed);
            Assert.AreEqual(_hostRun.Current.Guild.Length, copy.Guild.Length);
            Assert.AreEqual(new Vector2(-3.5f, 4.25f),    copy.Guild[2].SavedPosition,
                "Позиция сосуда пережила дорогу");
        }

        /// <summary>
        /// Гость входит и просит забег сам — объявление, посланное ему навстречу, ушло бы в пустоту:
        /// его приёмник рождается позже рукопожатия. Это тот же класс дефекта, что «работает со второго
        /// раза», и держится он именно здесь.
        /// </summary>
        [Test]
        public void GuestAsksForTheRun_AndGetsItWithoutWaitingForAChange()
        {
            _hostBus.AddGold(200);
            _broadcast.Tick();
            _net.PollAll();          // всё, что было до входа гостя

            var latecomer = new GuestRunState(_net.CreateNode());
            try
            {
                latecomer.Start();   // вход: подписался и спросил
                _net.PollAll();      // просьба дошла до хоста, снимок вернулся

                Assert.IsNotNull(latecomer.Current, "Опоздавший гость получил забег, ничего не дожидаясь");
                Assert.AreEqual(_hostRun.Current.Gold, latecomer.Current.Gold);
            }
            finally
            {
                latecomer.Dispose();
            }
        }
    }
}
