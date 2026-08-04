using System;
using System.Collections;
using System.Collections.Generic;
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

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Сценарий раздачи: игра открывает место как хозяин сеанса, а на другом конце провода сидит
    /// напарник — и проверяется, что до него доехало всё, что обещано реестром возможностей.
    /// </summary>
    /// <remarks>
    /// <b>Чем это отличается от EditMode-тестов раздачи.</b> Те собирают вещателя, ленту и приёмник
    /// руками и проверяют, что кодек не врёт. Здесь никто ничего не собирает: игра поднимается целиком,
    /// место открывается тем же вызовом, что у игрока, и сообщения уходят настоящей разводкой. Именно
    /// эта разница ловит поломки, которых EditMode не видит: объект, который не создался; вещатель,
    /// которого не зарегистрировали; событие, на которое никто не подписался.
    ///
    /// <para><b>Почему проверяем ИМЕННО каналы, а не «нет ошибок».</b> Каждая строка ниже — строка из
    /// <c>docs/player-capability-registry.md</c>: «кто на арене» едет паспортами, «где мы» — состоянием
    /// мероприятия, тела на арене вне боя — кадром покоя ленты. Пропажа любого из трёх уже случалась
    /// вживую и каждый раз выглядела для игрока как «подключился и никого не вижу».</para>
    ///
    /// <para><b>Провод настоящий.</b> Steam в тестах не поднимается, и сеть работает петлёй в своём
    /// процессе (см. <c>RootLifetimeScope</c>) — тест просто заводит на ней второй узел. Роль напарника
    /// исполняет он: полноценную вторую игру в одном процессе не поднять, у неё был бы общий с первой
    /// мир и общая камера.</para>
    /// </remarks>
    public sealed class CoopHandoutScenarioTest
    {
        private const float BootTimeout = 20f;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator ProvingGrounds_HandsOutThePlaceTheRosterAndTheStage()
        {
            LogAssert.ignoreFailingMessages = true; // SetUp фреймворк перетирает между тестами
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "корневой скоуп не поднялся");

            // Второй конец провода. Заводим ДО открытия места: паспорта уходят событием спавна, и
            // подключившийся позже их уже не застанет — ровно этим гость и оставался без состава,
            // пока хост не научился отвечать на вопрос «кто на арене» (04.08.2026).
            var network = root.Container.Resolve<LoopbackNetwork>();
            INetTransport partner = network.CreateNode();
            var heard = new ChannelTally(partner);

            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);

            activities.Open(ActivitySetup.ProvingGrounds);

            // Ждём не «сколько-нибудь кадров», а появления обещанного: кадр покоя уходит десять раз в
            // секунду, и жёсткое число кадров сделало бы тест плавающим на медленной машине.
            yield return WaitUntil(() => heard.Got(NetChannel.ActivityState)
                                      && heard.Got(NetChannel.BattleRoster)
                                      && heard.Got(NetChannel.TapeChunk),
                                   partner, seconds: 8f);

            Assert.IsTrue(heard.Got(NetChannel.ActivityState),
                "напарнику не сказали, ГДЕ мы — он не откроет ни арену, ни карту");
            Assert.IsTrue(heard.Got(NetChannel.BattleRoster),
                "напарнику не сказали, КТО на арене — кадры приедут, а рисовать будет нечем");
            Assert.IsTrue(heard.Got(NetChannel.TapeChunk),
                "напарнику не поехала лента — арена в покое тоже везётся ею, кадром покоя");

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        /// <summary>Что вообще доехало до второго конца провода, по каналам.</summary>
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
        /// Качать кадры игры и провод, пока условие не выполнится. Провод качаем САМИ: петля доставляет
        /// сообщения только в <c>Poll</c>, а этот узел ничей — его никто не тикает.
        /// </summary>
        private static IEnumerator WaitUntil(Func<bool> done, INetTransport partner, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert, не таймаут
                partner.Poll();
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
