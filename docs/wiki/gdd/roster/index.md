---
title: "Roster - Overview"
order: 0
status: draft
updated: 2026-07-16
---

Вход в реестр персонажей и сводки баланса ростера. Это **представление** данных, а не источник истины.

## Правило источника истины

Данные персонажа живут в YAML-блоке его [карточки реликвии](../relics/) (папка `relics/`). Файлы этой папки — только читают и агрегируют эти поля. Ничего здесь не заполняется вручную.

## Файлы

| Файл | Назначение |
|---|---|
| [[gdd/roster/character-registry\|Roster - Character Registry]] | Как открыть интерактивный реестр (`.base`) и правило источника истины |
| `character-registry.base` | Obsidian Bases — интерактивная таблица всех карточек `kind: character` |
| [[gdd/roster/roster-balance\|Roster - Balance]] | Dataview-срезы: классы, профили, позиции, школы/сродства урона, тематика, пол, «требует уточнения» |
| [[gdd/roster/tag-reference\|Roster - Tag Reference]] | **Нормативный** словарь YAML-полей и допустимых значений (rarity/position/`combat_class`/школы/affinity/creature_type/playstyle/mechanics/…) |
| [[gdd/roster/unit-tag-glossary\|Roster - Unit Tag Glossary]] | Глоссарий доп-тегов: 4 оси `Role → DamageType → Playstyle → Mechanic`, EN-канон имён |
| [[gdd/roster/relic-tag-assignments\|Roster - Relic Tag Assignments]] | Раскладка тегов по реликвиям ростера |
| [[gdd/roster/roster-gaps\|Roster - Gaps]] | **Заявка на вердикт:** кого не хватает ростеру и сколько (только «кто», без механик) |

> «Справочник тегов» — источник допустимых значений для всех карточек (реликвий и врагов); карточки врагов ссылаются на него из [[gdd/enemies/index|Enemies - Catalog]].
