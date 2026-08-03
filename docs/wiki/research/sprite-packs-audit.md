---
title: Аудит спрайт-паков (Art/Sprites)
status: draft
updated: 2026-07-19
---

**Статус:** 🟡 Draft

Аудит анимированных паков в `Assets/_Project/Art/Sprites` на пригодность к бою (автобаттлер). Оценка по листам/кадрам **без прогона в Play**: наличие циклов, полнота набора, читаемость силуэта, настроение.

Связанный документ: [[sprite-recommendations|Рекомендации спрайтов под персонажей ГДД]].

---

## Метод и исключения

**Включено:** паки с боевым минимумом анимаций (хотя бы Idle/Ready + Walk/Run + Attack; желательно Hit/Death).

**Исключено / только пометка:**

| Что | Почему |
|---|---|
| `Characters 500+ (by batareya)` | Статичные портреты, без боевых циклов |
| `Goblin Monsters 4 Colors` | Сетка статичных силуэтов классов (~10 поз), не анимации |
| `Nneshan` | Статичная копейщица, без циклов |
| `vagabond` | Один кадр jump/landing, не боевой набор |
| `Martial Hero 3(1)` | Дубликат папки `Martial Hero 3` |
| `Monster_Creatures_Fantasy(Version 1.3)` в обеих корневых папках | Только `Attack3` + снаряд — **неполный** фрагмент |

**Дубликаты:** `Martial Hero 2` одинаков в `Pixel Art Heroes` и `New FREE Pixel Art Heroes`. `Monster_Creatures_Fantasy(Version 1.3)` тоже продублирован.

**Стилевые семьи (для склейки ростера):**

1. **LuizMomo / «Heroes»** — крупные листы ~100–250 px высоты, богатый idle, Jump/Fall (платформерный багаж). Хорошо читаются в бою.
2. **Ansimuz / Tiny RPG** — chibi ~100×100, простые циклы, «cute-retro».
3. **Per-frame low-fantasy** (viking/skeleton/loreon/imp) — мелкие кадры ~50–100 px, мрачнее, dungeon-toned.
4. **Bandits / AxeWarriors** — средне-крупные per-frame, западный fantasy / викинг.

Число кадров — оценка по размеру листа (ширина ÷ высота кадра) или по числу файлов в папке анимации; у нестандартной нарезки — «≈».

---

## `Pixel Art Heroes`

### EVil Wizard 2 — 1 юнит

**Внешний вид:** высокий тёмный колдун в фиолетово-золотых робах с капюшоном; посох с магическим пламенем. Мрачный, «босс-маг».

#### Юнит: Evil Wizard 2

1. **Анимации:** Idle 8, Run 8, Attack1/2 по 8, Take hit 3, Death 7, Jump/Fall по 2. Качество высокое (плавный idle, читаемый посох).
2. **ГДД:** [[the-bonewright|The Bonewright]], [[the-winter|The Winter]], [[the-storm|The Storm]], [[bandit-warlock|Bandit Warlock]]. Не для мили-танков.
3. **Сеттинг:** тёмный маг, лич-эстетика, культист, элитный кастер акта.

---

### Fantasy Warrior — 1 юнит

**Внешний вид:** воин с длинными белыми волосами, бирюзовая туника, тёмно-синий плащ, изогнутый меч. Dark-heroic.

#### Юнит: Fantasy Warrior

1. **Анимации:** Idle 10, Run 8, Attack1–3 (7/7/8), Take hit 3, Death 7, Jump/Fall по 3. Очень полный набор, высокий polish.
2. **ГДД:** [[the-pyre|The Pyre]], [[the-paragon|The Paragon]], [[the-verdict|The Verdict]] (частично). Слабо для копья/лука/магии.
3. **Сеттинг:** элитный мечник, «избранный», антигерой, капитан гильдии.

---

### Huntress — 1 юнит

**Внешний вид:** охотница в оливковом капюшоне, копьё с крупным наконечником, жёлтые глаза в тени. Есть отдельные спрайты копья.

#### Юнит: Huntress (копьё)

