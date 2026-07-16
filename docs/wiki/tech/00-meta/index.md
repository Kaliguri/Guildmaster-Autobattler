---
title: "Meta - Index"
order: 0
status: living
updated: 2026-07-16
---

Карта технической документации (MOC). Здесь — **реализация**, не геймдизайн (дизайн — в `gdd/`). Порядок внутри кластеров задаётся полем `order`, отображаемое имя — `title`, имена файлов — латинские слаги. Ведение — контур скилла `tech-scribe` (в разработке).

> **Кластеры (Diátaxis-раскладка):** `reference/` (сухие факты) · `explanation/` (как и почему устроен код) · `how-to/` (решить задачу) · `planning/` (планы и роадмап) · `00-meta/` (служебное).

## Planning (`planning/`) — планы и роадмап

- [[tech/planning/roadmap|Planning - Roadmap]] — **план реализации по фазам** (главное окно в прогресс). `living`
- [[tech/planning/phase-1-combat-core|Phase 1: Combat Core]] — сборки, тик-цикл, системы боя. `реализовано`
- [[tech/planning/stat-system|Stat System]] — канонические 30 статов, операции, пайплайн урона. `ready`
- [[tech/planning/phase-2-effects|Phase 2: Effects & Abilities]] — модель эффектов, способности, диспел. `реализовано`
- [[tech/planning/phase-3-ai-relics|Phase 3: AI & Relics]] — AI-пресеты, каденс 10 Гц, движковые расширения, реликвии. `реализовано`
- [[tech/planning/attack-timing|Attack Timing]] — тайминг авто-атаки: windup + кадр удара. `реализовано`
- [[tech/planning/phase-4-content|Phase 4: Content Framework]] — контент-каркас: SO, id, реестр, конфиги. `реализовано`
- [[tech/planning/content-hub|Content Hub]] — UITK EditorWindow для авторинга контента. `реализовано`
- [[tech/planning/sfx|SFX (FMOD)]] — SFX-события, банки, микс. `в работе`
- [[tech/planning/deployment-encounters|Deployment & Encounters]] — расстановка, энкаунтеры, готовые бои (dev-срез). `реализовано`
- [[tech/planning/vertical-slice|Vertical Slice]] — петля забега, пролог, мета-заглушки. `living`
- [[tech/planning/stabilization|Stabilization & UI Loop]] — стабилизация, UI-петля, недостающие механики. `living` (текущее)
- [[tech/planning/visual-harness|Visual Harness]] — asmdef-слой презентации, играбельный харнесс боя. `история`
- [[tech/planning/seed|Seed & RNG]] — сиды, генерация забега, воспроизводимость. `planned`
- [[tech/planning/steam-workshop|Steam Workshop]] — Workshop-процессы, версионирование схемы (post-festival). `planned`

## Reference (`reference/`) — сухие факты

- [[tech/reference/tech-stack|Reference - Tech Stack]] — утверждённый стек, паттерны, уроки прошлого проекта. `needs_review`
- [[tech/reference/assemblies|Assemblies]] — карта asmdef, граф зависимостей, правила. `ready`
- [[tech/reference/combat-model|Combat Model]] — «Сосуд + Реликвия», стат-система, модель эффектов/диспела. `ready`
- [[tech/reference/data-layer|Data Layer]] — три слоя данных, каталог SO, id/лок-конвенции, реестр, валидация. `ready`
- [[tech/reference/saves|Saves]] — автосейв, хост, мультиплеер. `needs_review`
- [[tech/reference/scene-sorting|Scene & Sorting]] — иерархия BattleScene, сортировочные слои 2D, Y-sort. `needs_review`
- [[tech/reference/input-camera|Input & Camera]] — Input System за `IInputService`, Cinemachine (3 режима). `ready`
- [[tech/reference/arena|Arena & Deployment]] — геометрия арены как данные, зоны Normal/Extended. `needs_review`

## Explanation (`explanation/`) — как устроен код

- [[tech/explanation/index|Explanation - Code Map]] — слои, поток данных, карта классов, порядок чтения. `needs_review`
- [[tech/explanation/di-events|DI & Events]] — VContainer, скоупы, MessagePipe vs C#-события. `needs_review`
- [[tech/explanation/simulation|Simulation & Tick]] — тик 30 Гц, аккумулятор, команды, RNG, пауза. `needs_review`
- [[tech/explanation/data-stats-damage|Data, Stats, Damage]] — SO-контент, `StatType`, модификаторы, пайплайн урона. `ready`
- [[tech/explanation/effects-abilities|Effects & Abilities]] — `[SerializeReference]`, stateless-компоненты, стаки, диспел. `ready`
- [[tech/explanation/presentation|Presentation]] — раздел сим/визуал, `CombatPresenter`, сглаживание 30→60. `ready`
- [[tech/explanation/netcode|Netcode]] — **host-authoritative** (решение), что запарковано, главная таска MP. `ready`
- [[tech/explanation/run-flow|Run Flow]] — стейт-машина забега, события как флоу, автосейв, реконнект. `ready`

## How-to (`how-to/`) — решить задачу

- [[tech/how-to/project-setup|How-to - Project Setup]] — чеклист настройки проекта Unity. `needs_review`
- [[tech/how-to/adding-assets|Adding Assets]] — Git LFS, import-настройки, нейминг, заводка арта/аудио. `needs_review`
- [[tech/how-to/docs-site|Docs Site]] — Quartz + Doxygen на GitHub Pages, CI/CD деплой. `ready`

## Meta (`00-meta/`) — служебное

- [[tech/00-meta/tech-changelog|Meta - Tech Changelog & Decisions]] — реестр решений, changelog, открытый техдолг, реестр аудита. `living`

---

## Легенда статусов (frontmatter `status`)

- `draft` — набросок · `needs_review` — готов, нужна сверка/ревизия · `ready` — актуально · `planned` — план на будущее (реализации ещё нет) · `living` — вечно живой (роадмап/changelog) · `archive` — исторический
