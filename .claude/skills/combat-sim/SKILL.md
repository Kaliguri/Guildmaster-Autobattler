---
name: combat-sim
description: >-
  Рабочий контур боевой симуляции (combat sim) Guildmaster — детерминированное
  ядро боя на 30 Гц, система эффектов (producer→consumer, теги, стакинг),
  displacement/separation, авто-атаки и способности, юнит-POCO, а также КОНТРАКТ
  развязки sim→presentation. Используй ВСЕГДА, когда задача касается боя:
  симуляция, тик/Tick, детерминизм, CombatSimulation, ICombatContext, эффект/
  RuntimeEffect/EffectSystem, реактивы, CombatEvent, способность/AutoAttack,
  displacement/knockback/separation, Brain/AI боя, RuntimeUnit, урон/щит/хил в
  бою, teamId/исход боя, или когда правишь что-либо под
  Assets/_Project/Scripts/Combat, боевые сервисы в Game/Services
  (CombatLoopService, CombatFeelDirector, TimeScaleService) и боевой мост
  Presentation (CombatPresenter, CombatEvents). Срабатывай, даже если слова
  «симуляция» нет, но по сути правится боевая логика или её развязка.
  НЕ применять к: балансу/id/loc определений (EffectData, UnitData, VesselData —
  это скилл data-authoring; здесь только ПОВЕДЕНИЕ), боевому uGUI-HUD
  (Image.Filled и т.п.), чистому визуал-полишу/арту (FloatingText, PixelBurst,
  DeathShatter — их правит Макс).
---

# Combat Sim — рабочий контур Guildmaster

Этот скилл — процедура, а не справка. Он превращает разрозненные инварианты боя в
чеклист, который прогоняется на КАЖДОЙ боевой задаче. Цель — чтобы симуляция
оставалась детерминированной, headless-тестируемой и развязанной от презентации,
а новый боевой контент ложился в готовые контракты, а не рядом с ними.

## Прежде всего: карта боя

Ядро уже построено и покрыто ~29 EditMode-тестами. Ничего не изобретай — читай,
продолжай, встраивайся в существующие швы.

| Что | Где |
|---|---|
| Сердце симуляции (`Tick`, порядок систем, `ComputeChecksum`) | `Assets/_Project/Scripts/Combat/CombatSimulation.cs` |
| Константы шага (`TickRate=30`, `TickDelta=1/30`, `AiTickRate=10`) | `Assets/_Project/Scripts/Core/Simulation/SimConstants.cs` |
| Реалтайм-драйвер (accumulator, ЕДИНСТВЕННЫЙ `Time.deltaTime`) | `Assets/_Project/Scripts/Game/Services/CombatLoopService.cs` |
| Шов мутации мира | `Assets/_Project/Scripts/Combat/ICombatContext.cs` |
| Юнит sim-модель (POCO) | `Assets/_Project/Scripts/Combat/Units/RuntimeUnit.cs` |
| Фабрика/загрузка юнитов | `Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs`, `EncounterLoader.cs` |
| Статы | `Assets/_Project/Scripts/Combat/Stats/Stats.cs` |
| Система эффектов | `Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs` |
| Экземпляр эффекта (состояние) | `Assets/_Project/Scripts/Combat/Effects/RuntimeEffect.cs` |
| Контракты компонентов эффектов | `Assets/_Project/Scripts/Combat/Effects/IRuntimeEffectComponent.cs` |
| Компоненты-поведения (~20) | `Assets/_Project/Scripts/Combat/Effects/Components/*.cs` |
| Внутренняя шина событий (producer→consumer) | `Assets/_Project/Scripts/Combat/Effects/CombatEvent.cs` |
| Displacement / Separation / Movement | `Assets/_Project/Scripts/Combat/Systems/*.cs` |
| Способности / авто-атаки | `Assets/_Project/Scripts/Combat/Abilities/*.cs`, `Systems/AutoAttackSystem.cs`, `Systems/AttackTiming.cs` |
| Исход боя / стороны (teamId) | `Assets/_Project/Scripts/Combat/BattleOutcome.cs` |
| Мост sim→presentation | `Assets/_Project/Scripts/Presentation/CombatPresenter.cs` |
| MessagePipe-события боя | `Assets/_Project/Scripts/Presentation/Events/CombatEvents.cs` |
| Режиссёр «значимости» (global-feel) | `Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs` |
| Единственный писатель `Time.timeScale` | `Assets/_Project/Scripts/Game/Services/TimeScaleService.cs` |
| Combat-тесты (~29, slice-паттерн) | `Assets/_Project/Tests/EditMode/Combat/*.cs` |

