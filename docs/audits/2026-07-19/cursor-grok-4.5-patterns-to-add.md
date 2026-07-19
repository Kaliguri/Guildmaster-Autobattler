**Предложения: паттерны, которых в проекте ещё нет (но стоило бы)**  
**Модель:** Cursor Grok 4.5  
**Дата:** 2026-07-19  
**Тип:** architecture proposals — все пункты **`proposed`**, не канон  
**База:** живой код `Assets/_Project/Scripts` + tech-wiki; инвентарь «что уже есть» сверен, не выдуман

Связанные аудиты того же дня:  
- `cursor-grok-4.5.md` — combat-ядро  
- `cursor-grok-4.5-seams-data-flow-stats.md` — presentation / data / persist / stats

---

## Зачем этот документ

В проекте уже сильный набор паттернов: headless sim, VContainer scopes, MessagePipe, command queue + FIFO reactives, SerializeReference composition, content registry, async UniTask flow, `IBattleSession` bridge, фасады `IAudioService`/`IInputService`, Strategy AI, Factory юнитов.

Ниже — **чего нет**, но что закрывает реальные дыры (из аудитов и швов), а не «модные буквы ради букв».  
Правило отбора: паттерн попадает сюда, только если без него уже больно или скоро будет больно (persist / кооп / контент / сейвы).

**Не предлагаю:** CQRS/MediatR, DOTS/ECS, UniRx, fixed-point lockstep, SO-event channels (Ryan Hipple) — либо отвергнуто проектом, либо дублирует MessagePipe/DI без выигрыша.

---

## Уже есть (краткий инвентарь — чтобы не предлагать дубли)

| Паттерн | Где |
|---|---|
| Composition Root + nested LifetimeScopes | `Root` → `World` → `Combat` |
| Headless sim + Presenter | `CombatSimulation` / `CombatPresenter` |
| Seam мутаций | `ICombatContext` |
| Pub/Sub | MessagePipe + C# events (dual bus) |
| Fixed tick + accumulator | `CombatLoopService` |
| Command queue (tick-scheduled) | `ISimCommand` / `_commandQueue` |
| FIFO internal events | `_eventQueue` / reactives |
| Factory / Registry / Strategy | `RuntimeUnitFactory`, `ContentRegistry`, `IUnitBrain` |
| Facade / Adapter | Audio, Input, Save |
| Bridge cross-scope | `IBattleSession` |
| Async flow stack | `GameFlow` / `IEventFlow` |
| Dirty cache | `Stats` |
| Object pool (presentation) | FloatingText, PixelBurst |
| Null Object / Solo stubs | `EmptyFocusPointSource`, `SoloReadyGate` |
| Partial MVVM + screen stack | ViewModels, `UiNavigator` |

---

## Предлагаемые паттерны (по приоритету)

### 1. Teardown / Guarded Transition (cancel-safe flow) — **P0**

**Чего нет:** `BattleFlow.Run` await'ит исход; при cancel `RequestReset` не вызывается (нет `try/finally`).  
**Зачем:** persist-мир = сцена живёт вечно; без гарантированного teardown арена остаётся Fighting под меню.  
**Как выглядит:**

```csharp
try {
    // launch → await outcome → retries
} finally {
    _session.RequestReset(); // идемпотентный world-reset
}
```

Обобщение: любой async-флоу с побочным эффектом на живой скоуп — **Guarded Transition** (enter/exit пара обязательна).  
**Триггер:** сейчас (меню/отмена забега).  
**Не путать с:** UniTask cancellation сам по себе не чистит сим.

---

### 2. Dual-Clock Pause Façade — **P1**

**Чего нет:** единого API «поставить бой на паузу». Space парит sim + `TimeScaleService`; Deployment/dev/команды часто только sim.  
**Зачем:** два часа (тик-системы vs `Time.timeScale`) уже есть; без фасада пути разъезжаются (сим стоит, анимации бегут / наоборот).  
**Как выглядит:**

