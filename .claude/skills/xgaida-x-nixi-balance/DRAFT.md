# DRAFT — Balance (совместная работа над балансом)

**Статус:** ЧЕРНОВИК скилла, копится по ходу сессии 2026-07-17. Финализируем в `SKILL.md` в конце
сессии (Макс), когда поймём, что реально нужно. Пока — рабочие заметки об инструментах и петле.

Это НЕ триггерный скилл (файл `DRAFT.md`, не `SKILL.md`) — чтобы не регистрировался
полусырым. Превратить в `SKILL.md` осознанно, с описанием-триггером.

---

## Суть контура: петля баланса = read → edit → read

Баланс правит Макс (ГД). Моя роль — крутить петлю и давать цифры, не решать за него.
Инструменты — две стороны одной петли:

| Сторона | Инструмент | Где | Что |
|---|---|---|---|
| **READ** (метрики) | **SimBench** | `Assets/_Project/Scripts/Balance` (+`/Editor`), меню `Tools/Balance/*` | headless-стенд поверх боевого ядра: аудитор, DPS/AoE, выживаемость, дуэли+BT-рейтинг, кастом-сценарии. Отчёты CSV+MD в `BalanceReports/`. Дизайн — `docs/wiki/tech/40-planning/simbench.md`. |
| **WRITE** (правки) | **ContentEditService** | `Assets/_Project/Scripts/Data/Editor/ContentEditService.cs` (`Guildmaster.Data.Editor`) | безопасная правка ЗНАЧЕНИЙ контент-SO через SerializedObject+Undo, с change-record и журналом. Сосед `ContentCrudService` (тот — жизненный цикл ассета, этот — значения). |

Read без Write = таблицы, которые некуда применить. Write без Read = правки вслепую.
Петля: прогнал бенч → увидел перекос → поправил через `ContentEditService` → перепрогнал.

---

## READ: SimBench (кратко)

Меню `Tools/Balance/*` (или публичные `*.Run()` через `execute_code`):
- `ContentAuditor.Run()` — статический аудит (бейк статов + производные + флаги выбросов), без боя.
- `DpsBench.Run()` — DPS solo/AoE до убийства эталон-цели фикс-HP + разбивка урона Auto/Ability/DoT%.
- `SurvivabilityBench.Run()` — TTD/EHP vs эталон-атакующих (solo и focus3).
- `DuelMatrixBench.Run()` — round-robin 1v1 + матрица матчапов + BT-рейтинг.
- `ScenarioBench.Run(BalanceScenarioData)` — кастом-бой состав vs состав.

Готчи: бой сейчас **RNG-free по исходу** → дуэли детерминированы (WR 1/0), MC по сидам вырожден.
Immortal-dummy ломает %HP → DPS по эталон-цели фикс-HP. BT вырождается при идеальной сепарации
(надёжна колонка WinRate). Подробнее — память `[[simbench-tool]]` и simbench.md.

## WRITE: ContentEditService (API)

Editor-only, статические методы, каждый возвращает `Change` (было→стало). `Save()` в конце.
- `LoadAll<T>()`, `Resolve<T>(idOrName)` — выборка ассетов.
- `ScaleStat(unit, StatType, factor)` / `SetStat(unit, stat, op, value)` — статы (`_stats`).
- `SetFloat(asset, path, value)` / `AddFloat(asset, path, delta)` — любое float-поле по пути.
- `AddAbilityCooldown(unit, abilityId, delta)` — кулдаун способности.
- `SetEffectComponentFloat(effect, fieldName, value)` — поле компонента эффекта по ИМЕНИ (без знания
  пути массива; напр. `_internalCooldownSeconds` у `BulwarkComponent` = «кд щита» Defender).
- `WriteChangeLog(changes, title)` → `BalanceReports/balance_changes_*.md` (аудит «что крутили»).

Пример дожатой правки (сессия 2026-07-17): ближники MaxHP ×1.5, Ranger `_movingAttackSpeedPenaltyPct`
0.5→0.6, Defender щит cd 4→5, гоблины MaxHP ×1.5 — всё через один `execute_code`-скрипт, с журналом.

## Как правильно применять правки

- **Не hand-YAML, не execute_code «в лоб» по одному** — через `ContentEditService` (SerializedObject+Undo).
- Массовые правки — цикл по когорте (`LoadAll<RelicData>()` + фильтр по `AttackType`/имени).
- Значения живут в `.asset` (data-authoring HARD: не хардкод, дефолт в ассете).
- После правок — `Save()` + журнал; затем перепрогнать бенч, показать сдвиг Максу.

## Роль и границы (как в других скиллах)

- **Баланс решает Макс.** Я кручу петлю, даю цифры, предлагаю — не назначаю числа сама.
- **Стенд — доп-инфо, не судья.** Сочетания/синергии он не ловит. Финал — плейтест Макса.
- Стыки: **data-authoring** (правка ЗНАЧЕНИЙ SO — там же живёт `ContentEditService`), **combat-sim**
  (метрики через outward-события; правки контракта урона — там, напр. `DamageResult.SourceKind`),
  **tech-scribe** (доки о стенде — simbench.md, tech-changelog).

## Открытые вопросы к финализации скилла (обсудить в конце сессии)

- Нужны ли высокоуровневые когорты-хелперы (`MeleeRelics()`, `Goblins()`) — или цикла по `AttackType` хватает?
- Стоит ли пришить `ContentEditService` к странице «Balance» Content Hub (кнопки «×1.5 HP выделенным»)?
- Батч-режим «пресет правок из файла» (data-driven список изменений) — надо ли?
- Как фиксировать «дизайн-намерение» правки (почему +50% HP) — в журнале? в ГДД (gdd-scribe)?
- Что стенду не хватает по метрикам для реальных решений (кроме сочетаний, которые он принципиально не ловит)?
