---
title: "Reference - Audio Inventory"
order: 55
status: ready
updated: 2026-07-26
---

**Статус:** ready — СГЕНЕРИРОВАН `scripts/audio/gen_audio_reference.py` из карты звука.
Руками не править: правка уедет при следующем прогоне. Источник правды —
`scripts/audio/audio_map.py`, оттуда же собираются FMOD-события и каталог.

---

Что в игре звучит, чем именно, откуда взят материал и кто его дёргает. Одна страница,
чтобы вопрос «а этот звук у нас вообще есть и где он играет» не требовал раскопок по
трём слоям — карте, FMOD-проекту и коду.

Как это устроено и почему — [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]].

## Сводка

| | |
|---|---|
| FMOD-событий | 109 |
| из них per-action дефолтов | 14 |
| исходных сэмплов | 211 |
| банки | `SFX.bank`, `Music.bank` (+ Master/strings) в `Assets/StreamingAssets` |
| целевая громкость | -23.0 dB RMS активной части, true peak ≤ -1.0 dBFS |

## Шины

Слайдеры настроек пишут в `bus:/`, `bus:/Music`, `bus:/SFX` — поэтому под-шины обязаны
висеть именно под ними, иначе громкость никуда не доедет.

| Шина | Уровень | Что в ней |
|---|---|---|
| `bus:/SFX` | +0 dB | — (родительская) |
| `bus:/Music` | -6 dB | music |
| `bus:/SFX/Combat` | +0 dB | cast, death, impact, tonal, whoosh |
| `bus:/SFX/UI` | -5 dB | ui, ui_soft |
| `bus:/SFX/Ambient` | -9 dB | ambient |
| `bus:/SFX/Stingers` | +1 dB | stinger |

## Категории: микс и анти-каша

Категория задаёт всё поведение звука в бою: громкость, разброс, сколько копий может
звучать разом и кого душить при переполнении.

| Категория | Шина | Offset | Питч | Громк. | Голосов | Кулдаун | Stealing | Приоритет |
|---|---|---|---|---|---|---|---|---|
| `impact` | SFX/Combat | +0 dB | ±2.5 st | −3.0 dB | 4 | 50 мс | Quietest | Low |
| `whoosh` | SFX/Combat | -2 dB | ±1.5 st | −3.0 dB | 4 | 60 мс | Quietest | Low |
| `tonal` | SFX/Combat | -3 dB | ±0.5 st | −2.0 dB | 3 | 60 мс | Virtualize | Medium |
| `cast` | SFX/Combat | -1 dB | ±1.0 st | −2.0 dB | 2 | 50 мс | Virtualize | Medium |
| `death` | SFX/Combat | +0 dB | ±2.0 st | −3.0 dB | 3 | 80 мс | Oldest | High |
| `stinger` | SFX/Stingers | +0 dB | ±0.0 st | −0.0 dB | 1 | 250 мс | None | Highest |
| `ui` | SFX/UI | -2 dB | ±1.0 st | −2.0 dB | 2 | 30 мс | Oldest | High |
| `ui_soft` | SFX/UI | -9 dB | ±1.0 st | −2.0 dB | 2 | 40 мс | Oldest | Medium |
| `music` | Music | +0 dB | ±0.0 st | −0.0 dB | 1 | 0 мс | None | Highest |
| `ambient` | SFX/Ambient | -6 dB | ±0.0 st | −0.0 dB | 1 | 0 мс | None | Medium |

Слоумо: глобальный параметр `TimeScale` (0.05…3.0) крутит питч `bus:/SFX/Combat` по кривой из 8 точек. Пишет его только `TimeScaleService`.

## Дефолты действий

Играют, когда точной записи под контент нет. Без них новый юнит или эффект был бы немым.

