using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Flow;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Diagnostics;
using Guildmaster.Guild;
using Guildmaster.UI.DevConsole;
using MessagePipe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Строит и проводит экраны забега из UXML-шаблонов, а стек, видимость и
    /// глушение геймплейного ввода делегирует <see cref="UiNavigator"/> (UI-реворк Ф2). Каждый экран получает
    /// честный <see cref="ScreenKind"/> — по нему навигатор ВЫЧИСЛЯЕТ видимость нижних и suppress, вместо
    /// прежней ручной синхронизации (<c>_menuModeActive</c>/<c>_prevContext</c>/CSS-классов-флагов).
    /// Настройки применяются живьём; Cancel/Save — на кнопках, ESC = навигация назад.
    /// </summary>
    public sealed class MenuRouter : IDisposable, Core.Flow.IHubPresence
    {
        private readonly IInputService _input;
        private readonly UiNavigator _nav;
        private readonly SettingsViewModel _settingsVm;
        private readonly LoadoutViewModel _loadoutVm;
        private readonly ILocalizationService _loc;
        private readonly IRunControl _runControl; // QA #18: «В главное меню»/«Выход» из системного меню

        // Dev-консоль (Трек К): реестр команд и хвост логов приходят из корневого скоупа — консоль одна на
        // сессию, а команды в неё кладут модули из разных скоупов.
        private readonly Core.DevConsole.DevCommandRegistry _registry;
        private readonly Core.DevConsole.DevConsoleLog _log;

        // Кооп-сессия за швом Core: UI спрашивает состояние и просит поднять/войти/выйти, но про NGO и
        // транспорт не знает ничего — иначе сетевой стек приехал бы в слой, который рисует кнопки.
        private readonly Core.Net.ICoopSessionControl _coop;

        // Дома и их забеги для экрана «Создать игру». Спрашиваются напрямую у профиля и диска, потому
        // что меню живёт ВНЕ сеанса: держателя состояния в этот момент не существует.
        private readonly Core.Persistence.IProfileService _profiles;
        private readonly Core.Persistence.ISaveService    _save;

        // Палитра проекта — единственный владелец цвета. Роутер её не читает сам: он передаёт её ригу
        // карточек, чтобы тот красил тело той же ступенью приглушения, что бой.
        private readonly GuildmasterPalette _palette;

        // Счёт общего согласия. Роутер ЗАПОМИНАЕТ последнее объявление, а не только слушает: гейт
        // объявляет счёт в момент привязки действия, то есть ДО того, как экран построен, и живая
        // подписка это объявление пропустила бы — кнопка открылась бы без «(N/M)».
        private readonly ISubscriber<Core.Net.ReadyGateChangedEvent> _readySub;
        private readonly IDisposable _readySubscription;
        private Core.Net.ReadyGateChangedEvent _lastReady;
        // Что делать со счётом, пока открыт экран, который его ждёт. null — таких экранов нет, и счёт
        // просто запоминается.
        private Action<Core.Net.ReadyGateChangedEvent> _onReadyChanged;

        private VisualElement _root;
        private VisualElement _modalLayer;   // верхний слой — заслонка выхода ложится поверх и паузы
        private VisualTreeAsset _pauseUxml;
        private VisualTreeAsset _settingsUxml;
        private VisualTreeAsset _devConsoleUxml;
        private VisualTreeAsset _devLogUxml;

        // Один инстанс на всю сессию: экран носит историю команд, и пересоздание стирало бы её.
        private DevConsoleScreen _devConsole;
        private DevLogScreen _devLog;
        private VisualTreeAsset _loadoutUxml;
        private VisualTreeAsset _rewardUxml;
        private VisualTreeAsset _eventUxml;
        private VisualTreeAsset _continueUxml;
        private VisualTreeAsset _shopUxml;
        private VisualTreeAsset _chestUxml;
        private VisualTreeAsset _campUxml;
        private VisualTreeAsset _outcomeUxml;
        private VisualTreeAsset _mainMenuUxml;
        private VisualTreeAsset _newGameUxml;
        private VisualTreeAsset _guildSelectUxml;
        private VisualTreeAsset _hubUxml;
        private VisualTreeAsset _profileUxml;
        private VisualTreeAsset _confirmUxml;

        // Профиль: набор скинов, число слотов, имя из Steam и применение выбранного курсора. Роутер
        // держит их функциями, а не тянет сервисы вглубь экрана: экран — разметка, а не владелец правил.
        private readonly CursorSkinCatalog _cursorSkins;
        private readonly int               _profileSlotLimit;
        private readonly int               _guildSlotLimit;
        private readonly Func<string>      _steamName;
        private readonly Action<string>    _cursorApply;

        /// <summary>Сколько мейн-цветов предлагаем. Столько же токенов в палитре — предел кооп-сессии.</summary>
        private const int ProfileColorCount = 4;
        private VisualTreeAsset _titleCardUxml;
        private Sprite _titleCardSeal;
        private VisualTreeAsset _loadoutInventoryUxml;
        private VisualTreeAsset _arcanaCardUxml;

        // Идентификатор системного меню (pause) в стеке навигатора — ToggleSystemMenu отличает «мы в меню»
        // (даже если сверху настройки) от «поверх игры». Заменил маркер-скан gm-pause-root по CSS-классу.
        private const string PauseId = "pause";

        public MenuRouter(IInputService input, UiNavigator nav, SettingsViewModel settingsVm, LoadoutViewModel loadoutVm,
                          ILocalizationService loc, IRunControl runControl,
                          IPublisher<MainMenuVisibilityChangedEvent> mainMenuVisPub,
                          Core.Audio.IAudioService audio,
                          GuildmasterPalette palette,
                          Core.DevConsole.DevCommandRegistry registry,
                          Core.DevConsole.DevConsoleLog devLog,
                          Core.Net.ICoopSessionControl coop,
                          Core.Persistence.IProfileService profiles,
                          Core.Persistence.ISaveService save,
                          GameConfig gameConfig,
                          Core.Players.IPlatformIdentity platform,
                          Core.Players.ICursorSkinControl cursors,
                          ISubscriber<Core.Net.ReadyGateChangedEvent> readySub)
        {
            _cursorSkins     = gameConfig?.CursorSkins;
            _profileSlotLimit = gameConfig != null ? gameConfig.MaxProfiles : 1;
            _guildSlotLimit   = gameConfig != null ? gameConfig.MaxGuildsPerProfile : 1;
            _steamName       = () => platform != null ? platform.PlayerName : "Игрок";
            _cursorApply     = id => cursors?.Apply(id);
            _readySub = readySub;
            // Подписка живёт столько же, сколько роутер, и это не лень: гейт объявляет счёт в момент
            // привязки действия — раньше, чем экран заказан. Подписка на время показа это объявление
            // пропустила бы, и кнопка открылась бы без «(N/M)».
            _readySubscription = readySub?.Subscribe(e =>
            {
                _lastReady = e;
                _onReadyChanged?.Invoke(e);
            });
            _profiles = profiles;
            _save = save;
            _coop = coop;
            _registry = registry;
            _log = devLog;
            _audio = audio;
            _palette = palette;
            _input = input;
            _nav = nav;
            _settingsVm = settingsVm;
            _loadoutVm = loadoutVm;
            _loc = loc;
            _runControl = runControl;
            _mainMenuVisPub = mainMenuVisPub;
        }

        private readonly IPublisher<MainMenuVisibilityChangedEvent> _mainMenuVisPub;

        /// <summary>
        /// Пока главное меню на экране — как его закрыть выбором «Ристалище». Ставится показом меню,
        /// снимается при уходе. Нужен, чтобы запрос площадки снимал экран ТЕМ ЖЕ путём, что кнопка:
        /// резолв через навигатор гасит и панель, и стол под ней. Обход мимо навигатора оставлял фон меню
        /// висеть поверх мира, а площадку — невидимой.
        /// </summary>
        private System.Action _resolveMainMenuAsProvingGrounds;
        private System.Action _resolveMainMenuAsCoopGuest;
        private bool _coopGuestPending;              // приглашение приняли до того, как меню открылось
        private System.Action _resolveTitleCard;      // бут-экран умеет закрываться и не по клику
        private bool _provingGroundsPending;          // Ристалище запросили до того, как меню открылось
        // Звук экранов, у которых он СВОЙ (награда, лавка, привал, сундук): общий клик даёт корневой
        // UiSoundSystem, а эти моменты игрок должен отличать на слух.
        private readonly Core.Audio.IAudioService _audio;

        public bool IsOpen => _nav.IsOpen;

        /// <summary>
        /// Закрыть главное меню выбором «Ристалище», если оно сейчас на экране. Возвращает false, если
        /// меню не показано — тогда решение принимает верхний цикл игры, а не UI.
        /// </summary>
        public bool TryLeaveMainMenuForProvingGrounds()
        {
            if (_resolveMainMenuAsProvingGrounds == null)
            {
                // Меню ещё не открыто — мы на бут-экране, и запрос пришёл из dev-консоли (её открывают
                // как раз тогда, когда игра куда-то не дошла). Запоминаем намерение и торопим титул-карту:
                // главное меню отдаст Ристалище сразу, как только появится. Прежде запрос в этот момент
                // молча терялся, и команда «работала» без единого следа (наход. Макса 02.08.2026).
                _provingGroundsPending = true;
                SkipTitleCard();
                return true;
            }

            System.Action resolve = _resolveMainMenuAsProvingGrounds;
            _resolveMainMenuAsProvingGrounds = null;
            resolve();
            return true;
        }

        /// <summary>
        /// Закрыть главное меню, потому что нас приняли в чужую игру. Возвращает <c>false</c>, только
        /// если закрывать нечего и запомнить намерение тоже не выйдет.
        /// </summary>
        /// <remarks>
        /// <b>Меню закрывается не кликом, а событием сети,</b> и это не костыль: выбор игрок уже сделал
        /// — в оверлее друзей Steam, возможно ещё до запуска игры. Оставить его в меню значило бы
        /// показывать «Начать / Продолжить» человеку, который вообще-то уже в чужой партии.
        /// <para>Тот же приём, что у Ристалища (<see cref="TryLeaveMainMenuForProvingGrounds"/>): пока
        /// меню не открылось, намерение ждёт и торопит бут-экран — приглашение, принятое на заставке,
        /// иначе молча потерялось бы.</para>
        /// </remarks>
        public bool TryLeaveMainMenuForCoopGuest()
        {
            if (_resolveMainMenuAsCoopGuest == null)
            {
                _coopGuestPending = true;
                SkipTitleCard();
                return true;
            }

            System.Action resolve = _resolveMainMenuAsCoopGuest;
            _resolveMainMenuAsCoopGuest = null;
            resolve();
            return true;
        }

        /// <summary>Закрыть бут-экран, если он на экране: за ним ждут главного меню.</summary>
        private void SkipTitleCard()
        {
            System.Action resolve = _resolveTitleCard;
            _resolveTitleCard = null;
            resolve?.Invoke();
        }

        /// <summary>
        /// Меняется на каждый Push/Pop/резолв навигатора (Ф4): бутстрап подписывается вместо поллинга структуры
        /// стека в <c>Update</c> — по нему пересчитываются подсветка таба и backdrop (снос части K3/K4).
        /// </summary>
        public event Action Changed
        {
            add => _nav.Changed += value;
            remove => _nav.Changed -= value;
        }

        /// <summary>Бутстрап отдаёт слои-контейнеры (Ф4) и UXML-шаблоны экранов (ссылки из сцены, не DI).</summary>
        public void Initialize(VisualElement screensLayer, VisualElement modalLayer, VisualTreeAsset pauseUxml, VisualTreeAsset settingsUxml,
            VisualTreeAsset loadoutUxml = null, VisualTreeAsset rewardUxml = null, VisualTreeAsset eventUxml = null,
            VisualTreeAsset continueUxml = null, VisualTreeAsset shopUxml = null,
            VisualTreeAsset chestUxml = null, VisualTreeAsset outcomeUxml = null, VisualTreeAsset mainMenuUxml = null,
            VisualTreeAsset loadoutInventoryUxml = null,
            VisualTreeAsset arcanaCardUxml = null, VisualTreeAsset campUxml = null,
            VisualTreeAsset titleCardUxml = null, Sprite titleCardSeal = null,
            VisualTreeAsset devConsoleUxml = null, VisualTreeAsset devLogUxml = null,
            VisualTreeAsset newGameUxml = null, VisualTreeAsset profileUxml = null,
            VisualTreeAsset confirmUxml = null,
            VisualTreeAsset guildSelectUxml = null, VisualTreeAsset hubUxml = null)
        {
            _newGameUxml = newGameUxml;
            _guildSelectUxml = guildSelectUxml;
            _hubUxml = hubUxml;
            _profileUxml = profileUxml;
            _confirmUxml = confirmUxml;
            _devConsoleUxml = devConsoleUxml;
            _devLogUxml = devLogUxml;
            _root = screensLayer; // корень оверлеев = слой экранов (null-guard в Open*); FillRoot растягивает по нему
            _modalLayer = modalLayer;
            _pauseUxml = pauseUxml;
            _settingsUxml = settingsUxml;
            _loadoutUxml = loadoutUxml;
            _rewardUxml = rewardUxml;
            _eventUxml = eventUxml;
            _continueUxml = continueUxml;
            _shopUxml = shopUxml;
            _chestUxml = chestUxml;
            _outcomeUxml = outcomeUxml;
            _mainMenuUxml = mainMenuUxml;
            _loadoutInventoryUxml = loadoutInventoryUxml;
            _arcanaCardUxml = arcanaCardUxml;
            _campUxml = campUxml;
            _titleCardUxml = titleCardUxml;
            _titleCardSeal = titleCardSeal;

            // Навигатор Ф4: два слоя-контейнера. Page/Sheet → screensLayer (под топбаром); Modal (pause/
            // settings) → modalLayer (над топбаром, fullscreen-scrim накрывает его — QA #36). Контекст сборки
            // несёт слой экранов (куда FillRoot-оверлеи растягиваются) и локализатор.
            _nav.Initialize(screensLayer, modalLayer, new UiScreenContext(screensLayer, key => _loc?.GetString(key)));
        }

        // Тонкая обёртка существующего вью-билдера в экран навигатора (Ф2): несёт Kind (видимость/suppress
        // считает навигатор), тег режима для подсветки таба и опц. идентификатор (pause). Билдер вызывается
        // лениво при Push — resolved-guard/Detach-страховки внутри билдеров пока живут (уберутся в Ф3).
        private sealed class RouterScreen : UiScreen
        {
            private readonly Func<VisualElement> _build;
            private readonly Action _onExit;
            public override ScreenKind Kind { get; }
            public override string ModeTag { get; }
            public override bool SuppressScrim { get; }
            public string ScreenId { get; }

            public RouterScreen(ScreenKind kind, Func<VisualElement> build, string modeTag = null,
                                string screenId = null, Action onExit = null, bool suppressScrim = false)
            {
                Kind = kind;
                _build = build;
                ModeTag = modeTag;
                ScreenId = screenId;
                _onExit = onExit;
                SuppressScrim = suppressScrim;
            }

            public override void Build(UiScreenContext ctx) => Root = _build();

            // Владелец persistent-оверлея (тест-зона/инвентарь, Ф5/Ф6) обнуляет свою ссылку при ЛЮБОМ снятии
            // (Pop/PopAll/Remove) — иначе PopAll завершения забега оставил бы висячую ссылку → рассинхрон.
            public override void OnExit() => _onExit?.Invoke();
        }

        /// <summary>Открыто ли главное меню (гейт для ESC и для «настройки без скрима»).</summary>
        private bool _mainMenuOpen;

        // scrimless: модалка не рисует собственное затемнение (настройки из главного меню — там темнить
        // нечего, панель просто подменяет панель). Намерение уезжает СВОЙСТВОМ ЭКРАНА: класс
        // gm-screen--scrimless принадлежит UiNavigator.SyncVisibility, и повешенный здесь руками он
        // тут же перезаписывался обратно — затемнение возвращалось (наход. Макса).
        /// <returns>Положенный экран — тем, кто снимает его не кнопкой «Назад», а сам (цепочка поверх меню).</returns>
        private UiScreen PushScreen(Func<VisualElement> build, ScreenKind kind, string modeTag = null, string screenId = null,
                                    CancellationToken ct = default, Action onExit = null, bool scrimless = false)
        {
            var pushed = new RouterScreen(kind, build, modeTag, screenId, onExit, scrimless);
            _nav.Push(pushed, ct);
            return pushed;
        }

        // Обёртка flow-экрана с результатом (Ф3): вью-билдер получает делегат Resolve и связывает с ним свои
        // колбэки (выбор/пропуск). Навигатор гарантирует РОВНО ОДИН резолв — явный или DefaultResult при снятии
        // без выбора. Снимает нужду в resolved-guard'ах и DetachFromPanelEvent-страховках внутри билдеров.
        private sealed class RouterResultScreen<TResult> : UiScreen<TResult>
        {
            private readonly Func<Action<TResult>, VisualElement> _build;
            private readonly TResult _default;
            public override ScreenKind Kind { get; }
            public override string ModeTag { get; }
            public override TResult DefaultResult => _default;

            public RouterResultScreen(ScreenKind kind, TResult defaultResult,
                Func<Action<TResult>, VisualElement> build, string modeTag = null)
            {
                Kind = kind;
                _default = defaultResult;
                _build = build;
                ModeTag = modeTag;
            }

            public override void Build(UiScreenContext ctx) => Root = _build(Resolve);
        }

        /// <summary>
        /// Снять подписки роутера. Зовёт VContainer при уничтожении контейнера — своей строки вызова
        /// нет и не должно быть.
        /// </summary>
        public void Dispose() => _readySubscription?.Dispose();

        private void Pop() => _nav.Pop();

        /// <summary>
        /// Можно ли показать экран. <b>Не фолбэк, а громкий отказ</b>: неназначенный UXML — это баг разводки
        /// <see cref="UiRootBootstrap"/>, а не режим работы, и раньше каждый такой случай молча выполнял
        /// колбэк УСПЕХА — узел засчитывался, награда пропускалась, игра закрывалась (аудит фолбэков
        /// 2026-07-26, п.1). Шаг петли по-прежнему завершается: зависшая петля забега хуже пропущенного
        /// экрана, — но теперь он завершается с красной ошибкой, а до билда его ловит SceneWiringTests.
        /// </summary>
        /// <param name="screen">Имя экрана и поля в бутстрапе — чтобы ошибку можно было грепнуть.</param>
        /// <param name="uxml">Шаблон экрана.</param>
        /// <param name="payloadOk">Данные запроса на месте (сессия привала, магазин, событие).</param>
        private bool CannotShow(string screen, VisualTreeAsset uxml, bool payloadOk = true)
        {
            if (_root == null)
            {
                Debug.LogError($"[MenuRouter] - экран '{screen}': нет корня UI (UiRootBootstrap не инициализировал роутер) → шаг пропущен");
                return true;
            }
            if (uxml == null)
            {
                Debug.LogError($"[MenuRouter] - экран '{screen}': UXML не назначен в UiRootBootstrap → шаг пропущен");
                return true;
            }
            if (!payloadOk)
            {
                Debug.LogError($"[MenuRouter] - экран '{screen}': пустые данные запроса → шаг пропущен");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Открыть loadout-экран для юнита (по дабл-клику в фазе расстановки; публикуется как
        /// <see cref="OpenLoadoutRequest"/>, бутстрап зовёт сюда). Пушится как полноэкранный оверлей.
        /// </summary>
        public void OpenLoadout(OpenLoadoutRequest req)
        {
            if (CannotShow("Лоадаут (_loadoutScreen)", _loadoutUxml)) return;
            _loadoutVm.Open(req);
            PushScreen(BuildLoadoutScreen, ScreenKind.Page);
        }

        /// <summary>
        /// Полноэкранный лоадаут/инвентарь (редизайн, Ф3a): грид таро-карточек реликвий + детали.
        /// Открывается табом «Инвентарь» в топбаре. <paramref name="onClose"/>
        /// зовётся на ЛЮБОМ закрытии (Pop/Esc/PopAll) через DetachFromPanelEvent — бутстрап по нему
        /// возвращает ран-топбар. Реликвии — весь контент (фильтр по владению — Фаза 5); gold из RunState.
        /// </summary>
        // Инвентарь (Ф6): формальный Sheet-экран навигатора. ТУМБЛЕР — открыт → снять (Remove из любого места
        // стека, даже под паузой), закрыт → построить и Push. Владеет экраном роутер (ссылка _inventoryScreen,
        // обнуляется onExit при ЛЮБОМ снятии — смерть _inventoryOpen/onClose-Detach в бутстрапе, K6). Закрытие
        // НЕ через PopAll → карта петли акта под инвентарём НЕ сносится (нет Aborted, класс #31).
        private UiScreen _inventoryScreen;

        /// <summary>Открыт ли инвентарь (для backdrop-логики бутстрапа — единый источник вместо флага).</summary>
        public bool IsInventoryOpen => _inventoryScreen != null;

        /// <summary>Есть ли карта петли акта в стеке (result-экран выбора узла) — скрытая под геймплеем или видимая.
        /// Отличает «вернуться на карту петли» (выйти из боя) от read-only просмотра (нет карты петли).</summary>
        public bool HasMapInStack => _nav.AnyScreen(s => s.ModeTag == UiScreen.MapModeTag);

        /// <summary>Показать инвентарь (радио-режим): Sheet-тело поверх геймплея. Идемпотентно (уже открыт → no-op).</summary>
        public void ShowInventory(int gold, Action<RelicData, RelicDragPhase> onRelicDrag = null)
        {
            if (_inventoryScreen != null) { UiTrace.Log("router.ShowInventory: уже открыт → no-op"); return; }
            if (CannotShow("Инвентарь (_loadoutInventoryScreen)", _loadoutInventoryUxml)) return;
            if (CannotShow("Карточка аркана (_arcanaCard)", _arcanaCardUxml)) return;
            UiTrace.Log("router.ShowInventory: Push inventory Sheet");
            _inventoryScreen = new RouterScreen(ScreenKind.Sheet, () => BuildInventory(gold, onRelicDrag),
                                                modeTag: UiScreen.InventoryModeTag, onExit: () => _inventoryScreen = null);
            _nav.Push(_inventoryScreen); // QA #21: тег режима подсвечивает таб
        }

        /// <summary>Снять инвентарь (радио-режим). Идемпотентно (не открыт → no-op). Remove из любого места стека.</summary>
        public void HideInventory()
        {
            UiTrace.Log($"router.HideInventory: {(_inventoryScreen != null ? "Remove inventory" : "нет экрана → no-op")}");
            if (_inventoryScreen != null) _nav.Remove(_inventoryScreen); // OnExit обнулит ссылку
        }

        private VisualElement BuildInventory(int gold, Action<RelicData, RelicDragPhase> onRelicDrag)
        {
            VisualElement screen = LoadoutInventoryView.Build(
                _loadoutInventoryUxml, _arcanaCardUxml,
                _loadoutVm.Relics, gold,
                titleOf: r => ContentTitle.Arcana(r != null ? r.Id : null),
                narrativeOf: r => _loadoutVm.Desc(r),
                localize: key => _loc?.GetString(key),
                lockedSlots: 0,
                cardAnimations: _settingsVm.CardAnimations,
                cardAttackAnimation: _settingsVm.CardAttackAnimation,
                onRelicDrag: onRelicDrag, // QA #5: drag карточки реликвии на юнита в мире
                tagsOf: r => _loadoutVm.ResolveTags(r),   // теги «быстрого чтения» из данных релика
                statsOf: r => _loadoutVm.ResolveStats(r), // базовые статы тем же каскадом, что у боя
                palette: _palette);                       // цвет приглушения тела — как в бою

            // Инвентарь = ТОЛЬКО тело; навигация (режимы) и меню — в глобальном топбаре (RunModeBar). Sheet:
            // навигатор НЕ глушит геймплей — под инвентарём живут юниты/камера (клики разводит PointerOverUI над
            // панелями vs дыркой). Класс gm-screen--transparent — только СТИЛЬ (прозрачный фон), suppress = Kind.
            screen.AddToClassList(TransparentScreenClass);
            return screen;
        }

        // Тест-зона (Ф5): «геймплей»-пространство = Sheet-экран навигатора (ModeTag "battle", прозрачный корень,
        // pickingMode Ignore — мир под ним живёт и кликается). Карта петли акта (Page) под ним прячется правилом
        // видимости (Sheet скрывает Page) — БЕЗ ручного HideTopForTest (снос K5). Показ/скрытие — по СОСТОЯНИЮ
        // TestZoneChangedEvent (бутстрап), владелец состояния — DeploymentController. Идемпотентно.
        private UiScreen _testZoneScreen;

        /// <summary>Войти в UI тест-зоны: Sheet-пространство «Бой» поверх стека (карта под ним прячется).</summary>
        public void ShowTestZone()
        {
            if (_testZoneScreen != null) { UiTrace.Log("router.ShowTestZone: уже показан → no-op"); return; }
            UiTrace.Log("router.ShowTestZone: Push test-zone Sheet");
            _testZoneScreen = new RouterScreen(ScreenKind.Sheet, BuildTestZoneSpace, modeTag: UiScreen.BattleModeTag,
                                               onExit: () => _testZoneScreen = null);
            _nav.Push(_testZoneScreen);
        }

        /// <summary>Выйти из UI тест-зоны: снять Sheet-пространство из любого места стека (даже под инвентарём).</summary>
        public void HideTestZone()
        {
            UiTrace.Log($"router.HideTestZone: {(_testZoneScreen != null ? "Remove test-zone" : "нет экрана → no-op")}");
            if (_testZoneScreen != null) _nav.Remove(_testZoneScreen); // OnExit обнулит _testZoneScreen
        }

        // World-карта (фаза D): сама карта живёт в мире, а UI держит лишь прозрачное Sheet-пространство —
        // ради тега режима (подсветка таба «Карта») и контекста ввода (навигатор ставит InputContext.Map,
        // где world-камера жива). Показ/скрытие — по СОСТОЯНИЮ WorldMapSpaceChangedEvent (бутстрап),
        // владелец состояния — WorldMapNodeChooser. Идемпотентно, как тест-зона.
        private UiScreen _mapSpaceScreen;

        /// <summary>Показана ли world-карта: её пространство есть в стеке. Фон забега при этом гасится — карта
        /// теперь рисуется В МИРЕ, и непрозрачный backdrop просто закрыл бы её собой.</summary>
        public bool IsMapSpaceOpen => _mapSpaceScreen != null;

        /// <summary>Лежит ли на экране непрозрачная страница (ивент/магазин/сундук/награда/исход) — ей нужен
        /// задник-стол вместо просвечивающего мира (QA #50).</summary>
        public bool HasVisiblePage => _nav.HasVisiblePage;

        /// <summary>Войти в пространство world-карты: прозрачный Sheet с тегом режима «карта».</summary>
        public void ShowMapSpace()
        {
            if (_mapSpaceScreen != null) { UiTrace.Log("router.ShowMapSpace: уже показан → no-op"); return; }
            UiTrace.Log("router.ShowMapSpace: Push map Sheet");
            _mapSpaceScreen = new RouterScreen(ScreenKind.Sheet, BuildMapSpace, modeTag: UiScreen.MapModeTag,
                                               onExit: () => _mapSpaceScreen = null);
            _nav.Push(_mapSpaceScreen);
        }

        /// <summary>Выйти из пространства world-карты: снять Sheet из любого места стека.</summary>
        public void HideMapSpace()
        {
            UiTrace.Log($"router.HideMapSpace: {(_mapSpaceScreen != null ? "Remove map space" : "нет экрана → no-op")}");
            if (_mapSpaceScreen != null) _nav.Remove(_mapSpaceScreen); // OnExit обнулит _mapSpaceScreen
        }

        // Прозрачное «окно в мир» карты — та же роль, что у пространства тест-зоны: контента не рисует,
        // ввод не ловит (клики уходят в мир через PointerOverUI), несёт лишь режим.
        private static VisualElement BuildMapSpace()
        {
            var space = new VisualElement { name = "map-space", pickingMode = PickingMode.Ignore };
            space.style.position = Position.Absolute;
            space.style.left = 0; space.style.top = 0; space.style.right = 0; space.style.bottom = 0;
            return space;
        }

        // Прозрачное полноэкранное «окно в мир» тест-зоны: не рисует контента (мир виден сквозь), не ловит ввод
        // (Ignore — клики идут в мир через PointerOverUI). Несёт лишь роль «мы в геймплей-пространстве тест-зоны».
        private static VisualElement BuildTestZoneSpace()
        {
            var space = new VisualElement { name = "test-zone-space", pickingMode = PickingMode.Ignore };
            space.style.position = Position.Absolute;
            space.style.left = 0; space.style.top = 0; space.style.right = 0; space.style.bottom = 0;
            return space;
        }

        // Титул таро-карты в стиле ГДД (аркан «The X»): «relic.flame_swordsman» → «The Flame Swordsman».
        // QA #12: ☰/ESC ОТКРЫВАЮТ системное меню ПОВЕРХ текущего экрана (инвентарь/карта/бой), не закрывая
        // его. Если мы уже в меню (pause в стеке — даже под настройками) — шаг назад (settings→pause→закрыть).
        public void ToggleSystemMenu()
        {
            if (_root == null)
            {
                Debug.LogError("[MenuRouter] - системное меню: нет корня UI (UiRootBootstrap не инициализировал роутер) → ESC не работает");
                return;
            }
            // В главном меню системного меню нет: выходить из игры некуда, а ESC-панель поверх главного
            // меню — просто баг (наход. Макса, раунд 3, п.3).
            if (_mainMenuOpen) return;
            bool inMenu = _nav.AnyScreen(s => s is RouterScreen r && r.ScreenId == PauseId);
            if (inMenu) _nav.Pop();                                          // уже в системном меню → назад/закрыть
            else PushScreen(BuildPauseScreen, ScreenKind.Modal, screenId: PauseId); // QA #19: меню ПОВЕРХ (Modal со scrim)
        }

        /// <summary>
        /// Открыть/закрыть dev-консоль (Трек К). Экран живёт ОДНИМ инстансом между показами: в нём история
        /// команд, и пересоздание стирало бы её при каждом закрытии.
        /// </summary>
        /// <remarks>
        /// Консоль не подчиняется правилу «в главном меню оверлеев нет» (в отличие от ESC-меню): её
        /// открывают в том числе чтобы разобраться, почему игра не дошла дальше главного меню.
        /// </remarks>
        public void ToggleDevConsole()
        {
            if (_root == null)
            {
                Debug.LogError("[MenuRouter] - dev-консоль: нет корня UI (роутер не инициализирован)");
                return;
            }

            if (_devConsoleUxml == null)
            {
                Debug.LogError("[MenuRouter] - dev-консоль: не разведён UXML (поле _devConsoleScreen в UiRootBootstrap)");
                return;
            }

            if (_devConsole != null && _nav.AnyScreen(s => ReferenceEquals(s, _devConsole)))
            {
                _nav.Remove(_devConsole);
                DevConsoleVisibilityChanged?.Invoke(false);
                return;
            }

            CloseDevOverlays();

            // Свой буфер, НЕ общий с лог-консолью: здесь идёт разговор «спросил — ответили», и поток
            // Debug.Log из боя затопил бы его за секунды. Логи движка живут на F2.
            _devConsole ??= new DevConsoleScreen(_devConsoleUxml, _registry, new Core.DevConsole.DevConsoleLog());
            _nav.Push(_devConsole);
            DevConsoleVisibilityChanged?.Invoke(true);
        }

        /// <summary>
        /// Снять все открытые dev-полки (командная консоль, лог, витрина боёв). Полки взаимоисключающи:
        /// вторая поверх первой — это две простыни внахлёст, в которых не читается ни одна.
        /// </summary>
        /// <remarks>
        /// Ищем по маркеру <see cref="IDevOverlayScreen"/>, а не по типам: витрину боёв показывает
        /// dev-модуль, и роутер о ней ничего не знает — знать и не должен.
        /// </remarks>
        public void CloseDevOverlays()
        {
            var open = new List<UiScreen>();
            _nav.AnyScreen(s =>
            {
                if (s is IDevOverlayScreen) open.Add(s);
                return false;   // обходим весь стек, а не ищем первое совпадение
            });

            for (int i = 0; i < open.Count; i++)
            {
                _nav.Remove(open[i]);
                if (ReferenceEquals(open[i], _devConsole)) DevConsoleVisibilityChanged?.Invoke(false);
            }
        }

        /// <summary>Открыть/закрыть лог-консоль (F2): хвост сообщений движка, без строки ввода.</summary>
        public void ToggleDevLog()
        {
            if (_root == null)
            {
                Debug.LogError("[MenuRouter] - лог-консоль: нет корня UI (роутер не инициализирован)");
                return;
            }

            if (_devLogUxml == null)
            {
                Debug.LogError("[MenuRouter] - лог-консоль: не разведён UXML (поле _devLogScreen в UiRootBootstrap)");
                return;
            }

            if (_devLog != null && _nav.AnyScreen(s => ReferenceEquals(s, _devLog)))
            {
                _nav.Remove(_devLog);
                _log?.Detach();   // парный к Attach ниже: обещание «пока есть кому смотреть» держит хозяин
                return;
            }

            CloseDevOverlays();

            _devLog ??= new DevLogScreen(_devLogUxml, _log);
            _log?.Attach();   // хвост копится, только пока его есть кому смотреть
            _nav.Push(_devLog);
        }

        /// <summary>
        /// Консоль показана (<c>true</c>) или снята (<c>false</c>). Слушают те, кому мало глушения ввода:
        /// dev-модуль ставит на это время паузу симуляции, иначе бой доигрывает за полкой невидимым.
        /// </summary>
        public event Action<bool> DevConsoleVisibilityChanged;

        // Закрыть все экраны и снять глушение (навигатор пересчитает suppress из фазы). Внутренний close-callback
        // текстового ивента (выбор без результата = снять стек). НЕ для завершения забега (то — единая отмена, K11).
        private void CloseAll() => _nav.PopAll();

        /// <summary>Режим-таб верхнего оверлея (QA #21), из констант <see cref="UiScreen"/>. Единый источник подсветки топбара.</summary>
        public string ActiveScreenMode => _nav.ActiveModeTag;

        /// <summary>
        /// Открыто ли системное меню (пауза или что-либо поверх неё). Топбар держит по этому флагу
        /// таб настроек нажатым, пока меню на экране (наход. Макса, раунд 2, п.6).
        /// </summary>
        public bool IsSystemMenuOpen => _nav.AnyScreen(s => s is RouterScreen r && r.ScreenId == PauseId);

        // Прозрачные оверлеи (инвентарь) НЕ глушат геймплей — под ними живёт мир (юниты/камера, развязка
        // через IInputService.PointerOverUI). Класс остаётся ТОЛЬКО как стиль (прозрачный фон); поведение
        // suppress теперь определяет ScreenKind.Sheet в навигаторе, а не наличие этого класса.
        private const string TransparentScreenClass = "gm-screen--transparent";

        // Маркер экрана системного меню (pause) для стиля/якорей UXML. Логика «мы в меню» — по RouterScreen.ScreenId.
        private const string PauseScreenClass = "gm-pause-root";

        // --- Экраны ---

        private VisualElement BuildPauseScreen()
        {
            var screen = FillRoot(_pauseUxml.CloneTree());
            screen.AddToClassList(PauseScreenClass); // стилевой маркер «системное меню»
            // «Продолжить» = снять ТОЛЬКО системное меню (Pop), а не весь стек (CloseAll снёс бы карту под паузой
            // → resolve узла null → Aborted, тот же баг класса #37). Экраны под меню (карта/инвентарь) остаются.
            screen.Q<Button>("btn-return").clicked += Pop;
            screen.Q<Button>("btn-settings").clicked += () => PushScreen(BuildSettingsScreen, ScreenKind.Modal);

            // Приглашение живёт ЗДЕСЬ, а не в главном меню: лобби поднимается вместе с игрой, и до
            // входа звать друга некуда. Экран не закрываем — оверлей Steam ложится поверх, игрок
            // возвращается в ту же паузу.
            var invite = screen.Q<Button>("btn-invite");
            if (invite != null)
            {
                invite.text = Loc("ui.menu.invite", "Пригласить друга");
                invite.SetEnabled(_coop?.CanInvite ?? false);
                invite.clicked += () => _coop?.InviteFriend();
            }

            // QA #18/#37: «В главное меню» прерывает забег ЕДИНОЙ отменой (токен) — снять меню (Pop) + отменить
            // забег; отмена сама закрывает открытый экран забега (карта/награда/…) через навигатор и всплывает
            // OperationCanceledException в GameFlow → главное меню. Никакого CloseAll-веника (снос K11). Сейв цел.
            var toMenu = screen.Q<Button>("btn-main-menu");
            if (toMenu != null)
            {
                toMenu.text = Loc("ui.menu.to_main_menu", "В главное меню");
                toMenu.clicked += () => { Pop(); _runControl?.RequestReturnToMainMenu(); };
            }
            var quit = screen.Q<Button>("btn-quit");
            if (quit != null)
            {
                quit.text = Loc("ui.menu.quit", "Выйти из игры");
                quit.clicked += () => { ShowQuitVeil(); _runControl?.RequestQuit(); };
            }
            return screen;
        }

        // Локализованная строка с RU-фолбэком (весь новый UI на code-fallback до записи в String Table).
        private string Loc(string key, string ru)
        {
            string v = _loc?.GetString(key);
            return string.IsNullOrEmpty(v) ? ru : v;
        }

        /// <summary>
        /// Экран «Создать игру»: режим и галочка лобби. Открывается поверх главного меню.
        /// </summary>
        /// <remarks>
        /// <b>Экран не решает, куда идти дальше, — решает режим.</b> Площадка и матч уходят в игру
        /// кликом (дома у них нет), Кампания ведёт на выбор дома. Кнопки «Начать» здесь больше нет:
        /// она была третьим шагом после двух выборов, и первые два уже отвечали на всё (реш. Макса
        /// 04.08.2026).
        /// </remarks>
        /// <param name="pushOver">
        /// Чем открыть следующий экран цепочки. Своим <c>PushScreen</c> здесь нельзя: цепочку поверх
        /// меню снимает её хозяин одним махом, и экран, положенный мимо него, пережил бы уборку.
        /// </param>
        private VisualElement BuildNewGameScreen(Action<GameStartRequest> onStart, Action<Func<VisualElement>> pushOver)
        {
            if (CannotShow("Создать игру (_newGameScreen)", _newGameUxml)) return new VisualElement();

            return NewGameScreenView.Build(
                _newGameUxml,
                _coop?.IsSteamReady ?? false,
                key => _loc?.GetString(key),
                onPick: (mode, lobby) =>
                {
                    if (mode != GameMode.Campaign) { onStart?.Invoke(new GameStartRequest(mode, null, lobby)); return; }

                    // Дом — следующий экран, а заказ собирается там: сюда он уже не вернётся, поэтому
                    // галочку лобби несём с собой, а не спрашиваем повторно.
                    pushOver?.Invoke(() => BuildGuildSelectScreen(lobby, onStart));
                },
                onBack: Pop);
        }

        /// <summary>
        /// Экран выбора дома (только Кампания): слоты гильдий, свободные — под новую.
        /// </summary>
        private VisualElement BuildGuildSelectScreen(bool onlineLobby, Action<GameStartRequest> onStart)
        {
            if (CannotShow("Гильдия (_guildSelectScreen)", _guildSelectUxml)) return new VisualElement();

            return GuildSelectScreenView.Build(
                _guildSelectUxml,
                GuildSelectScreenView.ReadGuilds(_profiles, _save),
                _guildSlotLimit,
                key => _loc?.GetString(key),
                onPick: guildId => onStart?.Invoke(new GameStartRequest(GameMode.Campaign, guildId, onlineLobby)),
                onBack: Pop);
        }

        /// <summary>
        /// Двор гильдии между выбором дома и забегом. Пока заглушка с единственной дверью наружу.
        /// </summary>
        public void OpenHub(OpenHubRequest req)
        {
            // Отказ здесь ЗАВЕРШАЕТ шаг: без двора игрок остался бы стоять между домом и актом, и
            // забег не начался бы никогда. Экран пропущен — забег идёт, но ошибка красная.
            if (CannotShow("Двор гильдии (_hubScreen)", _hubUxml)) { req.OnStartRun?.Invoke(); return; }

            var screen = new RouterResultScreen<bool>(ScreenKind.Page, true,
                resolve => HubScreenView.Build(_hubUxml, req.GuildName, key => _loc?.GetString(key),
                                               onStartRun: () => resolve(true)));

            _hubScreen = screen; // «двор открыт» — это ссылка на его экран, и другого владельца у факта нет
            ShowHubAsync(screen, req).Forget();
        }

        private async UniTaskVoid ShowHubAsync(RouterResultScreen<bool> screen, OpenHubRequest req)
        {
            await _nav.ShowAsync(screen);
            if (ReferenceEquals(_hubScreen, screen)) _hubScreen = null;
            req.OnStartRun?.Invoke();
        }

        // ── Двор глазами сеанса (IHubPresence) ───────────────────────────────
        // Хост объявляет гостю «где мы», и двор — часть этого ответа. Открывает его петля игры, а петли
        // у гостя нет: без этого шва он оставался там, где его застало подключение (наход. Макса
        // 04.08.2026, прогон вдвоём).

        /// <summary>Экран двора, пока он на стеке. Он же ответ на вопрос «открыт ли двор».</summary>
        private RouterResultScreen<bool> _hubScreen;

        bool Core.Flow.IHubPresence.IsShown => _hubScreen != null;

        /// <summary>
        /// Открыть или закрыть двор по объявлению хоста.
        /// </summary>
        /// <remarks>
        /// Имени дома у гостя нет и не будет: двор здесь — МЕСТО, а чей он, приезжает состоянием забега
        /// своим каналом. <c>OnStartRun</c> тоже пуст — забег начинает владелец, и гостевой экран,
        /// закрывшись сам, не должен никого никуда отправлять.
        /// </remarks>
        void Core.Flow.IHubPresence.SetVisible(bool visible)
        {
            if (visible == (_hubScreen != null)) return; // применяется целиком и каждый раз — повтор штатен

            if (visible) { OpenHub(new OpenHubRequest(null, null)); return; }

            RouterResultScreen<bool> screen = _hubScreen;
            _hubScreen = null;
            _nav.Remove(screen);
        }

        /// <summary>
        /// Спросить подтверждение необратимого действия. <c>true</c> — игрок согласился.
        /// </summary>
        /// <remarks>
        /// <b>Модалка поверх текущего экрана, а не вместо него.</b> Вопрос всегда про то, что игрок
        /// сейчас видит («удалить ЭТОТ слот»), и убрать контекст из-под вопроса значит заставить его
        /// вспоминать, о чём речь.
        /// <para><b>Умолчание — отказ:</b> снятие экрана мимо кнопок (Esc, отмена сверху) читается как
        /// «нет». У необратимого действия любая неясность обязана падать в безопасную сторону.</para>
        /// </remarks>
        public async UniTask<bool> ConfirmAsync(string title, string body, string consequence, string confirmText)
        {
            if (CannotShow("Подтверждение (_confirmDialog)", _confirmUxml)) return false;

            var screen = new RouterResultScreen<bool>(ScreenKind.Modal, false, resolve =>
            {
                VisualElement root = FillRoot(_confirmUxml.CloneTree());

                var titleLabel = root.Q<Label>("confirm-title");
                var bodyLabel  = root.Q<Label>("confirm-body");
                var consLabel  = root.Q<Label>("confirm-consequence");
                var cancel     = root.Q<Button>("btn-cancel");
                var confirm    = root.Q<Button>("btn-confirm");

                if (titleLabel != null) titleLabel.text = title;
                if (bodyLabel  != null) bodyLabel.text  = body;

                if (consLabel != null)
                {
                    consLabel.text = consequence ?? string.Empty;
                    if (string.IsNullOrEmpty(consequence)) consLabel.style.display = DisplayStyle.None;
                }

                if (cancel != null)
                {
                    cancel.text = _loc?.GetString("ui.confirm.cancel") is { Length: > 0 } c ? c : "Отмена";
                    cancel.clicked += () => resolve(false);
                }

                if (confirm != null)
                {
                    confirm.text = confirmText;
                    confirm.clicked += () => resolve(true);
                }

                return root;
            });

            return await _nav.ShowAsync(screen);
        }

        /// <summary>
        /// Показать профиль. <paramref name="required"/> — профиля нет вовсе: экран открывается без
        /// «Назад» и закрывается сам, как только слот заведён.
        /// </summary>
        public void OpenProfile(OpenProfileRequest req)
        {
            if (CannotShow("Профиль (_profileScreen)", _profileUxml)) { req.OnClosed?.Invoke(); return; }

            PushScreen(() => BuildProfileScreen(req.Required, req.OnClosed),
                       ScreenKind.Modal, scrimless: _mainMenuOpen);
        }

        private VisualElement BuildProfileScreen(bool required, Action onClosed)
        {
            void Rebuild()
            {
                // Список слотов и активный профиль поменялись — экран пересобирается целиком. Точечная
                // правка строк стоила бы своего кода ради экрана, который открывают раз в сессию.
                Pop();
                PushScreen(() => BuildProfileScreen(required, onClosed), ScreenKind.Modal, scrimless: _mainMenuOpen);
            }

            var slots = new List<ProfileScreenView.SlotEntry>();
            if (_profiles != null)
            {
                string activeId = _profiles.ActiveProfile.Id;
                for (int i = 0; i < _profiles.Profiles.Count; i++)
                {
                    Core.Persistence.ProfileSummary p = _profiles.Profiles[i];
                    slots.Add(new ProfileScreenView.SlotEntry(p.Id, p.Name, p.Id == activeId));
                }
            }

            bool canLeave = !required || (_profiles?.HasActiveProfile ?? false);

            return FillRoot(ProfileScreenView.Build(
                _profileUxml,
                slots,
                _profileSlotLimit,
                _profiles?.Identity ?? default,
                _steamName?.Invoke() ?? "Игрок",
                _cursorSkins?.Skins,
                ProfileColorCount,
                canLeave,
                key => _loc?.GetString(key),
                onSelect: id => { _profiles?.SelectProfile(id); Rebuild(); },
                onCreate: () =>
                {
                    if (_profiles?.CreateProfile() == null) return;

                    // Обязательный показ существует ради одного события — появления профиля. Оно
                    // случилось, держать игрока больше не на чем.
                    if (required) { Pop(); onClosed?.Invoke(); return; }
                    Rebuild();
                },
                onDelete: id => ConfirmDeleteAsync(id, Rebuild).Forget(),
                onSave: identity =>
                {
                    _profiles?.SaveIdentity(identity);
                    _cursorApply?.Invoke(identity.CursorSkinId);
                },
                onBack: () => { Pop(); onClosed?.Invoke(); }));
        }

        /// <summary>
        /// Спросить и снести профиль. Отдельным методом, потому что вопрос асинхронный, а обработчик
        /// кнопки — нет: держать здесь <c>async void</c> значило бы терять исключения молча.
        /// </summary>
        private async UniTaskVoid ConfirmDeleteAsync(string profileId, Action onDone)
        {
            string name = profileId;
            if (_profiles != null)
            {
                for (int i = 0; i < _profiles.Profiles.Count; i++)
                    if (_profiles.Profiles[i].Id == profileId) name = _profiles.Profiles[i].Name;
            }

            bool yes = await ConfirmAsync(
                _loc?.GetString("ui.profile.delete.title") is { Length: > 0 } t ? t : "Удалить профиль?",
                $"{name}",
                // Последствие названо числом домов, а не словом «всё»: «все гильдии» звучит абстрактно,
                // «три дома вместе с их забегами» — нет.
                DeleteConsequence(profileId),
                _loc?.GetString("ui.profile.delete") is { Length: > 0 } d ? d : "Удалить");

            if (!yes) return;

            _profiles?.DeleteProfile(profileId);
            onDone?.Invoke();
        }

        /// <summary>Что именно пропадёт вместе с профилем.</summary>
        private string DeleteConsequence(string profileId)
        {
            // Дома чужого профиля не спросить, не переключившись на него, — а переключение ради текста
            // диалога сменило бы игроку активный слот. Поэтому число домов называем только для текущего.
            bool isActive = _profiles != null && _profiles.ActiveProfile.Id == profileId;
            if (!isActive) return "Вместе с ним пропадут его дома и все их забеги. Это необратимо.";

            int guilds = _profiles.Guilds.Count;
            string homes = guilds == 1 ? "один дом" : $"{guilds} дома";
            return $"Вместе с ним пропадут {homes} и все их забеги. Это необратимо.";
        }

        private VisualElement BuildSettingsScreen()
        {
            var screen = FillRoot(_settingsUxml.CloneTree());

            // Подписи через loc с RU-фолбэком (как остальной новый UI); значения проводятся из VM.
            string L(string key, string ru) { string v = _loc?.GetString(key); return string.IsNullOrEmpty(v) ? ru : v; }

            var master = screen.Q<Guildmaster.UI.Components.SliderRow>("row-master");
            var music  = screen.Q<Guildmaster.UI.Components.SliderRow>("row-music");
            var sfx    = screen.Q<Guildmaster.UI.Components.SliderRow>("row-sfx");
            if (master != null) master.LabelText = L("ui.settings.volume_master", "Общий");
            if (music  != null) music.LabelText  = L("ui.settings.volume_music", "Музыка");
            if (sfx    != null) sfx.LabelText    = L("ui.settings.volume_sfx", "Звук");

            // Таб «Игра»: тумблеры презентации (анимация карточек / анимация атаки).
            var cardAnim   = screen.Q<Guildmaster.UI.Components.ToggleRow>("toggle-card-anim");
            var cardAttack = screen.Q<Guildmaster.UI.Components.ToggleRow>("toggle-card-attack");
            var tipDetails = screen.Q<Guildmaster.UI.Components.ToggleRow>("toggle-tooltip-details");
            if (cardAnim   != null) cardAnim.LabelText   = L("ui.settings.card_anim", "Анимация карточек");
            if (cardAttack != null) cardAttack.LabelText = L("ui.settings.card_attack", "Анимация атаки карточек");
            // §II.10.4: галка «всегда подробно». Shift при ней работает наоборот — временно даёт краткий вид.
            if (tipDetails != null) tipDetails.LabelText = L("ui.settings.tooltip_details", "Всегда подробные подсказки");

            // Таб «Графика»: дисплей. Списки живые — их наполняет Sync, потому что набор частот зависит
            // от выбранного разрешения и меняется прямо во время правки.
            var windowMode = screen.Q<Guildmaster.UI.Components.SelectRow>("row-window-mode");
            var resolution = screen.Q<Guildmaster.UI.Components.SelectRow>("row-resolution");
            var refreshRow = screen.Q<Guildmaster.UI.Components.SelectRow>("row-refresh-rate");
            var videoHint  = screen.Q<Label>("video-hint");
            if (windowMode != null) windowMode.LabelText = L("ui.settings.window_mode", "Режим окна");
            if (resolution != null) resolution.LabelText = L("ui.settings.resolution", "Разрешение");
            if (refreshRow != null) refreshRow.LabelText = L("ui.settings.refresh_rate", "Частота обновления");

            string ModeLabel(Core.Settings.WindowMode m) => m switch
            {
                Core.Settings.WindowMode.ExclusiveFullscreen => L("ui.settings.window_mode.exclusive", "Полноэкранный"),
                Core.Settings.WindowMode.Windowed            => L("ui.settings.window_mode.windowed", "Оконный"),
                _                                            => L("ui.settings.window_mode.borderless", "Окно без рамок"),
            };

            _settingsVm.BeginEdit();

            // SliderRow/ToggleRow сами обновляют свой вид (в т.ч. в SetValueWithoutNotify).
            void Sync()
            {
                master.SetValueWithoutNotify(_settingsVm.Master);
                music.SetValueWithoutNotify(_settingsVm.Music);
                sfx.SetValueWithoutNotify(_settingsVm.Sfx);
                cardAnim?.SetValueWithoutNotify(_settingsVm.CardAnimations);
                cardAttack?.SetValueWithoutNotify(_settingsVm.CardAttackAnimation);
                tipDetails?.SetValueWithoutNotify(_settingsVm.AlwaysDetailedTooltips);
                // «Атака» осмысленна только при включённой анимации карточек.
                cardAttack?.SetEnabled(_settingsVm.CardAnimations);

                SyncDisplay();
            }

            void SyncDisplay()
            {
                if (windowMode != null)
                {
                    var modes = new List<string>();
                    foreach (Core.Settings.WindowMode m in SettingsViewModel.WindowModes) modes.Add(ModeLabel(m));
                    windowMode.SetChoices(modes, _settingsVm.WindowModeIndex);
                }

                if (resolution != null)
                {
                    var items = new List<string>();
                    foreach ((int w, int h) in _settingsVm.Resolutions) items.Add($"{w} x {h}");
                    resolution.SetChoices(items, _settingsVm.ResolutionIndex);
                }

                if (refreshRow != null)
                {
                    var rates = new List<string>();
                    foreach (RefreshRate r in _settingsVm.RefreshRates) rates.Add($"{r.value:0.##} Гц");
                    refreshRow.SetChoices(rates, _settingsVm.RefreshRateIndex);

                    // Вне эксклюзивного полноэкранного частоту держит композитор рабочего стола —
                    // гасим строку вместо того, чтобы предлагать выбор без эффекта.
                    refreshRow.SetRowEnabled(_settingsVm.RefreshRateSelectable);
                }

                if (videoHint != null)
                {
                    bool locked = !_settingsVm.RefreshRateSelectable;
                    videoHint.text = locked
                        ? L("ui.settings.refresh_rate.locked",
                            "Частоту обновления можно менять только в полноэкранном режиме.")
                        : string.Empty;
                    videoHint.EnableInClassList("gm-tab-page--hidden", !locked);
                }
            }

            Sync();

            master.Slider.RegisterValueChangedCallback(e => _settingsVm.SetMaster(e.newValue));
            music.Slider.RegisterValueChangedCallback(e => _settingsVm.SetMusic(e.newValue));
            sfx.Slider.RegisterValueChangedCallback(e => _settingsVm.SetSfx(e.newValue));
            cardAnim?.Toggle.RegisterValueChangedCallback(e => _settingsVm.SetCardAnimations(e.newValue));
            cardAttack?.Toggle.RegisterValueChangedCallback(e => _settingsVm.SetCardAttackAnimation(e.newValue));
            tipDetails?.Toggle.RegisterValueChangedCallback(e => _settingsVm.SetAlwaysDetailedTooltips(e.newValue));

            windowMode?.Dropdown.RegisterValueChangedCallback(_ => _settingsVm.SetWindowMode(windowMode.Index));
            resolution?.Dropdown.RegisterValueChangedCallback(_ => _settingsVm.SetResolution(resolution.Index));
            refreshRow?.Dropdown.RegisterValueChangedCallback(_ => _settingsVm.SetRefreshRate(refreshRow.Index));

            // VM → контролы (Defaults/Cancel меняют значения «снаружи»). Отписка при снятии с панели.
            Action onChanged = Sync;
            _settingsVm.Changed += onChanged;
            Action onDisplayChanged = SyncDisplay;
            _settingsVm.DisplayChanged += onDisplayChanged;
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _settingsVm.Changed -= onChanged;
                _settingsVm.DisplayChanged -= onDisplayChanged;
            });

            screen.Q<Button>("btn-save").clicked += () => { _settingsVm.Save(); Pop(); };
            screen.Q<Button>("btn-cancel").clicked += () => { _settingsVm.Cancel(); Pop(); };
            screen.Q<Button>("btn-defaults").clicked += () => _settingsVm.ResetToDefaults();

            WireSettingsTabs(screen);
            return screen;
        }

        // Табы настроек (Игра/Графика/Звук): клик показывает свою страницу и прячет прочие. Раскладка и
        // стиль — из UXML/USS. Публичный, потому что тем же переключением пользуется UI-стенд превью:
        // иначе страницу «Графика» нельзя посмотреть, не поднимая весь бут игры.
        public static void WireSettingsTabs(VisualElement screen)
        {
            var tabGame  = screen.Q<Button>("tab-game");
            var tabVideo = screen.Q<Button>("tab-video");
            var tabAudio = screen.Q<Button>("tab-audio");
            var pageGame  = screen.Q<VisualElement>("page-game");
            var pageVideo = screen.Q<VisualElement>("page-video");
            var pageAudio = screen.Q<VisualElement>("page-audio");
            if (tabGame == null || tabVideo == null || tabAudio == null) return;

            void Show(Button tab, VisualElement page)
            {
                tabGame.EnableInClassList("gm-tab--active", tab == tabGame);
                tabVideo.EnableInClassList("gm-tab--active", tab == tabVideo);
                tabAudio.EnableInClassList("gm-tab--active", tab == tabAudio);
                pageGame?.EnableInClassList("gm-tab-page--hidden", page != pageGame);
                pageVideo?.EnableInClassList("gm-tab-page--hidden", page != pageVideo);
                pageAudio?.EnableInClassList("gm-tab-page--hidden", page != pageAudio);
            }

            tabGame.clicked  += () => Show(tabGame, pageGame);
            tabVideo.clicked += () => Show(tabVideo, pageVideo);
            tabAudio.clicked += () => Show(tabAudio, pageAudio);
        }

        private VisualElement BuildLoadoutScreen()
        {
            var screen = FillRoot(_loadoutUxml.CloneTree());

            var grid       = screen.Q<ScrollView>("relic-grid");
            var detailName = screen.Q<Label>("detail-name");
            var detailDesc = screen.Q<Label>("detail-desc");
            var detailTags = screen.Q<Label>("detail-tags");
            var detailStats = screen.Q<Label>("detail-stats");

            grid.contentContainer.AddToClassList("gm-grid");
            var cards = new List<(RelicData relic, VisualElement card)>();

            void ShowDetail(RelicData r)
            {
                detailName.text  = _loadoutVm.Name(r);
                detailDesc.text  = _loadoutVm.Desc(r);
                detailTags.text  = _loadoutVm.Tags(r);
                detailStats.text = _loadoutVm.StatsSummary(r);
            }

            void RefreshCards()
            {
                foreach (var (relic, card) in cards)
                {
                    card.EnableInClassList("gm-card--selected", _loadoutVm.IsSelected(relic));
                    card.EnableInClassList("gm-card--current", _loadoutVm.IsCurrent(relic));
                }
            }

            IReadOnlyList<RelicData> relics = _loadoutVm.Relics;
            for (int i = 0; i < relics.Count; i++)
            {
                RelicData relic = relics[i];
                var card = new VisualElement();
                card.AddToClassList("gm-card");

                var sprite = new VisualElement();
                sprite.AddToClassList("gm-card__sprite");
                if (relic.Icon != null) sprite.style.backgroundImage = new StyleBackground(relic.Icon);
                card.Add(sprite);

                var name = new Label(_loadoutVm.Name(relic));
                name.AddToClassList("gm-card__name");
                card.Add(name);

                // Наведение → детали; клик → выбор (+звук) + предпросмотр деталей.
                card.RegisterCallback<PointerEnterEvent>(_ => ShowDetail(relic));
                card.RegisterCallback<ClickEvent>(_ => { _loadoutVm.Select(relic); RefreshCards(); ShowDetail(relic); });

                grid.Add(card);
                cards.Add((relic, card));
            }

            RefreshCards();
            ShowDetail(_loadoutVm.Selected ?? (relics.Count > 0 ? relics[0] : null));

            // Табы-заглушки (кроме Релик) — недоступны (структура на будущее: Предметы/Улучшения/AI).
            Disable(screen.Q<Button>("tab-items"));
            Disable(screen.Q<Button>("tab-upgrades"));
            Disable(screen.Q<Button>("tab-ai"));

            // Принять = применить + закрыть; Сохранить = применить, не закрывая; Закрыть = отмена.
            screen.Q<Button>("btn-accept").clicked += () => { _loadoutVm.Apply(); Pop(); };
            screen.Q<Button>("btn-save").clicked   += () => { _loadoutVm.Apply(); RefreshCards(); };
            screen.Q<Button>("btn-close").clicked  += Pop;
            return screen;
        }

        // Экран награды (A3) — на UXML (RewardScreen.uxml) через общий RewardScreenView. Навигатор гарантирует
        // ровно один OnResolved, включая закрытие без выбора (= пропуск), чтобы флоу забега не завис (Ф3).
        public void OpenReward(OpenRewardRequest req)
        {
            if (CannotShow("Награда (_rewardScreen)", _rewardUxml)) { req.OnResolved?.Invoke(RewardChoiceResult.Skip); return; }
            ShowRewardAsync(req).Forget();
        }

        private async UniTaskVoid ShowRewardAsync(OpenRewardRequest req)
        {
            var screen = new RouterResultScreen<RewardChoiceResult>(ScreenKind.Page, RewardChoiceResult.Skip,
                resolve => RewardScreenView.Build(
                    _rewardUxml,
                    req.Choices,
                    req.InventoryFull,
                    req.CurrentInventory,
                    relic => _loadoutVm.Name(relic),
                    key => _loc?.GetString(key),
                    (chosen, dropId) =>
                    {
                        _audio?.Play("reward.take.stinger");
                        resolve(dropId != null ? RewardChoiceResult.Swap(chosen, dropId) : RewardChoiceResult.Take(chosen));
                    },
                    () => { _audio?.Play("reward.skip.ui"); resolve(RewardChoiceResult.Skip); },
                    // Хук выбора карточки был заведён в экране, но никогда не прокидывался — карточка
                    // анимировалась молча.
                    _ => _audio?.Play("reward.card_select.ui"),
                    _palette));   // цвет ступени приглушения — тот же, что в бою

            RewardChoiceResult result = await _nav.ShowAsync(screen, req.Cancellation); // экран снят ДО колбэка (II.5); ct → закрыть при отмене (QA #37)
            req.OnResolved?.Invoke(result);
        }

        // Экран текстового ивента (StS-style) — на UXML (EventScreen.uxml) через общий EventScreenView.
        // Выбор фиксирует последствие (колбэк → флоу применяет эффекты), затем показывается текст-результат.
        // Закрытие без выбора (ESC/PopAll) = -1, чтобы флоу не завис.
        public void OpenTextEvent(OpenTextEventRequest req)
        {
            if (CannotShow("Текстовое событие (_eventScreen)", _eventUxml, req.Event != null)) { req.OnChosen?.Invoke(-1); return; }
            PushScreen(() => BuildTextEventScreen(req), ScreenKind.Page, ct: req.Cancellation); // QA #37: отмена закрывает ивент
        }

        private VisualElement BuildTextEventScreen(OpenTextEventRequest req)
        {
            bool resolved = false;

            void Resolve(int index)
            {
                if (resolved) return;
                resolved = true;
                if (index >= 0) _audio?.Play("event.choice.ui"); // -1 = закрытие без выбора, ему звучать нечем
                req.OnChosen?.Invoke(index);
            }

            VisualElement screen = EventScreenView.Build(
                _eventUxml,
                req.Event,
                key => _loc?.GetString(key),
                Resolve);

            // Страховка: закрытие без выбора (ESC/PopAll) = пропуск (-1), чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnChosen?.Invoke(-1); }
            });

            return screen;
        }

        // Прощание узла — последний кадр магазина/сундука/привала: та же панель, что у ивента, но без вариантов.
        // Живёт по токену узла (гаснет на входе в следующий), уводят с него кнопки бита поверх (QA #48/#49).
        public void ShowNodeFarewell(OpenNodeFarewellRequest req)
        {
            if (CannotShow("Прощание узла (_eventScreen)", _eventUxml)) return;
            PushScreen(() => BuildNodeFarewellScreen(req), ScreenKind.Page, ct: req.Cancellation);
        }

        private VisualElement BuildNodeFarewellScreen(OpenNodeFarewellRequest req)
        {
            VisualElement screen = _eventUxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("event-title");
            var body  = root.Q<Label>("event-body");
            if (title != null) title.text = _loc?.GetString(req.TitleKey) ?? string.Empty;
            if (body  != null) body.text  = _loc?.GetString(req.BodyKey)  ?? string.Empty;

            // Иллюстрации и вариантов у прощания нет: кадр держит текст, а выбор уже сделан.
            var image = root.Q<VisualElement>("event-image");
            if (image != null) image.style.display = DisplayStyle.None;
            root.Q<VisualElement>("event-choices")?.Clear();

            return screen;
        }

        // Кнопки бита (A4) — прозрачный оверлей с кнопками в правом нижнем углу. Нажатие любой снимает экран.
        // Гейт (одна кнопка): закрытие без нажатия (ESC/PopAll) всё равно резолвит, чтобы петля не зависла.
        // Передышка (две кнопки): петля НЕ ждёт этот экран — он снимается по ct, когда узел выбран.
        public void ShowContinue(OpenContinueRequest req)
        {
            if (CannotShow("Кнопки бита (_continueScreen)", _continueUxml)) { req.OnContinue?.Invoke(); return; }
            ShowContinueAsync(req).Forget();
        }

        private async UniTaskVoid ShowContinueAsync(OpenContinueRequest req)
        {
            bool formation = false; // какую кнопку нажали — «К построению» не должна дёргать OnContinue

            // Kind = Modal, а НЕ Page: Page прячет всё под собой, и кнопки бита стирали бы экран пройденного
            // узла с текстом-прощанием (QA #48/#49). Modal структурно ничего не прячет, а собственного
            // затемнения у этого экрана нет (корень — .gm-continue-screen, не .gm-screen), так что фон под
            // ним остаётся как есть. Глушение ввода не меняется: Page глушил его ровно так же.
            var screen = new RouterResultScreen<bool>(ScreenKind.Modal, false, resolve =>
            {
                var body = FillRoot(_continueUxml.CloneTree());

                var btn = body.Q<Button>("btn-continue");
                if (btn != null)
                {
                    Label(btn, req.LabelKey);
                    btn.clicked += () => resolve(true);
                }

                // Вторая кнопка есть только у передышки: без колбэка её вовсе не показываем.
                var formationBtn = body.Q<Button>("btn-formation");
                if (formationBtn != null)
                {
                    if (req.OnFormation == null) formationBtn.style.display = DisplayStyle.None;
                    else
                    {
                        Label(formationBtn, req.FormationLabelKey);
                        formationBtn.clicked += () => { formation = true; resolve(true); };
                    }
                }
                return body;
            });

            bool pressed = await _nav.ShowAsync(screen, req.Cancellation); // ct → снять при отмене забега/выборе узла

            if (formation) { req.OnFormation?.Invoke(); return; }

            // Гейт обязан резолвить даже когда экран сняли без нажатия (ESC/PopAll) — иначе петля акта повиснет.
            // Передышка — наоборот: её снимают штатно (узел выбран), и «открыть карту» тогда не при чём.
            bool isRestBeat = req.OnFormation != null;
            if (pressed || !isRestBeat) req.OnContinue?.Invoke();
        }

        // Подпись кнопки из лок-ключа; пустой ключ или отсутствующий перевод — оставляем дефолт из UXML.
        private void Label(Button button, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            string text = _loc?.GetString(key);
            if (!string.IsNullOrEmpty(text)) button.text = text;
        }

        // Экран магазина (B2) — на UXML (ShopScreen.uxml) через общий ShopScreenView, биндится к IShopController.
        // «Уйти»/закрытие резолвит OnLeave (петля продолжается). Ровно один вызов.
        public void OpenShop(OpenShopRequest req)
        {
            if (CannotShow("Лавка (_shopScreen)", _shopUxml, req.Shop != null)) { req.OnLeave?.Invoke(); return; }
            ShowShopAsync(req).Forget();
        }

        private async UniTaskVoid ShowShopAsync(OpenShopRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => ShopScreenView.Build(
                    _shopUxml,
                    req.Shop,
                    relic => _loadoutVm.Name(relic),
                    key => _loc?.GetString(key),
                    () => resolve(true)));

            await _nav.ShowAsync(screen, req.Cancellation); // «Уйти»/закрытие → OnLeave; ct → закрыть при отмене (QA #37)
            req.OnLeave?.Invoke();
        }

        // Экран сундука (B3) — на UXML (ChestScreen.uxml). Клик по крышке резолвит OnOpen (флоу катит награду),
        // затем сундук закрывается. Закрытие без клика тоже резолвит, чтобы флоу не завис.
        public void OpenChest(OpenChestRequest req)
        {
            if (CannotShow("Сундук (_chestScreen)", _chestUxml)) { req.OnOpen?.Invoke(); return; }
            ShowChestAsync(req).Forget();
        }

        private async UniTaskVoid ShowChestAsync(OpenChestRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => ChestScreenView.Build(_chestUxml, key => _loc?.GetString(key),
                    () => { _audio?.Play("chest.open.stinger"); resolve(true); }));

            await _nav.ShowAsync(screen, req.Cancellation); // клик/закрытие → OnOpen; ct → закрыть при отмене (QA #37)
            req.OnOpen?.Invoke();
        }

        // Экран привала — на UXML (CampScreen.uxml). Живёт, пока отряд тратит бюджет действий; закрывается
        // по «Пройти мимо» (или ESC/PopAll), и только тогда резолвит OnLeave. Тем и отличается от ивента:
        // выбор здесь повторяемый, а выход — отдельное решение.
        public void OpenCamp(OpenCampRequest req)
        {
            if (CannotShow("Привал (_campScreen)", _campUxml, req.Session != null)) { req.OnLeave?.Invoke(); return; }
            ShowCampAsync(req).Forget();
        }

        private async UniTaskVoid ShowCampAsync(OpenCampRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => CampScreenView.Build(_campUxml, req.Session, key => _loc?.GetString(key), () => resolve(true),
                    ok => _audio?.Play(ok ? "camp.action.ui" : "camp.denied.ui")));

            await _nav.ShowAsync(screen, req.Cancellation); // уход/закрытие → OnLeave; ct → закрыть при отмене (QA #37)
            req.OnLeave?.Invoke();
        }

        // Boot title card — до главного меню. Клик / авто-таймер → OnDismiss.
        public void ShowTitleCard(OpenTitleCardRequest req)
        {
            if (CannotShow("Заставка (_titleCardScreen)", _titleCardUxml)) { req.OnDismiss?.Invoke(); return; }
            ShowTitleCardAsync(req).Forget();
        }

        private async UniTaskVoid ShowTitleCardAsync(OpenTitleCardRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve =>
                {
                    // Бут-экран умеет закрываться не только по клику: dev-запрос Ристалища торопит его,
                    // потому что ждать заставку ради тест-боя незачем (см. TryLeaveMainMenuForProvingGrounds).
                    _resolveTitleCard = () => resolve(true);
                    return TitleCardScreenView.Build(
                        _titleCardUxml,
                        _titleCardSeal,
                        key => _loc?.GetString(key),
                        onDismiss: () => resolve(true));
                });

            await _nav.ShowAsync(screen);
            _resolveTitleCard = null;
            req.OnDismiss?.Invoke();
        }

        // Экран исхода забега (C2) — на UXML (OutcomeScreen.uxml). «В меню» резолвит OnToMenu; закрытие тоже.
        public void ShowOutcome(OpenOutcomeRequest req)
        {
            if (CannotShow("Исход забега (_outcomeScreen)", _outcomeUxml)) { req.OnToMenu?.Invoke(); return; }
            ShowOutcomeAsync(req).Forget();
        }

        /// <summary>
        /// Экран итога боя. «Продолжить» здесь — не команда, а согласие: экран закрывается не по нажатию,
        /// а когда согласие собралось.
        /// </summary>
        /// <remarks>
        /// <b>Разница видна только вдвоём, и она принципиальная.</b> Закрывай экран по нажатию — и
        /// подтвердивший первым остался бы стоять над полем с трупами: экрана нет, расстановки ещё нет,
        /// нажать нечего. Поэтому кнопка лишь отправляет согласие и показывает «(N/M)», а закрытие
        /// приходит признаком срабатывания от гейта — тем же самым и у хоста, и у гостя.
        /// </remarks>
        private async UniTaskVoid ShowOutcomeAsync(OpenOutcomeRequest req)
        {
            Action<bool> close = null;
            VisualElement built = null;

            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve =>
                {
                    close = resolve;
                    built = OutcomeScreenView.Build(_outcomeUxml, req.Victory, key => _loc?.GetString(key),
                        onToMenu: () => resolve(true),
                        onContinue: req.OnContinue);
                    // Счёт, объявленный ДО постройки экрана, уже лежит в поле: гейт объявляет его в момент
                    // привязки действия, то есть раньше, чем этот экран вообще заказан.
                    ApplyReadyCount(built, _lastReady);
                    return built;
                });

            // Пока экран открыт, счёт ведёт его. Слушаем не сами — постоянная подписка живёт в роутере и
            // помнит последнее объявление; здесь только «что делать, пока экран на виду».
            _onReadyChanged = e =>
            {
                ApplyReadyCount(built, e);
                if (e.Key == Core.Net.ReadyKeys.BattleContinue && e.Fired) close?.Invoke(false); // согласились все
            };

            try
            {
                bool toMenu = await _nav.ShowAsync(screen);
                if (toMenu) req.OnToMenu?.Invoke();
            }
            finally { _onReadyChanged = null; }
        }

        private void ApplyReadyCount(VisualElement root, Core.Net.ReadyGateChangedEvent e)
        {
            if (root == null || e.Key != Core.Net.ReadyKeys.BattleContinue) return;
            OutcomeScreenView.SetContinueCount(root, key => _loc?.GetString(key),
                e.Ready, e.Required, e.LocallyReady);
        }

        // Главное меню — на UXML (MainMenuScreen.uxml). «Создать игру» открывает выбор режима ПОВЕРХ
        // меню и резолвит его собранным заказом; «Настройки» тоже поверх и меню не закрывают.
        public void OpenMainMenu(OpenMainMenuRequest req)
        {
            // Единственный гард, чей отказ ЗАКРЫВАЕТ игру: без главного меню игроку некуда деться, а висеть
            // на чёрном экране хуже. Поэтому Quit остаётся — но громко, а не молча, как было.
            if (CannotShow("Главное меню (_mainMenuScreen)", _mainMenuUxml)) { req.OnChoice?.Invoke(MainMenuOutcome.Quit); return; }
            ShowMainMenuAsync(req).Forget();
        }

        private async UniTaskVoid ShowMainMenuAsync(OpenMainMenuRequest req)
        {
            RouterResultScreen<MainMenuOutcome> screen = null;

            // Экраны, открытые ПОВЕРХ меню (режим, дом, профиль, настройки). Меню уходит в игру не
            // само по себе, а из конца этой цепочки, поэтому снять её обязан тот, кто её растил:
            // резолв меню снимает только меню, а всё, что лежит выше, пережило бы его и осталось
            // висеть поверх мира на весь забег.
            var overMenu = new List<UiScreen>();

            // Экран поверх меню — Page, а не Modal: непрозрачную страницу навигатор прячет под собой
            // САМ. Прежде панель меню пряталась здесь руками (display = None), и ближайший же
            // SyncVisibility возвращал ей Flex — меню воскресало под новым экраном, и оба читались
            // одним слипшимся листом (наход. Макса 04.08.2026). Затемнения у Page нет по устройству,
            // и это верно: мы и так в меню, темнить нечего (реш. Макса, раунд 3). В забеге настройки
            // остаются модалкой со скримом — там под ними живой мир.
            void OpenOverMenu(Func<VisualElement> build) => overMenu.Add(PushScreen(build, ScreenKind.Page));

            // Игрок дособрал заказ — цепочка поверх меню отработала и уходит целиком, снизу вверх.
            void CloseOverMenu()
            {
                for (int i = overMenu.Count - 1; i >= 0; i--) _nav.Remove(overMenu[i]);
                overMenu.Clear();
            }

            screen = new RouterResultScreen<MainMenuOutcome>(ScreenKind.Page, MainMenuOutcome.Quit,
                resolve =>
                {
                    _resolveMainMenuAsProvingGrounds = () => resolve(MainMenuOutcome.StartGame(GameStartRequest.DevProvingGrounds));
                    _resolveMainMenuAsCoopGuest      = () => resolve(MainMenuOutcome.JoinCoop);

                    // Запрос пришёл, пока меню ещё не было на экране, — отдаём Ристалище сразу, не
                    // показывая меню игроку: он его не звал, он звал тест-бой.
                    if (_provingGroundsPending)
                    {
                        _provingGroundsPending = false;
                        _resolveMainMenuAsProvingGrounds = null;
                        resolve(MainMenuOutcome.StartGame(GameStartRequest.DevProvingGrounds));
                    }

                    // То же с принятым приглашением: игрок уже в чужой партии, меню ему показывать нечего.
                    if (_coopGuestPending)
                    {
                        _coopGuestPending = false;
                        _resolveMainMenuAsCoopGuest = null;
                        resolve(MainMenuOutcome.JoinCoop);
                    }
                    return MainMenuScreenView.Build(
                        _mainMenuUxml,
                        key => _loc?.GetString(key),
                        onCreate:   () => OpenOverMenu(() => BuildNewGameScreen(
                            request => { CloseOverMenu(); resolve(MainMenuOutcome.StartGame(request)); },
                            OpenOverMenu)),
                        // «Присоединиться» меню НЕ закрывает: игрок соглашается войти уже в оверлее
                        // Steam, а уводит нас отсюда рукопожатие — оно резолвит меню само.
                        onJoin:     () => _coop?.BrowseFriends(),
                        onSettings: () => OpenOverMenu(BuildSettingsScreen),
                        onProfile:  () => OpenOverMenu(() => BuildProfileScreen(required: false, onClosed: null)),
                        onQuit:     () => { ShowQuitVeil(); resolve(MainMenuOutcome.Quit); },
                        canJoin:    _coop?.IsSteamReady ?? false);
                });

            // Забег кончился — UI прошлого забега кончается вместе с ним (QA #51). Инвентарь, карта и тест-зона
            // живут в стеке как СОСТОЯНИЯ и своих владельцев переживают: без этой уборки новый забег открывался
            // с распахнутым инвентарём поверх карты и подсвеченным табом прошлого режима. PopAll резолвит
            // висящие экраны их дефолтом и через OnExit обнуляет ссылки роутера (_inventoryScreen и соседи).
            _nav.PopAll();

            // Пока меню на экране, презентационный слой подкладывает под него стол (иначе за меню пустота).
            _mainMenuVisPub?.Publish(new MainMenuVisibilityChangedEvent(true));
            _mainMenuOpen = true;
            try
            {
                MainMenuOutcome outcome = await _nav.ShowAsync(screen); // снятие без выбора = Quit (цикл не виснет)
                req.OnChoice?.Invoke(outcome);
            }
            finally
            {
                // Через finally, а не после await: меню снимают и отменой, и выходом из игры — фон обязан
                // погаснуть в любом случае, иначе он останется висеть поверх мира.
                _mainMenuVisPub?.Publish(new MainMenuVisibilityChangedEvent(false));
                _mainMenuOpen = false;
                _resolveMainMenuAsProvingGrounds = null;
                _resolveMainMenuAsCoopGuest      = null;
            }
        }

        /// <summary>
        /// Закрыть картинку заслонкой, потому что игрок выбрал выход из игры.
        /// </summary>
        /// <remarks>
        /// Движок закрывается не мгновенно — уборка графики, звука и Steam занимает заметное время, — а
        /// меню к этому моменту уже снято выбором. Всё это время игрок смотрел на фон пустой арены и
        /// читал его как зависшую игру (наход. Макса 03.08.2026).
        /// <para>Заслонка кладётся <b>мимо навигатора</b>, прямо в слой, и снять её нечем: снимать
        /// нечего — за ней закрытие процесса. Экран стека здесь был бы хуже, а не лучше: любой
        /// последующий <c>PopAll</c> вернул бы картинку ровно в тот момент, когда показывать её уже
        /// незачем.</para>
        /// </remarks>
        private void ShowQuitVeil()
        {
            VisualElement layer = _modalLayer ?? _root;
            if (layer == null) return;

            // Ловит ввод: после выбора выхода клики в мир не должны ничего запускать.
            var veil = new VisualElement { name = "quit-veil", pickingMode = PickingMode.Position };
            veil.AddToClassList("gm-quit-veil");
            layer.Add(veil);
        }

        private static void Disable(Button b) { if (b != null) b.SetEnabled(false); }

        // Клон UXML → растянуть на весь корень панели (оверлей).
        private static VisualElement FillRoot(VisualElement tree)
        {
            tree.style.position = Position.Absolute;
            tree.style.left = 0;
            tree.style.top = 0;
            tree.style.right = 0;
            tree.style.bottom = 0;
            return tree;
        }
    }
}
