---
title: "Planning - Phase 4: Content Framework"
order: 60
status: archive
updated: 2026-07-28
---

**Статус:** Реализовано (контент-каркас: `ContentDefinition`, жизненный цикл id, реестр `ContentDatabase`, валидация). Документ — архив исполненного ТЗ.

---

> Исполняемое техзадание Фазы 4 «Контент-каркас». **Дизайн-источник — дата-слой (код `Assets/_Project/Scripts/Data/`, правила — скилл `xgaida-x-nixi-data-authoring`) (ред. 3)** — при любой неоднозначности этого ТЗ решает файл 13; при конфликте файла 13 с реальным кодом — спросить Макса, не угадывать.
> Исполнение: пакеты 0–7 строго по порядку (каждый опирается на предыдущий), один пакет = одна ветка = один PR в `dev`.

---

## 0. Общие правила исполнения (читать перед каждым пакетом)

1. **Ветки:** от свежего `origin/dev`, имя `feat/content-framework-p{N}`. PR в `dev`, squash-merge, **ветку после мержа удалить**. Коммиты — английский, conventional (`.cursor/rules/git-conventions.mdc`).
2. **Тесты:** после каждого пакета — полный EditMode-прогон (Unity MCP `run_tests` или `./scripts/run-tests.ps1`). База: **все текущие тесты зелёные** (~196). Красный тест, который «мешает», — повод остановиться, а не править тест.
3. **Детерминизм:** пакеты 0–3 не должны менять поведение боя при дефолтных значениях. Существующие checksum/детерминизм-тесты — контракт; если изменились ожидания — это регрессия.
4. **Сцены и префабы:** правки `BattleScene`/префабов — только через Unity MCP с проверкой, что префаб **не открыт в Prefab Stage** (`PrefabStageUtility.GetCurrentPrefabStage()` == null — жёсткое правило проекта). Ссылочные поля на сцене (например, конфиг-SO на `LifetimeScope`) — через `manage_gameobject`/`execute_code`, НЕ ручной правкой YAML. Если редактор занят/недоступен — оставить шаг Максу с точной инструкцией.
5. **Запреты из дизайна:** никакого Odin Serializer; `AnimationCurve.Evaluate` вне бейка; мутаций SO в рантайме; `Resources.Load`; статик-доступа к конфигам. Odin — только атрибуты/дровверы/окна (сборки `*.Editor`).
6. **`[MovedFrom]`:** при любом переименовании/переносе типа, сериализуемого через `[SerializeReference]`, — атрибут обязателен.
7. **Сериализация при рефакторинге классов:** Unity матчит данные по **именам полей**. Перенос поля в базовый класс безопасен, если имя не менялось. Переименование поля — только с `[FormerlySerializedAs]`. Перед переносом сверить фактические имена в коде и `.asset`-YAML.
8. **Editor-скрипты миграции ассетов:** одноразовые утилиты класть в `Assets/_Project/Scripts/Data/Editor/Migrations/`, меню `Tools/Guildmaster/Migrations/...`; после прогона утилита остаётся в репо (история), но помечается `[Obsolete]`-комментарием «выполнено, дата».
9. **Визуальная приёмка** (пакет 4 и любые изменения картинки) — за Максом; агент проверяет только не-визуально (тесты, значения, события).

Целевая раскладка ассетов (наводится в пакете 1, перемещение `.asset` безопасно — GUID сохраняется):

```
Assets/_Project/ScriptableObjects/
├── Relics/          ├── Enemies/        ├── Effects/
├── Traits/          ├── Items/          ├── Tags/
├── AiPresets/       ├── Guildmasters/   ├── Consequences/
├── RunModifiers/    ├── Vessels/        ├── Visuals/
├── Configs/         └── Database/ContentDatabase.asset
```

---

## Пакет 0 — Фикс-лист (чистка дублей и мёртвых конфигов)

**Цель:** один источник правды для каждого значения; поведение боя не меняется. Дизайн: файл 13 §4.2.

Шаги:

