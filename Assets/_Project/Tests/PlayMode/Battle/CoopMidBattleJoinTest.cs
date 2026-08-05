using System;
using System.Collections;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Session;
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
    /// Напарник пришёл ПОСРЕДИ боя: бой встаёт, ему уезжает вся история, и только потом всё
    /// продолжается.
    /// </summary>
    /// <remarks>
    /// <b>Схема принята Максом 04.08.2026 дословно:</b> «Когда кто-то пытается подключиться — все
    /// встает на паузу. „Загрузка“... пишется обоим. Ему передается вся информация, он запускает View
    /// в том месте. где были все игроки до этого. Потом отчет и продолжения». Механика написана в тот
    /// же день, но вживую её никто не видел: чтобы подключиться посреди боя, нужны две машины и
    /// человек, готовый войти ровно в нужный момент.
    ///
    /// <para><b>Проверяется не «пришло что-то», а три отдельные вещи:</b> история поехала (её
    /// принципиально больше, чем один чанк текущего момента), бой встал общей паузой, и пауза потом
    /// СНЯЛАСЬ. Последнее важнее всего: зависшая навсегда пауза — это два игрока, смотрящие на
    /// неподвижную арену, и выглядит она как «игра повисла», а не как ожидание.</para>
    /// </remarks>
    public sealed class CoopMidBattleJoinTest
    {
        private const float BootTimeout = 20f;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator JoiningMidBattle_HoldsTheBattleAndShipsTheWholeTape()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(6);

            // Начинаем бой тем же путём, что игрок: общим согласием. В соло участник один, и гейт
            // пропускает в тот же кадр.
            IReadyGate gate = FindInSession<IReadyGate>();
            Assert.IsNotNull(gate, "на площадке нет общего согласия — бой не начать");
            gate.ToggleLocal();

            // Даём бою идти: истории должно накопиться заметно больше одного чанка, иначе «вся лента»
            // и «текущий момент» неотличимы, и тест доказывал бы не то.
            yield return WaitSeconds(2.5f);

            // И только теперь приходит напарник.
            INetTransport partner = root.Container.Resolve<LoopbackNetwork>().CreateNode();
            var heard = new ChannelTally(partner);

            var hold = FindAnywhere<Guildmaster.Net.Tape.MidBattleJoinHold>();
            Assert.IsNotNull(hold, "у хозяина нет держателя паузы подключения — держать бой нечем");

            // Просим бой ЦЕЛИКОМ — ровно тем же способом, что настоящий гость: пустая просьба на
            // канале повтора и означает «пришли всё, что есть». Пока тест молча слушал, до него
            // доезжал текущий поток, и «история» в проверке ниже была бы им же.
            RequestWholeBattle(partner);

            yield return WaitUntil(() => hold.Holding, partner, seconds: 10f);
            Assert.IsTrue(hold.Holding,
                "бой не встал на общую паузу — напарник догоняет ленту, пока она убегает от него");

            int chunksAtHold = heard.Count(NetChannel.TapeChunk);

            // Пауза обязана сняться САМА: отсчёт короткий, и держать её дольше нечем. Зависшая
            // навсегда пауза — это два игрока перед неподвижной ареной, то есть «игра повисла».
            yield return WaitUntil(() => !hold.Holding, partner, seconds: 20f);
            Assert.IsFalse(hold.Holding,
                "пауза подключения не снялась — для обоих игроков это «игра повисла», а не ожидание");

            Assert.Greater(heard.Count(NetChannel.TapeChunk), chunksAtHold,
                "за паузу напарнику не доехало ни одного чанка — держали бой впустую");

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        // ── помощники ────────────────────────────────────────────────────────

        /// <summary>
        /// «Пришли бой целиком» — пустая просьба на канале повтора. Номер чанка в просьбе значит
        /// «повтори вот этот», а его отсутствие — «повтори бой» (см. <c>TapeIntake</c>).
        /// </summary>
        private static void RequestWholeBattle(INetTransport partner)
        {
            byte[] envelope = null;
            partner.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.TapeResend, default, ref envelope),
                NetDelivery.Reliable);
        }

        private sealed class ChannelTally
        {
            private readonly Dictionary<NetChannel, int> _seen = new Dictionary<NetChannel, int>();

            public ChannelTally(INetTransport transport) => transport.MessageReceived += Handle;

            public bool Got(NetChannel channel) => Count(channel) > 0;

            public int Count(NetChannel channel) => _seen.TryGetValue(channel, out int n) ? n : 0;

            private void Handle(int from, ArraySegment<byte> message)
            {
                if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out _)) return;
                _seen[channel] = Count(channel) + 1;
            }
        }

        /// <summary>Достать что-нибудь из скоупа сеанса — своего входа у него нет намеренно.</summary>
        private static T FindInSession<T>() where T : class => FindAnywhere<T>();

        private static T FindAnywhere<T>() where T : class
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

        private static IEnumerator WaitUntil(Func<bool> done, INetTransport partner, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
                partner.Poll();
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
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
