---
title: "Planning - SFX (FMOD)"
order: 80
status: archive
updated: 2026-07-28
---

> [!info] Статус: ТЗ отработано, продолжение — во втором заходе
> Это ТЗ исполнено; дальнейшая работа по звуку (покрытие, сведение, громкость) ведётся в
> [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]], который и есть актуальный документ по SFX.
> Тело ниже — архив замысла на 2026-07-13.

**Статус:** ТЗ к исполнению (объём утверждён Максом 2026-07-13: базовый звук из бесплатных паков, без покупок; финальная полировка позже через ElevenLabs — см. память `planned-ai-subscriptions`)

---

> Исполняемое техзадание «SFX-затычки»: полное озвучивание текущего боя и UI через FMOD Studio из уже скачанных бесплатных паков. Цель — игра **звучит целиком** (ни одного немого события), качество — «приличная затычка», не финал. Архитектурный принцип: стабильные пути FMOD-событий + подмена wav = будущий апгрейд звука без перепроводки.
> Исполнение: пакеты 0–7 строго по порядку, один пакет = одна ветка = один PR в `dev`.

---

## 0. Общие правила исполнения (читать перед каждым пакетом)

1. **Ветки:** от свежего `origin/dev`, имя `feat/sfx-p{N}`. PR в `dev`, squash-merge, **ветку после мержа удалить**. Коммиты — английский, conventional.
2. **Тесты:** после каждого пакета — полный EditMode-прогон (`run_tests` через Unity MCP или `./scripts/run-tests.ps1`). Красный тест — повод остановиться, не править тест.
3. **FMOD только за `IAudioService`.** Ни одного вызова `RuntimeManager`/`EventInstance` вне `FmodAudioService` (правило CLAUDE.md). Исключение: `TimeScaleService` может выставлять один глобальный параметр FMOD через тонкий интерфейс (см. §2.6).
4. **asmdef-границы святы:** Presentation не ссылается на Game (уже был цикл). `IAudioService` живёт в Core — доступен всем.
5. **Ссылки на события — по GUID** (`FMODUnity.EventReference` хранит GUID+путь; path-only ссылки тихо перепривязываются при переименовании).
6. **Банки — в `Assets/StreamingAssets`, НЕ в Addressables** (Addressables у нас только для Localization; порядок инициализации FMOD ломается от addressables-банков).
7. **Аудио-QA — за Максом** (как визуал): агент проверяет не-ушами — события летят, ключи резолвятся, каталог валиден, тесты зелёные. После каждого пакета — короткий список «что послушать».
8. **Лицензии:** Sonniss GDC — royalty-free, без атрибуции, но **редистрибуция самих wav запрещена** → исходные wav попадают только в приватный реп (ок), реп не публиковать паблично с ними; AI-тренинг на Sonniss-материале запрещён лицензией. RPG Essentials (Leohpaz) — бесплатно в коммерческих, кредит опционален (добавить в будущий credits). Kenney — CC0.
9. **Правки существующего кода — минимальные и точечные.** Каркас (`AudioPresenter`, `AudioResolver`, `AudioCatalog`) спроектирован под это ТЗ — расширять, не переписывать.

---

## 1. Контекст: что уже есть в проекте

### 1.1 Код-каркас (живой, но спящий)