| Действие | Событие | Категория | Сэмплов |
|---|---|---|---|
| `Attack` | `event:/SFX/Combat/attack` | whoosh | 4 |
| `Fire` | `event:/SFX/Combat/fire` | whoosh | 3 |
| `Hit` | `event:/SFX/Combat/hit` | impact | 7 |
| `Evade` | `event:/SFX/Combat/evade` | whoosh | 3 |
| `Shield` | `event:/SFX/Combat/shield` | impact | 6 |
| `Heal` | `event:/SFX/Combat/heal` | tonal | 1 |
| `Cast` | `event:/SFX/Combat/cast` | cast | 1 |
| `Death` | `event:/SFX/Combat/death` | death | 6 |
| `Apply` | `event:/SFX/Combat/apply` | tonal | 2 |
| `Expire` | `event:/SFX/Combat/expire` | tonal | 1 |
| `Tick` | `event:/SFX/Combat/tick` | tonal | 1 |
| `Ui` | `event:/SFX/UI/ui` | ui | 5 |
| `Stinger` | `event:/Stingers/stinger` | stinger | 1 |
| `Loop` | `event:/SFX/Ambient/loop` | ambient | 1 |

## Точечные ключи

### Бой (`combat.*`)

Кто дёргает: боевая симуляция → AudioPresenter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `combat.attack_interrupted.evade` | `event:/SFX/Combat/attack_interrupted` | ui_soft | 2 |
| `combat.unit_spawn.ui` | `event:/SFX/Combat/unit_spawn` | ui_soft | 2 |

### Исход и старт боя (`battle.*`)

Кто дёргает: фаза боя → RunAudioPresenter / CombatFeelDirector.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `battle.defeat.stinger` | `event:/Stingers/defeat` | stinger | 1 |
| `battle.start.stinger` | `event:/Stingers/battle_start` | stinger | 1 |
| `battle.victory.stinger` | `event:/Stingers/victory` | stinger | 1 |

### Feel-слой (`feel.*`)

Кто дёргает: CombatFeelDirector и UnitView (killstinger, тяжёлый удар, финишер, разлёт).

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `feel.death_shatter.death` | `event:/SFX/Feel/death_shatter` | impact | 5 |
| `feel.finisher.stinger` | `event:/SFX/Feel/finisher` | stinger | 2 |
| `feel.heavy_hit.hit` | `event:/SFX/Feel/heavy_hit` | impact | 5 |
| `feel.kill.stinger` | `event:/SFX/Feel/kill` | stinger | 5 |

### Реликвии (герои) (`relic.*`)

Кто дёргает: по contentId юнита из боевых событий.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `relic.assassin.cast` | `event:/SFX/Relics/assassin/cast` | whoosh | 1 |
| `relic.cryomancer.attack` | `event:/SFX/Relics/cryomancer/attack` | tonal | 1 |
| `relic.cryomancer.cast` | `event:/SFX/Relics/cryomancer/cast` | cast | 2 |
| `relic.defender.shield` | `event:/SFX/Relics/defender/shield` | impact | 3 |
| `relic.druid.cast` | `event:/SFX/Relics/druid/cast` | tonal | 1 |
| `relic.flame_swordsman.attack` | `event:/SFX/Relics/flame_swordsman/attack` | whoosh | 2 |
| `relic.iron_spearman.attack` | `event:/SFX/Relics/iron_spearman/attack` | whoosh | 2 |
| `relic.light_shepherd.cast` | `event:/SFX/Relics/light_shepherd/cast` | tonal | 1 |
| `relic.light_shepherd.fire` | `event:/SFX/Relics/light_shepherd/fire` | tonal | 1 |
| `relic.ranger.fire` | `event:/SFX/Relics/ranger/fire` | whoosh | 2 |
| `relic.treant.attack` | `event:/SFX/Relics/treant/attack` | impact | 2 |
| `relic.whirl_monk.cast` | `event:/SFX/Relics/whirl_monk/cast` | whoosh | 1 |

### Эффекты и статусы (`effect.*`)

