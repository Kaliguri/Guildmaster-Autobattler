# Аудит кода и систем Guildmaster — Autobattler

**Автор:** GPT-5.6 Sol  
**Дата:** 09.07.2026, 23:55:02 (UTC+3)  
**Ветка:** `dev`  
**Область:** игровой код, тесты, asmdef, сцены, ScriptableObject-контент, CI/tooling и соответствующая техническая документация.

---

## Итог

Архитектура боевого ядра не требует переписывания. Основная модель выбрана удачно:

- детерминированная headless-симуляция отделена от presentation;
- `CombatSimulation` владеет состоянием боя и задаёт фиксированный порядок систем;
- системы получают зависимости явно;
- мутации эффектов проходят через `ICombatContext`;
- сборки в основном направлены правильно (`Core → Data → Combat → Game/Presentation`);
- боевое ядро покрыто большим набором содержательных EditMode-тестов.

Проблема находится не в фундаменте, а на стыках:

1. **Текущий build boot-flow не собран:** первой запускается пустая `CoreScene`, тогда как root scope и камера находятся в `BattleScene`.
2. **EditMode suite сейчас красный:** 171/172 теста прошли, один стабильно падает из-за устаревшего пути `Assets/Tests/...`.
3. **Runtime lifecycle не замкнут:** завершение боя публикуется, но никто не вызывает выгрузку battle scope; подписка `CombatPresenter` зависит от порядка `OnEnable` и DI.
4. **Есть настоящая потеря консистентности данных:** параметры Kite сериализуются, но движение их не читает; `armorK` хранится в двух местах; у Копейщика противоречивый resource type.
5. **Заготовки мультиплеера пока противоречат выбранной host-authoritative модели.** Это ещё не текущая поломка, поскольку NGO path не подключён к сценам, но его нельзя активировать как есть.
6. **Есть локальные костыли и техдолг**, однако большая часть уже изолирована или честно отмечена в коде. Их следует убирать постепенно, а не делать общий рефакторинг.

Главная рекомендация: **не менять устройство `CombatSimulation` и не вводить новую “универсальную архитектуру”**. Сначала восстановить рабочий вертикальный runtime-путь и зафиксировать его интеграционными тестами, затем закрыть конкретные расхождения данных и только потом готовить сетевой шов.

---

## Что было проверено

- `Assets/_Project/Scripts` — Core, Data, Combat, Game, Presentation, Net, DevTools.
- `Assets/_Project/Tests` — EditMode и PlayMode.
- `Assets/_Project/Scenes` и `ProjectSettings/EditorBuildSettings.asset`.
- ScriptableObject-данные реликвий.
- asmdef-граф и локальный test runner.
- Техническая wiki по фазам 1–3, AI, арене, сборкам и save/session flow.
- Фактический запуск тестов через Unity 6000.4.8f1:
  - **EditMode:** 172 total, 171 passed, 1 failed.
  - **PlayMode:** 2 total, 2 passed.

Third-party код плагинов глубоко не аудировался. Профилирование памяти/CPU и сетевой MPPM-прогон не выполнялись.

---

## P0 — исправить до следующей содержательной фазы

### P0.1. Build запускается с пустой CoreScene

**Доказательства**

- `ProjectSettings/EditorBuildSettings.asset:7-13` — первой сценой стоит `Assets/_Project/Scenes/CoreScene.unity`.
- `Assets/_Project/Scenes/CoreScene.unity:122-125` — `m_Roots: []`.
- `Assets/_Project/Scenes/BattleScene.unity:122-154` — `RootLifetimeScope` находится в BattleScene.
- `Assets/_Project/Scenes/BattleScene.unity:425-459` — там же находится дочерний `CombatLifetimeScope`.
- `GameBootstrap` должен находиться в CoreScene (`Assets/_Project/Scripts/Game/GameBootstrap.cs:8-24`), но в сцене его нет.

**Последствие**

Стандартный запуск билда открывает пустую сцену и не может вызвать `GameFlow.BootAsync()`. Прямой запуск BattleScene работает как dev-harness и маскирует отсутствие настоящего boot-flow. Если позже просто наполнить CoreScene, но оставить root в BattleScene, появятся два session scope и потенциально две камеры.

**Решение**

- Перенести persistent `RootLifetimeScope`, `GameBootstrap` и основную камеру в CoreScene.
- Удалить root и persistent-камеру из BattleScene.
- Оставить в BattleScene только `CombatLifetimeScope` как дочерний scope.
- Зафиксировать один поддерживаемый путь: `CoreScene → additive BattleScene → unload BattleScene`.

