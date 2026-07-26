# -*- coding: utf-8 -*-
"""
Карта звука Guildmaster: ключ каталога -> FMOD-событие -> исходные сэмплы.

Единственный источник правды для аудио-пайплайна (spec: docs/wiki/tech/40-planning/sfx-round-2.md).
Из неё генерируются: FMOD Project/SourceAudio (нормализованные копии), manifest.json и populate.js.

Ключ каталога — канон {contentId}.{action} (AudioResolver). isDefault=True — это per-action
дефолт, и тогда key = имя действия ("hit", "ui", ...).

Категория задаёт микс: под-шину, стартовый offset громкости, рандомизацию и voice-макросы
(анти-каша плотного боя). Значения — стартовые, докручиваются по слуху через Live Update.
"""

# --- склады исходников (пути от корня репо) ---
KI   = "Assets/Kenney/kenney_interface-sounds/Audio"      # CC0
KUI  = "Assets/Kenney/kenney_ui-audio/Audio"              # CC0
KRPG = "Assets/Kenney/kenney_rpg-audio/Audio"             # CC0
KIMP = "Assets/Kenney/kenney_impact-sounds/Audio"         # CC0
KMJ  = "Assets/Kenney/kenney_music-jingles/Audio"         # CC0
RPGE = "Assets/Kenney/RPG_Essentials_Free"                # free for commercial, credit optional

# --- целевая громкость нормализации ---
TARGET_RMS_DB = -23.0   # RMS активной части (порог тишины -45 dB)
TRUE_PEAK_DB  = -1.0    # потолок пика; гейн никогда не покупается клиппингом

# --- категории: под-шина, offset, рандомизация, анти-каша ---
# stealing: 0=Oldest 1=Furthest 2=Quietest 3=Virtualize 4=None
# priority: 0=Lowest 1=Low 2=Medium 3=High 4=Highest
CATEGORIES = {
    "impact":  dict(bus="SFX/Combat",  volumeDb= 0.0, pitchSt=2.5, volRandDb=3.0, maxVoices=4, cooldownMs=50, stealing=2, priority=1, spatial=True),
    "whoosh":  dict(bus="SFX/Combat",  volumeDb=-2.0, pitchSt=1.5, volRandDb=3.0, maxVoices=4, cooldownMs=60, stealing=2, priority=1, spatial=True),
    "tonal":   dict(bus="SFX/Combat",  volumeDb=-3.0, pitchSt=0.5, volRandDb=2.0, maxVoices=3, cooldownMs=60, stealing=3, priority=2, spatial=True),
    "cast":    dict(bus="SFX/Combat",  volumeDb=-1.0, pitchSt=1.0, volRandDb=2.0, maxVoices=2, cooldownMs=50, stealing=3, priority=2, spatial=True),
    "death":   dict(bus="SFX/Combat",  volumeDb= 0.0, pitchSt=2.0, volRandDb=3.0, maxVoices=3, cooldownMs=80, stealing=0, priority=3, spatial=True),
    "stinger": dict(bus="SFX/Stingers",volumeDb= 0.0, pitchSt=0.0, volRandDb=0.0, maxVoices=1, cooldownMs=250, stealing=4, priority=4),
    "ui":      dict(bus="SFX/UI",      volumeDb=-2.0, pitchSt=1.0, volRandDb=2.0, maxVoices=2, cooldownMs=30, stealing=0, priority=3),
    "ui_soft": dict(bus="SFX/UI",      volumeDb=-9.0, pitchSt=1.0, volRandDb=2.0, maxVoices=2, cooldownMs=40, stealing=0, priority=2),
    "music":   dict(bus="Music",       volumeDb= 0.0, pitchSt=0.0, volRandDb=0.0, maxVoices=1, cooldownMs=0,  stealing=4, priority=4, looping=True),
    "ambient": dict(bus="SFX/Ambient", volumeDb=-6.0, pitchSt=0.0, volRandDb=0.0, maxVoices=1, cooldownMs=0,  stealing=4, priority=2, looping=True),
}

# --- шинная иерархия (родитель -> дети); создаётся под bus:/ ---
BUS_TREE = {
    "SFX":   dict(parent=None,  volumeDb=0.0),
    "Music": dict(parent=None,  volumeDb=-6.0),
    "SFX/Combat":   dict(parent="SFX", volumeDb=0.0),
    "SFX/UI":       dict(parent="SFX", volumeDb=-5.0),
    "SFX/Ambient":  dict(parent="SFX", volumeDb=-9.0),
    "SFX/Stingers": dict(parent="SFX", volumeDb=1.0),
}

