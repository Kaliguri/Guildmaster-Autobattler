---
title: "Planning - SFX раунд 2 (покрытие, микс, громкость)"
order: 81
status: needs_review
updated: 2026-07-26
---

**Статус:** пакеты A–E ВЫПОЛНЕНЫ 2026-07-26 (скоуп «делаем всё», решение Макса). Ждёт
приёмки на слух — что слушать, см. §8. Разделы 1–2 описывают состояние ДО работы и
оставлены как объяснение, почему всё сделано именно так.

---

> Аудит звукового слоя на 2026-07-26. Раунд 1 ([[sfx|Planning - SFX (FMOD)]]) поднял каркас:
> фасад, резолвер, каталог, 31 FMOD-событие, банки. Этот документ фиксирует, **что из
> раунда 1 осталось недоделанным**, **где игра немая**, и **как привести SFX к одному
> уровню громкости**. Контур — `xgaida-x-nixi-audio`.

---

## 1. Что звучит сегодня (факты, не память)

| Слой | Состояние |
|---|---|
| FMOD-события | 31 (`event:/SFX/Combat/*`, `Relics/*`, `Effects/*`, `Feel/*`, `UI/*`, `event:/Stingers/*`) |
| Исходных сэмплов | 58 (Kenney impact-sounds `.ogg` + RPG Essentials Free `.wav`) |
| `AudioCatalog.asset` | 16 точных ключей + 13 per-action дефолтов |
| Реально играет | **только бой**: hit / shield / death / heal / evade / attack / fire / cast + victory/defeat/kill-стингеры |
| Вне боя | один вызов `LoadoutViewModel.cs:67` (и тот мёртвый, см. §2.6) |
| Музыка / амбиент | **нет вообще** |
| Банки | `Master.bank`, `Master.strings.bank`, `SFX.bank` от 2026-07-13, консистентны с проектом |

Пайплайн раунда 1 живой и воспроизводимый: манифест → `FMOD Project/Tooling/populate.js` →
`fmodstudiocl -script` → `fmodstudiocl -build`. FMOD Studio 2.03.14 установлен локально.

---

## 2. Диагностика: что сломано или недоделано

### 2.1. Под-шин нет — слайдеры «Музыка» и «Звук» ничего не делают (P0)

`FmodAudioService` пишет громкость в `bus:/Music` и `bus:/SFX` (`FmodAudioService.cs:34-36`).
В FMOD-проекте таких шин **не существует**: есть три группы `SFX_Combat`, `SFX_UI`,
`Stingers` — и **все 31 событие роутятся мимо них, прямо в Master Bus**
(`mixerInput.output → MixerMaster` в каждом `Metadata/Event/*.xml`).

Следствие: работает только слайдер «Общий»; «Музыка» и «Звук» — тихий no-op
(`SetBusVolume` глотает невалидную шину). Решение [[audio-subbuses]] («под-шины заложить
рано») формально принято, фактически не выполнено.

### 2.2. Разброс громкости 19.3 dB (P0)

Замер всех 58 исходников (ffmpeg: RMS активной части, порог тишины −45 dB):

| Пак | RMS активной части | Пик |
|---|---|---|
| Kenney impact-sounds | −15.1 … −24.7 dB | −0.8 … −1.4 dB (пик-нормализованы) |
| RPG Essentials Free | −23.0 … −34.4 dB | −3.7 … −19.7 dB |

Разброс **19.3 dB** между самым громким (`impactPunch_medium_001`) и самым тихим
(`56_Attack_03`). Ни на одном событии нет volume-фейдера (все на 0 dB), так что разброс
идёт на выход как есть. Хуже: **внутри одного мульти-инструмента** лежат сэмплы из обоих
паков — `event:/SFX/Combat/hit` содержит Kenney (−15 dB) и RPG (−27…−31 dB), то есть один
и тот же удар звучит то в полный голос, то вдвое тише. То же в `shield`, `death`, `attack`.

