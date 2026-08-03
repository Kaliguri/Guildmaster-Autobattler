---
title: "Reference - Editor Tools"
order: 55
status: ready
updated: 2026-07-20
---

**Статус:** ready — меню собрано под один корень 2026-07-20.

Наши редакторные инструменты и их место в главном меню Unity.

---

## ПРАВИЛО: один корень — `Alebardium`

**Весь наш редакторный тулинг живёт под `Alebardium/`.** Ни `Tools/`, ни `Tools/Guildmaster/`, ни своего
корня в панели — только `Alebardium`. Это имя студии, и по нему сразу видно, что инструмент наш, а не
пришёл с пакетом.

Так было не всегда: пункты расползлись по четырём корням (`Tools/Guildmaster`, `Tools/Balance`,
`Tools/UI Preview` и один-единственный правильный `Alebardium/Palette Remapper`), и найти нужное можно
было только перебором. Собрано в один корень; **новый `[MenuItem]` заводить сразу под `Alebardium/`.**

Проверить, что ничего не расползлось снова:

```powershell
rg 'MenuItem\("' Assets/_Project --type cs
```

Все строки должны начинаться с `Alebardium/`.

### Что НЕ трогаем

`[CreateAssetMenu]` остаётся под `Guildmaster/…` — это меню создания игрового **контента**
(`Assets → Create → Guildmaster → …`), а не инструменты студии. Разные вещи, разные корни: `Alebardium` —
чем мы работаем, `Guildmaster` — что мы делаем.

---

## Раскладка меню

Приоритеты расставлены так, чтобы окна были сверху, а группы — ниже и отделены чертой.

| Пункт | Приоритет | Что делает | Файл |
|---|---|---|---|
| `Alebardium/Content Hub` | 0 | Окно работы с контентом | `EditorTools/ContentHub/ContentHubWindow.cs` |
| `Alebardium/Palette Remapper` | 1 | Перекраска растрового арта в нашу палитру | `EditorTools/PaletteRemap/PaletteRemapWindow.cs` |
| `Alebardium/Balance/0. Audit Content` | 100 | Аудит контента | `Balance/Editor/BalanceMenu.cs` |
| `Alebardium/Balance/1. DPS Bench` | 120 | Прогон урона по всем реликвиям | там же |
| `Alebardium/Balance/1. Survivability Bench` | 121 | Прогон выживаемости | там же |
| `Alebardium/Balance/2. Duel Matrix + Rating` | 140 | Матрица дуэлей и рейтинг | там же |
| `Alebardium/Balance/Run Selected Scenario` | 160 | Прогнать выбранный сценарий | там же |
| `Alebardium/UI Preview/Loadout Inventory (redesign)` | 200 | Превью экрана инвентаря | `DevTools/UiPreviewMenu.cs` |
| `Alebardium/UI Preview/Component Gallery` | 220 | Галерея UI-компонентов | там же |
| `Alebardium/UI Preview/Loadout Hub (legacy)` | 221 | Старый хаб лоадаута | там же |
| `Alebardium/Audio/Populate Catalog from Manifest` | 300 | Заполнить аудио-каталог из манифеста | `EditorTools/Audio/AudioCatalogPopulator.cs` |
| `Alebardium/Data/Sync Content Database` | 400 | Пересобрать реестр контента | `Data/Editor/ContentDatabaseSync.cs` |
| `Alebardium/Data/Migrations/Phase 4 - Package 1` | 420 | Миграция id и раскладки | `Data/Editor/Migrations/` |
| `Alebardium/Data/Migrations/Phase 4 - Package 3 (AI presets)` | 421 | Миграция AI-пресетов | там же |
| `Alebardium/Data/Migrations/Phase 4 - Package 3 (StatsConfig)` | 422 | Миграция стат-конфига | там же |
| `Alebardium/Animation/…` | 610–620 | Профиль рига, валидатор клипов, разделение частей | `EditorTools/AnimationLab/Rig/` |
| `Alebardium/VFX/Post FX Lab` | 700 | Стенд пост-обработки: боевой профиль и карта живьём, A/B, снятие кадров | `Presentation/Editor/PostFxLabWindow.cs` |

### Как раздавать приоритеты новым пунктам

Unity рисует разделитель, когда соседние приоритеты расходятся больше чем на 10. Отсюда шаг сотнями
между группами и единицами внутри группы:

| Диапазон | Группа |
|---|---|
| 0–99 | окна верхнего уровня |
| 100–199 | `Balance/` |
| 200–299 | `UI Preview/` |
| 300–399 | `Audio/` |
| 400–499 | `Data/` |
| 600–699 | `Animation/` |
| 700–799 | `VFX/` |

Новая группа — следующая свободная сотня.

---

Связано с [[tech/10-reference/asset-inventory|Reference - Asset Inventory]] (Palette Remapper готовит
производные спрайты), [[tech/40-planning/sfx|Planning - SFX]] (аудио-каталог).
