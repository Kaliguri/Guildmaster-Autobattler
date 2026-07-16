---
title: "Meta - Index"
order: 0
status: living
---

Карта геймдизайн-документации (MOC). Порядок глав задаётся полем `order` во frontmatter;
имена файлов — латинские слаги, отображаемое имя — `title`. Ведение — контур скилла `gdd-scribe`.

## Сокращения

| Сокращение | Полное название |
|---|---|
| **ГД** | Гильдмастер |
| **«Сосуд»** | Участник гильдии, носитель Реликвий (Vessel). Термин всегда в кавычках «». |
| **Реликвия** | Реликвия героя — памятная вещь, хранящая силу героя |

## 00 · Служебные (`00-meta/`)

- [[journal-adr|Meta - Decision Journal]] — все принятые решения (ADR: дата + причина + статус). Источник, откуда решения расходятся по канону.
- [[gdd/00-meta/roadmap|Meta - Roadmap]] — только НЕрешённое, что требует обсуждения.
- [[gdd/00-meta/open|Meta - Open Questions]] — идеи на рассмотрении (свод бывших 0.2/0.3/0.6).
- [[glossary|Meta - Glossary]] — единый словарь терминов RU\|EN.
- [[legacy|Meta - Legacy]] — архив снятого: как было, почему сняли.

> План разработки (стадии, фазы) — в техчасти: [[tech/40-planning/roadmap|Тех: Roadmap реализации]]. Отдельного GDD-роадмапа нет.

## 10 · Видение (`10-vision/`)

- [[vision|Vision - Overview]] — чем игра является: pitch, столпы, core loop.
- [[pillars|Vision - Pillars]] — 4 столпа как фильтр всех решений.
- [[concept|Vision - Concept]] — жанр, ключевая идея, игровой цикл.
- [[lore|Vision - Lore]] — сеттинг и предыстория мира.
- [[guildmaster|Vision - Guildmaster]] — роль игрока: стартовый набор, стиль гильдии (в бою не участвует).
- [[difficulty-skill|Vision - Difficulty & Skill]] — три оси, модель рандома, правила честности.

## 20 · Бой (`20-combat/`)

- [[combat-system|Combat - System]] — автобой, типы боёв, подготовка и итог.
- [[stats|Combat - Stats]] — словарь и смысл боевых статов.
- [[effects|Combat - Effects]] — каталог эффектов + идентичность стихий и сродств.

## 30 · Забег и мета (`30-run-meta/`)

- [[injuries-mettle|Run - Injuries & Mettle]] — ось истощения «Сосуда».
- [[procedural-lore|Run - Procedural Lore]] — сид-генерируемая личность.
- [[meta-progression|Run - Meta Progression]] — экономика забега, реворд-ramp, левел реликвий.
- [[events-minigames|Run - Events & Minigames]] — карта, события, мини-игры.

## 40 · Контент (`40-content/`)

- [[relics-overview|Content - Relics]] — редкость, типы; Судьбы и перки. Карточки — [[relics/index|Content - Relics · каталог]].
- [[items-banners|Content - Items & Banners]] — предметы (Vessel) и Знамёна (Party): слоты, авто-триггеры.
- Ростер — [[roster/index|Roster - Overview]].
- Враги — [[enemies/index|Enemies - Catalog]] · [[enemies/factions/index|Factions - Index]].

## 50 · Со-режим и UX (`50-modes-ux/`)

- [[multiplayer|Modes - Multiplayer]] — кооператив, распределение «Сосудов».
- [[controls|Modes - Controls]] — раскладка клавиш (техника ввода — в техчасти).

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