Кто дёргает: EffectSystem.OnEffectApplied / OnEffectEnded.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `effect.bulwark_shield.apply` | `event:/SFX/Effects/bulwark_shield/apply` | impact | 2 |
| `effect.burn.apply` | `event:/SFX/Effects/burn/apply` | tonal | 1 |
| `effect.burn.tick` | `event:/SFX/Effects/burn/tick` | ui_soft | 1 |
| `effect.dodge.apply` | `event:/SFX/Effects/dodge/apply` | ui_soft | 1 |
| `effect.frozen.apply` | `event:/SFX/Effects/frozen/apply` | tonal | 1 |
| `effect.frozen.expire` | `event:/SFX/Effects/frozen/expire` | impact | 5 |
| `effect.hunters_mark.apply` | `event:/SFX/Effects/hunters_mark/apply` | ui_soft | 2 |
| `effect.ice_chains_stun.apply` | `event:/SFX/Effects/ice_chains_stun/apply` | impact | 2 |
| `effect.ignition.apply` | `event:/SFX/Effects/ignition/apply` | cast | 1 |
| `effect.light_mend.tick` | `event:/SFX/Effects/light_mend/tick` | ui_soft | 1 |
| `effect.overgrowth.apply` | `event:/SFX/Effects/overgrowth/apply` | tonal | 1 |
| `effect.resolute_strike_stun.apply` | `event:/SFX/Effects/resolute_strike_stun/apply` | impact | 2 |
| `effect.spore_cloud.apply` | `event:/SFX/Effects/spore_cloud/apply` | tonal | 1 |
| `effect.stealth.apply` | `event:/SFX/Effects/stealth/apply` | whoosh | 2 |
| `effect.stealth_buff.apply` | `event:/SFX/Effects/stealth_buff/apply` | whoosh | 1 |
| `effect.vortex_entry.apply` | `event:/SFX/Effects/vortex_entry/apply` | whoosh | 1 |

### Враги (`enemy.*`)

Кто дёргает: по contentId врага.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `enemy.training_dummy.hit` | `event:/SFX/Enemies/training_dummy/hit` | impact | 4 |

### Интерфейс (`ui.*`)

Кто дёргает: UiSoundSystem (корень панели), BattleInputController, DeploymentController.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `ui.back.ui` | `event:/SFX/UI/back` | ui | 3 |
| `ui.click.ui` | `event:/SFX/UI/click` | ui | 5 |
| `ui.deploy_grab.ui` | `event:/SFX/UI/deploy_grab` | ui | 2 |
| `ui.deploy_place.ui` | `event:/SFX/UI/deploy_place` | ui | 2 |
| `ui.deploy_reject.ui` | `event:/SFX/UI/deploy_reject` | ui | 1 |
| `ui.disabled.ui` | `event:/SFX/UI/disabled` | ui | 3 |
| `ui.drag_drop.ui` | `event:/SFX/UI/drag_drop` | ui | 3 |
| `ui.drag_grab.ui` | `event:/SFX/UI/drag_grab` | ui | 2 |
| `ui.drag_reject.ui` | `event:/SFX/UI/drag_reject` | ui | 2 |
| `ui.hover.ui` | `event:/SFX/UI/hover` | ui_soft | 5 |
| `ui.modal_open.ui` | `event:/SFX/UI/modal_open` | ui | 2 |
| `ui.pause.ui` | `event:/SFX/UI/pause` | ui | 1 |
| `ui.relic_equip.ui` | `event:/SFX/UI/relic_equip` | ui | 2 |
| `ui.relic_select.ui` | `event:/SFX/UI/relic_select` | ui | 3 |
| `ui.relic_unequip.ui` | `event:/SFX/UI/relic_unequip` | ui | 1 |
| `ui.resume.ui` | `event:/SFX/UI/resume` | ui | 1 |
| `ui.screen_close.ui` | `event:/SFX/UI/screen_close` | ui | 1 |
| `ui.screen_open.ui` | `event:/SFX/UI/screen_open` | ui | 1 |
| `ui.scroll.ui` | `event:/SFX/UI/scroll` | ui_soft | 3 |
| `ui.slider.ui` | `event:/SFX/UI/slider` | ui_soft | 3 |
| `ui.speed.ui` | `event:/SFX/UI/speed` | ui_soft | 1 |
| `ui.tab.ui` | `event:/SFX/UI/tab` | ui | 3 |
| `ui.toggle.ui` | `event:/SFX/UI/toggle` | ui | 3 |
| `ui.tooltip_detail.ui` | `event:/SFX/UI/tooltip_detail` | ui_soft | 1 |
| `ui.tooltip_show.ui` | `event:/SFX/UI/tooltip_show` | ui_soft | 2 |