1. **Анимации:** Idle 8, Run 8, Attack1–3 (5/5/7), Take hit 3, Death 8, Jump/Fall по 2 + Spear/Spear move. Хорошее качество; копьё — сильный силуэт.
2. **ГДД:** лучший матч [[the-spear|The Spear]]; также [[the-hunter|The Hunter]] (если трактовать как melee-hunter), [[the-warden|The Warden]] (эстетика леса/охоты).
3. **Сеттинг:** амазонка, страж границы, охотница на зверей, эльфийский авангард.

---

### Huntress 2 — 1 юнит (+ стрела)

**Внешний вид:** белоголовая лучница в зелёной тунике, лук и колчан. Лесная палитра.

#### Юнит: Huntress 2 (лук)

1. **Анимации:** Idle 10, Run 8, Attack 6, Get Hit 3, Death 10, Jump/Fall по 2 + Arrow Move/Static. Полный боевой набор, качество высокое.
2. **ГДД:** топ для [[the-hunter|The Hunter]]; также [[bandit-venombow|Bandit Venombow]], [[goblin-archer|Goblin Archer]] (если перекрасить — слабее, человек).
3. **Сеттинг:** рейнджер, эльфийка-лучница, лесной скаут, охотница гильдии.

---

### Martial Hero 2 — 1 юнит *(дубликат в New FREE)*

**Внешний вид:** боец в синем кимоно, красная oni-маска, катана, чёрный хвост.

#### Юнит: Martial Hero 2

1. **Анимации:** Idle 4, Run 8, Attack1/2 по 4, Take hit 3, Death 7, Jump/Fall по 2. Чуть короче idle/атак, чем у соседей по линейке; силуэт отличный.
2. **ГДД:** [[the-gale|The Gale]] (восточная тема); частично [[the-verdict|The Verdict]]. Не для европейского рыцаря/жреца.
3. **Сеттинг:** монах-убийца, самурай, маскированный дуэлянт, «восточный» акт.

---

### Medieval Warrior Pack 2 — 1 юнит

**Внешний вид:** безоружный боец в красной тунике, стойка боевых искусств (кулаки/тело).

#### Юнит: Medieval Warrior 2

1. **Анимации:** Idle 8, Run 8, Attack1–4 по 4, Take Hit 4 (+ white silhouette), Death 6, Jump/Fall по 2. Много вариантов удара — плюс для комбо-читаемости.
2. **ГДД:** сильный матч [[the-gale|The Gale]]; слабее для танков с щитом/оружием.
3. **Сеттинг:** монах, боксёр арены, уличный боец, ученик ордена без оружия.

---

### Medieval Warrior Pack 3 — 1 юнит

**Внешний вид:** светловолосый воин, navy-одежда, серебряные наплечники, огромный двуручник.

#### Юнит: Medieval Warrior 3

1. **Анимации:** Idle 10, Run 6, Attack1–3 (4/4/5), Get Hit 3, Death 9, Jump/Fall по 2. Качество высокое; бег чуть короче.
2. **ГДД:** [[the-pyre|The Pyre]], [[the-paragon|The Paragon]], [[the-bulwark|The Bulwark]] (если принять двуручник вместо «щитового» танка), [[bandit-bruiser|Bandit Bruiser]].
3. **Сеттинг:** паладин без шлема, чемпион арены, элитный наёмник.

---

### Monsters Creatures Fantasy 2 — 4 юнита

**Внешний вид семьи:** пиксельные данжен-мобы, единый стиль, хорошая читаемость на малом масштабе.

#### Bat

1. **Анимации:** fly, attack, hurt, death, fall, fly-to-fall (по листам, fly ≈12). Качество хорошее.
2. **ГДД:** ни на реликвию напрямую; саппорт/аддон к некроманту или элитный летучий моб. Не [[pack-wolf|Pack Wolf]].
3. **Сеттинг:** склеп, ночной биом, призванный familiar, мелкий летающий враг.

#### Mimic

1. **Анимации:** Idle_closed/open/transformed, opening, transform, walk, attack_1/2, hurt, death — очень богатый набор.
2. **ГДД:** ни на текущего героя; сильный кандидат на ивент-врага / сундук-ловушку (пока нет в ростере врагов).
3. **Сеттинг:** данж-мимик, проклятый сундук, сюрприз-элит.

