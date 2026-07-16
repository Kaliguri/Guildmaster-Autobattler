---
title: "Reference - Assemblies"
order: 10
status: ready
updated: 2026-07-16
---

**Статус:** Сверено с `.asmdef` (2026-07-16)

---

Актуальная карта сборок проекта. Обновлять при добавлении новых модулей или изменении зависимостей.

> Граф пересмотрен в рамках Фазы 1 (см. [[tech/40-planning/phase-1-combat-core|10. Архитектура реализации — Фаза 1]] §2). Старый набор (Core/Units/Combat/Guild/UI) был временным; здесь — финальная структура «как надо».

---

## Граф зависимостей

Рантайм-сборки (`includePlatforms: []` — все платформы):

```
Core
 ├─ Data            → Core
 ├─ Combat          → Core, Data
 ├─ Guild           → Core, Data
 ├─ MiniGames       → Core, Data
 ├─ Net             → Core, Combat
 ├─ Presentation    → Core, Data, Combat
 ├─ UI              → Core, Data, Guild
 ├─ DevTools        → Core, Data, Combat, Presentation, UI, Game
 └─ Game            → Core, Data, Combat, Guild, MiniGames, Net, Presentation, UI
```

Стрелка означает «зависит от». Всё зависит **только вниз**. `Game` — единственный рантайм-модуль, кто знает всех (composition root). `Core` ни от чего не зависит.

Editor-only сборки (`includePlatforms: ["Editor"]` — в билд не попадают):

```
Data.Editor        → Data, Core
Game.Editor        → Game, Core
Audio.Editor       → Presentation
ContentHub.Editor  → Data, Data.Editor, Core, Combat, Presentation
UI.Editor          → (нет ссылок)
```

Тестовые сборки (`UNITY_INCLUDE_TESTS`):

```
Tests.EditMode  → Core, Data, Data.Editor, Combat, Presentation, Game, Guild, ContentHub.Editor   [Editor]
Tests.PlayMode  → Core, Data, Combat, Game
```

---

## Текущие сборки

### Рантайм

| Сборка | Путь | Зависит от (внутр.) | Внешние пакеты | Назначение |
|---|---|---|---|---|
| `Guildmaster.Core` | `Scripts/Core/` | — | — | `IRngService`, математика сим, тик-контракты, базовые интерфейсы команд/событий |
| `Guildmaster.Data` | `Scripts/Data/` | Core | Odin (атрибуты, автореференс) | ScriptableObject-определения (`StatsConfig`, `RelicData`, `VesselData`, `EffectData`), `StatType`, `ScalableValue`, полиморфные интерфейсы поведения (`IEffectComponent`) |
| `Guildmaster.Combat` | `Scripts/Combat/` | Core, Data | — (чистая логика, без VContainer/UniTask/физики) | Детерминированная симуляция: `RuntimeUnit`, `Stats`, тик-степпер, системы, пайплайн урона, `ICombatContext`, spatial hash |
| `Guildmaster.Guild` | `Scripts/Guild/` | Core, Data | — | Ростер, `RunState`, ресурсы (стаб до Фазы 5) |
| `Guildmaster.MiniGames` | `Scripts/MiniGames/` | Core, Data | — | Изолированные мини-игры за `IMiniGame` (стаб) |
| `Guildmaster.Net` | `Scripts/Net/` | Core, Combat | NGO (`Unity.Netcode.Runtime`), VContainer | Host-authoritative реле команд, инициализация транспорта (спайк в Фазе 1) |
| `Guildmaster.Presentation` | `Scripts/Presentation/` | Core, Data, Combat | Shapes, LitMotion, UniTask, MessagePipe, VContainer, TextMeshPro, UnityEngine.UI, Cinemachine, FMOD | World-space вид боя: спрайты, HP-бары, damage numbers, debug-draw, фидбэк, боевая камера. **Только читает сим** |
| `Guildmaster.UI` | `Scripts/UI/` | Core, Data, Guild | VContainer, UniTask, MessagePipe(+VContainer), Localization | Экраны меню/HUD/карты на UI Toolkit, MVVM (стаб до Фазы 7) |
| `Guildmaster.DevTools` | `Scripts/DevTools/` | Core, Data, Combat, Presentation, UI, Game | Quantum Console (`QFSW.QC`), VContainer, Input System | Debug-команды `gm_*` |
| `Guildmaster.Game` | `Scripts/Game/` | Core, Data, Combat, Guild, MiniGames, Net, Presentation, UI | VContainer, UniTask, MessagePipe(+VContainer), FMOD, Input System, Localization, ResourceManager | Composition root (`RootLifetimeScope`/`CombatLifetimeScope`), GameFlow, загрузка сцен, пуск тик-цикла. NGO приходит транзитивно через `Net` |

### Editor-only (`includePlatforms: ["Editor"]`, в билд не попадают)

