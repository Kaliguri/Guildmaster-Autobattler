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
- [ ] **Ф2 — Чистка данных.** 10 героев + враги: тип урона поисточниково по карточкам.
  Друид: тычка Pierce физ, `affinity=None`, яд — в DoT спор. Шепард: автоатака = хил, без
  damage-типа. Pyre: тычка Slash, Burn-DoT = Fire. Копейщик: тычка Pierce / ульта Slash.
  Пересверить `docs/wiki/gdd/roster/relic-tag-assignments.md` под новую модель.
- [ ] **Ф3 — Теги на карточку.** Авто-тег из `DamageType`-агрегата + ручные Playstyle/Mechanic
  героям + вывод чипами «иконка+подпись» в порядке `Role→DamageType→Playstyle→Mechanic`.
- [ ] **Ф4 — Доки/долг.** glossary (Magical+элементы+Arcane), `stats.md`, `tag-reference`,
  тех-модель урона (tech-scribe), ADR. Сюда собраны ВСЕ док-правки (не дробим по фазам —
  модель менялась до Ф3).

## Решения по ходу / готчи

- Доки не трогаем до Ф4 (иначе двойная работа — модель меняется в Ф1/Ф2).
- `StatType` int-значения (4/13/14) и `DamageSchool` int (1) НЕ меняем — ассеты не мигрируют.
- Unity MCP-мост моргает на перезагрузке домена после C#-правок; переспрашивать job после паузы.
- `Thorns` дефолт стал `Magical` (был `Elemental`, та же школа int=1). Семантику шипов
  (физ/маг) не трогал в Ф0 — если надо, правится в Ф2 при чистке данных.
