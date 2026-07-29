---
title: "Meta - Index"
order: 0
status: living
updated: 2026-07-28
---

Карта технической документации (MOC). Здесь — **реализация**, не геймдизайн (дизайн — в `gdd/`). Порядок внутри кластеров задаётся полем `order`, отображаемое имя — `title`, имена файлов — латинские слаги. Ведение — контур скилла `tech-scribe`.

> [!important] Статус дока живёт ТОЛЬКО во frontmatter
> В списках ниже статусов намеренно нет. Раньше они стояли ярлыками в конце строк — и это был
> второй владелец факта: ярлыки разошлись с полем `status` (`реализовано` / `в работе` / `история`
> вообще не из закрытого списка статусов). Актуальный статус смотри в дашборде выше, в шапке самого
> дока или прогоном `scripts/check-wiki-frontmatter.ps1`. **Не возвращать ярлыки в списки.**

> **Кластеры (Diátaxis-раскладка, папки нумерованы для порядка):** `10-reference/` (сухие факты) · `20-explanation/` (как и почему устроен код) · `30-how-to/` (решить задачу) · `40-planning/` (планы и роадмап) · `00-meta/` (служебное).

## Дашборд готовности (авто)

> [!info] Требует плагина Dataview
> Сводки считаются автоматически по `status`/`updated` из frontmatter. Без плагина работают ярлыки статуса в списках-кластерах ниже. Легенда статусов — в конце страницы.

### Сводка по статусам

```dataview
TABLE length(rows) AS "Доков"
FROM "tech"
WHERE file.name != "index"
GROUP BY status AS "Статус"
SORT key ASC
```

### Требует внимания (needs_review / draft)

```dataview
TABLE status AS "Статус", updated AS "Обновлён"
FROM "tech"
WHERE (status = "needs_review" OR status = "draft") AND file.name != "index"
SORT updated ASC, file.name ASC
```

## Planning (`40-planning/`) — планы и роадмап

- [[tech/40-planning/roadmap|Planning - Roadmap]] — **план реализации по фазам** (главное окно в прогресс).
- [[tech/40-planning/phase-1-combat-core|Phase 1: Combat Core]] — сборки, тик-цикл, системы боя.
- [[tech/40-planning/stat-system|Stat System]] — канонические 30 статов, операции, пайплайн урона.
- [[tech/40-planning/tick-resolution|Tick Resolution]] — тик как один момент: двухфазность, закон видимости, коммит урона и хила.
- [[tech/40-planning/phase-2-effects|Phase 2: Effects & Abilities]] — модель эффектов, способности, диспел.
- [[tech/40-planning/phase-3-ai-relics|Phase 3: AI & Relics]] — AI-пресеты, каденс 10 Гц, движковые расширения, реликвии.
- [[tech/40-planning/attack-timing|Attack Timing]] — тайминг авто-атаки: windup + кадр удара.
- [[tech/40-planning/barriers|Barriers]] — инженерная часть Стены и Купола: швы, порядок, тесты.
- [[tech/40-planning/phase-4-content|Phase 4: Content Framework]] — контент-каркас: SO, id, реестр, конфиги.
- [[tech/40-planning/content-hub|Content Hub]] — UITK EditorWindow для авторинга контента.
- [[tech/40-planning/sfx|SFX (FMOD)]] — SFX-события, банки, микс.
- [[tech/40-planning/sfx-round-2|SFX Round 2]] — покрытие, сведение, громкость: второй заход по звуку.
- [[tech/40-planning/deployment-encounters|Deployment & Encounters]] — расстановка, энкаунтеры, готовые бои (dev-срез).
- [[tech/40-planning/vertical-slice|Vertical Slice]] — петля забега, пролог, мета-заглушки.
- [[tech/40-planning/stabilization|Stabilization & UI Loop]] — стабилизация, UI-петля, недостающие механики.
- [[tech/40-planning/act-map-run-loop|Act Map & Run Loop]] — карта акта и всё, что на ней висит: магазин, сундук, элитка, экран исхода.
- [[tech/40-planning/visual-harness|Visual Harness]] — asmdef-слой презентации, играбельный харнесс боя.
- [[tech/40-planning/seed|Seed & RNG]] — сиды, генерация забега, воспроизводимость.
- [[tech/40-planning/simbench|SimBench Balance Harness]] — headless-стенд баланса поверх боевого ядра, метрики и бенчи.
- [[tech/40-planning/steam-workshop|Steam Workshop]] — Workshop-процессы, версионирование схемы (post-festival).
- [[tech/40-planning/lighting-2d|2D Lighting]] — динамический 2D-свет, тёмные сцены, normal-карты через Laigter.
- [[tech/40-planning/save-system|Save System]] — профили и гильдии, версионирование, миграции, Steam Cloud, кооп-швы.