| Сборка | Путь | Зависит от (внутр.) | Внешние пакеты | Назначение |
|---|---|---|---|---|
| `Guildmaster.Data.Editor` | `Scripts/Data/Editor/` | Data, Core | Odin, Localization(+Editor) | Редакторные инспекторы и утилиты для контент-SO |
| `Guildmaster.Game.Editor` | `Scripts/Game/Editor/` | Game, Core | Odin | Редакторные утилиты композиции/Game |
| `Guildmaster.Audio.Editor` | `Scripts/EditorTools/Audio/` | Presentation | FMOD (+Editor) | Редакторный аудио-инструментарий (мост FMOD Studio) |
| `Guildmaster.ContentHub.Editor` | `Scripts/EditorTools/ContentHub/` | Data, Data.Editor, Core, Combat, Presentation | — | Окно Content Hub (авторинг контента) |
| `Guildmaster.UI.Editor` | `Scripts/EditorTools/UI/` | — | — | Редакторный UITK-инструментарий (namespace `Guildmaster.UI.EditorTools`) |

### Тесты

| Сборка | Путь | Зависит от (внутр.) | Внешние пакеты | Назначение |
|---|---|---|---|---|
| `Guildmaster.Tests.EditMode` | `Tests/EditMode/` | Core, Data, Data.Editor, Combat, Presentation, Game, Guild, ContentHub.Editor | NUnit, UniTask, TestRunner | Детерминизм, урон, статы, spatial hash, снаряды, контент-валидация (`Editor`-платформа) |
| `Guildmaster.Tests.PlayMode` | `Tests/PlayMode/` | Core, Data, Combat, Game | NUnit, TestRunner | Интеграция: battle start → loop → end |

> Пути рантайм/Editor-сборок — относительно `Assets/_Project/Scripts/`; тесты — относительно `Assets/_Project/Tests/`.
> Editor-сборки `Data.Editor` и `Game.Editor` включают Odin через `overrideReferences` + `precompiledReferences` (Sirenix DLL).

---

## Правила

### Куда класть новый скрипт

1. **Базовые контракты и математика сим** (`IRngService`, тик-константы, интерфейсы команд/событий) → `Core`
2. **Данные-определения** (SO: статы, реликвии, сосуды, эффекты; полиморфные интерфейсы поведения) → `Data`
3. **Логика боя** (симуляция, урон, движение, таргетинг, снаряды) → `Combat`
4. **Гильдия** (ростер, `RunState`, ресурсы) → `Guild`
5. **Сеть** (host-auth реле, транспорт) → `Net`
6. **World-space презентация боя** (спрайты, HP-бары, числа, debug-draw, Feel) → `Presentation`
7. **Экранный UI** (UI Toolkit, меню, HUD, MVVM) → `UI`
8. **Debug-команды** (`gm_*`, Quantum Console) → `DevTools`
9. **Composition root, GameFlow, загрузка сцен, тик-пульс** → `Game`
10. **Редакторные инструменты** (инспекторы, окна, авторинг-тулзы) → `<Module>/Editor/` или `EditorTools/<Tool>/`, отдельная `*.Editor`-сборка с `includePlatforms: ["Editor"]`

### Запрещённые зависимости

- `Core` не зависит ни от чего в проекте
- `Data` зависит только от `Core` (никакой боевой/сетевой логики)
- `Combat` — чистая C#-логика: **без VContainer, UniTask, Unity-физики**; зависимости приходят через конструкторы, не `[Inject]`
- `Presentation`/`UI` **только читают** сим — не мутируют состояние боя
- `Game` — вершина рантайма; из рантайм-сборок его импортирует только `DevTools`. Помимо этого на `Game` завязаны редакторные (`Game.Editor`) и тестовые (`Tests.*`) сборки — это допустимо, они вне рантайм-графа
- Циклические зависимости запрещены

> Если зависимость тянет вверх (`Combat` хочет знать о `Guild`) — вынеси общий интерфейс в `Core`. Если SO-определение тянет поведение (`RelicData` → `EffectData`/`IEffectComponent`) — оба живут в `Data`, ниже `Combat`.

### Добавление нового модуля

1. Создать папку в `Assets/_Project/Scripts/<ModuleName>/`
2. Создать `.asmdef`: `Create → Assembly Definition` в Unity
3. Имя сборки: `Guildmaster.<ModuleName>`
4. Namespace: `Guildmaster.<ModuleName>`
5. Добавить зависимости только те, что реально нужны
6. Обновить таблицу **в этом файле**
7. Обновить `.asmdef` тестовых сборок, если тесты покрывают новый модуль

### Именование

- Файл `.asmdef`: `Guildmaster.<ModuleName>.asmdef`
- Имя сборки (`name`): `Guildmaster.<ModuleName>`
- Корневой namespace (`rootNamespace`): `Guildmaster.<ModuleName>`
- Папка: `PascalCase`, совпадает с именем модуля

---

## История изменений

| Дата | Изменение |
|---|---|
| 2026-05-28 | Начальная структура: Core, Units, Combat, Guild, UI |
| 2026-05-30 | Рефактор графа под Фазу 1 (док 10 §2): `Units`→`Data`; добавлены `MiniGames`, `Net`, `Presentation`, `DevTools`, `Game`. Тесты переведены на `Data` |
| 2026-07-16 | Сверка с реальными `.asmdef`: добавлен Editor-слой (`Data.Editor`, `Game.Editor`, `Audio.Editor`, `ContentHub.Editor`, `UI.Editor`); уточнены внешние пакеты и внутренние ссылки (`Net`→VContainer, `DevTools`→Presentation/UI, `Game`↛NGO напрямую); исправлен путь тестов (`Assets/_Project/Tests/`) |
