---
title: "Journal - The Block Reads As A Shadow Around The Shield"
date: 2026-07-31
tags: [vfx, presentation, combat]
---

**Решили:** блок показывается **затемнением тела**, а не подсветкой щита. В момент, когда щит поглотил
урон, тело уходит в тень (`CombatFeelConfig.BlockBodyDim = 0.45` на `BlockDimSeconds = 0.18`), а
часть-щит держит свой цвет — насыщенное золото `--gm-flare-honey` (#FFCC33). Удар, съеденный щитом
целиком, вспышки тела больше не получает вовсе: вспышка означает «дошло», а сюда не дошло.

**Почему:** вспышку ярче сделать нельзя — `PlayHitFlash` клампит peak к единице, и обычное попадание
уже в потолке (см. запись
[«Defence Turns Gold»](2026-07-31-defence-turns-gold-and-the-block-flare-comes-from-brightness.md)).
HDR-флар, которым я пыталась обойти потолок, Макс отверг как пересвет: «Надо было как синее раньше по
яркости! Не больше!!!». Его формулировка решения: «Скорее надо не щит сделать более ярким, а юнита
более тусклым» — и это работает лучше, потому что светлее уже некуда, а темнее есть куда.

**Шов:** у `BodyVisualState` появилось поле `ShieldTint` и метод `WithShieldTint`; `SkeletalBodyVisual`
получил `_shieldPart` и красит эту часть отдельно от прочих. Через `SpriteRenderer` напрямую нельзя —
тело живёт за `IUnitBodyVisual`, и у покадрового тела щита нет вовсе (там поле пустое, эффект не играет).
Обычный кадр отдаёт `ShieldTint == Tint`, так что для всех, кроме блока, ничего не меняется.

**Грабли:** реестр тумблеров джуса поймал новый вход сразу двумя тестами — `OnShieldAbsorbed` без
записи в `FeelToggleCoverageTests.Registry` и `EnableBlockDim` без входа. Это ровно та защита, ради
которой перепись живёт в тесте, а не в комментарии: ручка без эффекта и эффект без ручки одинаково
плохи, и оба ловятся автоматически.

**Владелец правды:** `UnitView.OnShieldAbsorbed` (твин затемнения, unscaled — блок случается в hitstop),
`SkeletalBodyVisual._shieldPart`, `CombatFeelConfig.BlockBodyDim` / `BlockDimSeconds`,
`tokens.semantic.uss` (`--gm-color-combat-block-flash`), тест `FeelToggleCoverageTests`.
