---
title: "Journal - TMP Outline Needs A Shader Keyword"
date: 2026-07-31
tags: [presentation, ui, gamefeel, tmp]
---

**Решили:** обводку и подложку боевой цифры включать ключевыми словами шейдера
(`OUTLINE_ON`, `UNDERLAY_ON`), а не одними значениями свойств.

**Почему:** свойства `_OutlineWidth` / `_OutlineColor` / `_UnderlayColor` в материале цифры стояли
с самого его создания, но шейдер TMP читает их только при включённом ключевом слове. Материал,
сделанный копией дефолтного материала шрифта (`new Material(fontAsset.material)` или
`CopySerialized`), приносит значения и НЕ приносит keywords — в файле `.mat` это видно как пустой
`m_ValidKeywords`. Из-за этого цифра всё время рисовалась плоской: тёмный текст ложился на тёмное
тело и на землю без всякого контура, а инспектор при этом показывал «обводка 0.256» и выглядел
исправным.

**Грабли:** проверять надо не поле в инспекторе, а `m_ValidKeywords` в `.mat` (или
`material.IsKeywordEnabled`). Симптом «значение стоит, эффекта нет» у TMP означает keyword, а не
кривое число. Тот же механизм у мягкой тени: без `UNDERLAY_ON` смещение и мягкость игнорируются.

**Владелец правды:** `Assets/_Project/Art/Materials/TMP_CombatNumber_Outline.mat`, префаб
`Prefabs/UI/FloatingText.prefab`.