1. **ArmorK:** `CombatLifetimeScope` перестаёт держать `_armorK` — значение берётся из уже инжектируемого `StatsConfig.ArmorConstantK` (оба сейчас 100 — поведение идентично). Поле `_armorK` удалить, проводку `WithParameter`/конструкторов перевести на конфиг. Сцену проверить: лишний serialized-остаток не мешает, но если MCP доступен — почистить.
2. **`PassiveThresholdPct = 0.2`** в `BulwarkComponent.cs:64` и `DodgeComponent.cs:79`: заменить литерал на именованную константу `AIProfile.DefaultPassiveThresholdPct` (объявить в `AIProfile`, использовать и в его собственном дефолте). Итог: значение объявлено ровно один раз.
3. **«Глобальный радиус» `500f`** (`MarkTransferComponent.cs:16`, `AbilitySystem.cs:144`, `WhirlDashLandingComponent.cs:80`): вынести в `SimConstants.GlobalSearchRadius`, три места читают её.
4. **Радиус коллизии снаряда `Size × 0.25`** (`AutoAttackSystem.cs:117,148`): вынести в `SimConstants.ProjectileHitRadiusFactor`.
5. **Деспавн снарядов `±200`** (`ProjectileSystem.cs:135`): заменить на «границы арены + `SimConstants.ProjectileDespawnMargin` (предложение: 5)». Выяснить, как границы арены доступны системе (сим уже знает bounds для стен) — привязать к ним, не к мировому нулю.
6. **`GuildmasterCommands.MakeTestUnit`:** сверить дефолт-статы болванчика со `StatsConfig`; где инжект доступен — читать оттуда, где нет — оставить с комментарием-ссылкой на StatsConfig (не плодить тихий дрейф).
7. **`DamageNumberSpawner`/`DamageNumber`:** проверить использование (сцена/префабы/код). Если мертвы — удалить оба файла; если живы — доложить Максу, не удалять.
8. **`CombatFocusTarget._positionDamping`:** код-дефолт 3 → 4 (значение сцены; косметика).

**Acceptance:** полный прогон зелёный; grep не находит литералов `0.2f` (в этих трёх местах), `500f`, `0.25f` (в двух местах), `200f` в перечисленных файлах; поведенческие/checksum-тесты без изменений ожиданий.

---

## Пакет 1 — `ContentDefinition`, id, реестр, валидация

**Цель:** каркас идентификации и реестра. Дизайн: файл 13 §2, §3.6, §8.

Шаги:

1. `ContentDefinition : ScriptableObject` (abstract) в `Guildmaster.Data.Definitions`: `[SerializeField] string _id` + `public string Id`.
2. `ContentDomains` (static, там же): маппинг тип → префикс домена (`RelicData → "relic"`, `EffectData → "effect"`, `VesselData → "vessel"`, далее пополняется). Утилита `MakeId(Type, assetName)` → `domain.lower_snake`.
3. **Жизненный цикл id (файл 13 §2.3):** editor-логика в `Guildmaster.Data.Editor`:
   - автозаполнение пустого id из имени ассета (хук `OnValidate` в `ContentDefinition` под `#if UNITY_EDITOR`, сама генерация — в editor-утилите);
   - Odin-drawer: id read-only + кнопка «Edit Id» (разблокировка с warning);
   - авто-уникализация суффиксом `_2` при генерации, если занято.
4. **Миграция существующих типов:** `RelicData`, `VesselData`, `EffectData` наследуют `ContentDefinition`. У `EffectData` сейчас собственный Id — сверить имя backing-поля; если не `_id`, перенести с `[FormerlySerializedAs]`. Проставить ids миграционным скриптом: `relic.cryomancer` и т.д. (из имён ассетов), эффекты — префиксовать существующие id до `effect.*`. Поле `DisplayNameKey` удалить из `RelicData`/`VesselData` (ключи теперь выводятся из id — данные в ассетах уже соответствуют шаблону, потери нет).
5. **`ContentDatabase`** SO + `IContentDatabase` (файл 13 §3.6, сигнатуры там). Реализация словаря — при регистрации в DI. Editor-утилита `Tools/Guildmaster/Sync Content Database` — сканирует проект и наполняет списки (никакого ручного перетаскивания).
6. **DI:** `[SerializeField] ContentDatabase` на `RootLifetimeScope` → `RegisterInstance<IContentDatabase>`. ⚠ Назначение ассета в сцене — правило 0.4 (MCP или Макс).
7. **Раскладка папок** (см. §0) — переместить существующие ассеты.
8. **EditMode-тесты** `Tests/EditMode/Content/ContentValidationTests.cs` (правила файла 13 §8: 1, 2, 3, 5): формат/уникальность/непустота id по всем `ContentDefinition`-ассетам; полнота `ContentDatabase` (+ актуальность после Sync); отсутствие null в `[SerializeReference]`-списках; кросс-инварианты (`FleeAtHpPct < ReturnAtHpPct`, `AttackSpeedMin < Max`).

