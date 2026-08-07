using Guildmaster.Core.Audio;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Session;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Состав скоупа Сессии по роли. Инвариант живёт между инсталлером и всеми, кто пишет забег,
    /// поэтому он в тесте, а не в комментарии: комментарий виден одной стороне шва, а нарушит его
    /// вторая — тот, кто однажды зарегистрирует держателя состояния в обход роли.
    /// </summary>
    public sealed class SessionScopeTests
    {
        /// <summary>
        /// Владелец сейва держит забег сам и пишет его сам — иначе главному меню нечего спрашивать про
        /// «Продолжить», а узлам некуда сохраняться.
        /// </summary>
        [Test]
        public void Owner_HoldsTheRunAndTheCommandBus()
        {
            using IObjectResolver session = BuildSession(SessionRole.Owner);

            Assert.IsTrue(session.TryResolve(out RunStateService _), "у владельца нет держателя забега");
            Assert.IsTrue(session.TryResolve(out RunCommandBus _), "у владельца нет шины команд забега");
            Assert.AreEqual(SessionRole.Owner, session.Resolve<SessionContext>().Role);
            Assert.IsTrue(session.Resolve<SessionContext>().IsOwner);

            // Наружу сеанс отдаёт себя через два узких имени — по ним его спрашивают роутеры.
            Assert.IsInstanceOf<RunStateService>(session.Resolve<ISessionRunState>(),
                "у владельца забег держит он сам");
            Assert.IsInstanceOf<RunCommandBus>(session.Resolve<ISessionRunCommands>(),
                "и команды применяет локально");
        }

        /// <summary>
        /// У гостя сервиса, умеющего писать сейв, НЕ СУЩЕСТВУЕТ. Это сильнее любой проверки «а мы точно
        /// хост?»: гость играет в чужом состоянии, и то, чего нет в контейнере, нельзя случайно позвать
        /// ни в кооп-ветке, ни в общей.
        /// </summary>
        [Test]
        public void Guest_HasNothingThatCanWriteTheSave()
        {
            using IObjectResolver session = BuildSession(SessionRole.Guest);

            Assert.IsFalse(session.TryResolve(out RunStateService _),
                "гость получил держателя забега — он умеет писать чужой сейв");
            Assert.IsFalse(session.TryResolve(out RunCommandBus _),
                "гость получил шину команд — он применяет их у себя вместо хоста");
            Assert.IsFalse(session.TryResolve(out RunCommandLog _),
                "гость получил лог команд — значит где-то предполагается, что он их применяет");
            Assert.AreEqual(SessionRole.Guest, session.Resolve<SessionContext>().Role);
            Assert.IsFalse(session.Resolve<SessionContext>().IsOwner);

            // Читать забег и просить изменения он всё же умеет — иначе играть было бы нечем. Разница в
            // том, ЧЕМ он это делает: приёмником снимков и отправителем интентов.
            Assert.IsInstanceOf<Guildmaster.Game.Session.Net.GuestRunState>(
                session.Resolve<ISessionRunState>(), "гостю забег приезжает снимком");
            Assert.IsInstanceOf<Guildmaster.Game.Session.Net.RemoteRunCommands>(
                session.Resolve<ISessionRunCommands>(), "а изменения он просит сделать хоста");
        }

        /// <summary>
        /// Читатель-долгожитель видит забег ровно тогда, когда сеанс открыт, и не держит ссылку на его
        /// содержимое. Вне сессии ответ «забега нет» — это факт, а не отказ.
        /// </summary>
        [Test]
        public void RunRouter_AnswersNothingWithoutASession()
        {
            var host = new SessionHost(); // не открывали: мира в EditMode нет и не нужно
            IRunStateView view = new SessionRunRouter(host);

            Assert.IsFalse(host.IsOpen);
            Assert.IsNull(view.Current);
            Assert.IsNull(host.Run);
            Assert.IsNull(host.Commands);
            Assert.IsNull(host.Context);
        }

        /// <summary>
        /// Писать в забег можно всегда, даже когда забега нет: тип записи существует независимо от
        /// сеанса, а ответом на «некуда» служит <c>false</c>. Без этого дев-арена, Ристалище и PvP
        /// роняли бы контейнер на подъёме — им шина не нужна, но зависимость никуда не девалась.
        /// </summary>
        [Test]
        public void CommandRouter_AnswersNotWrittenWithoutASession()
        {
            IRunCommands commands = new SessionCommandRouter(new SessionHost());

            Assert.IsFalse(commands.SetSlotPosition(0, Vector2.zero));
            Assert.IsFalse(commands.SetSlotRelic(0, "relic.any"));
            Assert.IsFalse(commands.RequestSave());
            Assert.DoesNotThrow(() => commands.AddGold(10));
            Assert.DoesNotThrow(() => commands.AwardBattleReward());
        }

        /// <summary>
        /// Бой собирается БЕЗ забега — это и есть проверка правильности реза уровней: боевой скоуп
        /// заказывают четверо (узел карты, Ристалище, PvP, тест), и трое из них живут без сейва.
        /// </summary>
        /// <remarks>
        /// Инвариант живёт между скоупом боя и составом сессии, поэтому он в тесте: комментарий увидел
        /// бы только тот, кто и так собирался его соблюсти, а нарушит его тот, кто однажды попросит
        /// в конструкторе держателя состояния — и узнает об этом лишь падением контейнера в игре.
        /// <para>Список типов явный: боевой скоуп собирается из префаба, и вывести его состав
        /// рефлексией нельзя. Добавил боевой сервис, который трогает забег, — допиши сюда.</para>
        /// </remarks>
        [Test]
        public void BattleScope_NeverAsksForTheOwnerHalf()
        {
            System.Type[] battleTypes =
            {
                typeof(Guildmaster.Game.Flow.BattleStartup),
                typeof(Guildmaster.Game.DeploymentController),
                typeof(Guildmaster.Game.Flow.BattleHost), // живёт в мероприятии, но заказывает бой и без забега
            };

            foreach (System.Type type in battleTypes)
            foreach (System.Reflection.ConstructorInfo ctor in type.GetConstructors())
            foreach (System.Reflection.ParameterInfo p in ctor.GetParameters())
            {
                Assert.AreNotEqual(typeof(RunStateService), p.ParameterType,
                    $"{type.Name} просит держателя состояния — без владельца сейва такой скоуп не поднимется");
                Assert.AreNotEqual(typeof(RunCommandBus), p.ParameterType,
                    $"{type.Name} просит шину напрямую — мимо роутера, а значит мимо ответа «писать некуда»");
            }
        }

        /// <summary>
        /// Собрать контейнер сессии в отрыве от Unity-скоупов: инсталлер — обычный
        /// <see cref="IInstaller"/>, и состав по роли проверяется без сцены.
        /// </summary>
        /// <remarks>
        /// Зависимости, которые в игре приходят из предков (сейв, профиль, конфиг, звук), кладём сюда
        /// заглушками: VContainer проверяет разрешимость всей ветки на <c>Build</c>, а дефолтные
        /// значения параметров конструктора он не подставляет.
        /// </remarks>
        /// <summary>Указатель без камеры: в EditMode мира нет, и присутствию нечего отправлять.</summary>
        private sealed class TestPointer : Guildmaster.Core.Input.IPointerWorld
        {
            public UnityEngine.Vector2 Position   => UnityEngine.Vector2.zero;
            public bool                IsAvailable => false;
        }

        /// <summary>Платформа без платформы: имя есть, Steam-а нет.</summary>
        private sealed class TestPlatform : Guildmaster.Core.Players.IPlatformIdentity
        {
            public string PlayerName => "Игрок";
        }

        private static IObjectResolver BuildSession(SessionRole role)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance<ISaveService>(new InMemorySaveService());
            builder.RegisterInstance<IProfileService>(new FixedProfileService());
            builder.RegisterInstance(ScriptableObject.CreateInstance<GameConfig>());
            builder.RegisterInstance<IAudioService>(new SilentAudio());

            // Личность платформы: сеанс представляет игрока по имени, и в EditMode Steam-а нет. Шов
            // узкий ровно затем, чтобы подменяться одной строкой.
            builder.RegisterInstance<Guildmaster.Core.Players.IPlatformIdentity>(new TestPlatform());

            // Указатель в мире: присутствие спрашивает, где курсор. В EditMode камеры нет, и шов честно
            // отвечает «недоступен» — курсор просто не отправляется.
            builder.RegisterInstance<Guildmaster.Core.Input.IPointerWorld>(new TestPointer());

            // Транспорт и владелец мероприятий приходят из предков: раздача состояния и объявление
            // «где мы» живут в сеансе, потому что это обязанность роли.
            builder.RegisterInstance<Guildmaster.Net.Transport.INetTransport>(
                new Guildmaster.Net.Transport.LoopbackNetwork().CreateNode());
            builder.RegisterInstance(new Guildmaster.Game.Activity.ActivityHost(new SessionHost(), null));

            // Показ мира — тоже из предков, и приходит он двумя УЗКИМИ швами. Это и есть причина, по
            // которой швы завелись: без них сеанс требовал бы здесь весь показ карты и сборщик тел со
            // сценой мира, то есть состав сеанса нельзя было бы проверить в EditMode вовсе.
            builder.RegisterInstance<Guildmaster.Game.Flow.IPartyStage>(new SilentStage());
            builder.RegisterInstance<Guildmaster.Game.Flow.IActMapPresence>(new SilentMap());
            builder.RegisterInstance<Guildmaster.Core.Flow.IHubPresence>(new SilentHub());

            // Гостевая половина ещё и ПОКАЗЫВАЕТ итог боя, поэтому просит паблишер экрана, ленту боя,
            // свою сторону и выход из чужой сессии. Всё это приходит из предков и здесь заглушается: тест
            // проверяет СОСТАВ сеанса по роли, а не показ.
            builder.RegisterInstance<MessagePipe.IPublisher<Guildmaster.Guild.OpenOutcomeRequest>>(
                new SilentOutcomePublisher());
            builder.RegisterInstance<MessagePipe.ISubscriber<Guildmaster.Presentation.BattleEndedEvent>>(
                new SilentBattleEnded());
            builder.RegisterInstance<Guildmaster.Core.Net.ICoopSessionControl>(new SilentCoop());
            builder.RegisterInstance<Guildmaster.Core.Players.ILocalPlayer>(new TestLocalPlayer());

            // Шина корня. Заглушкой, а не настоящим MessagePipe: сеанс только ПУБЛИКУЕТ «забег
            // начался», и поднимать ради этого весь брокер значило бы проверять не состав сеанса.
            builder.RegisterInstance<MessagePipe.IPublisher<Guildmaster.Game.Flow.RunPartyReadyEvent>>(
                new SilentPublisher());
            builder.RegisterInstance<MessagePipe.IPublisher<Guildmaster.Core.Net.ReadyGateChangedEvent>>(
                new SilentReadyPublisher());

            new SessionInstaller(role).Install(builder);
            return builder.Build();
        }

        private sealed class SilentStage : Guildmaster.Game.Flow.IPartyStage
        {
            public void PlaceParty() { }
        }

        private sealed class SilentMap : Guildmaster.Game.Flow.IActMapPresence
        {
            public bool IsShown => false;
            public void SetVisible(bool visible) { }
            public void Refresh() { }
            public bool IsChoosing => false;
            public void BeginChoose(
                System.Collections.Generic.IReadOnlyList<Guildmaster.Guild.MapNode> available,
                bool show = true) { }
            public void EndChoose() { }
        }

        private sealed class SilentHub : Guildmaster.Core.Flow.IHubPresence
        {
            public bool IsShown => false;
            public void SetVisible(bool visible) { }
        }

        private sealed class SilentPublisher : MessagePipe.IPublisher<Guildmaster.Game.Flow.RunPartyReadyEvent>
        {
            public void Publish(Guildmaster.Game.Flow.RunPartyReadyEvent message) { }
        }

        private sealed class SilentOutcomePublisher
            : MessagePipe.IPublisher<Guildmaster.Guild.OpenOutcomeRequest>
        {
            public void Publish(Guildmaster.Guild.OpenOutcomeRequest message) { }
        }

        /// <summary>Лента боя, по которой никто не бьётся: подписаться можно, событий не будет.</summary>
        private sealed class SilentBattleEnded
            : MessagePipe.ISubscriber<Guildmaster.Presentation.BattleEndedEvent>
        {
            public System.IDisposable Subscribe(
                MessagePipe.IMessageHandler<Guildmaster.Presentation.BattleEndedEvent> handler,
                params MessagePipe.MessageHandlerFilter<Guildmaster.Presentation.BattleEndedEvent>[] filters) =>
                new NoSubscription();

            private sealed class NoSubscription : System.IDisposable
            {
                public void Dispose() { }
            }
        }

        /// <summary>Сессии нет: сеанс проверяется в отрыве от сети, и уходить неоткуда.</summary>
        private sealed class SilentCoop : Guildmaster.Core.Net.ICoopSessionControl
        {
            public Guildmaster.Core.Net.CoopSessionState State => Guildmaster.Core.Net.CoopSessionState.Offline;
            public Guildmaster.Core.Net.CoopEndReason EndReason => Guildmaster.Core.Net.CoopEndReason.None;
            public string EndMessage => string.Empty;
            public bool CanInvite    => false;
            public bool IsSteamReady => false;

            public event System.Action<Guildmaster.Core.Net.CoopSessionState> StateChanged;
            public event System.Action<int> PeerLeft;

            public bool StartHost() => false;
            public void InviteFriend() { }
            public void BrowseFriends() { }
            public void Leave()
            {
                StateChanged?.Invoke(State);
                PeerLeft?.Invoke(0);
            }
        }

        /// <summary>Своя сторона вне сеанса — первая: сторон в EditMode-тесте всё равно одна.</summary>
        private sealed class TestLocalPlayer : Guildmaster.Core.Players.ILocalPlayer
        {
            public int Team => 0;
        }

        private sealed class SilentReadyPublisher
            : MessagePipe.IPublisher<Guildmaster.Core.Net.ReadyGateChangedEvent>
        {
            public void Publish(Guildmaster.Core.Net.ReadyGateChangedEvent message) { }
        }

        private sealed class SilentAudio : IAudioService
        {
            public void Play(string soundKey) { }
            public void PlayAt(string soundKey, Vector3 position) { }
            public void Stop(string soundKey) { }
            public void StopAll() { }
            public void SetMasterVolume(float volume) { }
            public void SetMusicVolume(float volume) { }
            public void SetSfxVolume(float volume) { }
            public void SetGlobalParameter(string name, float value) { }
        }
    }
}
