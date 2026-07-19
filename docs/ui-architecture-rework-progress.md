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
| **Ф4 Слои-контейнеры** | ⏳ следующий | — | контейнеры вместо BringToFront/репарента |
| Ф5 Тест-зона | ⬜ | — | Sheet-экран + единый источник TestZone |
| Ф6 Инвентарь | ⬜ | — | формальный Sheet, смерть `_inventoryOpen` |
| Ф7 Чистка + доки | ⬜ | — | снос мёртвого, tech-scribe |

**Точка остановки задана Максом (2026-07-19):** гнать Ф1→Ф2→Ф3 автономно, **стоп после Ф3**
на его play (полный забег) — НЕ после Ф2, как в протоколе плана. Ф0–Ф3 влиты.

**play-QA П1 ПРОШЁЛ (2026-07-19): 6 находок (#32-#37b)** — см. `docs/persist-battle-qa-findings.md`
+ раздел «Находки пакета П1» ниже (порядок фиксов). НЕ чинены (Макс: сначала записать всё + реестр
костылей + порядок, не латать).

**► СТАРТ СЛЕДУЮЩЕЙ СЕССИИ (согласовано Максом 2026-07-19): сначала #37 (вылет-Aborted «В меню» с
карты) + #37b (лог) отдельным фиксом, ПОТОМ Ф4 (слои).** Порядок в разделе «Находки пакета П1» ниже.

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

---

## Осознанно отложено / зафиксировано (НЕ молча — адресовать в указанных фазах)

- **Мультиписатель контекста ЖИВ.** `DeploymentController` пишет `InputService.SetContext` мимо
  навигатора (в главном меню наблюдается `ctx=Combat`, но `GameplaySuppressed=true` держит ввод
  заглушённым → безопасно). **Лечит Ф5** (DeploymentController перестаёт звать SetContext, Ф5.5).
- **ConfirmDialog** (II.9.3, «В меню»/«Выход» с подтверждением) — отложен. Требует полноценного
  UXML-экрана + SerializeField в бутстрапе + проводки в CoreScene (кодом строить = нарушить
  правило «разметка в UXML»). Без него «В меню» = 1:1 с прежним. Сделать отдельным шагом.
- **Text event НЕ переведён на ShowAsync** — оставлен на `PushScreen`+`DetachFromPanelEvent`.
  Причина: его выбор варианта НЕ закрывает экран (показывает результат-текст на месте, закрытие —
  отдельной кнопкой), не ложится в модель «резолв = закрытие». Адресовать отдельно.
- **`HideTopForTest`/`ShowHiddenForTest`/`_hiddenForTest`** — мост через `_nav.Top.Root.display` +
  `_nav.SyncInput()`. Умирают в Ф5 (тест-зона станет Sheet поверх карты).
- **`SyncInput` на смене фазы бутстрапом** — доработка Ф4/Ф5. Сейчас контекст боя держит
  DeploymentController, поэтому в Ф2/Ф3 навигатор SyncInput'ит только на Push/Pop (поведение 1:1).

---

## Следующий шаг: Ф4 (слои-контейнеры)

**Файлы:** `UiRootBootstrap.cs` (`InitTopBar`, `Update`), `RunModeBarView.cs`, возможно `RunModeBar.uxml`.
**Суть (план II.4 + Ф4):** в корне UIDocument создать фиксированные слои-контейнеры (порядок
добавления = z-order): `layer-backdrop / layer-battle-center / layer-screens / layer-topbar /
layer-cursors / layer-tooltip / layer-system`. Навигатор кладёт экраны в `layer-screens`.
Умирают: `BringToFront()` в Update, `SendToBack`-жонглирование, репарент `battle-center`.
Backdrop/подсветка табов — по подписке на `navigator.Changed`, не поллинг структуры.
Швы Ф4: локаль-hot-swap persistent-слоёв (II.9.2); UI-звуки delegation-подпиской (II.9.4);
`ActiveSpace` навигатора под live-курсоры (II.14).

**Навигатор уже готов к Ф4:** есть `event Changed`, слой отдаётся в `Initialize(screensLayer, ctx)` —
достаточно передать `layer-screens` вместо корня.

---

## Реестр костылей UI-слоя (СНЕСТИ, не строить под них)

> **Принцип (Макс 2026-07-19):** костыли не проектируются как «правильно» — при работе над фазой
> целимся в верную модель и **сносим костыль**, а не подгоняем игру под него. Список — чтобы ни один
> не забылся и не «оброс» логикой. Колонка «снос» = фаза, где костыль умирает.

| # | Костыль | Где | Снос |
|---|---|---|---|
| K1 | `BringToFront()` топбара КАЖДЫЙ кадр (императивный z-порядок) | `UiRootBootstrap.Update` | Ф4 (слои) |
| K2 | Репарент `battle-center` из топбара в root + `SendToBack` | `UiRootBootstrap.InitTopBar` | Ф4 |
| K3 | Поллинг `Phase` в `Update` вместо события | `UiRootBootstrap.Update` | Ф4/Ф5 (событие/подписка) |
| K4 | Подсветка табов `ActiveScreenMode ?? фаза` — прыгает при pause | `UiRootBootstrap.ActiveMode` | Ф4 (единый источник, QA #35) |
| K5 | `HideTopForTest`/`ShowHiddenForTest`/`_hiddenForTest` мост | `MenuRouter` | Ф5 (тест-зона = Sheet) |
| K6 | `_testActive` флаг-тумблер тест-зоны (рассинхрон) | `UiRootBootstrap.OnBattleMode` | Ф5 (QA #34) |
| K7 | `TestZoneArenaSkin` самотогл на бродкаст (а не на состояние) | `TestZoneArenaSkin` | Ф5 |
| K8 | Мультиписатель контекста: `SetContext` мимо навигатора | `DeploymentController` | Ф5 (Ф5.5) |
| K9 | `Keyboard.current` напрямую (Enter расстановки) | `DeploymentController.ReadyPressed` | Трек Х |
| K10 | Text event на Push+Detach (выбор ≠ закрытие, не ShowAsync) | `MenuRouter` | отдельно |
| K11 | `btn-main-menu`: `CloseAll()`+`RequestReturnToMainMenu` = двойной путь → Aborted | `MenuRouter.BuildPauseScreen` | приоритетно (QA #37) |
| K12 | flow (ShowMapAsync и др.) НЕ пробрасывают актовый `ct` в ShowAsync | `MenuRouter.Show*Async` | приоритетно (QA #37) |
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

---

**Связано:** `docs/ui-architecture-rework-plan.md` (план), заметка памяти `ui-architecture-rework`
(проекция), `docs/persist-battle-qa-findings.md` (play-QA раунды).
