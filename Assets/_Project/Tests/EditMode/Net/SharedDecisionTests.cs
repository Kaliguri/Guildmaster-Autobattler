using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Общее решение с вариантами: группа получает то, что выбрали ВСЕ и одинаково.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт между сетью, экраном и правилами коопа, поэтому он в тесте.</b> Согласие
    /// («Начать бой») — частный случай выбора с одним вариантом, и обе формы обязаны считаться ОДНИМ
    /// механизмом: два похожих счётчика разъедутся, а расхождение здесь читается игроком как зависшая
    /// кнопка.
    /// <para>Отдельно закреплено то, что нарушить легче всего: <b>расхождение не разрешается само</b>.
    /// Все проголосовали, но за разное — решение не принято, и игра НЕ зовёт арбитра: спор это выбор
    /// игроков, а не диагноз (канон коопа, вердикт Макса 30.07.2026). Соблазн «ну давайте возьмём
    /// вариант большинства» будет возникать снова.</para>
    /// </remarks>
    public sealed class SharedDecisionTests
    {
        private const string Key  = DecisionKeys.RewardPick;
        private const string Ruby = "relic.ruby";
        private const string Iron = "relic.iron";

        [Test]
        public void Solo_TakesWhatWasChosen()
        {
            INetTransport node = new LoopbackNetwork().CreateNode();
            var decision = new HostSharedDecision(node, null);
            decision.Start();

            string taken = null;
            decision.Bind(Key, option => taken = option);

            decision.Choose(Ruby);

            Assert.AreEqual(Ruby, taken, "в одиночку выбор исполняется сразу и именно тот, что сделали");
            Assert.AreEqual(0, decision.Voted, "после срабатывания голоса гаснут");
        }

        [Test]
        public void SameOptionTwice_TakesTheVoteBack()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();
            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();

            net.CreateNode(); // второй участник: иначе решение сработает на первом же голосе
            net.PollAll();

            decision.Bind(Key, _ => { });

            decision.Choose(Ruby);
            Assert.AreEqual(Ruby, decision.LocalChoice);

            decision.Choose(Ruby);
            Assert.AreEqual(DecisionOptions.None, decision.LocalChoice, "повтор того же варианта — отказ от голоса");
            Assert.AreEqual(0, decision.Voted);
        }

        [Test]
        public void OtherOption_ReplacesTheVoteWithoutGivingItUpFirst()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();
            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();

            net.CreateNode();
            net.PollAll();

            decision.Bind(Key, _ => { });

            decision.Choose(Ruby);
            decision.Choose(Iron);

            Assert.AreEqual(Iron, decision.LocalChoice, "передумать можно одним нажатием, без промежуточного шага");
            Assert.AreEqual(1, decision.Voted, "голос один, а не два");
        }

        [Test]
        public void EveryoneChoseTheSame_DecisionIsTaken()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode  = net.CreateNode();
            INetTransport guestNode = net.CreateNode();
            net.PollAll();

            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();

            string taken = null;
            decision.Bind(Key, option => taken = option);

            decision.Choose(Ruby);
            Assert.IsNull(taken, "один из двоих — ждём второго");

            SendVote(guestNode, Ruby);
            net.PollAll();

            Assert.AreEqual(Ruby, taken, "сошлись на одном — решение принято");
        }

        [Test]
        public void EveryoneChoseButDifferently_NothingHappensAndNobodyIsAsked()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode  = net.CreateNode();
            INetTransport guestNode = net.CreateNode();
            net.PollAll();

            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();

            string taken = null;
            decision.Bind(Key, option => taken = option);

            decision.Choose(Ruby);
            SendVote(guestNode, Iron);
            net.PollAll();

            Assert.IsNull(taken, "выбрали разное — решения нет, и большинство тут ничего не решает");
            Assert.AreEqual(2, decision.Voted, "при этом высказались оба, и это видно");
            Assert.AreEqual(2, decision.Required);
        }

        [Test]
        public void Agreement_IsStillJustAChoiceWithOneOption()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode  = net.CreateNode();
            INetTransport guestNode = net.CreateNode();
            net.PollAll();

            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();

            int fired = 0;
            decision.Bind(DecisionKeys.BattleStart, () => fired++);

            decision.ToggleLocal();
            Assert.AreEqual(0, fired);

            // Гость шлёт ровно то же, что отправила бы его половина гейта на «Готов».
            SendVote(guestNode, DecisionOptions.Agree);
            net.PollAll();

            Assert.AreEqual(1, fired, "старое согласие обязано работать через новый механизм без оговорок");
        }

        [Test]
        public void Tally_CarriesWhoVotedForWhat()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode  = net.CreateNode();
            INetTransport guestNode = net.CreateNode();
            net.PollAll();

            var decision = new HostSharedDecision(hostNode, null);
            decision.Start();
            decision.Bind(Key, _ => { });

            // Гость слушает объявление хоста так же, как это делает его половина гейта.
            var seen = new List<PlayerChoice>();
            guestNode.MessageReceived += (from, message) =>
            {
                if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out var payload)) return;
                if (channel != NetChannel.Decision) return;

                var bytes = new NetByteReader(payload);
                if (bytes.ReadByte() != DecisionWire.Tally) return;

                bytes.ReadByte();   // планка
                bytes.ReadBool();   // сработало
                bytes.ReadString(); // ключ

                seen.Clear();
                int count = bytes.ReadByte();
                for (int i = 0; i < count; i++)
                    seen.Add(new PlayerChoice(bytes.ReadByte(), bytes.ReadString()));
            };

            decision.Choose(Iron);
            net.PollAll();

            Assert.AreEqual(1, seen.Count, "объявление несёт голоса поимённо — показу нужно «кто за что»");
            Assert.AreEqual(Iron, seen[0].Option);
            Assert.AreEqual(hostNode.LocalPeerId, seen[0].PlayerId);
        }

        /// <summary>Голос гостя ровно в том виде, в каком его шлёт <c>GuestSharedDecision</c>.</summary>
        private static void SendVote(INetTransport guest, string option)
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(DecisionWire.Vote);
            writer.WriteString(option);

            byte[] envelope = null;
            guest.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.Decision, writer.WrittenSegment, ref envelope),
                NetDelivery.Reliable);
        }
    }
}