### 2.3. Глобального параметра `TimeScale` в FMOD-проекте нет (P1)

`TimeScaleService.cs:208` исправно шлёт `AudioParameters.TimeScale` (0.05…3) в
`RuntimeManager.StudioSystem.setParameterByName`. В проекте FMOD **нет ни одного параметра**
(`Metadata/ParameterPreset/` пуст), автоматизации питча тоже нет. Слоумо на добивании и
скорости 2x/3x сейчас **не слышны** — звук идёт как при 1x.

### 2.4. Нет рандомизации и анти-каши (P1)

`populate.js` умеет только события, мульти-инструменты, банк и `maxVoices`. Категории в
манифесте (`pitchSemitones`, `volumeDb`, `cooldownMs`, `stealing`, `priority`) объявлены, но
**не применяются** — в скрипте это помечено как «GUI PASS». В метаданных нет ни одного
модулятора. Следствие: «пулемётный» повтор одинакового сэмпла в плотном бою, никакого
кулдауна и разумного voice stealing (только `maxVoices`).

### 2.5. Звук существует только внутри боя (P0 для покрытия)

`AudioPresenter` зарегистрирован в `CombatLifetimeScope.cs:69` (Scoped) и умирает вместе с
`BattleScene`. `WorldLifetimeScope` аудио не регистрирует. Меню, карта акта, награда,
магазин, привал, инвентарь, переходы — немые полностью. `IAudioService` при этом синглтон в
Root (`RootLifetimeScope.cs:74`), то есть **шов для root-презентера уже есть**.

### 2.6. Мёртвые ключи и хуки

| Что | Где | Почему мёртвое |
|---|---|---|
| `{relicId}.select` | `LoadoutViewModel.cs:67` | `select` — не `AudioAction`, фолбэка на дефолт нет → всегда тишина |
| `onCardSelectSound` | `RewardScreenView.cs:36,109` | `MenuRouter.cs:613-623` параметр не передаёт |
| `ui.pause.ui`, `ui.resume.ui`, `ui.deploy_place.ui` | каталог + FMOD | события созданы, **никто их не играет** |
| `AudioAction.Ui`, `Apply`, `Expire`, `Tick` | enum + резолвер + дефолты | ни одного вызова в коде |
| kill-стингер | `AudioPresenter.cs:70` | без кулдауна (в отличие от `CombatFeelDirector.KillSlowCooldown`) — пачка добиваний наложится сама на себя |
| `IAudioService.Stop` | `FmodAudioService.cs:28-32` | no-op, хендлы не хранятся → лупы/музыка/каналинг технически невозможны |

### 2.7. Пайплайн не воспроизводится на другой машине (P2)

`manifest.sourceRoot = C:/My Program Files/_TEMP/SFX` — папка вне репозитория. При этом
паки **уже лежат в репо**: `Assets/Kenney/kenney_impact-sounds`, `kenney_interface-sounds`
(100 файлов UI, CC0), `RPG_Essentials_Free`. Правильный дом для отобранного и
нормализованного материала — `FMOD Project/SourceAudio/` (§3.3).

---

## 3. Громкость: метод и целевые числа

### 3.1. Целевой уровень

**−23 dB RMS активной части, true peak ≤ −1 dBFS.** Активная часть = сигнал выше −45 dB
(для one-shot интегральный LUFS не считается — гейтинг EBU R128 требует материала длиннее
0.4 с, а у нас половина сэмплов короче).

Почему −23: при этой цели пиковый потолок ограничивает всего 4 файла из 58, а разброс
воспринимаемой громкости схлопывается **19.3 dB → 1.6 dB**. Более громкие цели упираются в
пик-нормализованный Kenney (−20 → 18 ограниченных файлов, разброс 4.6 dB). Формула гейна:
`gain = min(target − rms, −1 − peak)` — громкость никогда не покупается клиппингом.