```csharp
interface IBattlePause {
    void SetPaused(bool paused); // ALWAYS: CombatSimulation + TimeScaleService
}
```

Единственная точка для Input, Bootstrap, DevTools, будущих net PauseCommand.  
**Триггер:** любой второй путь паузы кроме Space.  
**Уже почти есть:** комментарии в `BattleInputController` — вынести в сервис.

---

### 3. Presentation Sink (отделить notify от мутаций) — **P1**

**Чего нет:** `ICombatContext` несёт и мутации (`DealDamage`/`Heal`/…), и presentational notify (`NotifyAttackStarted`, `ReportAreaHit`).  
**Зачем:** headless-контракт «context = мир» размывается; тесты/headless тащат «View»-семантику в имени.  
**Как выглядит:**

```csharp
interface ICombatMutationContext { /* DealDamage, Heal, ApplyEffect, … */ }
interface ICombatPresentationSink { // или только C# events на CombatSimulation
    void AttackStarted(...);
    void AreaHit(...);
}
```

Sim системы зависят от mutation; sink инжектится в Game/Presentation или остаётся event-only на симе.  
**Триггер:** следующий notify на context; или рефактор перед MP.  
**Не предлагаю:** вынос всего в MessagePipe внутри Combat (сломает headless/asmdef).

---

### 4. Frame Clock / Interpolation Provider — **P1**

**Чего нет:** презентер считает `alpha = deltaTime / TickDelta` сам.  
**Зачем:** правильная интерполяция = доля **незавершённого тика** из accumulator loop. Сейчас FPS-зависимое «псевдо-сглаживание».  
**Как выглядит:**

```csharp
interface ISimFrameClock {
    float InterpolationAlpha { get; } // accumulator / TickDelta, clamp 0..1
}
// CombatLoopService — единственный писатель
// CombatPresenter / ProjectileView — читатели
```

**Триггер:** уже болит визуально (известно в tech-changelog §3.3).  
**Связь:** тот же clock может позже кормить status-overlay «render pose».

---

### 5. Outward Event Policy (один контракт наружу) — **P2**

**Чего нет:** явной политики dual bus. C# events полные; MessagePipe — 4 события; Audio слушает C#, Feel — MP + иногда C#.  
**Зачем:** новый consumer не знает, куда подписываться; легко пропустить Heal/Evade/Reset.  
**Как выглядит (выбрать одно и зафиксировать в tech):**

| Вариант | Суть |
|---|---|
| **A. C# = truth** | MessagePipe — только Game-удобства; док «не жди полного покрытия» |
| **B. MP = full outward** | Presenter — тонкий republisher всех sim events |
| **C. Adapter layer** | `CombatEventHub` в Game: один Subscribe API → внутри C# или MP |

**Предложение Cursor Grok 4.5:** пока фестиваль — **A + явный док**; перед кооп-UI — **B**.  
**Не предлагаю:** третий канал (ScriptableObject events).

---

### 6. Content Contract Validator (semantic Spec-lite) — **P1**

**Чего нет:** semantic checks в CI/Doctor (есть id/dupes/null SerializeReference; нет polarity↔tag, ResourceType vs cost, Ability heal+damage vs TargetMode).  
**Зачем:** Druid-style heal+damage на одиночной цели молча дропает урон (`ApplyToTarget` XOR).  
**Как выглядит:**

```csharp
// Уже есть ContentValidationService — РАСШИРИТЬ правилами-спеками:
interface IContentRule {
    void Check(ContentDefinition def, ValidationReport report);
}
// PolarityTagConsistencyRule, AbilityPayloadModeRule, ResourceContractRule, …
```

Это не полноценный Specification GoF — **валидационные правила как Strategy-плагины** к Doctor/EditMode.  
**Триггер:** сейчас (контент растёт).  
**Не предлагаю:** runtime Spec-объекты на каждый таргет в бою (дорого и дублирует AbilityTargetMode).