#### Rat

1. **Анимации:** idle, run, attack_bite, hurt, rat-death. Достаточно для мелкого моба.
2. **ГДД:** не текущие фракции; скорее filler-моб / summon.
3. **Сеттинг:** канализация, зараза, рой.

#### Slime (в этом паке)

1. **Анимации:** idle, walk, attack, hurt, death. Базовый моб-набор.
2. **ГДД:** нет прямого; см. также большой slime-пак ниже.
3. **Сеттинг:** стартовый слайм, грибная пещера, природа-акт.

---

### Monster_Creatures_Fantasy (Version 1.3) — 4 «юнита» *(неполный)*

Только `Attack3` + projectile/bomb/sword. **Для боя не готов** (нет Idle/Run/Hit/Death). Имеет смысл только как референс силуэта или если добрать полный пак с itch/LuizMomo.

Юниты по виду: Flying eye, Goblin (с бомбой), Mushroom, Skeleton — классические данжен-мобы в том же стиле, что Fantasy 2.

---

## `New FREE Pixel Art Heroes`

### ArcherHero — 1 юнит

**Внешний вид:** минималистичный лучник в бирюзовом капюшоне, один «глаз», тёмный лук. Скрытный, graphic-novel.

#### Юнит: ArcherHero

1. **Анимации:** Idle+Run на одном листе (мало кадров idle), Normal/High/Low Attack, Dash, Jumping, death. Набор есть, но **короче и грубее**, чем Huntress 2; нарезка нестандартная.
2. **ГДД:** запасной для [[the-hunter|The Hunter]], [[the-verdict|The Verdict]] (капюшон/скрытность), [[bandit-venombow|Bandit Venombow]].
3. **Сеттинг:** ассасин-лучник, ночной охотник, «безликий» рейнджер.

---

### AxeWarriors — 1 архетип × 3 палитры

**Внешний вид:** мускулистый викинг с бородой, топор на плечах, безрукавка. Варианты: **Blond / Brown / Orange** (цвет волос/акцентов) — один дизайн.

#### Юнит: Axe Warrior

1. **Анимации (per-frame):** Idle 6, Run 11, Attack 13, Hurt 3, Death 12 (+ NoBlood), Jump 5. Очень полный и «жирный» набор атак/смерти. Качество высокое.
2. **ГДД:** топ для [[the-draugr|The Draugr]] (если живой викинг / до нежити); также [[the-runesmith|The Runesmith]] (слабо — нужен молот), [[bandit-bruiser|Bandit Bruiser]].
3. **Сеттинг:** северный рейдер, берсерк, варвар, чемпион арены с топором.

---

### Bandits — 2 юнита

**Внешний вид:** Light Bandit — борода, светлая туника, меч; Heavy Bandit — синяя туника, капюшон, более широкий рубака. Землястая палитра, «наёмники».

#### Light Bandit

1. **Анимации:** Idle 4, Combat Idle 4, Run 8, Attack 8, Hurt 2, Recover 8, Jump 1, Death 1. Атака/бег хорошие; **Death слабый** (1 кадр).
2. **ГДД:** [[bandit-shieldman|Bandit Shieldman]] / общий бандит; также [[the-pyre|The Pyre]] (рискованный мечник), [[the-verdict|The Verdict]].
3. **Сеттинг:** лёгкий разбойник, гильдейский новобранец, уличный дуэлянт.

#### Heavy Bandit

1. **Те же анимации**, что у Light. Силуэт тяжелее.
2. **ГДД:** [[bandit-bruiser|Bandit Bruiser]], [[the-bulwark|The Bulwark]] (без щита — компромисс), [[the-runesmith|The Runesmith]] (если принять как «работягу»).
3. **Сеттинг:** старший бандит, наёмный громила, сержант шайки.

---

### Evil Wizard — 1 юнит

**Внешний вид:** более простой/ранний тёмный маг (предшественник Wizard 2/3). Фиолетовая магия.

#### Юнит: Evil Wizard (v1)

