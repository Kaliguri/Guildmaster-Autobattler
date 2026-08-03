---
title: "Journal - Cast Glow Addresses Part By Role"
date: 2026-07-31
tags: [gamefeel, presentation, body]
---

**Решили:** свечение оружия при касте адресуется по РОЛИ части. Роль живёт компонентом-меткой
`UnitPartRole` на узле части скелета (`Weapon`/`Shield`/`Limb`/`Body`), а само свечение — три поля в
общем `BodyVisualState` (`Glow`/`GlowColor`/`GlowRoles`), не отдельный канал. Каждое тело само решает,
светится ли его часть (`SkeletalBodyVisual` — по пересечению роли с маской; `SpriteBodyVisual` —
целиком), и передаёт готовый `partGlows` в `BodyShaderIds.Write`.

**Почему:** роль принадлежит ЧАСТИ (клинку), а не порядку в `SkeletalBodyVisual._parts` — метка на
узле переживает пересборку списка частей, тогда как параллельный `List<PartRole>` рассинхронился бы с
`_parts` на первом же `RebuildParts`. Свечение поверх общего состояния, а не вторым писателем в
`UnitView`: меч обязан И светиться сам, И вспыхивать со всеми при ударе — второй независимый канал
развёл бы одно тело по двум владельцам. `Write` не выводит свечение из маски сам (не знает ролей
части) — признак вычисляет тело, поэтому покадровое тело без ролей светится целиком без спец-кейса в
шейдерном слое.

**Грабли:** `Body` — одновременно неймспейс (`Guildmaster.Presentation.Body`) и свойство вида
(`IUnitBodyVisual Body`). В объявлении типа `Body.PartRole` резолвится в неймспейс, а в ВЫРАЖЕНИИ
(`roles == Body.PartRole.None`) — в свойство, и компиляция падает `CS0236/CS1061`. Снято алиасом
`using PartRole = Guildmaster.Presentation.Body.PartRole;` в `UnitView`.

**Владелец правды:** `PartRole.cs`, `UnitPartRole.cs`, `SkeletalBodyVisual.cs` (кэш ролей +
`partGlows`), `BodyShaderIds.cs` (`Write`), тест `FeelToggleCoverageTests` (тумблер `EnableCastGlow`).