# --- пространственность боевых звуков ---
# Арена ~20x12 мировых единиц, слушатель в центре, поэтому расстояние до дальнего края ~15.
# minimumDistance берём ЗАВЕДОМО больше арены: внутри неё громкость падать не должна — иначе бой
# у края поля станет тише боя в центре, а это читается как «звук сломался», а не как глубина.
# Панорама при этом работает на любом расстоянии: она считается по углу, а не по дистанции.
SPATIAL = dict(
    minimumDistance=25.0,     # внутри арены затухания практически нет
    maximumDistance=120.0,    # запас, чтобы улетевший снаряд не обрывался ступенькой
    panBlend=1.0,             # полный стерео-пан по позиции
    stereoSeparation=45.0,    # уже дефолтных 60: арена узкая, крайние звуки не должны улетать в один канал
    dopplerMultiplier=0.0,    # доплера в 2D-автобаттлере быть не должно
)

# --- дакинг: стингер поджимает боевую шину, чтобы его было слышно поверх мясорубки ---
# Компрессор вешается на ЦЕЛЬ, а sidechain-источник — на шину-триггер (решение [[audio-subbuses]]).
DUCKING = dict(
    target="SFX/Combat",      # что поджимаем
    source="SFX/Stingers",    # чем триггерим
    threshold=-22.0,          # dB: боевая шина обычно ходит около -20, так что дак срабатывает по стингеру
    ratio=4.0,
    attackMs=10.0,            # быстро, иначе стингер потонет в первых миллисекундах
    releaseMs=350.0,          # медленно, иначе бой «выныривает» рывком
    makeupDb=0.0,
)

# --- глобальный параметр микса (контракт с AudioParameters.TimeScale) ---
TIME_SCALE_PARAM = dict(
    name="TimeScale", minimum=0.05, maximum=3.0, initial=1.0,
    # автоматизация питча bus:/SFX/Combat: 12*log2(ts), кламп в диапазон питча FMOD (-24..24 st)
    bus="SFX/Combat",
    curve=[(0.05, -24.0), (0.25, -24.0), (0.5, -12.0), (0.75, -4.98), (1.0, 0.0),
           (1.5, 7.02), (2.0, 12.0), (3.0, 19.02)],
)


def E(key, cat, files, action=None, default=False, path=None):
    """Одна запись карты. path вычисляется из ключа, если не задан явно."""
    return dict(key=key, category=cat, files=files, action=action, isDefault=default, path=path)