1. **Анимации:** Idle 8, Move 8, Attack 8, Take Hit 4, Death 5. Достаточно для кастера; меньше вариативности, чем у Wizard 2/3.
2. **ГДД:** [[the-winter|The Winter]], [[the-storm|The Storm]], [[bandit-warlock|Bandit Warlock]] — запасной вариант.
3. **Сеттинг:** младший колдун, культист, элит шайки.

---

### Evil Wizard 3 — 1 юнит

**Внешний вид:** бледный маг с белым хвостом, красное пальто, посох с черепом. Некромантская эстетика + снаряды.

#### Юнит: Evil Wizard 3

1. **Анимации:** Idle 10, Walk/Run по 8, Attack 13, Get hit 3, Death 18, Jump/Fall по 3 + Projectile Moving/Explode. Один из **самых полных** кастер-наборов.
2. **ГДД:** топ для [[the-bonewright|The Bonewright]]; также [[bandit-warlock|Bandit Warlock]], тёмный вариант [[the-storm|The Storm]] / [[the-winter|The Winter]].
3. **Сеттинг:** некромант, лич-ученик, военный чёрный маг, элитный босс-кастер.

---

### Forest_Monsters_FREE — 1 юнит (×2 варианта VFX)

**Внешний вид:** живой мухомор (красная шляпка, бежевое тело). Cute-evil. Варианты **with VFX / without VFX**.

#### Mushroom

1. **Анимации:** Idle ≈9, Run 10, Attack ≈13, AttackWithStun 30, Hit ≈6, Die ≈19, Stun ≈23. Очень насыщенно; качество анимации высокое для моба.
2. **ГДД:** сильный визуальный якорь для [[the-bloom|The Bloom]] (грибной друид — сам друид человек не идеален, но гриб = summon/альт-форма/враг-синергия); также природа-моб под Bloom.
3. **Сеттинг:** лесной акт, споры, «милый» элит, summon друида.

---

### FREE_Kobold Warrior — 1 юнит × 2 стиля контура

**Внешний вид:** синий кобольд с большими ушами и хвостом, кинжал, шарф. Варианты **with_outline / without_outline**.

#### Kobold Warrior

1. **Анимации:** IDLE ≈9, RUN ≈12, ATTACK 1 ≈8. **Нет Death/Hit** — для боя дырка; можно заглушить.
2. **ГДД:** ближе к [[goblin-cutthroat|Goblin Cutthroat]] / мелкий гоблин, чем к герою; не точный гоблин (кобольд).
3. **Сеттинг:** кобольды как отдельная фракция, шахты, «милый» скирмишер.

---

### Goblin_Fighter — 1 юнит

**Внешний вид:** классический зелёный гоблин с большим изогнутым мечом, наплечники. Один атлас-лист.

#### Goblin Fighter

1. **Анимации:** на одном листе 1024×640 — idle/run/attack/death (нужна ручная нарезка). По виду smear в атаке хороший; Death присутствует.
2. **ГДД:** [[goblin-warrior|Goblin Warrior]], [[goblin-grunt|Goblin Grunt]] (апскейл), [[goblin-commander|Goblin Commander]] (если добавить аксессуар).
3. **Сеттинг:** стандартный melee-гоблин фракции.

---

### Goose / Wizardgooseassets — 1–2 «юнита»

**Внешний вид:** белый гусь; wizard-гусь с яйцами/«turd»-FX (мемный пак).

#### Goose

1. **Анимации:** Idle 2, Walk/Run/Flap по 4. Минимально.
2. **ГДД:** ни на кого из канона. Только ивент/пасхалка.
3. **Сеттинг:** комедийный ивент, питомец.

#### Wizard Goose

1. **Анимации:** Idle 11, Run 12, Dash, EggBlast/EggBomb, Lay, Turd* FX. Полнее обычного гуся.
2. **ГДД:** нет.
3. **Сеттинг:** мем-босс, joke encounter.

---

### Hero Knight 2 — 1 юнит

**Внешний вид:** рыцарь: серебряный шлем, красная туника, синие наплечники, меч. Классический «герой-солдат».

#### Hero Knight 2

