using System;
using System.Collections;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Net;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Session;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Гость глазами игрока: что он ВИДИТ и что он МОЖЕТ, когда хозяин объявил место.
    /// </summary>
    /// <remarks>
    /// <b>Здесь гостем работает сама игра</b>, а хозяина изображает тест на другом конце провода. Это
    /// обратная сторона <see cref="CoopHandoutScenarioTest"/>, и именно она ловит то, что дошло до
    /// живого прогона 05.08.2026: боевой скоуп у гостя не поднимался вовсе, и он «подключился, но
    /// никого не видел». Раздача при этом работала — проверять надо обе стороны.
    ///
    /// <para><b>Номер хоста тест забирает себе</b> (<c>CreateNode(claimHost: true)</c>): игра поднимает
    /// транспорт на старте, задолго до того, как выяснится роль сеанса, и иначе её же «шлём хосту»
    /// уходило бы ей самой.</para>
    ///
    /// <para><b>Проверки — строки реестра возможностей, а не «нет исключений».</b> Место открылось;
    /// шов «кто на арене» отвечает; согласие гостя доезжает до хозяина. Каждая из трёх ломалась
    /// вживую, и каждый раз выглядела как отдельный баг.</para>
    /// </remarks>
    public sealed class GuestFollowsTheHostTest
    {
        private const float BootTimeout = 20f;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator Guest_OpensThePlaceTheHostAnnounced()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "корневой скоуп не поднялся");

            var network = root.Container.Resolve<LoopbackNetwork>();
            INetTransport host = network.CreateNode(claimHost: true);

            var sessions = root.Container.Resolve<SessionHost>();
            sessions.Open(SessionRole.Guest);
            yield return WaitFrames(2);

            // «Где мы»: Ристалище с открытой ареной в расстановке — то самое сообщение, после которого
            // у гостя обязан подняться боевой скоуп.
            SendActivity(host, new ActivityState(
                ActivityKind.ProvingGrounds, hideOpponent: false, ownUnitsOnly: false,
                battleOpen: true, phase: BattlePhase.Deployment));

            LifetimeScope combat = null;
            yield return WaitUntil(() => (combat = LifetimeScope.Find<CombatLifetimeScope>()) != null
                                         && combat.Container != null,
                                   host, seconds: 5f);

            Assert.IsNotNull(combat, "гость не открыл место, которое объявил хозяин");
            Assert.IsNotNull(combat.Container,
                "боевой скоуп гостя не собрался — смотри в консоли, какой регистрации не хватило");

            // Шов «кто на арене» обязан не только разрешиться, но и ОТВЕТИТЬ: на нём стоят круги-опоры
            // и выбор бойца под курсором, то есть руки игрока целиком.
            var arena = combat.Container.Resolve<IArenaUnits>();
            Assert.IsNotNull(arena, "у гостя нет шва «кто на арене» — не будет ни кругов, ни драга");
            Assert.DoesNotThrow(() => { var _ = arena.Units; }, "шов «кто на арене» у гостя не отвечает");

            sessions.Close();
            yield return WaitFrames(2);
        }

        [UnityTest]
        public IEnumerator Guest_ConsentReachesTheHost()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var network = root.Container.Resolve<LoopbackNetwork>();
            INetTransport host = network.CreateNode(claimHost: true);
            var heard = new ChannelTally(host);

            var sessions = root.Container.Resolve<SessionHost>();
            sessions.Open(SessionRole.Guest);
            yield return WaitFrames(2);

            SendActivity(host, new ActivityState(
                ActivityKind.ProvingGrounds, hideOpponent: false, ownUnitsOnly: false,
                battleOpen: true, phase: BattlePhase.Deployment));

            // Ждём, пока фаза доедет и гейт получит ключ: до этого «готов» нажимать не на что.
            IReadyGate gate = null;
            yield return WaitUntil(() => (gate = FindInSession<IReadyGate>()) != null
                                         && LifetimeScope.Find<CombatLifetimeScope>() != null,
                                   host, seconds: 5f);
            Assert.IsNotNull(gate, "у гостя нет общего согласия — кнопке «Начать» не на чём стоять");

            gate.ToggleLocal();

            yield return WaitUntil(() => heard.Got(NetChannel.ReadyGate), host, seconds: 3f);

            // Кнопка, живая на вид и мёртвая на деле, у нас была дважды — поэтому проверяется не
            // локальный флаг, а факт: согласие ДОЕХАЛО до того, кто решает.
            Assert.IsTrue(heard.Got(NetChannel.ReadyGate),
                "гость подтвердил готовность, а до хозяина это не доехало — кнопка мёртвая");

            sessions.Close();
            yield return WaitFrames(2);
        }

        // ── помощники ────────────────────────────────────────────────────────

        private static void SendActivity(INetTransport host, in ActivityState state)
        {
            var writer = new NetByteWriter(16);
            byte[] envelope = null;
            ArraySegment<byte> payload = ActivityStateCodec.Write(state, writer);
            host.SendToAll(NetEnvelope.Wrap(NetChannel.ActivityState, payload, ref envelope),
                           NetDelivery.Reliable);
        }

        /// <summary>
        /// Достать что-нибудь из скоупа сеанса. Своего входа у сеанса нет намеренно — он не витрина, —
        /// поэтому тест ищет скоуп в сцене и спрашивает его контейнер напрямую.
        /// </summary>
        private static T FindInSession<T>() where T : class
        {
            LifetimeScope[] scopes = UnityEngine.Object.FindObjectsByType<LifetimeScope>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < scopes.Length; i++)
            {
                if (scopes[i] == null || scopes[i].Container == null) continue;
                if (scopes[i].Container.TryResolve(out T value)) return value;
            }
            return null;
        }

        private sealed class ChannelTally
        {
            private readonly HashSet<NetChannel> _seen = new HashSet<NetChannel>();

            public ChannelTally(INetTransport transport) => transport.MessageReceived += Handle;

            public bool Got(NetChannel channel) => _seen.Contains(channel);

            private void Handle(int from, ArraySegment<byte> message)
            {
                if (NetEnvelope.TryUnwrap(message, out NetChannel channel, out _)) _seen.Add(channel);
            }
        }

        /// <summary>
        /// Качать кадры игры и провод, пока условие не выполнится. Провод качаем сами: петля доставляет
        /// только в <c>Poll</c>, а этот узел ничей.
        /// </summary>
        private static IEnumerator WaitUntil(Func<bool> done, INetTransport host, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
                host.Poll();
                yield return null;
            }
        }

        private static IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync("CoreScene", LoadSceneMode.Single);

            float deadline = Time.realtimeSinceStartup + BootTimeout;
            while (UnityEngine.Object.FindAnyObjectByType<WorldLifetimeScope>() == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"мир не поднялся за {BootTimeout} с — бут сломан");
                yield return null;
            }

            yield return WaitFrames(2);
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
    }
}