# =============================================================================
# 1. Per-action дефолты (обязательны: резолвер падает на них, когда точной записи нет)
# =============================================================================
DEFAULTS = [
    E("attack",  "whoosh",  [f"{RPGE}/10_Battle_SFX/22_Slash_04.wav",
                             f"{RPGE}/12_Player_Movement_SFX/56_Attack_03.wav",
                             f"{KRPG}/knifeSlice.ogg", f"{KRPG}/knifeSlice2.ogg"], action="Attack", default=True,
      path="event:/SFX/Combat/attack"),
    E("fire",    "whoosh",  [f"{RPGE}/8_Atk_Magic_SFX/25_Wind_01.wav",
                             f"{KRPG}/cloth1.ogg", f"{KRPG}/cloth3.ogg"], action="Fire", default=True,
      path="event:/SFX/Combat/fire"),
    E("hit",     "impact",  [f"{KIMP}/impactPunch_medium_000.ogg", f"{KIMP}/impactPunch_medium_001.ogg",
                             f"{KIMP}/impactPunch_medium_002.ogg", f"{KIMP}/impactPunch_medium_003.ogg",
                             f"{KIMP}/impactPunch_medium_004.ogg", f"{RPGE}/10_Battle_SFX/15_Impact_flesh_02.wav",
                             f"{RPGE}/10_Battle_SFX/77_flesh_02.wav"], action="Hit", default=True,
      path="event:/SFX/Combat/hit"),
    E("evade",   "whoosh",  [f"{RPGE}/10_Battle_SFX/35_Miss_Evade_02.wav",
                             f"{KRPG}/cloth2.ogg", f"{KRPG}/cloth4.ogg"], action="Evade", default=True,
      path="event:/SFX/Combat/evade"),
    E("shield",  "impact",  [f"{KIMP}/impactMetal_medium_000.ogg", f"{KIMP}/impactMetal_medium_001.ogg",
                             f"{KIMP}/impactMetal_medium_002.ogg", f"{KIMP}/impactMetal_medium_003.ogg",
                             f"{KIMP}/impactMetal_medium_004.ogg", f"{RPGE}/10_Battle_SFX/39_Block_03.wav"],
      action="Shield", default=True, path="event:/SFX/Combat/shield"),
    E("heal",    "tonal",   [f"{RPGE}/8_Buffs_Heals_SFX/02_Heal_02.wav"], action="Heal", default=True,
      path="event:/SFX/Combat/heal"),
    E("cast",    "cast",    [f"{RPGE}/8_Atk_Magic_SFX/45_Charge_05.wav"], action="Cast", default=True,
      path="event:/SFX/Combat/cast"),
    E("death",   "death",   [f"{KIMP}/impactSoft_heavy_000.ogg", f"{KIMP}/impactSoft_heavy_001.ogg",
                             f"{KIMP}/impactSoft_heavy_002.ogg", f"{KIMP}/impactSoft_heavy_003.ogg",
                             f"{KIMP}/impactSoft_heavy_004.ogg", f"{RPGE}/10_Battle_SFX/69_Enemy_death_01.wav"],
      action="Death", default=True, path="event:/SFX/Combat/death"),
    E("apply",   "tonal",   [f"{RPGE}/8_Buffs_Heals_SFX/16_Atk_buff_04.wav",
                             f"{RPGE}/8_Buffs_Heals_SFX/17_Def_buff_01.wav"], action="Apply", default=True,
      path="event:/SFX/Combat/apply"),
    E("expire",  "tonal",   [f"{RPGE}/8_Buffs_Heals_SFX/21_Debuff_01.wav"], action="Expire", default=True,
      path="event:/SFX/Combat/expire"),
    E("tick",    "tonal",   [f"{RPGE}/8_Atk_Magic_SFX/46_Poison_01.wav"], action="Tick", default=True,
      path="event:/SFX/Combat/tick"),
    E("ui",      "ui",      [f"{KI}/click_001.ogg", f"{KI}/click_002.ogg", f"{KI}/click_003.ogg",
                             f"{KI}/click_004.ogg", f"{KI}/click_005.ogg"], action="Ui", default=True,
      path="event:/SFX/UI/ui"),
    E("stinger", "stinger", [f"{RPGE}/10_Battle_SFX/55_Encounter_02.wav"], action="Stinger", default=True,
      path="event:/Stingers/stinger"),
    # Дефолт Loop — нейтральный room tone: любой незаданный .loop даёт фон, а не тишину-сюрприз.
    E("loop",    "ambient", ["music/arena_ambient.ogg"], action="Loop", default=True,
      path="event:/SFX/Ambient/loop"),
]

# =============================================================================
# 2. Бой: точечные ключи
# =============================================================================
COMBAT = [
    E("battle.start.stinger",   "stinger", [f"{RPGE}/10_Battle_SFX/55_Encounter_02.wav"], path="event:/Stingers/battle_start"),
    E("battle.victory.stinger", "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL00.ogg"],  path="event:/Stingers/victory"),
    E("battle.defeat.stinger",  "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL16.ogg"],  path="event:/Stingers/defeat"),

    E("combat.unit_spawn.ui",   "ui_soft", [f"{KI}/pluck_001.ogg", f"{KI}/pluck_002.ogg"], path="event:/SFX/Combat/unit_spawn"),
    E("combat.attack_interrupted.evade", "ui_soft", [f"{KI}/scratch_001.ogg", f"{KI}/scratch_002.ogg"],
      path="event:/SFX/Combat/attack_interrupted"),
    E("enemy.training_dummy.hit", "impact", [f"{KIMP}/impactWood_medium_000.ogg", f"{KIMP}/impactWood_medium_001.ogg",
                                             f"{KIMP}/impactWood_medium_002.ogg", f"{KIMP}/impactWood_medium_003.ogg"],
      path="event:/SFX/Enemies/training_dummy/hit"),
]

# =============================================================================
# 3. Feel-слой (визуал уже есть, звук догоняет)
# =============================================================================
FEEL = [
    E("feel.kill.stinger",         "stinger", [f"{KIMP}/impactBell_heavy_000.ogg", f"{KIMP}/impactBell_heavy_001.ogg",
                                               f"{KIMP}/impactBell_heavy_002.ogg", f"{KIMP}/impactBell_heavy_003.ogg",
                                               f"{KIMP}/impactBell_heavy_004.ogg"], path="event:/SFX/Feel/kill"),
    E("feel.heavy_hit.hit",        "impact",  [f"{KIMP}/impactPunch_heavy_000.ogg", f"{KIMP}/impactPunch_heavy_001.ogg",
                                               f"{KIMP}/impactPunch_heavy_002.ogg", f"{KIMP}/impactPunch_heavy_003.ogg",
                                               f"{KIMP}/impactPunch_heavy_004.ogg"], path="event:/SFX/Feel/heavy_hit"),
    E("feel.death_shatter.death",  "impact",  [f"{KIMP}/impactGlass_heavy_000.ogg", f"{KIMP}/impactGlass_heavy_001.ogg",
                                               f"{KIMP}/impactGlass_heavy_002.ogg", f"{KIMP}/impactGlass_heavy_003.ogg",
                                               f"{KIMP}/impactGlass_heavy_004.ogg"], path="event:/SFX/Feel/death_shatter"),
    E("feel.finisher.stinger",     "stinger", [f"{KIMP}/impactBell_heavy_002.ogg", f"{KI}/bong_001.ogg"],
      path="event:/SFX/Feel/finisher"),
]