### Карта акта (`map.*`)

Кто дёргает: WorldMapView.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `map.close.ui` | `event:/SFX/Map/close` | ui | 2 |
| `map.node_hover.ui` | `event:/SFX/Map/node_hover` | ui_soft | 1 |
| `map.node_locked.ui` | `event:/SFX/Map/node_locked` | ui | 2 |
| `map.node_select.ui` | `event:/SFX/Map/node_select` | ui | 3 |
| `map.open.ui` | `event:/SFX/Map/open` | ui | 2 |
| `map.travel_arrive.ui` | `event:/SFX/Map/travel_arrive` | ui | 1 |
| `map.travel_start.ui` | `event:/SFX/Map/travel_start` | ui_soft | 1 |

### Переходы (`flow.*`)

Кто дёргает: RunAudioPresenter (шторка перехода).

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `flow.fade_in.ui` | `event:/SFX/Flow/fade_in` | ui_soft | 1 |
| `flow.fade_out.ui` | `event:/SFX/Flow/fade_out` | ui_soft | 1 |

### Награда (`reward.*`)

Кто дёргает: MenuRouter + RunAudioPresenter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `reward.card_select.ui` | `event:/SFX/Flow/reward_card_select` | ui | 2 |
| `reward.open.stinger` | `event:/Stingers/reward_open` | stinger | 1 |
| `reward.skip.ui` | `event:/SFX/Flow/reward_skip` | ui | 1 |
| `reward.take.stinger` | `event:/Stingers/reward_take` | stinger | 1 |

### Лавка (`shop.*`)

Кто дёргает: ShopController.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `shop.buy.ui` | `event:/SFX/Flow/shop_buy` | ui | 1 |
| `shop.denied.ui` | `event:/SFX/Flow/shop_denied` | ui | 1 |
| `shop.reroll.ui` | `event:/SFX/Flow/shop_reroll` | ui | 2 |
| `shop.sell.ui` | `event:/SFX/Flow/shop_sell` | ui | 1 |

### Привал (`camp.*`)

Кто дёргает: CampScreenView через MenuRouter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `camp.action.ui` | `event:/SFX/Flow/camp_action` | ui | 1 |
| `camp.denied.ui` | `event:/SFX/Flow/camp_denied` | ui | 1 |

### Сундук (`chest.*`)

Кто дёргает: MenuRouter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `chest.open.stinger` | `event:/Stingers/chest_open` | stinger | 1 |

### Текстовые события (`event.*`)

Кто дёргает: MenuRouter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `event.choice.ui` | `event:/SFX/Flow/event_choice` | ui | 2 |

### Забег (`run.*`)

Кто дёргает: RunAudioPresenter, RunStateService.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `run.gold_gain.ui` | `event:/SFX/Flow/gold_gain` | ui | 2 |
| `run.outcome_defeat.stinger` | `event:/Stingers/run_defeat` | stinger | 1 |
| `run.outcome_victory.stinger` | `event:/Stingers/run_victory` | stinger | 1 |
| `run.start.stinger` | `event:/Stingers/run_start` | stinger | 1 |

