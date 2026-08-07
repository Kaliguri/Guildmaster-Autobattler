using Guildmaster.Core.Net;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Общее согласие: действие происходит ровно тогда, когда его подтвердили все участники.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между сетевым слоем и кнопкой: одна сторона считает голоса, другая рисует счёт, и
    /// правятся они порознь. Отдельно закреплён уход игрока — кнопка, которую нельзя нажать, потому что
    /// ждут вышедшего, зависает молча и без видимой причины.
    /// </remarks>
    public sealed class AgreementDecisionTests
    {
        [Test]
        public void Solo_FiresImmediately()
        {
            var net  = new LoopbackNetwork().CreateNode();
            var gate = new HostSharedDecision(net, null);
            gate.Start();

            int fired = 0;
            gate.Bind("battle.start", () => fired++);

            Assert.AreEqual(1, gate.Required, "в одиночку участник один");

            gate.ToggleLocal();

            Assert.AreEqual(1, fired, "соло не должно ничего ждать");
            Assert.AreEqual(0, gate.Voted, "после срабатывания счёт обнуляется");
        }

        [Test]
        public void TwoPlayers_WaitForBoth()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostSharedDecision(hostNode, null);
            gate.Start();

            host.CreateNode();  // второй игрок входит в сессию...
            host.PollAll();     // ...и хост узнаёт об этом на своём следующем кадре, а не мгновенно

            int fired = 0;
            gate.Bind("battle.start", () => fired++);

            Assert.AreEqual(2, gate.Required);

            gate.ToggleLocal();
            Assert.AreEqual(0, fired, "один из двоих — ждём второго");
            Assert.AreEqual(1, gate.Voted);
        }

        [Test]
        public void SecondPress_TakesTheVoteBack()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostSharedDecision(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });

            gate.ToggleLocal();
            Assert.IsTrue(gate.HasLocalChoice);

            gate.ToggleLocal();
            Assert.IsFalse(gate.HasLocalChoice, "второе нажатие снимает своё согласие");
            Assert.AreEqual(0, gate.Voted);
        }

        [Test]
        public void Reset_DropsEveryVote()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostSharedDecision(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });
            gate.ToggleLocal();

            gate.Reset("расстановка изменилась");

            Assert.AreEqual(0, gate.Voted, "подтверждали то, чего больше нет");
        }

        [Test]
        public void RebindingAnotherAction_ClearsVotes()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostSharedDecision(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });
            gate.ToggleLocal();

            gate.Bind("battle.continue", () => { });

            Assert.AreEqual(0, gate.Voted, "согласие относилось к другому действию");
        }

        /// <summary>
        /// Срабатывание объявляется отдельным признаком, а не выводится из обнулённого счёта: экран,
        /// ждущий согласия, обязан закрыться именно на нём. Сброс тоже обнуляет счёт — спутав их, экран
        /// закрывался бы от чужой правки расстановки.
        /// </summary>
        [Test]
        public void Firing_IsAnnouncedApartFromReset()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var heard = new ListPublisher();
            var gate = new HostSharedDecision(hostNode, heard);
            gate.Start();

            gate.Bind("battle.continue", () => { });
            heard.Events.Clear();

            gate.ToggleLocal(); // соло: согласие собрано целиком, действие произошло

            Assert.IsTrue(heard.Events.Exists(e => e.Fired && e.Key == "battle.continue"),
                "срабатывание должно быть объявлено, иначе экран итога не закроется");

            // Сброс проверяем вдвоём: в одиночку любое согласие срабатывает мгновенно, и отличить сброс
            // от срабатывания на таком материале нельзя в принципе.
            var pair = new LoopbackNetwork();
            INetTransport pairHost = pair.CreateNode();
            var heardPair = new ListPublisher();
            var paired = new HostSharedDecision(pairHost, heardPair);
            paired.Start();
            pair.CreateNode();
            pair.PollAll();

            paired.Bind("battle.continue", () => { });
            paired.ToggleLocal();
            heardPair.Events.Clear();

            paired.Reset("расстановка изменилась");

            Assert.IsFalse(heardPair.Events.Exists(e => e.Fired),
                "сброс — это не срабатывание, и путать их нельзя: экран закрылся бы от чужой правки");
        }

        private sealed class ListPublisher : IPublisher<SharedDecisionChangedEvent>
        {
            public readonly System.Collections.Generic.List<SharedDecisionChangedEvent> Events = new();

            public void Publish(SharedDecisionChangedEvent message) => Events.Add(message);
        }
    }
}