# =============================================================================
# 4. Реликвии (герои) — только там, где нужна узнаваемость поверх дефолта
# =============================================================================
RELICS = [
    E("relic.cryomancer.attack",    "tonal",  [f"{RPGE}/8_Atk_Magic_SFX/13_Ice_explosion_01.wav"], path="event:/SFX/Relics/cryomancer/attack"),
    E("relic.cryomancer.cast",      "cast",   [f"{RPGE}/8_Atk_Magic_SFX/13_Ice_explosion_01.wav",
                                               f"{RPGE}/8_Atk_Magic_SFX/45_Charge_05.wav"], path="event:/SFX/Relics/cryomancer/cast"),
    E("relic.light_shepherd.cast",  "tonal",  [f"{RPGE}/8_Buffs_Heals_SFX/30_Revive_03.wav"], path="event:/SFX/Relics/light_shepherd/cast"),
    E("relic.light_shepherd.fire",  "tonal",  [f"{KI}/pluck_001.ogg"], path="event:/SFX/Relics/light_shepherd/fire"),
    E("relic.whirl_monk.cast",      "whoosh", [f"{RPGE}/12_Player_Movement_SFX/88_Teleport_02.wav"], path="event:/SFX/Relics/whirl_monk/cast"),
    E("relic.assassin.cast",        "whoosh", [f"{RPGE}/8_Atk_Magic_SFX/25_Wind_01.wav"], path="event:/SFX/Relics/assassin/cast"),
    E("relic.ranger.fire",          "whoosh", [f"{KRPG}/cloth1.ogg", f"{KRPG}/cloth4.ogg"], path="event:/SFX/Relics/ranger/fire"),
    E("relic.iron_spearman.attack", "whoosh", [f"{KRPG}/drawKnife1.ogg", f"{KRPG}/drawKnife3.ogg"], path="event:/SFX/Relics/iron_spearman/attack"),
    E("relic.defender.shield",      "impact", [f"{KIMP}/impactPlate_heavy_000.ogg", f"{KIMP}/impactPlate_heavy_001.ogg",
                                               f"{KIMP}/impactPlate_heavy_002.ogg"], path="event:/SFX/Relics/defender/shield"),
    E("relic.flame_swordsman.attack", "whoosh", [f"{KRPG}/chop.ogg", f"{KRPG}/knifeSlice.ogg"], path="event:/SFX/Relics/flame_swordsman/attack"),
    E("relic.treant.attack",        "impact", [f"{KIMP}/impactPlank_medium_000.ogg", f"{KIMP}/impactPlank_medium_001.ogg"],
      path="event:/SFX/Relics/treant/attack"),
    E("relic.druid.cast",           "tonal",  [f"{RPGE}/8_Buffs_Heals_SFX/17_Def_buff_01.wav"], path="event:/SFX/Relics/druid/cast"),
]