1. **Анимации:** Idle 11, Run 8, Attack 6, Dash 4, Take Hit 4, Death 9, Jump/Fall по 4. Полный набор, высокое качество.
2. **ГДД:** [[the-bulwark|The Bulwark]], [[the-paragon|The Paragon]], [[the-spear|The Spear]] (без копья — компромисс), [[the-shepherd|The Shepherd]] слабо.
3. **Сеттинг:** рыцарь ордена, гвардеец, стартовый герой-танк.

---

### HunterOrc — 1 юнит

**Внешний вид:** орк-лучник, зелёная кожа, красная кожаная броня, лук + projectile-кадры.

#### Hunter Orc

1. **Анимации:** Idle 4, Walk 8, Attack1 9, Attack2 14, Hurt 4, Death 6 + Projectile×5. Боевой минимум закрыт; idle короткий.
2. **ГДД:** не герой ростера; враг-орков нет в ГДД. Ближе к «тяжёлому» [[goblin-archer|Goblin Archer]] / отдельной орк-фракции.
3. **Сеттинг:** орк-охотник, вассал вождя, рейнджер степи.

---

### imp_axe_demon — 2 юнита

**Внешний вид:** `demon_axe_red` — крупный демон с рогами и двуручным топором; `imp_red` — меньший бес с дубиной. Одна красная палитра.

#### Demon Axe

1. **Анимации (per-frame):** ready, walk/run, attack, hit, jump… (~50 кадров на демона). Хорошая читаемость рогов/топора.
2. **ГДД:** нет прямого героя; элит/босс; слабо к [[the-draugr|The Draugr]] (демон ≠ драугр).
3. **Сеттинг:** адский акт, summon демониста, элит golem-акта «зло».

#### Imp

1. Тот же стиль, меньше силуэт.
2. **ГДД:** мелкий summon / моб.
3. **Сеттинг:** бес, пачка импов.

---

### Martial Hero — 1 юнит

**Внешний вид:** ронин в широкополой конической шляпе, бежево-коричневые одежды, синие ножны катаны.

#### Martial Hero (шляпа)

1. **Анимации:** Idle 8, Run 8, Attack1/2 по 6, Take Hit 4 (+ white), Death 6, Jump/Fall по 2. Сильный уникальный силуэт.
2. **ГДД:** [[the-gale|The Gale]], [[the-verdict|The Verdict]], странник-версия [[the-hunter|The Hunter]].
3. **Сеттинг:** бродячий мастер клинка, восточный акт, «безымянный» герой.

---

### Martial Hero 2 — *(см. Pixel Art Heroes — дубликат)*

---

### Martial Hero 3 — 1 юнит

**Внешний вид:** мускулистый боец без рубашки, тяжёлый дадао на плече, зелёный пояс.

#### Martial Hero 3

1. **Анимации:** Idle 10, Run 8, Attack1–3 (7/6/9), Take Hit 3, Death 11, Going Up/Down по 3. Отличный набор.
2. **ГДД:** [[the-pyre|The Pyre]], [[the-draugr|The Draugr]] (берсерк-тело), [[the-paragon|The Paragon]], [[bandit-bruiser|Bandit Bruiser]].
3. **Сеттинг:** гладиатор, берсерк, чемпион кулачных ям.

---

### pack_loreon_knight — 1 юнит

**Внешний вид:** скелет в сегментных латах, olive-повязка, короткий меч. Grim dungeon.

#### Loreon Skeleton Knight

1. **Анимации (per-frame):** ready 3, walk 4, run 6, attack1/4 по 6, hit 3, jump 5, fall_back 5, stand_up 5. Нет явного длинного death-цикла (есть fall_back). Средний polish.
2. **ГДД:** summon [[the-bonewright|The Bonewright]]; также [[the-draugr|The Draugr]] (если «тяжёлая» нежить).
3. **Сеттинг:** костяной страж, скелет-рыцарь склепа.

---

### Ranger cat — 1 юнит × 2 масштаба

**Внешний вид:** антропоморфная кошка/рысь, зелёный жилет. Варианты **100% (64×48)** и **200% (128×96)** — один дизайн.

