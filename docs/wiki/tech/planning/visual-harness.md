---
title: "Planning - Visual Harness"
order: 120
status: archive
updated: 2026-07-16
---

**Статус:** Выполнен и закрыт (2026-07-09). Обе сессии доставлены:
> - **Сессия A (asmdef)** — была сделана в `master` ещё с 2026-05-28 (`ecf9257`/`4aa01e5`) задолго до составления плана; исходное «asmdef-файлов нет» — ложь. См. вики `1. Сборки (Assembly Definitions).md`.
> - **Сессия B (визуальный харнесс)** — доставлена **мержем `dev` + `feature/phase3-relics` в `master`** (`217fe07`, `8e4a49b`), а не постройкой с нуля: харнесс уже был на ветках и просто не был влит. Верифицировано зелёно: компиляция чистая, EditMode 136/136, PlayMode 2/2 (вкл. `Battle_StartsAndEndsWithWinner`).
> - Реальность разошлась с эскизом Сессии B: доставлено на **настоящих спрайтах** (`UnitVisual` SO: FantasyWarrior/MedievalWarrior) + срез «Железный копейщик», а НЕ плейсхолдер-палитрой + `Relic_Bruiser`/`Archer`. Значит `CombatPalette` (шов цвет-токенов) так и не построен — остаётся открытым до реальной нужды.
> Ниже — исходный план как есть (историческая ценность для шагов харнесса).

---

> План возврата боя «на экран»: сначала asmdef-слои (изолированно), потом рабочий спрайтовый визуальный харнесс на SO-данных. Разбито на две сессии, чтобы контекст не пух и каждая давала зелёную верификацию.
>
> Связано: [[tech/explanation/index|Explanation - Code Map]], [[tech/explanation/presentation|Explanation - Presentation]], [[tech/00-meta/tech-changelog|Meta - Tech Changelog & Decisions]].

---

## 0. Зачем и на чём стоим (verified baseline 2026-07-08)

То, что раньше «бегало и билось» — это `CombatDebugDraw` (гизмо-слой, data-driven от `_simulation.Units`). Спрайтовый путь (`CombatPresenter`/`UnitView`/`HealthBarView`/`DamageNumberSpawner`) написан, но **не запускается**:
- префабов юнита нет вообще (0 `.prefab` в `_Project`);
- `BattleScene.unity` = только Main Camera (в git коммитилась пустой);
- `.asset` в `_Project` нет → нет `StatsConfig`, нет `RelicData`;
- `BattleSetupBuilder.SetupTestBattle(...)` определён, но никем не вызывается.

Значит задача = **собрать runnable rig**, а не писать новую архитектуру презентации.

### Статус швов (принцип «шов сейчас — реализация потом»)

| Шов | Статус |
|---|---|
| №1 Ключи локализации в SO | ✅ **уже есть** — `RelicData._displayNameKey` |
| №3 Стабильные id | ✅ **закрыт** механикой Unity (ссылки на SO по GUID, rename-safe) |
| №2 Токены цвета | ⬜ открыт → тонкий срез (один SO цвет-ролей), см. Сессия B |
| №4 asmdef-слои | ⬜ открыт → **Сессия A** (asmdef-файлов в проекте нет, всё в `Assembly-CSharp`) |

---

## Сессия A — asmdef-слои (делать ПЕРВОЙ)

**Почему первой:** визуальный харнесс добавит новый код и сцену; разбивать сборки после — значит раскидывать этот свежий код по ассембли повторно. Делаем структуру до, чтобы новый код рождался в правильных сборках. Проход изолированный: не меняет геймплей-логику, верификация = зелёная компиляция + существующие тесты.

**Целевая раскладка (зависимость в одну сторону):**

```
Guildmaster.Core          → (UnityEngine)                  # Simulation, Random, SimConstants
Guildmaster.Data          → Core                           # Definitions, Stats (SO)
Guildmaster.Combat        → Core, Data                     # sim, systems, effects, units, abilities
Guildmaster.Presentation  → Core, Data, Combat             # Presenter, Views, DamageNumbers
Guildmaster.Net           → Core, Combat (+ NGO, Facepunch)
Guildmaster.Game          → всё выше (+ VContainer, UniTask, MessagePipe)   # scopes, bootstrap, services
Guildmaster.DevTools      → Game, Combat (+ Quantum Console)
Guildmaster.Tests.EditMode / .PlayMode → тестируемые сборки
```