# =============================================================================
# 5. Эффекты/статусы (id из ScriptableObjects/Effects)
# =============================================================================
EFFECTS = [
    E("effect.frozen.apply",   "tonal",  [f"{RPGE}/8_Atk_Magic_SFX/13_Ice_explosion_01.wav"], path="event:/SFX/Effects/frozen/apply"),
    E("effect.frozen.expire",  "impact", [f"{KIMP}/impactGlass_medium_000.ogg", f"{KIMP}/impactGlass_medium_001.ogg",
                                          f"{KIMP}/impactGlass_medium_002.ogg", f"{KIMP}/impactGlass_medium_003.ogg",
                                          f"{KIMP}/impactGlass_medium_004.ogg"], path="event:/SFX/Effects/frozen/expire"),
    E("effect.burn.apply",     "tonal",  [f"{RPGE}/8_Atk_Magic_SFX/25_Wind_01.wav"], path="event:/SFX/Effects/burn/apply"),
    E("effect.burn.tick",      "ui_soft",[f"{RPGE}/8_Atk_Magic_SFX/46_Poison_01.wav"], path="event:/SFX/Effects/burn/tick"),
    E("effect.ignition.apply", "cast",   [f"{RPGE}/8_Atk_Magic_SFX/45_Charge_05.wav"], path="event:/SFX/Effects/ignition/apply"),
    E("effect.spore_cloud.apply", "tonal", [f"{RPGE}/8_Atk_Magic_SFX/46_Poison_01.wav"], path="event:/SFX/Effects/spore_cloud/apply"),
    E("effect.ice_chains_stun.apply", "impact", [f"{KIMP}/impactMetal_heavy_000.ogg", f"{KIMP}/impactMetal_heavy_001.ogg"],
      path="event:/SFX/Effects/ice_chains_stun/apply"),
    E("effect.resolute_strike_stun.apply", "impact", [f"{KIMP}/impactTin_medium_000.ogg", f"{KIMP}/impactTin_medium_001.ogg"],
      path="event:/SFX/Effects/resolute_strike_stun/apply"),
    E("effect.stealth.apply",  "whoosh", [f"{KRPG}/cloth2.ogg", f"{KRPG}/cloth4.ogg"], path="event:/SFX/Effects/stealth/apply"),
    E("effect.stealth_buff.apply", "whoosh", [f"{KRPG}/cloth1.ogg"], path="event:/SFX/Effects/stealth_buff/apply"),
    E("effect.hunters_mark.apply", "ui_soft", [f"{KI}/tick_001.ogg", f"{KI}/tick_002.ogg"], path="event:/SFX/Effects/hunters_mark/apply"),
    E("effect.bulwark_shield.apply", "impact", [f"{KIMP}/impactPlate_medium_000.ogg", f"{KIMP}/impactPlate_medium_001.ogg"],
      path="event:/SFX/Effects/bulwark_shield/apply"),
    E("effect.dodge.apply",    "ui_soft",[f"{KI}/tick_004.ogg"], path="event:/SFX/Effects/dodge/apply"),
    E("effect.vortex_entry.apply", "whoosh", [f"{RPGE}/12_Player_Movement_SFX/88_Teleport_02.wav"], path="event:/SFX/Effects/vortex_entry/apply"),
    E("effect.overgrowth.apply", "tonal", [f"{RPGE}/8_Buffs_Heals_SFX/17_Def_buff_01.wav"], path="event:/SFX/Effects/overgrowth/apply"),
    E("effect.light_mend.tick", "ui_soft", [f"{KI}/pluck_002.ogg"], path="event:/SFX/Effects/light_mend/tick"),
]