### Меню (`menu.*`)

Кто дёргает: RunAudioPresenter.

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `menu.hide.ui` | `event:/SFX/Flow/menu_hide` | ui | 1 |
| `menu.show.ui` | `event:/SFX/Flow/menu_show` | ui | 1 |
| `menu.title_card.stinger` | `event:/Stingers/title_card` | stinger | 1 |

### Музыка (`music.*`)

Кто дёргает: RunAudioPresenter (одна дорожка за раз).

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `music.battle.loop` | `event:/Music/battle` | music | 1 |
| `music.map.loop` | `event:/Music/map` | music | 1 |
| `music.menu.loop` | `event:/Music/menu` | music | 1 |

### Амбиент (`ambient.*`)

Кто дёргает: RunAudioPresenter (пока мир на первом плане).

| Ключ | Событие FMOD | Категория | Сэмплов |
|---|---|---|---|
| `ambient.arena.loop` | `event:/SFX/Ambient/arena` | ambient | 1 |

## Музыка и амбиент

Лупы играются хранимым `EventInstance` (`FmodAudioService`), одна дорожка за раз;
приоритет — меню важнее фазы боя, бой важнее карты.

| Ключ | Трек | Лицензия | Источник |
|---|---|---|---|
| `music.menu.loop` | Fantasy Orchestral Theme | CC0 | OpenGameArt |
| `music.map.loop` | Town Theme (cynicmusic) | CC0 | OpenGameArt |
| `music.battle.loop` | Battle Theme A (cynicmusic) | CC0 | OpenGameArt |
| `ambient.arena.loop` | Loopable Dungeon Ambience | CC0 | OpenGameArt |

Громкость: музыка нормализована к -18.0 LUFS, амбиент — к -24.0 LUFS
(интегральный EBU R128, в отличие от one-shot).

## Откуда взяты сэмплы

| Пак | Лицензия | Обязательства | Файлов задействовано |
|---|---|---|---|
| Kenney — Interface Sounds | CC0 | без атрибуции | 63 |
| Kenney — UI Audio | CC0 | без атрибуции | 10 |
| Kenney — RPG Audio | CC0 | без атрибуции | 34 |
| Kenney — Impact Sounds | CC0 | без атрибуции | 51 |
| Kenney — Music Jingles | CC0 | без атрибуции | 8 |
| RPG Essentials Free (Leohpaz) | бесплатно для коммерческих | кредит опционален | 40 |

Sonniss GDC-бандл в игре **не используется**: его файлы длинные и дизайнерские, их надо
резать на слух, а CC0-паки закрыли все дыры. Лежит как резерв.

## Пайплайн

```
scripts/audio/audio_map.py            ключ → категория → сэмплы (ЕДИНСТВЕННЫЙ источник правды)
  → build_source_audio.py             нормализация → FMOD Project/SourceAudio + manifest.json
  → build_populate.py                 → FMOD Project/Tooling/populate.js
  → fmodstudiocl -script populate.js  шины, события, микс, параметр TimeScale
  → fmodstudiocl -build -export-guids банки → Assets/StreamingAssets
  → меню Alebardium/Audio/Populate Catalog from Manifest   → AudioCatalog.asset
  → gen_audio_reference.py            → этот документ
```

Проверки: `audit_samples.py` (технический брак), `clap_pick.py --verify` (соответствие
сэмпла его смыслу), EditMode-тесты `AudioCoverageTests` (код ↔ каталог ↔ манифест).

## Связи

- [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]] — как всё устроено и почему
- [[tech/10-reference/asset-inventory|Reference - Asset Inventory]] — паки в репозитории
- [[gdd/10-vision/audio-subbuses|Vision - Audio Sub-buses]] — решение о под-шинах
- [[gdd/10-vision/backlog-audio-sfx|Vision - Audio & SFX Backlog]] — техники на будущее