| Что | Где | Состояние |
|---|---|---|
| `IAudioService` (`Play(key)`, `Stop(key)`, `SetMusicVolume`) | `Assets/_Project/Scripts/Core/Audio/IAudioService.cs` | интерфейс готов |
| `UnityAudioService` — стаб (Debug.Log) | `Assets/_Project/Scripts/Game/Services/UnityAudioService.cs` | заменяется на FMOD-импл |
| Регистрация в DI | `RootLifetimeScope.cs:38` | тут свапается импл |
| `AudioPresenter` — MessagePipe → `Play` | `Assets/_Project/Scripts/Presentation/Audio/AudioPresenter.cs` | **НЕ зарегистрирован ни в одном scope** — инертен |
| `AudioResolver` — ключ `{contentId}.{action}` → каталог → per-action дефолт → тишина+лог | `.../Audio/AudioResolver.cs` | готов, юнит-тесты есть |
| `AudioAction` enum: `Attack, Hit, Death, Cast, Ui` | `.../Audio/AudioAction.cs` | расширяется в П4 (§2.2) |
| `AudioCatalog` SO — ключ → `FMODUnity.EventReference` + per-action дефолты | `.../Audio/AudioCatalog.cs`, ассет `Assets/_Project/ScriptableObjects/Audio/AudioCatalog.asset` | пустой, заполняется в П5 |
| Voicing-матрица (юнит × действие) | `ContentHubWindow.Audio.cs` | готовый инструмент контроля покрытия |
| Тесты каталога/резолвера | `Assets/_Project/Tests/EditMode/Audio/AudioTests.cs` | требуют: нет дубль-ключей, дефолты покрывают все действия |

Живые пути событий до `AudioPresenter` сегодня: только `DamageDealtEvent` (Hit) и `UnitDiedEvent` (Death). Остальное — П4.

### 1.2 FMOD

- Плагин FMOD for Unity установлен (`Assets/Plugins/FMOD/`).
- FMOD Studio проект: **`FMOD Project/Guildmaster Autobattler Game.fspro`** (корень репо, рядом с Assets) — **пустой** (только каркас, Master-банки без событий).
- `Assets/StreamingAssets` — пустой, банки в Unity не попадают.

### 1.3 Исходный материал (скачан Максом)

