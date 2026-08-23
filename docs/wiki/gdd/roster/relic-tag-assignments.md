---
title: "Roster - Memento Tag Assignments"
order: 6
status: needs_review
updated: 2026-07-24
---

# Предложенное распределение тегов по мементо

> **ЧЕРНОВИК на проверку Макса.** Теги по [[unit-tag-glossary|глоссарию]], сверено с **дизайн-
> карточками** (`relics/*`, их `mechanics`/`roles`) И фактическим kit кода (активки inline).
> `Role`/`DamageType` — **авто** (для контекста). `Playstyle`/`Mechanic` — предложение.

> **СВЕРЕНО С РЕАЛИЗАЦИЕЙ 2026-07-25** (рефактор модели урона). DamageType теперь считается
> **поисточниково** из данных: автоатака + наносящие урон способности. Колонка отражает то, что
> реально отдаёт `UnitTagResolver`. Playstyle/Mechanic проставлены в `_infoTags` всех 10 героев.

| Мементо (карточка) | Role | DamageType (авто) | Playstyle | Mechanic |
|---|---|---|---|---|
| **Assassin** (The Verdict) | Assassin | Physical · *Pierce* | Escape · Duelist | Stealth · Evasion · Execute · Burst |
| **Cryomancer** (The Winter) | Ranged | Magical · *Ice* | Debuffer · Peel | Control · AOE |
| **Defender** (The Bulwark) | Tank | Physical · *Slash* | Durable · Peel · Distraction | Shield · Control · Debuff |
| **Druid** (The Bloom) | Support | Physical · *Pierce* ⁽¹⁾ | Debuffer | DoT · Heal · AOE · Debuff |
| **FlameSwordsman** (The Pyre) | Bruiser | Physical · *Slash* + *Fire* ⁽²⁾ | Duelist | DoT · Ramp · Execute · Burst |
| **IronSpearman** (The Spear) | Bruiser | Physical · *Pierce* + *Slash* ⁽³⁾ | — | AOE · Burst |
| **LightShepherd** (The Shepherd) | Support | Pure · *Light* ⁽⁴⁾ | — | Heal · Cleanse |
| **Ranger** (The Hunter) | Ranged | Physical · *Pierce* | Debuffer · Escape | Debuff |
| **Treant** (The Thorn) | Tank | Physical · *Blunt* | Durable · Physical Ward · Distraction | AOE · Ramp |
| **WhirlMonk** (The Gale) | Bruiser | Physical · *Blunt* | Initiator | Dash · Teleport · Control · Burst |

⁽¹⁾ **Druid** — тычка просто колющая, БЕЗ сродства: «Яд» это сродство эффекта, а не тип урона
(см. [[gdd/20-combat/effects/poison|Отравление]]). Яд живёт в спорах-DoT; тег `Poison` из эффектов
в v1 не собирается (Combat-слой недоступен UI).
⁽²⁾ **Pyre** — *Fire* приходит из эффекта «Поджог» (Burn), не из автоатаки → в v1-теге его нет.
⁽³⁾ **IronSpearman** — эталон поисточниковости: автоатака *Pierce*, «Стальной вихрь» *Slash*.
⁽⁴⁾ **Shepherd** — редизайн 2026-07-25: атака светом = **Чистый** (True) + сродство *Light*,
лечит раненого союзника на 100% нанесённого (см. [[the-shepherd]]).

### Обоснование по карточкам (кратко)

- **Assassin** (Verdict) — «вырезает слабую цель и уходит из фокуса»: Escape; Изворотливость = Evasion; Добивание = Execute. **НЕ** Tank Buster (по Максу).
- **Cryomancer** (Winter) — заморозка→масс-стан: Control + AOE (масс-стан по всем Frozen); замедление/контроль угроз = Debuffer/Peel.
- **Defender** (Bulwark) — «держит линию, закрывает союзников»: Durable/Peel/Distraction; Оплот = Shield; Решительный удар = Control (Stun) + Debuff (−30% урон).
- **Druid** (Bloom) — **дебаффер + хилер через яды**: Взрыв спор = Heal за каждый уникальный яд + AOE; споры = DoT/Debuff (−скор.атаки). (Правка Макса учтена.)
- **FlameSwordsman** (Pyre) — поджоги+Угли+детонация: DoT · Ramp (Угли/скор.атаки) · Execute (детонация-добив) · Burst (Воспламенение). **Duelist** — его сила копится на конкретной цели («Угли» висят на ней), смена цели её обнуляет. Fire теперь приходит и из **самой автоатаки**: по горящей цели половина удара уходит Огнём (модель 2026-07-26/4), а не только из эффекта Burn.
- **Assassin** (Verdict) — **Duelist** добавлен той же меркой: размен один-на-один его стихия, а уклонение работает против одиночного бьющего, не против свалки.
- **IronSpearman** (Spear) — линия + Стальной вихрь ×3 вокруг: AOE · Burst. (Line — не тег, это форма.)
- **LightShepherd** (Shepherd) — **редизайн 2026-07-25**: атака светом (Чистый урон) лечит раненого союзника на 100% нанесённого + Длань жизни + клинз тир≤2: Heal · Cleanse.
- **Ranger** (Hunter) — стрельба на ходу + Метка (+25% получаемого урона на цели): Debuffer (метка-уязвимость на враге) · Escape (кайт); Метка = Debuff.
- **Treant** (Thorn) — **танк от физброни + AOE вокруг** (правка Макса): Physical Ward · Durable · Distraction; шипы = AOE; Разрастание = Ramp. **НЕ** Summon.
- **WhirlMonk** (Gale) — рывок+отбрасывание+телепорт-в-спину: Initiator; Dash · Teleport · Control (Knockback+оглушение) · Burst (телепорт ×2).

### Теги от улучшений (динамические, при прокачке)

- **Treant** — «Ядовитые шипы» (T1) → добавляет `Poison` + `DoT`.
- **FlameSwordsman** — «Пылающие разрезы» (T1) → сдвигает в `Ranged` (дальник); «Исцеляющий огонь» → `Heal`.

## Решения (утверждены Максом 2026-07-24)

1. **Физ-подтип** (*Blunt/Slash/Pierce*): **завести поле подтипа в данных** — тег станет авто (и
   подтип сможет влиять на урон позже). Проставить героям из карточек.
2. **FlameSwordsman / IronSpearman** — **без Playstyle** (чистые ДД-инструменталы, читаются из Role+Mechanic).
3. **Ranger = Debuffer** (метка — уязвимость на враге), не Buffer.

## Решения рефактора модели урона (2026-07-25)

4. **Тип урона — поисточниковый**, не «один на юнита»: автоатака / каждая способность / каждый DoT
   несут свой (`PhysicalSubtypeOverride`, `MagicElementOverride` на `AbilityData`).
5. **«Стихийная» школа и броня → «Магическая»** (возврат к исходному `Magic*`); добавлен элемент
   **Аркана** (чистая магия без стихии, механика позже).
6. **Яд — сродство, не тип урона.** Убран с автоатаки Друида; DoT спор переведён с `True` на
   `Physical` (правило «яд гасится бронёй по своей школе»).
7. **Shepherd — носитель Света:** Чистый урон + хил союзнику от нанесённого (`AllyMendComponent`).