**Задачи:**
1. Создать `.asmdef` по слоям выше; проверить, что зависимости строго в одну сторону (Core ничего не тянет из Combat/Presentation/Game).
2. Развесить plugin-ссылки по сборкам, которым они реально нужны (VContainer, MessagePipe, UniTask, Sirenix/Odin, FMOD, Facepunch, Unity.Localization, Addressables, Newtonsoft) — точный список крутится по красной компиляции.
3. Редакторный код (если появится) — отдельный `*.Editor` asmdef, чтобы не тёк в билд.
4. `run_tests` (EditMode) — зелёно. Коммит.

**Риск/стоп:** если развеска ссылок уходит в долгую — это ожидаемо (полдня), не паниковать; но если что-то ломается концептуально (циклическая зависимость) — остановиться и обсудить, не костылить.

---

## Сессия B — визуальный харнесс (6 шагов, каждый = видимый результат + коммит)

Цель: Play в BattleScene → две команды плейсхолдер-юнитов спавнятся, идут навстречу, бьются, HP-бары текут, damage numbers всплывают, бой заканчивается. На SO-данных + одном SO цвет-ролей.

**Шаг 0 — Контент (SO, единый источник).** `StatsConfig.asset` + 2× `RelicData.asset` (`Relic_Bruiser`, `Relic_Archer`) со стат-модами: MaxHP, MoveSpeed, AttackRange, AttackDamage, AttackSpeed. Без MaxHP-мода юнит стартует с 0 HP и дохнет. Через `manage_scriptable_object`.

**Шаг 1 — `CombatPalette` SO (шов №2, тонкий срез).** Роли-цвета: `TeamA`, `TeamB`, `HpFull`, `HpLow`, `HpBarBg`, `DamageNumber`. Один ассет. Без машинерии переключения тем — полный инъектируемый `IThemeService` приедет только когда появится 2-я тема (дальтонизм).

**Шаг 2 — Префаб юнита (плейсхолдер).** `UnitView.prefab`: `UnitView` + `SpriteRenderer` (встроенный белый квадрат/круг, тинт по команде) + дочерний `HealthBar` с `HealthBarView` + fill/bg SpriteRenderer. Рефактор `HealthBarView`/`UnitView`: цвета берут из `CombatPalette` (передаются в `Bind`), а не из SerializeField-литералов — это проводка шва.

**Шаг 3 — Проброс цвета через презентацию.** Регистрирую `CombatPalette` в `CombatLifetimeScope` (RegisterInstance через SerializeField). `CombatPresenter` инъектит палитру; на спавн тинтит спрайт по команде и передаёт HP-цвета в `UnitView.Bind`.

**Шаг 4 — Rig в BattleScene + триггер спавна.** Объект `[Combat]` c `CombatLifetimeScope` (назначить StatsConfig, CombatPalette, префаб на презентере) + компоненты `CombatPresenter` и `CombatDebugDraw` (чтобы `RegisterComponentInHierarchy` их нашёл). Добавить `BattleStarter` (VContainer `IStartable`, `[SerializeField] RelicData[] _teamA/_teamB`) → в `Start` зовёт `BattleSetupBuilder.SetupTestBattle`. Камера кадрирует поле. **Коммит населённой сцены** (в git её содержимого не было ни разу).

**Шаг 5 — Запуск и наблюдение.** Play → смотрим глазами + дешёвый smoke-тест (EditMode/PlayMode). Чиню проводку.

**Шаг 6 (опц., мелкий) — фикс дёрганья интерполяции** (техдолг §3.3: `alpha` = `accumulator/TickDelta` из `CombatLoopService`, а не `Time.deltaTime/TickDelta`). Только если рябит в глаза.

### Проверить по ходу (не блокер)
`CombatLifetimeScope` объявлен дочерним от `RootLifetimeScope`. На Шаге 4 выяснить: запускается ли BattleScene автономно (быстрая итерация), или нужен родитель через CoreScene→GameFlow (напр. ради `IAudioService`). Если нужен родитель — сделать так, чтобы харнесс работал и standalone.

---

## Зафиксированные решения

- Цвет — **тонкий срез**: один `CombatPalette` SO цвет-ролей, без переключения тем. Полный `IThemeService` — когда появится 2-я тема.
- Порядок: **A (asmdef) → B (визуал)**, разными сессиями.
- Спрайты — плейсхолдеры (встроенные), не реальный арт.

## Явно отложено (НЕ в этом плане)

VFX Graph / Cinemachine / Feel-джус; Odin-редакторы авторинга; реальный арт; объём контента (2 тест-юнита достаточно); `IThemeService` и переключение тем; техдолг §3 (кроме опц. §3.3).
