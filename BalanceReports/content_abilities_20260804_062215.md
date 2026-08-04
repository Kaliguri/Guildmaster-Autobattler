# Способности китов
_Сгенерировано 2026-08-04 06:22 — SimBench_

Разбор способностей: по строке на способность. Cooldown — базовый кулдаун в секундах, Cost — стоимость ресурса, DmgMult — множитель урона от авто-атаки, Radius — радиус области (0 = одиночная цель), Heal — плоское лечение или процент недостающего HP. Effects — что способность накладывает, с описаниями из той же таблицы, что видит игрок.

| Relic | Ability | Cooldown | Cost | DmgMult | Target | Radius | Heal | Effects | EffectDesc |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Antimage | overload | 0 | 30 | 0 | NearestEnemy | 0 | 0 | Перегрузка | Швыряет в цель долю всего магического урона, поглощённого с прошлой Перегрузки. |
| Arcanist | arcane_volley | 0 | 15 | 0 | NearestEnemy | 0 | 0 | Разлад | Магическая защита цели снижена. Стаки складываются и обновляют срок. |
| Assassin | shadow_step | 0 | 75 | 0 | Self | 0 | 0 | Скрытность |  |
| Cryomancer | ice_zone | 0 | 40 | 0 | NearestEnemy | 3 | 0 | Изморозь | Холод копится на цели: сначала замедляет, потом приковывает к месту, а на пределе обращает в лёд. |
| Defender | resolute_strike | 0 | 50 | 0 | NearestEnemy | 0 | 0 |  |  |
| Dreameater | lullaby | 0 | 50 | 0 | NearestEnemy | 0 | 0 | Сон · Кошмар | Цель не действует и выпадает из выбора цели. Чужой удар будит её и бьёт вдвое сильнее. | Урон тьмой, растущий за каждую секунду сна. |
| Druid | spore_burst | 0 | 45 | 0 | AllEnemiesWithTag | 2 | 0 |  |  |
| Emberkeeper | fan_the_flames | 0 | 30 | 0 | LowestHpAlly | 0 | 0 | Раздуть жар · Заслон от жара · Угли | Сильное развеивание: снимает даже то, что не берёт лекарь. | Урон огнём снижен на 25%. | Тлеющие угли: каждый стак усиливает получаемый носителем урон огнём, а огонь — это [Магический урон]. Ложатся на кого угодно — на врага и на своего одинаково. Копятся от любого огня, осыпаются без подпитки. |
| FlameSwordsman | ignition | 0 | 50 | 0 | NearestEnemy | 0 | 0 | Воспламенение |  |
| Frostbound | ice_harvest | 0 | 40 | 5 | AllEnemiesWithTag | 0 | 0 | Ледяная хватка | Жатва держит цель, пока идёт замах. |
| IronSpearman | steel_whirl | 0 | 45 | 3 | Self | 2.5 | 0 | Вихревой захват | Замедление, слабеющее с каждым мгновением. |
| LightShepherd | hand_of_life | 0 | 30 | 0 | LowestHpAlly | 0 | 100 | Очищение светом | Свет снимает базовую порчу вместе с лечением. |
| Necromancer | summon_skeleton | 0 | 100 | 0 | Self | 0 | 0 |  |  |
| Nightblade | venom_seal | 0 | 25 | 0 | NearestEnemy | 0 | 0 | Ядовитая печать | Через 3 секунды печать взрывается, нанося урон обеими школами. |
| Ranger | hunters_mark | 0 | 30 | 0 | NearestEnemy | 0 | 0 | Метка охотника |  |
| TrashArcher | trash_aimed_shot | 0 | 60 | 3 | NearestEnemy | 0 | 0 |  |  |
| TrashBonecaller | trash_raise_bones | 0 | 100 | 0 | Self | 0 | 0 |  |  |
| TrashBrawler | trash_deflect | 0 | 60 | 0 | Self | 0 | 0 |  |  |
| TrashCutthroat | trash_low_blow | 0 | 50 | 2.5 | NearestEnemy | 0 | 0 |  |  |
| TrashHerbalist | trash_salve | 0 | 20 | 0 | LowestHpAlly | 0 | 180 | Отвар |  |
| TrashHexer | trash_hex | 0 | 60 | 0 | NearestEnemy | 0 | 0 | Сглаз |  |
| TrashShieldbearer | trash_rusty_guard | 0 | 60 | 0 | Self | 0 | 0 |  |  |
| Treant | overgrowth | 0 | 40 | 0 | Self | 0 | 0 | Разрастание |  |
| WaterMonk | water_shield | 0 | 60 | 0 | LowestHpAlly | 0 | 0 | Водяной щит | Поглощает урон 5 секунд. По истечении или при уничтожении разлетается волной: толкает и замедляет врагов рядом. |
| WhirlMonk | whirl_push | 0 | 60 | 0 | NearestEnemy | 0 | 0 | Захват вихря |  |
| BoneDevDuelist | resolute_strike | 0 | 50 | 0 | NearestEnemy | 0 | 0 |  |  |
| BoneStorybookDevDuelist | resolute_strike | 0 | 50 | 0 | NearestEnemy | 0 | 0 |  |  |
| BanditBruiser | bandit_hammer_stun | 6 | 0 | 0 | NearestEnemy | 0 | 0 | BanditHammerStun |  |
| BanditWarlock | warlock_mark | 8 | 0 | 0 | NearestEnemy | 0 | 0 | WarlockMark |  |
| BanditWarlock | warlock_mend | 4 | 0 | 0 | LowestHpAlly | 0 | 0 |  |  |
| EarthGolem | golem_stone_ward | 15 | 0 | 0 | Self | 0 | 0 |  |  |
| GoblinCommander | goblin_warcry | 10 | 0 | 0 | AlliesInRadius | 4 | 0 | GoblinWarcry |  |
| GoblinJester | goblin_dust_handful | 8 | 0 | 0 | NearestEnemy | 0 | 0 | Blind · Blind |  |
| GoblinMocker | goblin_mocking_gestures | 6 | 0 | 0 | NearestEnemyWithResource | 0 | 0 | ManaDrain |  |
| GoblinShaman | shaman_ward | 8 | 0 | 0 | LowestHpAlly | 0 | 0 |  |  |