---

### 7. Durable Write-Back (Unit of Work lite для RunState) — **P1**

**Чего нет:** drag позиций / battle-loadout не пишут в `RunState`; reset откатывает строй и релики. Hub-loadout пишет, battle — нет.  
**Зачем:** persist-мир обещает «отряд на арене = забег»; без write-back превью врёт относительно сейва.  
**Как выглядит:**

```csharp
interface IRunStateMutator {
    void CommitDeployment(IReadOnlyList<(vesselId, pos, relicId)> slots);
    void EquipRelic(...); // уже частично в RunStateService
}
// Вызов: на StartCombat и/или ExitDeployment — одна транзакция
```

Не полный UoW/ORM — **явный commit-шов** «эфемерный deploy → durable RunState».  
**Триггер:** до того как игрок привыкнет к «расставил — и сохранилось».

---

### 8. Save Schema Migration Pipeline — **P2** (док уже требует, кода нет)

**Чего нет:** runtime миграций `schemaVersion` на файле сейва (у ContentDatabase поле есть; editor migrations есть; load-path migrate — нет).  
**Зачем:** без этого апдейт ломает сейвы; вики уже зафиксировала требование.  
**Как выглядит:**

```csharp
interface ISaveMigration {
    int FromVersion { get; }
    int ToVersion { get; }
    JObject Migrate(JObject raw); // или typed DTO chain
}
// SaveService.Load: while version < current → apply chain
```

**Триггер:** первый breaking change `RunState` / после фестиваля до внешнего билдa.  
**Связь:** Workshop UGC — тот же паттерн.

---

### 9. Intent → Authority Command (net-ready) — **P2** (задел)

**Чего нет:** полных player intents как команд с `(tick, playerId, seq)`; есть только Pause/Resume RPC + solo stubs.  
**Зачем:** host-authoritative уже выбран; расстановка сейчас мутирует `Position` локально.  
**Как выглядит:**

```csharp
readonly struct PlayerIntent { /* PlaceUnit, Ready, Equip, … */ }
interface IPlayerIntentSource { /* Solo vs Net */ }
// Хост: Intent → ISimCommand с TargetTick
// Клиент: только send intent, никогда не пишет в sim
```

Частично намечено в `SoloPlayerIntentSource` / XML DeploymentController.  
**Триггер:** старт MP-фазы; не раньше.  
**Не предлагаю:** lockstep replay как основную модель (отвергнуто).

---

### 10. Combat Replay Recorder (optional event log) — **P3**

**Чего нет:** записи сид + лог команд для точного реплея (вики seed/saves упоминают как опцию).  
**Зачем:** баг-репорты, баланс, анти-чит позже; checksum уже есть как грубый probe.  
**Как выглядит:** append-only `List<ISimCommand>` + root seed на бой; `ReplayPlayer` кормит ту же очередь.  
**Триггер:** post-festival / инструмент баланса (рядом с SimBench).  
**Не предлагаю:** full event sourcing мира.

---

### 11. Sim-side Object Pool (RuntimeUnit / Projectile) — **P3**

**Чего нет:** пулы в симе; только VFX/text. Юниты/снаряды — new на бой.  
**Зачем:** на фестивальном масштабе не нужно; на волнах/DoT-снарядах — GC spikes в EditMode-массбенчах.  
**Триггер:** профиль покажет allocs в hot path **или** SimBench Monte-Carlo.  
**Осторожно:** пул + детерминизм = жёсткий Reset() контракт; иначе флаги текут между боями.

---

### 12. Phase Owner (single writer для BattlePhase) — **P2**

**Чего нет:** одного владельца фазы. Пишут `DeploymentController`, `BattleBootstrap`, `UnbindClock`.  
**Зачем:** persist-регрессии уже ловили (фаза Fighting на boot).  
**Как выглядит:**