Число совпадает с вещательным ориентиром EBU R128 (−23 LUFS) не по необходимости, а по
удобству: запас до 0 dBFS остаётся микшеру, а не сэмплам.

### 3.2. Слои громкости (кто за что отвечает)

1. **Сэмпл** — нормализован к −23 dB (одинаковый старт для всех, §3.1).
2. **Событие FMOD** — художественный offset (тычка тише ульты), крутится по слуху.
3. **Категория/под-шина** — баланс групп (§3.4).
4. **Слайдеры игрока** — `bus:/`, `bus:/Music`, `bus:/SFX`.

Свой C#-инструмент сведения не строим — это решение [[audio-subbuses]], микс живёт в FMOD.

### 3.3. Где живут исходники

`FMOD Project/SourceAudio/{category}/{event}_{NN}.wav` — нормализованные копии в репо,
`manifest.sourceRoot` указывает на них. Тогда populate воспроизводится на любой машине, а
пере-нормализация — один прогон скрипта.

### 3.4. Стартовые уровни шин и категорий (для Live Update)

```
bus:/                    0 dB
├── bus:/Music          −6 dB   (задел, пока пусто)
└── bus:/SFX             0 dB
    ├── SFX/Combat       0 dB   ← якорь микса (боевой импакт)
    ├── SFX/UI          −5 dB   (UI не должен спорить с боем)
    ├── SFX/Ambient     −9 dB   (задел)
    └── SFX/Stingers    +1 dB, sidechain-дак Combat на −5 dB
```

Категорийные offset'ы поверх нормализованного сэмпла: `impact` 0, `whoosh` −2,
`cast` −1, `tonal` −3, `death` 0, `ui` −2, `stinger` 0.

### 3.5. Рандомизация (анти-«пулемёт»)

| Категория | Pitch | Volume |
|---|---|---|
| impact / death | ±2.5 st | −3 dB |
| whoosh | ±1.5 st | −3 dB |
| tonal / cast | ±0.5…1 st | −2 dB |
| ui | ±1 st | −2 dB |
| stinger | 0 | 0 |

(Random-модулятор громкости в FMOD всегда отнимает — потому цель нормализации берётся с
запасом на этот минус.)

### 3.6. Слоумо

Параметр `TimeScale` (0.05…3, дефолт 1) → автоматизация питча `SFX/Combat` по
`12·log2(ts)` (0 st при ts=1). Музыка, UI и стингеры параметром не трогаются.

---

## 4. Реестр недостающих звуков

Ключи — по канону `{contentId}.{action}`. Приоритет: **P0** — без этого игра звучит
сломанной, **P1** — заметная дыра, **P2** — полировка. Колонка «материал» — что взять из
уже лежащего в репо (Kenney interface-sounds `ki`, Kenney impact-sounds `kimp`, RPG
Essentials `rpg`, Sonniss GDC `son`).

### 4.1. Бой: то, чего нет в симуляции (нужны швы, §5)

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `battle.start.stinger` | старт боя (событие есть, вызова нет) | P0 | `son` Horn Braams / `rpg 55_Encounter` |
| `effect.{id}.apply` | наложен стан/заморозка/щит/яд/баф/стелс | P0 | `rpg 8_Buffs_Heals`, `son` Magic |
| `effect.{id}.expire` | эффект спал | P1 | то же, затухающее |
| `effect.{id}.tick` | тик DoT/HoT (сейчас звучит как обычный hit) | P1 | `rpg 46_Poison` |
| `combat.unit_spawn.ui` | юнит появился на арене | P1 | `ki` pluck / `son` Organic UI |
| `combat.attack_interrupted.evade` | замах сорван станом | P2 | `ki` scratch |
| `combat.knockback_start.attack` | юнита отбросило | P1 | `kimp` whoosh-слой |
| `combat.knockback_land.hit` | приземление после полёта | P1 | `kimp impactWood_heavy` |
| `combat.projectile_miss.evade` | снаряд ушёл в никуда | P2 | `ki` swish |
| `combat.aoe.cast` | зона удара сработала (`AreaHit`) | P2 | `son` Elemental Impacts |
| `combat.step.ui` | шаги (хук contact-dust уже с кулдауном) | P2 | `kimp footstep_*` |
| `battle.reset.stinger` | бой перезапущен (dev-R) — нужен и как глушилка хвостов | P2 | — |

