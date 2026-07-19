# UI-архитектура: ресёрч и план переработки

> **Статус:** утверждённый план (Макс, 2026-07-19). Основа — системный ресёрч UI-слоя
> (4 параллельных аудита кода + внешние референсы + обсуждение с Максом).
> **Исполнитель:** агент-сессия (Opus). Документ самодостаточен — исполнять можно без чата-первоисточника.
> **Ветка:** `feat/persist-battle-flow` (ОБЩАЯ, см. «Правила исполнения»).
> **Связанные доки:** `docs/persist-battle-qa-findings.md` (раунды play-QA 1–5; раунд 5 — мандат этого ресёрча).

---

## TL;DR

У UI нет единого владельца состояния: «что показано, что глушится, что поверх» складывается
из ~17 переменных в 5 подсистемах (`MenuRouter`, `UiRootBootstrap`, `DeploymentController`,
`InputService`, `BattleSession` + скин тест-зоны), которые синхронизируются вручную — событиями,
снапшотами, CSS-классами-как-флагами и поллингом в `Update`. Каждая точечная правка добавляла
режим и ломала соседний (регрессии #30, #31 раунда 5 — прямое следствие). Отдельно найдена
независимая мина: **PanelSettings не масштабирует UI под разрешение**.

Решение — три слоя с жёсткими границами:

1. **Навигация** — стек типизированных экранов (Page/Modal/Sheet) + слои-контейнеры.
   Единственный владелец видимости и ввода. **Client-local, в коопе НЕ реплицируется.**
2. **Презентация** — MVVM: тупые View, POCO ViewModel, Unity 6 runtime binding для новых экранов.
3. **Данные** — единый источник истины (`RunState` и сервисы), host-authoritative шов под кооп.
   UI мутирует данные только через команды-интенты (MessagePipe), не напрямую.

Ключевая формула: **ввод и видимость становятся вычислимой функцией от (верх стека, фаза боя),
а не мутируемым состоянием.** План — 8 фаз, каждая = рабочая игра + коммит.

---

# Часть I. Текущее состояние (диагноз)

## I.1. Как сейчас решается «что показано»

Пять независимых подсистем, ни одна не владеет картиной целиком:

1. **Стек оверлеев** `MenuRouter._stack` (`Assets/_Project/Scripts/UI/MenuRouter.cs:30`) —
   единственная честная часть: настоящий LIFO-стек полноэкранных `VisualElement`.
   Но поведение экрана (глушит ли, прячет ли нижний, модальный ли) размазано по
   CSS-классам-маркерам и параметрам `Push`.
2. **Глушение ввода** — `InputService.GameplaySuppressed` + `Context`
   (`Assets/_Project/Scripts/Game/Input/InputService.cs`). **Три писателя:**
   `MenuRouter.SyncSuppress` (штатный путь), `MenuRouter.HideTopForTest/ShowHiddenForTest`
   (в обход, `MenuRouter.cs:237,246`), `DeploymentController` (`DeploymentController.cs:153,195,209,497`).
3. **Топбар** (`UiRootBootstrap` + `RunModeBarView`) — вне стека, поверх всего через
   `BringToFront()` **каждый кадр** (`UiRootBootstrap.cs:253`); `battle-center` физически
   репарентнут из топбара в корень и опущен `SendToBack` (`UiRootBootstrap.cs:212-219`).
   Вся видимость — поллинг в `Update`.
4. **Тест-зона** — режим без владельца: stateless-бродкаст `ToggleTestZoneRequest`,
   состояние продублировано в **пяти** местах (см. I.2).
5. **Фаза боя** `BattleSession.Phase` (`None/Deployment/Fighting`) — пишет боевой скоуп,
   UI поллит в `Update`.

## I.2. Инвентарь состояния (полный список — что подлежит замене)

| # | Состояние | Где | Кто пишет | Судьба в целевой архитектуре |
|---|---|---|---|---|
| 1 | `_stack` | `MenuRouter.cs:30` | Push/Pop/CloseAll | ЖИВЁТ — становится ядром навигатора |
| 2 | mode-тег `userData` ("inventory"/"map") | `MenuRouter.cs:217,224` | Push | ЖИВЁТ — свойство `UiScreen.ModeTag` |
| 3 | `_menuModeActive` | `MenuRouter.cs:47` | Enter/ExitMenuMode | УМИРАЕТ — ввод вычислим |
| 4 | `_prevContext` (снапшот) | `MenuRouter.cs:46` | EnterMenuMode | УМИРАЕТ — восстановление = пересчёт, не снапшот |
| 5 | `_hiddenForTest` | `MenuRouter.cs:229` | HideTopForTest | УМИРАЕТ — тест-зона = экран в стеке |
| 6 | класс `gm-screen--transparent` как флаг | `MenuRouter.cs:163,263,270` | OpenInventory | УМИРАЕТ — `ScreenKind.Sheet` |
| 7 | класс `gm-pause-root` как маркер | `MenuRouter.cs:266,295` | BuildPauseScreen | УМИРАЕТ — тип экрана |
| 8 | `InputService._context` | `InputService.cs:35` | MenuRouter + DeploymentController | ЖИВЁТ — но писатель ОДИН (навигатор) |
| 9 | `GameplaySuppressed` | `InputService.cs:40` | 3 писателя | ЖИВЁТ — но писатель ОДИН (навигатор; + dev-консоль) |
| 10 | `PointerOverUI` (`panel.Pick`) | `InputService.cs:151` | вычисляемое | ЖИВЁТ как есть |
| 11 | `_inventoryOpen` | `UiRootBootstrap.cs:98` | ToggleInventory + Detach-колбэк | УМИРАЕТ — вопрос к стеку (`ActiveModeTag == "inventory"`) |
| 12 | `_testActive` | `UiRootBootstrap.cs:277` | OnBattleMode | УМИРАЕТ — `IBattleSession.TestZone` |
| 13 | `_deploying` | `DeploymentController.cs:62` | контроллер | ЖИВЁТ (внутреннее состояние мира — законно) |
| 14 | `_testZone` | `DeploymentController.cs:63` | контроллер | ЖИВЁТ у владельца, публикуется через сессию |
| 15 | `BattleSession.Phase` | `BattleSession.cs:184` | контроллер/ResetToWorld | ЖИВЁТ — вход функции ввода |
| 16 | `TestZoneArenaSkin._gray` (самотогл!) | `TestZoneArenaSkin.cs:25,32` | подписка на Toggle | УМИРАЕТ — скин слушает состояние, не тумблер |
| 17 | `resolved`-guard ×10 копий | каждый `Build*Screen` | Detach-колбэки | УМИРАЕТ — «ровно один результат» гарантирует навигатор |

## I.3. Корни регрессий раунда 5 (для проверки «вылечено по построению»)

- **#31 (P0, «Бой» много раз → выход из игры):** `_testActive` (бутстрап) рассинхронился со
  стеком роутера (инвентарь открылся поверх скрытой карты) → `CloseOverlays` снёс скрытую
  карту → resolve узла null → `Aborted` → `Quit`. Плюс `TestZoneArenaSkin` самотоглится на
  каждый бродкаст, даже когда `DeploymentController` его проигнорировал (`DeploymentController.cs:167`) —
  копии расходятся с первого же «холостого» нажатия. Отсюда же **#28** (мельтешение/несерая трава).
- **#30 (P1, инвентарь мёртв):** глушение/контекст пишут три хозяина; путь тест-зоны пишет
  напрямую в обход `SyncSuppress` — после него `SyncSuppress` восстанавливает «не то».
- **#22 (раунд 3, было):** реэнтрантный `CloseAll` — `DetachFromPanelEvent`-страховки экранов
  синхронно резолвят флоу, продолжение открывает новый экран прямо в цикле удаления. Залатано
  снапшотом (`MenuRouter.cs:202-209`), но паттерн «flow-резолв на Detach» остался и продолжает кусаться.
- **#29 (хваталка юнита, круг+верх)** — НЕ UI-архитектура (deployment-пикинг в мире).
  **Вне скоупа этого плана**, отдельная задача.

## I.4. Мина фундамента: PanelSettings

`Assets/_Project/UI/GuildmasterPanelSettings.asset`: `ScaleMode = ConstantPixelSize`,
`Scale = 1`, `referenceResolution = {640, 360}`. Референс-разрешение **мёртвое** (работает
только при `ScaleWithScreenSize`). Итог: UITK рендерится 1:1 в физические пиксели, под
разрешение не масштабируется — на 4K вдвое мельче задуманного. Канон скилла uitk
(«канва 1920×1080, scale=1») не соответствует ассету. Дополнительно: в `CoreScene.unity:183`
у `UIDocument` `sortingOrder: 20` (магическое число, в ассете панели — 0).
В `Assets/_Project/Scripts/UI/IntegerPanelScaler.cs` есть какой-то скейлер — его роль
**проверить перед правкой** (возможно, он уже компенсирует масштаб).

## I.5. Слои рендера (что уже хорошо и остаётся)

Screen-space uGUI **нет вообще**: HP/мана-бары и цифры урона — world-space Canvas на
сортинг-слое `WorldUI`; UITK-панель прозрачна (`clearColor: 0`) и композитится поверх мира.
Поэтому работают «дырки»: корень инвентаря `pickingMode = Ignore`
(`LoadoutInventoryView.cs:52`), и `InputService.PointerOverUI` (`panel.Pick`) разводит
клики UI↔мир. Этот механизм **сохраняется как есть** — его инварианты (ровно один
`UIDocument`; прозрачные корни всегда `Ignore`) фиксируются в вики при чистке (Ф7).

## I.6. Дизайн-система (стиль) — здорова, отдельный трек

Трёхъярусные токены реально работают **для цвета** (semantic → primitives, тема свапается).
Болячки — не блокеры, вынесены в параллельный Трек Д (см. Часть III):
- semantic-яруса нет для spacing/border/font/radius — потребляются примитивы напрямую (десятки мест);
- нет `--gm-color-gold`: `--gm-brass-300` напрямую в ≥8 правилах (`components.uss:956,981,1074,1116,1148,1548,1668,918`);
- два недомигрированных дубля: `LoadoutScreen.uxml` (старый, английские литералы без loc,
  всё ещё жив через `MenuRouter.BuildLoadoutScreen`) vs `LoadoutInventoryScreen.uxml`;
  `RunTopBar.uxml` (легаси) vs `RunModeBar.uxml`. Классы глобального ран-бара живут в чужом
  неймспейсе `gm-loadout__*` (`components.uss:1234-1339`);
- 3 рецепта Honeti 9-slice скопированы по 6–7 раз (нет базовых классов);
- галерея `UiGalleryScreen.uxml` покрывает ~15% компонентов и сама нарушает токены (raw rgb/px);
- мёртвые токены: `--gm-ink-900`, `--gm-ink-400`, `--gm-parchment-200`, `--gm-inset-control`, `--gm-gap-stack`.

---

# Часть II. Целевая архитектура

## II.1. Три слоя и кооп-границы (КОНСТИТУЦИЯ этого плана)

| Слой | Форма | Кооп-природа |
|---|---|---|
| **Навигация** | стек типизированных экранов + слои-контейнеры; макро-фазы (MainMenu/Run/Battle) — существующий `GameFlow` | **client-local. НЕ реплицируется НИКОГДА.** У каждого игрока своё «какое окно открыто» |
| **Презентация** | MVVM: View тупые, VM = POCO, Unity 6 runtime binding (`[CreateProperty]`) для новых экранов | локальная |
| **Данные** | единый источник (`RunState`, сервисы); мутации из UI — только команды-интенты (MessagePipe) | host-authoritative шов; репликация (`NetworkVariable.OnValueChanged` → VM) подключается позже без слома |

Обоснование границы: реплицируется **состояние мира, а не состояние взгляда**. Общий
инвентарь/синхронные действия (подтверждены Максом) идут через слой данных: интент →
хост применяет → состояние реплицируется → VM обновляется. Optimistic update + rollback —
**отложено** до реального сетевого лага; шов (команды вместо прямых мутаций) кладётся сейчас.

## II.2. Навигатор — эскиз API

Неймспейс `Guildmaster.UI` (рядом с роутером). Все классы — POCO (тестируемы в EditMode без сцены).

```csharp
/// Тип экрана определяет ПОВЕДЕНИЕ — вместо CSS-классов-флагов и параметров Push.
public enum ScreenKind
{
    Page,   // полноэкранный, непрозрачный, глушит геймплей, прячет экран под собой (карта, магазин, ивент)
    Modal,  // поверх со scrim, глушит, НЕ прячет низ (пауза, настройки)
    Sheet,  // прозрачный, НЕ глушит (мир под ним жив через PointerOverUI), НЕ прячет низ структурно (инвентарь, тест-зона)
}

public abstract class UiScreen
{
    public abstract ScreenKind Kind { get; }
    public virtual string ModeTag => null;            // подсветка таба: "map"/"inventory"/"battle"/null
    public VisualElement Root { get; protected set; } // строится в Build из VisualTreeAsset
    public abstract void Build(UiScreenContext ctx);  // клон UXML + проводка; ctx несёт loc/VM/шаблоны
    public virtual void OnEnter() {}                  // добавлен в слой
    public virtual void OnExit() {}                   // снят (единственная точка отписок)
    public virtual void OnFocus() {}                  // стал верхним
    public virtual void OnBlur() {}                   // перекрыт другим
}

/// Экран, возвращающий результат флоу (награда, карта, магазин…). Ровно один резолв —
/// гарантия НАВИГАТОРА (снятие без выбора = defaultResult), а не Detach-колбэков.
public abstract class UiScreen<TResult> : UiScreen
{
    protected void Resolve(TResult result); // первый вызов побеждает, экран закрывается
    public abstract TResult DefaultResult { get; } // ESC/CloseAll без выбора
}

public sealed class UiNavigator
{
    public void Push(UiScreen screen);
    public void Pop();
    public void PopAll();                              // бывший CloseAll; резолвит result-экраны их DefaultResult
    public UniTask<TResult> ShowAsync<TResult>(UiScreen<TResult> s, CancellationToken ct);
    public UiScreen Top { get; }
    public bool IsOpen { get; }
    public string ActiveModeTag { get; }               // Top?.ModeTag
    public event Action Changed;                       // топбар/backdrop подписываются вместо поллинга структуры
}
```

## II.3. Ввод — вычислимая функция, один писатель

Сердце реформы. `GameplaySuppressed` и `Context` перестают быть мутируемым состоянием
с тремя хозяевами — они **выводятся**:

```
SyncInput():
    modal = Top != null && Top.Kind != Sheet
    input.GameplaySuppressed = modal
    input.SetContext(modal ? Menu : WorldContextOf(clock.Phase))

WorldContextOf(phase): Deployment → Deployment; Fighting → Combat; None → None
```

Вызывается навигатором на **каждое** изменение стека и по подписке на смену `BattlePhase`.
Следствия:
- `_prevContext`-снапшот не нужен: «восстановить» = пересчитать из фазы (снапшот гнил, если
  фаза менялась при открытом меню — теперь это невозможно по построению);
- `DeploymentController` **больше не зовёт `SetContext` вообще** — только `SetPhase`;
  `MenuRouter.HideTopForTest`-обходы исчезают;
- dev-консоль QFSW остаётся вторым легитимным писателем `GameplaySuppressed` (внешний
  модальный слой) — зафиксировать комментарием, не ломать.

## II.4. Слои — контейнеры вместо императивного z-порядка

В корне `UIDocument` создаются фиксированные контейнеры (порядок добавления = порядок отрисовки):

```
[0] layer-backdrop      — задний фон забега (pickingMode Ignore)
[1] layer-battle-center — кнопка «Начать»/таймер боя (за экранами — QA #19/#23)
[2] layer-screens       — стек навигатора (Page/Modal/Sheet добавляются сюда)
[3] layer-topbar        — RunModeBar (кликабелен всегда, в т.ч. над меню — текущее поведение, принятое QA #12)
```

Умирают: `BringToFront()` в `Update`, `SendToBack`-жонглирование, репарент `battle-center`
из топбара (слот сразу строится в своём слое). Scrim модалки — часть корня Modal-экрана
(как сейчас у паузы), топбар он не затемняет (принятое поведение).

## II.5. Flow-экраны — «ровно один результат» в одном месте

Сейчас: 10 копий `resolved`-guard + `DetachFromPanelEvent`-страховки + снапшот-хак в
`CloseAll` + двухшаговая реэнтрантность через `IRunControl.Cancel`. Целевое:

- `GameFlow`/петля акта продолжают публиковать `Open*Request` через MessagePipe
  (развязка сборок сохраняется);
- обработчик в `UiRootBootstrap` зовёт `navigator.ShowAsync(screen, ct)` и вызывает
  `req.OnResolved(...)` **ровно один раз** из результата;
- закрытие без выбора (ESC, PopAll, отмена забега) → навигатор резолвит `DefaultResult`
  (Skip / null / −1 — те же семантики, что сейчас);
- порядок «закрыть ДО колбэка» (комментарии в `MenuRouter.cs:488,565,601…`) навигатор
  гарантирует структурно: резолв происходит после снятия экрана из стека.

## II.6. Тест-зона — экран в стеке + единый источник состояния

Владелец мирового состояния — `DeploymentController` (как сейчас). Новое:

1. `IBattleSession` получает `bool TestZone { get; }` + событие `TestZoneChanged`
   (выставляет контроллер вместе с `SetPhase`). Это **единственный** источник «тест активен».
2. UI: `TestZoneScreen : UiScreen` с `Kind = Sheet`, `ModeTag = "battle"`, прозрачным корнем.
   Пушится/попается **по событию** `TestZoneChanged`, не по нажатию кнопки.
3. Кнопка «Бой» вне боя публикует интент `ToggleTestZoneRequest` (как сейчас) — и всё.
   Никакого `_testActive` в бутстрапе: решение «войти/выйти» принимает контроллер по своему
   состоянию (есть отряд, не боевая расстановка).
4. `TestZoneArenaSkin` подписывается на `TestZoneChanged` (состояние, не тумблер) —
   самотогл и рассинхрон #28 умирают.
5. Вход с карты: карта (Page) остаётся в стеке, тест-зона (Sheet) пушится поверх → карта
   скрыта правилом стека, ввод свободен (Sheet не глушит), выход = Pop → карта вернулась,
   ввод пересчитался. `HideTopForTest`/`ShowHiddenForTest`/`_hiddenForTest` умирают.
   Инвентарь поверх тест-зоны — ещё один Push; порядок корректен по построению (#31
   становится структурно невозможен).

## II.7. Анти-скоуп (что НЕ делаем в этом заходе)

- НЕ тащим Redux-фреймворк/стор: единый источник данных = существующие `RunState`+сервисы,
  принцип «команды вместо мутаций» — да, бойлерплейт actions/reducers — нет.
- НЕ реплицируем ничего сейчас: кладём только швы (команды, единый источник, событийные мосты).
- НЕ переписываем вью-билдеры экранов (`*ScreenView.Build`) — они тупые и годные; меняется
  только обвязка (кто их зовёт и как резолвится результат).
- НЕ редизайним экраны визуально и не трогаем UXML/USS сверх необходимого.
- НЕ чиним #29 (хваталка) — отдельная задача про deployment-пикинг.
- НЕ строим формальный макро-FSM класс: макро-фазы уже выражены `GameFlow`-циклом — достаточно.

---

# Часть III. Пошаговый план исполнения

Каждая фаза: рабочая игра, зелёные тесты (`./scripts/run-tests.ps1` или `run_tests` MCP),
коммит. Фазы идут строго по порядку — каждая опирается на предыдущую.

## Ф0. Фундамент: PanelSettings

**Файлы:** `Assets/_Project/UI/GuildmasterPanelSettings.asset`, `Assets/_Project/UI/Dev/PreviewPanelSettings.asset`,
`Assets/_Project/Scripts/UI/IntegerPanelScaler.cs` (прочитать!), `.claude/skills/xgaida-x-nixi-uitk/SKILL.md`.

1. Прочитать `IntegerPanelScaler` и найти его потребителей — выяснить, компенсирует ли он
   масштаб уже сейчас. Если да — согласовать решение с его логикой (не два скейлера).
2. Перевести runtime-панель на `ScaleWithScreenSize`, `referenceResolution = 1920×1080`,
   `ScreenMatchMode = MatchWidthOrHeight`, `match = 1` (высота). Токены (шрифты 22–48px,
   отступы 8–64) спроектированы под 1080p — теперь это станет правдой на любом экране.
   Стилизованный пиксель-арт-курс проекта допускает нецелые скейлы (решение 2026-07-18).
3. Preview-панель — те же значения (превью обязано совпадать с игрой).
4. Обновить скилл uitk (раздел «Канва») под фактическую конфигурацию.
5. **Приёмка:** скрин 1920×1080 (эталон — не изменился визуально) + скрин в
   нестандартном разрешении (2560×1440 или Device Simulator): UI занимает ту же долю экрана.
   Скрины Максу в чат (HARD-правило).

## Ф1. Скелет навигатора (создать, не подключать)

**Новые файлы:** `Assets/_Project/Scripts/UI/Navigation/` → `ScreenKind.cs`, `UiScreen.cs`,
`UiNavigator.cs`, `UiScreenContext.cs`. Тесты: `Assets/_Project/Tests/EditMode/UiNavigatorTests.cs`
(по месту существующих EditMode-тестов проекта).

1. Реализовать API из II.2 + `SyncInput` из II.3 (зависимости `IInputService`, `IBattleClock`
   через конструктор; слои — `VisualElement`-контейнеры, отдаются в `Initialize(root)`).
2. EditMode-тесты БЕЗ сцены (панель не нужна — стек и `SyncInput` чистая логика; для
   `VisualElement` достаточно голых элементов): push/pop порядок; Page прячет низ, Modal/Sheet
   нет; suppress = f(top, phase) по таблице (Sheet поверх Deployment → ввод миру; Modal поверх
   Sheet → Menu; pop модалки при Phase=Fighting → Combat, не снапшот); `ShowAsync` — ровно один
   резолв (явный / PopAll → Default / повторный Resolve игнорируется); реэнтрантный Push из
   резолва при PopAll не ломает обход (регресс-тест на #22).
3. Регистрация в DI (`RootLifetimeScope`) — рядом с `MenuRouter`.
4. **Приёмка:** тесты зелёные; поведение игры не изменилось (навигатор ещё не подключён).

## Ф2. Ядро-своп: MenuRouter переезжает на навигатор (поведение 1:1)

Эволюция изнутри — БЕЗ параллельной второй системы (два владельца ввода = та же болезнь).

**Файлы:** `MenuRouter.cs`, `UiRootBootstrap.cs`.

1. Внутри `MenuRouter` заменить `_stack`/`Push`/`Pop`/`CloseAll`/`SyncSuppress` на делегацию
   в `UiNavigator`. Каждый существующий `Build*Screen` оборачивается в `UiScreen` с честным
   `Kind`: pause/settings → Modal; map/shop/event/reward/chest/outcome/mainmenu/continue/loadout → Page;
   inventory → Sheet (класс `gm-screen--transparent` остаётся только как СТИЛЬ, из логики уходит).
2. Убить: `_menuModeActive`, `_prevContext`, `EnterMenuMode`/`ExitMenuMode`, проверку
   `ClassListContains(TransparentScreenClass)` в логике, маркер-скан `gm-pause-root` в
   `ToggleSystemMenu` (заменить на `Top is PauseScreen`-проверку по типу).
3. `IMenuRouter`-контракт и все потребители (GameFlow, бутстрап) — НЕ трогать в этой фазе.
4. **Приёмка:** ручной смоук по чек-листу §III-Приёмка (сценарии 1–4) + скрины; тесты зелёные.

## Ф3. Flow-результаты: ShowAsync вместо Detach-резолвов

**Файлы:** `MenuRouter.cs` (методы `OpenReward/OpenTextEvent/OpenMap/ShowContinue/OpenShop/OpenChest/ShowOutcome/OpenMainMenu`), `UiRootBootstrap.cs`.

1. Каждый flow-экран становится `UiScreen<TResult>` с `DefaultResult` (Skip / −1 / null / …
   — семантики из текущих Detach-страховок, `MenuRouter.cs:505,541,577,618,650,675,699,733`).
2. Обработчики `Open*Request` в бутстрапе: `await navigator.ShowAsync(...)` →
   `req.OnResolved(result)` один раз.
3. Убить: все 10 `resolved`-замыканий, все `DetachFromPanelEvent`-страховки резолва,
   снапшот-хак в `CloseAll` (комментарий QA #22 — заменить регресс-тестом из Ф1),
   комментарии «закрыть ДО колбэка» (гарантирует навигатор).
4. Кнопки паузы «В меню»/«Выход» (`IRunControl`) — проверить цепочку отмены забега:
   `PopAll` → отмена CTS → главное меню открывается заново. Регресс-сценарий №6 чек-листа.
5. **Приёмка:** полный забег end-to-end (меню → карта → бой → награда → карта → … → исход → меню)
   без визуальных изменений; тесты зелёные.

## Ф4. Слои: контейнеры вместо BringToFront/репарента

**Файлы:** `UiRootBootstrap.cs` (`InitTopBar`, `Update`), `RunModeBarView.cs`, `RunModeBar.uxml` (если слот «Начать» проще вынести в UXML слоя).

1. Создать контейнеры II.4 в `Initialize` навигатора; стек добавляет экраны в `layer-screens`.
2. Топбар — в `layer-topbar` один раз; убить `BringToFront()` из `Update`.
3. `battle-center` — строить сразу в `layer-battle-center`, убить репарент+`SendToBack`.
4. Backdrop — в `layer-backdrop`; видимость — по событиям `navigator.Changed` +
   `TestZoneChanged`/фаза (подписки), а не поллинг структуры. Поллинг ДАННЫХ в `Update`
   (золото, таймеры, `SetFighting`) — остаётся, это законно.
5. Подсветка табов: подписка на `navigator.Changed` → `SetActiveMode(navigator.ActiveModeTag ?? фаза)`.
6. **Приёмка:** скрины всех комбинаций слоёв (меню поверх карты; инвентарь; бой; «Начать»
   за оверлеями — QA #19/#23 не регрессят); тесты зелёные.

## Ф5. Тест-зона: единый источник + экран в стеке (самая опасная миграция)

**Файлы:** `BattleSession.cs` (+`IBattleSession`/`IBattleClock` в `Data/Definitions/BattleClock.cs`),
`DeploymentController.cs`, `UiRootBootstrap.cs` (`OnBattleMode`), `TestZoneArenaSkin.cs`,
новый `TestZoneScreen.cs`.

1. `IBattleSession.TestZone` + `TestZoneChanged` — пишет только `DeploymentController`
   (`EnterTestZone`/`ExitTestZone`/`StartCombat`/`OnFreeDeployment`).
2. `TestZoneScreen` (Sheet, ModeTag "battle") пушится/попается по `TestZoneChanged` в бутстрапе.
3. `OnBattleMode` упрощается до интентов: Fighting → `PopAll`; иначе publish
   `ToggleTestZoneRequest` — решает контроллер. Особые случаи: mode "inventory" → PopAll перед
   интентом (как сейчас); модальный flow (`IsOpen` и Top не Sheet/не карта) → no-op (как сейчас).
   Карту НЕ трогать: она остаётся под Sheet-экраном (II.6.5). Убить `_testActive`.
4. `TestZoneArenaSkin`: подписка на `TestZoneChanged(bool)` вместо самотогла.
5. `EnterTestZone` больше не зовёт `SetContext` (Ф2 уже отдала ввод навигатору — проверить,
   что фаза `Deployment` через `SyncInput` даёт миру ввод при Sheet-верхе).
6. **Приёмка (регресс раунда 5):** «Бой» многократно с карты — вход/выход стабилен, карта
   возвращается, забег цел (#31); «Бой» на ивенте/магазине — no-op; трава/пол сереют и
   возвращаются синхронно (#28); инвентарь поверх тест-зоны → drag реликвии на юнита работает,
   выход из тест-зоны корректен. Скрины Максу.

## Ф6. Инвентарь: формальный Sheet, смерть `_inventoryOpen`

**Файлы:** `UiRootBootstrap.cs` (`ToggleInventory`, `Update`), `MenuRouter.cs` (`OpenInventory`).

1. `InventoryScreen : UiScreen` (Sheet, ModeTag "inventory"); тумблер кнопки:
   `navigator.ActiveModeTag == "inventory" ? Pop : Push`.
2. Убить `_inventoryOpen` и `onClose`-колбэк через Detach; backdrop-логика читает навигатор.
3. Relic-drag (`RelicDragEvent` → `DeploymentController`) — не трогать, работает через интенты.
4. **Приёмка (регресс #30):** в расстановке открыть инвентарь → юниты/камера живут под ним,
   drag из грида на юнита работает; ESC поверх инвентаря → меню; закрытие меню → инвентарь
   жив и интерактивен. Скрины.

## Ф7. Чистка и документация

1. Удалить мёртвое: `HideTopForTest`/`ShowHiddenForTest`/`HasHiddenForTest`, легаси-путь
   `RunTopBarView` (если `_runModeBar` назначен в CoreScene — согласовать с Максом),
   неиспользуемые методы `IMenuRouter`; финальное имя: `MenuRouter` либо растворяется в
   `UiNavigator` + экраны, либо остаётся тонким фасадом `IMenuRouter` — по факту того, что
   от него осталось (решить по месту, не плодить слоёв ради слоёв).
2. Прогнать ВЕСЬ чек-лист приёмки (ниже) + полный тест-сьют.
3. tech-scribe: обновить `docs/wiki/tech` (UI-слой: навигатор, типы экранов, слои, функция
   ввода, инварианты PointerOverUI) + запись в инженерный changelog.
4. Обновить память агента (заметка `loadout-screen-redesign` / `ui-architecture-rework`).

## Трек Д (параллельно или после, НЕ блокирует Ф0–Ф7): дизайн-система

Порядок по рычагу: (1) `--gm-color-gold` + 2×`danger-hover`, миграция 8+2 мест;
(2) судьба легаси-дублей — `LoadoutScreen.uxml` (старый loadout: либо удалить вместе с
`OpenLoadout`-путём, если новый инвентарь его заменил, либо довести loc-ключи) и
`RunTopBar.uxml`; переименовать классы ран-бара из `gm-loadout__*` в `gm-runbar__*`;
(3) базовые классы 9-slice (`gm-frame--outline/window/fill`), свести 6–7 копий;
(4) галерея: все `.gm-*`-компоненты, свотчи из токенов; (5) мёртвые токены; loc-литералы
`SettingsScreen.uxml` (заголовок/табы/кнопки). Решение по semantic-ярусу для
spacing/font/border: **оставить примитив-директ и переформулировать правило №2 скилла**
(семантика обязательна только для цвета) — не плодить алиасы ради буквы правила.

## Правила исполнения (HARD — нарушение = переделка)

1. **Общая ветка** `feat/persist-battle-flow`, возможна параллельная сессия: `git add` только
   точечно (свои файлы), свежий `git log` перед коммитом, `CoreScene.unity` не сохранять
   вслепую из редактора.
2. Коммит после каждой фазы (Conventional Commits, англ., от Max Gaida, без Co-Authored-By).
3. Тесты — под игру, не наоборот; не ослаблять поведение ради зелёного.
4. Весь новый видимый текст — через loc-ключи, RU заполнен (но в этом плане нового текста
   почти нет — перенос существующего).
5. UI-показ Максу = свежий скрин КАРТИНКОЙ в том же ответе, честный масштаб.
6. Разметка/стиль — UXML/USS + токены; в C# — только логика (кишки custom control — исключение).
7. Никаких молчаливых временных решений — любой обход фиксируется явно.
8. Play-mode визуальную приёмку делает Макс; агент проверяет сборку, тесты, `read_console`,
   UI-скрины через превью/`render_ui`.

## Чек-лист финальной приёмки (сценарии из QA-раундов 1–5)

1. ESC везде: в бою → меню поверх, мир жив; ещё ESC → закрыть; в настройках → назад в паузу.
2. Карта → узел → бой → «Начать» → победа → награда → карта: полный цикл, один резолв на экран.
3. «Начать» не видна ни в меню, ни на карте, ни в главном меню (#23); видна только в расстановке.
4. Табы подсвечиваются из одного источника: карта/инвентарь/бой (#11/#21).
5. Инвентарь: юниты и камера под ним живут (#16/#30), drag реликвии на юнита (#5/#27), ESC поверх.
6. Пауза → «В меню»: забег прерван, главное меню, без вылета (#22); «Выход» закрывает игру.
7. Тест-зона: с карты и из пустого стека, многократно, с инвентарём поверх — без вылета (#31),
   пол+трава сереют/возвращаются синхронно (#28), выход возвращает карту.
8. Разрешение ≠1080p: UI пропорционален (Ф0).

## Внешние референсы (основа решений)

- QuizU (официальный сэмпл Unity, UITK screen manager): https://discussions.unity.com/t/quizu-managing-menu-screens-in-ui-toolkit-post-2/310272
- UnityScreenNavigator (Page/Modal/Sheet-типизация): https://github.com/Haruma-K/UnityScreenNavigator
- Unity 6 runtime data binding (MVVM): https://docs.unity3d.com/6000.4/Documentation/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/data-binding.html
- UITK + Input System (гейтинг, PointerOverUI): https://docs.unity3d.com/6000.1/Documentation/Manual/UIE-faq-event-and-input-system.html
- NGO Authority (host-authoritative границы): https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.11/manual/terms-concepts/authority.html
- NetworkVariable → OnValueChanged (мост сеть→VM, на будущее): https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.5/manual/basics/networkvariable.html
- Инвентарь-синк (command + optimistic + rollback, на будущее): https://gamecoderstudios.com/how-we-built-a-worldwide-inventory-sync-for-a-crafting-heavy-mmo/
- Дизайн-система UITK (боевая, токены/BEM): https://github.com/sinanata/unity-ui-toolkit-design-system