**Acceptance:** прогон зелёный, включая новые тесты; у всех контент-ассетов id формата `domain.name`; `ContentDatabase.asset` создан и полон; спавн через `gm_spawn_*` работает как раньше.

---

## Пакет 2 — Конфиги: `SimTuningConfig`, расширение `StatsConfig`, `GameConfig`

**Цель:** все глобальные ручки — в SO; сим получает снапшот. Дизайн: файл 13 §3.4, §4.

Шаги:

1. `SimTuningConfig` SO (`Configs/`): поля и дефолты — таблица файла 13 §3.4 (AiTickRate, MaxAttackAnimTicks, MinWindupTicks, сепарация ×4, ProjectileHitRadiusFactor, ProjectileDespawnMargin, KiteFleeFactor, DefaultPassiveThresholdPct, GlobalSearchRadius). `TickRate` НЕ переносится.
2. **Бейк:** plain-структура `SimTuning` (readonly, в `Guildmaster.Core` или `Combat`); `CombatLifetimeScope` строит её из SO один раз при создании скоупа и инжектит в системы. Системы/`SimConstants`-потребители переводятся на снапшот; в `SimConstants` остаются только `TickRate`/производные и не-балансные константы. Константы из пакета 0 (`GlobalSearchRadius` и др.) переезжают из `SimConstants` в конфиг.
3. `SeparationSystem` public-поля (live-твик `gm_sep_*`) инициализируются из снапшота; команды продолжают работать.
4. **`StatsConfig`:** заполнить `Defaults` базовыми значениями юнита (согласовать список с Максом по факту: MaxHP, MoveSpeed, AttackRange, AttackSpeed…— свериться с [[tech/40-planning/stat-system|Planning - Stat System]] и текущими значениями болванчика/реликвий, чтобы дефолты не изменили баланс семёрки).
5. `GameConfig` SO: дефолты громкостей (master/music/sfx), `DefaultLocale`, `VesselItemSlots = 3`. Регистрация в `RootLifetimeScope`. Потребителей пока нет — только тип, ассет, DI.
6. Dev-команда `gm_tuning_rebake` (QC): пересобрать `SimTuning` из SO и применить к живому бою + `Debug.LogWarning("battle tainted")`.
7. Тесты: конфиг-инварианты в `ContentValidationTests` (клампы, диапазоны); тест «дефолты SimTuningConfig == прежние SimConstants-значения» (страховка неизменности баланса).

⚠ Сцена: поле `SimTuningConfig` на `CombatLifetimeScope` — правило 0.4.

**Acceptance:** прогон зелёный; checksum/детерминизм-тесты без изменений; `gm_sep_*` работает; grep: балансных литералов из таблицы §3.4 в коде систем нет.

---

## Пакет 3 — Иерархия `UnitData` + новые контент-типы

**Цель:** `UnitData` → `RelicData`/`EnemyData`; все мета-SO Фазы 4. Дизайн: файл 13 §3.0–3.2.

Шаги:

1. **`UnitData : ContentDefinition`** (abstract): перенести боевой кит из `RelicData` (список — файл 13 §3.1), **имена сериализованных полей не менять** (правило 0.7). `ExtendedZoneAccess` остаётся в `RelicData`.
2. `RelicData : UnitData`: остаются/добавляются `Rarity` (`RelicRarity`), `RunEffects: EffectData[]`, `AltAiPresets: AIPresetData[]`, `ExtendedZoneAccess`.
3. `EnemyData : UnitData`: `ThreatPoints: int`, `GoldBounty: int`.
4. **Потребители:** `RuntimeUnitFactory`, `BattleSetupBuilder`, `GuildmasterCommands` — принимать `UnitData` там, где нужен только кит (vessel-параметр остаётся опциональным как сейчас).
5. **`AIPresetData`** SO + миграция: editor-скрипт создаёт 7 ассетов `ai_preset.*` из inline-`Ai` реликвий, назначает в `UnitData.Ai`; после проверки — удалить inline-поле `AIProfile` из `RelicData` (отдельным коммитом внутри пакета: сначала перенос+назначение, прогон, потом удаление поля).
6. **Новые SO** (поля — таблицы файла 13 §3.0/3.2, без самодеятельности): `TagData` (+`TagCategory`), `TraitData` (+`TraitPolarity`), `ConsequenceData` (+`ConsequencePolarity`), `GuildmasterData`, `ItemData` (+`ItemScope`), `RunModifierData` (+`RunModTarget`). Enum'ы — в `Guildmaster.Data.Definitions` рядом с `CombatCategories.cs`.
7. `InfoTags: TagData[]` — добавить в `UnitData`, `EffectData`, `AbilityData`, и в новые типы по таблицам. `EffectData.Icon`, `UnitData.Icon` — добавить.
8. `ContentDomains` + `ContentDatabase` + Sync + валидация — пополнить всеми новыми типами (правило 7 §8: `GuildmasterData.StartingRelicIds` существуют, и т.п.).
9. **Глоссарий:** в `docs/wiki/gdd/0.4. Локализация.md` — Перк→**Trait** (EN), добавить строку Последствие→**Consequence** (система: Травма/Закалка — виды последствий). Упоминания в GDD 4/9 — минимальная синхронизация терминов EN.

