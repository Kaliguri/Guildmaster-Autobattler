# Реализация всех врагов — рабочий журнал

> Живой журнал большого захода: прописать и реализовать всех задизайненных врагов.
> Ветка: `feat/slice-feel-and-content`. Started 2026-07-31.
> Не путать с каноном: решения по дизайну — в `docs/wiki/gdd/00-meta/journal-adr.md`,
> инженерные — в `docs/wiki/tech/00-meta/journal/`. Этот файл — оперативная ведомость сессии.

## Мандат (от Макса, 2026-07-31)

- Реализовать **всех** записанных сейчас противников, не только 6 свежих голых ассетов.
- Есть куча **старых** задизайненных врагов вообще без ассетов — их тоже.
- Порядок двухфазный: **сначала всех прописать, потом всех реализовать.**
- Работа автономная, параллельно с другими сессиями.

### Развилки, закрытые Максом

1. **Scope:** все обычные враги. Боссы — отдельный заход позже (им нужны системы, которых в движке нет: стадии, чтение состава, катсцены, воскрешения, подкрепления).
2. **Новый код:** чистые локальные механики — кодить (слепота детерминированная, антихил-шкала, швырок союзника). Мана-дрейн — **отложить** (завязан на нерешённый вопрос Арканы). Врагов, зависящих от отложенного, пометить в `implementation-status`.
3. **Проказники** (гоблины белая/синяя повязка, карточек нет): провести дизайн-проход самой по правилам content-design, завести карточки, реализовать; дизайн-решения вынести на приёмку Максу.

## Состояние движка (разведка 2026-07-31)

Движок **богатый и data-driven**. Новый враг из стандартного набора собирается целиком из данных, код не нужен.

- 51 компонент эффектов: яд/DoT (`PeriodicDamageComponent`), все стат-дебаффы (`StatModifierComponent`), контроль (`ControlComponent`: PreventAct/Move/Cast), сон, заморозка (3 ступени), щиты, шипы, вампиризм, огонь (угли/поджог/детонация), метки, уклонение/парирование, стелс (`ConcealmentComponent`, 4 ступени), диспел, призыв на старте.
- Способности (`AbilityData` + `AbilitySystem`): каст-тайм, каналы, AOE (круг/линия), масс-по-тегу, ауры, залпы, призыв, смещение.
- Displacement (`DisplacementSystem`): шаговое смещение, «ядро» (бьёт на сегменте), удар о стену (+урон+стан), цепные толчки.
- Summons (`SummonSystem` + `RuntimeUnitFactory.CreateSummon`): срок жизни, DiesWithSummoner, лимит.
- AI (`ProfileBrain` из `AIProfile`): Filter→Score→Override; таргет-режимы; кайт/отступление/подход.

Путь врага: `EnemyData` SO → `EncounterData` (по строковому id) → `EncounterLoader` → `RuntimeUnitFactory.Create` → тик-цикл. **Прописать врага = дата-операция.**

### Ограничения движка (где нужен код или где враг будет неполным)

- **AI выбора каста примитивен** — кастуется первая готовая активка по порядку (`AbilitySystem.cs:111`, помечено как «плейсхолдер Фазы 3»). Для командира/мага с несколькими активками сыграет тупо. Возможная работа: приоритизация каста.
- **Слепота** — CC-примитива нет. Нужен новый компонент (детерминированный промах каждую X-ю атаку; контра «Меткость»). Журнал ГД запись №37.
- **Антихил** (снижение входящего хила 30/50/75/100%) — компонента нет, нужен новый.
- **Швырок союзника** (командир, наездник: хватает соседнего мили-гоблина и кидает как снаряд) — displacement есть, но «живой снаряд из союзника» вероятно нужен код.
- **Мана-дрейн** — ОТЛОЖЕН (вопрос Арканы).
- Нет taunt/fear/charm как отдельных механик.

## Ведомость врагов (не-боссы)

Статусы: `asset+card` есть ассет и карточка; `card-only` карточка есть, ассета нет; `asset-bare` ассет голый; `none` ничего.
Колонка «прописать» — нужно ли создать/дополнить ассет. «код» — требуется ли новый код.

