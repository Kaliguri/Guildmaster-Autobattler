# Журнал захода: расчистка `open.md` и статусы контента

Заход начат **2026-07-29**. Постановка — Макс: (1) вынести решённое из открытых вопросов в журнал,
(2) разобрать черновые идеи и разнести по канону, (3) завести разделители «реализовано / теория» и
док-статус на каждый тип контента.

Порядок согласован: **структура → расчистка → анализ черновиков**.
Идеи фиксируются только после вердикта Макса (правило скилла: своё предложение — `proposed`).

---

## Фаза 1 — структура (СДЕЛАНО 2026-07-29)

Итог: 67 карточек получили `impl`/`asset`/`order` по полосам; заведены 5 разделителей и 4 док-статуса;
линт расширен под новые поля; конвенция описана в справочнике скилла. Оба гейта зелёные
(`check-wiki-frontmatter` 179 доков, `check-wiki-links` 206 доков).

Что делаем: сквозной `order` по группам, файл-разделитель в каждой папке контента,
`implementation-status.md` на тип контента, поля `impl` / `asset` во frontmatter карточек.

### Собранные факты (сверка ассетов с ГДД, 2026-07-29)

Мерка: наличие ассета в `Assets/_Project/ScriptableObjects/` (для эффектов — рантайм-компонента
в `Assets/_Project/Scripts/Combat/Effects/Components/`).

**Реликвии — 10 из 21 карточки в движке:**

| Ассет | Карточка |
|---|---|
| `relic.assassin` | the-verdict |
| `relic.cryomancer` | the-winter |
| `relic.defender` | the-bulwark |
| `relic.druid` | the-bloom |
| `relic.flame_swordsman` | the-pyre |
| `relic.iron_spearman` | the-spear |
| `relic.light_shepherd` | the-shepherd |
| `relic.ranger` | the-hunter |
| `relic.treant` | the-thorn |
| `relic.whirl_monk` | the-gale |

`relic.base` — служебный базовый ассет, карточки не имеет и не должен.
Без ассета (11): the-bond, the-bonewright, the-cadence, the-draugr, the-martyr, the-mirror,
the-paragon, the-runesmith, the-storm, the-tide, the-warden.

**Враги — 4 из 13:** `enemy.goblin_archer`, `enemy.goblin_cutthroat`, `enemy.goblin_grunt`,
`enemy.goblin_warrior`. Плюс `enemy.training_dummy` — дев-болванка, карточки не имеет.
Без ассета: goblin-commander, goblin-shaman, goblin-wolfrider, pack-wolf, earth-golem и все
четыре бандита.

**Виды:** в движке только `Goblins.asset`; bandits / beasts / golems — только доки.

**Предметы — расхождение в обе стороны:**

| Ассет | Что внутри | В ГДД |
|---|---|---|
| `item.swift_boots` | `Stat 20`, Op Flat, `0.6` | есть в common-items, но там **+30%**, а в ассете flat |
| `item.oaken_charm` | MaxHp +200, Vessel | **карточки нет** |
| `item.war_banner` | MaxHp +80, Party | **карточки нет**; в ГДД четыре других Знамени |

**Эффекты — по наличию рантайм-компонента.** Нет в коде вовсе: Bleed, Sleep, Charm, Frenzy,
Immobilize, Silence. Есть: Burn (Ember/Ignition), Shield, Stealth, Mark, Heal, Dispel,
StatModifier, Displace, Control (стан). Яд — есть тег и урон, идентичность (анти-хил) нет.

### Решения по структуре (мои, механика — не дизайн)

1. `impl: engine | partial | paper` во frontmatter карточки — **единственный владелец** факта
   «в каком состоянии». `asset: [id]` — чей ассет. `impl_note` — чем именно расходится.
2. `order` по группам: служебные 0–9 · в движке 10–499 · **разделитель 500** · теория 510+ ·
   сырьё-инкубаторы 900.
3. Разделитель — файл `divider-theory.md`, `title` с обязательным кластер-префиксом (иначе
   роняет `check-wiki-frontmatter.ps1`, ослаблять гейт не стал).
4. `implementation-status.md` в каждой папке контента — сводка (Dataview) + раздел про то, чего
   в ГДД нет вовсе (движковые заглушки).

### Долг, найденный по ходу

- Док-строка `check-wiki-frontmatter.ps1` обещает пропуск `template-*`, а код их проверяет.
  Расхождение док↔код, чинится правкой док-строки.
- `item.oaken_charm` и `item.war_banner` — контент в движке без дизайна. Решить: завести карточки
  или удалить ассеты (вердикт Макса).

---

## Фаза 2 — расчистка `open.md` (не начата)

## Фаза 3 — разбор черновиков (не начата)
