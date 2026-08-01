---
title: "Journal - New Content Needs One Dot And A Registry Sync"
date: 2026-08-01
tags: [content, ids, localization]
---

**Решили:** id новой семьи контента именуются `relic.trash_archer`, а не `relic.trash.archer` —
подсемейство кодируется подчёркиванием внутри имени, а не второй точкой.

**Почему:** владелец правила — `ContentDomains.IsValidId`: ровно одна точка, дальше только
`[a-z0-9_]`. Иерархия «домен.семья.имя» правилом не предусмотрена, и расширять его ради семи
болванок значило бы трогать формат, на котором стоят лок-ключи, аудио-ключи и сейвы. Подчёркивание
даёт ту же читаемость (`relic.trash_*` группируется сортировкой) за нулевую цену.

**Грабли:** три штуки, все вскрылись только полным прогоном EditMode, потому что до него всё
выглядело рабочим.

1. **Невалидный id молчит до теста.** Ассет создаётся, инспектор доволен, юнит собирается и бьёт —
   ловит только `ContentValidationTests.AllContent_HasValidId`. Проверять новый id надо сразу:
   `ContentDomains.IsValidId(def.Id)` из `execute_code`.
2. **`ContentDatabase` не сканирует папку.** Реестр наполняется меню
   `Alebardium/Data/Sync Content Database`; без прогона новый контент есть на диске, но его нет для
   игры. Сторож — `ContentValidationTests.ContentDatabase_ExistsAndComplete`.
3. **Переименование id тянет за собой лок-ключи.** Они выводятся из id (`{id}.name`), поэтому
   переезд — это перенести значения на новые ключи и **убрать старые** через
   `Collection.SharedData.RemoveKey`, иначе в таблице копятся сироты, которых не видит ни один тест.

**Владелец правды:** `ContentDomains.IsValidId` (формат), `ContentDatabase.asset` (реестр),
тесты `ContentValidationTests`.
