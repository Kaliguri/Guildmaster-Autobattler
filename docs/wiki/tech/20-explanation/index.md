---
title: "Explanation - Code Map"
order: 0
status: archive
updated: 2026-07-26
---

> [!warning] Архив: описание кода на дату сверки, а не правда о коде
> С 2026-07-30 правда о коде живёт **в коде** (`<summary>`/`<remarks>`) и в тестах: у каждого факта
> один владелец, выбранный по тому, что ломается при расхождении. Страница не пополняется и с кодом
> не сверяется — имена классов, сигнатуры и структура могли измениться. Читать как замысел и
> историю; актуальное искать в коде. Почему так —
> [[tech/00-meta/journal/2026-07-30-code-owns-truth-journal-owns-why|Journal - Code Owns Truth, Journal Owns Why]].

**Было при заморозке:** актуализирован 2026-07-16 (тик-ордер, имена классов); отдельные разделы — needs_review

---

> Это **гайд по фактическому коду** проекта (а не план, как доки «10/12»). Цель — чтобы ты понимал каждое архитектурное решение: что за что отвечает, почему выбран один паттерн и не выбран другой. Читается от простого к глубокому.
>
> Каждый документ раздела начинается с блока **«Простыми словами»**, потом идёт детально. Связан с [[tech/40-planning/phase-1-combat-core|Planning - Phase 1: Combat Core]], [[tech/40-planning/phase-2-effects|Planning - Phase 2: Effects & Abilities]], [[tech/00-meta/journal/2026-07-30-library-picks-and-the-alternatives-we-turned-down|Journal - Library Picks And The Alternatives We Turned Down]].

## Документы раздела

| # | Документ | О чём |
|---|---|---|
| 00 | **Обзор и карта кода** (этот файл) | Слои, поток данных, где что лежит, порядок чтения |
| 01 | [[tech/00-meta/journal/2026-07-30-the-bus-stops-at-the-combat-assembly|Journal - The Bus Stops At The Combat Assembly]] | VContainer, скоупы, инъекция, MessagePipe vs C#-события |
| 02 | [[tech/00-meta/journal/2026-07-30-why-the-tick-order-is-this-order|Journal - Why The Tick Order Is This Order]] | Тик 30 Гц, аккумулятор, очередь команд, RNG, checksum, пауза |
| 03 | [[tech/00-meta/journal/2026-07-30-stats-pipeline-neither-reorders-nor-clamps|Journal - The Stats Pipeline Neither Reorders Nor Clamps]] | SO-контент, `StatType`, слоистые модификаторы, пайплайн урона |
| 04 | [[tech/00-meta/journal/2026-07-30-effects-are-ordered-by-id-and-attributed-by-weight|Journal - Effects Are Ordered By Id, Attributed By Weight]] | `EffectData` + компоненты, стаки, диспел, контроль, реактивность |
| 05 | слой презентации (код `Assets/_Project/Scripts/Presentation/`) | Раздел сим/визуал, `UnitView`, сглаживание 30→60 |
| 06 | [[tech/00-meta/journal/2026-06-19-host-authoritative-not-lockstep|Journal - Host-Authoritative, Not Lockstep]] | Host-authoritative vs lockstep, что запарковано, главная таска MP |
| 07 | [[tech/00-meta/tech-changelog|Meta - Tech Changelog & Decisions]] | Отложенное, исправленное, открытые вопросы |

---

## Простыми словами

Игра внутри разделена на **две независимые половины**:

1. **«Мозг» (симуляция)** — чистый C#-код, который считает бой по тикам: кто куда идёт, кто кого бьёт, сколько HP осталось. Он **ничего не знает** про картинку, звук и Unity-сцену. Его можно запустить «вслепую» (headless) в тесте и проверить, что при одном и том же входе он всегда даёт один и тот же результат.

2. **«Тело» (презентация)** — Unity-объекты (`MonoBehaviour`, спрайты, HP-бары, цифры урона), которые **читают** состояние мозга и рисуют его. Тело никогда не меняет бой — только показывает.

Между ними — тонкий провод из событий. Мозг говорит «юнит №5 получил 30 урона», тело рисует цифру «30» и трясёт камеру.