**Слои (asmdef) — зависимость строго вниз:** `Core ← Data ← Combat`; `Presentation`
ссылается на `Combat` и читает его; `Game` сшивает всё через DI (VContainer) и
MessagePipe. `Guildmaster.Combat` НЕ тянет Presentation, MessagePipe, VContainer,
FMOD, движковые presentation-либы. Это и есть headless-ядро.

## Четыре правила, нарушение которых = переделка (HARD)

Каждое закрывает конкретный способ, которым бой незаметно загнивает. Пойми «почему» —
тогда не придётся заучивать «нельзя».

1. **Бой headless: `Guildmaster.Combat` не зависит от презентации и движка.**
   Combat-код видит только `Core` и `Data`. Никаких `MonoBehaviour`, `GameObject`,
   `Time.*`, VContainer, MessagePipe, FMOD внутри `Combat`. Единственный `Time.deltaTime`
   во всём бою — в `CombatLoopService` (реалтайм-драйвер в слое `Game`).
   *Почему:* как только sim дёргает движок, он перестаёт быть тестируемым без сцены и
   воспроизводимым — а на этом стоят и ~29 EditMode-тестов, и кооп-детерминизм.
   *Граница:* презентация СМОТРИТ на sim (подписка, чтение) — это нормально; sim на
   презентацию не смотрит НИКОГДА.

2. **Детерминизм тика.** Внутри `Combat` запрещены источники недетерминизма:
   `UnityEngine.Random`, `System.Random`-своя, `Time.*`, `DateTime.Now`,
   недетерминированный порядок (нестабильная сортировка, обход `HashSet`/`Dictionary`
   как источник порядка). RNG — только `ICombatContext.Rng`; шаг времени — только
   `SimConstants.TickDelta`; порядок систем в `Tick` — фиксирован и менять его нельзя
   без явного решения.
   *Почему:* два клиента (host-authoritative кооп) обязаны прогнать одинаковый тик
   одинаково; `ComputeChecksum()` ловит рассинхрон, но только если источник детерминирован.
   *Инструмент:* правишь sim-систему — прогони checksum/replay-проверку (рекомендуется,
   см. `references/simulation-and-determinism.md`).

3. **Мир мутируется только через шов.** Состояние боя меняется ТОЛЬКО внутри
   `CombatSimulation.Tick` (в фиксированном порядке систем) и через `ICombatContext`
   (`DealDamage`/`Heal`/`ApplyEffect`/`Dispel`/`Displace`/`SpawnProjectile`/…). Никто
   снаружи не пишет в `RuntimeUnit` напрямую; способности и эффекты трогают мир только
   через контекст.
   *Почему:* единая точка мутации = единый порядок, единые события, единый checksum.
   Прямая запись в обход контекста — это молчаливый источник рассинхрона и багов
   «эффект сработал, а событие не поднялось».

4. **Эффект = stateless-компонент, состояние — в `RuntimeEffect`.** Новый эффект —
   это класс по одному из контрактов `IRuntimeEffectComponent` (OnApply/OnExpire,
   `IPeriodicComponent`, `IReactiveComponent`, `IPreDamageComponent`,
   `IStackableComponent`, `IScalablePotency`), БЕЗ полей-состояния (компонент шарится
   между носителями). Всё изменяемое живёт в `RuntimeEffect` (`RemainingTicks`,
   `Stacks`, `ScaledPotency[]`, …). Обязательны теги (`EffectTag`) и правило стака
   (`StackRule`).
   *Почему:* stateless-компонент безопасно переиспользуется на сотнях юнитов; поле в
   компоненте — это общий mutable-стейт и мгновенный кросс-юнит-баг.

**Плюс два сквозных инварианта (тоже HARD):**

- **Стороны — только `teamId` (int).** Никакой «команды игрока» в бою. Исход —
  «победила команда N» (`BattleOutcome`), сторон может быть >2 (шов под PvP).
  Принадлежность игрока живёт выше боя (`ILocalPlayer.Team`, `_localViewerTeam` в презентере).