**Acceptance:** прогон зелёный; бой семёрки идентичен (checksum-тесты); `.asset` реликвий не потеряли данных (диф ассетов — только ожидаемые поля); все новые типы создаются через `CreateAssetMenu`, валидируются, зарегистрированы в БД.

---

## Пакет 4 — Unity Animation: клипы-слоты и маркеры-как-данные

**Цель:** `UnitVisual` на `AnimationClip`-слотах; сим-тайминг из маркеров. Дизайн: файл 13 §3.1 (`UnitVisual`), решение ред. 3.

Шаги:

1. `UnitVisual` v2: поля-слоты `AnimationClip` (Idle/Run/Attack/Death/Hit/Skills[4]) + `Portrait`. Старые поля (спрайт-массивы, fps, `attackHitFrames`) пока пометить `[FormerlySerializedAs]`-нейтрально/оставить — удалить после успешной конверсии (шаг 5), отдельным коммитом.
2. Базовый `AnimatorController` (один на проект, `Visuals/UnitBase.controller`): стейты по слотам; `UnitView` собирает `AnimatorOverrideController` в рантайме из `UnitVisual`.
3. `UnitView`: заменить кастомный кадровый плеер на `Animator`; **`animator.fireEvents = false`**; портировать логику подгонки скорости стейта Attack под сим-windup (маркер удара должен лечь на сим-тик удара — логика существует в текущем плеере, переносится).
4. **Ридер маркеров:** утилита (Data или Presentation — по месту бейка) читает `clip.events`, фильтр по `functionName == "Marker"`; `event.time` → тики при бейке боя. Источник тайминга удара для сима — как раньше `attackHitFrames`, теперь маркеры клипа Attack.
5. **Конверсия 2 ассетов** (`FantasyWarrior`, `MedievalWarrior`): editor-скрипт генерирует `AnimationClip`-ы из спрайт-массивов (keyframe = кадр, длительность из fps), `attackHitFrames` → `AnimationEvent("Marker")` в клипе Attack. Клипы — в `Visuals/{Name}/`.
6. `AbilityData.VisualSlot` (`SkillSlot` enum) — поле добавить; каст играет слот (у текущих способностей — дефолт Skill1, назначить осмысленно, где очевидно).
7. Валидация §8 п.8: обязательные слоты заполнены; клипы Attack/Skill содержат ≥1 маркер; время маркера в пределах клипа; `VisualSlot` указывает на непустой слот.
8. ⚠ Префаб `UnitView` меняется (Animator-компонент) — правило 0.4 (Prefab Stage) обязательно.

**Acceptance:** прогон зелёный (включая windup/тайминг-тесты — ожидания не меняются); валидация маркеров зелёная; **визуальная приёмка Макса** (idle/run/attack/death, момент удара совпадает с ощущением до миграции); после приёмки — коммит с удалением легаси-полей и старого плеера.

---

## Пакет 5 — Контент-менеджер (Odin-окно)

**Цель:** одно окно для всего контента. Дизайн: файл 13 §9.

Шаги:

1. `ContentCrudService` (plain editor-класс, без GUI-вызовов, `Guildmaster.Data.Editor`): Create (тип → папка по §0, автогенерация id, фокус-переименование), Duplicate (**с регенерацией id**), Delete (проверка использований через `SearchService`/`ref=` → список), пути folder-per-type, вызов Sync БД после операций.
2. `ContentManagerWindow : OdinMenuEditorWindow` (`Tools/Guildmaster/Content Manager`): дерево доменов через `AddAllAssetsAtPath`, search-тулбар, справа Odin-инспектор; тулбар Create/Duplicate/Delete/Validate (Validate — прогон правил §8 по выбранному, вывод в окно).
3. Инструкция для Макса (в PR-описании): сетап Unity Search — включить dependency indexing, Saved Searches `t:RelicData`, `t:EffectData`…

