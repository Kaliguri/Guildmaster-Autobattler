using Guildmaster.Core.Audio;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Random;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using Guildmaster.UI;
using Guildmaster.Presentation.Audio;
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

        [Tooltip("Каталог звуков (ключ→FMOD-событие; вики impl «09»). Потребители — FmodAudioService и AudioPresenter. " +
                 "Пусто = игра не падает, но звука нет: назначить ассет Assets/_Project/ScriptableObjects/Audio/AudioCatalog.")]
        [SerializeField] private AudioCatalog _audioCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);

            // Контент: SO — чистые данные, рантайм-индекс (id → def) строится один раз здесь (вики «13» §3.6).
            builder.RegisterInstance<IContentDatabase>(new ContentRegistry(_contentDatabase.Entries));

            // Общие дефолты игры (потребителей пока нет — тип/ассет/DI под Фазу 6/7).
            builder.RegisterInstance(_gameConfig);

            // Каталог доступен обоим потребителям (FmodAudioService резолвит ключ→событие, AudioPresenter
            // строит поверх него резолвер). Ассет не назначен → пустой рантайм-инстанс (всё в тишину, бой
            // не падает) — тот же приём, что у CombatFeelConfig.
            var audioCatalog = _audioCatalog != null ? _audioCatalog : ScriptableObject.CreateInstance<AudioCatalog>();
            builder.RegisterInstance(audioCatalog);
            builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>();

            // Настройки игрока: единый источник + JSON-персист + живое применение в аудио (клиент-локально).
            // Entry point — Start() зовёт Load() и применяет сохранённые громкости на старте сессии.
            builder.RegisterEntryPoint<SettingsService>(Lifetime.Singleton).As<ISettingsService>();

            // Рантайм-UI (оверлеи меню/настроек): VM + роутер сессионные; бутстрап — UIDocument-компонент
            // в CoreScene (инъекция методом через RegisterComponentInHierarchy). ESC открывает меню.
            builder.Register<SettingsViewModel>(Lifetime.Singleton);
            builder.Register<MenuRouter>(Lifetime.Singleton).AsSelf().As<IMenuRouter>();
            builder.RegisterComponentInHierarchy<UiRootBootstrap>();

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