# =============================================================================
# 6. UI-мелочь (один шов на корне панели)
# =============================================================================
UI = [
    E("ui.click.ui",     "ui",      [f"{KUI}/click1.ogg", f"{KUI}/click2.ogg", f"{KUI}/click3.ogg",
                                     f"{KUI}/click4.ogg", f"{KUI}/click5.ogg"], path="event:/SFX/UI/click"),
    E("ui.hover.ui",     "ui_soft", [f"{KUI}/rollover1.ogg", f"{KUI}/rollover2.ogg", f"{KUI}/rollover3.ogg",
                                     f"{KUI}/rollover4.ogg", f"{KUI}/rollover5.ogg"], path="event:/SFX/UI/hover"),
    E("ui.tab.ui",       "ui",      [f"{KI}/switch_001.ogg", f"{KI}/switch_003.ogg", f"{KI}/switch_005.ogg"], path="event:/SFX/UI/tab"),
    E("ui.toggle.ui",    "ui",      [f"{KI}/toggle_001.ogg", f"{KI}/toggle_002.ogg", f"{KI}/toggle_003.ogg"], path="event:/SFX/UI/toggle"),
    E("ui.slider.ui",    "ui_soft", [f"{KI}/tick_001.ogg", f"{KI}/tick_002.ogg", f"{KI}/tick_004.ogg"], path="event:/SFX/UI/slider"),
    E("ui.back.ui",      "ui",      [f"{KI}/back_001.ogg", f"{KI}/back_002.ogg", f"{KI}/back_003.ogg"], path="event:/SFX/UI/back"),
    E("ui.disabled.ui",  "ui",      [f"{KI}/error_002.ogg", f"{KI}/error_004.ogg", f"{KI}/error_006.ogg"], path="event:/SFX/UI/disabled"),
    E("ui.tooltip_show.ui", "ui_soft", [f"{KRPG}/bookFlip1.ogg", f"{KRPG}/bookFlip2.ogg"], path="event:/SFX/UI/tooltip_show"),
    E("ui.tooltip_detail.ui", "ui_soft", [f"{KRPG}/bookFlip3.ogg"], path="event:/SFX/UI/tooltip_detail"),
    E("ui.screen_open.ui",  "ui",   [f"{KRPG}/bookOpen.ogg"], path="event:/SFX/UI/screen_open"),
    E("ui.screen_close.ui", "ui",   [f"{KRPG}/bookClose.ogg"], path="event:/SFX/UI/screen_close"),
    E("ui.modal_open.ui",   "ui",   [f"{KI}/maximize_003.ogg", f"{KI}/maximize_006.ogg"], path="event:/SFX/UI/modal_open"),
    E("ui.drag_grab.ui",    "ui",   [f"{KRPG}/beltHandle1.ogg", f"{KRPG}/beltHandle2.ogg"], path="event:/SFX/UI/drag_grab"),
    E("ui.drag_drop.ui",    "ui",   [f"{KI}/drop_001.ogg", f"{KI}/drop_002.ogg", f"{KI}/drop_003.ogg"], path="event:/SFX/UI/drag_drop"),
    E("ui.drag_reject.ui",  "ui",   [f"{KI}/error_001.ogg", f"{KI}/error_003.ogg"], path="event:/SFX/UI/drag_reject"),
    E("ui.scroll.ui",       "ui_soft", [f"{KI}/scroll_001.ogg", f"{KI}/scroll_003.ogg", f"{KI}/scroll_005.ogg"], path="event:/SFX/UI/scroll"),
    E("ui.pause.ui",        "ui",   [f"{RPGE}/10_UI_Menu_SFX/092_Pause_04.wav"], path="event:/SFX/UI/pause"),
    E("ui.resume.ui",       "ui",   [f"{RPGE}/10_UI_Menu_SFX/098_Unpause_04.wav"], path="event:/SFX/UI/resume"),
    E("ui.speed.ui",        "ui_soft", [f"{KI}/tick_002.ogg"], path="event:/SFX/UI/speed"),
]

# =============================================================================
# 7. Расстановка отряда и снаряжение
# =============================================================================
DEPLOY = [
    E("ui.deploy_grab.ui",    "ui", [f"{KRPG}/handleSmallLeather.ogg", f"{KRPG}/handleSmallLeather2.ogg"], path="event:/SFX/UI/deploy_grab"),
    E("ui.deploy_place.ui",   "ui", [f"{RPGE}/10_UI_Menu_SFX/070_Equip_10.wav", f"{KRPG}/dropLeather.ogg"], path="event:/SFX/UI/deploy_place"),
    E("ui.deploy_reject.ui",  "ui", [f"{RPGE}/10_UI_Menu_SFX/033_Denied_03.wav"], path="event:/SFX/UI/deploy_reject"),
    E("ui.relic_equip.ui",    "ui", [f"{KRPG}/metalClick.ogg", f"{KRPG}/metalLatch.ogg"], path="event:/SFX/UI/relic_equip"),
    E("ui.relic_unequip.ui",  "ui", [f"{RPGE}/10_UI_Menu_SFX/071_Unequip_01.wav"], path="event:/SFX/UI/relic_unequip"),
    E("ui.relic_select.ui",   "ui", [f"{KI}/select_001.ogg", f"{KI}/select_003.ogg", f"{KI}/select_005.ogg"], path="event:/SFX/UI/relic_select"),
]

# =============================================================================
# 8. Карта акта
# =============================================================================
MAP = [
    E("map.node_hover.ui",    "ui_soft", [f"{RPGE}/10_UI_Menu_SFX/001_Hover_01.wav"], path="event:/SFX/Map/node_hover"),
    E("map.node_select.ui",   "ui",      [f"{KI}/select_002.ogg", f"{KI}/select_004.ogg", f"{KI}/select_006.ogg"], path="event:/SFX/Map/node_select"),
    E("map.node_locked.ui",   "ui",      [f"{RPGE}/10_UI_Menu_SFX/029_Decline_09.wav", f"{KI}/error_005.ogg"], path="event:/SFX/Map/node_locked"),
    E("map.travel_start.ui",  "ui_soft", [f"{KRPG}/cloth1.ogg"], path="event:/SFX/Map/travel_start"),
    E("map.travel_arrive.ui", "ui",      [f"{KI}/bong_001.ogg"], path="event:/SFX/Map/travel_arrive"),
    E("map.open.ui",          "ui",      [f"{KI}/open_001.ogg", f"{KI}/open_003.ogg"], path="event:/SFX/Map/open"),
    E("map.close.ui",         "ui",      [f"{KI}/close_001.ogg", f"{KI}/close_003.ogg"], path="event:/SFX/Map/close"),
]