#### Ranger Cat

1. **Анимации:** idle ≈5, run ≈11, attack 1–3 (≈5–8), hit ≈3, jump ≈7–11, dash 1 + hitbox-листы. Полный платформерный набор; нет явного death.
2. **ГДД:** ни на канон-реликвию (зверолюд не в темах). Возможный альт [[the-hunter|The Hunter]] / ивент.
3. **Сеттинг:** зверолюд-рейнджер, фея-кот, комедийный герой.

---

### SkeletonKnight — 1 юнит

**Внешний вид:** скелет-рыцарь в серебряных латах, красный плащ, огромный меч за спиной. Высокий polish.

#### Skeleton Knight

1. **Анимации:** IDLE ≈15, WALK ≈12, HURT ≈3, DEATH ≈12, DOWN/FWD/SIDE_SWING ≈10, FULL_COMBO ≈29. Очень богатые атаки; отличное качество.
2. **ГДД:** топ-визуал для [[the-draugr|The Draugr]]; также элитный summon Bonewright / отдельный undead-босс.
3. **Сеттинг:** драугр-рыцарь, чемпион склепа, проклятый гвардеец.

---

### skeleton_sword — 1 юнит

**Внешний вид:** простой скелет с коротким мечом и одним наплечником. Минималистичный dungeon-mob.

#### Skeleton Sword

1. **Анимации:** ready, walk, run, attack, hit, jump, dead_near/far, corpse, reborn. Есть **reborn** — плюс для некромантии. Качество скромнее SkeletonKnight.
2. **ГДД:** идеальный summon для [[the-bonewright|The Bonewright]]; массовый undead-моб.
3. **Сеттинг:** рядовой скелет-мечник.

---

### Slime_Pixel_Monsters_Vol_1 — 1 архетип × много цветов

**Внешний вид:** классический желе-слайм. Цвета: blue, cyan, green, purple, red, silver, yellow (+ outline-варианты в структуре пака).

#### Slime

1. **Анимации (per-frame, очень плотные):** idle 56, walk 42, attack_1/2 (84/91), hurt_1/2 по 35, death 70, bounce/slide/revive… Качество анимации **выдающееся** для моба; избыточно для нужд автобаттлера (можно проредить).
2. **ГДД:** нет героя-слайма; природа/Bloom-синергия как моб; filler акта.
3. **Сеттинг:** стартовые волны, грибные/болотные зоны, цветовые элиты.

---

### swordman pack — 1 юнит

**Внешний вид:** пехотинец в открытом шлеме, серые латы, одноручный меч; на листе palette-swatches.

#### Swordman

1. **Анимации:** один атлас 384×320 (idle/run/attack/death — ручная нарезка). Smear в атаке хороший.
2. **ГДД:** [[the-pyre|The Pyre]], [[the-bulwark|The Bulwark]], бандиты-мечники, generic soldier.
3. **Сеттинг:** городская стража, солдат королевства, наёмник.

---

### Tiny RPG Pack 01 — 2 юнита

**Внешний вид:** chibi Soldier (шлем с гребнем, копьё) и chibi Orc (зелёный, копьё). Cute-retro.

#### Soldier

1. **Анимации:** Idle 6, Walk 8, Attack01–03 по 6, Hurt 4, Death 4. Компактно и достаточно.
2. **ГДД:** мелкий масштаб — скорее UI/иконка или «крошечный» режим; слабо как основной герой рядом с LuizMomo. Для [[the-spear|The Spear]] по оружию — да, по стилю — нет.
3. **Сеттинг:** игрушечная армия, питомец-солдат, отдельный «tiny» визуальный слой.

#### Orc

1. Те же циклы.
2. **ГДД:** нет орков в каноне; tiny-враг.
3. **Сеттинг:** орк-набег в chibi-стиле.

---

### Tiny RPG Pack 02 — 2 юнита

**Внешний вид:** chibi Demon_A (рога, огромный топор) и Blood Monster_A. Есть варианты with shadows.

#### Demon_A / Blood Monster_A

