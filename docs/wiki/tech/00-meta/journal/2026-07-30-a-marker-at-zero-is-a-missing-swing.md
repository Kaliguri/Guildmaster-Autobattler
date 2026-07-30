---
title: "Journal - A Contact Marker At Zero Is A Missing Swing"
date: 2026-07-30
tags: [animation, content, combat]
---

**Решили:** при переезде юнита на новый пак арта маркер контакта в клипе атаки ставится на ту же ДОЛЮ
клипа, что была у прежнего арта, а не «на глаз». Монах переехал на Fantasy Warrior — маркер поставлен на
50%, потому что прежний Medieval Warrior отмечал кадр 2 из 4. Инвариант «маркер не на нуле» держит
`AnimationValidationTests.Visuals_AttackMarkerIsNotAtFrameZero`.

**Почему:** сим выводит из маркера долю замаха (`windup = hitFrame / frameCount`), значит смена арта
незаметно меняет БОЕВОЙ тайминг. У Fantasy Warrior маркер лежал на времени 0, и авто-атака Монаха стала
бить в первом же кадре, без подводки. Отвергли «поставить где красиво»: подбор доли на глаз — это правка
баланса под видом правки визуала. Друида править не пришлось: Forest Mushroom отмечает кадр 5 из 10 — та
же половина.

**Грабли:** ноль формально «внутри клипа», поэтому существующая проверка `Visuals_AttackClipHasMarkerWithinClip`
его пропускала: дефект здесь не отсутствующий маркер, а бессмысленный. И `AnimatorOverrideController` тут
не при чём — тайминг живёт в маркере КЛИПА, а слоты контроллера и `UnitVisual._skillClips` заполняются
отдельно (все три места пришлось тронуть за один переезд).

**Владелец правды:** `Assets/_Project/Scripts/Data/Definitions/ClipMarkers.cs`,
тест `Assets/_Project/Tests/EditMode/Content/AnimationValidationTests.cs`.