Папка: **`C:\My Program Files\_TEMP\SFX\`**

| Пак | Файлов | Что брать |
|---|---|---|
| `kenney_impact-sounds/Audio` | 133 | `impactPunch_*`/`impactSoft_*` — мили-удары; `impactMetal/Plate_*` — щит/блок; `impactGlass_*` — **death shatter / заморозка-шаттер**; `footstep_*` — резерв |
| `RPG_Essentials_Free` | 49 | `10_Battle_SFX` (slash, impact_flesh, miss/evade!), `8_Atk_Magic_SFX`, `8_Buffs_Heals_SFX`, `10_UI_Menu_SFX`, `12_Player_Movement_SFX` |
| `Sonniss.com-GDC2026-GameAudioBundle` | 350 | прицельно (см. §3, колонка «материал»): Designed Ice (Криомант!), Melee Weapons Pack 2, Historical Weapons Vol.2, Cinematic Fight Vol.1, Elemental Palette, Elemental Mutation Whooshes, Emotion and Magic, Fantasy Game 2, Bass Drops & Downers (тяжёлый удар/финишер), Cinematic Horn Braams (старт боя), Vox Hominis (усилия/смерти людей), Humanoid Creatures Vol.4 (враги), UI-паки Cinematic Sound Design, CB Organic UI (расстановка юнитов), Colossal Impacts / Ultra Transitions (стингеры) |

Явные дыры бесплатного материала: **яд, свет/тьма как выраженные темы**. Правило: берём ближайшее приличное (магия общего вида), дыру фиксируем в ключах каталога — заменится ElevenLabs-генерацией позже. Не блокироваться.

---

## 2. Дизайн-решения (утверждены, не перерешивать)

### 2.1 Формат ключей

Как в `AudioResolver`: `{contentId}.{action}`, contentId — любой стабильный id (`relic.assassin`, `effect.frozen`, `battle.victory`, `ui.pause`). Один ключ = одно FMOD-событие. Все вариации/рандомизация — **внутри** события (multi-instrument), не отдельными ключами.

### 2.2 Расширение `AudioAction`

Итоговый набор действий (существующие + новые):

`Attack` (замах/свинг), `Fire` (выстрел снаряда/muzzle), `Hit` (импакт по цели), `Evade` (уворот/промах), `Shield` (поглощение щитом), `Heal`, `Cast` (способность), `Death`, `Apply` (эффект наложен), `Expire` (эффект кончился), `Tick` (периодика DoT/HoT), `Ui`, `Stinger` (событийные акценты: килл, победа/поражение, старт боя).

Каждое новое действие обязано получить per-action дефолт в каталоге (тест это уже требует). Если при реализации какое-то действие оказывается без единого источника событий — не добавлять его «на будущее», YAGNI.

### 2.3 Иерархия FMOD-событий и банки

```
event:/SFX/Combat/...    (attack, hit, death, cast, ...)
event:/SFX/Relics/{relic}/...
event:/SFX/Effects/{effect}/...
event:/SFX/Feel/...      (kill stinger, heavy-hit bass, finisher, shatter)
event:/SFX/UI/...
event:/Stingers/...      (victory, defeat, battle start)
```

Банк один: `SFX.bank` (+ Master/strings). Дробление банков — не сейчас.

Шины (mixer): `Master → SFX_Combat`, `Master → SFX_UI`, `Master → Stingers`, `Master → Music` (пустая, задел). Слоумо-питч вешается **только** на `SFX_Combat`.

### 2.4 Рандомизация и вариативность (значения по умолчанию)

| Параметр | Значение |
|---|---|
| Вариантов в multi-instrument | 3–5 (меньше 3 — только если материала нет) |
| Питч-рандом: удары/импакты (шумовые) | ±2–3 полутона |
| Питч-рандом: вжухи/whoosh | ±1–2 st |
| Питч-рандом: тональные (магия-«ноты», UI, стингеры) | ±0.5 st или 0 |
| Громкость-рандом | ±3 dB |
| Слоение тяжёлых звуков (килл, финишер, ульты) | transient + body + tail, каждый слой — свой multi-instrument |

### 2.5 Анти-каша плотного боя (event macros, обязательны на каждом боевом событии)

| Категория | Max Instances | Stealing | Cooldown | Priority |
|---|---|---|---|---|
| Автоатака hit/attack/fire | 4 | Quietest | 60 ms | Low |
| Абилки cast, apply | 2 на событие | Virtualize | 50 ms | Medium |
| Death, kill-stinger | 3 | Oldest | 80 ms | High |
| Финишер/победа/поражение | 1 | — | — | Highest |
| UI | 2 | Oldest | 30 ms | High |

Плюс sidechain: `Stingers` дакает `SFX_Combat` на −4..−6 dB. Точные значения — стартовые, крутятся Максом через Live Update.

### 2.6 Слоумо / таймскейл

FMOD **не** следует `Time.timeScale`. Решение: глобальный параметр FMOD `TimeScale` (0.05–3), в Studio он крутит pitch шины `SFX_Combat` по кривой `12·log2(ts)` (при ts=1 → 0 st). Пишет его **только `TimeScaleService`** (он и так единственный писатель таймскейла) — через новый метод на `IAudioService` (например `SetGlobalParameter(name, value)`) или отдельный узкий интерфейс, на вкус исполнителя, но без прямого FMOD-вызова из Game-сборки. Музыка и UI-шины параметром не трогаются. Смена скорости игры (1x/2x/3x) — тем же параметром.

### 2.7 Feel-синхроны

Звуки, привязанные к хукам `CombatFeelDirector` (килл-стингер под слоумо-пульс, бас-слой тяжёлого удара, финишер-стадии) — триггерятся из тех же мест, где уже дёргаются shake/slowmo (Game-слой, `IAudioService` доступен). Death shatter — из `UnitView.StartShatter`-пути через существующие события презентации, НЕ новым прямым вызовом FMOD.

---

## 3. Полный список звуков (ключи каталога)

Колонка «материал» — стартовая подсказка кураторства, не догма. Пометка ◆ = ядро (пакеты 0–5), ◇ = добивка (П5–П6, можно резать по времени).

### 3.1 Per-action дефолты (обязательные, 13 шт.)

| Ключ-дефолт (action) | Материал |
|---|---|
| ◆ Attack | RPG Essentials `22_Slash_*` / Melee Weapons Pack 2 (whoosh-часть) |
| ◆ Fire | Cinematic Fight Vol.1 / Elemental Mutation Whooshes |
| ◆ Hit | Kenney `impactPunch_*` + RPG Essentials `Impact_flesh_*` |
| ◆ Evade | RPG Essentials `35_Miss_Evade_*` |
| ◆ Shield | Kenney `impactMetal_*`/`impactPlate_*` |
| ◆ Heal | RPG Essentials `8_Buffs_Heals_SFX` |
| ◆ Cast | RPG Essentials `8_Atk_Magic_SFX` / Emotion and Magic |
| ◆ Death | Vox Hominis (усилия/предсмертные) + Kenney `impactSoft_*` |
| ◆ Apply | Emotion and Magic / Fantasy Game 2 (короткий бафф-акцент) |
| ◆ Expire | то же, обратный/затухающий |
| ◆ Tick | тихий короткий из Magic/Buffs |
| ◆ Ui | RPG Essentials `10_UI_Menu_SFX` |
| ◆ Stinger | Colossal Impacts / Ultra Transitions |

### 3.2 Ядро боя (поверх дефолтов, где нужна специфика)

| Ключ | Триггер в коде | Материал |
|---|---|---|
| ◆ `combat.hit_melee.hit` | `DamageDealtEvent`, AttackType=Melee | Kenney punch + Dumais Melee |
| ◆ `combat.hit_ranged.hit` | AttackType=Ranged | Historical Weapons (стрела-импакт) |
| ◆ `combat.hit_magic.hit` | DamageType=Magic | Elemental Palette |
| ◆ `combat.shield_absorb.shield` | `DamageResult.ShieldDamage > 0` | Kenney metal/plate |
| ◇ `combat.aoe.cast` | `OnAreaHit` (нужен ивент, П4) | Elemental Mutation Impacts |
| ◇ `combat.knockback_land.hit` | `DisplacementSystem.OnDisplacementEnded` (П4) | Kenney `impactWood_heavy`/dust |
| ◆ `battle.start.stinger` | старт боя (`CombatLoopService`) | **Cinematic Horn Braams** |
| ◆ `battle.victory.stinger` | `BattleEndedEvent`, Outcome=Victory | Fantasy Game 2 / Anime Game (фанфара) |
| ◆ `battle.defeat.stinger` | Outcome=Defeat | то же, минорное / Bass Drops |
| ◇ `enemy.training_dummy.hit` | contentId-ключ | Kenney `impactWood_*` (он деревянный, пусть звучит смешно) |

### 3.3 Реликвии (7 юнитов; attack/hit/death наследуются от дефолтов, ниже — только уникальное)

| Ключ | Момент | Материал |
|---|---|---|
| ◇ `relic.iron_spearman.attack` | выпад копья (пирс) | Historical Weapons / Dumais |
| ◇ `relic.iron_spearman.cast` | активка | Cinematic Fight |
| ◆ `relic.light_shepherd.fire` | хил-снаряд, полёт | Emotion and Magic (светлое) |
| ◆ `relic.light_shepherd.heal` | хил-импакт | Buffs_Heals |
| ◇ `relic.light_shepherd.cast` | ульта «Длань жизни» | Emotion and Magic, слоёное |
| ◆ `relic.cryomancer.attack` | фрост-атака | **100 kHz Designed Ice** |
| ◆ `effect.frozen.apply` | заморозка наложена | Designed Ice (кристаллизация) |
| ◆ `relic.cryomancer.cast` | «Ледяные оковы» масс-стан | Designed Ice + Colossal Impact |
| ◆ `effect.frozen.expire` | шаттер/спад | Kenney `impactGlass_*` + Ice |
| ◇ `relic.defender.shield` | Оплот: щит-ап | metal + magic-слой |
| ◇ `relic.defender.cast` | ульта | Cinematic Fight |
| ◆ `relic.ranger.fire` | выстрел лука | Historical Weapons (bow) |
| ◇ `effect.hunters_mark.apply` | метка (+перескок — тот же ключ) | Emotion and Magic (короткий акцент) |
| ◆ `relic.assassin.cast` | уход в стелс / ре-стелс | Air Designed / Elemental Mutation (vanish-whoosh) |
| ◇ `relic.assassin.attack` | усиленный удар из стелса | slash + bass-слой |
| ◆ `relic.whirl_monk.cast` | дэш | Elemental Mutation Whooshes |
| ◇ `relic.whirl_monk.hit` | кнокбэк-«ядро»/приземление | Colossal Impacts (лёгкая версия) |
| ◇ `effect.vortex_entry.apply` | вихрь-телепорт | Air Designed |

### 3.4 Эффекты/статусы (общие)

◇ `effect.{id}.apply/expire` для: стан (`ResoluteStrikeStun`, `IceChainsStun`), стелс-бафф, dodge, weaken/дебафф, bulwark-щит. DoT/HoT-тики — дефолт `Tick` (пер-эффектные — потом).

### 3.5 Gamefeel-слой

| Ключ | Триггер | Материал |
|---|---|---|
| ◆ `feel.kill.stinger` | `CombatFeelDirector` kill-пульс | Bass Drops + transient |
| ◆ `feel.heavy_hit.hit` | heavy-hit shake (≥ HeavyHitFrac) | Bass Drops (только бас-слой поверх обычного hit) |
| ◆ `feel.finisher.stinger` | финишер боя (4 стадии — звук на вход в слоумо) | Ultra Transitions + Bass Downers |
| ◆ `feel.death_shatter.death` | `UnitView` shatter | Kenney `impactGlass_heavy` (свой multi) |

### 3.6 UI / мета

| Ключ | Триггер | Материал |
|---|---|---|
| ◆ `ui.pause.ui` / `ui.resume.ui` | `BattleInputController.OnPauseToggle` | UI Interaction Elements |
| ◇ `ui.speed.ui` | смена скорости («.») | System & UI Feedback |
| ◇ `ui.camera.ui` | Tab-камера | лёгкий whoosh из UI-паков |
| ◇ `ui.deploy_place.ui` | расстановка юнита (`DeploymentService`) | **CB Organic UI** (building-звуки) |
| ◇ `ui.unit_spawn.ui` | `UnitSpawnedEvent` | Fantasy Game 2 |

«Мана полная / абилка готова» — **вне скоупа** (нет события в симе; отдельная задача).

---

## 4. Пакеты работ

### П0 — Кураторство и staging
Создать staging **вне Assets**: `FMOD Project/SourceAudio/` (в .gitignore? — НЕТ, коммитим: приватный реп, см. §0.8; но без Sonniss-россыпи целиком — только отобранные файлы). Прогнать паки, отобрать материал по §3, переименовать под конвенцию батч-импорта: `{Category}/{event_name}_multi_{N}.wav` (N=1..5). Конвертация в wav 44.1k/16bit где нужно (ffmpeg), опционально — грубая нормализация громкости по категориям. Итог: манифест `manifest.json` (ключ каталога → event-путь → файлы) — он же вход П1 и источник П5.

### П1 — Скриптовая заливка FMOD
JS-скрипт в `FMOD Project/Scripts/populate.js` (FMOD Studio Scripting API): читает манифест, создаёт иерархию событий §2.3, multi-instruments с файлами, рандом-модуляторы питча/громкости (§2.4), event macros (§2.5), шины и банк SFX. Референс-имплементация: `github.com/synnys/FMOD-Studio-Batch-Audio-Import-Script`. Запуск: `fmodstudio -script` (или из GUI-меню Scripts первый раз). **Скрипт идемпотентен** (повторный прогон обновляет, не дублирует) — это критично для будущей замены wav.

### П2 — Банки в Unity
Сборка банков **отдельным** вызовом `fmodstudio -build` (известный готча: build() изнутри -script исторически глючил). FMOD for Unity settings → указать на `.fspro`, банки в `Assets/StreamingAssets`. Экспорт GUIDs (File → Export GUIDs) — файл идёт в П5. Проверка: Unity без ошибок FMOD в консоли, event browser видит события.

### П3 — FmodAudioService + включение каркаса
`FmodAudioService : IAudioService` (Game-сборка, рядом с `UnityAudioService`): `Play(key)` → resolver → `EventReference` → one-shot (позиционность пока не нужна — 2D). Свап регистрации в `RootLifetimeScope`. Регистрация `AudioPresenter` в `CombatLifetimeScope.RegisterPresentation`. Смоук: Hit и Death реально звучат в бою. + `SetGlobalParameter` для §2.6, `TimeScaleService` пишет `TimeScale`-параметр.

### П4 — Недостающие события
Прокинуть в MessagePipe (по образцу `DamageDealtEvent`): AttackStarted, AbilityCast, Healed, AttackEvaded, ProjectileSpawned, AreaHit, DisplacementEnded, EffectApplied, EffectExpired (+ поле `EffectTag`/effectId). Подписки в `AudioPresenter` с маппингом на действия §2.2 (расширить enum + дефолты + тесты). UI-звуки: подписки на существующие команды/инпут-события. Feel-синхроны — вызовы `IAudioService` из `CombatFeelDirector` (§2.7). Battle start/victory/defeat — из `BattleEndedEvent` и старта `CombatLoopService`.

### П5 — Наполнение AudioCatalog
Из манифеста П0 + GUIDs П2 заполнить `AudioCatalog.asset`: все ◆-ключи §3 + 13 дефолтов. Программно (editor-скрипт из манифеста), не руками. Проверка: voicing-матрица ContentHub — существующие юниты без красных дыр; тест «дефолты покрывают все действия» зелёный.

### П6 — Микс и добивка
◇-ключи по остаточному времени. Sidechain-дакинг, финишер-снапшот, слоумо-кривая питча, балансировка категорий по громкости (грубая). Инструкция Максу для Live Update-сессии.

### П7 — Тесты и приёмка
EditMode: валидация каталога (существующие тесты + новые на новые действия), валидация манифеста (все ключи каталога имеют событие в GUIDs-экспорте — ловит рассинхрон FMOD↔каталог). Обновить code-guide/changelog (файл 07 вики). Финальный чек-лист Максу.

---

## 5. Приёмка (что слушает Макс)

1. Бой с 2×2+ юнитами: удары/смерти/хилы звучат, **каши нет** (одновременные удары не сливаются во флэнджер).
2. Слоумо-килл: звук «смазывается» вниз вместе со временем, музыкальных/UI-артефактов нет; 2x/3x скорость — звук питчится вверх терпимо.
3. Победа/поражение/старт боя — стингеры, бой под стингером приглушается.
4. Криомант звучит льдом, Монах — вжухами, Пастырь — светом (узнаваемость хотя бы этих трёх).
5. Пауза/резюм/расстановка — UI-отклик.
6. Ничто не звучит дважды «в фазе» и ничего не молчит (voicing-матрица зелёная по ◆).

## 6. Известные риски

- `fmodstudio -script`/`-build` двухфазность (§П2) — не пытаться собрать банки из populate-скрипта.
- Питч тональных звуков против будущей музыки — потому ±0.5 st (§2.4), не расширять.
- Пустой `Assets/StreamingAssets` в git: Unity может не хранить пустую папку — банки её создадут, проверить .meta.
- Sonniss-файлы длинные/дизайнерские — резать до коротких one-shot'ов при кураторстве (П0), не кидать 10-секундные файлы в multi-instrument.
- FMOD Studio должен быть установлен локально (проверить наличие `fmodstudio.exe` до П1; если нет — стоп, сказать Максу).