1. **Анимации:** Idle/Walk/Attack01–02/Hurt/Death — стандартный tiny-набор.
2. **ГДД:** нет прямого; мелкий демон-моб.
3. **Сеттинг:** адский filler, summon.

---

### viking_axe_pack — 1 юнит

**Внешний вид:** бородатый воин в мехе/коже, круглый щит (топор в названии пака). Low-fantasy, мрачный.

#### Viking Axe

1. **Анимации:** ready, walk, run, attack, hit, jump, roll, dead. Боевой минимум есть; polish средний.
2. **ГДД:** [[the-draugr|The Draugr]], [[the-bulwark|The Bulwark]] (щит!), [[bandit-shieldman|Bandit Shieldman]].
3. **Сеттинг:** северный щитоносец, рейдер, фронтовик варваров.

---

### Warrior-V1.3 — 1 юнит

**Внешний вид:** женщина-воин, серебряная кираса, фиолетовая накидка, двуручник на спине. Много platformer-анимаций + FX.

#### Warrior (female)

1. **Анимации:** idle 6, Run 8, Attack 12, Hurt 4, Death 11, Dash/Dash Attack, Crouch, Slide, WallSlide, Jump/Fall… **Избыток** для автобаттлера, но ядро отличное. Высокий polish.
2. **ГДД:** [[the-pyre|The Pyre]], [[the-paragon|The Paragon]], [[the-verdict|The Verdict]], женский альт [[the-bulwark|The Bulwark]].
3. **Сеттинг:** капитан гильдии, рыцарь-женщина, авантюристка.

---

### Wizard Pack — 1 юнит

**Внешний вид:** доброжелательный старый маг, фиолетовые робы, посох с кристаллом, парящая книга. Не «evil».

#### Wizard (добрый)

1. **Анимации:** Idle ≈6–7, Run ≈10, Attack1/2 ≈10, Hit ≈5, Death ≈9, Jump/Fall ≈2. Полный кастер-набор, высокое качество.
2. **ГДД:** [[the-shepherd|The Shepherd]], [[the-winter|The Winter]], [[the-storm|The Storm]], [[the-cadence|The Cadence]] (если принять как «арканист»), [[the-tide|The Tide]].
3. **Сеттинг:** мудрый советник, боевой архимаг «светлой» школы, гильдейский маг.

---

## Сводка по полноте для боя

| Уровень | Паки |
|---|---|
| Отлично (Idle+Move+Attack+Hit+Death, polish) | Fantasy Warrior, Huntress/2, MW2/3, Martial Hero/3, Hero Knight 2, Evil Wizard 2/3, Wizard Pack, SkeletonKnight, AxeWarriors, Warrior-V1.3, Slime vol.1, Forest Mushroom, MCF2 (Bat/Mimic/Rat/Slime) |
| Хорошо с оговорками | Bandits (слабый Death), HunterOrc, Goblin_Fighter (нарезка), loreon/skeleton_sword/viking, Martial Hero 2 (короткие атаки), ArcherHero |
| Дырки в наборе | Kobold (нет Death/Hit), Ranger cat (нет Death), Goose |
| Не для боя сейчас | MCF 1.3 fragment, Nneshan, vagabond, Characters 500+, Goblin Monsters 4 Colors (статика), joke-гуси как герои |

---

## Заметки для продакшена

- Стили **не смешивать** в одном отряде без единого outline/палитры: LuizMomo рядом с Tiny RPG будет «ломаться».
- Jump/Fall/Dash у многих паков — платформерный багаж; в автобаттлере можно не использовать.
- Per-frame паки (viking/skeleton/axe) проще резать в Unity, spritesheet-паки LuizMomo — через Grid by Cell Count (см. agent-workflows).
- ~~Спрайт-паки под `Pixel Art Heroes/` по git-конвенции пока **не коммитятся** в репо~~ — **неверно, поправлено 2026-07-20:**
  `git ls-files Assets/_Project/Art/Sprites` даёт 5219 записей, 29 МБ. Пул отслеживается. Из него в игре
  задействовано 12 листов из двух паков (Medieval Warrior Pack 2, Fantasy Warrior) — см.
  [[tech/10-reference/asset-inventory|Reference - Asset Inventory]].
