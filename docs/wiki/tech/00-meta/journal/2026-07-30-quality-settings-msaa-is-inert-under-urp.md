---
title: "Journal - Quality Settings MSAA Is Inert Under URP"
date: 2026-07-30
tags: [urp, settings, rendering]
---

**Решили:** тиры в `ProjectSettings/QualitySettings.asset` не выравниваем, хотя они выглядят
рассинхронизированными (`Very High` держит MSAA 2x, `Ultra` — ноль). Поле мёртвое: **URP не читает
`QualitySettings.antiAliasing`**, MSAA берётся из URP-ассета (`UniversalRP.asset`, `m_MSAA: 1` —
выключен). Заявка на «починить тиры» отозвана в
[`docs/unity-2d-settings-audit.md`](../../../../unity-2d-settings-audit.md) пунктом 3.

**Почему:** правка инертного поля создаёт видимость решённой проблемы и провоцирует следующего
искать эффект там, где его нет. Живое в тех же тирах ровно одно — `anisotropicTextures: Forced On`
на двух верхних; оно работает, но при ортокамере без наклона не даёт ничего.

**Грабли:** одно и то же имя свойства пишется по-разному в двух местах — в `.meta` это
`spriteGenerateFallbackPhysicsShape`, в пресете `m_SpriteGenerateFallbackPhysicsShape`. Угадывать
нельзя, читается через `SerializedObject` по итератору. Соседняя готча из того же захода: настройки
импорта у части живут и в `.meta`, и в пресете папки, привязанном через `PresetManager` — см.
[`2026-07-30-sprite-filtering-bilinear-no-mips`](2026-07-30-sprite-filtering-bilinear-no-mips.md).

**Владелец правды:** `UniversalRP.asset` (реальный MSAA), `docs/unity-2d-settings-audit.md` (что
проверено и отвергнуто).
