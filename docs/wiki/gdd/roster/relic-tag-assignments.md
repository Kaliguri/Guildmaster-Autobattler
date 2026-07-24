---
title: "Roster - Relic Tag Assignments"
order: 6
status: needs_review
updated: 2026-07-24
---

# Предложенное распределение тегов по реликвиям

> **ЧЕРНОВИК на проверку Макса.** Теги по [[unit-tag-glossary|глоссарию]], сверено с **дизайн-
> карточками** (`relics/*`, их `mechanics`/`roles`) И фактическим kit кода (активки inline).
> `Role`/`DamageType` — **авто** (для контекста). `Playstyle`/`Mechanic` — предложение.

| Реликвия (карточка) | Role | DamageType | Playstyle | Mechanic |
|---|---|---|---|---|
| **Assassin** (The Verdict) | Assassin | Physical · *Pierce* | Escape | Stealth · Evasion · Execute · Burst |
| **Cryomancer** (The Winter) | Ranged | Magical · *Ice* | Debuffer · Peel | Control · AOE |
| **Defender** (The Bulwark) | Tank | Physical · *Slash* | Durable · Peel · Distraction | Shield · Control · Debuff |
| **Druid** (The Bloom) | Support | Magical · *Poison* | Debuffer | DoT · Heal · AOE · Debuff |
| **FlameSwordsman** (The Pyre) | Bruiser | Physical · *Slash* + *Fire* | — | DoT · Ramp · Execute · Burst |
| **IronSpearman** (The Spear) | Bruiser | Physical · *Pierce* | — | AOE · Burst |
| **LightShepherd** (The Shepherd) | Support | Magical · *Light* | — | Heal · Cleanse |
| **Ranger** (The Hunter) | Ranged | Physical · *Pierce* | Debuffer · Escape | Debuff |
| **Treant** (The Thorn) | Tank | Physical · *Blunt* | Durable · Physical Ward · Distraction | AOE · Ramp |
| **WhirlMonk** (The Gale) | Bruiser | Physical · *Blunt* | Initiator | Dash · Teleport · Control · Burst |

### Обоснование по карточкам (кратко)

- **Assassin** (Verdict) — «вырезает слабую цель и уходит из фокуса»: Escape; Изворотливость = Evasion; Добивание = Execute. **НЕ** Tank Buster (по Максу).
- **Cryomancer** (Winter) — заморозка→масс-стан: Control + AOE (масс-стан по всем Frozen); замедление/контроль угроз = Debuffer/Peel.
- **Defender** (Bulwark) — «держит линию, закрывает союзников»: Durable/Peel/Distraction; Оплот = Shield; Решительный удар = Control (Stun) + Debuff (−30% урон).
- **Druid** (Bloom) — **дебаффер + хилер через яды**: Взрыв спор = Heal за каждый уникальный яд + AOE; споры = DoT/Debuff (−скор.атаки). (Правка Макса учтена.)
- **FlameSwordsman** (Pyre) — поджоги+Угли+детонация: DoT · Ramp (Угли/скор.атаки) · Execute (детонация-добив) · Burst (Воспламенение). Fire — из эффекта Burn, не из школы.
- **IronSpearman** (Spear) — линия + Стальной вихрь ×3 вокруг: AOE · Burst. (Line — не тег, это форма.)
- **LightShepherd** (Shepherd) — автоатаки лечат + Длань жизни + клинз тир≤2: Heal · Cleanse.
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
