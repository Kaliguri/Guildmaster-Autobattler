using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using MessagePipe;
using UnityEngine;
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
        [Tooltip("Реестр всего контента (вики «13» §3.6). Наполняется Tools/Guildmaster/Sync Content Database.")]
        [SerializeField] private ContentDatabase _contentDatabase;

        [Tooltip("Общие дефолты игры (громкости, локаль, слоты предметов; вики «13» §3.4). Потребители — Фаза 6/7.")]
        [SerializeField] private GameConfig _gameConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);

            // Контент: SO — чистые данные, рантайм-индекс (id → def) строится один раз здесь (вики «13» §3.6).
            builder.RegisterInstance<IContentDatabase>(new ContentRegistry(_contentDatabase.Entries));

            // Общие дефолты игры (потребителей пока нет — тип/ассет/DI под Фазу 6/7).
            builder.RegisterInstance(_gameConfig);

            builder.Register<UnityAudioService>(Lifetime.Singleton).As<IAudioService>();

            // Локализация: сервис поверх String Tables (вики «13» §5). Потребители (UI) — Фаза 7.
            builder.Register<LocalizationService>(Lifetime.Singleton).As<ILocalizationService>();

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