- **Значения — из данных/конфигов, не хардкод.** Тюнеры боя — `SimTuning` (dev
  `gm_*`), статы — через `StatsConfig`/`Override`, feel — `CombatFeelConfig`. Магические
  числа в sim-коде = двойная работа при балансе. (Как в UI: не хардкодить, чтобы не
  переделывать.)

## Граница с data-authoring (эффект живёт на два дома)

Эффект — стык двух скиллов, режем чётко:

- **combat-sim владеет ПОВЕДЕНИЕМ:** компонент-логика (`IRuntimeEffectComponent`),
  тик, стакинг-механика, реактивы, порядок применения, `CreateRuntime` для системных
  эффектов в коде.
- **data-authoring владеет ОПРЕДЕЛЕНИЕМ:** `EffectData` SO, `id` (`domain.name`),
  баланс-цифры, `[SerializeReference]`-состав компонентов, loc-ключи.

Задача «новый эффект» обычно трогает оба: логику пишу здесь, определение/баланс — по
контуру data-authoring. На стыке — взаимная ссылка «см. другой скилл», а не спор за задачу.

## Развязка sim → presentation (шов, не визуал)

Скилл держит КОНТРАКТ развязки, но не визуальный полиш (его правит Макс):

- **sim поднимает C#-события** (`OnDamageDealt`, `OnUnitDied`, …); **`CombatPresenter`**
  — единственный, кто на них подписан со стороны презентации, спавнит views и
  **ретранслирует в MessagePipe** (`CombatEvents.cs`). Audio/UI/feel слушают MessagePipe,
  не sim.
- **global-feel только в `CombatFeelDirector`** (kill-slowmo, shake, финишер). Точечный
  per-hit фидбэк — в презентере. Больше нигде глобальным временем/тряской не рулим.
- **`Time.timeScale` пишет только `TimeScaleService`.** Он же не трогает sim-время
  (`ElapsedSeconds = currentTick * TickDelta`): slowmo/пауза меняют реальное время на
  тик, но не детерминизм.

Детали контракта и антипаттерны — `references/presentation-seam.md`.

## Как я авторю боевой код — ГИБРИД (файл + проверка через MCP)

1. **Пишу файлы напрямую** (`Write`/`Edit`) — контролирую код и его слой.
2. **После C#-правок — `read_console`** (Unity MCP): дождаться компиляции, ноль ошибок,
   только потом использовать новые типы.
3. **Перед «готово» — `run_tests`** по Combat-подмножеству (EditMode/Combat) — быстро;
   полный прогон отдаём CI.
4. **Проверяю слой:** новый код в `Combat` не тянет запрещённые зависимости (правило 1).

## Чеклист сдачи боевой задачи

Прогнать перед тем, как сказать «готово»:

- [ ] Новый код в `Combat` не зависит от Presentation/движка (headless цел)
- [ ] Ноль источников недетерминизма; RNG через `ICombatContext.Rng`, dt через `TickDelta`
- [ ] Мутация мира только через `Tick`/`ICombatContext`, не прямой записью в `RuntimeUnit`
- [ ] Новый эффект — stateless-компонент; состояние в `RuntimeEffect`; теги + `StackRule` заданы
- [ ] Стороны — `teamId`; никакой «команды игрока»; значения — из конфигов, не хардкод
- [ ] Развязка цела: sim не смотрит на presentation; `timeScale`/global-feel — в своих местах
- [ ] На каждый новый эффект/способность/систему — свой EditMode-тест (тесты под игру, не наоборот)
- [ ] `read_console` чист (компиляция); `run_tests` по Combat зелёный
- [ ] Правил sim-систему → прогнал checksum/replay-проверку

## Справочные файлы (читать по надобности)

- `references/simulation-and-determinism.md` — тик, фиксированный порядок систем,
  `ICombatContext`, `SimConstants`, `CombatLoopService`, детерминизм, checksum/replay.
  Читать перед правкой любой sim-системы.
- `references/effects-and-events.md` — контракт эффектов, компоненты, `RuntimeEffect`,
  теги/стакинг, шина `CombatEvent`, реактивы, displacement-как-эффект. Читать перед
  созданием эффекта.
- `references/presentation-seam.md` — развязка sim→presentation, MessagePipe-события,
  `CombatPresenter`, `CombatFeelDirector`, `TimeScaleService`, границы визуала.
