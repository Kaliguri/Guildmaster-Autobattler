---
title: "Roster - Balance"
order: 20
status: draft
updated: 2026-07-26
---

# Баланс ростера

> [!info] Как работает
> Эта страница считает только персонажей с `kind: character` в [`../relics`](../relics/). Для интерактивной таблицы откройте [character-registry.base](character-registry.base).

> [!warning] Требование
> Блоки ниже используют community-плагин **Dataview**. Без него карточки и `.base` продолжают работать, но автоматические сводки на этой странице не отрисуются.

## Всего персонажей

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY rarity
SORT key ASC
```

## Роли (боевой класс)

Role — ось, выводимая из `combat_class`; заполняется один раз и не дублируется руками.

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY combat_class
SORT length(rows) DESC
```

## Профиль (Playstyle)

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN playstyle AS style
GROUP BY style
SORT length(rows) DESC
```

## Позиции и дальность

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY position
SORT key ASC
```

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY combat_range
SORT key ASC
```

## Типы урона

### Физический

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN physical_damage AS damage
GROUP BY damage
SORT length(rows) DESC
```

### Магическая школа

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN magical_damage AS damage
GROUP BY damage
SORT length(rows) DESC
```

### Сродство

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN affinity AS damage
GROUP BY damage
SORT length(rows) DESC
```

## Тематика и пол

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY gender
SORT key ASC
```

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN themes AS theme
GROUP BY theme
SORT length(rows) DESC
```

## Готовность карточек

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
GROUP BY status
SORT key ASC
```

## Данные, требующие уточнения

```dataview
TABLE needs_review AS "Что уточнить"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character" AND length(needs_review) > 0
SORT file.name ASC
```
