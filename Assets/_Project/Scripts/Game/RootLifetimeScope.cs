using Guildmaster.Core.Input;
using Guildmaster.Core.Random;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using MessagePipe;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Корневой DI-скоуп, живёт всю сессию вместе с CoreScene.
    /// Регистрирует сессионные сервисы: RNG, MessagePipe, Audio, SceneLoader, GameFlow
    /// (вики «10» §8.1).
    /// </summary>
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);

            builder.Register<UnityAudioService>(Lifetime.Singleton).As<IAudioService>();
            builder.Register<SceneLoader>(Lifetime.Singleton);
            builder.Register<GameFlow>(Lifetime.Singleton);

            // Ввод глобален и переживает перезагрузку боевой сцены (вики «16» §3).
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService>();

            var options = builder.RegisterMessagePipe();
            builder.RegisterBuildCallback(c => GlobalMessagePipe.SetProvider(c.AsServiceProvider()));
        }

        private static ulong GenerateRootSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks;
        }
    }
}
