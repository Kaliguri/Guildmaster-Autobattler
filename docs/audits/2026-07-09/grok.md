**Аудит:** архитектура и консистентность кода  
**Модель:** Cursor Grok 4.5  
**Дата:** 09.07.2026 23:52:30  
**Объём:** ~105 `.cs` в `Assets/_Project/Scripts`, 13 asmdef, сверка с вики (`5`, `1. Сборки`, `Гайд/00–07`, tech debt `07`)

---

## Вердикт

Фундамент **здоровый и в целом соответствует заявленной архитектуре**. Жёсткие красные линии проекта (нет синглтонов, нет Physics2D в симе, тик 30 Hz, DI через VContainer, Combat без VContainer/UI) в основном держатся. Критичных «разъехавшихся» систем, которые уже ломают одиночный бой, почти нет.

Главные риски сейчас — не «всё сделано неправильно», а **накопленные швы и долг после Фазы 3**: презентация просочилась в `ICombatContext`, очередь событий может молча обрезаться, сид боя недетерминирован, сеть частично в lockstep-наследии, плюс мёртвый/дублирующий код. Это чинится точечно; переписывать ядро не нужно.

**Оценка зрелости (festival slice):** ~7.5/10 по архитектуре, ~6.5/10 по гигиене швов и мёртвому коду.

---

## Что сделано правильно (не ломать)

| Область | Оценка |
|---|---|
| Разделение Combat (POCO) / Presentation (read-only) / Game (composition root) | Сильно |
| Asmdef-граф, Combat → только Core+Data | Сильно |
| Тик: accumulator + `MaxCatchUpTicksPerFrame`, `Tick(dt=TickDelta)` | Сильно |
| `ICombatContext` как единый шов мутаций сима | Сильно (кроме presentation-методов) |
| Индексные `for` по спискам юнитов, tie-break по `Id` в AI | Сильно |
| Эффекты: Data marker + Combat runtime hooks, FIFO event queue без рекурсии | Сильно |
| VContainer scopes (Root / Combat child) | Сильно |
| Документированный tech debt (`Гайд/07`) — долг осознанный, не скрытый | Сильно |
| Тестовое покрытие EditMode (по changelog: 171 зелёных) | Хорошо |

Уроки прошлого проекта (синглтоны, монолит без asmdef, `UnityEngine.Random` в логике, GlobalEventSystem) в основном учтены.

---

## Findings по серьёзности

### Critical — может менять исход боя

#### 1. `DrainEventQueue` молча дропает хвост очереди

**Где:** `Combat/CombatSimulation.cs` — после капа `MaxEventsPerDrain = 512` вызывается `_eventQueue.Clear()`.

**Почему плохо:** кап нужен против бесконечного thorns↔thorns, но **сброс остатка** делает исход боя зависимым от «успели ли уложиться в 512». Это уже не «защита от зависания», а потенциально недетерминированное/неполное применение реактивов.

**Лучше:**
- при превышении капа — **assert / fail-fast в dev** + лог с тиком;
- в релизе — либо додренировать с лимитом итераций и явным «circuit breaker» событием, либо помечать бой как corrupted;
- не `Clear()` без следа.

---

### High — архитектурный долг / будущая боль

#### 2. Презентация на шве `ICombatContext`

**Где:** `ICombatContext.ReportAreaHit` / `NotifyAttackStarted` / `NotifyAttackInterrupted`; вызовы из `AutoAttackSystem`, `AbilitySystem`.

**Почему плохо:** шов мутаций сима смешивается с fire-and-forget презентацией. Системы Combat знают про «вжух» и оверлей зон. Headless-тесты и будущий host-auth клиент тащат лишний контракт. Документация говорит «сим отделён» — код уже чуть нарушил границу ради festival VFX.

**Лучше (без большого рефактора):**
- вынести в отдельный `ICombatFeedback` / `ICombatPresentationSink` (nullable no-op в тестах), **или**
- только C#-события на `CombatSimulation` (как уже сделано для урона/смерти) — системы не зовут презентацию напрямую.

`ReportAreaHit` как событие сима ок; методы на `ICombatContext` — нет.

#### 3. Два параллельных канала наружу

**Где:** C# `event` на `CombatSimulation` + MessagePipe-структуры в `Presentation/Events`, ретрансляция в `CombatPresenter`.

