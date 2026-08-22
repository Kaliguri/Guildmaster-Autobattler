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
using Guildmaster.UI.Components;
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
    public sealed class MenuRouter : IDisposable
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
        private readonly ISubscriber<Core.Net.SharedDecisionChangedEvent> _readySub;
        private readonly IDisposable _readySubscription;
        // Состав сеанса: по нему кружок голоса получает мейн-цвет своего игрока. Вне сеанса пуст, и
        // рисовать кружки незачем — играет один.
        private readonly Core.Players.ISessionRoster _roster;
        private Core.Net.SharedDecisionChangedEvent _lastReady;
        // Что делать со счётом, пока открыт экран, который его ждёт. null — таких экранов нет, и счёт
        // просто запоминается.
        private Action<Core.Net.SharedDecisionChangedEvent> _onReadyChanged;

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
        private VisualTreeAsset _guildSelectUxml;

        /// <summary>Строки показанного ожидания — их правит <see cref="SetBusyStage"/>. Нет ожидания — null.</summary>
        private Components.WaitNote _busyNote;
        private VisualTreeAsset _hubUxml;
        private VisualTreeAsset _profileUxml;

        /// <summary>Экран заведения слота — один на профиль и на дом.</summary>
        private VisualTreeAsset _slotCreateUxml;

        // Профиль: набор скинов, число слотов, имя из Steam и применение выбранного курсора. Роутер
        // держит их функциями, а не тянет сервисы вглубь экрана: экран — разметка, а не владелец правил.
        private readonly CursorSkinCatalog _cursorSkins;

        // Знаки профиля и дома: их предлагает экран создания. Пусто — про знак не спрашиваем вовсе.
        private readonly GuildEmblemCatalog _guildEmblems;
        private readonly int               _profileSlotLimit;
        private readonly int               _guildSlotLimit;
        private readonly Func<string>      _steamName;
        private readonly Action<string, int> _cursorApply;

        private VisualTreeAsset _titleCardUxml;
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
                          ISubscriber<Core.Net.SharedDecisionChangedEvent> readySub,
                          Core.Players.ISessionRoster roster)
        {
            _roster = roster;
            _cursorSkins     = gameConfig?.CursorSkins;
            _guildEmblems    = gameConfig?.GuildEmblems;
            _profileSlotLimit = gameConfig != null ? gameConfig.MaxProfiles : 1;
            _guildSlotLimit   = gameConfig != null ? gameConfig.MaxGuildsPerProfile : 1;
            _steamName       = () => platform != null ? platform.PlayerName : "Игрок";
            _cursorApply     = (id, colorIndex) => cursors?.Apply(id, colorIndex);
            _readySub = readySub;
            // Подписка живёт столько же, сколько роутер, и это не лень: гейт объявляет счёт в момент
            // привязки действия — раньше, чем экран заказан. Подписка на время показа это объявление
            // пропустила бы, и кнопка открылась бы без «(N/M)».
            _readySubscription = readySub?.Subscribe(e =>
            {
                _lastReady = e;
                _onHubReadyChanged?.Invoke(e);
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
            VisualTreeAsset titleCardUxml = null,
            VisualTreeAsset devConsoleUxml = null, VisualTreeAsset devLogUxml = null,
            VisualTreeAsset profileUxml = null,
            VisualTreeAsset guildSelectUxml = null, VisualTreeAsset hubUxml = null,
            VisualTreeAsset slotCreateUxml = null)
        {
            _slotCreateUxml = slotCreateUxml;
            _guildSelectUxml = guildSelectUxml;
            _hubUxml = hubUxml;
            _profileUxml = profileUxml;
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
            public override bool RequiresBackdrop { get; }
            public string ScreenId { get; }

            public RouterScreen(ScreenKind kind, Func<VisualElement> build, string modeTag = null,
                                string screenId = null, Action onExit = null, bool suppressScrim = false,
                                bool requiresBackdrop = false)
            {
                Kind = kind;
                _build = build;
                ModeTag = modeTag;
                ScreenId = screenId;
                _onExit = onExit;
                SuppressScrim = suppressScrim;
                RequiresBackdrop = requiresBackdrop;
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
                                    CancellationToken ct = default, Action onExit = null, bool scrimless = false,
                                    bool requiresBackdrop = false)
        {
            var pushed = new RouterScreen(kind, build, modeTag, screenId, onExit, scrimless, requiresBackdrop);
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

        /// <summary>Просит ли видимый экран задник сам (настройки: кадр занят целиком, панели нет) — причина,
        /// не зависящая от типа экрана и сильнее живого боя за спиной.</summary>
        public bool HasScreenRequiringBackdrop => _nav.HasVisibleBackdropRequest;

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
            // Настройки из паузы остаются Modal (глушение ввода, возврат к паузе по ESC), но задник просят
            // сами: экран занимает кадр целиком и панели не имеет — под ним мельтешила бы арена забега.
            screen.Q<Button>("btn-settings").clicked +=
                () => PushScreen(BuildSettingsScreen, ScreenKind.Modal, requiresBackdrop: true);

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
            return NewGameScreenView.Build(
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

            void Rebuild()
            {
                // Список домов поменялся — экран пересобирается целиком, как и экран профиля: точечная
                // правка строк стоила бы своего кода ради экрана, который открывают раз в сессию.
                Pop();
                PushScreen(() => BuildGuildSelectScreen(onlineLobby, onStart),
                           ScreenKind.Page, requiresBackdrop: true);
            }

            return GuildSelectScreenView.Build(
                _guildSelectUxml,
                GuildSelectScreenView.ReadGuilds(_profiles, _save),
                _guildSlotLimit,
                key => _loc?.GetString(key),
                onPick: guildId => onStart?.Invoke(new GameStartRequest(GameMode.Campaign, guildId, onlineLobby)),
                onBack: Pop,
                onCreate: () => AskAndCreateGuildAsync(Rebuild).Forget(),
                onDelete: id => ConfirmDeleteGuildAsync(id, Rebuild).Forget(),
                emblemOf: id => _guildEmblems?.Resolve(id),
                shadeOf: index => _palette != null &&
                                  _palette.TryGet(Core.Players.PlayerColors.TokenOf(index), out UnityEngine.Color shade)
                                      ? shade
                                      : UnityEngine.Color.white);
        }

        /// <summary>
        /// Двор гильдии между выбором дома и забегом. Пока заглушка с единственной дверью наружу.
        /// </summary>
        public void OpenHub(OpenHubRequest req)
        {
            // Отказ здесь ЗАВЕРШАЕТ шаг: без двора игрок остался бы стоять между домом и актом, и
            // забег не начался бы никогда. Экран пропущен — голосуем за выход сами, но ошибка красная.
            if (CannotShow("Двор гильдии (_hubScreen)", _hubUxml)) { req.OnStartRun?.Invoke(); return; }

            VisualElement built = null;
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, true,
                _ =>
                {
                    // Кнопка НЕ закрывает двор — она отправляет голос. Закрытие приходит объявлением, и
                    // потому одинаково у обеих ролей: раньше клик закрывал двор нажавшему, и дать эту
                    // кнопку гостю было нельзя — напарник остался бы стоять один.
                    built = HubScreenView.Build(_hubUxml, req.GuildName, key => _loc?.GetString(key),
                                                onStartRun: () => req.OnStartRun?.Invoke(),
                                                canStartRun: req.OnStartRun != null,
                                                stage: (req.ActNumber, req.Level, req.ActTitleKey),
                                                onLeave: req.OnLeave);
                    ApplyHubCount(built, _lastReady);
                    return built;
                });

            _hubScreen = screen; // «двор открыт» — это ссылка на его экран, и другого владельца у факта нет
            ShowHubAsync(screen, req, () => built).Forget();
        }

        private async UniTaskVoid ShowHubAsync(RouterResultScreen<bool> screen, OpenHubRequest req,
                                               Func<VisualElement> built)
        {
            // Пока двор открыт, счёт на кнопке ведёт он: напарник соглашается уже после того, как ты
            // нажал, и молчащая кнопка выглядела бы как зависшая.
            Action<Core.Net.SharedDecisionChangedEvent> before = _onHubReadyChanged;
            _onHubReadyChanged = e => ApplyHubCount(built(), e);

            try { await _nav.ShowAsync(screen, req.Cancellation); }
            finally
            {
                _onHubReadyChanged = before;
                if (ReferenceEquals(_hubScreen, screen)) _hubScreen = null;
            }
        }

        private Action<Core.Net.SharedDecisionChangedEvent> _onHubReadyChanged;

        private void ApplyHubCount(VisualElement root, Core.Net.SharedDecisionChangedEvent e)
        {
            if (root == null || e.Key != Core.Net.DecisionKeys.RunStart) return;

            HubScreenView.SetStartCount(root, key => _loc?.GetString(key),
                e.Voted, e.Required, e.HasLocalChoice);
        }

        /// <summary>Экран двора, пока он на стеке.</summary>
        /// <remarks>
        /// <b>Второго пути к нему больше нет</b> (09.08.2026): двор объявляется шагом сеанса, как и
        /// прочие общие экраны, и приходит сюда одним запросом у обеих ролей. Прежде рядом жил шов
        /// <c>IHubPresence</c> — хост объявлял «двор открыт», гость поднимал экран сам, — и это была
        /// ровно та форма, из-за которой экраны узла разъезжались между ролями.
        /// </remarks>
        private RouterResultScreen<bool> _hubScreen;

        /// <summary>
        /// Стоим ли мы во дворе прямо сейчас. Спрашивает системное меню: ESC работает «внутри игры», а
        /// двор идёт ДО открытия мероприятия — по признаку мероприятия он выглядел как главное меню, и
        /// клавиша молча не делала ничего (наход. Макса 22.08.2026: «Не работает ESC меню (а должно,
        /// вдруг хотим в настройки зайти пока игроков ждем?)»).
        /// </summary>
        public bool IsCourtyardOpen => _hubScreen != null;

        /// <summary>
        /// Показать сообщение игроку: что случилось, почему и что сказала система.
        /// </summary>
        /// <remarks>
        /// <b>Модалка поверх места события:</b> сообщение всегда про то, что игрок сейчас делал, и
        /// убирать это из-под него незачем. Ассета не требует — вид собирается кодом (см.
        /// <see cref="NoticeDialogView"/>), поэтому отказ показать здесь невозможен, и молчание игроку
        /// больше не грозит.
        /// <para><b>Единственное окно на всю игру</b> (решение Макса 09.08.2026): ошибка, приглашение,
        /// разрыв связи и подтверждение — один код с разным списком ответов. Раньше их было три,
        /// каждое со своим UXML и своей разметкой.</para>
        /// </remarks>
        public void ShowNotice(in Core.Flow.NoticeRequest request)
        {
            Core.Flow.NoticeRequest captured = request;
            RouterScreen screen = null;
            screen = new RouterScreen(ScreenKind.Modal,
                () => NoticeDialogView.Build(in captured, key => _loc?.GetString(key),
                                             close: () => { if (screen != null) _nav.Remove(screen); }));

            _nav.Push(screen);
        }

        /// <summary>
        /// Показать титр: знак и крупное слово, которые въезжают в кадр и уходят сами.
        /// </summary>
        /// <remarks>
        /// <b>Мимо навигатора, прямо в слой.</b> Титр не экран: он ничего не решает, ничего не ждёт и
        /// не должен ни прятать нижнее, ни глушить ввод — а любой <c>ScreenKind</c> делает что-то из
        /// этого (Page прячет, Modal глушит, Sheet прячет страницу под собой). Стек он поэтому не
        /// трогает вовсе, как и заслонка выхода.
        /// <para>Снимает себя сам, по времени: кнопки у титра нет и быть не должно.</para>
        /// </remarks>
        public void ShowTitle(in Core.Flow.TitleRevealRequest request)
        {
            VisualElement layer = _modalLayer ?? _root;
            if (layer == null) return;

            var titre = new Components.TitleReveal();
            titre.Dress(
                Loc(request.LineKey, request.LineFallback),
                string.IsNullOrEmpty(request.SubKey) && string.IsNullOrEmpty(request.SubFallback)
                    ? null
                    : Loc(request.SubKey, request.SubFallback),
                _guildEmblems?.Resolve(request.GlyphId),
                ToneOf(request.Tone));

            layer.Add(titre);
            titre.Play(request.HoldSeconds > 0f ? request.HoldSeconds : DefaultTitleHold,
                       () => titre.RemoveFromHierarchy());
        }

        /// <summary>Сколько титр держится, если заказчик не назвал своё время.</summary>
        private const float DefaultTitleHold = 1.3f;

        private static Components.TitleReveal.Tone ToneOf(Core.Flow.TitleRevealTone tone) => tone switch
        {
            Core.Flow.TitleRevealTone.Triumph => Components.TitleReveal.Tone.Triumph,
            Core.Flow.TitleRevealTone.Defeat  => Components.TitleReveal.Tone.Defeat,
            _                                 => Components.TitleReveal.Tone.Call,
        };

        /// <summary>
        /// Показать ожидание, пока живёт токен заказчика.
        /// </summary>
        /// <remarks>
        /// <b>Снимает экран отмена, а не игрок.</b> Кнопки «закрыть» у ожидания нет: закрытое окно
        /// означало бы, что ждать перестали, — а ждать не перестали. Уже отменённый токен не показывает
        /// ничего: ожидание кончилось раньше, чем успело начаться, и мигать им незачем.
        /// </remarks>
        public void ShowBusy(in Core.Flow.BusyRequest request)
        {
            if (request.Until.IsCancellationRequested) return;

            Core.Flow.BusyRequest captured = request;
            var screen = new RouterScreen(ScreenKind.Modal,
                () =>
                {
                    VisualElement built = BusyOverlayView.Build(in captured, key => _loc?.GetString(key),
                                                                out Components.WaitNote note);
                    _busyNote = note;
                    return built;
                });

            _nav.Push(screen);

            // Регистрация переживает сам показ: токен может отмениться в любой момент, в том числе
            // прямо сейчас — тогда экран снимется следующим кадром, не успев моргнуть.
            captured.Until.Register(() =>
            {
                _busyNote = null;
                _nav.Remove(screen);
            });
        }

        /// <summary>
        /// Ожидание продвинулось: сменить строку этапа, не пересобирая экран.
        /// </summary>
        /// <remarks>
        /// Ожидания нет на экране — сообщение молча тонет, и это верно: этап относится к показанному
        /// ожиданию, а показывать его самому по себе негде. Кричать в лог тут было бы шумом на ровном
        /// месте — гонка «этап пришёл на кадр раньше снятия экрана» законна и ничего не ломает.
        /// </remarks>
        public void SetBusyStage(in Core.Flow.BusyStageChanged stage)
        {
            if (_busyNote == null) return;

            string text = null;
            if (!string.IsNullOrEmpty(stage.StageKey)) text = _loc?.GetString(stage.StageKey);
            if (string.IsNullOrEmpty(text)) text = stage.StageFallback;

            _busyNote.Detail = text;
        }

        /// <summary>
        /// Спросить подтверждение необратимого действия. <c>true</c> — игрок согласился.
        /// </summary>
        /// <remarks>
        /// <b>То же окно, что у сообщений и разрыва связи</b> (решение Макса 09.08.2026): вопрос — это
        /// уведомление с двумя ответами, и отдельного экрана ему не нужно. Раньше он жил своим UXML и
        /// своей разметкой, третьим почти одинаковым диалогом рядом с двумя другими.
        /// <para><b>Отказ — это НАЖАТЬ «Отмена»</b>, а не закрыть окно мимо кнопок: закрыть его нечем
        /// («Пока все требует кнопки»). Прежнее умолчание «снятие = нет» держало безопасную сторону при
        /// Esc, которого у модалки нет.</para>
        /// </remarks>
        public UniTask<bool> ConfirmAsync(string title, string body, string consequence, string confirmText)
        {
            var answered = new UniTaskCompletionSource<bool>();

            ShowNotice(new Core.Flow.NoticeRequest(
                Core.Flow.NoticeKind.Warning,
                titleKey: null, titleFallback: title,
                bodyKey: null, bodyFallback: body,
                consequence: consequence,
                options: new System.Collections.Generic.List<Core.Flow.NoticeOption>
                {
                    // ПОДТВЕРДИТЬ СЛЕВА, ОТМЕНА СПРАВА (правило Макса 22.08.2026). Порядок ответов
                    // задаётся здесь, потому что окно рисует их в порядке списка — и это
                    // единственное место, где вопрос собирается.
                    new Core.Flow.NoticeOption(null, confirmText, () => answered.TrySetResult(true)),
                    new Core.Flow.NoticeOption("ui.confirm.cancel", "Отмена",
                                               () => answered.TrySetResult(false), primary: true),
                }));

            return answered.Task;
        }



        /// <summary>
        /// Показать профиль. <paramref name="required"/> — профиля нет вовсе: экран открывается без
        /// «Назад» и закрывается сам, как только слот заведён.
        /// </summary>
        public void OpenProfile(OpenProfileRequest req)
        {
            if (CannotShow("Профиль (_profileScreen)", _profileUxml)) { req.OnClosed?.Invoke(); return; }

            PushScreen(() => BuildProfileScreen(req.Required, req.OnClosed),
                       ScreenKind.Page, requiresBackdrop: true);
        }

        /// <summary>
        /// Развилка «Профиль»: две карточки — сменить профиль или настроить его.
        /// </summary>
        /// <remarks>
        /// Заказ Макса 21.08.2026 по рефу Heroes Olden Era: кнопка меню открывает не список, а две
        /// крупные двери. Обе ведут на ОДИН экран в разных лицах — разметка у них общая, а вопрос
        /// разный.
        /// <para><b>За дверью — Page, а не Modal</b> (правило Макса 22.08.2026: «У нас должен быть лишь
        /// 1 основной экран одновременно»). Модалка нижнее не прячет по устройству навигатора, поэтому
        /// развилка оставалась видна за экраном, который сама и открыла, и два разных вопроса читались
        /// одним слипшимся листом. Страницу навигатор прячет под собой сам — ровно как цепочку поверх
        /// главного меню.</para>
        /// </remarks>
        private VisualElement BuildProfileHub()
        {
            return FillRoot(ProfileHubView.Build(
                key => _loc?.GetString(key),
                onSelectProfile: () => PushScreen(
                    () => BuildProfileScreen(required: false, onClosed: null, customize: false),
                    ScreenKind.Page, requiresBackdrop: true),
                onCustomize: () => PushScreen(
                    () => BuildProfileScreen(required: false, onClosed: null, customize: true),
                    ScreenKind.Page, requiresBackdrop: true),
                onBack: Pop));
        }

        /// <summary>
        /// Экран заведения слота: имя и знак. Один на профиль и на дом.
        /// </summary>
        /// <remarks>
        /// Заказ Макса 22.08.2026: «При создания профиля должен открыться экран настройки… в котором
        /// должны выбрать название и иконку», и следом — «делаем UI выбора гильдии, создания и удаления
        /// в духе профиля». Отсюда один экран на оба случая: разница только в подписях.
        /// </remarks>
        private VisualElement BuildSlotCreateScreen(SlotCreateView.SlotKind kind, string suggestedName,
                                                    Action<Core.Persistence.SlotCreationRequest> onCreate)
        {
            if (CannotShow("Создание слота (_slotCreateScreen)", _slotCreateUxml)) return new VisualElement();

            return FillRoot(SlotCreateView.Build(
                _slotCreateUxml,
                kind,
                suggestedName,
                _guildEmblems,
                _palette,
                Core.Players.PlayerColors.Count,
                key => _loc?.GetString(key),
                onCreate: request => { Pop(); onCreate?.Invoke(request); },
                onBack: Pop));
        }

        private VisualElement BuildProfileScreen(bool required, Action onClosed, bool customize = false)
        {
            void Rebuild()
            {
                // Список слотов и активный профиль поменялись — экран пересобирается целиком. Точечная
                // правка строк стоила бы своего кода ради экрана, который открывают раз в сессию.
                Pop();
                PushScreen(() => BuildProfileScreen(required, onClosed, customize),
                           ScreenKind.Page, requiresBackdrop: true);
            }

            var slots = new List<ProfileScreenView.SlotEntry>();
            if (_profiles != null)
            {
                string activeId = _profiles.ActiveProfile.Id;
                for (int i = 0; i < _profiles.Profiles.Count; i++)
                {
                    Core.Persistence.ProfileSummary p = _profiles.Profiles[i];
                    slots.Add(new ProfileScreenView.SlotEntry(p.Id, p.Name, p.Id == activeId, p.Stats,
                                                             p.EmblemId, p.EmblemColorIndex));
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
                Core.Players.PlayerColors.Count,
                _palette,
                canLeave,
                customize,
                key => _loc?.GetString(key),
                onSelect: id => { _profiles?.SelectProfile(id); Rebuild(); },
                // ПУСТОЙ СЛОТ СПРАШИВАЕТ, а не заводит молча (заказ Макса 22.08.2026: «При нажатии на
                // него - появляется сообщение-уведомление (хотите создать новый профиль?), нажимаем -
                // да (окно названия профиля), нет (ничего не происходит, остаемся там где были)»).
                onCreate: () => AskAndCreateProfileAsync(required, onClosed, Rebuild).Forget(),
                onDelete: id => ConfirmDeleteAsync(id, Rebuild).Forget(),
                onSave: identity =>
                {
                    _profiles?.SaveIdentity(identity);
                    _cursorApply?.Invoke(identity.CursorSkinId, identity.ColorIndex);
                },
                // Показать выбранное немедленно — мимо профиля: на диск пишет «Сохранить».
                onPreview: identity => _cursorApply?.Invoke(identity.CursorSkinId, identity.ColorIndex),
                onBack: () =>
                {
                    // Ушли без «Сохранить» — возвращаем то, что лежит в профиле. Иначе примерка
                    // переживала бы экран и выглядела бы сохранённой, хотя её никто не записывал.
                    Core.Persistence.ProfileIdentity saved = _profiles?.Identity ?? default;
                    _cursorApply?.Invoke(saved.CursorSkinId, saved.ColorIndex);

                    Pop();
                    onClosed?.Invoke();
                },
                emblemOf: id => _guildEmblems?.Resolve(id),
                shadeOf: index => _palette != null &&
                                  _palette.TryGet(Core.Players.PlayerColors.TokenOf(index), out UnityEngine.Color shade)
                                      ? shade
                                      : UnityEngine.Color.white));
        }

        /// <summary>
        /// Спросить про новый дом и, если игрок согласился, открыть экран заведения.
        /// </summary>
        /// <remarks>Тот же порядок, что у профиля: вопрос — экран — заведение.</remarks>
        private async UniTaskVoid AskAndCreateGuildAsync(Action rebuild)
        {
            bool yes = await ConfirmAsync(
                Loc("ui.guilds.create.ask.title", "Завести новую гильдию?"),
                Loc("ui.guilds.create.ask.body", "Свободный слот станет новым домом."),
                consequence: null,
                Loc("ui.guilds.create.ask.confirm", "Завести"));

            if (!yes) return;

            PushScreen(() => BuildSlotCreateScreen(
                    SlotCreateView.SlotKind.Guild,
                    SuggestedGuildName(),
                    request =>
                    {
                        if (_profiles?.CreateGuild(request.Name, request) == null) return;
                        rebuild?.Invoke();
                    }),
                ScreenKind.Page, requiresBackdrop: true);
        }

        /// <summary>Имя дома по умолчанию: «Гильдия N» по числу заведённых.</summary>
        private string SuggestedGuildName()
            => string.Format(Loc("ui.guilds.create.default_name", "Гильдия {0}"),
                             (_profiles?.Guilds.Count ?? 0) + 1);

        /// <summary>
        /// Спросить и снести дом. Вместе с ним уходит его забег — об этом и предупреждаем.
        /// </summary>
        private async UniTaskVoid ConfirmDeleteGuildAsync(string guildId, Action onDone)
        {
            string name = guildId;
            if (_profiles != null)
            {
                for (int i = 0; i < _profiles.Guilds.Count; i++)
                    if (_profiles.Guilds[i].Id == guildId) name = _profiles.Guilds[i].Name;
            }

            bool yes = await ConfirmAsync(
                Loc("ui.guilds.delete.title", "Удалить гильдию?"),
                name,
                Loc("ui.guilds.delete.consequence",
                    "Вместе с домом пропадёт его забег, ростер и всё нажитое. Это необратимо."),
                Loc("ui.guilds.delete", "Удалить"));

            if (!yes) return;

            _profiles?.DeleteGuild(guildId);
            onDone?.Invoke();
        }

        /// <summary>
        /// Спросить про новый профиль и, если игрок согласился, открыть экран заведения.
        /// </summary>
        /// <remarks>
        /// Вопрос стоит ПЕРЕД экраном, а не вместо него: пустой слот — это место, а не кнопка, и клик
        /// по нему может быть промахом. Согласился — дальше имя и знак.
        /// </remarks>
        private async UniTaskVoid AskAndCreateProfileAsync(bool required, Action onClosed, Action rebuild)
        {
            bool yes = await ConfirmAsync(
                Loc("ui.profile.create.ask.title", "Создать новый профиль?"),
                Loc("ui.profile.create.ask.body", "Свободный слот станет новым профилем."),
                consequence: null,
                Loc("ui.profile.create.ask.confirm", "Создать"));

            if (!yes) return;

            PushScreen(() => BuildSlotCreateScreen(
                    SlotCreateView.SlotKind.Profile,
                    SuggestedProfileName(),
                    request =>
                    {
                        if (_profiles?.CreateProfile(request) == null) return;

                        // Обязательный показ существует ради одного события — появления профиля. Оно
                        // случилось, держать игрока больше не на чем.
                        if (required) { Pop(); onClosed?.Invoke(); return; }
                        rebuild?.Invoke();
                    }),
                ScreenKind.Page, requiresBackdrop: true);
        }

        /// <summary>Имя, которое подставлено в поле по умолчанию: «Профиль N» по числу занятых слотов.</summary>
        private string SuggestedProfileName()
            => string.Format(Loc("ui.profile.create.default_name", "Профиль {0}"),
                             (_profiles?.Profiles.Count ?? 0) + 1);

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

        /// <summary>
        /// Уйти из настроек. С несохранёнными правками сперва спрашиваем — и только «да» их теряет.
        /// </summary>
        /// <remarks>
        /// Откат зовётся ПОСЛЕ согласия, а не до вопроса: настройки применяются живьём, и откатить их
        /// на время диалога значило бы показать игроку чужую громкость и чужое разрешение ровно в тот
        /// момент, когда он решает, терять ли свои.
        /// </remarks>
        private async UniTaskVoid LeaveSettingsAsync()
        {
            if (_settingsVm.HasUnsavedChanges)
            {
                bool leave = await ConfirmAsync(
                    Loc("ui.settings.leave.title", "Выйти из настроек?"),
                    Loc("ui.settings.leave.body", "Несохранённые изменения будут потеряны."),
                    consequence: null,
                    Loc("ui.settings.leave.confirm", "Выйти"));

                if (!leave) return;
            }

            _settingsVm.Cancel();
            Pop();
        }

        /// <summary>Сбросить настройки к начальным — с вопросом: отменить это нечем, кроме памяти игрока.</summary>
        private async UniTaskVoid ResetSettingsAsync()
        {
            bool reset = await ConfirmAsync(
                Loc("ui.settings.reset.title", "Сбросить настройки?"),
                Loc("ui.settings.reset.body", "Все значения вернутся к начальным."),
                consequence: null,
                Loc("ui.settings.reset.confirm", "Сбросить"));

            if (reset) _settingsVm.ResetToDefaults();
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

            // Уход с несохранёнными правками и сброс к начальным — оба необратимы для того, что игрок
            // только что крутил, и оба спрашивают (правило Макса 22.08.2026). Уход БЕЗ правок не
            // спрашивает ничего: вопрос без последствий приучает жать «да» не читая.
            Components.BackButton leave = screen.Q<Components.BackButton>("btn-cancel");
            leave?.Localize(key => _loc?.GetString(key));
            if (leave != null) leave.clicked += () => LeaveSettingsAsync().Forget();

            screen.Q<Button>("btn-defaults").clicked += () => ResetSettingsAsync().Forget();

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
                // КОНТРОЛ, а не ручная сборка. До 07.08.2026 здесь построчно повторялся конструктор
                // RelicCard — те же классы, тот же спрайт, та же подпись, — и расхождение уже стоило
                // грида: карточки контрола стали focusable, а собранные тут остались недоступны с
                // клавиатуры. Один владелец сборки: правка контрола доезжает сюда сама.
                var card = new RelicCard { RelicName = _loadoutVm.Name(relic) };
                card.SetSprite(relic.Icon);

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
            // Дверь наружу — тот же контрол, что и на прочих экранах: слово, место и вид у возврата
            // одни на всю игру (правило Макса 22.08.2026).
            Components.BackButton close = screen.Q<Components.BackButton>("btn-close");
            close?.Localize(key => _loc?.GetString(key));
            if (close != null) close.clicked += Pop;
            return screen;
        }

        // Экран награды (A3) — на UXML (RewardScreen.uxml) через общий RewardScreenView. Навигатор гарантирует
        // ровно один OnResolved, включая закрытие без выбора (= пропуск), чтобы флоу забега не завис (Ф3).
        public void OpenReward(OpenRewardRequest req)
        {
            if (CannotShow("Награда (_rewardScreen)", _rewardUxml)) { req.OnVote?.Invoke(RewardOptions.Skip); return; }
            ShowRewardAsync(req).Forget();
        }

        /// <summary>
        /// Витрина награды. «Взять» здесь — не команда, а голос: награда общая, и забирает её группа.
        /// </summary>
        /// <remarks>
        /// Устроено ровно как экран итога боя, и по той же причине: закрывай экран по своему нажатию —
        /// и подтвердивший первым остался бы смотреть в пустоту, пока остальные ещё выбирают. Поэтому
        /// нажатие отправляет голос, а закрытие приходит признаком срабатывания от общего решения —
        /// одинаково у хозяина и у гостя.
        /// </remarks>
        private async UniTaskVoid ShowRewardAsync(OpenRewardRequest req)
        {
            Action<bool> close = null;
            VisualElement built = null;

            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve =>
                {
                    close = resolve;
                    return built = RewardScreenView.Build(
                        _rewardUxml,
                        req.Choices,
                        req.InventoryFull,
                        req.CurrentInventory,
                        relic => _loadoutVm.Name(relic),
                        key => _loc?.GetString(key),
                        (chosen, dropId) =>
                        {
                            _audio?.Play("reward.take.stinger");
                            req.OnVote?.Invoke(RewardOptions.Swap(chosen.Id, dropId));
                        },
                        () => { _audio?.Play("reward.skip.ui"); req.OnVote?.Invoke(RewardOptions.Skip); },
                        // Хук выбора карточки был заведён в экране, но никогда не прокидывался — карточка
                        // анимировалась молча.
                        _ => _audio?.Play("reward.card_select.ui"),
                        _palette);   // цвет ступени приглушения — тот же, что в бою
                });

            _onReadyChanged = e =>
            {
                if (e.Key != Core.Net.DecisionKeys.RewardPick) return;

                RewardScreenView.SetVotes(built, e.Choices, ColorOf, solo: e.Required <= 1);
                if (e.Fired) close?.Invoke(false); // сошлись все
            };

            // Счёт мог быть объявлен до постройки экрана — решение взводится раньше показа.
            RewardScreenView.SetVotes(built, _lastReady.Choices, ColorOf, solo: _lastReady.Required <= 1);

            try { await _nav.ShowAsync(screen, req.Cancellation); } // ct → закрыть при отмене (QA #37)
            finally { _onReadyChanged = null; }
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
                Resolve,
                req.Gold);

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
                        onContinue: req.OnContinue,
                        onRestart:  req.OnRestart,
                        onToGuild:  req.OnToGuild,
                        summary: req.Summary,
                        // Знак — ТОТ ЖЕ, что у титра исхода: игрок только что видел его во весь экран.
                        glyph: _guildEmblems?.Resolve(req.Victory ? "emblem.crown" : "emblem.skull-crossed-bones"));
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

                // Экран уходит по срабатыванию своего решения: на площадке это «продолжить», после
                // забега — «заново»/«во двор». Оба закрывают одинаково у хоста и у гостя.
                bool mine = e.Key == Core.Net.DecisionKeys.BattleContinue ||
                            e.Key == Core.Net.DecisionKeys.RunAfter;
                if (mine && e.Fired) close?.Invoke(false);
            };

            try
            {
                bool toMenu = await _nav.ShowAsync(screen);
                if (toMenu) req.OnToMenu?.Invoke();
            }
            finally { _onReadyChanged = null; }
        }

        /// <summary>
        /// Мейн-цвет участника по его номеру. Вне сеанса — первый: рисовать всё равно нечего.
        /// </summary>
        private int ColorOf(int playerId) =>
            _roster != null && _roster.TryGet(playerId, out Core.Players.SessionPlayer p) ? p.ColorIndex : 0;

        /// <summary>
        /// Обновить счёт на общих кнопках экрана исхода.
        /// </summary>
        /// <remarks>
        /// После забега общих кнопок ДВЕ, и счёт у каждой свой — сколько выбрали ИМЕННО ЕЁ. Общий счёт
        /// решения на обеих означал бы, что напарник согласился с тобой, когда он выбрал соседнюю.
        /// </remarks>
        private void ApplyReadyCount(VisualElement root, Core.Net.SharedDecisionChangedEvent e)
        {
            if (root == null) return;

            if (e.Key == Core.Net.DecisionKeys.BattleContinue)
            {
                OutcomeScreenView.SetSharedCount(root, "btn-continue", "ui.outcome.continue", "Продолжить",
                    key => _loc?.GetString(key), e.Voted, e.Required, e.HasLocalChoice);
                return;
            }

            if (e.Key != Core.Net.DecisionKeys.RunAfter) return;

            OutcomeScreenView.SetSharedCount(root, "btn-restart", "ui.outcome.restart", "Начать заново",
                key => _loc?.GetString(key), VotesFor(e, Core.Net.RunAfterOptions.Restart), e.Required,
                e.LocalChoice == Core.Net.RunAfterOptions.Restart);

            OutcomeScreenView.SetSharedCount(root, "btn-guild", "ui.outcome.to_guild", "Во двор гильдии",
                key => _loc?.GetString(key), VotesFor(e, Core.Net.RunAfterOptions.Guild), e.Required,
                e.LocalChoice == Core.Net.RunAfterOptions.Guild);
        }

        /// <summary>Сколько участников выбрали именно этот вариант.</summary>
        private static int VotesFor(Core.Net.SharedDecisionChangedEvent e, string option)
        {
            int count = 0;
            for (int i = 0; i < e.Choices.Count; i++)
                if (e.Choices[i].Option == option) count++;

            return count;
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
            void OpenOverMenu(Func<VisualElement> build, bool requiresBackdrop = false) =>
                overMenu.Add(PushScreen(build, ScreenKind.Page, requiresBackdrop: requiresBackdrop));

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
                            // Лямбда, а не метод-группа: у OpenOverMenu появился второй (опциональный)
                            // параметр, а метод с ним в Action<Func<VisualElement>> не преобразуется.
                            b => OpenOverMenu(b))),
                        // «Присоединиться» меню НЕ закрывает: игрок соглашается войти уже в оверлее
                        // Steam, а уводит нас отсюда рукопожатие — оно резолвит меню само.
                        onJoin:     () => _coop?.BrowseFriends(),
                        // Настройки просят стол ЯВНО: за главным меню может идти живой бой, и он гасит задник —
                        // для меню это верно (бой ради того и заведён), для настроек нет. См. RequiresBackdrop.
                        onSettings: () => OpenOverMenu(BuildSettingsScreen, requiresBackdrop: true),
                        onProfile:  () => OpenOverMenu(BuildProfileHub),
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