### 4.2. Feel-слой (визуал уже есть, звука нет)

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `feel.death_shatter.death` | разлёт на осколки (`UnitView.cs:1032`) — **самый громкий кадр и он немой** | P0 | `kimp impactGlass_heavy` (событие уже создано!) |
| `feel.heavy_hit.hit` | удар выше `HeavyHitFrac` | P0 | `kimp impactPunch_heavy` (событие есть) |
| `feel.finisher.stinger` | вход в финишер-слоумо | P1 | `son` Bass Downers |
| `feel.death_anticipate.tick` | дрожь перед разлётом | P2 | `son` low rumble |

`feel.kill.stinger` уже играет — но нужен кулдаун (§2.6).

### 4.3. Расстановка отряда

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `ui.deploy_grab.ui` | взял юнита | P0 | `ki` drop_001 (обратный) / `rpg 070_Equip` |
| `ui.deploy_place.ui` | поставил (событие есть, вызова нет) | P0 | `rpg 070_Equip` |
| `ui.deploy_reject.ui` | невалидная клетка (ветки нет в коде) | P0 | `ki` error_00X |
| `ui.deploy_valid.ui` | призрак вошёл в валидную зону (edge) | P2 | `ki` tick_00X, тихо |
| `ui.relic_equip.ui` | реликвия надета на юнита | P0 | `rpg 070_Equip_10` |
| `ui.relic_unequip.ui` | реликвия снята | P1 | `rpg 071_Unequip_01` |
| `ui.deploy_ready.stinger` | нажали «Начать» | P0 | `ki` confirmation |

### 4.4. Карта акта (сейчас немая целиком)

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `map.node_hover.ui` | наведение на узел | P0 | `rpg 001_Hover_01` |
| `map.node_select.ui` | выбран доступный узел | P0 | `ki` select_00X |
| `map.node_locked.ui` | клик по недоступному (nudge уже есть) | P0 | `ki` error / `rpg 033_Denied` |
| `map.travel_start.ui` | фишка отряда поехала | P1 | `ki` scratch, тихо |
| `map.travel_arrive.ui` | приехала в узел | P1 | `ki` bong |
| `map.reveal.ui` | открылись новые узлы / разошлась дымка | P1 | `son` Organic UI |
| `map.open.ui` / `map.close.ui` | карта показана / убрана | P1 | `ki` open / close |
| `map.path_dot.tick` | бегущая волна по дорожке (по темпу) | P2 | `ki` tick, очень тихо |

