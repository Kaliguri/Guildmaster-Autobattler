---
title: Main Page
order: 0
status: living
---

Живой обзор всей геймдизайн-документации (Dataview). Русские названия — из `title`,
статусы — из `status`. Таблицы обновляются сами при добавлении/правке доков.
Курируемая карта со ссылками-описаниями — [[index|Индекс ГДД (MOC)]].

**Быстро:** [[vision|Vision]] · [[pillars|Столпы]] · [[journal-adr|Журнал решений]] · [[roadmap|Roadmap]] · [[glossary|Глоссарий]] · [[open|Открытые вопросы]]

## Готовность по кластерам

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус", file.folder AS "Кластер"
FROM "gdd"
WHERE title AND status
SORT order ASC, file.folder ASC
```

## 10 · Видение

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd/10-vision"
WHERE title
SORT order ASC
```

## 20 · Бой

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd/20-combat"
WHERE title
SORT order ASC
```

## 30 · Забег и мета

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd/30-run-meta"
WHERE title
SORT order ASC
```

## 40 · Контент (главы)

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd/40-content"
WHERE title
SORT order ASC
```

## 50 · Со-режим и UX

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd/50-modes-ux"
WHERE title
SORT order ASC
```

## Контент-каталоги (карточки)

Реликвии-классы:

```dataview
LIST
FROM "gdd/relics"
WHERE kind = "character"
SORT file.name ASC
```

Противники:

```dataview
TABLE WITHOUT ID file.link AS "Противник", faction AS "Фракция", enemy_group AS "Тип"
FROM "gdd/enemies"
WHERE kind = "enemy"
SORT faction ASC, enemy_group ASC
```

## Что требует внимания

Черновики и «нужен глаз»:

```dataview
TABLE WITHOUT ID link(file.link, title) AS "Документ", status AS "Статус"
FROM "gdd"
WHERE title AND (status = "draft" OR status = "needs_review")
SORT status ASC, order ASC
```