## Reference (`10-reference/`) — сухие факты

- [[tech/10-reference/tech-stack|Reference - Tech Stack]] — утверждённый стек, паттерны, уроки прошлого проекта.
- [[tech/10-reference/code-standards|Reference - Code Standards]] — инварианты кода, документирование, детерминизм, фолбэки, меню редактора.
- [[tech/10-reference/assemblies|Assemblies]] — карта asmdef, граф зависимостей, правила.
- [[tech/10-reference/combat-model|Combat Model]] — «Сосуд + Реликвия», стат-система, модель эффектов/диспела.
- [[tech/10-reference/data-layer|Data Layer]] — три слоя данных, каталог SO, id/лок-конвенции, реестр, валидация.
- [[tech/10-reference/saves|Saves]] — автосейв, хост, мультиплеер.
- [[tech/10-reference/asset-inventory|Asset Inventory]] — какой контент в проекте, откуда, лицензии, что используется.
- [[tech/10-reference/editor-tools|Editor Tools]] — наш тулинг под одним корнем `Alebardium`, раскладка и приоритеты меню.
- [[tech/10-reference/audio-inventory|Audio Inventory]] — реестр звуков: генерируется из карты звука скриптом, не правится руками.
- [[tech/10-reference/scenes|Scenes]] — какие сцены есть, что каждая держит, когда грузится.
- [[tech/10-reference/scene-sorting|Scene & Sorting]] — конвенции именования в сцене, сортировочные слои 2D, Y-sort.
- [[tech/10-reference/input-camera|Input & Camera]] — Input System за `IInputService`, Cinemachine (4 режима, риг в `WorldScene`).
- [[tech/10-reference/ui-navigation|UI Navigation]] — стек-навигатор, типы экранов/слои, ввод = f(стек, фаза), `PointerOverUI`.
- [[tech/10-reference/ui-typography|UI Typography]] — шкала кегля по ярусам: единый источник размеров и стиля текста в UI.
- [[tech/10-reference/vfx-color|Reference - VFX Color]] — цвет боевых эффектов: два поля на юните, потребители, множители, порог bloom.
- [[tech/10-reference/arena|Arena & Deployment]] — геометрия арены как данные, зоны Normal/Extended.

## Explanation (`20-explanation/`) — как устроен код

- [[tech/20-explanation/index|Explanation - Code Map]] — слои, поток данных, карта классов, порядок чтения.
- [[tech/20-explanation/di-events|DI & Events]] — VContainer, скоупы, MessagePipe vs C#-события.
- [[tech/20-explanation/simulation|Simulation & Tick]] — тик 30 Гц, аккумулятор, команды, RNG, пауза.
- [[tech/20-explanation/data-stats-damage|Data, Stats, Damage]] — SO-контент, `StatType`, модификаторы, пайплайн урона.
- [[tech/20-explanation/effects-abilities|Effects & Abilities]] — `[SerializeReference]`, stateless-компоненты, стаки, диспел.
- [[tech/20-explanation/presentation|Presentation]] — раздел сим/визуал, `CombatPresenter`, сглаживание 30→60.
- [[tech/20-explanation/netcode|Netcode]] — **host-authoritative** (решение), что запарковано, главная таска MP.
- [[tech/20-explanation/run-flow|Run Flow]] — стейт-машина забега, события как флоу, автосейв, реконнект.

## How-to (`30-how-to/`) — решить задачу

- [[tech/30-how-to/project-setup|How-to - Project Setup]] — чеклист настройки проекта Unity.
- [[tech/30-how-to/adding-assets|Adding Assets]] — Git LFS, import-настройки, нейминг, заводка арта/аудио.
- [[tech/30-how-to/docs-site|Docs Site]] — Quartz + Doxygen на GitHub Pages, CI/CD деплой.

## Meta (`00-meta/`) — служебное

- **`00-meta/journal/`** — журнал решений: одна запись = один файл `ГГГГ-ММ-ДД-slug.md`, append-only.
  Владеет вопросом «почему так решили и что отвергли». Пополняется по правилу из `CLAUDE.md`;
  индекса нет намеренно — листинг папки строит Quartz, имя файла и есть навигация.
- [[tech/00-meta/tech-debt|Meta - Tech Debt]] — живой реестр сознательно отложенного долга и главной будущей таски.
- [[tech/00-meta/tech-changelog|Meta - Tech Changelog (Archive)]] — **архив** записей 19.06–28.07.2026. Не пополняется, с кодом не сверяется.

---

## Легенда статусов (frontmatter `status`)

- `draft` — набросок · `needs_review` — готов, нужна сверка/ревизия · `ready` — актуально · `planned` — план на будущее (реализации ещё нет) · `living` — вечно живой (роадмап/changelog) · `archive` — исторический