| id | Титул | Фракция | Состояние | Прописать | Механика | Код |
|---|---|---|---|---|---|---|
| enemy.goblin_grunt | Гоблин с палкой | goblins | asset-bare | статы ок, навыков нет | — | нет |
| enemy.goblin_warrior | Гоблин-воин | goblins | asset-bare | навесить барьер 50 | Оплот-барьер | нет |
| enemy.goblin_cutthroat | Гоблин-убийца | goblins | asset-bare | скрытность+урон в спину | стелс старт, rear-strike | нет |
| enemy.goblin_archer | Гоблин-лучник | goblins | asset-bare | статы ок, кайт | — | нет |
| enemy.goblin_commander | Гоблин-командир | goblins | card-only | создать ассет | клич-бафф, парирование, швырок | ДА (швырок) |
| enemy.goblin_shaman | Гоблин-маг | goblins | card-only | создать ассет | огн.снаряд AOE каст, барьер союзнику, огн.сферы | нет? |
| enemy.goblin_wolfrider | Наездник на волке | goblins | card-only | создать ассет | таран насквозь, швырок в тыл | ДА (швырок) |
| enemy.bandit_bruiser | Разбойник с молотом | bandits | card-only | создать ассет+вид | стан каждой 2-й атаки, побег | нет |
| enemy.bandit_shieldman | Разбойник со щитом | bandits | card-only | создать ассет | барьер 100 ×2, побег | нет |
| enemy.bandit_venombow | Отравл. арбалет | bandits | card-only | создать ассет | ядовитый DoT + антихил 50%, побег | ДА (антихил) |
| enemy.bandit_warlock | Разбойник-маг | bandits | card-only | создать ассет | метка (рут+20% урона), хил-отступление союзника, побег | нет? |
| enemy.pack_wolf | Волк стаи | beasts | card-only | создать ассет+вид | +15% урона за союзника в радиусе | нет? |
| enemy.earth_golem | Земляной голем | golems | card-only | создать ассет+вид | цикл 3 удара (AOE, отброс+замедл), щит физ.1000 | нет? |
| enemy.skeleton_swordsman | Скелет-мечник | undead? | asset-bare | сверить карточку | ? | нет |
| enemy.training_dummy | Манекен | — | asset-bare | без карточки намеренно | — | нет |
| гоблин-проказник (белый) | ? | goblins | none | дизайн+карточка+ассет | слепота 3с/8с | ДА (слепота) |
| гоблин-проказник (синий) | ? | goblins | none | дизайн+карточка+ассет | мана-дрейн канал | ОТЛОЖЕН |

## Разбивка на тиры (после разведки полноты 2026-07-31)

### Тир A — механика проработана, берём в заход

Все 13 карточных + проказники (делегированы) + скелет базово.

- Гоблины: grunt, warrior, cutthroat, archer (довести ассеты); commander, shaman, wolfrider (создать).
- Разбойники: bruiser, shieldman, venombow, warlock (создать + завести вид `Bandits`).
- Звери: pack_wolf (создать + вид `Beasts`).
- Големы: earth_golem (создать + вид `Golems`).
- Проказники белый/синий (дизайн → карточка → ассет; синий с отложенным дрейном).
- skeleton_swordsman: свести карточку, базовая роль.

### Тир B — только заявка, дизайна нет. ОТЛОЖЕНО (не дизайню без Макса)

Фиксируется в `enemies/implementation-status.md` как «ждёт дизайна». Реализовать нельзя без дизайн-прохода; дизайн новых сущностей — прерогатива Макса.

- **Разбойник-дуэлянт** — парирование (примитив `effect.parry` есть) + щит 200/3с на парир + +50% урона при одном враге рядом.
- **Разбойник-убийца** — колющий + 30% как кровь/5с + Concealment (стелс-тир есть).
- **Разбойник — стилер предметов** — стаб; механики кражи предметов в движке нет.
- **Друиды-кураторы** — новый глагол «контроль стаи»; отдельный вид людей-друидов, пул зверь+друид.
- **Нежить «Призрачный Корован»** — ось есть (смерть-как-ресурс, лёд/тьма, скелет +урон от дробящего/−20% от колющего), росписи юнитов нет. Голый `skeleton_swordsman` — де-факто материал.

### За пределами захода целиком

- Все 3 босса (goblin-warband, bandit-nest, deserter-army) + именованные боссы (Сорель, Алдис, слепой друид, «Он/Она» гнёзд) — нужны системы стадий/чтения состава/катсцен/подкреплений/воскрешений.
- Deserter-подвиды (кавалерия/лучники/разведчики) — часть боссового вида.
- Dev-фикстуры (`bone_dev`, `training_dummy`, `enemy.test`) — не ростер.

## План сборки (числа СТАРТОВЫЕ, под balance-замер)