**Почему терпимо сейчас:** мост один (`CombatPresenter`).  
**Риск:** Audio/VFX/UI начнут подписываться кто на что — дубли и пропуски.

**Правило на будущее:** единственный внешний bus для потребителей выше Presentation — MessagePipe; C# events — только внутренний шов Sim→Presenter.

#### 4. Сид боя: wall-clock + `UnityEngine.Random`

**Где:** `Game/CombatLifetimeScope.GenerateBattleSeed()`.

Нарушает правило «рандом только через `IRngService`» (пусть и вне тика). Блокирует воспроизводимые реплеи/дебаг. TODO про host-seed уже есть — ок для MP, но **для соло** лучше явный seed из DevTools / конфига харнесса.

`gm_rng_seed` сейчас stub (только лог) — инструмент обещан, поведения нет.

#### 5. Net: lockstep-наследие при решении host-auth

**Где:** `Net/NetworkCommandRelay` (broadcast ClientRpc «все применяют на тике»), `Net/_Parked/SimSyncProbe`.

Задокументировано — хорошо. Но живой код реле **уже врёт модели**. Перед Фазой MP не наращивать на нём фичи; переписать путь intent→host→state replicate.

#### 6. Аллокации в горячих путях эффектов

| Место | Проблема |
|---|---|
| `MarkTransferComponent` | `new List<RuntimeUnit>()` на каждый `UnitDied` |
| `Stats.RebuildCache` | 3× `new float[StatCount]` (уже в tech debt 3.2) |
| `EffectSystem.Apply` | `ScaledPotency = new float[componentCount]` |

На festival-масштабе терпимо; при росте эффектов/стат-баффов — буферы на контексте/инстансе.

---

### Medium — костыли, мёртвый код, упрощения

#### 7. Мёртвый / дублирующий код

| Артефакт | Статус |
|---|---|
| `ISimEvent` | Пустой маркер, `CombatEventData` его не реализует |
| `DamageNumberSpawner` | Orphan; цифры идут через `CombatPresenter` + pool |
| `LifestealComponent` | Избыточен под моделью «эффекты → статы» (debt 3.7) |
| `FloatingText.Spawn` static Instantate-путь | Legacy рядом с пулом |

Рекомендация: удалить/свести в одной сессии гигиены, пока связей мало.

#### 8. Грубый alpha интерполяции

`CombatPresenter`: `alpha = deltaTime / TickDelta` вместо доли аккумулятора из `CombatLoopService` (debt 3.3). Косметика, но шов loop↔presenter стоит заложить до полировки анимаций.

#### 9. Presentation читает внутренности сима напрямую

`UnitView` / overlays трогают `RuntimeUnit`, `EffectTag`, `DamageResult`. Для lite-harness ок; перед полноценным UI/MP — тонкий read-model или snapshot, иначе любой рефактор `RuntimeUnit` ломает View.

#### 10. `GlobalMessagePipe.SetProvider`

Статический escape hatch MessagePipe в `RootLifetimeScope`. Прагматично для пакета; держать в узком месте, не размножать `GlobalMessagePipe.Get...` по геймплею.

#### 11. Упрощения контента Фазы 3 (осознанные)

Уже в changelog 2.3: блок C ульты → `CurrentTarget`, кайт по `AttackRange`, dodge-заряды в компоненте, нет monk side-dash. Это не костыли архитектуры, а **временные геймдизайн-срезы** — не путать с техдолгом ядра.

#### 12. Пустые сборки-заглушки

`Guild`, `MiniGames`, `UI` — asmdef без кода. Нормально как reservation; не наполнять «на всякий случай».

#### 13. `CombatSimulation` god-object (~393 строки)

Debt 3.1 — пока читаемо. Триггер на вынос: command queue + checksum + event hub, когда появится вторая реализация контекста или MP-репликация.

---

### Low / Observation

- `FindObjectOfType` только в DevTools — приемлемо; `gm_toggle_status` лучше инжектить overlay.
- `DamageNumber` reflection на `color`/`text` — хрупкий обход TMP; заменить на явный тип, когда зависимость стабильна.
- `Core` asmdef не `noEngineReferences` — Combat/Core используют `Vector2`/`Mathf`; осознанный tradeoff под host-auth (не lockstep float).
- `IRngService` в тике почти не крутит `Next*` (в основном checksum) — инфраструктура впереди критов/проков; ок.
- Targeting O(n²) в `ProfileBrain` — осознанно на малых N; SpatialHash уже есть для AOE.

