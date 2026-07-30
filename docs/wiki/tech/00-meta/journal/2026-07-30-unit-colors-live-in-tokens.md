---
title: "Journal - Unit Colors Live In Tokens, Units Keep Only Roles"
date: 2026-07-30
tags: [presentation, data, design-system, visual]
---

**Решили:** на `UnitData` не осталось ни одного `Color` — только роли (`UnitTone` — чем светит,
`BodyShade` — ступень приглушения тела). Значения живут в токенах дизайн-системы
(`--gm-flare-*` / `--gm-dim-*` → роли `--gm-color-unit-*` → снимок `GuildmasterPalette`), резолв роли в
цвет — `UnitColorRoles` (Data), HDR-яркость накручивает `CombatColorPalette` множителями-полями.
Градиент разброса перестал быть данными: он выводится из роли и кэшируется ПО РОЛИ.

**Почему:** требование Макса «все цвета в токенах, а не наобум в SO» — формально незакрытый долг правила
от 2026-07-27, цвета юнитов были последним местом с сырыми `Color`. Четыре развилки решены так:
(1) роли названы по СМЫСЛУ оттенка, а не по юниту — роль на каждого героя превратила бы палитру во
второй реестр контента при 25+ реликвиях; (2) множители яркости живут в `CombatColorPalette`, а не в
палитре и не в Data — в снимок палитры HDR не едет по правилу от 27.07, и это авторинг фидбэка;
(3) градиент разброса не поле, потому что три поля на юнита дублировали формулу множителей, вбитую
руками в 17 ассетов; (4) показ цвет НЕ резолвит — `UnitView.SetVfxSpread` подаёт презентер, иначе вид
стал бы вторым входом за цветом.

**Грабли:** четыре, и все про Unity, а не про цвет.
`PrefabUtility.SaveAsPrefabAsset` записал в вариант новый `runtimeAnimatorController`, но **молча
потерял `SpriteRenderer.sprite`** — тот остался прежним, и подмена арта выглядела наполовину сделанной.
Сработала правка через `SerializedObject` компонента + `SaveAssetIfDirty` + `ImportAsset`.
Спрайты пака лежат НЕ рядом с его `UnitVisual`: путь надо брать из idle-клипа
(`AnimationUtility.GetObjectReferenceCurveBindings`), угадывание по имени папки дало «нет такого файла».
Масштаб фигуры живёт на узле `Body` (у нас 3.6–4.5), и считать его надо от НЕПРОЗРАЧНОЙ высоты кадра, а
не от `sprite.bounds`: гриб при унаследованных 4.15 выходил ростом 2.6 юнита против 1.7 у человека.
`UnitVisual._skillClips` — отдельный список, независимый от `AnimatorOverrideController`: заполнив
только контроллер, я получила красный `AbilityVisualSlots_PointToNonEmptySlot` — при переезде на новый
пак заполнять надо оба места.

**Владелец правды:** `Assets/_Project/UI/Theme/tokens.primitives.uss` и `tokens.semantic.uss` (значения),
`Assets/_Project/Scripts/Data/Definitions/UnitColorRoles.cs` (роль → имя токена),
`Assets/_Project/Scripts/Presentation/Design/CombatColorPalette.cs` (множители и градиенты),
тест `Assets/_Project/Tests/EditMode/Content/UnitTintPolicyTests.cs`.
