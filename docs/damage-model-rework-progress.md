# Рефактор модели урона — журнал

> Большая многофазная задача (старт 2026-07-24). Выросла из «доделать теги юнита»:
> вскрылось, что модель типа урона неполна и терминология «своенравничает».
> Ветка: `feature/unit-tag-icons`. План утверждён Максом.

## Концепция (утверждено Максом 2026-07-24)

- **Две брони: Физическая и Магическая** (бывш. «Стихийная/Elemental» — уходим в «Магическая»).
  Плюс **Чистый** (True) — мимо любой брони.
- **Тип урона задаётся ПОИСТОЧНИКОВО**, не на юните целиком: автоатака, каждая
  способность, каждый DoT-эффект несут свой тип. У копейщика тычка Колющая, ульта Режущая.
  У юнита в итоге несколько типов урона сразу.
- **Оси типа урона:** School (броня-категория) + PhysicalSubtype (Blunt/Slash/Pierce)
  + **MagicElement** (Fire/Ice/Lightning/**Arcane**) + Affinity (Poison/Light/Dark, «вкус» поверх).
- **Arcane** — чистая магия без стихии (гасится магической бронёй). Механика — потом, enum+тег — сейчас.
- **В бою реально считаются только School + Affinity** (броня по School, множитель по типу существа
  от Affinity). Подтип/элемент — качественные метки «для чтения» (+ задел «влияют на урон позже»),
  в `DamageRequest`/пайплайн пока НЕ тащим.

## Архитектура

- **Хранение — плоские поля** на каждом источнике (Odin-дропдауны, минимум миграции сериализации).
- **Логика — через `readonly struct DamageType { School, PhysicalSubtype, MagicElement, Affinity }`**,
  который резолвер собирает с учётом `Inherit`-override.
- Тег «быстрого чтения» = агрегат `DamageType` по всем источникам юнита (данные, не рантайм).

## Фазы

- [x] **Ф0 — Терминология.** `Elemental→Magic` в коде: `StatType.MagicArmor/MagicPen/MagicPenPct`
  (int 4/13/14 сохранены), `DamageSchool.Magical` (int=1), `DamageSchoolOverride.Magical` (int=2),
  дефолты `PeriodicDamage/Ignition/Thorns`, refs в `DamagePipeline/StatKinds/ContentAuditor/ContentHub`.
  Тесты (`DamagePipeline/DotBattle/PoisonBurnThorns`) переименованы. **241/241 зелёные.**
- [x] **Ф1 — `MagicElement` + оси.** enum `MagicElement {None,Fire,Ice,Lightning,Arcane}` +
  override-энумы `PhysicalSubtypeOverride`/`MagicElementOverride` + `DamageCategories.Resolve`.
  `readonly struct DamageType {School,PhysicalSubtype,MagicElement,Affinity}` (нормализует конкретику
  под школу). Поля: `UnitData._magicElement` + `ResolveAutoAttackDamageType()`; override подтипа/элемента
  на `AbilityData` + `ResolveDamageType(caster)`; подтип/элемент на `PeriodicDamageComponent` + `DamageType`.
  8 EditMode-тестов (`DamageTypeResolverTests`). **249/249 зелёные.**
- [x] **Ф2a — Чистка типов урона.** Оси добиты в `ArmorThornsComponent`/`IgnitionComponent`.
  Миграция ассетов (editor-скрипт через execute_code): Druid тычка Pierce физ + `affinity=None`
  (яд = сродство, не тип урона — poison.md; живёт в спорах-DoT); SporeCloud DoT school True→Physical
  (яд от физ-тычки гасится физбронёй); Cryomancer тычка +Ice; Burn/Ignition +Fire; IronSpearman
  «Стальной вихрь» +подтип Slash (автоатака Pierce); Treant шипы +Pierce. 8 автоатак из 10 уже
  были верны. **249/249 зелёные.**
- [x] **Ф2b — Редизайн Шепарда (новая механика).** `AllyMendComponent` (reactive на DamageDealt):
  автоатака лечит самого раненого союзника (HP%, тай-брейк по Id) в радиусе вокруг носителя на
  долю нанесённого. Ассет `effect.light_mend` (пассив, permanent). Shepherd relic: школа урона
  автоатаки Physical→True (+сродство Light, было), AutoAttackDamage 100→33 (черновое), +granted
  LightMend. AI-preset: `AutoAttackMode` Heal→Damage, targeting AllyLowest→Nearest. 3 EditMode-теста
  (`AllyMendComponentTests`) + `MockCombatContext.Heals`-трекинг. Карточка the-shepherd обновлена. **252/252.**
  - `AutoAttackMode.Heal`-механика в коде НЕ удалена (её держат `ShepherdSliceTests`, автономны —
    строят relic в коде). Шепард на неё больше не завязан. Удаление — отдельный техдолг.
  - Долг: лок-ключи `effect.light_mend.name/.desc` (Ф4 с остальным loc).
- [ ] **Ф2c — Пересверка** `docs/wiki/gdd/roster/relic-tag-assignments.md` под новую модель
  (Druid Physical·Pierce+Poison-DoT, Shepherd True·Light, Cryomancer Magical·Ice).
- [x] **Ф3a — Резолвер тегов (Data).** `UnitTagResolver.Resolve(unit, db)` → упорядоченный
  `List<TagData>`: Role из класса, DamageType из статических источников (автоатака + наносящие
  урон способности; зонтик→конкретика→сродство), ручные из `InfoTags`; сортировка по оси
  `Role→DamageType→Playstyle→Mechanic`; отсутствующий ассет тега молча пропускается. id `tag.<snake>`.
  Стихии из эффектов (Burn→Fire) НЕ собираются — Combat-слой недоступен UI (осознанный задел).
  4 EditMode-теста. **256/256 зелёные.**
- [ ] **Ф3b — UI-вывод + ручные теги.** РАЗВИЛКА: два loadout-экрана — старый `LoadoutScreen.uxml`
  (`MenuRouter.BuildLoadoutScreen`, detail-tags = Label) и новый трёхколоночный
  `LoadoutInventoryScreen.uxml` (`LoadoutInventoryView`, detail-tags = VisualElement-контейнер).
  Определить живой перед тем, как вешать чипы. Плюс проставить ручные Playstyle/Mechanic героям
  в `_infoTags` по `relic-tag-assignments`. UI-инвентарь хрупкий (см. память) — не вслепую.
- [ ] **Ф4 — Доки/долг.** glossary (Magical+элементы+Arcane), `stats.md`, `tag-reference`,
  тех-модель урона (tech-scribe), ADR. Сюда собраны ВСЕ док-правки (не дробим по фазам —
  модель менялась до Ф3).

## Решения по ходу / готчи

- Доки не трогаем до Ф4 (иначе двойная работа — модель меняется в Ф1/Ф2).
- `StatType` int-значения (4/13/14) и `DamageSchool` int (1) НЕ меняем — ассеты не мигрируют.
- Unity MCP-мост моргает на перезагрузке домена после C#-правок; переспрашивать job после паузы.
- `Thorns` дефолт стал `Magical` (был `Elemental`, та же школа int=1). Семантику шипов
  (физ/маг) не трогал в Ф0 — если надо, правится в Ф2 при чистке данных.
