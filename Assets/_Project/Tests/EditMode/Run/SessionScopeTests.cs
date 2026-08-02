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
            Assert.IsTrue(session.TryResolve(out IRunCommands _), "у владельца нет шины команд забега");
            Assert.AreEqual(SessionRole.Owner, session.Resolve<SessionContext>().Role);
            Assert.IsTrue(session.Resolve<SessionContext>().IsOwner);
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
            Assert.IsFalse(session.TryResolve(out IRunCommands _),
                "гость получил шину команд — он применяет их у себя вместо хоста");
            Assert.AreEqual(SessionRole.Guest, session.Resolve<SessionContext>().Role);
            Assert.IsFalse(session.Resolve<SessionContext>().IsOwner);
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
        /// Собрать контейнер сессии в отрыве от Unity-скоупов: инсталлер — обычный
        /// <see cref="IInstaller"/>, и состав по роли проверяется без сцены.
        /// </summary>
        /// <remarks>
        /// Зависимости, которые в игре приходят из предков (сейв, профиль, конфиг, звук), кладём сюда
        /// заглушками: VContainer проверяет разрешимость всей ветки на <c>Build</c>, а дефолтные
        /// значения параметров конструктора он не подставляет.
        /// </remarks>
        private static IObjectResolver BuildSession(SessionRole role)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance<ISaveService>(new InMemorySaveService());
            builder.RegisterInstance<IProfileService>(new FixedProfileService());
            builder.RegisterInstance(ScriptableObject.CreateInstance<GameConfig>());
            builder.RegisterInstance<IAudioService>(new SilentAudio());

            new SessionInstaller(role).Install(builder);
            return builder.Build();
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
