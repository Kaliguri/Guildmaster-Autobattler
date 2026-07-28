---
title: "Planning - SimBench Balance Harness"
order: 135
status: archive
updated: 2026-07-28
---

> [!info] Статус: реализовано, ниже — архив ТЗ
> Одна поправка к строке статуса: меню переехало под наш единственный корень — пункты живут как
> `Alebardium/Balance/*` (`Assets/_Project/Scripts/Balance/Editor/BalanceMenu.cs`), а не `Tools/Balance/*`.
> Актуальная раскладка меню — [[tech/10-reference/editor-tools|Reference - Editor Tools]].
> Остальное тело плана — след замысла на 2026-07-17 и не переписывается.

**Статус:** Реализовано (Фазы 0–2, 2026-07-17), ветка `feat/run-loop-and-simbench`. Код — `Assets/_Project/Scripts/Balance` (+ `/Editor`), меню `Tools/Balance/*`, тесты `Guildmaster.Balance.Tests` (3/3 зелёные). Верифицировано на реальном контенте (10 архетипов + 4 гоблина): цифры дискриминирующие. «Регрессия баланса в CI» — вырезана (наблюдательность на Максе).

> **Дожатия (2026-07-17):**
> - **Разбивка урона по источнику** (§1.3) — реализована через `DamageResult.SourceKind` (эхо `DamageRequest.SourceKind`), а НЕ через смену сигнатуры `OnDamageDealt` — шов sim→presentation не тронут. Колонки Auto/Ability/DoT% в DPS-бенче и per-source в сценарии.
> - **Write-сторона петли баланса** — `ContentEditService` (`Guildmaster.Data.Editor`): безопасная правка значений контент-SO (SerializedObject+Undo, change-log). SimBench (read) + ContentEditService (write) = петля read→edit→read. Черновик общего скилла — `.claude/skills/xgaida-x-nixi-balance/DRAFT.md`.

> **Коррекция против эскиза (§1.4):** DPS-бенч мерит урон/сек до убийства эталонной цели **фикс-HP (3000)**, а не по бессмертному 1e9. Причина: бессмертный 1e9-манекен взрывал механики «% от HP цели» (FlameSwordsman давал 127M «DPS»). Фикс-HP цель корректна и для %HP-китов. Прочие бенчи — по эскизу.
>
> Ниже — исходный эскиз (историческая ценность замысла).

---

> Набор **инструментов начального баланса**: headless-стенд, который гоняет боевое ядро без презентации и выдаёт цифры (DPS/EHP/TTK/win-rate/рейтинг) в CSV/Markdown для геймдизайнера. Инструмент **не решает** баланс — он даёт доп-информацию ГД. Сочетания/синергии он принципиально не ловит (комбинаторный взрыв), финальный баланс — за Максом и плейтестами.
>
> Связано: [[tech/20-explanation/simulation|Explanation - Simulation & Tick]], [[tech/20-explanation/data-stats-damage|Explanation - Data, Stats, Damage]], [[tech/20-explanation/di-events|Explanation - DI & Events]], [[tech/40-planning/content-hub|Planning - Content Hub]] (страница «Баланс» хаба — потребитель этих же цифр).

---

## 0. Зачем и на чём стоим (verified baseline 2026-07-17)

Ядро **уже спроектировано под headless-прогон** — это ключевой факт, делающий стенд дешёвым:

- `CombatSimulation` (`Assets/_Project/Scripts/Combat/CombatSimulation.cs`) конструируется вручную (rng + armorK + `SpatialHash` + системы), тикается голым циклом `Tick(SimConstants.TickDelta)` до `Outcome != Ongoing`. Ровно так делают существующие тесты — [`CombatSimulationTests.BuildSim`](../../../../Assets/_Project/Tests/EditMode/Combat/CombatSimulationTests.cs) и `BattleIntegrationTest`.
- Ядро — POCO: **нет** зависимостей на MonoBehaviour, рендер, VFX, аудио. Презентация развязана через outward C#-events (`OnDamageDealt`, `OnHealed`, `OnUnitDied`, `OnBattleEnded`, `OnAttackEvaded`) — нет подписчиков, ничего не происходит. Это и есть **шов для метрик**: стенд подписывается на них и агрегирует.
- **Ограничение:** конструктор создаёт `ScriptableObject`-маркер (`EffectData.CreateRuntime`), поэтому стенд обязан исполняться **внутри Unity** (Test Runner / `-batchmode`), а не как автономный .NET-процесс. Для «гонять бои без презентации» это несущественно.
- **Синтетические юниты — тривиальны:** `RuntimeUnit` строится напрямую (`new Stats(null)` + `StatModifier[]`), без SO и фабрики (образец — `MakeMeleeUnit`). Манекены (бессмертная цель, фикс-DPS пушка, AoE-кластер) собираются кодом.
- **Реальный контент — через фабрику:** `RuntimeUnitFactory.Create(UnitData, VesselData, team, pos, items)` (нужны `StatsConfig`, `EffectSystem`, `ICombatContext` = сам sim). Контента хватает: 10 боевых архетипов (реликвии) + 4 гоблина + `TrainingDummy`, 8 энкаунтеров, 11 боевых пресетов.

