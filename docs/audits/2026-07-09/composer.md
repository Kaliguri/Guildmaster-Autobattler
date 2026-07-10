**Статус:** Аудит кодовой базы  
**Дата:** 09.07.2026, 23:51  
**Модель:** Composer (Cursor)  
**Объём:** `Assets/_Project/Scripts/` (~105 runtime-файлов), 27 EditMode + 1 PlayMode тестовых файлов, 171 `[Test]`

---

## Вердикт

Проект в **хорошем состоянии для конца Фазы 3**. Архитектурные правила из вики в целом соблюдены: детерминированная тиковая симуляция, разделение Combat/Presentation, VContainer без синглтонов, граф сборок соответствует документу. Критических «сломанных» архитектур нет — есть **осознанный техдолг** и несколько мест, где ранние решения уже устарели относительно принятой host-authoritative модели.

Главный риск — не текущий бой (он стабилен и хорошо покрыт тестами), а **подготовка к мультиплееру и ран-лупу**: RNG не используется, сид боя генерируется через `UnityEngine.Random`, сетевой слой всё ещё несёт lockstep-наследие, а ряд систем (арена, сохранения, UI) описаны в документах, но отсутствуют в коде.

**Оценка зрелости по слоям:**

| Слой | Оценка | Комментарий |
|---|---|---|
| Combat simulation | 🟢 Сильный | 30 Hz, command queue, checksum, 171 EditMode-тест |
| Data / SO pipeline | 🟢 Сильный | Реликвии, эффекты, `[SerializeReference]` |
| DI / Game layer | 🟢 Хороший | Два LifetimeScope, чистый ctor-inject в Combat |
| Presentation | 🟡 Рабочий dev-harness | Есть дубли и костыли, но граница не нарушена |
| Networking | 🔴 Устаревший каркас | Lockstep-остатки, нет репликации состояния |
| Run flow / Save / UI | ⚪ Не начато | Только asmdef-заглушки |

---

## Что сделано хорошо

### 1. Боевая симуляция — ядро проекта

`CombatSimulation` (~472 строки) — центральный, но не раздутый god-object. Порядок тика явный и задокументирован:

```
ApplyCommands → Brain → Ability → Movement → Displacement
→ SpatialHash → AutoAttack → Projectiles → Regen → Effects
→ DrainEvents → Death → CheckOutcome → tick++
```

Практики детерминизма соблюдены:
- фиксированный `SimConstants.TickDelta` (30 Hz);
- `for`-циклы по индексу, без `foreach` по `Dictionary`/`HashSet` в горячих путях;
- tie-break по `Id` (например, retreat в `MovementSystem`);
- AI stagger через `BrainPhase`;
- кап догоняющих тиков (`MaxCatchUpTicksPerFrame = 5`) в `CombatLoopService`;
- кап реактивных событий (`MaxEventsPerDrain = 512`);
- `ComputeChecksum()` для валидации синхронизации.

`Time.deltaTime` используется **только** в `CombatLoopService` — ровно как задумано.

### 2. Граф сборок и DI

11 asmdef соответствуют `guildmaster-wiki/.../1. Сборки (Assembly Definitions).md`. Зависимости текут вниз; `Guildmaster.Game` — единственный composition root. `Guildmaster.Combat` не тянет VContainer.

- Нет `static Instance` / service-locator синглтонов в игровой логике.
- `Combat` использует только constructor injection — без `[Inject]`.
- Два скоупа: `RootLifetimeScope` (сессия) + `CombatLifetimeScope` (бой).

### 3. Разделение симуляции и презентации

`CombatPresenter` явно документирован как read-only. Presentation **не вызывает** `DealDamage`, `ApplyEffect`, `EnqueueCommand`, `Tick()`. Мутация состояния — только через `CombatSimulation` и системы.

### 4. Тесты

27 EditMode-файлов, **171 тест** — сильное покрытие для фазы разработки:
- детерминизм (`CombatSimulationTests`);
- damage pipeline, stats, effects, abilities;
- все 7 Common-реликвий (slice-тесты);
- attack timing / windup;
- spike для `[SerializeReference]` через границу Data↔Combat.

Headless-конструкция симуляции в тестах (без VContainer, без сцен) — правильный паттерн.

### 5. Контент через ScriptableObject

`RelicData`, `EffectData`, `AbilityData`, `AIProfile`, `StatsConfig` — баланс и поведение в данных, не в коде. Полиморфные компоненты эффектов через `[SerializeReference]`.

---

## Критические находки

