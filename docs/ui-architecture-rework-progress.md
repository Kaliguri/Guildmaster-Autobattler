# UI-реворк: рабочий журнал (прогресс + решения)

> **Назначение.** Живой лог исполнения `docs/ui-architecture-rework-plan.md`. План — что делать;
> этот файл — **что уже сделано, какие решения приняты и что осознанно отложено**, чтобы любая
> следующая сессия (агент или человек) подхватила без раскопок. Обновлять в конце каждой фазы.
>
> **Ветка:** `feat/persist-battle-flow`. **Веду:** Никси. **Старт:** 2026-07-19.

---

## Статус фаз

| Фаза | Статус | Коммит | Что |
|---|---|---|---|
| Ф0 Фундамент (PanelSettings) | ✅ влито | `1a36bf0f` | панели → ScaleWithScreenSize; pixel-perfect удалён; device-профили |
| Ф1 Скелет навигатора | ✅ влито | `bced833a` | `Navigation/` + 12 тестов; создан, НЕ подключён |
| Ф2 Своп ядра MenuRouter | ✅ влито | `7756b948` | делегация стека/ввода навигатору, поведение 1:1 |
| Ф3 Flow через ShowAsync | ✅ влито | `168f747f` | 7 flow-экранов на ShowAsync, Detach-страховки сняты |
| **Ф4 Слои-контейнеры** | ✅ влито | `539d048e` | 8 слоёв в корне; K1-K4 снесены; QA #32/#33/#35/#36; шов локали |
| **Ф5 Тест-зона** | ✅ влито | `85ecdef9` | Sheet-экран + единый источник TestZoneChangedEvent; K5-K8 сняты |
| **Ф6 Инвентарь** | ✅ влито | `85ecdef9` | формальный Sheet, смерть `_inventoryOpen`; закрытие=Remove не PopAll |
| Ф7 Чистка + доки | ⏳ следующий | — | снос мёртвого, tech-scribe |

**Точка остановки задана Максом (2026-07-19):** гнать Ф1→Ф2→Ф3 автономно, **стоп после Ф3**
на его play (полный забег) — НЕ после Ф2, как в протоколе плана. Ф0–Ф3 влиты.

**play-QA П1 ПРОШЁЛ (2026-07-19): 6 находок (#32-#37b)** — см. `docs/persist-battle-qa-findings.md`
+ раздел «Находки пакета П1» ниже (порядок фиксов). НЕ чинены (Макс: сначала записать всё + реестр
костылей + порядок, не латать).

**► СТАРТ СЛЕДУЮЩЕЙ СЕССИИ (согласовано Максом 2026-07-19): сначала #37 (вылет-Aborted «В меню» с
карты) + #37b (лог) отдельным фиксом, ПОТОМ Ф4 (слои).** Порядок в разделе «Находки пакета П1» ниже.

**► #37/#37b СДЕЛАНЫ (2026-07-19, ждут play-QA, 408/408).** Единая система отмены забега (Макс за неё +
ресёрч подтвердил cooperative-cancellation паттерн). Токен забега пробрасывается через всю цепочку показа
(6 запросов +`ct`, 4 флоу +`AttachExternalCancellation`, `UiNavigator.Push` +`ct`-оверлоад, `MenuRouter`
проброс + `CloseAll`→`Pop` в паузе, `ActRunner` различает отмену/Aborted + лог). Снесены K11+K12. Регресс-тест
в `ActRunnerTests`. Детали — QA-трекер #37/#37b.

