using System;
using System.Collections;
using Guildmaster.Core.Net;
using Guildmaster.Game;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Разрыв связи глазами того, кто остался: напарник отвалился — хозяин обязан УЗНАТЬ об этом.
    /// </summary>
    /// <remarks>
    /// <b>Почему именно этот сценарий первым.</b> Уход гостя не порождал у хозяина ничего: он
    /// оставался в игре, не зная, что остался один (наход. Макса 04.08.2026). Диалог мы завели, но
    /// проверяли его только вживую вдвоём — то есть ровно тем способом, который и пропустил дефект.
    ///
    /// <para><b>Сессия поднимается настоящая.</b> Подъём и вход живут в шве транспорта с 05.08.2026,
    /// поэтому <c>StartHost</c> работает и на петле — сеанс не отличает её от Steam. Пока шва не было,
    /// сеанс держал Steam-транспорт конкретным типом, и подключить к нему что-либо в тесте было
    /// нечем.</para>
    ///
    /// <para><b>Чего здесь ПОКА нет:</b> зеркального «хозяин ушёл» глазами гостя. Гость входит только
    /// по приглашению Steam — входа по адресу у нас нет намеренно (решение Макса 02.08.2026), — и
    /// чтобы отыграть это на петле, шов нужен ещё и лобби.</para>
    /// </remarks>
    public sealed class CoopDisconnectScenarioTest
    {
        private const float BootTimeout = 20f;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator HostLearnsThePartnerLeft()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "корневой скоуп не поднялся");

            var coop = root.Container.Resolve<ICoopSessionControl>();
            Assert.IsTrue(coop.StartHost(), "сессия не поднялась даже на петле — смотри лог транспорта");

            // Хозяин узнаёт об уходе ТОЛЬКО если он хозяин и по мнению транспорта тоже: уход соседа
            // разбирается по этому признаку. Проверяем отдельно, иначе провал ниже читался бы как
            // «событие потерялось», а не «мы вообще не хозяин».
            var transport = root.Container.Resolve<INetTransport>();
            Assert.IsTrue(transport.IsHost,
                $"игра не хозяин в петле (её номер {transport.LocalPeerId}) — уход напарника до неё не дойдёт");

            int left = NetPeer.NoPeer;
            coop.PeerLeft += peer => left = peer;

            // Отдельно слушаем сам транспорт: если уход дойдёт сюда, но не до сеанса — сломан сеанс, а
            // не петля. Разделять их обязательно, иначе провал читается как «что-то где-то потерялось».
            int leftOnWire = NetPeer.NoPeer;
            transport.PeerDisconnected += peer => leftOnWire = peer;

            // Напарник приходит и уходит. В петле соединение объявляется в момент создания узла,
            // поэтому «подключился» доедет до игры на ближайшем опросе транспорта.
            INetTransport partner = root.Container.Resolve<LoopbackNetwork>().CreateNode();
            yield return WaitFrames(3);

            partner.Shutdown();

            yield return WaitUntil(() => left != NetPeer.NoPeer, seconds: 5f);

            Assert.AreNotEqual(NetPeer.NoPeer, leftOnWire,
                "уход напарника не дошёл даже до транспорта — петля не уведомила о разрыве");
            Assert.AreNotEqual(NetPeer.NoPeer, left,
                "напарник отвалился, а хозяин об этом не узнал — он останется в игре один и без диалога");

            coop.Leave();
            yield return WaitFrames(2);
        }

        [UnityTest]
        public IEnumerator LeavingEndsTheSessionWithOurOwnReason()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var coop = root.Container.Resolve<ICoopSessionControl>();

            Assert.IsTrue(coop.StartHost(), "сессия не поднялась");
            Assert.AreNotEqual(CoopSessionState.Offline, coop.State, "сессия поднялась, но состояние не сменилось");

            coop.Leave();
            yield return WaitFrames(2);

            // Причина разрыва — то, по чему игрок читает, что случилось. Свой выход обязан отличаться
            // от чужого ухода: иначе вышедшему сам себе показался бы диалог «напарник отключился».
            Assert.AreEqual(CoopSessionState.Offline, coop.State, "вышли, а сессия жива");
            Assert.AreEqual(CoopEndReason.LocalRequest, coop.EndReason,
                "свой выход записан чужой причиной — игрок увидит не тот экран");
        }

        [UnityTest]
        public IEnumerator GuestLearnsTheHostLeft()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var coop = root.Container.Resolve<ICoopSessionControl>();

            // Хозяина изображает тест — вместе с его половиной рукопожатия. Отпечаток берём У ИГРЫ:
            // рукопожатие сверяет контент, и свой, посчитанный иначе, дал бы отказ вместо приёма.
            INetTransport hostWire = root.Container.Resolve<LoopbackNetwork>().CreateNode(claimHost: true);
            var hostHandshake = new Guildmaster.Net.Session.CoopHandshake(
                hostWire, root.Container.Resolve<Guildmaster.Data.ContentFingerprint>());

            // Приглашение приходит тем же событием, каким его приносит платформа.
            var lobby = root.Container.Resolve<Guildmaster.Net.Session.ICoopLobby>()
                        as Guildmaster.Net.Session.LoopbackLobby;
            Assert.IsNotNull(lobby, "в автоматическом прогоне комната обязана быть петлевой");
            lobby.SimulateInvite();

            yield return WaitUntil(() => coop.State == CoopSessionState.Connected, hostWire, seconds: 8f);
            Assert.AreEqual(CoopSessionState.Connected, coop.State,
                "гость не дошёл до «в сессии»: рукопожатие не состоялось");

            // Хозяин уходит. Для гостя это конец сессии — миграции авторитета у нас нет намеренно.
            hostWire.Shutdown();

            yield return WaitUntil(() => coop.State == CoopSessionState.Offline, hostWire, seconds: 5f);

            Assert.AreEqual(CoopSessionState.Offline, coop.State, "хозяин ушёл, а сессия у гостя жива");
            Assert.AreEqual(CoopEndReason.HostLeft, coop.EndReason,
                "уход хозяина записан не той причиной — гость увидит не тот экран");

            hostHandshake.Dispose();
        }

        [UnityTest]
        public IEnumerator HostVanishingDuringTheHandshake_DoesNotLeaveUsHanging()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var coop = root.Container.Resolve<ICoopSessionControl>();

            // Хозяин есть — но отвечать на «привет» не станет: он исчезает ровно в тот момент, когда
            // гость представился. Самая неудобная точка обрыва: сессия уже не «оффлайн», но ещё и не
            // «в игре».
            INetTransport hostWire = root.Container.Resolve<LoopbackNetwork>().CreateNode(claimHost: true);

            var lobby = root.Container.Resolve<Guildmaster.Net.Session.ICoopLobby>()
                        as Guildmaster.Net.Session.LoopbackLobby;
            Assert.IsNotNull(lobby, "в автоматическом прогоне комната обязана быть петлевой");
            lobby.SimulateInvite();

            yield return WaitUntil(() => coop.State == CoopSessionState.Connecting, hostWire, seconds: 5f);
            Assert.AreEqual(CoopSessionState.Connecting, coop.State, "гость даже не начал подключаться");

            hostWire.Shutdown();

            yield return WaitUntil(() => coop.State == CoopSessionState.Offline, hostWire, seconds: 8f);

            // Главное здесь — НЕ зависнуть. Гость, оставшийся в «подключаюсь» навсегда, не увидит ни
            // игры, ни объяснения: для него это игра, которая ничего не делает.
            Assert.AreEqual(CoopSessionState.Offline, coop.State,
                "гость завис на подключении — для него это игра, которая просто ничего не делает");
            Assert.AreEqual(CoopEndReason.ConnectionFailed, coop.EndReason,
                "обрыв на рукопожатии записан как уход хозяина — игрок решит, что его выгнали из готовой игры");
        }

        // ── помощники ────────────────────────────────────────────────────────

        /// <summary>Ждать условия, качая сторону хозяина: её узел ничей, его никто не тикает.</summary>
        private static IEnumerator WaitUntil(Func<bool> done, INetTransport hostWire, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break;
                hostWire.Poll();
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
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