Почему так, а не «по-юнитёвому» (каждый юнит — `MonoBehaviour`, двигает себя в `Update`)? Потому что игра **кооперативная по сети** и **с паузой**. Чтобы у двух игроков бой шёл одинаково (или чтобы пауза/реплей работали), нужна предсказуемая, отделённая от рендера симуляция. «По-юнитёвый» подход здесь упирается в стену синхронизации. Подробно — в [[tech/00-meta/journal/2026-07-30-why-the-tick-order-is-this-order|Journal - Why The Tick Order Is This Order]] и [[tech/00-meta/journal/2026-06-19-host-authoritative-not-lockstep|Journal - Host-Authoritative, Not Lockstep]].

---

## Слои и сборки (Assembly Definitions)

Код разбит на сборки (`.asmdef`), и зависимости идут **строго вниз**. Это не косметика: компилятор физически запрещает нижнему слою знать про верхний, поэтому «мозг» не может случайно потянуть за собой Unity-UI.

```
Core                      — фундамент: RNG, константы симуляции, интерфейсы
 ├─ Data        → Core           — SO-данные, статы (ScriptableObject, структуры)
 ├─ Combat      → Core, Data     — «мозг»: симуляция, системы, эффекты, урон
 ├─ Net         → Core, Combat   — сетевой слой (NGO)
 ├─ Presentation→ Core,Data,Combat — «тело»: визуал, читает сим
 └─ Game        → знает всех     — composition root: DI-скоупы, bootstrap, флоу
```

`Game` — единственный, кто видит все слои сразу: он их **собирает** (composition root). Всё остальное зависит только вниз. Карта сборок целиком — [[tech/10-reference/assemblies|Reference - Assemblies]].

> **Важный приём — кросс-сборочный шов эффектов.** `Data` лежит ниже `Combat`, но `EffectData` (в `Data`) должна хранить поведение эффектов, которое оперирует боевым состоянием (в `Combat`). Прямая ссылка `Data → Combat` запрещена графом. Решение: в `Data` объявлен пустой маркер-интерфейс `IEffectComponent`, а реальные хуки (`OnApply/OnTick/OnExpire`) — в `Combat` через производный `IRuntimeEffectComponent : IEffectComponent`. Поле `[SerializeReference] IEffectComponent[]` в `EffectData` хранит Combat-типы, не создавая зависимости вверх. Детали — [[tech/00-meta/journal/2026-07-30-effects-are-ordered-by-id-and-attributed-by-weight|Journal - Effects Are Ordered By Id, Attributed By Weight]].

---

## Поток данных: от запуска до удара

```
[Bootstrap]  GameBootstrap (MonoBehaviour в CoreScene)
     │  Start() → SceneLoader: мир, затем боевые системы → GameFlow.RunGameAsync()
     ▼
[Сцены]      WorldScene и CombatSystemsScene грузятся аддитивно к CoreScene — ОДИН раз
     │        за сессию и не выгружаются ([[tech/10-reference/scenes|Scenes]])
     ▼
[DI]         RootLifetimeScope (сессия) ─► WorldLifetimeScope (мир) ─► CombatLifetimeScope
     │         RNG, Audio, SceneLoader,                  battle-RNG, все системы,
     │         GameFlow, MessagePipe                     CombatSimulation, презентеры
     ▼
[Старт боя]  EncounterLoader создаёт RuntimeUnit'ы из SO → EnqueueUnitSpawn
     │
     ▼
[Пульс]      CombatLoopService (IAsyncStartable) копит Time.deltaTime и
     │         вызывает CombatSimulation.Tick() фиксированными шагами 30 Гц
     ▼
[Тик]        ApplyCommands → Brain → Ability → Movement → Displacement →
     │         Separation → SpatialHash → AutoAttack → Projectiles → Regen →
     │         Effects → DrainEvents → Death → CheckOutcome
     ▼
[События]    CombatSimulation шлёт C#-события (OnDamageDealt, OnUnitDied…)
     ▼
[Презентация] CombatPresenter ловит их, двигает UnitView, ретранслирует в
               MessagePipe → Audio/VFX/UI подписываются независимо
```

Каждую стрелку этого потока подробно разбирают документы 01–05.

---

## Карта классов: кто за что отвечает

### Core (фундамент)
| Класс | Ответственность |
|---|---|
| `IRngService` / `XorShiftRng` | Детерминированный генератор случайностей боя. Любой рандом — только через него |
| `SimConstants` | Частоты сим/AI, `TickDelta`, анти-лавина-кап. Единый источник правды по таймингам |
| `ISimCommand` / `ISimEvent` | Базовые контракты команд/событий симуляции |