Классовая база: `_baseHp 2000`, `_baseMove 3`, `_baseDps 110`. Класс даёт ТОЛЬКО HP и скорость (все ArmorBudget=0), поэтому **урон (Stat7), скорость атаки (Stat8), броня (Stat3/4), дальность (Stat9), скорость снаряда (Stat18) задаются per-unit `_stats` Override** — как у существующих гоблинов.

Классы (UnitClass): 0 Брузер(HP×1/Move×1/DpsNorm×1), 1 Танк(1.5/0.85/0.5), 2 Убийца(0.75/1.1/1.4), 3 РДД(0.65/0.75/1.2), 4 Поддержка(0.65/0.75/0.5).
StatType: 0 MaxHP, 3 PhysArmor, 4 MagicArmor, 7 AutoAttackDamage, 8 AttackSpeed, 9 AttackRange, 18 ProjectileSpeed, 20 MoveSpeed. Op: 2 PercentMult, 3 Override. DamageType: 1 Blunt, 2 Slash, 3 Pierce, 10 Fire, 21 Dark(True). CreatureType: 0 Living, 1 Undead, 2 Construct (Beast — свериться в CombatCategories.cs). AttackType: 0 Melee, 1 Ranged.

### Виды (species.*) — создать 3, scalers Op2 (PercentMult)

| Вид | MaxHP | MoveSpeed | Прим. |
|---|---|---|---|
| species.goblins (есть) | −0.6 (×0.4) | +0.1 | эталон «серой массы» |
| species.bandits | −0.3 (×0.7) | 0 | «сильнее серой массы» |
| species.beasts | −0.5 (×0.5) | +0.2 | быстрые, хрупкие |
| species.golems | +1.0 (×2.0) | −0.4 | тяжёлые, стойкие |

### EnemyData — стартовые per-unit числа (Override, кроме MaxHP-бампа элит)

| id | Класс | Creature | AA тип / Attack | AA dmg(7) | AS(8) | Armor(3/4) | Range(9)/Proj(18) | MaxHP override | threat |
|---|---|---|---|---|---|---|---|---|---|
| goblin_commander | Брузер | Living | Slash / Melee | 75 | 0.8 | 10 | — | 1600 (элита-бамп) | 5 |
| goblin_shaman | РДД | Living | Fire / Ranged | 90 | 0.5 | 0 | 7 / 8 | 450 | 5 |
| goblin_wolfrider | Убийца | Living | Pierce / Melee | 70 | 0.9 | 6 | — | 2400 | 5 |
| bandit_bruiser | Брузер | Living | Blunt / Melee | 90 | 0.7 | 8 | — | — (1400) | 3 |
| bandit_shieldman | Танк | Living | Slash / Melee | 65 | 0.9 | 20 | — | — (2100) | 3 |
| bandit_venombow | РДД | Living | Pierce / Ranged | 60 | 0.9 | 4 | 6 / 10 | — (910) | 3 |
| bandit_warlock | Поддержка | Living | Dark(True) / Ranged | 55 | 0.7 | 3 | 6 / 10 | — (910) | 5 |
| pack_wolf | Убийца | Beast? | Pierce / Melee | 30 | 1.2 | 0 | — | — (750) | 1 |
| earth_golem | Танк | Construct | Blunt / Melee | 120 | 0.4 | 40/0 | — | — (6000) | 3 |

MaxHP в скобках — вычисленный из каскада (не override). Элитам гоблинов override HP, т.к. видовой ×0.4 душит элиту.

### Механика на каждого (Фаза 2) — блоки из каталога

- goblin_warrior: новый ассет `effect.goblin_bulwark` = ShieldComponent{Base 50, stacking Stack, max 1, cooldown-через-rearm}. → `_grantedEffects`.
- goblin_cutthroat: `StealthPassive` (есть) в `_grantedEffects`; rear-strike — новый `effect.cutthroat_backstab` = RearStrikeEffectComponent{bonusEffect: доп-урон}. Либо ConcealmentComponent tier 2/3.
- goblin_commander: клич — новый AbilityData{targetMode 5 AlliesInRadius, areaRadius, effects:[новый бафф AS+Move]}; парирование — `Parry` (есть) в granted; швырок — КОД.
- goblin_shaman: огн.снаряд — AbilityData{castSeconds 1, Fire, Circle AOE}; барьер союзнику — AbilityData{targetMode LowestHpAlly, healEffect: shield}; огн.сферы — новый компонент или EveryNth+charge (оценить).
- goblin_wolfrider: разгон — displacement «ядро» (AbilityData _displaces + Cannonball) на старте; швырок в тыл — КОД.
- bandit_bruiser: каждая 2-я атака стан — EveryNthAttackComponent{period 2, charge: Empower + MicroStun/ShatterStun 1с}.
- bandit_shieldman: барьер 100×2 — новый `effect.bandit_bulwark` = ShieldComponent{Base 100, stacking Stack, max 2}.
- bandit_venombow: `_autoAttackEffects:[новый ядовитый DoT + антихил]`; антихил 50% — КОД (компонент).
- bandit_warlock: метка — новый `effect.warlock_mark` = StatModifier DamageTaken +0.2 + ControlComponent move (рут); хил-отступление — AbilityData{targetMode LowestHpAlly, healEffect}.
- pack_wolf: бонус стаи — новый компонент (динамика по числу союзников в радиусе) ИЛИ SelfStack — оценить. КОД вероятно.
- earth_golem: цикл 3 удара — EveryNthAttackComponent{period 3, charge: Empower knockback + WhirlSlow}; щит физ — AbilityData{selfEffects:[SchoolShield phys 1000], cooldown 15}.

