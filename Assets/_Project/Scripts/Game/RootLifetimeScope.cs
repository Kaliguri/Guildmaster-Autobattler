using Guildmaster.Core.Audio;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Players;
using Guildmaster.Core.Random;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Input;
using Guildmaster.Game.Players;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
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

        [Tooltip("Параметры генерации карты акта (глубина/зоны/якоря; оверхол карты 2026-07). Потребитель — GameFlow.BeginAct. " +
                 "Пусто = фолбэк на дефолтный конфиг (Start+12 испытаний+Boss, зоны разогрев/развитие/пик).")]
        [SerializeField] private ActConfig _actConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);

            // Контент: SO — чистые данные, рантайм-индекс (id → def) строится один раз здесь (вики «13» §3.6).
            builder.RegisterInstance<IContentDatabase>(new ContentRegistry(_contentDatabase.Entries));

            // Общие дефолты игры (потребителей пока нет — тип/ассет/DI под Фазу 6/7).
            builder.RegisterInstance(_gameConfig);

            // Конфиг генерации карты акта (оверхол 2026-07). Ассет не назначен → дефолтный инстанс (POCO-дефолты
            // с зонами/якорями), игра не падает — тот же приём, что у AudioCatalog. Потребитель — GameFlow.
            builder.RegisterInstance(_actConfig != null ? _actConfig : ScriptableObject.CreateInstance<ActConfig>());

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
            builder.Register<LoadoutViewModel>(Lifetime.Singleton);
            builder.Register<LoadoutHubViewModel>(Lifetime.Singleton);
            builder.Register<MenuRouter>(Lifetime.Singleton).AsSelf();
            // Навигатор экранов (UI-реворк Ф1): единый владелец видимости/ввода. Пока СОЗДАётся, но не
            // подключён к роутеру — переезд MenuRouter на него в Ф2. Зависимости (IInputService, IBattleClock)
            // резолвятся ниже в этом же скоупе.
            builder.Register<UiNavigator>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<UiRootBootstrap>();

            // Точка входа игры (D1): GameBootstrap в персистентной CoreScene получает GameFlow и крутит
            // верхний цикл меню→забег→меню. Инъекция полей — через RegisterComponentInHierarchy.
            builder.RegisterComponentInHierarchy<GameBootstrap>();

            // Локализация: сервис поверх String Tables (вики «13» §5). Потребители (UI) — Фаза 7.
            builder.Register<LocalizationService>(Lifetime.Singleton).As<ILocalizationService>();

            // Персистентность: соло-бэкенд JSON-файл за швом ISaveService (ES3/Steam Cloud — потом).
            builder.Register<JsonFileSaveService>(Lifetime.Singleton).As<ISaveService>();
            // Durable-состояние забега + правила вместимости реликов (план 11 §3.1, §5.4).
            builder.Register<RunStateService>(Lifetime.Singleton);

            // За какую команду играет этот клиент. Единственный источник ответа «мы победили?» —
            // в бою есть команды, а не «сторона игрока» (шов под PvP).
            builder.Register<SoloLocalPlayer>(Lifetime.Singleton).As<ILocalPlayer>();

            builder.Register<SceneLoader>(Lifetime.Singleton).As<ISceneLoader>().AsSelf();

            // Флоу забега (план 11): рукопожатие в боевой скоуп + сетевые швы (соло-тела). BattleFlow создаётся
            // per-node внутри GameFlow, потому в DI не регистрируется.
            // Один инстанс под двумя ролями: IBattleSession (write-side, боевой скоуп) + IBattleClock
            // (read-side, верхняя панель в UI-слое, план 12 Фаза 2).
            builder.Register<BattleSession>(Lifetime.Singleton).As<IBattleSession>().As<IBattleClock>();
            builder.Register<SoloReadyGate>(Lifetime.Singleton).As<IReadyGate>();
            builder.Register<SoloPlayerIntentSource>(Lifetime.Singleton).As<IPlayerIntentSource>();

            // Витрина наград после боя (A3): катит 1-из-3 реликов из контент-БД (детерминирован через RNG).
            builder.Register<RewardService>(Lifetime.Singleton);
            // Ценообразование реликвий (B1): цена по KitPower + разброс на сиде витрины.
            builder.Register<RelicPricer>(Lifetime.Singleton);
            // Показ награды (вынесен из GameFlow — переиспользуют петля акта и legacy-вход одного боя).
            builder.Register<RewardPresenter>(Lifetime.Singleton).As<IRewardPresenter>();
            // Единая кнопка «Продолжить» (A4) — бит между разрешённым узлом и возвратом на карту.
            builder.Register<ContinuePresenter>(Lifetime.Singleton).As<IContinuePresenter>();
            // Экран исхода забега (C2) — победа/поражение после акта.
            builder.Register<OutcomePresenter>(Lifetime.Singleton).As<IOutcomePresenter>();
            // Главное меню (D1) — верхний цикл игры.
            builder.Register<MainMenuPresenter>(Lifetime.Singleton).As<IMainMenuPresenter>();

            // Применение последствий текстовых ивентов к RunState (план 11 §5.1).
            builder.Register<EventEffectApplier>(Lifetime.Singleton);

            // Магазин (B2): логика витрины/покупки/продажи за IShopController; UI биндится к экземпляру из запроса.
            builder.Register<ShopController>(Lifetime.Singleton);

            // Петля акта (план act-map-run-loop §3.2): резолвер узлов + выбор узла через экран карты (A3) + раннер.
            // AutoFirstNodeChooser остаётся для headless/тестов; в игре узел выбирает игрок кликом по MapScreen.
            builder.Register<NodeResolver>(Lifetime.Singleton).As<INodeResolver>();

            // Линк к world-слою карты: держим ЗДЕСЬ (в корне), потому что петля акта живёт здесь, а сам слой —
            // компонент persist-мира из дочернего скоупа, которого корень напрямую не видит. Слой привязывает
            // себя к линку при старте.
            builder.Register<Presentation.Map.WorldMapViewLink>(Lifetime.Singleton)
                   .AsSelf().As<Presentation.Map.IWorldMapView>();

            // Выбор узла. Фаза D: world-карта (узлы в мире, камера как в бою). Откат на UITK-карту — заменой
            // одной этой строки на MapScreenNodeChooser (старый путь пока цел, до play-приёмки world-карты).
            // AutoFirstNodeChooser остаётся для headless/тестов.
            builder.Register<WorldMapNodeChooser>(Lifetime.Singleton).As<IMapNodeChooser>();
            builder.Register<ActRunner>(Lifetime.Singleton);

            // GameFlow ведёт верхний цикл + реализует IRunControl (QA #18): системное меню прерывает забег.
            builder.Register<GameFlow>(Lifetime.Singleton).AsSelf().As<Guildmaster.Core.Flow.IRunControl>();

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