### 0.1 Важная оговорка о детерминизме (влияет на дизайн)

Сейчас боевой пайплайн **RNG-free по исходу**: криты/разброс урона ещё не в `DamagePipeline` (зафиксировано комментарием в `CombatSimulationTests.cs:79-82`). Следствия:

- Monte-Carlo по сидам пока **вырожден**: разные сиды дают идентичный бой. Один сид = вся правда.
- Матрица дуэлей будет **детерминированной** (1/0), не вероятностной; win-rate — 0% или 100%.

**Решение:** петлю по сидам и агрегацию распределений (mean/median/p10/p90) закладываем в архитектуру сразу (дешёвый шов), но по умолчанию гоняем 1 сид и честно помечаем в отчёте «single-seed (combat RNG-free)». Когда крит/разброс войдут в пайплайн — включаем N сидов одним параметром, отчёты становятся распределениями без переписывания стенда.

---

## 1. Архитектура — один стенд, бенчи данными

Не зоопарк тулов «на каждый тип боя», а **один движок прогона + метрики**, а «типы боёв» — это разные сценарии/бенчи поверх него.

### 1.1 Сборки (asmdef)

| Сборка | Платформа | Содержимое | Ссылки |
|---|---|---|---|
| `Guildmaster.Balance` | runtime (all) | Только data-контракт: `BalanceScenarioData` (SO), enum'ы (`BenchKind`, `TargetShape`), спеки синтетиков (`SyntheticUnitSpec`). Тонкая, чтобы SO-ассеты сериализовались. | `Guildmaster.Data`, `Guildmaster.Core` |
| `Guildmaster.Balance.Editor` | editor | Движок: `SimBenchRunner` (строит sim, тикает, собирает метрики), `MetricCollector`, бенчи, рейтинг (Bradley-Terry/Elo), статический аудитор, писатели CSV/MD, пункты меню `Tools/Balance/*`. | `Guildmaster.Balance`, `Guildmaster.Combat`, `Guildmaster.Core`, `Guildmaster.Data`, `UnityEditor` |
| `Guildmaster.Balance.Tests` | editor (EditMode) | Санити-тесты метрик (манекен получает ровно N урона; детерминизм same-seed). | `Guildmaster.Balance.Editor` + выше + `nunit` |

**Презентацию, `Guildmaster.Game`, аудио — не подключать вовсе.** Стенд их не требует. Размещение: `Assets/_Project/Scripts/Balance/` (+ `/Editor`), тесты — `Assets/_Project/Tests/EditMode/Balance/`.

### 1.2 Ядро прогона — `SimBenchRunner`

Один метод-примитив, на котором стоит всё: собери sim → влей юнитов двух команд → тикай до исхода или до кэпа → верни собранные метрики.

```
BenchResult Run(BenchSetup setup, ulong seed, int maxTicks)
  1. sim = BuildHeadlessSim(seed)              // как CombatSimulationTests.BuildSim
  2. collector = new MetricCollector(sim)      // подписка на outward-events
  3. setup.Spawn(sim, factory)                 // юниты команд 0/1 (синтетик или фабрика)
  4. for t in 0..maxTicks while Ongoing: sim.Tick(TickDelta)
  5. return collector.Build(sim.Outcome, sim.CurrentTick)
```

`maxTicks` — потолок (напр. 3600 = 120 с боя) с явной пометкой «timeout» в отчёте, чтобы вечные бои не висли и не врали как «ничья».

### 1.3 Метрики (`MetricCollector`)

Подписывается на outward-events, копит per-unit и агрегаты. Канон метрик (устоявшийся — DPS/TTK/EHP):

- **Исход:** победившая команда, длительность боя (тики → сек), выжившие, timeout-флаг.
- **Урон:** нанесённый/полученный по юниту, разложение по `DamageSourceKind` (AutoAttack/Ability/Periodic/Reactive), оверкилл.
- **Производные:** DPS (урон/сек), TTK (тик первой смерти цели), EHP/время жизни, доля урона по ролям.
- **Хил/щиты:** вылечено (по факту, без overheal), поглощено щитом.
- **Кто умирает первым** (порядок смертей) — сигнал хрупкости состава.