---

## Архитектурные развилки: что оформлено неидеально

### A. `ICombatContext` раздувается «удобными» методами

Сейчас туда кладут всё, что системам «нужно сказать миру». Правильнее держать контекст **только про мутации сима** (урон, хил, эффекты, снаряды, query, displace, RNG, tick). Feedback — отдельный sink или события.

Это самый важный «сделать иначе», пока методов мало.

### B. Нет явного read-model для презентации

Решение «Presenter читает `RuntimeUnit`» ускорило харнесс, но закрепляет утечку. Альтернатива на рост: `IUnitViewState` / кадр snapshot после тика. Не делать сейчас; заложить, когда появится UI Toolkit HUD или клиентская реплика.

### C. Сеть: код отстаёт от решения

Решение host-auth зафиксировано (2026-06-19), реализация реле — ещё lockstep-shaped. Это **консистентность docs↔code**, не баг соло. Перед MP — один проход «привести Net к документу», не наоборот.

### D. Сиды и воспроизводимость недооценены для соло-дебага

Даже без MP: фиксируемый seed + рабочий `gm_rng_seed` окупаются сразу (репро багов реликвий). Дешевле, чем ждать Фазу MP.

---

## Чего делать не стоит

1. **Не** вводить fixed-point / lockstep — решение host-auth верное для коопа.
2. **Не** дробить `CombatSimulation` «на всякий случай» до реальной боли.
3. **Не** строить полный DTO-слой презентации до UI/MP.
4. **Не** удалять parked Net-код молча — он якорь решения; держать в `_Parked` с комментарием.
5. **Не** плодить интерфейсы 1:1 (`IFoo`→`Foo`) — текущие швы в основном оправданы; мёртвый только `ISimEvent`.

---

## Приоритетный backlog (после festival-среза или в ближайшей гигиене)

| # | Действие | Effort | Когда |
|---|---|---|---|
| 1 | Убрать silent `Clear()` в `DrainEventQueue` → fail/log | S | Сейчас |
| 2 | Вынести presentation-методы с `ICombatContext` | S–M | До следующих VFX на шве |
| 3 | Фиксируемый battle seed + живой `gm_rng_seed` | S | Сейчас (соло-дебаг) |
| 4 | Удалить `ISimEvent`, orphan `DamageNumberSpawner`, legacy Instantate-путь | S | Гигиена |
| 5 | Shared buffer для `QueryUnitsInRadius` в `MarkTransfer` | S | При следующем касании Следопыта |
| 6 | Пробросить accumulator fraction в Presenter | S | Полировка движения |
| 7 | Примирить/удалить `LifestealComponent` | S | Первый реальный вампирик-контент |
| 8 | Переписать `NetworkCommandRelay` под host-auth | M | Старт Фазы MP |
| 9 | Буферы в `Stats.RebuildCache` | S | Когда стат-эффекты заспамят GC |

---

## Scorecard чеклиста проекта

| Правило | Статус |
|---|---|
| Нет синглтонов / service locator в геймплее | ✅ |
| Нет `Find*` в runtime (кроме DevTools) | ✅ |
| Нет `GetComponent` в Update | ✅ |
| Нет `UnityEngine.Random` в тике | ✅ (есть в bootstrap seed) |
| Нет Physics2D в симе | ✅ |
| `Time.deltaTime` только в loop driver | ✅ |
| Детерминированный обход коллекций в симе | ✅ |
| FMOD только за `IAudioService` | ✅ (stub) |
| Combat без Presentation/Game deps | ✅ asmdef; ⚠️ presentation API на контексте |
| Документ ↔ код по сети | ⚠️ решение есть, реле устарело |
| Один источник правды по API | ✅ XML-doc в коде; вики — решения |

---

## Итог одной фразой

Ядро автобатлера собрано дисциплинированно и близко к дизайну; ловить нужно не «неправильную архитектуру», а **размытие шва симуляции презентацией**, **опасный drain событий**, **недетерминированный seed** и **устаревший Net-слой** — всё это ещё дёшево исправить до роста UI/MP/контента.