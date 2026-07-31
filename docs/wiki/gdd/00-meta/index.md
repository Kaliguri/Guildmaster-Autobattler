---
title: "Meta - Index"
order: 0
status: living
updated: 2026-07-30
---

Карта геймдизайн-документации (MOC). Порядок глав задаётся полем `order` во frontmatter;
имена файлов — латинские слаги, отображаемое имя — `title`. Ведение — контур скилла `gdd-scribe`.

## Дашборд готовности (авто)

> [!info] Требует плагина Dataview
> Сводки считаются автоматически по `status`/`updated` из frontmatter. Легенда статусов — в конце страницы.
> Механическую проверку шапок гоняет `scripts/check-wiki-frontmatter.ps1` (CI: workflow «Docs Lint»).

### Сводка по статусам

```dataview
TABLE length(rows) AS "Доков"
FROM "gdd"
WHERE file.name != "index"
GROUP BY status AS "Статус"
SORT key ASC
```

### Требует внимания (needs_review / draft)

```dataview
TABLE status AS "Статус", updated AS "Обновлён"
FROM "gdd"
WHERE (status = "needs_review" OR status = "draft") AND file.name != "index"
SORT updated ASC, file.name ASC
```

### Без даты обновления (свежесть неизвестна)

```dataview
TABLE status AS "Статус"
FROM "gdd"
WHERE !updated AND file.name != "index"
SORT file.name ASC
```

## Сокращения

| Сокращение | Полное название |
|---|---|
| **ГД** | Геймдизайн (ГД-решение, ГД-документация). **Роль игрока пишется словом — «Гильдмастер»**, сокращение за ней не закреплено |
| **«Сосуд»** | Участник гильдии, носитель Реликвий (Vessel). Термин всегда в кавычках «». |
| **Реликвия** | Реликвия героя — памятная вещь, хранящая силу героя |

## 00 · Служебные (`00-meta/`)

- [[journal-adr|Meta - Decision Journal]] — все принятые решения (ADR: дата + причина + статус). Источник, откуда решения расходятся по канону.
- [[gdd/00-meta/roadmap|Meta - Roadmap]] — только НЕрешённое, что требует обсуждения.
- [[gdd/00-meta/open|Meta - Inbox]] — **личный черновик Макса.** Скрайб сюда не пишет: только читает, разносит, сохраняет оригинал в `inbox/ГГГГ-ММ-ДД.md` и чистит файл целиком.
- [[gdd/00-meta/open-forks|Meta - Open Forks]] — только НЕрешённое: развилки без вердикта, сырьё под разбор, неутверждённое по карточкам, инкубатор требований.
- [[gdd/00-meta/closed-forks|Meta - Closed Forks]] — приложение к журналу: разборы **закрытых** развилок (почему выбрали именно так).
- [[glossary|Meta - Glossary]] — единый словарь терминов RU\|EN.
- [[legacy|Meta - Legacy]] — архив снятого: как было, почему сняли.

> План разработки (стадии, фазы) — в техчасти: [[tech/40-planning/roadmap|Тех: Roadmap реализации]]. Отдельного GDD-роадмапа нет.

## 10 · Видение (`10-vision/`)

- [[vision|Vision - Overview]] — чем игра является: pitch, столпы, core loop.
- [[pitch|Vision - Pitch]] — **сводный срез концепции 2026-07-19**; при расхождении с vision/concept главнее он.
- [[pillars|Vision - Pillars]] — **5 столпов** как фильтр всех решений.
- [[concept|Vision - Concept]] — жанр, ключевая идея, игровой цикл.
- [[guildmaster|Vision - Guildmaster & Captain]] — две разведённые сущности: **Гильдмастер** — роль игрока (руководит гильдией, в бою не участвует); **Капитан** — боевая сущность забега (стартовый набор Реликвий, гильдие-широкие бонусы, стиль).
- [[difficulty-skill|Vision - Difficulty & Skill]] — три оси, модель рандома, правила честности.
- [[visual-direction|Vision - Visual Direction]] — визуальный опыт и дорожная карта: стиль, пост-процесс, атмосфера, свет, переходы, gamefeel.
- [[character-animation|Vision - Character Animation]] — скелетка: два слоя движения, три оси переиспользования, инструмент, слои Animator (план, отложено).
- [[audio-subbuses|Vision - Audio Sub-buses]] — под-шины FMOD как ранний шов микса.