### 4.5. Экраны забега и переходы

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `flow.fade_in.ui` / `flow.fade_out.ui` | шторка-чернила закрылась/открылась | P0 | `son` Ultra Transitions |
| `flow.node_enter.stinger` | вход в узел (по типу узла) | P1 | `son` Transitions |
| `screen.open.ui` / `screen.close.ui` | любой Push/Pop экрана (один шов) | P0 | `ki` open_00X / close_00X |
| `screen.modal_open.ui` | модалка поверх | P1 | `ki` maximize |
| `reward.open.stinger` | открылся экран награды | P0 | `rpg 013_Confirm` + `son` fanfare |
| `reward.card_select.ui` | клик по карточке награды (хук мёртв) | P0 | `ki` select |
| `reward.take.stinger` | реликвия взята | P0 | `rpg 079_Buy_sell` / `son` Magic |
| `reward.skip.ui` | награда пропущена | P1 | `ki` back |
| `run.gold_gain.ui` | начислено золото | P0 | `ki` coin / `rpg 079_Buy_sell` |
| `shop.buy.ui` / `shop.sell.ui` / `shop.reroll.ui` | покупка / продажа / реролл | P1 | `rpg 079_Buy_sell`, `ki` switch |
| `shop.denied.ui` | не хватает золота / нет места | P1 | `rpg 033_Denied` |
| `chest.open.stinger` | крышка сундука откинулась | P1 | `kimp impactWood_heavy` + `son` |
| `camp.action.ui` / `camp.denied.ui` | действие привала / не по карману | P1 | `ki` confirmation / error |
| `event.choice.ui` | выбран вариант текстового ивента | P1 | `ki` select |
| `event.result.ui` | появился текст последствия | P2 | `ki` pluck |
| `run.outcome_victory.stinger` / `run.outcome_defeat.stinger` | экран исхода забега | P0 | `son` Colossal / Bass Downers |
| `run.start.stinger` | забег начался (отряд встал в мир) | P1 | `son` Horn |
| `menu.title_card.stinger` | тайтл-кард | P1 | `son` Transitions |
| `menu.show.ui` / `menu.hide.ui` | главное меню появилось/ушло | P1 | `ki` open / close |

### 4.6. UI-мелочь (один шов на корне панели, §5.2)

| Ключ | Момент | При | Материал |
|---|---|---|---|
| `ui.click.ui` | любая кнопка | P0 | `ki click_00X` |
| `ui.hover.ui` | наведение на интерактив | P0 | `rpg 001_Hover_01`, очень тихо |
| `ui.tab.ui` | переключение таба/режима топбара | P0 | `ki switch_00X` |
| `ui.toggle.ui` | тумблер настроек | P1 | `ki toggle_00X` |
| `ui.slider.ui` | шаг слайдера (по отпусканию, не покадрово) | P1 | `ki tick_00X` |
| `ui.slider_test.ui` | тестовый бип на слайдере «Звук» | P1 | дефолт `ui` |
| `ui.back.ui` | «Назад»/«Отмена»/ESC | P0 | `ki back_00X` |
| `ui.disabled.ui` | клик по недоступному действию | P1 | `ki error_00X` |
| `ui.tooltip_show.ui` | тултип раскрылся | P1 | `ki` pluck, очень тихо |
| `ui.tooltip_detail.ui` | Shift → подробный режим | P2 | `ki` scratch («шелест страницы») |
| `ui.drag_grab.ui` / `ui.drag_drop.ui` / `ui.drag_reject.ui` | драг в UITK: взял / положил / мимо | P0 | `ki` drop / error |
| `ui.scroll.ui` | прокрутка списка (колбэков ещё нет) | P2 | `ki scroll_00X` |
| `ui.pause.ui` / `ui.resume.ui` | пауза боя (события есть, вызовов нет) | P0 | уже в проекте |
| `ui.speed.ui` | смена скорости 1x/2x/3x | P1 | `ki tick` |
| `ui.camera.ui` | смена режима камеры (Tab) | P2 | `ki` swish |
| `ui.quit.ui` | выход из игры | P2 | `ki` close |

### 4.7. Амбиент и музыка (требует хранимых `EventInstance`, §5.4)

| Ключ | Момент | При |
|---|---|---|
| `music.menu.loop` | главное меню | P1 |
| `music.map.loop` | карта акта | P1 |
| `music.battle.loop` | бой (со слоями по интенсивности — [[backlog-audio-sfx]]) | P1 |
| `ambient.arena.loop` | фон арены | P2 |
| `ambient.map.loop` | фон карты | P2 |

**Итого в реестре: ~85 ключей**, из них P0 — 27.

---

## 5. Швов в коде не хватает

### 5.1. Симуляция (нужны новые события)