# =============================================================================
# 9. Флоу забега: переходы, награда, лавка, сундук, привал, ивент, исход, меню
# =============================================================================
FLOW = [
    E("flow.fade_in.ui",     "ui_soft", [f"{KI}/minimize_003.ogg"], path="event:/SFX/Flow/fade_in"),
    E("flow.fade_out.ui",    "ui_soft", [f"{KI}/maximize_003.ogg"], path="event:/SFX/Flow/fade_out"),

    E("reward.open.stinger", "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL07.ogg"], path="event:/Stingers/reward_open"),
    E("reward.card_select.ui", "ui",    [f"{KI}/select_007.ogg", f"{KI}/select_008.ogg"], path="event:/SFX/Flow/reward_card_select"),
    E("reward.take.stinger", "stinger", [f"{KMJ}/Pizzicato jingles/jingles_PIZZI00.ogg"], path="event:/Stingers/reward_take"),
    E("reward.skip.ui",      "ui",      [f"{KI}/back_004.ogg"], path="event:/SFX/Flow/reward_skip"),
    E("run.gold_gain.ui",    "ui",      [f"{KRPG}/handleCoins.ogg", f"{KRPG}/handleCoins2.ogg"], path="event:/SFX/Flow/gold_gain"),

    E("shop.buy.ui",         "ui",      [f"{RPGE}/10_UI_Menu_SFX/079_Buy_sell_01.wav"], path="event:/SFX/Flow/shop_buy"),
    E("shop.sell.ui",        "ui",      [f"{KRPG}/handleCoins2.ogg"], path="event:/SFX/Flow/shop_sell"),
    E("shop.reroll.ui",      "ui",      [f"{KI}/switch_006.ogg", f"{KI}/switch_007.ogg"], path="event:/SFX/Flow/shop_reroll"),
    E("shop.denied.ui",      "ui",      [f"{RPGE}/10_UI_Menu_SFX/033_Denied_03.wav"], path="event:/SFX/Flow/shop_denied"),

    E("chest.open.stinger",  "stinger", [f"{KRPG}/creak2.ogg"], path="event:/Stingers/chest_open"),
    E("camp.action.ui",      "ui",      [f"{RPGE}/10_UI_Menu_SFX/051_use_item_01.wav"], path="event:/SFX/Flow/camp_action"),
    E("camp.denied.ui",      "ui",      [f"{KI}/error_007.ogg"], path="event:/SFX/Flow/camp_denied"),
    E("event.choice.ui",     "ui",      [f"{KI}/confirmation_001.ogg", f"{KI}/confirmation_003.ogg"], path="event:/SFX/Flow/event_choice"),

    E("run.start.stinger",   "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL04.ogg"], path="event:/Stingers/run_start"),
    E("run.outcome_victory.stinger", "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL10.ogg"], path="event:/Stingers/run_victory"),
    E("run.outcome_defeat.stinger",  "stinger", [f"{KMJ}/Steel jingles/jingles_STEEL13.ogg"], path="event:/Stingers/run_defeat"),
    E("menu.title_card.stinger", "stinger", [f"{KMJ}/Pizzicato jingles/jingles_PIZZI09.ogg"], path="event:/Stingers/title_card"),
    E("menu.show.ui",        "ui",      [f"{KRPG}/doorOpen_2.ogg"], path="event:/SFX/Flow/menu_show"),
    E("menu.hide.ui",        "ui",      [f"{KRPG}/doorClose_2.ogg"], path="event:/SFX/Flow/menu_hide"),
]

# =============================================================================
# 10. Музыка и амбиент (лупы; играются через хранимый EventInstance)
#     Файлы кладутся отдельно (см. build_source_audio.py --music), ключи заведены заранее.
# =============================================================================
MUSIC = [
    E("music.menu.loop",    "music",   ["music/menu.mp3"],    path="event:/Music/menu"),
    E("music.map.loop",     "music",   ["music/map.mp3"],     path="event:/Music/map"),
    E("music.battle.loop",  "music",   ["music/battle.mp3"],  path="event:/Music/battle"),
    E("ambient.arena.loop", "ambient", ["music/arena_ambient.ogg"], path="event:/SFX/Ambient/arena"),
]

# Музыка нормализуется иначе, чем one-shot: интегральный LUFS (EBU R128), а не RMS активной части.
MUSIC_LUFS = -18.0        # музыка сидит ниже боевого якоря, слайдер Music её ещё поджимает
AMBIENT_LUFS = -24.0      # амбиент — фон, не претендует на внимание

