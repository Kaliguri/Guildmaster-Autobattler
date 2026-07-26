// =============================================================================
// Guildmaster — заливка звука в FMOD Studio. СГЕНЕРИРОВАН scripts/audio/build_populate.py
// из FMOD Project/Scripts/manifest.json. Правь карту (scripts/audio/audio_map.py) и
// перегенерируй — руками этот файл не трогать.
//
// Что делает (spec: docs/wiki/tech/40-planning/sfx-round-2.md):
//   1. Шины: bus:/SFX{Combat,UI,Ambient,Stingers} и bus:/Music — под слайдеры настроек.
//   2. События из манифеста: мульти-инструмент (round-robin), банк, роутинг в под-шину.
//   3. Микс: категорийный offset громкости, рандомизация питча/громкости (анти-«пулемёт»),
//      voice-макросы maxVoices/stealing/cooldown/priority (анти-каша плотного боя).
//   4. Глобальный параметр TimeScale + кривая питча bus:/SFX/Combat (slowmo слышен).
//
// Идемпотентен: повторный прогон пересобирает события и обновляет шины, не плодя дубли.
// Headless-safe: никаких модальных диалогов, весь вывод в populate_log.txt.
//
// RUN (две команды — заливка, потом сборка банков):
//   fmodstudiocl.exe -script "FMOD Project/Tooling/populate.js" "FMOD Project/Guildmaster Autobattler Game.fspro"
//   fmodstudiocl.exe -build -ignore-warnings -export-guids "FMOD Project/Guildmaster Autobattler Game.fspro"
// =============================================================================