**Бэклоги подачи** (сырые каталоги идей, питают `visual-direction`):
[[backlog-ui-juice|UI Juice]] · [[backlog-atmosphere-light-post|Свет, пост, атмосфера]] ·
[[backlog-audio-sfx|Audio & SFX]]. Джус и боевые эффекты переехали в `70-gamefeel/`.

## 20 · Бой (`20-combat/`)

- [[combat-system|Combat - System]] — автобой, типы боёв, подготовка и итог.
- [[stats|Combat - Stats]] — словарь и смысл боевых статов.
- [[positioning|Combat - Positioning]] — слоты вокруг цели, бонус за тыл, удержание линии танком, поведение классов (круг вердиктов закрыт 2026-07-26).
- [[effects|Combat - Effects]] — каталог эффектов + идентичность стихий и сродств.

## 30 · Забег и мета (`30-run-meta/`)

- [[injuries-mettle|Run - Injuries & Mettle]] — ось истощения «Сосуда».
- [[procedural-lore|Run - Procedural Lore]] — сид-генерируемая личность.
- [[meta-progression|Run - Meta Progression]] — экономика забега, реворд-ramp, левел реликвий.
- [[vessel-progression|Run - Vessel Progression]] — уровни «Сосуда» в забеге: статы, Судьбы-квесты, Обеты.
- [[events-minigames|Run - Events & Minigames]] — карта, события, мини-игры.
- [[guild-development|Run - Guild Development]] — Слой 3: дом, ветераны, смертность, книга гильдии.

## 40 · Контент (`40-content/`)

- [[relics-overview|Content - Relics]] — редкость, типы, боевой класс; Судьбы и перки. Карточки — [[relics/index|Relic - Catalog]].
- [[items-banners|Content - Items & Banners]] — предметы (Vessel) и Знамёна (Party): слоты, авто-триггеры.
  Карточки — [[gdd/40-content/items/fey-cloak|Fey Cloak]] · [[gdd/40-content/items/common-items|Common Items]] · [[banners|Content - Banners]].
- **Методички авторинга** — [[gdd/40-content/authoring/index|Content - Authoring]]: как заводить
  [[gdd/40-content/authoring/unit|юнита]], [[gdd/40-content/authoring/unit-relic|реликвию]],
  [[gdd/40-content/authoring/unit-enemy|врага]], [[gdd/40-content/authoring/effect|эффект]],
  [[gdd/40-content/authoring/item|предмет]], [[gdd/40-content/authoring/relic-upgrades|улучшения]].
- Ростер — [[roster/index|Roster - Overview]] · [[gdd/roster/tag-reference|Справочник тегов]] ·
  [[gdd/roster/unit-tag-glossary|Глоссарий доп-тегов]] · [[gdd/roster/relic-tag-assignments|Раскладка тегов]].
- Враги — [[enemies/index|Enemies - Catalog]] · [[enemies/species/index|Species - Index]] · [[gdd/enemies/bosses/index|Enemies - Bosses]].

### Что из контента существует в игре

Дизайн-карточка и работающая сущность — разные вещи. Состояние живёт в поле `impl` шапки карточки
(`engine` · `partial` · `paper`), сводки — по типу контента:

[[gdd/relics/implementation-status|Реликвии]] · [[gdd/enemies/implementation-status|Противники и виды]] ·
[[gdd/40-content/items/implementation-status|Предметы и Знамёна]] ·
[[gdd/20-combat/effects/implementation-status|Эффекты]].

В дереве папок контента тот же порядок: заведённое в движке сверху, ниже файл-разделитель
`BELOW: PAPER ONLY` — только описанное. Правило `order`: служебные 0–9 · в движке 10–499 ·
разделитель 500 · бумага 510+ · инкубаторы идей 900.

## 50 · Со-режим и UX (`50-modes-ux/`)

**Кооп (`50-modes-ux/coop/`)** — кластер заведён 2026-07-30 на месте прежнего одиночного
`multiplayer.md`. Вход — [[gdd/50-modes-ux/coop/index|Coop - Overview]] (модель сессии, дом в коопе,
карта владения кооп-фактами).

- [[presence|Coop - Presence & Cursors]] — пассивная видимость: курсоры, наведение, «кто что держит».
- [[interplay|Coop - Interplay Registry]] — реестр командных интеграций: перехват из рук, рисование,
  пинги, эмоции, печати; тест на интеграцию и предохранитель «процесс против подтверждённого».
- [[arbiters|Coop - Arbiters & Disputes]] — голосование, кубик d6, перебросы, альтернативы кубику.
- [[minigames|Coop - Minigames & Roles]] — вмешательство в общее, кооп-мини-игры, занятие
  на время боя, роли и семи-кооп.