### Ограничения, помечаемые вслух

- **Визуал (_visual/_viewPrefab):** арта для разбойников/зверей/големов НЕТ. Оставляю пустым (headless-верификация не требует). Новым гоблинам — временно гоблинский плейсхолдер. **Визуал — за Максом/PixelLab.**
- **Побег разбойников при 30% HP:** «выйти из боя совсем» в движке НЕТ (impl-status). AI-порог отступления (_retreat) есть — фракция играбельна, но без полного побега. Помечено.
- **Огн.сферы мага и бонус стаи волка:** возможно требуют нового компонента — решение в Фазе 2 после оценки.

## Ловушка каскада статов (найдено при пилоте, 2026-07-31)

Формула `Stats.cs`: `(base + ΣFlat) × (1+ΣPercentAdd) × Π(1+PercentMult)`, base = последний Override. Видовой `PercentMult` умножает ФИНАЛ → override HP юнита им срезается. Гоблины ×0.4 HP: override 450 у мага дал 180.

**Следствие:** элиты гоблинов не пробивают видовой ×0.4 (ни override, ни flat — flat тоже под множителем). Убрала override, элиты на класс×вид: командир 800, маг 520, наездник 600. Но карточка наездника говорит «HP МНОГО» — **расхождение с дизайном**. Дом: `enemies/implementation-status`. Чистое решение (за Максом/balance): элитный подвид с компенсирующим PercentMult / пересмотр видового множителя / индивидуальный HP-путь. НЕ костылём «override = желаемое/0.4». Память: [[stat-cascade-species-mult-caps-elites]].

Проверка статов без DI: `StatMath.BuildEffective(data, statsConfig, classBalanceConfig)`.

## Прогресс

- [x] Разведка: дизайн врагов, SO-ассеты, боевой код.
- [x] Разведка полноты: найдены Тир B (3 разбойника, друиды, нежить-роспись) — отложены.
- [x] Каталог блоков + классовые базы + план сборки.
- [x] **Фаза 1: прописано.** 3 вида (Bandits/Beasts/Golems), 5 AI-пресетов (BanditMelee/Ranged/Support, BeastFlanker, GolemDefender), 9 EnemyData (commander/shaman/wolfrider, 4 bandits, pack_wolf, earth_golem). Зарегистрированы в ContentDatabase (231 деф). Каскад статов проверен StatMath. Визуал разбойников/зверя/голема пуст (за Максом), новым гоблинам гоблинский плейсхолдер.
- [ ] Фаза 1-док: frontmatter карточек paper→partial, implementation-status. ← ТЕКУЩЕЕ
- [ ] Фаза 2: реализовать механику + новый код (слепота, антихил, швырок).
- [ ] Верификация в бою.

### Реестр созданного (Фаза 1)

Виды: `Species/Bandits.asset` (×0.7 HP), `Beasts.asset` (×0.5 HP, ×1.2 Move), `Golems.asset` (×2.0 HP, ×0.6 Move).
Пресеты: `AiPresets/{BanditMelee,BanditRanged,BanditSupport,BeastFlanker,GolemDefender}.asset`.
Враги: `Enemies/{GoblinCommander,GoblinShaman,GoblinWolfrider,BanditBruiser,BanditShieldman,BanditVenombow,BanditWarlock,PackWolf,EarthGolem}.asset`.
Итоговый HP (StatMath): golem 6000, shieldman 2100, bruiser 1400, commander 800, wolf 750, shaman 520, wolfrider 600, venombow 910, warlock 910.
