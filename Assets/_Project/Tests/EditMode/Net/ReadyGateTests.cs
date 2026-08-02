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
    public sealed class ReadyGateTests
    {
        [Test]
        public void Solo_FiresImmediately()
        {
            var net  = new LoopbackNetwork().CreateNode();
            var gate = new HostReadyGate(net, null);
            gate.Start();

            int fired = 0;
            gate.Bind("battle.start", () => fired++);

            Assert.AreEqual(1, gate.Required, "в одиночку участник один");

            gate.ToggleLocal();

            Assert.AreEqual(1, fired, "соло не должно ничего ждать");
            Assert.AreEqual(0, gate.Ready, "после срабатывания счёт обнуляется");
        }

        [Test]
        public void TwoPlayers_WaitForBoth()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostReadyGate(hostNode, null);
            gate.Start();

            host.CreateNode();  // второй игрок входит в сессию...
            host.PollAll();     // ...и хост узнаёт об этом на своём следующем кадре, а не мгновенно

            int fired = 0;
            gate.Bind("battle.start", () => fired++);

            Assert.AreEqual(2, gate.Required);

            gate.ToggleLocal();
            Assert.AreEqual(0, fired, "один из двоих — ждём второго");
            Assert.AreEqual(1, gate.Ready);
        }

        [Test]
        public void SecondPress_TakesTheVoteBack()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostReadyGate(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });

            gate.ToggleLocal();
            Assert.IsTrue(gate.LocallyReady);

            gate.ToggleLocal();
            Assert.IsFalse(gate.LocallyReady, "второе нажатие снимает своё согласие");
            Assert.AreEqual(0, gate.Ready);
        }

        [Test]
        public void Reset_DropsEveryVote()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostReadyGate(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });
            gate.ToggleLocal();

            gate.Reset("расстановка изменилась");

            Assert.AreEqual(0, gate.Ready, "подтверждали то, чего больше нет");
        }

        [Test]
        public void RebindingAnotherAction_ClearsVotes()
        {
            var host = new LoopbackNetwork();
            INetTransport hostNode = host.CreateNode();
            var gate = new HostReadyGate(hostNode, null);
            gate.Start();
            host.CreateNode();
            host.PollAll();

            gate.Bind("battle.start", () => { });
            gate.ToggleLocal();

            gate.Bind("battle.continue", () => { });

            Assert.AreEqual(0, gate.Ready, "согласие относилось к другому действию");
        }
    }
}