```csharp
// Только BattleSession.SetPhase — или
interface IBattlePhaseController {
    void EnterDeployment(DeploymentKind kind);
    void EnterFighting();
    void EnterWorld(); // None
}
```

Остальные зовут контроллер, не `SetPhase` в обход.  
**Триггер:** следующий баг «фаза залипла».

---

### 13. Idempotent State Broadcast (уже почти есть для TestZone) — **P2**

**Что появилось:** `ToggleTestZoneRequest` (intent) → owner → `TestZoneChangedEvent` (state).  
**Чего не хватает как общего паттерна:** того же для Phase / Pause / Inventory-open (где ещё самотоги).  
**Зачем:** смерть рассинхрона флагов UI↔мир.  
**Паттерн:** **Intent / Decision / State triad** — обобщить в tech-доке как стандарт шины.  
**Триггер:** любой новый тумблер с несколькими слушателями.

---

### 14. Result&lt;T&gt; для flow-ошибок — **P3** (осторожно)

**Чего нет:** generic `Result<T, Error>`; есть `BattleOutcome`, `EventResult`, …  
**Зачем:** `RequestLaunch` → bool теряет причину (нет скоупа / пустой пресет).  
**Предложение:** не тащить монады везде; точечно:

```csharp
readonly struct LaunchResult {
    public bool Ok;
    public LaunchFailReason Reason; // None, NoScope, BadPreset, DirtyArena
}
```

**Триггер:** когда bool-API начнёт врать в логах.

---

### 15. Чего НЕ добавлять (и почему)

| Паттерн | Почему нет |
|---|---|
| MediatR / CQRS bus | MessagePipe + DI + command queue уже закрывают роли |
| UniRx/R3 | Дубль MessagePipe/C# events; цена обучения |
| DOTS/ECS | Ядро OOP+lists+детерминизм; переписывание ради моды |
| SO Event Channels | Уже есть MessagePipe |
| Full FSM framework | UniTask stack + enums достаточны до сложного AI-графа |
| Service Locator | Анти-паттерн проекта; `GlobalMessagePipe` — терпимый минимум |
| Decorator chain на эффекты | SerializeReference composition уже выбран |

---

## Карта внедрения (proposed roadmap)

| Волна | Паттерны | Зачем сейчас |
|---|---|---|
| **Сейчас (до следующего play)** | 1 Teardown, 2 Pause façade, 4 Frame clock, 6 Content rules (хотя бы Ability XOR) | Баги/корректность из аудитов |
| **До закрытия persist-среза** | 7 Write-back, 12 Phase owner, 13 Intent/State triad в доке | Стабильный мир между боями |
| **Перед внешним билдом** | 8 Save migrations, 5 Outward event policy | Сейвы и потребители событий |
| **MP-фаза** | 3 Presentation sink (если ещё не), 9 Intent→Command | Чистый host-auth |
| **Инструменты / масштаб** | 10 Replay, 11 Sim pools, 14 Result structs | Баланс и перф |

---

## Связь с находками аудитов

| Паттерн | Закрывает |
|---|---|
| 1 Teardown | F-1 cancel без reset |
| 2 Pause façade | P-4 dual pause |
| 3 Presentation sink | P-2 notify на ICombatContext |
| 4 Frame clock | P-1 alpha; tech-debt §3.3 |
| 5 Event policy | P-3 dual bus |
| 6 Content rules | D-1…D-3 |
| 7 Write-back | F-3, F-4 |
| 9 Intent command | Deployment XML «будущий шов» |
| 12 Phase owner | F-5, F-8 split phase |

---

## Подпись

**Автор предложений:** Cursor Grok 4.5  
**Статус:** все пункты `proposed` — принимать/отклонять Максу и реализационным контурам  
**Принцип:** не паттерн ради паттерна; только то, что чинит существующий шов или готовит выбранную архитектуру (host-auth, persist, content-id)