### 1. `IRngService` подключён, но не используется в боевой логике

RNG инжектится в `CombatSimulation`, попадает в checksum через `_rng.Snapshot()`, но **ни один файл в `Combat/` не вызывает** `NextUInt`, `NextFloat`, `Chance()`.

Следствия:
- тесты на разные сиды дают идентичный результат (placeholder);
- инфраструктура RNG — мёртвый груз до появления критов/разброса;
- при добавлении случайности легко забыть прогнать через `IRngService`.

**Рекомендация:** при первой механике с рандомом — сразу тест `DifferentSeeds_ProduceDifferentOutcomes`. До этого — явно пометить в техдолге как «scaffolding Phase MP».

### 2. `UnityEngine.Random` в генерации сида боя

Файл: `Assets/_Project/Scripts/Game/CombatLifetimeScope.cs`, метод `GenerateBattleSeed()` (строки 83–90).

Нарушает правило «`UnityEngine.Random` в игровой логике запрещён». Сейчас безвредно (RNG не потребляется), но при включении случайных механик или replay станет блокером.

**Рекомендация:** заменить на детерминированную цепочку: `sessionSeed XOR battleIndex` или явный сид из `BattleSetupCommand`. Убрать `UnityEngine.Random` полностью.

### 3. `NetworkCommandRelay` — lockstep-наследие vs host-authoritative

Файл: `Assets/_Project/Scripts/Net/NetworkCommandRelay.cs`.

Код честно задокументирован, но **архитектурно противоречит** принятому решению от 2026-06-19. `BroadcastCommandClientRpc` заставляет всех клиентов применять команды локально — это путь lockstep, не host-auth.

`SimSyncProbe` в `Net/_Parked/` — ещё один артефакт lockstep-модели.

**Рекомендация:** до Фазы 6 не наращивать функционал поверх `NetworkCommandRelay`. При старте MP — переписать на `intent → host queue → replicate state`, удалить ClientRpc broadcast.

---

## Средний приоритет

### 4. DevTools обходит command queue

`GuildmasterCommands.cs`: `PauseForConsole` / `ResumeAfterConsole` вызывают `_simulation.SetPaused()` напрямую, минуя `PauseCommand`/`ResumeCommand`.

Пауза с `TargetTick` — единый путь для replay, MP и DevTools. Прямой `SetPaused` ломает консистентность.

`gm_rng_seed` — заглушка: логирует, но сид не меняет.

### 5. Хардкод вместо конфигов

| Место | Значение | Проблема |
|---|---|---|
| `ProjectileSystem.IsOutOfBounds` | `±200f` | Нет `ArenaBounds`; юниты/снаряды уходят в бесконечность |
| `BattleSetupBuilder` | spawn X: `-5f` / `5f` | Расстановка не из SO/конфига |
| `MovementSystem` | `range * 0.6f` | Magic number для kite band |
| `MarkTransferComponent` | `SearchRadius = 500f` | Не в данных эффекта |
| `CombatLifetimeScope` | `_armorK`, `_spatialHashCellSize` | На MonoBehaviour scope, не в battle config |

Документ `15. Арена и расстановка (дизайн).md` описывает систему, в коде её нет.

### 6. `ISimEvent` — мёртвый интерфейс

Определён в `Core/Simulation/ISimEvent.cs`, **нигде не реализуется**. Фактически используется `CombatEventData` + `Queue<CombatEventData>` в `CombatSimulation`.

### 7. Дублирование presentation-кода

- `FloatingText` + `ObjectPool` в `CombatPresenter` — **активный путь**.
- `DamageNumber` + `DamageNumberSpawner` — **не используются**.
- `FloatingText.Spawn()` — статический `Instantiate` без пула (вторичный путь).

### 8. Двойной канал событий (C# events + MessagePipe)

`CombatSimulation` поднимает C# events; `CombatPresenter` подписывается напрямую. `CombatEvents.cs` для MessagePipe покрывает не всё (`OnHealed`, `OnAttackEvaded` — только C# path).

### 9. Пробелы в тестах

| Область | Статус |
|---|---|
| `CombatLoopService` (accumulator, catch-up cap) | Нет тестов |
| `CombatLifetimeScope` / DI wiring | Нет PlayMode |
| `NetworkCommandRelay` | Нет тестов |
| `BattleScene` end-to-end | 2 PlayMode-теста, headless (без сцены) |

---

## Низкий приоритет / косметика

### 10. `CombatSimulation` растёт

~472 строки, 10 систем в конструкторе. Вики фиксирует осознанное отложение разбиения. Пока читаемо.