**Acceptance:** ручной чеклист (создать/дублировать/удалить/провалидировать каждый тип — id корректны, файлы в правильных папках, БД синхронизирована); рантайм-код не тронут; прогон зелёный.

---

## Пакет 6 — Локализация + Addressables-плумбинг

**Цель:** таблицы EN/RU живые, ключи валидируются, билд не теряет переводы. Дизайн: файл 13 §5, §6.

Шаги:

1. Пакет Addressables 2.3.1 → **2.3.16** (`manifest.json`), Localization — Locales `en`, `ru`; String Table Collection `Content` (+ `UI` заготовкой). Auto-группы `Localization-*` не трогать; Play Mode Script — `Use Asset Database`.
2. **Build Addressables on Player Build** включить в настройках; для CI (GameCI) — проверить, что контент собирается при билде плеера (при необходимости — явный `BuildPlayerContent()` в билд-методе; см. грабли game-ci #230).
3. `ILocalizationService` (`Guildmaster.Core` интерфейс, реализация в `Game` поверх таблиц): `GetString(key)`, смена локали. UI-потребители подключатся в Фазе 7 — сейчас сервис + DI.
4. **Editor-помощник** (файл 13 §6): drawer в инспекторе `ContentDefinition` — поля Name/Desc (EN/RU) читают/пишут entries таблицы `Content` по ключам `{id}.name`/`{id}.desc`; кнопка «Create missing keys» (по ассету и по домену).
5. Наполнить entries для существующего контента (7 реликвий, 2 визуала не нужны, вессели, эффекты с иконками) — EN-текст черновой (имена из ассетов), RU — по GDD-карточкам реликвий, где есть.
6. Валидация §8 п.4: обязательные суффиксы по доменам (relic/trait/item/…: `name`+`desc`; effect: `name`, `desc` — только если Icon задан).

**Acceptance:** прогон зелёный (включая новый лок-тест); в редакторе имя реликвии резолвится из таблицы; смена локали en↔ru меняет строку (проверка через тест сервиса или ручной чек).

---

## Пакет 7 — `AudioCatalog`

**Цель:** аудио-шов готов к приходу FMOD-контента. Дизайн: файл 13 §3.5, §7.

Шаги:

1. `AudioCatalog` SO — в **Presentation**-слое (проверить/добавить ссылку asmdef Presentation → FMODUnity): `Entries {Key, EventReference}[]`, `Defaults {Action, EventReference}[]`.
2. Резолвер (Presentation): ключ `{contentId}.{action}` → точный → дефолт действия → тишина + один лог на ключ за сессию. Подписка на боевые события MessagePipe — по образцу существующих презентеров; вызовы уходят в `IAudioService.Play(resolvedKey)` (реализация — заглушка, как сейчас).
3. Валидация: FMOD-сканер (Event References) — инструкция в PR; свой тест — каталог без дубликатов ключей, все `Defaults` покрывают канонический список действий (`attack`, `hit`, `death`, `cast`, `ui`).

**Acceptance:** компилится, прогон зелёный, резолвер покрыт юнит-тестом с фейк-каталогом; звука нет и не должно быть (FMOD-контент — Фаза 7/9).

---

## Пакет 8 — Ф5-типы (ОТЛОЖЕН)

`EncounterData`, `ActMapConfig`, `TextEventData`, `ShopConfig`, `RewardTableData`, `DifficultyConfig`, `DossierPoolsConfig` — **не делать в этой фазе**. Создаются при строительстве флоу забега (Фаза 5) по таблицам файла 13 §3.3; домены уже зарезервированы, валидация готова к расширению.

---

## Сводный Definition of Done Фазы 4

- [ ] Пакеты 0–7 смержены в `dev`, ветки удалены.
- [ ] Полный EditMode-прогон зелёный; checksum/детерминизм-тесты не меняли ожиданий ни в одном пакете.
- [ ] Все контент-ассеты: id `domain.name`, зарегистрированы в `ContentDatabase`, лок-ключи `name`/`desc` существуют.
- [ ] Глобальные балансные ручки — только в `StatsConfig`/`SimTuningConfig`/`GameConfig` (grep-чек по таблице §3.4).
- [ ] Контент-менеджер: create/duplicate/delete/validate работают по всем типам.
- [ ] Визуальная приёмка Макса: анимации (пакет 4) и общий смоук боя.
- [ ] Файл 13 переведён в статус «Реализовано» (+ отражены отклонения, если были); [[tech/00-meta/tech-changelog|Meta - Tech Changelog & Decisions]] пополнен записью о фазе.
