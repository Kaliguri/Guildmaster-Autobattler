# Структура, frontmatter и порядок без поломки ссылок

Читать при реорганизации, заведении нового дока, смене порядка/статуса. Здесь — целевая
модель и механика «переставлять, не ломая ссылки».

## Ключевая идея: отвязать ПОРЯДОК от ИДЕНТИЧНОСТИ

Сейчас номер главы сидит в имени файла (`13. Сложность…md`), поэтому «переставить» = 
«переименовать» = «сломать ссылки/URL». Лечение — разнести две вещи:

- **Идентичность** = стабильный **латинский слаг** в имени файла (`difficulty.md`). Не
  меняется при переупорядочивании → ссылки вечны.
- **Порядок** = поле `order` во frontmatter. Сменить порядок = поправить число в YAML,
  файл не трогается.
- **Человеческое имя** = `title:` по системе `Cluster - Name` (EN, решено 2026-07-16).
  Obsidian и Quartz показывают `title`, а не имя файла, — человек видит `Combat - System`,
  агент/URL/git видят `combat-system`.

### Система тайтлов (EN, `Cluster - Name`)

`title = "<Кластер> - <Имя>"`, разделитель ` - `, весь заголовок на английском по всему
vault (единый язык, совпадает с EN-каноном сущностей). Кластер — ярлык папки:

| Папка | Кластер | Папка | Кластер |
|---|---|---|---|
| `00-meta` | `Meta` | `40-content` | `Content` |
| `10-vision` | `Vision` | `50-modes-ux` | `Modes` |
| `20-combat` | `Combat` | `roster` | `Roster` |
| `30-run-meta` | `Run` | `enemies` | `Enemies` / `Faction` |

- **Карточки-сущности** (слаги + структурный title): реликвии `Relic - <Common|Unique> -
  <Name (Class)>` (`the-bloom.md` → `Relic - Common - The Bloom (Druid)`); враги
  `<Faction> - <Tier> - <Name>` (`bandit-bruiser.md` → `Bandits - Common - Bandit Bruiser`);
  фракции `Faction - <Name>` (`goblins.md` → `Faction - Goblins`).
- **Обзорный файл раздела** не дублирует префикс: `combat-system` → `Combat - System`,
  не `Combat - Combat`.
- Показ `title` вместо имени файла: **Front Matter Title** (встроенный проводник) +
  пропатченный **File Tree Alternative** (`docs/obsidian/filetree-frontmatter-patch.py` —
  title + сортировка по `order`). `.base` показывает `title`, не `file.name`.

Это индустриальный паттерн (Docusaurus `sidebar_position`, Jekyll `nav_order`); Quartz
explorer сортирует по frontmatter-полю через `sortFn`.

## Frontmatter-схема ГДД-дока

```yaml
---
title: "Combat - System"       # EN, система Cluster - Name
order: 20                      # порядок внутри кластера
status: ready                  # draft | needs_review | ready | living | archive
pillars: [readable-autobattle] # какие столпы затрагивает (опц.)
updated: 2026-07-16
---
```

- `status`: `draft` (набросок) · `needs_review` (готов, нужен глаз Макса) · `ready`
  (можно реализовывать) · `living` (журнал/roadmap — вечно живой) · `archive` (легаси).
  Заменяет сломанную текстовую легенду (README обещал `Draft/Ready/Outdated`, а в таблицах
  жили `Живой/Повестка/Референс/Архив`).
- Dataview собирает дашборд: `TABLE status, updated FROM "gdd" SORT order` → живая карта
  «что готово».

## Целевые папки-кластеры (Johnny.Decimal-lite)

Номер — на уровне ПАПКИ (грубый порядок, авто-сортировка), файлы внутри — слаги + `order`:

```
docs/wiki/gdd/
  00-meta/       readme · journal-adr · roadmap · open · glossary · legacy
  10-vision/     vision · pillars · concept · lore
  20-combat/     combat-system · stats · effects
  30-run-meta/   injuries-mettle · procedural-lore · meta-progression · events-minigames
  40-content/    relics-overview + relics/ · items-banners · roster/ · enemies/
  50-modes-ux/   multiplayer · controls
  research/      depth · randomness-appendix · autobattlers/
```

Принципы: ≤7 top-level, вложенность ≤3–4 уровня, папка = «где живёт», MOC = навигация.
`research/` (бывшие `14. Где живёт глубина`, `13.1. Приложение`) — вынести из канона глав.
Схлопнуть `0.2 Открытые` + `0.3 Черновик` + `0.6 Повестка` в один `open`.

## Механика переименования без поломки ссылок

- **Если переименовывает Макс из Obsidian** — включённая настройка *Files & links →
  Automatically update internal links* авто-чинит и `[[wikilinks]]`, и markdown-ссылки.
- **Если файлы двигаю я (git/FS)** — Obsidian не в курсе; я обязана сам починить ссылки:
  grep по старому имени во всех `.md` (и `[[...]]`, и `[](...%20...)` с энкодингом),
  заменить на новый слаг. Проверить `0.0 README`-индекс и MOC отдельно.
- После миграции на слаги будущие переносы **бесплатны** — порядок правится `order`, имя
  стабильно.

## План миграции (крупный разовый заход — ТОЛЬКО с QA Макса)

Не запускать автоматически без явного «да». Порядок:

1. Договорились о целевой раскладке (готово) → составить карту `старое имя → слаг + папка + order`.
2. Проставить frontmatter (`title/order/status`) во все доки — можно инкрементально, ещё
   до переименования (ссылки не трогает).
3. Пакетно переместить/переименовать в слаги + кластер-папки; **сразу** починить все
   ссылки и индекс.
4. Обновить `0.0 README` (→ MOC), вынести `research/`, схлопнуть служебные.
5. Настроить Quartz `sortFn` по `order`; проверить сайт-сборку.
6. Отдать Максу на визуальный QA (граф ссылок, публикация Quartz).

## MOC вместо голых README-таблиц

Индекс-страница кластера — живой Map of Content: сгруппированные `[[ссылки]]` с одной
строкой контекста, а не просто таблица файлов. Обновляется при добавлении дока в кластер.