**Защитные тесты**

- `BuildEntryScene_ContainsRootLifetimeScopeAndBootstrap`
- `BattleScene_ContainsCombatScopeButNoRootScope`
- `Boot_LoadsBattleSceneAdditively`

---

### P0.2. EditMode suite уже красный

**Доказательства**

Запуск 09.07.2026:

- 172 теста;
- 171 прошло;
- упал `SerializeReference_CombatComponent_SurvivesReloadInDataAsset`;
- ошибка: `Creating asset at path Assets/Tests/EditMode/__spike_effect.asset failed`.

Источник: `Assets/_Project/Tests/EditMode/Combat/SerializeReferenceSpikeTests.cs:20,42`.

Тесты фактически живут в `Assets/_Project/Tests`, а константа всё ещё указывает на отсутствующую папку `Assets/Tests/EditMode`.

**Последствие**

CI gate для code changes не является зелёным. Это не flaky-тест и не проблема SerializeReference — тест не доходит до проверяемого контракта.

**Решение**

Использовать существующий test path и гарантировать cleanup через `AssetDatabase.DeleteAsset`. Не создавать структуру `Assets/Tests` только ради сохранения устаревшего пути.

**Защитный тест**

- Текущий `SerializeReference_CombatComponent_SurvivesReloadInDataAsset` после исправления пути уже является нужной защитой.

---

## P1 — текущие runtime и data consistency проблемы

### P1.1. Завершение боя не замыкает game flow

**Доказательства**

- `CombatSimulation` вызывает `OnBattleEnded`.
- `CombatPresenter.cs:262-266` только публикует `BattleEndedEvent`.
- `GameFlow.cs:28-32` содержит правильный `OnBattleEndedAsync`, который выгружает BattleScene.
- В проекте нет ни одного `ISubscriber<BattleEndedEvent>` и нет другого вызова `OnBattleEndedAsync`.

**Последствие**

Симуляция останавливается, но battle scene и combat scope не выгружаются. При дальнейшем развитии это даст висящие presentation-объекты, незавершённый teardown и проблемы повторного старта боя.

**Решение**

Добавить scoped/session bridge в `Game`: подписка на `BattleEndedEvent` → `GameFlow.OnBattleEndedAsync`. Не помещать macro-flow внутрь `CombatSimulation` или `Presentation`.

**Защитные тесты**

- `BattleEndedEvent_UnloadsBattleScene`
- `BattleUnload_DisposesCombatScopeOnce`

---

### P1.2. Подписка CombatPresenter зависит от порядка Unity lifecycle и DI

**Доказательства**

`CombatPresenter.cs:59-72` получает симуляцию в `[Inject] Construct`, но подписывается только в `OnEnable` (`74-87`). Если `OnEnable` вызван до инъекции, метод выходит по `_simulation == null` и повторной подписки не происходит.

**Последствие**

В зависимости от порядка активации объектов presenter может не получать spawn/damage/end events. Это особенно опасно после перехода к additive scene loading.

**Решение**

Сделать подписку явной после injection (`Construct`, `IInitializable` или VContainer entry point), хранить состояние подписки и симметрично отписываться при dispose/destroy. Не полагаться на повторный `OnEnable`.

**Защитные тесты**

- `CombatPresenter_ReceivesSpawnEventAfterInjection`
- `CombatPresenter_Dispose_UnsubscribesAllHandlers`

---

### P1.3. Параметры Kite существуют в данных, но игнорируются системой движения

**Доказательства**

- `AIProfile.cs:108-118` задаёт `Kite.FleeDist` и `Kite.FallbackDist`.
- `ProfileBrain` использует только `Kite.Enabled`.
- `MovementSystem.cs:76-94` вычисляет полосу как `[AttackRange × 0.6, AttackRange]`.

**Последствие**

Inspector/GDD обещают настраиваемое поведение, но изменение полей не влияет на игру. Это настоящий consistency bug: данные выглядят рабочими, хотя являются мёртвыми.

**Решение**

Передавать выбранные параметры позиционирования в runtime intent/state. `MovementSystem` должен исполнять готовые расстояния, а не читать SO и не придумывать собственные коэффициенты. Если дизайн окончательно выбирает зависимость от AttackRange, удалить `FleeDist/FallbackDist` из данных и wiki.

**Защитные тесты**

- `Kite_UsesProfileFleeDistance`
- `Kite_StopsAtProfileFallbackDistance`

---

### P1.4. Два источника истины для armorK

**Доказательства**