- [[sync-model|Coop - Sync Model]] — что риалтайм, что «кино», что транзакция.
- [[wagers|Coop - Wagers & Personal Currency]] — личная валюта Гильдмастера, пари на бой, косметика,
  которую видят остальные.

Остальное в кластере:

- [[guild-courtyard|Modes - Guild Courtyard]] — Двор гильдии: форма хаба между забегами (люди телами,
  зоны, стол мастера, аватар игрока). Содержание дома — в [[guild-development]].
- [[proving-grounds|Modes - Proving Grounds]] — Ристалище: площадка вне забега, где смотрят бой.
- [[controls|Modes - Controls]] — раскладка клавиш (техника ввода — в техчасти).
- [[ui-feedback|Modes - UI Feedback]] — общие правила отклика интерфейса (недоступные действия и т.п.).

## 60 · Нарратив (`60-narrative/`)

Заведён 2026-07-29: **как** мы рассказываем историю и сама история. Карта кластера —
[[gdd/60-narrative/index|Narrative - Index]].

- [[gdd/60-narrative/lore|Narrative - Lore]] — сеттинг, путь к чемпионату, Реликвии в мире
  *(переехал из `10-vision/`)*.
- [[gdd/60-narrative/meta-narrative|Narrative - Meta]] — «игра, которая знает, что она игра»: тональный
  разворот, ключ «хаос против порядка» *(собран из трёх прежних владельцев)*.
- [[gdd/60-narrative/seeds|Narrative - Seeds]] — реестр посевов: что и где посеяно ради разворота.
- [[gdd/60-narrative/devices|Narrative - Devices]] — приёмы: голос разработчика, смена титулов, имена,
  которые надо заслужить.
- [[gdd/60-narrative/system-language|Narrative - System Language]] — язык Системы: `gm.`, локаль,
  адреса сущностей, уроки ресёрча.
- [[gdd/60-narrative/texts|Narrative - Texts]] — сами тексты: реплики, комментарии автора, лок-ключи.

Владелец кластера — скилл `xgaida-x-nixi-narrative`; тексты пишет он, в ассеты локализации их кладёт
`data-authoring`.

## 70 · Джус (`70-gamefeel/`)

Заведён 2026-07-31: **как бой ощущается** — эффекты, партиклы, свечение, тряска, замедление. Карта
кластера — [[gdd/70-gamefeel/index|Gamefeel - Index]].

- [[gdd/70-gamefeel/vfx-language|Gamefeel - VFX Language]] — как выглядит событие: роли-слои,
  тайминги, масштаб в долях роста юнита, словарь боевых событий.
- [[gdd/70-gamefeel/time-and-camera|Gamefeel - Time & Camera]] — реакция времени и камеры на момент:
  замедление, hitstop, тряска, фокус, зум — и политика значимости, кто из событий этого достоин.
- [[gdd/70-gamefeel/vfx-color|Gamefeel - VFX Color]] — чем красим: главный цвет и палитра разброса,
  роли оттенков ростера *(переехал из `10-vision/`)*.
- [[gdd/70-gamefeel/asset-palette|Gamefeel - Asset Palette]] — чем рисуем: какая текстура играет
  какую роль. Не инвентарь ассетов, а назначение.
- [[gdd/70-gamefeel/backlog-gamefeel|Gamefeel - Backlog (Tactile & Time)]] ·
  [[gdd/70-gamefeel/backlog-vfx-particles-shaders|Gamefeel - Backlog (Particles & Shaders)]] —
  банки идей *(переехали из `10-vision/`)*.

Владелец кластера — скилл `xgaida-x-nixi-gamefeel-vfx`: джус — область, где дизайн и реализация
неотделимы. Три слоя одного момента — визуал, время, камера — разведены по домам внутри кластера;
`visual-direction` трек 5 остаётся стилевой рамкой, но политика значимости живёт здесь.

## Research (`../../research/`)

- [[depth|Где живёт глубина]] — откуда глубина в PVE-автобаттлере без микро; разбор 14 игр жанра.
- [[randomness-appendix|Приложение — ресерч по рандому и скиллу]] — источники и приёмы других игр.
- [[00. Сводка и выводы|Разбор автобаттлеров]] — досье по играм жанра.

---

## Статусы документов (frontmatter `status`)

- `draft` — набросок, не финален
- `needs_review` — готов, нужен глаз Макса
- `ready` — готов, можно реализовывать
- `living` — вечно живой (журнал/roadmap/глоссарий)
- `archive` — легаси