(function () {
    'use strict';

    var LOG_PATH = "C:/My Projects/Guildmaster-Autobattler/FMOD Project/Scripts/populate_log.txt";
    var REPO_ROOT = "C:/My Projects/Guildmaster-Autobattler";
    var MANIFEST = {
  "sourceRoot": "FMOD Project/SourceAudio",
  "bank": "SFX",
  "musicBank": "Music",
  "targetRmsDb": -23.0,
  "truePeakDb": -1.0,
  "buses": {
    "SFX": {
      "parent": null,
      "volumeDb": 0.0
    },
    "Music": {
      "parent": null,
      "volumeDb": -6.0
    },
    "SFX/Combat": {
      "parent": "SFX",
      "volumeDb": 0.0
    },
    "SFX/UI": {
      "parent": "SFX",
      "volumeDb": -5.0
    },
    "SFX/Ambient": {
      "parent": "SFX",
      "volumeDb": -9.0
    },
    "SFX/Stingers": {
      "parent": "SFX",
      "volumeDb": 1.0
    }
  },
  "categories": {
    "impact": {
      "bus": "SFX/Combat",
      "volumeDb": 0.0,
      "pitchSt": 2.5,
      "volRandDb": 3.0,
      "maxVoices": 4,
      "cooldownMs": 50,
      "stealing": 2,
      "priority": 1
    },
    "whoosh": {
      "bus": "SFX/Combat",
      "volumeDb": -2.0,
      "pitchSt": 1.5,
      "volRandDb": 3.0,
      "maxVoices": 4,
      "cooldownMs": 60,
      "stealing": 2,
      "priority": 1
    },
    "tonal": {
      "bus": "SFX/Combat",
      "volumeDb": -3.0,
      "pitchSt": 0.5,
      "volRandDb": 2.0,
      "maxVoices": 3,
      "cooldownMs": 60,
      "stealing": 3,
      "priority": 2
    },
    "cast": {
      "bus": "SFX/Combat",
      "volumeDb": -1.0,
      "pitchSt": 1.0,
      "volRandDb": 2.0,
      "maxVoices": 2,
      "cooldownMs": 50,
      "stealing": 3,
      "priority": 2
    },
    "death": {
      "bus": "SFX/Combat",
      "volumeDb": 0.0,
      "pitchSt": 2.0,
      "volRandDb": 3.0,
      "maxVoices": 3,
      "cooldownMs": 80,
      "stealing": 0,
      "priority": 3
    },
    "stinger": {
      "bus": "SFX/Stingers",
      "volumeDb": 0.0,
      "pitchSt": 0.0,
      "volRandDb": 0.0,
      "maxVoices": 1,
      "cooldownMs": 250,
      "stealing": 4,
      "priority": 4
    },
    "ui": {
      "bus": "SFX/UI",
      "volumeDb": -2.0,
      "pitchSt": 1.0,
      "volRandDb": 2.0,
      "maxVoices": 2,
      "cooldownMs": 30,
      "stealing": 0,
      "priority": 3
    },
    "ui_soft": {
      "bus": "SFX/UI",
      "volumeDb": -9.0,
      "pitchSt": 1.0,
      "volRandDb": 2.0,
      "maxVoices": 2,
      "cooldownMs": 40,
      "stealing": 0,
      "priority": 2
    },
    "music": {
      "bus": "Music",
      "volumeDb": 0.0,
      "pitchSt": 0.0,
      "volRandDb": 0.0,
      "maxVoices": 1,
      "cooldownMs": 0,
      "stealing": 4,
      "priority": 4,
      "looping": true
    },
    "ambient": {
      "bus": "SFX/Ambient",
      "volumeDb": -6.0,
      "pitchSt": 0.0,
      "volRandDb": 0.0,
      "maxVoices": 1,
      "cooldownMs": 0,
      "stealing": 4,
      "priority": 2,
      "looping": true
    }
  },
  "timeScaleParam": {
    "name": "TimeScale",
    "minimum": 0.05,
    "maximum": 3.0,
    "initial": 1.0,
    "bus": "SFX/Combat",
    "curve": [
      [
        0.05,
        -24.0
      ],
      [
        0.25,
        -24.0
      ],
      [
        0.5,
        -12.0
      ],
      [
        0.75,
        -4.98
      ],
      [
        1.0,
        0.0
      ],
      [
        1.5,
        7.02
      ],
      [
        2.0,
        12.0
      ],
      [
        3.0,
        19.02
      ]
    ]
  },
  "events": [
    {
      "key": "attack",
      "action": "Attack",
      "isDefault": true,
      "path": "event:/SFX/Combat/attack",
      "category": "whoosh",
      "files": [
        "whoosh/attack_01.wav",
        "whoosh/attack_02.wav",
        "whoosh/attack_03.wav",
        "whoosh/attack_04.wav"
      ]
    },
    {
      "key": "fire",
      "action": "Fire",
      "isDefault": true,
      "path": "event:/SFX/Combat/fire",
      "category": "whoosh",
      "files": [
        "whoosh/fire_01.wav",
        "whoosh/fire_02.wav",
        "whoosh/fire_03.wav"
      ]
    },
    {
      "key": "hit",
      "action": "Hit",
      "isDefault": true,
      "path": "event:/SFX/Combat/hit",
      "category": "impact",
      "files": [
        "impact/hit_01.wav",
        "impact/hit_02.wav",
        "impact/hit_03.wav",
        "impact/hit_04.wav",
        "impact/hit_05.wav",
        "impact/hit_06.wav",
        "impact/hit_07.wav"
      ]
    },
    {
      "key": "evade",
      "action": "Evade",
      "isDefault": true,
      "path": "event:/SFX/Combat/evade",
      "category": "whoosh",
      "files": [
        "whoosh/evade_01.wav",
        "whoosh/evade_02.wav",
        "whoosh/evade_03.wav"
      ]
    },
    {
      "key": "shield",
      "action": "Shield",
      "isDefault": true,
      "path": "event:/SFX/Combat/shield",
      "category": "impact",
      "files": [
        "impact/shield_01.wav",
        "impact/shield_02.wav",
        "impact/shield_03.wav",
        "impact/shield_04.wav",
        "impact/shield_05.wav",
        "impact/shield_06.wav"
      ]
    },
    {
      "key": "heal",
      "action": "Heal",
      "isDefault": true,
      "path": "event:/SFX/Combat/heal",
      "category": "tonal",
      "files": [
        "tonal/heal_01.wav"
      ]
    },
    {
      "key": "cast",
      "action": "Cast",
      "isDefault": true,
      "path": "event:/SFX/Combat/cast",
      "category": "cast",
      "files": [
        "cast/cast_01.wav"
      ]
    },
    {
      "key": "death",
      "action": "Death",
      "isDefault": true,
      "path": "event:/SFX/Combat/death",
      "category": "death",
      "files": [
        "death/death_01.wav",
        "death/death_02.wav",
        "death/death_03.wav",
        "death/death_04.wav",
        "death/death_05.wav",
        "death/death_06.wav"
      ]
    },
    {
      "key": "apply",
      "action": "Apply",
      "isDefault": true,
      "path": "event:/SFX/Combat/apply",
      "category": "tonal",
      "files": [
        "tonal/apply_01.wav",
        "tonal/apply_02.wav"
      ]
    },
    {
      "key": "expire",
      "action": "Expire",
      "isDefault": true,
      "path": "event:/SFX/Combat/expire",
      "category": "tonal",
      "files": [
        "tonal/expire_01.wav"
      ]
    },
    {
      "key": "tick",
      "action": "Tick",
      "isDefault": true,
      "path": "event:/SFX/Combat/tick",
      "category": "tonal",
      "files": [
        "tonal/tick_01.wav"
      ]
    },
    {
      "key": "ui",
      "action": "Ui",
      "isDefault": true,
      "path": "event:/SFX/UI/ui",
      "category": "ui",
      "files": [
        "ui/ui_01.wav",
        "ui/ui_02.wav",
        "ui/ui_03.wav",
        "ui/ui_04.wav",
        "ui/ui_05.wav"
      ]
    },
    {
      "key": "stinger",
      "action": "Stinger",
      "isDefault": true,
      "path": "event:/Stingers/stinger",
      "category": "stinger",
      "files": [
        "stinger/stinger_01.wav"
      ]
    },
    {
      "key": "loop",
      "action": "Loop",
      "isDefault": true,
      "path": "event:/SFX/Ambient/loop",
      "category": "ambient",
      "files": [
        "ambient/loop_01.ogg"
      ]
    },
    {
      "key": "battle.start.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/battle_start",
      "category": "stinger",
      "files": [
        "stinger/battle_start_01.wav"
      ]
    },
    {
      "key": "battle.victory.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/victory",
      "category": "stinger",
      "files": [
        "stinger/victory_01.wav"
      ]
    },
    {
      "key": "battle.defeat.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/defeat",
      "category": "stinger",
      "files": [
        "stinger/defeat_01.wav"
      ]
    },
    {
      "key": "combat.unit_spawn.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Combat/unit_spawn",
      "category": "ui_soft",
      "files": [
        "ui_soft/unit_spawn_01.wav",
        "ui_soft/unit_spawn_02.wav"
      ]
    },
    {
      "key": "combat.attack_interrupted.evade",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Combat/attack_interrupted",
      "category": "ui_soft",
      "files": [
        "ui_soft/attack_interrupted_01.wav",
        "ui_soft/attack_interrupted_02.wav"
      ]
    },
    {
      "key": "enemy.training_dummy.hit",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Enemies/training_dummy/hit",
      "category": "impact",
      "files": [
        "impact/hit_01.wav",
        "impact/hit_02.wav",
        "impact/hit_03.wav",
        "impact/hit_04.wav"
      ]
    },
    {
      "key": "feel.kill.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Feel/kill",
      "category": "stinger",
      "files": [
        "stinger/kill_01.wav",
        "stinger/kill_02.wav",
        "stinger/kill_03.wav",
        "stinger/kill_04.wav",
        "stinger/kill_05.wav"
      ]
    },
    {
      "key": "feel.heavy_hit.hit",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Feel/heavy_hit",
      "category": "impact",
      "files": [
        "impact/heavy_hit_01.wav",
        "impact/heavy_hit_02.wav",
        "impact/heavy_hit_03.wav",
        "impact/heavy_hit_04.wav",
        "impact/heavy_hit_05.wav"
      ]
    },
    {
      "key": "feel.death_shatter.death",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Feel/death_shatter",
      "category": "impact",
      "files": [
        "impact/death_shatter_01.wav",
        "impact/death_shatter_02.wav",
        "impact/death_shatter_03.wav",
        "impact/death_shatter_04.wav",
        "impact/death_shatter_05.wav"
      ]
    },
    {
      "key": "feel.finisher.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Feel/finisher",
      "category": "stinger",
      "files": [
        "stinger/finisher_01.wav",
        "stinger/finisher_02.wav"
      ]
    },
    {
      "key": "relic.cryomancer.attack",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/cryomancer/attack",
      "category": "tonal",
      "files": [
        "tonal/attack_01.wav"
      ]
    },
    {
      "key": "relic.cryomancer.cast",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/cryomancer/cast",
      "category": "cast",
      "files": [
        "cast/cast_01.wav",
        "cast/cast_02.wav"
      ]
    },
    {
      "key": "relic.light_shepherd.cast",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/light_shepherd/cast",
      "category": "tonal",
      "files": [
        "tonal/cast_01.wav"
      ]
    },
    {
      "key": "relic.light_shepherd.fire",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/light_shepherd/fire",
      "category": "tonal",
      "files": [
        "tonal/fire_01.wav"
      ]
    },
    {
      "key": "relic.whirl_monk.cast",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/whirl_monk/cast",
      "category": "whoosh",
      "files": [
        "whoosh/cast_01.wav"
      ]
    },
    {
      "key": "relic.assassin.cast",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/assassin/cast",
      "category": "whoosh",
      "files": [
        "whoosh/cast_01.wav"
      ]
    },
    {
      "key": "relic.ranger.fire",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/ranger/fire",
      "category": "whoosh",
      "files": [
        "whoosh/fire_01.wav",
        "whoosh/fire_02.wav"
      ]
    },
    {
      "key": "relic.iron_spearman.attack",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/iron_spearman/attack",
      "category": "whoosh",
      "files": [
        "whoosh/attack_01.wav",
        "whoosh/attack_02.wav"
      ]
    },
    {
      "key": "relic.defender.shield",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/defender/shield",
      "category": "impact",
      "files": [
        "impact/shield_01.wav",
        "impact/shield_02.wav",
        "impact/shield_03.wav"
      ]
    },
    {
      "key": "relic.flame_swordsman.attack",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/flame_swordsman/attack",
      "category": "whoosh",
      "files": [
        "whoosh/attack_01.wav",
        "whoosh/attack_02.wav"
      ]
    },
    {
      "key": "relic.treant.attack",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/treant/attack",
      "category": "impact",
      "files": [
        "impact/attack_01.wav",
        "impact/attack_02.wav"
      ]
    },
    {
      "key": "relic.druid.cast",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Relics/druid/cast",
      "category": "tonal",
      "files": [
        "tonal/cast_01.wav"
      ]
    },
    {
      "key": "effect.frozen.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/frozen/apply",
      "category": "tonal",
      "files": [
        "tonal/apply_01.wav"
      ]
    },
    {
      "key": "effect.frozen.expire",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/frozen/expire",
      "category": "impact",
      "files": [
        "impact/expire_01.wav",
        "impact/expire_02.wav",
        "impact/expire_03.wav",
        "impact/expire_04.wav",
        "impact/expire_05.wav"
      ]
    },
    {
      "key": "effect.burn.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/burn/apply",
      "category": "tonal",
      "files": [
        "tonal/apply_01.wav"
      ]
    },
    {
      "key": "effect.burn.tick",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/burn/tick",
      "category": "ui_soft",
      "files": [
        "ui_soft/tick_01.wav"
      ]
    },
    {
      "key": "effect.ignition.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/ignition/apply",
      "category": "cast",
      "files": [
        "cast/apply_01.wav"
      ]
    },
    {
      "key": "effect.spore_cloud.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/spore_cloud/apply",
      "category": "tonal",
      "files": [
        "tonal/apply_01.wav"
      ]
    },
    {
      "key": "effect.ice_chains_stun.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/ice_chains_stun/apply",
      "category": "impact",
      "files": [
        "impact/apply_01.wav",
        "impact/apply_02.wav"
      ]
    },
    {
      "key": "effect.resolute_strike_stun.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/resolute_strike_stun/apply",
      "category": "impact",
      "files": [
        "impact/apply_01.wav",
        "impact/apply_02.wav"
      ]
    },
    {
      "key": "effect.stealth.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/stealth/apply",
      "category": "whoosh",
      "files": [
        "whoosh/apply_01.wav",
        "whoosh/apply_02.wav"
      ]
    },
    {
      "key": "effect.stealth_buff.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/stealth_buff/apply",
      "category": "whoosh",
      "files": [
        "whoosh/apply_01.wav"
      ]
    },
    {
      "key": "effect.hunters_mark.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/hunters_mark/apply",
      "category": "ui_soft",
      "files": [
        "ui_soft/apply_01.wav",
        "ui_soft/apply_02.wav"
      ]
    },
    {
      "key": "effect.bulwark_shield.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/bulwark_shield/apply",
      "category": "impact",
      "files": [
        "impact/apply_01.wav",
        "impact/apply_02.wav"
      ]
    },
    {
      "key": "effect.dodge.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/dodge/apply",
      "category": "ui_soft",
      "files": [
        "ui_soft/apply_01.wav"
      ]
    },
    {
      "key": "effect.vortex_entry.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/vortex_entry/apply",
      "category": "whoosh",
      "files": [
        "whoosh/apply_01.wav"
      ]
    },
    {
      "key": "effect.overgrowth.apply",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/overgrowth/apply",
      "category": "tonal",
      "files": [
        "tonal/apply_01.wav"
      ]
    },
    {
      "key": "effect.light_mend.tick",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Effects/light_mend/tick",
      "category": "ui_soft",
      "files": [
        "ui_soft/tick_01.wav"
      ]
    },
    {
      "key": "ui.click.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/click",
      "category": "ui",
      "files": [
        "ui/click_01.wav",
        "ui/click_02.wav",
        "ui/click_03.wav",
        "ui/click_04.wav",
        "ui/click_05.wav"
      ]
    },
    {
      "key": "ui.hover.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/hover",
      "category": "ui_soft",
      "files": [
        "ui_soft/hover_01.wav",
        "ui_soft/hover_02.wav",
        "ui_soft/hover_03.wav",
        "ui_soft/hover_04.wav",
        "ui_soft/hover_05.wav"
      ]
    },
    {
      "key": "ui.tab.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/tab",
      "category": "ui",
      "files": [
        "ui/tab_01.wav",
        "ui/tab_02.wav",
        "ui/tab_03.wav"
      ]
    },
    {
      "key": "ui.toggle.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/toggle",
      "category": "ui",
      "files": [
        "ui/toggle_01.wav",
        "ui/toggle_02.wav",
        "ui/toggle_03.wav"
      ]
    },
    {
      "key": "ui.slider.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/slider",
      "category": "ui_soft",
      "files": [
        "ui_soft/slider_01.wav",
        "ui_soft/slider_02.wav",
        "ui_soft/slider_03.wav"
      ]
    },
    {
      "key": "ui.back.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/back",
      "category": "ui",
      "files": [
        "ui/back_01.wav",
        "ui/back_02.wav",
        "ui/back_03.wav"
      ]
    },
    {
      "key": "ui.disabled.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/disabled",
      "category": "ui",
      "files": [
        "ui/disabled_01.wav",
        "ui/disabled_02.wav",
        "ui/disabled_03.wav"
      ]
    },
    {
      "key": "ui.tooltip_show.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/tooltip_show",
      "category": "ui_soft",
      "files": [
        "ui_soft/tooltip_show_01.wav",
        "ui_soft/tooltip_show_02.wav"
      ]
    },
    {
      "key": "ui.tooltip_detail.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/tooltip_detail",
      "category": "ui_soft",
      "files": [
        "ui_soft/tooltip_detail_01.wav"
      ]
    },
    {
      "key": "ui.screen_open.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/screen_open",
      "category": "ui",
      "files": [
        "ui/screen_open_01.wav"
      ]
    },
    {
      "key": "ui.screen_close.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/screen_close",
      "category": "ui",
      "files": [
        "ui/screen_close_01.wav"
      ]
    },
    {
      "key": "ui.modal_open.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/modal_open",
      "category": "ui",
      "files": [
        "ui/modal_open_01.wav",
        "ui/modal_open_02.wav"
      ]
    },
    {
      "key": "ui.drag_grab.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/drag_grab",
      "category": "ui",
      "files": [
        "ui/drag_grab_01.wav",
        "ui/drag_grab_02.wav"
      ]
    },
    {
      "key": "ui.drag_drop.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/drag_drop",
      "category": "ui",
      "files": [
        "ui/drag_drop_01.wav",
        "ui/drag_drop_02.wav",
        "ui/drag_drop_03.wav"
      ]
    },
    {
      "key": "ui.drag_reject.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/drag_reject",
      "category": "ui",
      "files": [
        "ui/drag_reject_01.wav",
        "ui/drag_reject_02.wav"
      ]
    },
    {
      "key": "ui.scroll.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/scroll",
      "category": "ui_soft",
      "files": [
        "ui_soft/scroll_01.wav",
        "ui_soft/scroll_02.wav",
        "ui_soft/scroll_03.wav"
      ]
    },
    {
      "key": "ui.pause.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/pause",
      "category": "ui",
      "files": [
        "ui/pause_01.wav"
      ]
    },
    {
      "key": "ui.resume.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/resume",
      "category": "ui",
      "files": [
        "ui/resume_01.wav"
      ]
    },
    {
      "key": "ui.speed.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/speed",
      "category": "ui_soft",
      "files": [
        "ui_soft/speed_01.wav"
      ]
    },
    {
      "key": "ui.deploy_grab.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/deploy_grab",
      "category": "ui",
      "files": [
        "ui/deploy_grab_01.wav",
        "ui/deploy_grab_02.wav"
      ]
    },
    {
      "key": "ui.deploy_place.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/deploy_place",
      "category": "ui",
      "files": [
        "ui/deploy_place_01.wav",
        "ui/deploy_place_02.wav"
      ]
    },
    {
      "key": "ui.deploy_reject.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/deploy_reject",
      "category": "ui",
      "files": [
        "ui/deploy_reject_01.wav"
      ]
    },
    {
      "key": "ui.relic_equip.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/relic_equip",
      "category": "ui",
      "files": [
        "ui/relic_equip_01.wav",
        "ui/relic_equip_02.wav"
      ]
    },
    {
      "key": "ui.relic_unequip.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/relic_unequip",
      "category": "ui",
      "files": [
        "ui/relic_unequip_01.wav"
      ]
    },
    {
      "key": "ui.relic_select.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/UI/relic_select",
      "category": "ui",
      "files": [
        "ui/relic_select_01.wav",
        "ui/relic_select_02.wav",
        "ui/relic_select_03.wav"
      ]
    },
    {
      "key": "map.node_hover.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/node_hover",
      "category": "ui_soft",
      "files": [
        "ui_soft/node_hover_01.wav"
      ]
    },
    {
      "key": "map.node_select.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/node_select",
      "category": "ui",
      "files": [
        "ui/node_select_01.wav",
        "ui/node_select_02.wav",
        "ui/node_select_03.wav"
      ]
    },
    {
      "key": "map.node_locked.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/node_locked",
      "category": "ui",
      "files": [
        "ui/node_locked_01.wav",
        "ui/node_locked_02.wav"
      ]
    },
    {
      "key": "map.travel_start.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/travel_start",
      "category": "ui_soft",
      "files": [
        "ui_soft/travel_start_01.wav"
      ]
    },
    {
      "key": "map.travel_arrive.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/travel_arrive",
      "category": "ui",
      "files": [
        "ui/travel_arrive_01.wav"
      ]
    },
    {
      "key": "map.open.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/open",
      "category": "ui",
      "files": [
        "ui/open_01.wav",
        "ui/open_02.wav"
      ]
    },
    {
      "key": "map.close.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Map/close",
      "category": "ui",
      "files": [
        "ui/close_01.wav",
        "ui/close_02.wav"
      ]
    },
    {
      "key": "flow.fade_in.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/fade_in",
      "category": "ui_soft",
      "files": [
        "ui_soft/fade_in_01.wav"
      ]
    },
    {
      "key": "flow.fade_out.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/fade_out",
      "category": "ui_soft",
      "files": [
        "ui_soft/fade_out_01.wav"
      ]
    },
    {
      "key": "reward.open.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/reward_open",
      "category": "stinger",
      "files": [
        "stinger/reward_open_01.wav"
      ]
    },
    {
      "key": "reward.card_select.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/reward_card_select",
      "category": "ui",
      "files": [
        "ui/reward_card_select_01.wav",
        "ui/reward_card_select_02.wav"
      ]
    },
    {
      "key": "reward.take.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/reward_take",
      "category": "stinger",
      "files": [
        "stinger/reward_take_01.wav"
      ]
    },
    {
      "key": "reward.skip.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/reward_skip",
      "category": "ui",
      "files": [
        "ui/reward_skip_01.wav"
      ]
    },
    {
      "key": "run.gold_gain.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/gold_gain",
      "category": "ui",
      "files": [
        "ui/gold_gain_01.wav",
        "ui/gold_gain_02.wav"
      ]
    },
    {
      "key": "shop.buy.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/shop_buy",
      "category": "ui",
      "files": [
        "ui/shop_buy_01.wav"
      ]
    },
    {
      "key": "shop.sell.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/shop_sell",
      "category": "ui",
      "files": [
        "ui/shop_sell_01.wav"
      ]
    },
    {
      "key": "shop.reroll.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/shop_reroll",
      "category": "ui",
      "files": [
        "ui/shop_reroll_01.wav",
        "ui/shop_reroll_02.wav"
      ]
    },
    {
      "key": "shop.denied.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/shop_denied",
      "category": "ui",
      "files": [
        "ui/shop_denied_01.wav"
      ]
    },
    {
      "key": "chest.open.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/chest_open",
      "category": "stinger",
      "files": [
        "stinger/chest_open_01.wav"
      ]
    },
    {
      "key": "camp.action.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/camp_action",
      "category": "ui",
      "files": [
        "ui/camp_action_01.wav"
      ]
    },
    {
      "key": "camp.denied.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/camp_denied",
      "category": "ui",
      "files": [
        "ui/camp_denied_01.wav"
      ]
    },
    {
      "key": "event.choice.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/event_choice",
      "category": "ui",
      "files": [
        "ui/event_choice_01.wav",
        "ui/event_choice_02.wav"
      ]
    },
    {
      "key": "run.start.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/run_start",
      "category": "stinger",
      "files": [
        "stinger/run_start_01.wav"
      ]
    },
    {
      "key": "run.outcome_victory.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/run_victory",
      "category": "stinger",
      "files": [
        "stinger/run_victory_01.wav"
      ]
    },
    {
      "key": "run.outcome_defeat.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/run_defeat",
      "category": "stinger",
      "files": [
        "stinger/run_defeat_01.wav"
      ]
    },
    {
      "key": "menu.title_card.stinger",
      "action": null,
      "isDefault": false,
      "path": "event:/Stingers/title_card",
      "category": "stinger",
      "files": [
        "stinger/title_card_01.wav"
      ]
    },
    {
      "key": "menu.show.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/menu_show",
      "category": "ui",
      "files": [
        "ui/menu_show_01.wav"
      ]
    },
    {
      "key": "menu.hide.ui",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Flow/menu_hide",
      "category": "ui",
      "files": [
        "ui/menu_hide_01.wav"
      ]
    },
    {
      "key": "music.menu.loop",
      "action": null,
      "isDefault": false,
      "path": "event:/Music/menu",
      "category": "music",
      "files": [
        "music/menu_01.ogg"
      ]
    },
    {
      "key": "music.map.loop",
      "action": null,
      "isDefault": false,
      "path": "event:/Music/map",
      "category": "music",
      "files": [
        "music/map_01.ogg"
      ]
    },
    {
      "key": "music.battle.loop",
      "action": null,
      "isDefault": false,
      "path": "event:/Music/battle",
      "category": "music",
      "files": [
        "music/battle_01.ogg"
      ]
    },
    {
      "key": "ambient.arena.loop",
      "action": null,
      "isDefault": false,
      "path": "event:/SFX/Ambient/arena",
      "category": "ambient",
      "files": [
        "ambient/arena_01.ogg"
      ]
    }
  ]
};

    var logLines = [];
    function log(status, subject, reason) {
        logLines.push(status + ": " + subject + (reason ? " (" + reason + ")" : ""));
    }
    function flush() {
        try {
            var f = studio.system.getFile(LOG_PATH);
            f.open(studio.system.openMode.WriteOnly);
            f.writeText(logLines.join("\n") + "\n");
            f.close();
        } catch (e) { /* headless: больше ничего не сделать */ }
    }

    function findByName(modelType, name) {
        var all = modelType.findInstances();
        for (var i = 0; i < all.length; i++) if (all[i].name === name) return all[i];
        return null;
    }

    function findOrCreateBank(name) {
        var b = findByName(studio.project.model.Bank, name);
        if (b) return b;
        b = studio.project.create("Bank");
        b.name = name;
        log("BANK", name, "created");
        return b;
    }

    // --- 1. Шины -------------------------------------------------------------
    // Имя шины в дереве — путь ("SFX/Combat"); в проекте это MixerGroup "Combat" внутри "SFX".
    var busByPath = {};
    function busLeafName(path) { var p = path.split("/"); return p[p.length - 1]; }

    // Сносим наши шины целиком и строим заново: сравнивать MixerGroup по ссылке нельзя
    // (===  на ManagedObject не работает), а lookup по пути на дублях врёт. События всё равно
    // пересоздаются ниже, так что роутинг не теряется.
    function purgeManagedBuses() {
        var groups = studio.project.model.MixerGroup.findInstances();
        var killed = 0;
        for (var i = 0; i < groups.length; i++) {
            var p;
            try { p = groups[i].getPath(); } catch (e) { continue; }
            for (var busPath in MANIFEST.buses) {
                if (!MANIFEST.buses.hasOwnProperty(busPath)) continue;
                var root = busPath.split("/")[0];
                if (p === "bus:/" + root || p.indexOf("bus:/" + root + "/") === 0) {
                    studio.project.deleteObject(groups[i]);
                    killed++;
                    break;
                }
            }
        }
        return killed;
    }

    function ensureBus(path, spec, master) {
        if (busByPath[path]) return busByPath[path];
        var parent = spec.parent ? ensureBus(spec.parent, MANIFEST.buses[spec.parent], master) : master;
        var g = studio.project.create("MixerGroup");
        g.name = busLeafName(path);
        g.output = parent;
        try { g.volume = spec.volumeDb; } catch (e) { log("WARN", path, "volume failed: " + e); }
        log("BUS", path, "created " + g.getPath());
        busByPath[path] = g;
        return g;
    }

    // --- 2. Папки событий ----------------------------------------------------
    function ensureEventFolder(folderPath) {
        var parent = studio.project.workspace.masterEventFolder;
        if (!folderPath) return parent;
        var parts = folderPath.split("/");
        for (var i = 0; i < parts.length; i++) {
            var name = parts[i], found = null;
            var kids = parent.items || [];
            for (var k = 0; k < kids.length; k++) {
                if (kids[k].isOfExactType && kids[k].isOfExactType("EventFolder") && kids[k].name === name) { found = kids[k]; break; }
            }
            if (!found) { found = studio.project.create("EventFolder"); found.name = name; found.folder = parent; }
            parent = found;
        }
        return parent;
    }

    function parseEventPath(path) {
        var rel = path.replace(/^event:\//, "");
        var i = rel.lastIndexOf("/");
        return { folder: i !== -1 ? rel.substring(0, i) : "", name: i !== -1 ? rel.substring(i + 1) : rel };
    }

    // ГОТЧА: у ManagedObject нет свойства isDestroyed — присваивание молча создаёт JS-поле и ничего
    // не удаляет (так раунд 1 плодил дубли). Удаляет только studio.project.deleteObject().
    // Чистим ВСЕ события внутри наших папок, которых нет в манифесте: populate — единственный
    // источник этих событий, ручных правок тут не держим.
    function purgeManagedEvents(keepPaths) {
        var events = studio.project.model.Event.findInstances();
        var killed = 0;
        for (var i = 0; i < events.length; i++) {
            var p;
            try { p = events[i].getPath(); } catch (e) { continue; }
            var managed = p.indexOf("event:/SFX/") === 0 || p.indexOf("event:/Stingers/") === 0 || p.indexOf("event:/Music/") === 0;
            if (!managed) continue;
            if (keepPaths[p]) continue;   // это событие пересоздаётся ниже — его снесёт rebuild
            studio.project.deleteObject(events[i]);
            killed++;
        }
        return killed;
    }

    function destroyExistingEvent(fullPath) {
        var events = studio.project.model.Event.findInstances();
        var killed = 0;
        for (var i = 0; i < events.length; i++) {
            var p;
            try { p = events[i].getPath(); } catch (e) { continue; }
            if (p === fullPath) { studio.project.deleteObject(events[i]); killed++; }
        }
        return killed;
    }

    // Ассеты, на которые больше никто не ссылается, — из проекта вон: иначе FMOD Project/Assets
    // копит мусор от прошлых прогонов (каждый импорт кладёт туда копию файла).
    function destroyUnusedAssets() {
        var files = studio.project.model.AudioFile.findInstances();
        var killed = 0;
        for (var i = 0; i < files.length; i++) {
            var f = files[i], refs = -1;
            try { refs = (f.sounds || []).length + (f.programmerSounds || []).length + (f.dataReferees || []).length; }
            catch (e) { continue; }
            if (refs === 0) { studio.project.deleteObject(f); killed++; }
        }
        return killed;
    }

    try {
        var master = studio.project.workspace.mixer.masterBus;
        log("STARTED", "populate", "master=" + master.getPath());
        flush();

        log("PURGE", "managed buses", purgeManagedBuses() + " removed");
        for (var busPath in MANIFEST.buses) {
            if (MANIFEST.buses.hasOwnProperty(busPath)) ensureBus(busPath, MANIFEST.buses[busPath], master);
        }
        flush();

        var sfxBank = findOrCreateBank(MANIFEST.bank || "SFX");
        var musicBank = findOrCreateBank(MANIFEST.musicBank || "Music");

        // --- 3. Глобальный параметр TimeScale --------------------------------
        var tsSpec = MANIFEST.timeScaleParam;
        var tsParam = null;
        if (tsSpec) {
            var preset = findByName(studio.project.model.ParameterPreset, tsSpec.name);
            if (!preset) {
                var created = studio.project.workspace.addGameParameter({ name: tsSpec.name });
                preset = findByName(studio.project.model.ParameterPreset, tsSpec.name);
                log("PARAM", tsSpec.name, "created");
            }
            if (preset && preset.parameter) {
                tsParam = preset.parameter;
                try {
                    tsParam.minimum = tsSpec.minimum;
                    tsParam.maximum = tsSpec.maximum;
                    tsParam.initialValue = tsSpec.initial;
                    tsParam.isGlobal = true;
                    log("PARAM", tsSpec.name, "range " + tsParam.minimum + ".." + tsParam.maximum + " global=" + tsParam.isGlobal);
                } catch (e) { log("WARN", tsSpec.name, "param props failed: " + e); }
            }
        }

        // Кривая питча боевой шины по TimeScale: slowmo/ускорение слышно.
        if (tsParam && tsSpec && busByPath[tsSpec.bus]) {
            var bus = busByPath[tsSpec.bus];
            var hasCurve = false;
            try {
                var autos = bus.automators || [];
                for (var a = 0; a < autos.length; a++) if (autos[a].nameOfPropertyBeingAutomated === "pitch") hasCurve = true;
            } catch (e) {}
            if (!hasCurve) {
                try {
                    var automator = bus.addAutomator("pitch");
                    var curve = automator.addAutomationCurve(tsParam);
                    for (var c = 0; c < tsSpec.curve.length; c++) {
                        curve.addAutomationPoint(tsSpec.curve[c][0], tsSpec.curve[c][1]);
                    }
                    log("AUTOMATION", tsSpec.bus + ".pitch", tsSpec.curve.length + " points on " + tsSpec.name);
                } catch (e) { log("WARN", "TimeScale curve", "failed: " + e); }
            } else {
                log("AUTOMATION", tsSpec.bus + ".pitch", "already present");
            }
        }
        flush();

        // --- 4. События ------------------------------------------------------
        var events = MANIFEST.events || [];
        var built = 0, failed = 0;

        var keep = {};
        for (var kp = 0; kp < events.length; kp++) keep[events[kp].path] = true;
        log("PURGE", "stale events", purgeManagedEvents(keep) + " removed");
        flush();

        for (var e = 0; e < events.length; e++) {
            var entry = events[e];
            var cat = MANIFEST.categories[entry.category] || {};
            var parsed = parseEventPath(entry.path);
            var folder = ensureEventFolder(parsed.folder);
            destroyExistingEvent(entry.path);

            var event = studio.project.create("Event");
            event.name = parsed.name;
            event.folder = folder;
            var track = event.addGroupTrack();

            var assets = [];
            for (var fi = 0; fi < entry.files.length; fi++) {
                var full = REPO_ROOT + "/" + MANIFEST.sourceRoot + "/" + entry.files[fi];
                var asset = studio.project.importAudioFile(full);
                if (asset) assets.push(asset); else log("FAILED", full, "import failed");
            }
            if (assets.length === 0) { event.isDestroyed = true; failed++; log("FAILED", entry.path, "no audio"); flush(); continue; }

            var length = 0;
            for (var m = 0; m < assets.length; m++) length = Math.max(length, assets[m].length || 1);
            if (length <= 0) length = 1;

            var instrument;
            if (assets.length > 1) {
                instrument = track.addSound(event.timeline, "MultiSound", 0, length);
                instrument.name = parsed.name;
                for (var s = 0; s < assets.length; s++) {
                    var single = studio.project.create("SingleSound");
                    single.audioFile = assets[s];
                    single.owner = instrument;
                }
            } else {
                instrument = track.addSound(event.timeline, "SingleSound", 0, length);
                instrument.audioFile = assets[0];
                instrument.name = parsed.name;
            }

            // Луп (музыка/амбиент): инструмент крутится, событие persistent — иначе оно само себя остановит.
            if (cat.looping) {
                try {
                    instrument.looping = true;
                    instrument.playCount = 0;
                    event.automatableProperties.isPersistent = true;
                    track.streaming = true;   // музыка/амбиент не грузятся в память целиком
                } catch (le) { log("WARN", entry.path, "looping failed: " + le); }
            }

            // Рандомизация: против «пулемётного» повтора одного сэмпла.
            if (cat.pitchSt) {
                try { instrument.addModulator("RandomizerModulator", "pitch").amount = cat.pitchSt; }
                catch (pe) { log("WARN", entry.path, "pitch modulator failed: " + pe); }
            }
            if (cat.volRandDb) {
                try { instrument.addModulator("RandomizerModulator", "volume").amount = cat.volRandDb; }
                catch (ve) { log("WARN", entry.path, "volume modulator failed: " + ve); }
            }

            // Банк + роутинг в под-шину + категорийная громкость.
            try { event.relationships.banks.add(cat.looping ? musicBank : sfxBank); }
            catch (be) { log("WARN", entry.path, "bank assign failed: " + be); }

            var targetBus = busByPath[cat.bus];
            if (targetBus) {
                try { event.mixerInput.output = targetBus; }
                catch (re) { log("WARN", entry.path, "route failed: " + re); }
            } else {
                log("WARN", entry.path, "bus not found: " + cat.bus);
            }
            try { event.mixerInput.volume = cat.volumeDb || 0; }
            catch (vo) { log("WARN", entry.path, "event volume failed: " + vo); }

            // Voice-макросы: сколько одновременных копий, кого душить и как часто пускать.
            try {
                var ap = event.automatableProperties;
                if (cat.maxVoices) ap.maxVoices = cat.maxVoices;
                if (cat.stealing !== undefined) ap.voiceStealing = cat.stealing;
                if (cat.priority !== undefined) ap.priority = cat.priority;
                if (cat.cooldownMs) ap.triggerCooldown = cat.cooldownMs / 1000.0;
            } catch (me) { log("WARN", entry.path, "macros failed: " + me); }

            built++;
            log("OK", entry.path, assets.length + " file(s), cat=" + entry.category + ", bus=" + cat.bus);
            if (built % 20 === 0) flush();
        }

        var purged = destroyUnusedAssets();
        log("CLEANUP", "unused assets", purged + " removed");

        log("SAVING", "project", "");
        flush();
        studio.project.save();
        log("DONE", "populate", "built " + built + ", failed " + failed + " — теперь fmodstudiocl -build -ignore-warnings -export-guids");
        flush();
    } catch (err) {
        log("FATAL", "populate", "" + err);
        flush();
    }
})();
