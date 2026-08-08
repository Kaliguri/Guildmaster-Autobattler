using Guildmaster.Core.Players;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Сторона участника как НАЗНАЧЕНИЕ: её можно поменять, и она переживает чужие входы и выходы.
    /// </summary>
    /// <remarks>
    /// <b>Решение Макса 08.08.2026:</b> «Мб в компаниях игрок сможет вставать на другую сторону. Или
    /// еще что. Это не должно быть прям ПРИВЯЗАНО к режиму или начальным параметром, это мы должны
    /// мочь легко менять». До этого сторона считалась от места входа и пересчитывалась при каждой
    /// пересадке — поменять её было нечем, а посаженного затёр бы следующий подключившийся.
    /// <para>Рассадка мероприятия («делить стороны или нет») осталась, но стала УМОЛЧАНИЕМ для тех,
    /// кого не сажали руками.</para>
    /// </remarks>
    public sealed class SessionSeatingTests
    {
        [Test]
        public void Seat_ChangesTheSide()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            net.CreateNode();
            net.PollAll();

            Assert.IsTrue(roster.TryGet(1, out SessionPlayer before));
            Assert.AreEqual(0, before.Team, "кампания: без деления все на нулевой стороне");

            roster.Seat(1, 1);

            Assert.IsTrue(roster.TryGet(1, out SessionPlayer after));
            Assert.AreEqual(1, after.Team, "посадили — сторона сменилась");
        }

        /// <summary>
        /// Главное свойство назначения: его не затирает чужой вход. Пока сторона считалась от места,
        /// подключившийся третий пересчитывал её всем.
        /// </summary>
        [Test]
        public void Seating_SurvivesSomeoneElseJoining()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            net.CreateNode();
            net.PollAll();
            roster.Seat(1, 1);

            net.CreateNode();      // третий участник входит уже после посадки
            net.PollAll();

            Assert.IsTrue(roster.TryGet(1, out SessionPlayer seated));
            Assert.AreEqual(1, seated.Team, "назначение пережило чужой вход");
        }

        /// <summary>
        /// Мероприятие сменилось целиком — прежние посадки к нему не относятся: посаженный в PvP на
        /// вторую сторону остался бы в кампании противником собственной группе.
        /// </summary>
        [Test]
        public void ChangingTheDefaultSeating_ClearsManualSeats()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            net.CreateNode();
            net.PollAll();

            roster.Seat(1, 0);                  // в PvP посадили гостя к себе
            roster.SplitBetweenSides(true);     // PvP делит стороны

            Assert.IsTrue(roster.TryGet(1, out SessionPlayer after));
            Assert.AreEqual(1, after.Team, "после смены рассадки играет умолчание мероприятия");
        }

        /// <summary>
        /// Ушедший уносит своё назначение: номер пира переиспользуется, и старая посадка досталась бы
        /// следующему молча — как чужое наследство.
        /// </summary>
        [Test]
        public void LeavingTakesTheSeatingAway()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            INetTransport guest = net.CreateNode();
            net.PollAll();
            roster.Seat(1, 1);

            guest.Shutdown();
            net.PollAll();

            net.CreateNode();   // на освободившееся место входит другой человек
            net.PollAll();

            // Номер мог и не переиспользоваться — это транспорту решать. Проверяем не «кто вошёл», а
            // что чужого назначения на этом номере не осталось: досталось бы оно молча.
            bool inherited = roster.TryGet(1, out SessionPlayer newcomer) && newcomer.Team != 0;
            Assert.IsFalse(inherited, "новичок садится по умолчанию, а не по чужому назначению");
        }

        /// <summary>Сажать некого — не повод падать: команда консоли зовётся вслепую.</summary>
        [Test]
        public void SeatingSomeoneWhoIsNotHere_DoesNothing()
        {
            var net = new LoopbackNetwork();
            INetTransport hostNode = net.CreateNode();

            var roster = new HostSessionRoster(hostNode, null, null, null);
            roster.Start();

            Assert.DoesNotThrow(() => roster.Seat(42, 1));
            Assert.AreEqual(1, roster.Players.Count, "состав не тронут");
        }
    }
}
