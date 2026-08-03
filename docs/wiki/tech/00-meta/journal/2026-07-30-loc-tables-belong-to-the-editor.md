---
title: "Journal - Loc Tables Belong To The Editor"
date: 2026-07-30
tags: [localization, tooling, agents]
---

**Решили:** лок-ключи заводятся ТОЛЬКО через `LocalizationEditorSettings` (например из
`execute_code`) с точечным `SaveAssetIfDirty`. Правка `Content_*.asset` и `Content Shared Data.asset`
как текста — запрещённый путь, даже когда кажется, что формат простой.

**Почему:** правка файла проигрывает открытому редактору дважды. Во-первых, он держит таблицы в
памяти и перезаписывает диск своей версией — ручные вставки просто исчезают. Во-вторых, и это
дороже: в `Shared Data` после списка `m_Entries` идут ещё `m_Metadata` и `m_KeyGenerator`, поэтому
вставка «перед последним `references:`» кладёт записи ВНЕ списка. Такой YAML Unity разбирает частично
(`Parser Failure at line N: Unexpected sequence indicator`) и пересохраняет обрезанным — ru-таблица
потеряла 203 живых строки. Восстановлено `git checkout`, затем 36 ключей заведены API. Порядок
восстановления важен: после checkout нужен `ImportAsset(ForceUpdate)`, иначе редактор перезапишет
диск обрезанной версией, которую всё ещё держит в памяти.

Та же ловушка накрывает любой ассет: правка `AiPresets/Nightblade.asset` через sed «не сработала» —
тесты гоняли то, что редактор держал в памяти, и зеркальный бой падал с прежним трейсом до
принудительного переимпорта.

**Грабли:** теневой проект один на все сессии (`LOCALAPPDATA\<name>-UnityShadow`). Два параллельных
прогона дерутся за `bee_backend` («More than one copy of bee_backend running»), и второй висит без
единого слова в консоли — видно только в `%TEMP%\guildmaster-tests\EditMode.log`.

**Владелец правды:** `Content Shared Data.asset` (структура), `docs/roster-expansion-progress.md`
§Операционные ловушки.