Правило (из `content-hub` §4): **не дублировать формулы сима.** DPS/урон берём из фактических событий боя, а не пересчитываем «damage×speed» руками. Разошедшиеся формулы = «таблица врёт».

### 1.4 Стартовая библиотека бенчей (процедурные)

Бенчи **генерируются кодом** из `ContentDatabase` — не N² ручных ассетов. Покрывают весь список Макса:

| Бенч | Схема | Метрика |
|---|---|---|
| DPS-solo | юнит vs бессмертный манекен | чистый single-target DPS, рампап |
| DPS-AoE | юнит vs кластер манекенов | AoE-пропускная, спред урона |
| Survivability-solo | фикс-DPS пушка vs юнит | время жизни, EHP |
| Survivability-AoE | AoE-пушка vs юнит | EHP под AoE |
| Duel (round-robin) | 1v1 все×все (Фаза 2) | матрица матчапов + рейтинг |
| Squad | состав vs состав (кастом-сценарий) | win, вклад ролей |

### 1.5 Кастом-сценарий — `BalanceScenarioData` (SO)

Контракт для бесспоке-боёв, которые ГД хочет закрепить (конкретный состав vs состав): две стороны (id юнитов + сосуды + предметы + позиции/режим расстановки), арена, условие/кэп, что мерить. Стартовая пара образцов. Это **дополнение** к процедурным бенчам, не замена.

---

## 2. Фазы (порядок реализации)

- **Фаза 0 — статический аудитор (парсинг).** Без симуляции: пробег по всем контент-SO, бейк реальных статов (реюз `Stats`+`StatsConfig`, не свои формулы), расчёт грубых производных (raw-DPS = урон×скорость, EHP), флаг выбросов и «мёртвых» статов. Ловит грубые ошибки до всякого боя, стоит копейки. Меню `Tools/Balance/Audit Content`. Производные помечаются «raw, без учёта wind-up/способностей».
- **Фаза 1 — стенд + бенчи + CSV.** `SimBenchRunner` + `MetricCollector` + синтетик-манекены + бенчи DPS/AoE/EHP + экспорт CSV/MD + `BalanceScenarioData`-контракт. Ядро ценности.
- **Фаза 2 — round-robin + рейтинг.** Дуэли все×все по реальным реликвиям, матрица матчапов, рейтинг **Bradley-Terry/Elo** (корректнее сырого win-rate; при детерминированном 1/0 сейчас — вырождается в топологическую сортировку, помечаем). Меню `Tools/Balance/Duel Matrix`.

---

## 3. Non-goals (сознательно НЕ делаем)

- **Сочетания/синергии** — стенд их не покрывает, только сэмплирует. Эмерджентную синергию видит Макс на плейтестах, не стенд. (Продвинутая кластеризация «beyond win rates» — бэклог, не сейчас.)
- **ML-автобалансер** — оверкилл и противоречит принципу «баланс решает ГД». Стенд даёт цифры, решает человек.
- **Регрессия баланса в CI** — вырезано по решению Макса (2026-07-17): наблюдательность за дрейфом баланса — целиком на нём, автогейта не ставим.
- **Красивый EditorWindow** — на старте меню+CSV (читается в спредшите). Окно — возможная будущая фаза (или страница «Баланс» в [[tech/40-planning/content-hub|Content Hub]]).

---

## 4. Карта файлов (план)

```
Assets/_Project/Scripts/Balance/
  Guildmaster.Balance.asmdef
  BalanceScenarioData.cs        # SO-контракт кастом-боя
  BenchKind.cs / TargetShape.cs # enum'ы
  SyntheticUnitSpec.cs          # спека манекена (data-only)
  Editor/
    Guildmaster.Balance.Editor.asmdef
    SimBenchRunner.cs           # примитив прогона
    MetricCollector.cs          # агрегатор метрик с events
    BenchResult.cs              # DTO результата
    SyntheticUnits.cs           # сборка манекенов в RuntimeUnit
    ContentAuditor.cs           # Фаза 0
    Benches/DpsBench.cs SurvivabilityBench.cs DuelMatrixBench.cs
    Rating/BradleyTerry.cs      # Фаза 2
    Report/CsvWriter.cs MarkdownWriter.cs
    BalanceMenu.cs              # пункты Tools/Balance/*
Assets/_Project/Tests/EditMode/Balance/
  Guildmaster.Balance.Tests.asmdef
  MetricCollectorTests.cs SimBenchRunnerTests.cs
```

Отчёты пишутся в gitignored-папку (`BalanceReports/` в корне проекта).
