---
title: "Roster - Balance"
status: draft
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

## Роли

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN roles AS role
GROUP BY role
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

### Стихийный

```dataview
TABLE length(rows) AS "Персонажей"
FROM "docs/wiki/gdd/relics"
WHERE kind = "character"
FLATTEN elemental_damage AS damage
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