- `CombatLifetimeScope.cs:22-23,62-63` хранит отдельное serialized `_armorK`.
- `StatsConfig` также содержит armor constant.
- `StatsConfig` передаётся в `RuntimeUnitFactory`, но `CombatSimulation` получает независимый float.

**Последствие**

Изменение баланса в StatsConfig может не изменить формулу фактического урона. Значения сейчас оба равны 100, поэтому ошибка скрыта.

**Решение**

Оставить один источник истины в `StatsConfig`/отдельном immutable combat config snapshot. `CombatLifetimeScope` не должен сериализовать копию одного и того же балансного параметра.

**Защитный тест**

- `CombatScope_UsesArmorConstantFromStatsConfig`

---

### P1.5. Неконсистентные данные Iron Spearman

**Доказательства**

`IronSpearman.asset`:

- `_resourceType: 0` (`None`) — строка 21;
- `_resourceOnHit: 5` — строка 25;
- MaxResource = 30 — строки 45-47;
- способность стоит 30 ресурса — строки 52-57.

**Последствие**

Сейчас `ResourceType` почти не используется runtime-кодом, поэтому способность может работать. Но UI, save DTO и будущие правила ресурсов получат противоречивый контракт.

**Решение**

Исправить тип ресурса и добавить общую content validation:

- cost > 0 требует `ResourceType != None`;
- resource gain > 0 требует `MaxResource > 0`;
- ability/resource IDs не пустые;
- значения процентов и интервалов находятся в допустимом диапазоне.

---

### P1.6. Локальный test runner использует другую Unity

**Доказательства**

- `scripts/run-tests.ps1:10` — `6000.0.23f1`.
- `ProjectSettings/ProjectVersion.txt:1` — `6000.4.8f1`.
- CI также использует `6000.4.8f1`.

**Последствие**

Скрипт либо не запускается, либо даёт результат на другой версии редактора.

**Решение**

Читать версию из `ProjectVersion.txt` и разрешать override параметром/env var. Не хранить вторую вручную обновляемую версию.

---

## P2 — детерминизм и будущий multiplayer seam

Эти пункты важны, но **не являются текущими production blocker**, потому что сетевой runtime ещё не подключён к сценам. Их нужно закрыть до первого реального NGO/MPPM vertical slice.

### P2.1. Battle seed создаётся вне IRngService

**Доказательства**

`CombatLifetimeScope.cs:83-89` использует одновременно `DateTime.UtcNow.Ticks` и `UnityEngine.Random.Range`.

**Почему это плохо**

- прямое нарушение правила проекта «игровой random только через `IRngService`»;
- бой нельзя воспроизвести по run seed;
- seed нельзя надёжно передать клиентам/реплею;
- root RNG зарегистрирован, но не участвует в derivation battle seed.

**Решение**

Зафиксировать контракт запуска боя:

`RunSeed + BattleIndex + Attempt → BattleSeed`.

В multiplayer seed назначает host и включает в start-battle payload. В save/replay сохраняется исходный run seed и индекс боя.

---

### P2.2. CombatLoopService не проверяет authority

**Доказательства**

Комментарий `CombatLoopService.cs:14` говорит «тикает только хост», но `StartAsync` всегда вызывает `_simulation.Tick`.

**Решение**

Ввести узкий `ICombatTickAuthority`/session role seam. Host тикает симуляцию; client применяет реплицированный view state. Не внедрять `NetworkManager.Singleton` прямо в Combat.

---

### P2.3. NetworkCommandRelay реализует незавершённый lockstep

**Доказательства**

`NetworkCommandRelay.cs:38-69` принимает intent на server, затем через `ClientRpc` ставит команду в локальную симуляцию каждого пира. Сам комментарий класса признаёт расхождение с host-authoritative моделью.

**Решение**

Оставить только:

`client intent → server validation → host command queue → host simulation → replicated result/snapshot`.

Не развивать checksum/broadcast-command путь как вторую сетевую архитектуру.

---

### P2.4. Команды одного тика не имеют полного порядка

**Доказательства**

- `ISimCommand` содержит только `TargetTick`.
- `CombatSimulation.EnqueueCommand` сортирует только по `TargetTick`.
- равные команды сохраняют локальный порядок доставки.

**Последствие**

При нескольких игроках результат будет зависеть от порядка получения RPC.

**Решение**

Фиксированный ключ `(TargetTick, PlayerId, Sequence)` или host-assigned monotonic sequence. Политика должна быть частью сетевого/реплейного контракта, а не деталью списка.

---

### P2.5. Float-состояние используется в cooldown и checksum

**Доказательства**