ALL = DEFAULTS + COMBAT + FEEL + RELICS + EFFECTS + UI + DEPLOY + MAP + FLOW + MUSIC

# =============================================================================
# 11. Описания для CLAP-сверки (scripts/audio/clap_pick.py --verify)
#     Текст на английском намеренно: модель обучена на англоязычных описаниях звука.
#     Ключи без описания сверяются по расшифровке самого ключа — этого хватает для UI и флоу,
#     а вот боевой смысл («лёд», «металл», «плоть») нужно проговаривать явно.
# =============================================================================
DESCRIPTIONS = {
    "attack":  "sword swing, weapon whoosh through air",
    "fire":    "arrow shot, projectile launch whoosh",
    "hit":     "punch impact on flesh, heavy body hit",
    "evade":   "quick dodge whoosh, cloth swish, missed swing",
    "shield":  "metal shield block, armour clang",
    "heal":    "magical healing chime, soft warm bell",
    "cast":    "magic spell charge up, arcane energy",
    "death":   "body falls, soft heavy thud, death groan",
    "apply":   "magical buff applied, short shimmer",
    "expire":  "magic effect fades away, debuff wears off",
    "tick":    "soft poison bubble, small periodic damage tick",
    "stinger": "short dramatic musical accent",

    "relic.cryomancer.attack":  "ice shard impact, frost crackle, freezing magic",
    "relic.cryomancer.cast":    "ice explosion, freezing blast spell",
    "relic.light_shepherd.cast": "holy light magic, healing radiance",
    "relic.light_shepherd.fire": "gentle magical light projectile",
    "relic.whirl_monk.cast":    "fast air dash whoosh, teleport",
    "relic.assassin.cast":      "stealth vanish, sudden wind whoosh",
    "relic.ranger.fire":        "bow shot, arrow release",
    "relic.iron_spearman.attack": "spear thrust, metal blade draw",
    "relic.defender.shield":    "heavy metal plate block",
    "relic.flame_swordsman.attack": "sword chop, blade slice",
    "relic.treant.attack":      "wooden branch hit, heavy timber impact",
    "relic.druid.cast":         "nature magic, growing plants buff",

    "effect.frozen.apply":  "ice crystallising, freezing over",
    "effect.frozen.expire": "glass shatter, ice breaking apart",
    "effect.burn.apply":    "fire ignites, flame whoosh",
    "effect.burn.tick":     "small fire crackle",
    "effect.ignition.apply": "flame charge up, fire building",
    "effect.spore_cloud.apply": "poison cloud, toxic bubbling",
    "effect.ice_chains_stun.apply": "heavy metal chains, iron clamp",
    "effect.resolute_strike_stun.apply": "metallic stun clang",
    "effect.stealth.apply": "cloth rustle, sneaking into shadow",
    "effect.hunters_mark.apply": "small targeting tick, marker beep",
    "effect.bulwark_shield.apply": "metal plate shield raised",
    "effect.vortex_entry.apply": "teleport whoosh, air vortex",
    "effect.overgrowth.apply": "nature growth buff, plants rising",
    "effect.light_mend.tick": "gentle healing pluck",

    "feel.kill.stinger":        "heavy bell impact, dramatic kill accent",
    "feel.heavy_hit.hit":       "very heavy punch, bass impact",
    "feel.death_shatter.death": "glass shattering into pieces",
    "feel.finisher.stinger":    "dramatic low bell, final blow accent",

    "combat.unit_spawn.ui":  "soft magical pluck, unit appears",
    "enemy.training_dummy.hit": "wooden dummy hit, hollow wood impact",

    "map.node_hover.ui":   "soft interface hover blip",
    "map.node_select.ui":  "interface select confirm click",
    "map.node_locked.ui":  "interface error buzz, denied",
    "map.travel_arrive.ui": "soft bell arrival chime",

    "run.gold_gain.ui":    "coins handled, money jingle",
    "shop.buy.ui":         "purchase confirm, coins",
    "chest.open.stinger":  "wooden chest creaking open",
    "ui.drag_grab.ui":     "leather belt handled, picking item up",
    "ui.drag_drop.ui":     "item dropped on surface",
    "ui.screen_open.ui":   "book opening, page turn",
    "ui.screen_close.ui":  "book closing",
    "ui.hover.ui":         "very soft interface rollover",
    "ui.click.ui":         "interface button click",
    "ui.disabled.ui":      "error buzz, action not allowed",
}

for _entry in ALL:
    if _entry["key"] in DESCRIPTIONS:
        _entry["desc"] = DESCRIPTIONS[_entry["key"]]
