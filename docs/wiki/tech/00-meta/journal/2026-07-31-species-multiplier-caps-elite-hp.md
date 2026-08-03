---
title: "Journal - Species Multiplier Caps Elite HP"
date: 2026-07-31
tags: [combat, stats, enemies, balance]
---

**Решили:** новым врагам стат-блок собирается каскадом класс×вид + per-unit `_stats` Override (урон/броня/дальность), как у существующих гоблинов; видовые множители фракций (bandits ×0.7 HP, beasts ×0.5/×1.2, golems ×2.0/×0.6) заведены стартовыми, под balance-замер. Элитам гоблинов override HP **убран** — оставлены честные класс×вид числа.

**Почему:** пробовала дать элите высокий HP через `_stats` MaxHP Override, но формула сборки (`Stats.cs`: `(base+ΣFlat)×(1+ΣPctAdd)×Π(1+PctMult)`, base = последний Override) применяет видовой `PercentMult` к ФИНАЛУ. `species.goblins` ×0.4 HP срезал override мага 450 → 180. Компенсирующий костыль «override = желаемое/0.4» (наездник 2400 → override 6000) отвергнут: хрупок к смене видового множителя и врёт в ассете. Flat тоже под множителем — не спасает. Итог: элиты гоблинов не пробивают видовую хрупкость, карточка наездника «HP МНОГО» даёт лишь 600 — расхождение вынесено в `enemies/implementation-status`, чистое решение (элитный подвид с компенсирующим PercentMult / пересмотр множителя) — за Максом/balance.

**Грабли:** видовой множитель — не «слой поверх класса», а множитель финала независимо от порядка групп модификаторов. Любой per-unit override/flat HP у врага с видовым HP-множителем умножается на него. Проверять статы врага без DI — `StatMath.BuildEffective(data, statsConfig, classBalanceConfig)` (тот же путь, что бой).

**Владелец правды:** `Stats.cs` (формула `Compose`), `EnemyScalers.cs` (порядок каскада), `StatMath.cs` (сверка без DI); тесты `StatsTests`, `ClassBaselineTests`. Расхождение — `docs/wiki/gdd/enemies/implementation-status.md`.
