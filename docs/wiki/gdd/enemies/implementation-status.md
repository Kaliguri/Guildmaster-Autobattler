---
title: "Enemies - Implementation Status"
order: 2
status: living
updated: 2026-07-29
---

# Противники: что в движке, что на бумаге

Отвечает на один вопрос: **какой враг существует в игре, а какой только описан.**
Дизайн — в карточках, принципы ростера — в [[enemies/index|Enemies - Catalog]].

## Как читать статус

Владелец факта — поле `impl` в шапке карточки; здесь сводка. Значения — те же три, что у реликвий
(см. [[gdd/relics/implementation-status|Relic - Implementation Status]] §Как читать статус).
Поле `asset` — id ассета (`enemy.goblin_grunt`).

**Мерка сверки:** наличие ассета в `Assets/_Project/ScriptableObjects/Enemies/` (виды —
`ScriptableObjects/Species/`). Ручная сверка — 2026-07-29: **4 карточки из 13** в движке, из видов —
только гоблины.

## В движке

```dataview
TABLE asset AS "Ассет", impl_note AS "Расхождение"
FROM "gdd/enemies"
WHERE impl = "engine" OR impl = "partial"
SORT order ASC
```

## Только дизайн

```dataview
TABLE status AS "Статус дока"
FROM "gdd/enemies"
WHERE impl = "paper"
SORT order ASC
```

## Виды

| Вид | Состояние |
|---|---|
| [[gdd/enemies/species/goblins\|Goblins]] | в движке — `species.goblins` |
| [[gdd/enemies/species/bandits\|Bandits]] · [[gdd/enemies/species/beasts\|Beasts]] · [[gdd/enemies/species/golems\|Golems]] | только дизайн |

Все четыре бандита имеют карточки, но ни ассета врага, ни ассета вида — то есть **фракция бандитов
целиком на бумаге**. Это самая крупная готовая-к-реализации пачка контента.

## Ассеты без карточки

`enemy.training_dummy` — дев-болванка для арены отладки и балансных бенчей. Карточки не имеет
и не должна: это инструмент, не противник забега.

## Энкаунтеры

Восемь ассетов `Encounters/` (`GoblinAmbush`, `GoblinRaid`, `GoblinScouts`, `GoblinSkirmishLine`,
`GoblinWarband` + три `Dummy*`) карточек в ГДД **не имеют вовсе**: состав встречи — это баланс,
а не дизайн-сущность, и живёт он в бенчах контура `balance`. Если появится дизайн-язык встреч
(«какие бывают волны»), его дом — [[combat-system|Combat - System]], не эта папка.