| Шов | Где | Зачем |
|---|---|---|
| `EffectSystem.OnEffectApplied(unit, def, source)` | `EffectSystem.cs:101` (+ стакинг `:380`, мгновенный `:154`) | открывает **весь** слой статусов разом; `def.Id` уже под рукой |
| `OnEffectExpired` → передавать `def`, не только `EffectTag` | `EffectSystem.cs:33,331` | без id ключ `effect.{id}.expire` не собрать |
| `DisplacementSystem`: старт полёта | `DisplacementSystem.cs:50` | приземление уже есть (`:107`) |
| `ProjectileSystem`: промах/деспавн | `ProjectileSystem.cs:81` | сейчас снаряд исчезает беззвучно |
| `AreaHit` → добавить источник/`contentId` | `Combat/AreaHit.cs:11` | сейчас структура не несёт id, ключ не собрать |

### 5.2. UI: один шов вместо сотни вызовов

`UiSoundSystem.Attach(_doc.rootVisualElement)` рядом с `UiRootBootstrap.cs:208` (тем же
приёмом, что `TooltipSystem.Attach`): `ClickEvent` / `PointerEnterEvent` на корне панели,
разбор по USS-классу (`.gm-button`, `.gm-chip`, `.gm-card`, `.gm-slot`, `.gm-tab`).
Озвучивает **все** клики и ховеры разом, без правок в экранах.

Второй уровень (если нужен разный звук): компонентные базы `Chip`, `Slot`, `RelicCard`,
`VesselCard`, `SliderRow`, `ToggleRow`, `UiDragDrop` — по одному месту на семейство.

Открытие/закрытие экранов — `UiNavigator.Push/Pop/RemoveScreen/PopAll`
(`UiNavigator.cs:120,143,250,171`), там же виден `ScreenKind`.

### 5.3. Флоу забега: root-презентер на MessagePipe

`RunAudioPresenter` в Root-скоупе, подписки на готовые события:
`OpenRewardRequest`, `OpenShopRequest`, `OpenChestRequest`, `OpenCampRequest`,
`OpenTextEventRequest`, `OpenContinueRequest`, `OpenNodeFarewellRequest`,
`OpenOutcomeRequest`, `OpenMainMenuRequest`, `MainMenuVisibilityChangedEvent`,
`OpenTitleCardRequest`, `RunPartyReadyEvent`, `WorldMapSpaceChangedEvent`,
`TestZoneChangedEvent`, `RelicDragEvent`, `ScreenFadeChangedEvent`, `BattleEndedEvent`.

Плюс `IBattleSession.PhaseChanged` (`BattleSession.cs:189`) — один C#-event закрывает
`None → Deployment → Fighting → Interlude`, то есть старт расстановки, **старт боя**,
конец боя и передышку. Живёт в Root → переживает бой.

Карта акта и `DeploymentController` наружу ничего не шлют — там нужны локальные вызовы
(hover/select/locked/grab/place/reject) либо новые события.

### 5.4. Фасад: лупы

`IAudioService.Stop` — no-op. Для музыки/амбиента нужен словарь `key → EventInstance` в
`FmodAudioService` (Play с `create+start`, Stop с `stop(ALLOWFADEOUT)+release`). Без этого
пункт 4.7 нереализуем.

---

## 6. Пакеты работ

