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

        [Tooltip("Расклад Ристалища по умолчанию — обе стороны. Потребитель — DeploymentController, когда " +
                 "состав никем не заказан. Пусто = площадка встаёт пустой (это не ошибка: «состав не собран»).")]
        [SerializeField] private ProvingGroundsConfig _provingGroundsConfig;

        [Tooltip("Бои, которые крутятся ЗА главным меню (список пресетов + сиды). Потребитель — " +
                 "MenuBattleDirector. Пусто = за меню останется обычный задник, это законная настройка.")]
        [SerializeField] private MenuBattleConfig _menuBattleConfig;

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

            // Расклад Ристалища по умолчанию. Optional: пустая площадка — законный ответ «состав не
            // собран», а не поломка. Регистрируем именно так, потому что расстановка спрашивает конфиг
            // ПОСЛЕДНИМ — после заказанного состава и расклада текущего захода.
            builder.RegisterInstance(ScopeWiring.Optional(_provingGroundsConfig, nameof(RootLifetimeScope),
                nameof(_provingGroundsConfig),
                "вход на Ристалище без заказа даст пустую площадку — драться будет не с кем"));

            // Бои за главным меню. Optional: пустой список означает «фон без боя» — так это и
            // настраивается, а не ломается.
            builder.RegisterInstance(ScopeWiring.Optional(_menuBattleConfig, nameof(RootLifetimeScope),
                nameof(_menuBattleConfig),
                "за главным меню не будет боя — останется задник, как было до 04.08.2026"));

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

            // Каналы диагностики — туда же и по той же причине: это свойство отладочного сеанса ЭТОЙ
            // машины. Кооп разбирают двое, включать каналы приходится обоим и каждый запуск; забыл
            // один — прогон бесполезен наполовину.
            builder.RegisterEntryPoint<DiagChannelStore>(Lifetime.Singleton);

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

            // Сборщик тел для арены вне боя: тот же каскад статов, что у панели выше и у боя, только
            // на выходе снимки для показа. Живёт в корне по той же причине — стат-конфиги выбраны
            // здесь, а тела стоят и тогда, когда боевого скоупа нет.
            builder.Register<Combat.WorldBodyBuilder>(
                _ => new Combat.WorldBodyBuilder(
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

            // Роутер отвечает и на вопрос сеанса «открыт ли двор»: экран его, значит и факт его.
            builder.Register<MenuRouter>(Lifetime.Singleton).AsSelf().As<Core.Flow.IHubPresence>();
            // Навигатор экранов (UI-реворк Ф1): единый владелец видимости/ввода. Пока СОЗДАётся, но не
            // подключён к роутеру — переезд MenuRouter на него в Ф2. Зависимости (IInputService, IBattleClock)
            // резолвятся ниже в этом же скоупе.
            builder.Register<UiNavigator>(Lifetime.Singleton);
            // Курсоры других игроков: слой им выдаёт корень UI, а тикают они сами — рисование идёт
            // каждый кадр и не зависит от того, какой экран сейчас открыт.
            builder.RegisterEntryPoint<Guildmaster.UI.Presence.CursorLayerView>(Lifetime.Singleton).AsSelf();
            // Список участников: кто с нами и на чьей стороне. Тикает сам — состав меняют подключение
            // и представление гостя, а не действие игрока.
            builder.RegisterEntryPoint<Guildmaster.UI.Presence.ParticipantsPanelView>(Lifetime.Singleton).AsSelf();
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
            // Владелец жизненного цикла Сессии — сеанса владения состоянием игры. Само состояние забега
            // и шина команд живут в ЕГО скоупе, а не здесь: смена владельца (кооп-гость, другой профиль)
            // обязана уносить прошлое состояние с собой, а вечный объект в корне уносить нечему.
            builder.Register<Session.SessionHost>(Lifetime.Singleton).AsSelf();

            // Чтение забега для тех, кто переживает сеансы (корневой UI, мир, показ): роутер спрашивает
            // держателя текущей сессии в момент обращения. Прямая ссылка означала бы состояние сеанса,
            // который давно кончился; писать через этот шов нельзя намеренно — запись идёт шиной команд
            // внутри сессии.
            builder.Register<Session.SessionRunRouter>(Lifetime.Singleton).As<IRunStateView>();

            // Запись в забег — тоже роутером и тоже из корня. Так бой и мероприятие собираются там, где
            // забега нет вовсе (дев-арена, Ристалище, PvP, тест): тип есть всегда, а ответ «писать
            // некуда» вызывающие уже умеют читать. Прямой резолв шины ронял бы такой скоуп целиком.
            builder.Register<Session.SessionCommandRouter>(Lifetime.Singleton)
                   .As<Guildmaster.Guild.Commands.IRunCommands>();

            // Кто с нами играет и за какую сторону играем мы. Единственный источник ответа «мы
            // победили?» и «чей это курсор»: в бою есть команды, а не «сторона игрока». Роутер, а не
            // объект с полем, потому что состав живёт в сеансе и умирает вместе с ним — а спрашивают
            // его те, кто сеансы переживает (бой, показ, звук).
            builder.Register<Session.SessionPlayerRouter>(Lifetime.Singleton)
                   .As<ILocalPlayer>().As<Guildmaster.Core.Players.ISessionRoster>();

            // Где мы сейчас — для строки списка участников у остальных. Живёт в корне, потому что
            // спрашивают его из сеанса, а сеансы сменяются: место считается по показу и навигатору,
            // которые сеанс переживают.
            // Точка входа, а не просто регистрация: тишину для «отошёл» надо копить каждый кадр.
            builder.RegisterEntryPoint<Session.LocalWhereabouts>(Lifetime.Singleton)
                   .As<Session.ILocalWhereabouts>();

            // Чужие курсоры — тем же роутером-приёмом: живут они в сеансе, а рисует их мир, который
            // сеансы переживает.
            builder.Register<Session.SessionPresenceRouter>(Lifetime.Singleton)
                   .As<Guildmaster.Core.Players.IPresenceView>();

            builder.Register<SceneLoader>(Lifetime.Singleton).As<ISceneLoader>();

            // Владелец жизненного цикла Занятия (забег, Ристалище, PvP, дев-арена): всё, что кончается
            // вместе с мероприятием, живёт в его скоупе, а не здесь. Верхняя петля игры открывает
            // занятие и закрывает — конец мероприятия это смерть скоупа, а не набор ручных сбросов.
            // Он же отвечает на вопрос «где мы»: вид мероприятия — факт, названный при входе, а не
            // вывод из наличия забега и состояния арены (наход. Макса 02.08.2026 — по выводу панель
            // забега пропадала целиком, стоило владельцу одного из признаков уехать в бой).
            builder.Register<Activity.ActivityHost>(Lifetime.Singleton).AsSelf().As<IActivityView>();

            // Часы боя для тех, кто переживает мероприятия (панель забега, навигатор, звук): роутер
            // делегирует часам текущего занятия, а вне занятия отвечает «боя нет». Прямая ссылка
            // означала бы фазу мероприятия, которое давно кончилось.
            builder.Register<Activity.ActivityClockRouter>(Lifetime.Singleton).As<IBattleClock>();

            // ── Кооп: Steam напрямую, без высокоуровневого netcode ───────────────────────────
            // Регистрируется всегда, даже в соло: транспорт без сессии не поднят, качать нечего, и
            // ветвление «а вдруг мы одни» не нужно ни одному потребителю.

            // Отпечаток контента считается один раз на старте: он сверяется на рукопожатии, а к тому
            // времени контент уже не меняется. Версия сборки — из настроек проекта.
            builder.Register(c => Guildmaster.Data.ContentFingerprint.Compute(
                    ScopeWiring.Require(_contentDatabase, nameof(RootLifetimeScope), nameof(_contentDatabase)),
                    Application.version),
                Lifetime.Singleton);

            // Steam поднимается ПЕРВЫМ: без него ни лобби, ни relay-сокет не существуют, а спрашивают
            // они его сразу. Он же качает колбэки — приглашения и события лобби приходят только так.
            builder.RegisterEntryPoint<Guildmaster.Net.Session.SteamBootstrap>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Core.Players.IPlatformIdentity>();

            builder.Register<Guildmaster.Net.Transport.SteamNetTransport>(Lifetime.Singleton).AsSelf();

            // Провод выбирается ЯВНО и по факту: поднялся Steam — идём через него, не поднялся —
            // поднимаем петлю в своём процессе и ГОВОРИМ ОБ ЭТОМ ВСЛУХ.
            //
            // Молчаливого пути здесь быть не должно. 02.08.2026 инициализация Steam уехала вместе с
            // удалённым netcode, и кооп молча вёл себя как при незапущенном клиенте: лобби «просто не
            // создавалось», кнопка гасла, а искать это пришлось живым тестом вдвоём. Пустой провод,
            // который честно называет себя, дешевле такой тишины.
            //
            // Второе следствие, ради которого это и сделано: петлю можно СОЕДИНИТЬ САМУ С СОБОЙ, и
            // тогда сценарий «хост раздаёт, гость принимает» проверяется в одном процессе, на живых
            // скоупах обеих ролей (см. PlayMode-тесты коопа).
            builder.Register<Guildmaster.Net.Transport.LoopbackNetwork>(Lifetime.Singleton);
            builder.Register<Guildmaster.Net.Transport.INetTransport>(r =>
            {
                // Автоматический прогон — это игра без игрока: окна нет, Steam-оверлея нет, звать
                // некого. Steam при этом может быть ЗАПУЩЕН на машине разработчика и радостно
                // подключиться — и тогда тест коопа проверял бы связь с чужим клиентом вместо
                // собственной раздачи (наход. 05.08.2026: прогон цеплялся к живому аккаунту).
                bool headless = Application.isBatchMode;

                if (!headless && r.Resolve<Guildmaster.Net.Session.SteamBootstrap>().IsReady)
                    return r.Resolve<Guildmaster.Net.Transport.SteamNetTransport>();

                Debug.LogWarning(headless
                    ? "[Net] Автоматический прогон → сеть работает петлёй в своём процессе."
                    : "[Net] Steam не поднят → сеть работает петлёй в своём процессе. " +
                      "Кооп по интернету недоступен, одиночная игра работает как обычно.");
                return r.Resolve<Guildmaster.Net.Transport.LoopbackNetwork>().CreateNode();
            }, Lifetime.Singleton);
            // Комната выбирается тем же признаком, что и провод: без платформы (или в автоматическом
            // прогоне) она петлевая — не зовёт никого и честно гасит кнопки приглашения.
            builder.Register<Guildmaster.Net.Session.SteamLobbyService>(Lifetime.Singleton).AsSelf();
            builder.Register<Guildmaster.Net.Session.LoopbackLobby>(Lifetime.Singleton).AsSelf();
            builder.Register<Guildmaster.Net.Session.ICoopLobby>(r =>
                !Application.isBatchMode && r.Resolve<Guildmaster.Net.Session.SteamBootstrap>().IsReady
                    ? r.Resolve<Guildmaster.Net.Session.SteamLobbyService>()
                    : (Guildmaster.Net.Session.ICoopLobby)r.Resolve<Guildmaster.Net.Session.LoopbackLobby>(),
                Lifetime.Singleton);
            builder.Register<Guildmaster.Net.Session.CoopHandshake>(Lifetime.Singleton);
            builder.Register<Guildmaster.Net.Session.CoopSession>(Lifetime.Singleton)
                   .As<Guildmaster.Core.Net.ICoopSessionControl>()
                   .AsSelf();
            builder.Register<Guildmaster.Net.BattleControlRelay>(Lifetime.Singleton);

            // Роли узла в бою здесь больше нет: 02.08.2026 её сменил СОСТАВ боевого скоупа, который
            // рождается внутри сеанса и роль сеанса уже знает (см. CombatLifetimeScope.RegisterCoop).
            builder.RegisterEntryPoint<Guildmaster.Net.NetPump>(Lifetime.Singleton);

            // Приглашение может доехать когда угодно — в меню, на заставке, между забегами, — поэтому
            // мост «нас приняли → уходим из меню» живёт в корне, а не в сеансе: сеанса в этот момент
            // ещё нет, его откроет верхний цикл игры.
            builder.RegisterEntryPoint<Session.Net.CoopGuestEntry>(Lifetime.Singleton);

            // Экран исхода забега (C2) — победа/поражение после акта.
            builder.Register<OutcomePresenter>(Lifetime.Singleton).As<IOutcomePresenter>();
            // Boot title card — один раз до главного меню.
            builder.Register<TitleCardPresenter>(Lifetime.Singleton).As<ITitleCardPresenter>();
            // Главное меню (D1) — верхний цикл игры.
            builder.Register<MainMenuPresenter>(Lifetime.Singleton).As<IMainMenuPresenter>();
            // Профиль: кем игрок заходит. Требуется до меню, открывается и по кнопке из него.
            builder.Register<ProfilePresenter>(Lifetime.Singleton).As<IProfilePresenter>();
            // Двор гильдии: дом, из которого уходят в забег. Стоит между выбором дома и актом.
            builder.Register<HubPresenter>(Lifetime.Singleton).As<IHubPresenter>();


            // Линк к world-слою карты: держим ЗДЕСЬ (в корне), потому что петля акта живёт здесь, а сам слой —
            // компонент persist-мира из дочернего скоупа, которого корень напрямую не видит. Слой привязывает
            // себя к линку при старте.
            builder.Register<Presentation.Map.WorldMapViewLink>(Lifetime.Singleton)
                   .AsSelf().As<Presentation.Map.IWorldMapView>();

            // Владелец показа карты в мире: и просмотр по табу «Карта» (в т.ч. посреди боя), и ожидание
            // выбора узла петлёй — через него одного.
            // Под интерфейсом — тоже: сеанс объявляет гостю, показана ли карта, но знать про показ
            // карты целиком ему незачем (см. IActMapPresence).
            builder.RegisterEntryPoint<WorldMapController>(Lifetime.Singleton)
                   .AsSelf().As<IActMapPresence>();

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


            // GameFlow ведёт верхний цикл + реализует IRunControl (QA #18): системное меню прерывает забег.
            builder.Register<GameFlow>(Lifetime.Singleton).AsSelf().As<Guildmaster.Core.Flow.IRunControl>();

            // Ввод глобален и переживает перезагрузку боевой сцены (вики «16» §3).
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService>();

            // Каким курсором играем: системный курсор игрока и изображение для чужих — один владелец,
            // иначе у себя одна стрелка, а у напарника другая.
            builder.RegisterInstance(ScopeWiring.Require(
                gameConfig.CursorSkins, nameof(GameConfig), nameof(GameConfig.CursorSkins)));
            builder.Register<Services.CursorSkinService>(Lifetime.Singleton)
                   .AsSelf().As<Guildmaster.Core.Players.ICursorSkinControl>()
                   .As<VContainer.Unity.IStartable>();

            // Указатель в мировых координатах: один владелец перевода «экран → мир» на расстановку и на
            // присутствие. Камеру ищет лениво — сцена арены поднимается позже этого скоупа.
            builder.Register<Guildmaster.Presentation.PointerWorld>(Lifetime.Singleton)
                   .As<Guildmaster.Core.Input.IPointerWorld>();

            // Провайдера GlobalMessagePipe здесь больше нет: статический доступ к шине не звал никто,
            // все потребители получают IPublisher/ISubscriber инъекцией — как и задумано (аудит 2026-07-26).
            builder.RegisterMessagePipe();

            // Разрыв связи глазами игрока: кто пропал и что делать. Живёт в корне, потому что переживает
            // и сеанс, и мероприятие — терять напарника можно в любом из них.
            builder.RegisterEntryPoint<Session.CoopDisconnectPresenter>(Lifetime.Singleton).AsSelf();

            // Приглашение, принятое посреди своей игры: рвём то, что играли. Уводит в чужую игру уже
            // цикл — он видит, что сессия стала гостевой.
            builder.RegisterEntryPoint<Session.CoopJoinInterrupt>(Lifetime.Singleton).AsSelf();
        }

        private static ulong GenerateRootSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks;
        }
    }
}
