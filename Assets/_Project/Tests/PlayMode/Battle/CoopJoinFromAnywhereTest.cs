using System;
using System.Collections;
using Guildmaster.Core.Net;
using Guildmaster.Data;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Session;
using Guildmaster.Net.Session;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Приглашение принимается ОТКУДА УГОДНО: из меню, из своей поднятой сессии, из открытого места.
    /// </summary>
    /// <remarks>
    /// <b>Требование Макса 04.08.2026:</b> «Нужно чтобы „присоединиться“ корректно работало у игрока
    /// где бы он не находило». Механика для этого есть — <c>CoopJoinInterrupt</c> рвёт своё и уходит в
    /// гости, — но проверялась она только вживую вдвоём: программно принять приглашение было нечем,
    /// пока комната не переехала за шов.
    ///
    /// <para><b>Что здесь легко проверить неправильно.</b> Соблазн — убедиться, что «сессия стала
    /// гостевой». Этого мало: если своё при этом не свернулось, игрок окажется в чужой игре со своим
    /// мероприятием на экране, и выглядеть это будет как угодно, только не как баг приглашения.
    /// Поэтому проверяются ОБА конца: чужая сессия поднялась и своё место закрылось.</para>
    /// </remarks>
    public sealed class CoopJoinFromAnywhereTest
    {
        private const float BootTimeout = 20f;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator JoinFromOwnHostedSession_DropsOurOwnAndGoesVisiting()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var coop = root.Container.Resolve<ICoopSessionControl>();

            // Мы уже принимаем гостей у себя — самое неудобное состояние для приглашения.
            Assert.IsTrue(coop.StartHost(), "своя сессия не поднялась");
            Assert.AreEqual(CoopSessionState.Hosting, coop.State, "своя сессия поднялась не в то состояние");

            Host visitor = SetUpVisitedHost(root);
            visitor.Lobby.SimulateInvite();

            yield return WaitUntil(() => coop.State == CoopSessionState.Connected, visitor.Wire, seconds: 8f);

            Assert.AreEqual(CoopSessionState.Connected, coop.State,
                "приглашение из своей поднятой сессии не увело в гости — игрок остался у себя");

            visitor.Dispose();
        }

        [UnityTest]
        public IEnumerator JoinWhileAPlaceIsOpen_ClosesThePlace()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var coop = root.Container.Resolve<ICoopSessionControl>();
            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();
            var view       = root.Container.Resolve<IActivityView>();

            // Своя игра идёт, место открыто — уходить придётся прямо отсюда.
            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(4);
            Assert.AreEqual(ActivityKind.ProvingGrounds, view.Current.Kind, "место не открылось — проверять нечего");

            Host visitor = SetUpVisitedHost(root);
            visitor.Lobby.SimulateInvite();

            yield return WaitUntil(() => coop.State == CoopSessionState.Connected
                                         && view.Current.Kind == ActivityKind.None,
                                   visitor.Wire, seconds: 8f);

            Assert.AreEqual(CoopSessionState.Connected, coop.State,
                "приглашение из открытого места не увело в гости");
            Assert.AreEqual(ActivityKind.None, view.Current.Kind,
                "ушли в гости, но своё место осталось на экране — игрок увидит чужую игру поверх своей");

            visitor.Dispose();
        }

        // ── тот, к кому идём ─────────────────────────────────────────────────

        /// <summary>Хозяин на другом конце провода: его узел, его половина рукопожатия и его комната.</summary>
        private sealed class Host : IDisposable
        {
            public INetTransport  Wire;
            public CoopHandshake  Handshake;
            public LoopbackLobby  Lobby;

            public void Dispose() => Handshake?.Dispose();
        }

        private static Host SetUpVisitedHost(RootLifetimeScope root)
        {
            INetTransport wire = root.Container.Resolve<LoopbackNetwork>().CreateNode(claimHost: true);

            var lobby = root.Container.Resolve<ICoopLobby>() as LoopbackLobby;
            Assert.IsNotNull(lobby, "в автоматическом прогоне комната обязана быть петлевой");

            // Отпечаток берём У ИГРЫ: рукопожатие сверяет контент, и свой дал бы отказ вместо приёма.
            return new Host
            {
                Wire      = wire,
                Lobby     = lobby,
                Handshake = new CoopHandshake(wire, root.Container.Resolve<ContentFingerprint>()),
            };
        }

        // ── помощники ────────────────────────────────────────────────────────

        private static IEnumerator WaitUntil(Func<bool> done, INetTransport hostWire, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
                hostWire.Poll();
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
