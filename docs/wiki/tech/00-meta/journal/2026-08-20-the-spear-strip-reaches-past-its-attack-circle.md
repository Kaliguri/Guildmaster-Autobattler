---
title: "Journal - The Spear Strip Reaches Past Its Attack Circle"
date: 2026-08-20
tags: [presentation, combat, vfx]
---

**Решили:** круг досягаемости показа считает `ImpactReach.ForAutoAttack` — чистая функция рядом с
солвером зон, знающая про линейную авто-атаку; для полосы круг равен расстоянию до её дальнего УГЛА
(`√(length² + halfWidth²)`), а не до конца.

**Почему:** показ брал круг только по `AttackRange` (`ReachCenter`), а `AutoAttackSystem.DealLineDamage`
бьёт на `Reach * AutoAttackLengthMult` и задевает ВСЕХ врагов в полосе, не только выбранную цель. У
Копейщика (единственный кит с `AutoAttackShape.Line`: круг 2.6, длина ×2, ширина 2.25) второй задетый
оказывался для показа недосягаем — `VisualDefects` орал «ни одной достижимой зоны» на штатном ударе, а
вспышка садилась заплаткой в наименее далёкую зону. Умножить прямо в презентере было проще, но формула
живёт по обе стороны шва sim→presentation: нарушить её можно из боевого файла, не открыв файл показа.
Поэтому она вынесена в тестируемую статику. Полуширина учтена не для красоты — narrow-phase
`QueryUnitsInLine` пускает цель на `width/2` вбок от оси, и такая цель дальше от бьющего, чем на длину
полосы.

**Грабли:** множитель повторён БУКВАЛЬНО, без страховочного `Max(1f)`: полоса короче круга — решение
контента, и показ обязан с ним согласиться, а не спорить. Тик яда и урон способности круг авто-атаки
не расширяют — вид урона теперь приходит в `ImpactPointFor` параметром (`DamageResult.SourceKind`).

**Владелец правды:** `Presentation/Effects/ImpactReach.cs`, тесты `ImpactZoneTests` (три штуки:
`SingleAutoAttack_KeepsSimulationReach`, `LineAutoAttack_ReachesFarCornerOfTheStrip`,
`TargetInsideStrip_IsNotDegenerate`).