| Пакет | Содержание | Итог |
|---|---|---|
| **A. Микс** | нормализация исходников в `SourceAudio` (−23 dB / −1 dBTP), шины `SFX`+`Music`+под-шины, роутинг событий, категорийные offset'ы, рандом-модуляторы, cooldown/stealing, параметр `TimeScale` + питч-кривая, пересборка банков | SFX на одном уровне, слайдеры работают, слоумо слышно |
| **B. Швы** | `UiSoundSystem` на корне панели, `RunAudioPresenter` на MessagePipe, `PhaseChanged`, `OnEffectApplied`, death-shatter/heavy-hit хуки, кулдаун kill-стингера, починка `{relicId}.select` и `onCardSelectSound` | игра перестаёт быть немой вне боя |
| **C. Контент** | отбор материала под ~85 ключей, populate, наполнение каталога, voicing-матрица зелёная | звучит всё из §4 |
| **D. Лупы** | `EventInstance`-словарь, музыка меню/карты/боя, амбиент | появляется музыка |
| **E. Тесты** | валидация «каталог ↔ GUIDs банка», тест «у каждого действия есть дефолт», тест ключей-сирот | рассинхрон ловится в CI |

---

## 7. Что сделано (2026-07-26)

### 7.1. Пайплайн

`scripts/audio/audio_map.py` — единственный источник правды: ключ → категория → исходные
сэмплы. Из него:

```
audio_map.py
  → build_source_audio.py   нормализация (-23 dB RMS / -1 dBTP) → FMOD Project/SourceAudio + manifest.json
  → build_populate.py       → FMOD Project/Tooling/populate.js
  → fmodstudiocl -script    шины, события, микс, параметр TimeScale
  → fmodstudiocl -build     банки → Assets/StreamingAssets
  → Alebardium/Audio/Populate Catalog from Manifest   → AudioCatalog.asset
```

`SourceAudio` — промежуточный артефакт (в `.gitignore`): FMOD при импорте кладёт свою копию
в `FMOD Project/Assets`, и версионируется именно она. Музыкальное сырьё — `FMOD Project/MusicSource`.

**Итог:** 109 событий, 211 сэмплов, 95 точных ключей + 14 дефолтов, 0 пустых ссылок.

### 7.2. Микс

- Шины `bus:/SFX/{Combat,UI,Ambient,Stingers}` + `bus:/Music`; события роутятся по категории.
  Слайдеры «Общий/Музыка/Звук» теперь доходят до звука.
- Громкость: сэмплы выровнены, поверх — категорийные offset'ы (UI −2, ui_soft −9, tonal −3…),
  поверх — фейдеры шин (UI −5, Ambient −9, Stingers +1, Music −6).
- Анти-каша: на каждом событии `maxVoices` + `stealing` + `cooldown` + `priority` по категории
  (удары 4/Quietest/50 мс/Low, касты 2/Virtualize, стингеры 1/None/Highest), плюс рандомизация
  питча (±0.5…2.5 st) и громкости (−2…−3 dB).
- Слоумо: глобальный параметр `TimeScale` (0.05…3) с кривой питча на `bus:/SFX/Combat`.

### 7.3. Код

| Шов | Где |
|---|---|
| Клики/наведения/табы/тумблеры/слайдеры/скролл — один слушатель на корне панели | `UI/UiSoundSystem.cs`, привязка в `UiRootBootstrap` |
| Экраны, карта, шторка, исход, меню, музыка (root-скоуп, переживает бой) | `Game/Services/RunAudioPresenter.cs` |
| Открытие/закрытие любого экрана | `UiNavigator.Push/Pop` |
| Статусы: наложен/спал с id эффекта | `EffectSystem.OnEffectApplied` / `OnEffectEnded` → `AudioPresenter` |
| Килл-стингер (под кулдауном слоумо), тяжёлый удар, финишер | `CombatFeelDirector` |
| Разлёт на осколки | `UnitView.StartShatter` |
| Узлы карты: наведение/выбор/недоступен/поездка | `WorldMapView` |
| Расстановка: взял/поставил/отказ, реликвия надета | `DeploymentController` |
| Пауза, скорость | `BattleInputController` |
| Лавка, привал, сундук, ивент, награда, золото | `ShopController`, `CampScreenView`, `MenuRouter`, `RunStateService` |
| Петли (музыка/амбиент) | `FmodAudioService` держит `EventInstance`; `Stop`/`StopAll` |