**► Ф4 СДЕЛАНА (2026-07-19, `539d048e`, ждёт play-QA, 412/412).** Скелет из 8 слоёв-контейнеров в корне
UIDocument (порядок Add = z): `backdrop / battle-center / screens / topbar / modal / cursors / tooltip /
system` (последние три — пустые заделы под курсоры/тултипы/тосты). Навигатор кладёт экран по `ScreenKind`:
Page/Sheet → screens (под топбаром), Modal → modal (над топбаром). Снятие — `RemoveFromHierarchy`.
**Снесены K1-K4:** BringToFront-каждый-кадр (топбар в своём слое); SendToBack-жонглирование battle-center
(узел переезжает в свой слой, ссылки RunModeBarView живы); поллинг структуры каждый кадр → подписка
`nav.Changed` + ребро фазы/инвентаря (данные боя остаются в Update); подсветка таба из верхнего НЕ-Modal
экрана (`ActiveModeTag` игнорит Modal). **QA:** #36 (Modal-scrim `.gm-screen` в layer-modal затемняет+
блокирует топбар, не скрывает — структурно, без спец-кода), #32 (ESC-меню только при `RunState.Current`),
#33 (`.gm-tab-page min-height:340` — панель настроек не прыгает по табам), #35 (единый источник подсветки).
**Шов II.9.2** локаль-hot-swap: подписка `ILocalizationService.LocaleChanged` → `RebuildTopBar`.
Верификация: 412/412 EditMode (+4 теста навигатора), структура слоёв программно (порядок точный,
Modal→layer-modal), скрины Modal-scrim + табы (#33) в чат.

**► Ф5+Ф6 СДЕЛАНЫ (2026-07-19, `85ecdef9`, ждут play-QA, 414/414).** Триггер — play-QA Макса поймал «главный
баг»: `CloseOverlays()`=`PopAll()` сносил карту петли акта (result-экран) при «Бой»/«Инвентарь» поверх неё
→ résolve узла null → `ActRunner` Aborted (+ рассинхрон подсветки/просвечивание карты). Это был класс #31/#30,
ждавший Ф5/Ф6 — не регрессия Ф4.
**МОДЕЛЬ (Макс, зафиксирована в [[persist-world-decisions]]):** карта и геймплей — ВЗАИМОИСКЛЮЧАЮЩИЕ
полноэкранные пространства (геймплей целиком закрывает карту и наоборот); инвентарь = геймплей (карта скрыта)
+ UI поверх. Карту петли акта НЕ снимать (result-экран, ждёт выбор — снять=Aborted), а ПРЯТАТЬ `display:none`.
Топбар над картой: прячет карту, входит в геймплей, карта ждёт возврата (решение Макса).
**Реализация:**
- **Навигатор `SyncVisibility` переписан:** Sheet (геймплей: инвентарь/тест-зона) ПРЯЧЕТ Page (карту) под собой;
  соседние Sheet друг друга не прячут; Modal не прячет структурно. + публичный `Remove(screen)` (снять из середины).
- **Ф5 тест-зона:** новое `TestZoneChangedEvent(bool)` — СОСТОЯНИЕ (vs интент `ToggleTestZoneRequest`).
  `DeploymentController` — единственный владелец (вещает на enter/exit/start/free-deploy + гейт входа при Fighting).
  `TestZoneArenaSkin` слушает состояние (смерть самотога `_gray` → #28). Тест-зона = Sheet-экран (`ModeTag "battle"`,
  прозрачный, Ignore) по подписке бутстрапа; карта прячется правилом видимости. «Начать» в тест-зоне = no-op
  (бой пока только через боевую расстановку — решение Макса). Сняты K5 (`HideTopForTest`/`ShowHiddenForTest`).
- **Ф6 инвентарь:** роутер владеет Sheet-экраном (`ModeTag "inventory"`); тумблер = `Remove(screen)`/`Push`.
  Закрытие = `Remove` ТОГО экрана, НЕ `PopAll` → карта петли под ним цела → нет Aborted. `RouterScreen.onExit`
  самообнуляет ссылку при любом снятии. Смерть `_inventoryOpen` (K6), `onClose`-Detach, веника `CloseOverlays`.
Верификация: 414/414 EditMode (+3 теста видимости), play (тест-зона Sheet по состоянию, pickingMode Ignore,
DI чист, 0 ошибок). Главный баг устранён СТРУКТУРНО (закрытие=Remove конкретного, не PopAll).

**► РАДИО-ТАБЫ СДЕЛАНЫ (2026-07-19, ждут коммита, 414/414, play-QA Макса ПРОШЁЛ «всё работает»).** play-QA
Ф5/Ф6 поймал 3 бага переключения (телепорт на карту при «Бой»×2; инвентарь с карты со 2 раза; «Бой» с
инвентаря = 3 нажатия). Трейсер `[UITRACE]` (см. ниже) показал корень: **табы работали как ТУМБЛЕРЫ**
(«Бой»×2 = вход+выход тест-зоны → карта возвращалась). **РЕШЕНИЕ МАКСА (конституция табов):** каждый таб
Карта/Бой/Инвентарь = отдельный РЕЖИМ ОТОБРАЖЕНИЯ, включён РОВНО ОДИН; таб = «перейти в режим», НЕ тумблер;
повтор = уже там = no-op. «Инвентарь» = бой + инвентарь над ним (не «инвентарь поверх карты»).
**Реализация:** тест-зона и инвентарь стали ЯВНЫМИ идемпотентными (`SetTestZoneRequest(bool Active)` вместо
`ToggleTestZoneRequest`; `ShowInventory`/`HideInventory` вместо `ToggleInventory`). В бутстрапе три
метода-перехода: `GoToBattle` (HideInventory + RequestTestZone(true)), `GoToInventory` (RequestTestZone(true)
+ ShowInventory), `GoToMap` (HideInventory + RequestTestZone(false); нет карты петли в стеке → OpenMapView
read-only). `MenuRouter.HasMapInStack` (`AnyScreen(ModeTag=="map")`) отличает «вернуться на карту петли» от
read-only. Контроллер `OnSetTestZone`: Active+!deploying+phase==None→Enter, !Active+testZone→Exit, иначе no-op.
Все 3 бага чинятся радио-моделью (проверено анализом переходов + play: идемпотентность инвентаря Show×2/Hide×2,
правило Sheet-прячет-Page вживую). **МОДЕЛЬ ПРОСТРАНСТВ (Макс уточнил, [[persist-world-decisions]]):** боевая
сцена = МИР (persist-сцена, камера) — ВСЕГДА база, НЕ в стеке навигатора, скрыть нельзя (только скин арены
тест↔обычная, финально через opacity/SAO — джус-задел). Карта = единственный непрозрачный оверлей, закрывает
мир. Инвентарь/тест-зона = прозрачные оверлеи, мир виден. Карту/инвентарь можно открыть в любой момент боя
(«посмотреть») — реализация держит (кнопки фазу не гейтят).

**ТРЕЙСЕР `UiTrace` (ВЫКЛЮЧЕН, НЕ удалять):** `Assets/_Project/Scripts/Data/Definitions/UiTrace.cs`,
`Enabled=false`. Вызовы `UiTrace.Log` в навигаторе (Push/Pop/PopAll/Remove + StackDesc), роутере
(Show/HideInventory/TestZone), бутстрапе (GoTo*), контроллере (OnSetTestZone/Enter/Exit). Включать точечно
при разборе переключений режимов/стека — вытягивать `[UITRACE]` из консоли (`read_console filter UITRACE`).

**СЛЕДУЮЩЕЕ: Ф7 (чистка мёртвого + tech-scribe).** Осталось: снести мёртвое (легаси RunTopBar-путь если
`_runModeBar` назначен, неиспользуемые IMenuRouter-методы), tech-scribe обновляет docs/wiki/tech (UI-слой:
навигатор/типы экранов/слои/радио-режимы/модель пространств), закрыть K8 (событие смены фазы), рассмотреть
трек Х/Т. Возможно финал реворка в след. сессии.

---

## Ключевые решения и отклонения от плана (с обоснованием)

1. **pixel-perfect ОТМЕНЁН полностью** (Макс: «у нас его не будет»). `IntegerPanelScaler`
   (целочисленный `ConstantPixelSize`) удалён — был навешан ТОЛЬКО в `UiGallery.unity` на `UIRoot`.
   Согласуется с `visual-direction.md` (стилизованный пиксель-арт, нецелый скейл, 2026-07-18).
   Панели: `scaleMode=ScaleWithScreenSize`, `referenceResolution=1920×1080`, `match=1` (высота),
   `scale=1` оставлен как шов под UI-scale доступности (в логике на `scale==1` не закладываться).
2. **«В меню?» = pause ГДЕ-ТО в стеке**, а НЕ план's `Top is PauseScreen`. Причина: настройки
   открываются поверх паузы → `Top` = settings, `Top is Pause` дало бы второй pause. Реализовано
   через `UiNavigator.AnyScreen(predicate)` + `RouterScreen.ScreenId == "pause"`.
3. **`IBattleClock` не имеет события смены фазы** — только `Phase` getter. Навигатор читает `Phase`
   внутри `SyncInput`; публичный `SyncInput()` дёргается на Push/Pop И извне (бутстрапом на смене
   фазы — заложено под Ф4/Ф5). Событие в боевой слой НЕ добавляла (это территория Ф5).
4. **Навигатор в Ф2 кладёт экраны прямо в корень панели** (слой = сам root). Именованные
   слои-контейнеры — Ф4. Топбар держится поверх через `BringToFront` бутстрапа (как было).
5. **ЕДИНАЯ СИСТЕМА ОТМЕНЫ ЗАБЕГА (Макс за неё, 2026-07-19; #37).** Вместо точечной латки карты — токен
   забега (`RunContext.Cancellation`) пробрасывается через ВСЮ цепочку показа (карта/continue/награда/
   магазин/сундук/ивент). Ресёрч подтвердил: это канонический cooperative-cancellation паттерн (CTS →
   токен во все await → один `catch OCE` на верхнем цикле — у `GameFlow` уже был). Обоснование объёма:
   узловые флоу (reward/shop/chest/event) ждали `await tcs.Task` БЕЗ токена → сами не закрывались →
   `CloseAll`-веник был единственным что их захлопывало; для карты его дефолт=null=Aborted=вылет. Точечно
   не чинилось без нового костыля. **Реализация:** см. верх файла (#37) + QA-трекер. **Ключ на будущее:**
   любой НОВЫЙ экран забега ОБЯЗАН нести `ct` в своём Request и вешать `AttachExternalCancellation` —
   иначе «В меню» с него оставит зомби. Простой экран (не result) → `UiNavigator.Push(screen, ct)`.
6. **СЕМАНТИКА ESC (Макс уточнил, 2026-07-19) — записана в план II.4 (КОНСТИТУЦИЯ).** ESC = НЕ «закрыть
   окно». Приоритет: drag → тултип → (в меню: шаг назад) → иначе открыть меню. ESC НИКОГДА не закрывает
   обычные окна (их закрывают свои контролы/завершение забега). Два «закрыть» РАЗВЕДЕНЫ (план II.5a):
   завершение забега (токен-отмена) vs ESC-навигация — не смешивать веником. Влияет на Ф4 (#32/#35/#36),
   Трек Т (тултип-ESC), Трек Х (drag-ESC).

---

## Осознанно отложено / зафиксировано (НЕ молча — адресовать в указанных фазах)

- **Мультиписатель контекста ЖИВ.** `DeploymentController` пишет `InputService.SetContext` мимо
  навигатора (в главном меню наблюдается `ctx=Combat`, но `GameplaySuppressed=true` держит ввод
  заглушённым → безопасно). **Лечит Ф5** (DeploymentController перестаёт звать SetContext, Ф5.5).
- **ConfirmDialog** (II.9.3, «В меню»/«Выход» с подтверждением) — отложен. Требует полноценного
  UXML-экрана + SerializeField в бутстрапе + проводки в CoreScene (кодом строить = нарушить
  правило «разметка в UXML»). Без него «В меню» = 1:1 с прежним. Сделать отдельным шагом.
- **Text event НЕ переведён на ShowAsync** — оставлен на `PushScreen`+`DetachFromPanelEvent` (K10).
  Причина: его выбор варианта НЕ закрывает экран (показывает результат-текст на месте, закрытие —
  отдельной кнопкой), не ложится в модель «резолв = закрытие». Адресовать отдельно.
  **UPD #37:** ивент теперь ct-aware — `TextEventFlow` вешает `AttachExternalCancellation(ctx.Cancellation)`,
  `OpenTextEvent` пушится через `PushScreen(..., ct: req.Cancellation)` → отмена забега размотает flow И
  снимет экран через навигатор. Т.е. «В меню» с ивента больше не оставляет зомби. Но контракт «выбор ≠
  закрытие» (K10) сам по себе НЕ снят — полный перевод на result-модель по-прежнему отдельная задача.
- **`HideTopForTest`/`ShowHiddenForTest`/`_hiddenForTest`** — мост через `_nav.Top.Root.display` +
  `_nav.SyncInput()`. Умирают в Ф5 (тест-зона станет Sheet поверх карты).
- **`SyncInput` на смене фазы бутстрапом** — доработка Ф4/Ф5. Сейчас контекст боя держит
  DeploymentController, поэтому в Ф2/Ф3 навигатор SyncInput'ит только на Push/Pop (поведение 1:1).

---

## Следующий шаг: Ф4 (слои-контейнеры)

**Файлы:** `UiRootBootstrap.cs` (`InitTopBar`, `Update`), `RunModeBarView.cs`, возможно `RunModeBar.uxml`.
**Суть (план II.4 + Ф4):** в корне UIDocument создать фиксированные слои-контейнеры (порядок
добавления = z-order): `layer-backdrop / layer-battle-center / layer-screens / layer-topbar /
layer-modal / layer-cursors / layer-tooltip / layer-system`. Навигатор кладёт Page/Sheet в
`layer-screens` (ПОД топбаром), Modal (pause/settings) — в `layer-modal` (ВЫШЕ топбара).
Умирают: `BringToFront()` в Update, `SendToBack`-жонглирование, репарент `battle-center`.
Backdrop/подсветка табов — по подписке на `navigator.Changed`, не поллинг структуры.
Швы Ф4: локаль-hot-swap persistent-слоёв (II.9.2); UI-звуки delegation-подпиской (II.9.4);
`ActiveSpace` навигатора под live-курсоры (II.14).

**КАРТА КОСТЫЛЕЙ Ф4 (разведано 2026-07-19, точные места — новой сессии НЕ перечитывать 4 файла):**
- **K1** `_topBar.Root.BringToFront()` — `UiRootBootstrap.Update` стр. ~255 (каждый кадр). Снос: топбар в
  `layer-topbar` один раз.
- **K2** репарент `battle-center` + `SendToBack` — `UiRootBootstrap.InitTopBar` стр. ~213-220. Снос:
  строить `battle-center` сразу в `layer-battle-center` (под `layer-screens`, над backdrop). Готча:
  `RunModeBarView` держит ссылки на `btn-start`/`battle-timer` по `Q(...)` в конструкторе — они должны
  пережить переезд слота (сейчас переживают репарент; при выносе в UXML слоя — перепроверить `Q`).
- **K3** поллинг `Phase`/backdrop/`runActive` в `Update` — `UiRootBootstrap.Update` стр. 223-263. Backdrop
  и подсветка табов → на подписку `navigator.Changed`. Поллинг ДАННЫХ (золото/акт/таймер/`SetFighting`) —
  ОСТАВИТЬ (законно, это не структура). `IBattleClock` события фазы НЕТ (реш. 3) → фаза пока поллится или
  бутстрап дёргает `navigator.SyncInput()` на смене (Ф4/Ф5).
- **K4/#35** `ActiveMode(phase)` — `UiRootBootstrap` стр. 322-328: `router.ActiveScreenMode ?? (phase!=None
  ? battle)`. Баг: когда pause (Modal, `ModeTag=null`) сверху → `ActiveScreenMode`=null → падает на фазу →
  подсветка «прыгает» на бой/карту при открытии ESC-меню. Фикс: подсветка из ВЕРХНЕГО НЕ-Modal экрана
  (Modal-меню не должно менять подсветку таба) — навигатору нужен «верхний тег, игнорируя Modal».

**QA-находки, чинимые в Ф4 (из П1 play-QA):**
- **#36** ESC-scrim затемняет+блокирует топбар, НЕ скрывает (план II.4 обновлён; Modal в `layer-modal`
  выше топбара, fullscreen-scrim `pickingMode Position`). Стиль scrim — `--gm-color-scrim` (есть,
  `tokens.semantic.uss:8`), класс `.gm-screen` scrim (`components.uss:20-27`), backdrop `.gm-screen-backdrop`
  (`components.uss:11`).
- **#35** подсветка табов из единого источника (см. K4 выше).
- **#32** ESC-меню только В ЗАБЕГЕ — гейт по `_runStates.Current != null` в `OnMenuToggle`/`ToggleSystemMenu`
  (сейчас ESC всегда открывает pause, даже в главном меню).
- **#33** фикс-высота панели настроек (не прыгает по табам) — `min-height` в USS. Попутно или Трек Д.

**РАЗВИЛКА Ф4 (решить в начале):** навигатору нужен ВТОРОЙ слой (`layer-modal`) — сейчас
`UiNavigator.Initialize(screensLayer, ctx)` знает один слой. Варианты: (а) `Initialize(screensLayer,
modalLayer, ctx)` и `Push` кладёт по `Kind` (Modal→modalLayer, иначе screensLayer); (б) навигатор держит
map `Kind→layer`. Рекомендую (а) — просто и явно. Затрагивает `UiNavigator.Push`/`RemoveScreen`/`PopAll`
(снимать из правильного слоя — хранить у экрана его слой или искать в обоих).

**Навигатор уже готов к Ф4:** есть `event Changed`, слой отдаётся в `Initialize(screensLayer, ctx)` —
достаточно передать `layer-screens` вместо корня.

---

## Реестр костылей UI-слоя (СНЕСТИ, не строить под них)

> **Принцип (Макс 2026-07-19):** костыли не проектируются как «правильно» — при работе над фазой
> целимся в верную модель и **сносим костыль**, а не подгоняем игру под него. Список — чтобы ни один
> не забылся и не «оброс» логикой. Колонка «снос» = фаза, где костыль умирает.

| # | Костыль | Где | Снос |
|---|---|---|---|
| ~~K1~~ | ~~`BringToFront()` топбара КАЖДЫЙ кадр~~ | `UiRootBootstrap.Update` | ✅ СНЕСЕНО (Ф4 `539d048e`, топбар в `layer-topbar`) |
| ~~K2~~ | ~~Репарент `battle-center` в root + `SendToBack`~~ | `UiRootBootstrap.InitTopBar` | ✅ СНЕСЕНО (Ф4, узел в `layer-battle-center`; полный вынос в UXML НЕ делали — не оправдан, ссылки RunModeBarView живы) |
| ~~K3~~ | ~~Поллинг структуры (backdrop/подсветка) каждый кадр~~ | `UiRootBootstrap.Update` | ✅ СНЕСЕНО (Ф4, подписка `nav.Changed` + ребро фазы/инвентаря; поллинг ДАННЫХ боя оставлен — законно; событие фазы у IBattleClock всё ещё НЕТ, реш.3) |
| ~~K4~~ | ~~Подсветка табов `ActiveScreenMode ?? фаза` — прыгает при pause~~ | `UiRootBootstrap.ActiveMode` | ✅ СНЕСЕНО (Ф4, `ActiveModeTag` = верхний НЕ-Modal, QA #35) |
| ~~K5~~ | ~~`HideTopForTest`/`ShowHiddenForTest`/`_hiddenForTest` мост~~ | `MenuRouter` | ✅ СНЕСЕНО (Ф5 `85ecdef9`, тест-зона = Sheet, карта прячется правилом видимости) |
| ~~K6~~ | ~~`_testActive`/`_inventoryOpen` флаги-тумблеры (рассинхрон)~~ | `UiRootBootstrap` | ✅ СНЕСЕНО (Ф5/Ф6, состояние = `TestZoneChangedEvent` + `_router.IsInventoryOpen`, QA #34) |
| ~~K7~~ | ~~`TestZoneArenaSkin` самотогл на бродкаст (а не на состояние)~~ | `TestZoneArenaSkin` | ✅ СНЕСЕНО (Ф5, слушает `TestZoneChangedEvent.Active`, #28) |
| K8 | Мультиписатель контекста: `SetContext` мимо навигатора | `DeploymentController` | ЧАСТИЧНО (Ф5: навигатор перезаписывает верно на Push/Pop; боевая расстановка без Push ещё полагается на прямой SetContext — полный снос ждёт события смены фазы, реш.3) |
| K9 | `Keyboard.current` напрямую (Enter расстановки) | `DeploymentController.ReadyPressed` | Трек Х |
| K10 | Text event на Push+Detach (выбор ≠ закрытие, не ShowAsync) | `MenuRouter` | отдельно |
| ~~K11~~ | ~~`btn-main-menu`: `CloseAll()`+`RequestReturnToMainMenu` = двойной путь → Aborted~~ | `MenuRouter.BuildPauseScreen` | ✅ СНЕСЕНО (QA #37, `Pop`+отмена) |
| ~~K12~~ | ~~flow (ShowMapAsync и др.) НЕ пробрасывают актовый `ct` в ShowAsync~~ | `MenuRouter.Show*Async` | ✅ СНЕСЕНО (QA #37, единая отмена) |
| K13 | `gm-screen--transparent`/`gm-pause-root` классы — оставлены как СТИЛЬ | `MenuRouter` | следить: не вернуть в логику |

## Находки пакета П1 (play-QA Макса 2026-07-19) — предлагаемый порядок фиксов

Полные описания — `docs/persist-battle-qa-findings.md` раздел «РЕВОРК UI, ПАКЕТ П1». Порядок:

1. **ПРИОРИТЕТНО (до/в начале Ф4):** #37 + #37b — Aborted при «В меню» с карты (K11+K12: цепочка
   отмены забега через `ct`, а не `CloseAll(null)`) + детальный лог пути прерывания. Кусается сейчас.
2. **Ф4 (слои — снос K1/K2/K3/K4):** #36 (ESC-scrim затемняет+блокирует топбар, НЕ скрывает — мнение
   Макса, см. план II.4 обновлён), #35 (подсветка табов из единого источника), #32 (ESC-меню только
   в забеге — гейт по `RunState.Current`). #33 (фикс-высота настроек) — попутно или Трек Д.
3. **Ф5 (тест-зона — снос K5/K6/K7/K8):** #34 («Бой» тумблер рассинхрон).

**Мнение Макса по табам/ESC (записано, ОТМЕНЯЕТ QA #12):** табы видны всегда В ЗАБЕГЕ и кликабельны
над обычными экранами (карта/инвентарь); ESC-меню превыше всех — **затемняет ВСЁ включая топбар +
блокирует клики, НО не скрывает**. Отражено в плане II.4 (layer-modal выше layer-topbar).

## Готчи исполнителя (сэкономят время в новой сессии)

- **Unity инстанс:** `Guildmaster-Autobattler@8736d65d`, порт 6400, один — `set_active_instance` не нужен.
- **Скрин UITK в play:** `manage_editor play` → `execute_code`: `panelSettings.targetTexture = RT` +
  `EditorApplication.Step()` ×6-8 + `ReadPixels` → PNG в `Assets/Screenshots/` (gitignored) → вернуть
  `targetTexture=null`. Рецепт — в скилле uitk `references/preview-and-screenshots.md`.
- **`execute_code` = тело метода:** без `using`; типы полными именами
  (`UnityEngine.UIElements.PanelSettings`). VContainer: `Resolve` НЕ generic без using —
  `(T)scope.Container.Resolve(typeof(T))`, `scope = FindFirstObjectByType<RootLifetimeScope>()`.
- **Программный клик UITK-кнопки:** `ClickEvent` НЕ триггерит `Button.clicked`. Работает
  `NavigationSubmitEvent`: `btn.Focus()` → Step → `using(var e = NavigationSubmitEvent.GetPooled()){ e.target=btn; btn.SendEvent(e);}`.
  `Query`/`Q` — extension, без using не резолвятся: обходить дерево через `ve.Children()`.
- **Коммиты через Bash-tool:** это Git Bash, НЕ PowerShell — heredoc `git commit -F - <<'EOF' … EOF`,
  а не PS here-string `@'…'@` (запорет subject литеральным `@`).
- **git add точечно** (ветка общая по конвенции): свои файлы; папочные `.meta` Unity кладёт на
  уровень ВЫШЕ новой папки (`Navigation.meta` рядом с `Navigation/`) — добавлять отдельно.
- **run_tests после правки:** `refresh_unity(compile:request, force)` → `read_console(filter CS)` (0 ошибок)
  → `run_tests(EditMode)` → `get_test_job(wait_timeout:180)`. **Готча:** первый `get_test_job` после
  компиляции может вернуть «No Unity Editor instances found» (domain reload отвалил мост на секунды) —
  просто переспросить `get_test_job` тем же `job_id`, мост переподключится. Текущий baseline: **408/408**
  EditMode (#37 добавил 1 регресс-тест к 407).
- **LF→CRLF warnings** при `git add` .cs на Windows — НОРМА (`autocrlf`), не ошибка, игнорировать.
- **Cooperative-cancellation (для будущих флоу):** ждать чужой tcs по токену = `await
  tcs.Task.AttachExternalCancellation(ct)` (UniTask) — при отмене бросает `OperationCanceledException`.
  Это паттерн всех узловых флоу после #37.

---

**Связано:** `docs/ui-architecture-rework-plan.md` (план), заметка памяти `ui-architecture-rework`
(проекция), `docs/persist-battle-qa-findings.md` (play-QA раунды).