- `AbilityRuntime.CooldownRemaining` — float seconds.
- `AbilitySystem` уменьшает его на `dt`.
- auto-attack и periodic effects уже используют integer ticks.
- checksum квантует позиции/HP через float multiplication и cast.

**Риск**

Внутри одного Windows host это приемлемо. Для lockstep/cross-platform checksum это хрупкая граница и несогласованный подход.

**Решение**

Перевести gameplay cooldown на ticks. Не обещать cross-platform lockstep по float checksum; при host-authoritative модели checksum оставить диагностикой хоста либо считать по каноническим integer snapshot fields.

---

## P3 — локальный техдолг и костыли

### P3.1. Event queue молча теряет хвост

`CombatSimulation.cs:409-433` после 512 событий очищает оставшуюся очередь без лога и явного результата.

Кап против бесконечного reactive ping-pong нужен, но silent clear скрывает ошибку контента и делает итог боя зависимым от safety limit.

Рекомендация:

- выдавать диагностическое событие/ошибку;
- покрыть boundary-тестом;
- явно выбрать политику: abort battle, suppress offending chain или ограничивать конкретный trigger.

---

### P3.2. Лишние аллокации в боевых путях

Подтверждено:

- `Stats.RebuildCache` создаёт три массива при каждой invalidation (`Stats.cs:60-65`);
- `MarkTransferComponent` создаёт новый `List<RuntimeUnit>` при смерти носителя (`MarkTransferComponent.cs:33-35`);
- `CombatPresenter` создаёт строки и coroutine/`WaitForSeconds` для split damage numbers;
- `UnitView` создаётся через `Instantiate`, хотя text уже использует pool.

Это не основание для немедленной оптимизации вслепую. Сначала профилировать типичный бой. Дешёвые исправления — переиспользуемые scratch arrays/buffers и отказ от coroutine на каждую цифру.

---

### P3.3. Два пути lifesteal

`CombatSimulation.DealDamage` применяет stat-based Lifesteal. `LifestealComponent` отдельно лечит по событию и прямо помечен как избыточный.

Риск двойного лечения уже задокументирован. Правильный путь — мигрировать оставшийся тест на `StatModifierComponent(+Lifesteal)` и удалить reactive-компонент, не поддерживая два канона.

---

### P3.4. AbilitySystem всё ещё частично принимает AI-решения

Система исполняет условия каста, но затем выбирает первую готовую способность по порядку списка. Для текущих common-реликвий с одной активкой это работает. При нескольких слотах порядок массива станет скрытым приоритетом AI.

Перед реликвиями с 2–3 активками нужен явный `CastIntent`/priority decision. Не нужно заранее строить сложный behavior tree.

---

### P3.5. Нет автоматической валидации ScriptableObject-контента

В проекте не найдено `OnValidate` или отдельного content validation pipeline. При большом количестве polymorphic effect data silent no-op станет дороже исправлять.

Рекомендация — editor-only validator и один EditMode suite, который сканирует production SO:

- уникальные и непустые ID;
- обязательные ссылки;
- корректные диапазоны;
- resource/cost consistency;
- Kite `FallbackDist > FleeDist`;
- animation hit frame внутри массива;
- отсутствие несовместимых компонентов, включая двойной lifesteal.

---

### P3.6. Presentation содержит временные параллельные решения

Найдены:

- активный `FloatingText` pool;
- отдельные `DamageNumber`/`DamageNumberSpawner`, фактически не являющиеся каноническим путём;
- runtime-created `CombatStatusOverlay`;
- MessagePipe publishers без consumer-слоя Audio/VFX/UI;
- `IAudioService` пока stub.

Это допустимо для lite harness, но следует явно пометить dev-only код и удалить дубли перед production presentation. Не стоит сейчас переписывать presentation целиком.

---

### P3.7. Незавершённые/мёртвые швы

- `ISimEvent` не имеет реализаций.
- `BrainDirty` почти не взводится; смерть текущей цели обработана отдельной проверкой.
- пустые asmdef `Guild`, `UI`, `MiniGames` уже входят в граф зависимостей.
- `SimSyncProbe` запаркован, но продолжает компилироваться.

Рекомендация: не сохранять абстракции «на будущее» только ради схемы. Либо дать шву ближайшего потребителя и тест, либо удалить/исключить из runtime assembly до нужной фазы.

---

## Документация и фактическая реализация

Wiki в целом полезна и часто объясняет причины решений, но часть страниц перестала быть надёжным источником текущего состояния:

- старые документы всё ещё называют удалённый `TargetingSystem`;
- несколько описаний tick order не включают `BrainSystem`/`DisplacementSystem`;
- путь тестов указан как `Assets/Tests`, фактически это `Assets/_Project/Tests`;
- некоторые комментарии называют код «скелетом Фазы 1», хотя реализованы фазы 2–3;
- save/session документы описывают будущие DTO, которых в коде пока нет;
- arena/placement документ корректно помечен как согласованный дизайн без реализации.

Не следует копировать полный список методов из кода в wiki. Достаточно обновить:

1. актуальный tick order;
2. карту asmdef;
3. статусы фаз/готовности;
4. принятые контракты seed/network/save;
5. известные отклонения lite harness от production flow.

---

## Арена и расстановка

Отсутствие arena bounds сейчас не является неожиданной архитектурной ошибкой: документ `15. Арена и расстановка (дизайн).md` прямо фиксирует, что реализация не начата.

Выбранное направление хорошее:

- geometry snapshot является данными симуляции;
- нет зависимости от `Physics2D`;
- authoring через prefab/gizmos;
- `MovementSystem` и `DisplacementSystem` получают одинаковые bounds;
- deployment validation живёт не в UI.

Менять этот дизайн не нужно. Важно не передавать в Combat `MonoBehaviour ArenaLayoutAuthoring`; на старте боя следует построить immutable `ArenaLayoutData`.

---

## Что сделано хорошо

### Детерминированное ядро

- фиксированный tick;
- нет `Time.deltaTime` внутри симуляции;
- нет `Physics2D`/`Rigidbody2D` в Combat;
- порядок основных циклов задаётся индексами списков;
- tie-break по ID используется в AI/позиционировании;
- periodic effects и windup используют integer ticks;
- presentation не вызывает `Tick` и не мутирует combat state.

### Границы модулей

- Combat не зависит от VContainer;
- Game является composition root;
- Data не зависит от Combat implementations;
- polymorphic SerializeReference seam специально тестируется;
- FMOD не протекает в gameplay code.

### Тестовая база

Даже с одним инфраструктурным падением тестовый набор сильный: damage pipeline, effects, projectiles, spatial hash, windup, AI cadence, determinism и вертикальные срезы реликвий. Это уже исполняемая спецификация, а не набор smoke-тестов.

### Осознанный техдолг

В коде честно отмечены lockstep-заготовка, parked sync probe, избыточный lifesteal и временный harness. Это снижает риск случайно принять их за канон.

---

## Рекомендуемый порядок работ

### Этап 1. Восстановить зелёный baseline

1. Исправить path SerializeReference test.
2. Исправить локальный Unity runner.
3. Добиться 172/172 EditMode и 2/2 PlayMode.

### Этап 2. Собрать настоящий runtime vertical slice

1. CoreScene как session root.
2. Additive BattleScene как child scope.
3. Надёжная инициализация CombatPresenter.
4. BattleEnded subscriber и teardown.
5. Scene-based PlayMode test, который проходит `boot → battle → result → unload`.

### Этап 3. Закрыть data consistency

1. Один `armorK`.
2. Kite data действительно управляет движением.
3. Исправить resource type Копейщика.
4. Добавить content validator.

### Этап 4. Укрепить детерминированные контракты

1. Battle seed derivation.
2. Cooldown ticks.
3. Полный порядок команд.
4. Явная политика event overflow.

### Этап 5. Только затем подключать multiplayer

1. Host tick authority.
2. Intent validation.
3. State/snapshot replication.
4. MPPM integration tests.

---

## Архитектурный вердикт

**Сохранять:**

- текущий слой Combat и его headless nature;
- `ICombatContext` как mutation seam;
- scoped VContainer composition;
- SO definitions → runtime state factory;
- MessagePipe только на границе presentation/game;
- design-first arena snapshot.

**Исправить:**

- boot/scene composition;
- lifecycle и teardown;
- источники истины конфигов;
- реально неиспользуемые serialized параметры;
- test/tooling drift;
- сетевой контракт до активации NGO.

**Не делать:**

- не заменять `CombatSimulation` ECS только из-за локальных аллокаций;
- не добавлять интерфейс каждому классу;
- не поддерживать одновременно lockstep и host-authoritative пути;
- не строить сложный AI framework до появления нескольких активок и доказанной необходимости;
- не смешивать arena authoring MonoBehaviour с runtime simulation data.

Проект находится в хорошем состоянии на уровне боевого фундамента, но сейчас имеет **незавершённый переход от headless/dev harness к настоящему игровому runtime**. Именно этот переход, а не новый крупный рефакторинг, должен быть следующим архитектурным приоритетом.
