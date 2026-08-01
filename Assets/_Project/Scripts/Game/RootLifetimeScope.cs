using Guildmaster.Core.Audio;
using Guildmaster.Core.DevConsole;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Players;
using Guildmaster.Core.Random;
using Guildmaster.Core.Settings;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using Guildmaster.Data.Stats;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Input;
using Guildmaster.Game.Players;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using Guildmaster.UI;
using Guildmaster.UI.Tooltips;
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
        [Tooltip("Реестр всего контента (вики «13» §3.6). Наполняется Alebardium/Data/Sync Content Database.")]
        [SerializeField] private ContentDatabase _contentDatabase;

        [Tooltip("Общие дефолты игры (громкости, локаль, слоты предметов; вики «13» §3.4). Потребители — Фаза 6/7.")]
        [SerializeField] private GameConfig _gameConfig;

        [Tooltip("Каталог звуков (ключ→FMOD-событие; вики impl «09»). Потребители — FmodAudioService и AudioPresenter. " +
                 "ОБЯЗАТЕЛЕН. Пусто = красная ошибка и полная тишина: назначить Assets/_Project/ScriptableObjects/Audio/AudioCatalog.")]
        [SerializeField] private AudioCatalog _audioCatalog;

        [Tooltip("Параметры генерации карты акта (глубина/зоны/якоря; оверхол карты 2026-07). Потребитель — GameFlow.BeginAct. " +
                 "ОБЯЗАТЕЛЕН. Пусто = красная ошибка, и карта пойдёт по дефолтам КОДА, а не по этому ассету.")]
        [SerializeField] private ActConfig _actConfig;

        [Tooltip("Снимок палитры проекта (UI/Theme/tokens.*.uss → Alebardium/Дизайн-система/Пересобрать палитру). " +
                 "Нужен интерфейсу, чтобы карточка реликвии красила тело ТЕМ ЖЕ путём, что бой: юнит хранит " +
                 "ступень приглушения, цвет живёт здесь. Пусто = карточка покажет арт как есть.")]
        [SerializeField] private GuildmasterPalette _palette;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);

            // Контент: SO — чистые данные, рантайм-индекс (id → def) строится один раз здесь (вики «13» §3.6).
            builder.RegisterInstance<IContentDatabase>(
                new ContentRegistry(ScopeWiring.Require(_contentDatabase, nameof(RootLifetimeScope), nameof(_contentDatabase)).Entries));

            // Общие дефолты игры (экономика забега — владелец ассет, HARD-правило проекта).
            GameConfig gameConfig = ScopeWiring.Require(_gameConfig, nameof(RootLifetimeScope), nameof(_gameConfig));
            builder.RegisterInstance(gameConfig);

            // Конфиг генерации карты акта (оверхол 2026-07). Потребитель — GameFlow.
            builder.RegisterInstance(ScopeWiring.Optional(_actConfig, nameof(RootLifetimeScope), nameof(_actConfig),
                "карта акта пойдёт по дефолтам кода, а не по ассету — правки дизайнера не применятся"));

            // Палитра проекта: интерфейсу она нужна ровно за одним — цветом ступени приглушения на карточке
            // реликвии. Без неё карточка рисует арт как нарисован, поэтому Optional, а не Require.
            builder.RegisterInstance(ScopeWiring.Optional(_palette, nameof(RootLifetimeScope), nameof(_palette),
                "карточка реликвии покажет тело без приглушения — в бою оно будет, на карточке нет"));

            // Каталог доступен обоим потребителям (FmodAudioService резолвит ключ→событие, AudioPresenter
            // строит поверх него резолвер).
            var audioCatalog = ScopeWiring.Optional(_audioCatalog, nameof(RootLifetimeScope), nameof(_audioCatalog),
                "звука не будет вообще");
            builder.RegisterInstance(audioCatalog);
            builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>();

            // Настройки игрока: единый источник + персист за ISaveService + живое применение в аудио.
            // Entry point — Start() зовёт Load() и применяет сохранённые громкости на старте сессии.
            builder.RegisterEntryPoint<SettingsService>(Lifetime.Singleton).As<ISettingsService>();

            // Настройки дисплея — отдельно: они про КОМПЬЮТЕР, а не про игрока, поэтому едут в
            // машинно-локальное хранилище мимо Steam Cloud. Entry point применяет режим на старте сессии.
            builder.RegisterEntryPoint<DisplayService>(Lifetime.Singleton).As<IDisplayService>();

            // Рантайм-UI (оверлеи меню/настроек): VM + роутер сессионные; бутстрап — UIDocument-компонент
            // в CoreScene (инъекция методом через RegisterComponentInHierarchy). ESC открывает меню.
            // Стат-превью для UI (панель деталей инвентаря): считает те же числа, что боевая сборка.
            // Живёт в корне, а не в боевом скоупе: инвентарь открывается и вне боя.
            // Стат-конфиги приходят ИЗ GameConfig, а не отдельными полями скоупа: играющий экземпляр
            // выбран в одном месте, поэтому панель инвентаря не может показать числа, которых нет в бою.
            builder.Register<IUnitStatPreview>(
                _ => new UnitStatPreview(
                    ScopeWiring.Require(gameConfig.Stats, nameof(GameConfig), nameof(GameConfig.Stats)),
                    ScopeWiring.Require(gameConfig.ClassBalance, nameof(GameConfig), nameof(GameConfig.ClassBalance))),
                Lifetime.Singleton);

            // Слой описаний (Трек Д-о, план §II.10.1): единственная дорога, по которой число попадает
            // игроку на глаза. Тултипы, карточки и (позже) панель юнита берут текст и величины отсюда,
            // а не считают у себя — иначе на первом же ребалансе экраны разойдутся с боем.
            // Оформление терминов (цвет по разделу глоссария + полужирный). Регистрируется ДО слоя
            // описаний: тот принимает его через конструктор и больше ничего о цветах не знает —
            // палитра остаётся в USS, откуда её и читают доноры (см. KeywordStyle).
            builder.Register<KeywordStyle>(Lifetime.Singleton).As<IKeywordStyle>().AsSelf();
            builder.Register<DescriptionService>(Lifetime.Singleton).As<IDescriptionService>();

            // Тултипы (Трек Т, план §II.10.5): одна система на панель + сборка содержимого по запросу.
            // Систему привязывает к слою layer-tooltip бутстрап UI — он владелец панели и слоёв.
            builder.Register<TooltipContentFactory>(Lifetime.Singleton).As<ITooltipContentFactory>();
            builder.Register<TooltipSystem>(Lifetime.Singleton);

            // Звук интерфейса: один слушатель на корне панели вместо вызова в каждом экране (привязывает
            // бутстрап UI — он владелец панели). Звук забега вне боя (экраны, карта, переходы, музыка):
            // живёт в корне, а не в боевом скоупе, иначе всё за пределами боя остаётся немым.
            builder.Register<UiSoundSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<RunAudioPresenter>(Lifetime.Singleton);

            builder.Register<SettingsViewModel>(Lifetime.Singleton);
            builder.Register<LoadoutViewModel>(Lifetime.Singleton);

            // Dev-консоль (Трек К): реестр и хвост логов живут в корне, потому что команды регистрируют
            // модули из РАЗНЫХ скоупов (боевые — из боевого, мировые — из корневого), а консоль одна.
            // Регистрируются всегда: пустой реестр ничего не стоит, а гейт «только в редакторе» стоит на
            // подписке тогла — иначе билд-сборка ловила бы отсутствующую зависимость у своих же команд.
            builder.Register<DevCommandRegistry>(Lifetime.Singleton);
            builder.Register<DevConsoleLog>(Lifetime.Singleton);

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

            // Кто выбирает язык на старте: сохранённый из prefs, а при первом запуске — язык системы.
            // Сам LocalizationService этого не делает намеренно: он умеет отдать строку и переключить
            // локаль, но не знает про настройки игрока и не должен решать за него.
            builder.RegisterEntryPoint<LocaleStartup>(Lifetime.Singleton);

            // Персистентность: JSON-файл за швом ISaveService — наш собственный и единственный бэкенд
            // (реш. 2026-07-26).
            builder.Register<JsonFileSaveService>(Lifetime.Singleton).As<ISaveService>();
            // Второе хранилище — для данных компьютера (разрешение, режим окна, частота). Лежит вне
            // Saves/, поэтому Steam Cloud его не трогает: чужое разрешение на втором ПК в лучшем случае
            // неудобно, в худшем — чёрный экран на неподдерживаемом режиме.
            builder.Register<LocalJsonFileSaveService>(Lifetime.Singleton).As<ILocalSaveService>();
            // Иерархия сохранений: профиль → гильдии → забег. Entry point поднимает прошлый выбор, а на
            // чистой установке заводит первый профиль с гильдией — иначе забегу некуда писаться.
            builder.RegisterEntryPoint<ProfileService>(Lifetime.Singleton).As<IProfileService>();
            // Durable-состояние забега + правила вместимости реликов (план 11 §3.1, §5.4).
            builder.Register<RunStateService>(Lifetime.Singleton);

            // Шина команд забега: снаружи сборки Guild в RunState пишут только через неё, и мутаторы
            // internal держат это компилятором. Лог append-only даёт реплей, аудит «кто передвинул» и
            // хвост для реконнекта; соло идёт этим же путём, иначе кооп нашёл бы обход первым же
            // расхождением состояний (ТЗ кооп-вертикали §4.1).
            builder.Register<Guildmaster.Guild.Commands.RunCommandLog>(Lifetime.Singleton);
            builder.Register<Guildmaster.Guild.Commands.RunCommandApplier>(Lifetime.Singleton);
            builder.Register<Guildmaster.Guild.Commands.RunCommandBus>(Lifetime.Singleton)
                   .As<Guildmaster.Guild.Commands.IRunCommands>()
                   .AsSelf();

            // За какую команду играет этот клиент. Единственный источник ответа «мы победили?» —
            // в бою есть команды, а не «сторона игрока» (шов под PvP).
            builder.Register<SoloLocalPlayer>(Lifetime.Singleton).As<ILocalPlayer>();

            builder.Register<SceneLoader>(Lifetime.Singleton).As<ISceneLoader>();

            // ── Кооп: транспорт, сессия, общая пауза ──────────────────────────────────────────
            // Регистрируется всегда, даже в соло: транспорт без сессии не поднят, качать нечего, и
            // ветвление «а вдруг мы одни» не нужно ни одному потребителю.
            builder.RegisterComponentInHierarchy<Unity.Netcode.NetworkManager>();

            // Отпечаток контента считается один раз на старте: он сверяется на рукопожатии, а к тому
            // времени контент уже не меняется. Версия сборки — из настроек проекта.
            builder.Register(c => Guildmaster.Data.ContentFingerprint.Compute(
                    ScopeWiring.Require(_contentDatabase, nameof(RootLifetimeScope), nameof(_contentDatabase)),
                    Application.version),
                Lifetime.Singleton);

            builder.Register<Guildmaster.Net.Transport.NgoTransport>(Lifetime.Singleton)
                   .As<Guildmaster.Net.Transport.INetTransport>()
                   .AsSelf();
            builder.Register<Guildmaster.Net.Session.CoopSession>(Lifetime.Singleton);
            builder.Register<Guildmaster.Net.BattleControlRelay>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Guildmaster.Net.NetPump>(Lifetime.Singleton);

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
            // Кнопки бита (A4): гейт «бой добит → к награде» и передышка между узлами.
            builder.Register<ContinuePresenter>(Lifetime.Singleton).As<IContinuePresenter>();
            // Экран исхода забега (C2) — победа/поражение после акта.
            builder.Register<OutcomePresenter>(Lifetime.Singleton).As<IOutcomePresenter>();
            // Boot title card — один раз до главного меню.
            builder.Register<TitleCardPresenter>(Lifetime.Singleton).As<ITitleCardPresenter>();
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

            // Владелец показа карты в мире: и просмотр по табу «Карта» (в т.ч. посреди боя), и ожидание
            // выбора узла петлёй — через него одного.
            builder.RegisterEntryPoint<WorldMapController>(Lifetime.Singleton).AsSelf();

            // Владелец «моргания» между кадрами (QA #53). Держим в КОРНЕ, а не рядом с картой: переход
            // переживает и уход заказчика, и смену сцены под шторкой — карта, заказав его, тут же уходит
            // в узел и довести его до конца не может.
            builder.RegisterEntryPoint<Presentation.Transition.ScreenTransitionRunner>(Lifetime.Singleton)
                   .As<Core.Flow.IScreenTransition>();

            // ЕДИНЫЙ такт визуала: от него пляшут биение узлов, волна по дорожкам и всё ритмичное, что
            // появится дальше. Пока считается от часов; когда музыка научится задавать темп, сменится
            // реализация, а потребители — нет.
            builder.Register<Presentation.Tempo.VisualTempo>(Lifetime.Singleton)
                   .As<Presentation.Tempo.IVisualTempo>();

            // Общий реестр визуальных эффектов: одно место, где их гасят и возвращают (дев-команды gm_fx,
            // позже — настройки игры, там часть из них станет доступностью).
            builder.Register<Presentation.Effects.VisualToggles>(Lifetime.Singleton).AsSelf();

            // Выбор узла — world-карта (узлы в мире, камера как в бою). UITK-карта снесена после приёмки:
            // держать второй путь к той же карте значило чинить каждый баг дважды.
            // AutoFirstNodeChooser остаётся для headless/тестов.
            builder.Register<WorldMapNodeChooser>(Lifetime.Singleton).As<IMapNodeChooser>();

            // Стыки узлов: возврат мира после узла + передышка с кнопками «Продолжить»/«К построению».
            builder.Register<RunBeatStage>(Lifetime.Singleton).As<IRunBeatStage>();
            builder.Register<ActRunner>(Lifetime.Singleton);

            // GameFlow ведёт верхний цикл + реализует IRunControl (QA #18): системное меню прерывает забег.
            builder.Register<GameFlow>(Lifetime.Singleton).AsSelf().As<Guildmaster.Core.Flow.IRunControl>();

            // Ввод глобален и переживает перезагрузку боевой сцены (вики «16» §3).
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService>();

            // Провайдера GlobalMessagePipe здесь больше нет: статический доступ к шине не звал никто,
            // все потребители получают IPublisher/ISubscriber инъекцией — как и задумано (аудит 2026-07-26).
            builder.RegisterMessagePipe();
        }

        private static ulong GenerateRootSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks;
        }
    }
}