### 7.4. Тесты

`Assets/_Project/Tests/EditMode/Audio/AudioCoverageTests.cs`:

1. каждый ключ, который **зовёт код** (литералы `Play("…")` и `PlayUi("…")`), резолвится в каталоге;
2. каталог совпадает с FMOD-манифестом в обе стороны (ловит «пересобрал FMOD, забыл каталог»);
3. в каталоге нет пустых `EventReference`.

Полный EditMode-прогон после работы: **539/539**.

### 7.5. Материал и лицензии

| Пак | Лицензия | Где |
|---|---|---|
| Kenney: impact-sounds, interface-sounds, **ui-audio**, **rpg-audio**, **music-jingles** | CC0 | `Assets/Kenney/*` (последние три докачаны 2026-07-26) |
| RPG Essentials Free (Leohpaz) | бесплатно в коммерческих, кредит опционален | `Assets/Kenney/RPG_Essentials_Free` |
| Музыка: Fantasy Orchestral Theme, Town Theme (cynicmusic), Battle Theme A (cynicmusic), Loopable Dungeon Ambience | CC0, OpenGameArt | `FMOD Project/MusicSource` |

Sonniss GDC не использовался: длинные дизайнерские файлы требуют нарезки на слух, а Kenney
CC0 закрыл все P0-дыры.

## 8. Приёмка: что слушать

1. **Бой 2×2+:** удары/смерти/хилы примерно одной громкости, каши нет; пачка добиваний даёт
   один стингер, а не наложение.
2. **Слоумо на добивании и скорость 2×/3×:** боевая шина едет по питчу, UI и музыка — нет.
3. **Статусы:** заморозка/стан/яд/щит слышны отдельно от обычного удара.
4. **Разлёт на осколки** звучит после смерти, а не вместе с ней.
5. **Интерфейс:** клик, наведение, табы, драг-дроп, отказ (недоступный узел, дроп мимо, покупка
   без золота) — отличимы на слух и не спорят с боем по громкости.
6. **Карта:** наведение на узел, выбор, недоступный узел, приезд фишки.
7. **Музыка:** меню → карта → бой переключаются без наложения; в исходе забега музыка уходит.
8. **Слайдеры настроек** реально меняют громкость (раньше работал только «Общий»).

Что крутить, если что-то не так: числа в `scripts/audio/audio_map.py` (`CATEGORIES`, `BUS_TREE`,
`TARGET_RMS_DB`) → перегон пайплайна; либо живьём через FMOD Live Update, а потом перенести
значения в карту, чтобы следующий прогон их не затёр.

## 9. Осталось (следующий заход)

- **Тонкая настройка на слух** — стартовые числа микса выставлены расчётом, не ухом.
- **Sidechain-дакинг** стингеров под боевую шину и mixer snapshot под boss-intro
  ([[audio-subbuses]] §Бонус-стык) — в скрипте пока нет.
- **Слои импакта** (transient/body/tail) и intensity-слои ударов — [[backlog-audio-sfx]] §1.
- **Адаптивная музыка** (vertical layering по интенсивности боя) — сейчас одна дорожка за раз.
- **Позиционность**: весь звук 2D; пан по позиции юнита на арене — [[backlog-audio-sfx]] §5.
- Немыми осознанно оставлены: шаги, промах снаряда, приземление после отбрасывания, area-hit
  (в `AreaHit` нет id источника), смена режима камеры, выход из игры.

## Связи

- [[sfx|Planning - SFX (FMOD)]] — ТЗ раунда 1 (каркас).
- [[audio-subbuses|Vision - Audio Sub-buses]] — решение по под-шинам.
- [[backlog-audio-sfx|Vision - Audio & SFX Backlog]] — техники (слои импакта, HDR, адаптивная музыка).
- [[backlog-gamefeel|Vision - Gamefeel Backlog]] — куда цепляется звук по времени.