### Data (контент)
| Класс | Ответственность |
|---|---|
| `RelicData`, `VesselData` | SO «Чемпион»/«Пилот»: стат-блоки, эффекты, способности (иммутабельный контент) |
| `EffectData`, `AbilityData` | SO определения эффекта/способности + полиморфные компоненты поведения |
| `StatsConfig` | Глобальные дефолты статов и тюнинг-константы (armor-K, клампы) |
| `StatType`, `StatModifier`, `ModifierOp`, `ScalableValue` | Стат-система: перечисление статов, модификаторы, скейл от статов |

### Combat (мозг)
| Класс | Ответственность |
|---|---|
| `CombatSimulation` | Сердце: тик-цикл, реализует `ICombatContext`, очередь команд, события, checksum |
| `ICombatContext` | Шов: единственная точка мутаций боя для систем и компонентов эффектов |
| `RuntimeUnit` | Рантайм-юнит на один бой (POCO, без `MonoBehaviour`) |
| `Stats` | Слоистые модификаторы + кэш итоговых значений |
| `*System` (Brain/Movement/Displacement/Separation/AutoAttack/Projectile/Death/Regen/Ability) | По одной системе на аспект тика |
| `EncounterLoader` | Data-driven сборка боя из `EncounterData`: спавн юнитов/врагов через `RuntimeUnitFactory` (в `Combat/Units`) |
| `EffectSystem` | Жизненный цикл эффектов: наложение, стаки, тик, диспел, контроль |
| `DamagePipeline` | Чистый расчёт урона (статические функции) |
| `SpatialHash` | Бесалокационные пространственные запросы |
| `RuntimeUnitFactory` | Единственная точка сборки `RuntimeUnit` из SO |
| `ICombatCommand` + `Commands/*` | Мутации боя на границе тика (Pause/Resume/SpawnUnit) |

### Game (composition root)
| Класс | Ответственность |
|---|---|
| `GameBootstrap` | Точка входа: поднимает DI, грузит обе persist-сцены, зовёт `GameFlow.RunGameAsync` |
| `RootLifetimeScope` / `WorldLifetimeScope` / `CombatLifetimeScope` | DI-скоупы сессии, мира и боевых систем |
| `GameFlow` | Макро-флоу: главное меню → забег → исход (сцен не грузит) |
| `CombatLoopService` | Реалтайм-пульс: `Time.deltaTime` → фиксированные тики |

### Presentation (тело)
| Класс | Ответственность |
|---|---|
| `CombatPresenter` | Мост сим→визуал: подписка на события, спавн вью, ретрансляция в MessagePipe |
| `UnitView`, `HealthBarView`, `FloatingText` | Отображение и интерполяция (всплывающие числа — пул `FloatingText`) |

### Net (сеть)
| Класс | Ответственность |
|---|---|
| `NetworkCommandRelay` | Реле команд клиент→хост (концепт keeper при host-authoritative) |
| `FacepunchTransportBootstrap` | Steam-транспорт под NGO |
| `_Parked/SimSyncProbe` | ⛔ Запаркован (lockstep-инструмент, см. [[tech/00-meta/journal/2026-06-19-host-authoritative-not-lockstep|Journal - Host-Authoritative, Not Lockstep]]) |

---

## В каком порядке читать

1. Этот обзор — общая карта.
2. [[tech/00-meta/journal/2026-07-30-the-bus-stops-at-the-combat-assembly|Journal - The Bus Stops At The Combat Assembly]] — как части находят друг друга (ты просил DI отдельно).
3. [[tech/00-meta/journal/2026-07-30-why-the-tick-order-is-this-order|Journal - Why The Tick Order Is This Order]] — сердце, без которого остальное не имеет смысла.
4. [[tech/00-meta/journal/2026-07-30-stats-pipeline-neither-reorders-nor-clamps|Journal - The Stats Pipeline Neither Reorders Nor Clamps]] → [[tech/00-meta/journal/2026-07-30-effects-are-ordered-by-id-and-attributed-by-weight|Journal - Effects Are Ordered By Id, Attributed By Weight]] — содержимое боя.
5. слой презентации (код `Assets/_Project/Scripts/Presentation/`) — как это видно.
6. [[tech/00-meta/journal/2026-06-19-host-authoritative-not-lockstep|Journal - Host-Authoritative, Not Lockstep]] — почему вся эта строгость, и куда идём по кооперативу.
7. [[tech/00-meta/tech-changelog|Meta - Tech Changelog & Decisions]] — что отложено и почему.