### 11. `RuntimeUnit` — mutable shared state

Presentation держит live-ссылки на `RuntimeUnit`. По конвенции не мутирует — но нет compile-time гарантии.

### 12. Float-детерминизм не доказан

`Vector2`, `Mathf` повсюду в Combat. Для host-auth co-op PvE — приемлемо. Зафиксировать в доке как принятое решение.

### 13. Stub-модули

`Guild/`, `UI/`, `MiniGames/` — только `.asmdef`, 0 `.cs`. Соответствует roadmap.

### 14. Мелкие отклонения DevTools

`FindObjectOfType`, `LifetimeScope.Find`, runtime `AddComponent` для overlay — изолировано в dev-only путях.

### 15. Устаревшая документация

`00. Обзор и карта кода` (дата 2026-06-19), `5. Технологический стек` (старые имена `DamageCalculator`, grid AI), путь тестов в `1. Сборки`, пустой `0.1. Открытые вопросы`. Код впереди части документов.

---

## Архитектурные решения: что правильно, что пересмотреть

### Оставить как есть

| Решение | Почему |
|---|---|
| Host-authoritative (не lockstep) | Правильно для co-op PvE с паузой |
| `AIProfile` + `ProfileBrain` | Достаточно для 7 реликвий; полный граф — YAGNI |
| `[SerializeReference]` через границу Data↔Combat | Проверено spike-тестом |
| `ICombatContext` / `IBattleView` | Оправданы для тестов |
| Command queue с `TargetTick` | Единый путь для replay, MP, pause |
| Catch-up cap + event drain cap | Защита от spiral-of-death |

### Пересмотреть до следующей фазы

| Решение | Альтернатива | Когда |
|---|---|---|
| Сид боя = `UtcNow ^ Random` | Сид из `RunState` / host command | До случайных механик |
| `NetworkCommandRelay` broadcast | Host queue + state replication | Фаза 6 |
| Хардкод arena bounds | `ArenaConfig` SO | До визуальной арены |
| C# events + MessagePipe дубль | Один канал через MessagePipe | При Audio/VFX/UI |
| `ISimEvent` / `DamageNumber*` | Удалить мёртвый код | Сейчас |

### Не делать преждевременно

- Разбивать `CombatSimulation` «на будущее».
- Fixed-point math — host-auth снимает необходимость.
- Полный Filter/Score/Override AI.

---

## Консистентность: правила проекта vs реальность

| Правило | Статус | Примечание |
|---|---|---|
| Нет синглтонов | ✅ | VContainer lifetimes only |
| Нет `Find*` в hot paths | ✅ | Только DevTools `Start` |
| Нет `UnityEngine.Random` в game logic | ❌ | `GenerateBattleSeed` |
| Нет `Physics2D` в combat | ✅ | |
| Нет `Time.deltaTime` в sim | ✅ | Только `CombatLoopService` |
| RNG через `IRngService` | ⚠️ | Инфра есть, потребления нет |
| Данные в SO, не в коде | ⚠️ | Arena, spawn — хардкод |
| Presentation не мутирует sim | ✅ | Проверено |
| Тесты | ✅ | 171 EditMode |

---

## Приоритетный план действий

### Сейчас (1–2 часа)

1. Удалить `DamageNumber.cs` + `DamageNumberSpawner.cs`.
2. Удалить или пометить deprecated `ISimEvent`.
3. Обновить `00. Обзор и карта кода`.
4. Закрыть Phase 3 step 7 (retro + glossary).

### До MP / случайных механик

5. Убрать `UnityEngine.Random` из `GenerateBattleSeed`.
6. Реализовать `gm_rng_seed`.
7. DevTools pause → `PauseCommand`/`ResumeCommand`.

### Фаза 4–6

8. `ArenaConfig` SO (doc 15).
9. Content validation pipeline.
10. `RunState` + `IEventFlow`.
11. Переписать `NetworkCommandRelay`.
12. PlayMode-тест `BattleScene` + MPPM.

---

## Итог

**Серьёзных архитектурных ошибок нет.** Фундамент заложен правильно.

**Реальные риски консистентности:**
1. **RNG/seed** — инфраструктура есть, дисциплина использования не сформирована.
2. **Сеть** — код отстаёт от host-auth модели.
3. **Документация** — часть вики устарела относительно кода.

Срочный рефакторинг ядра не нужен. Следующий шаг — закрыть Phase 3, убрать мёртвый код, зафиксировать контракты arena/seed/network перед Фазой 5/6.