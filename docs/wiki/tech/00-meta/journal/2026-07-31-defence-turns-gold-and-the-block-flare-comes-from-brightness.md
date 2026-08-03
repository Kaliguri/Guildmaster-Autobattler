---
title: "Journal - Defence Turns Gold And The Block Flare Comes From Brightness"
date: 2026-07-31
tags: [vfx, ui, palette, combat]
---

**Решили:** защита стала золотой — семантический токен `--gm-color-combat-shield` теперь ссылается на
примитив `--gm-flare-gold` вместо `--gm-flare-ice`. Вспышка тела в момент блока берёт **тот же цвет,
поднятый в HDR** множителем `CombatColorPalette._blockFlashBrightness = 2.4` (итог — яркость 2.2, за
порогом bloom 1.0). Стан «Решительного удара» поднят 0.5 → 1 с в `ResoluteStrikeStun` и в карточке.

**Почему:** «ярче» нельзя было сделать силой вспышки — `UnitView.PlayHitFlash` клампит peak через
`Mathf.Clamp01`, а обычное попадание уже играет на единице. Ручка «во сколько раз ярче блок» в
feel-конфиге была бы обманом: поле есть, эффекта нет. Единственный способ поднять вспышку выше
потолка — цвет за порогом bloom, и он же объясняет, почему у флара нет своей роли в палитре: в USS
цвета LDR, а HDR по устройству палитры накручивается множителем в коде (так же живёт свечение юнита —
`_mainBrightness`, `_spreadBrightness`, `_overbrightBrightness`).

**Грабли:**
- **Владелец цвета — `tokens.*.uss`, а `GuildmasterPalette.asset` только снимок.** Я правила снимок
  напрямую, и это поймал тест `Снимок_совпадает_с_токенами_в_USS` («в снимке #FFD666, в USS #9EDBFF»).
  Правильный порядок: правка USS → меню `Alebardium → Дизайн-система → Пересобрать палитру`. Роль,
  добавленная руками в снимок, при пересборке исчезает — потому что её нет в источнике.
- Правка YAML ассета мимо редактора теряется: `BulwarkShield._telegraphSeconds` остался 0.6, хотя на
  диске стояло 0.3 — Unity держал своё значение в памяти. Ассеты правятся через `SerializedObject` +
  `SaveAssetIfDirty`, код — файлами.

**Владелец правды:** `UI/Theme/tokens.semantic.uss` (цвет защиты),
`CombatColorPalette.ShieldFlare` и `_blockFlashBrightness` (яркость вспышки блока),
`CombatPresenter` (блок берёт флар, а не UI-цвет), тест `Снимок_совпадает_с_токенами_в_USS`.
