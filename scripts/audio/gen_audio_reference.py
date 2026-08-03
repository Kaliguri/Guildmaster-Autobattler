# -*- coding: utf-8 -*-
"""
Генератор reference-документа «что у нас звучит»: docs/wiki/tech/10-reference/audio-inventory.md.

Документ собирается ИЗ КАРТЫ (audio_map.py) и манифеста, а не пишется руками — иначе он
разойдётся с проектом на первой же правке звука, а это ровно та болезнь, от которой заведена
tech-вика. Правишь карту → прогоняешь пайплайн → перегенерируешь эту страницу.

Запуск:
    python scripts/audio/gen_audio_reference.py
"""
import json
import os
import sys
from collections import defaultdict, OrderedDict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audio_map as M  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MANIFEST = os.path.join(REPO, "FMOD Project", "Scripts", "manifest.json")
OUT = os.path.join(REPO, "docs", "wiki", "tech", "10-reference", "audio-inventory.md")
DATE = "2026-07-26"   # правится вместе с прогоном; в скрипте нет часов, чтобы вывод был воспроизводим

# Откуда взялся материал и на каких условиях. Проверено по License.txt в паках.
SOURCES = OrderedDict([
    (M.KI,   ("Kenney — Interface Sounds", "CC0", "без атрибуции")),
    (M.KUI,  ("Kenney — UI Audio", "CC0", "без атрибуции")),
    (M.KRPG, ("Kenney — RPG Audio", "CC0", "без атрибуции")),
    (M.KIMP, ("Kenney — Impact Sounds", "CC0", "без атрибуции")),
    (M.KMJ,  ("Kenney — Music Jingles", "CC0", "без атрибуции")),
    (M.RPGE, ("RPG Essentials Free (Leohpaz)", "бесплатно для коммерческих", "кредит опционален")),
])

MUSIC_SOURCES = [
    ("music.menu.loop", "Fantasy Orchestral Theme", "CC0", "OpenGameArt"),
    ("music.map.loop", "Town Theme (cynicmusic)", "CC0", "OpenGameArt"),
    ("music.battle.loop", "Battle Theme A (cynicmusic)", "CC0", "OpenGameArt"),
    ("ambient.arena.loop", "Loopable Dungeon Ambience", "CC0", "OpenGameArt"),
]

# Куда какой ключ подключён в коде: заполняется вручную по факту вызова (см. §5 sfx-round-2).
TRIGGERS = {
    "combat": "боевая симуляция → AudioPresenter",
    "battle": "фаза боя → RunAudioPresenter / CombatFeelDirector",
    "feel": "CombatFeelDirector и UnitView (killstinger, тяжёлый удар, финишер, разлёт)",
    "relic": "по contentId юнита из боевых событий",
    "effect": "EffectSystem.OnEffectApplied / OnEffectEnded",
    "enemy": "по contentId врага",
    "ui": "UiSoundSystem (корень панели), BattleInputController, DeploymentController",
    "map": "WorldMapView",
    "flow": "RunAudioPresenter (шторка перехода)",
    "reward": "MenuRouter + RunAudioPresenter",
    "shop": "ShopController",
    "camp": "CampScreenView через MenuRouter",
    "chest": "MenuRouter",
    "event": "MenuRouter",
    "run": "RunAudioPresenter, RunStateService",
    "menu": "RunAudioPresenter",
    "music": "RunAudioPresenter (одна дорожка за раз)",
    "ambient": "RunAudioPresenter (пока мир на первом плане)",
}


def source_of(path):
    for prefix, meta in SOURCES.items():
        if path.startswith(prefix):
            return meta
    return ("?", "?", "?")


def main():
    with open(MANIFEST, encoding="utf-8") as fh:
        manifest = json.load(fh)
    events = manifest["events"]
    by_key = {e["key"]: e for e in events}

    # Группируем по домену ключа (первая часть до точки), дефолты — отдельно.
    defaults = [e for e in events if e["isDefault"]]
    grouped = defaultdict(list)
    for e in events:
        if e["isDefault"]:
            continue
        grouped[e["key"].split(".")[0]].append(e)

    files_total = sum(len(e["files"]) for e in events)
    lines = []
    add = lines.append

    add("---")
    add('title: "Reference - Audio Inventory"')
    add("order: 55")
    add("status: ready")
    add(f"updated: {DATE}")
    add("---")
    add("")
    add("**Статус:** ready — СГЕНЕРИРОВАН `scripts/audio/gen_audio_reference.py` из карты звука.")
    add("Руками не править: правка уедет при следующем прогоне. Источник правды —")
    add("`scripts/audio/audio_map.py`, оттуда же собираются FMOD-события и каталог.")
    add("")
    add("---")
    add("")
    add("Что в игре звучит, чем именно, откуда взят материал и кто его дёргает. Одна страница,")
    add("чтобы вопрос «а этот звук у нас вообще есть и где он играет» не требовал раскопок по")
    add("трём слоям — карте, FMOD-проекту и коду.")
    add("")
    add("Как это устроено и почему — [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]].")
    add("")
    add("## Сводка")
    add("")
    add("| | |")
    add("|---|---|")
    add(f"| FMOD-событий | {len(events)} |")
    add(f"| из них per-action дефолтов | {len(defaults)} |")
    add(f"| исходных сэмплов | {files_total} |")
    add(f"| банки | `SFX.bank`, `Music.bank` (+ Master/strings) в `Assets/StreamingAssets` |")
    add(f"| целевая громкость | {M.TARGET_RMS_DB} dB RMS активной части, true peak ≤ {M.TRUE_PEAK_DB} dBFS |")
    add("")

    add("## Шины")
    add("")
    add("Слайдеры настроек пишут в `bus:/`, `bus:/Music`, `bus:/SFX` — поэтому под-шины обязаны")
    add("висеть именно под ними, иначе громкость никуда не доедет.")
    add("")
    add("| Шина | Уровень | Что в ней |")
    add("|---|---|---|")
    bus_content = defaultdict(list)
    for name, cat in M.CATEGORIES.items():
        bus_content[cat["bus"]].append(name)
    for bus, spec in M.BUS_TREE.items():
        cats = ", ".join(sorted(bus_content.get(bus, []))) or "— (родительская)"
        add(f"| `bus:/{bus}` | {spec['volumeDb']:+.0f} dB | {cats} |")
    add("")

    add("## Категории: микс и анти-каша")
    add("")
    add("Категория задаёт всё поведение звука в бою: громкость, разброс, сколько копий может")
    add("звучать разом и кого душить при переполнении.")
    add("")
    add("| Категория | Шина | Offset | Питч | Громк. | Голосов | Кулдаун | Stealing | Приоритет |")
    add("|---|---|---|---|---|---|---|---|---|")
    steal = {0: "Oldest", 1: "Furthest", 2: "Quietest", 3: "Virtualize", 4: "None"}
    prio = {0: "Lowest", 1: "Low", 2: "Medium", 3: "High", 4: "Highest"}
    for name, c in M.CATEGORIES.items():
        add(f"| `{name}` | {c['bus']} | {c['volumeDb']:+.0f} dB | ±{c['pitchSt']} st | −{c['volRandDb']} dB "
            f"| {c['maxVoices']} | {c['cooldownMs']} мс | {steal.get(c['stealing'], '?')} "
            f"| {prio.get(c['priority'], '?')} |")
    add("")
    add(f"Слоумо: глобальный параметр `{M.TIME_SCALE_PARAM['name']}` "
        f"({M.TIME_SCALE_PARAM['minimum']}…{M.TIME_SCALE_PARAM['maximum']}) крутит питч "
        f"`bus:/{M.TIME_SCALE_PARAM['bus']}` по кривой из {len(M.TIME_SCALE_PARAM['curve'])} точек. "
        f"Пишет его только `TimeScaleService`.")
    add("")

    add("## Дефолты действий")
    add("")
    add("Играют, когда точной записи под контент нет. Без них новый юнит или эффект был бы немым.")
    add("")
    add("| Действие | Событие | Категория | Сэмплов |")
    add("|---|---|---|---|")
    for e in defaults:
        add(f"| `{e['action']}` | `{e['path']}` | {e['category']} | {len(e['files'])} |")
    add("")

    add("## Точечные ключи")
    add("")
    domain_titles = {
        "combat": "Бой", "battle": "Исход и старт боя", "feel": "Feel-слой", "relic": "Реликвии (герои)",
        "effect": "Эффекты и статусы", "enemy": "Враги", "ui": "Интерфейс", "map": "Карта акта",
        "flow": "Переходы", "reward": "Награда", "shop": "Лавка", "camp": "Привал", "chest": "Сундук",
        "event": "Текстовые события", "run": "Забег", "menu": "Меню", "music": "Музыка", "ambient": "Амбиент",
    }
    for domain in sorted(grouped, key=lambda d: list(domain_titles).index(d) if d in domain_titles else 99):
        entries = sorted(grouped[domain], key=lambda e: e["key"])
        add(f"### {domain_titles.get(domain, domain)} (`{domain}.*`)")
        add("")
        add(f"Кто дёргает: {TRIGGERS.get(domain, '—')}.")
        add("")
        add("| Ключ | Событие FMOD | Категория | Сэмплов |")
        add("|---|---|---|---|")
        for e in entries:
            add(f"| `{e['key']}` | `{e['path']}` | {e['category']} | {len(e['files'])} |")
        add("")

    add("## Музыка и амбиент")
    add("")
    add("Лупы играются хранимым `EventInstance` (`FmodAudioService`), одна дорожка за раз;")
    add("приоритет — меню важнее фазы боя, бой важнее карты.")
    add("")
    add("| Ключ | Трек | Лицензия | Источник |")
    add("|---|---|---|---|")
    for key, title, lic, src in MUSIC_SOURCES:
        if key in by_key:
            add(f"| `{key}` | {title} | {lic} | {src} |")
    add("")
    add(f"Громкость: музыка нормализована к {M.MUSIC_LUFS} LUFS, амбиент — к {M.AMBIENT_LUFS} LUFS")
    add("(интегральный EBU R128, в отличие от one-shot).")
    add("")

    add("## Откуда взяты сэмплы")
    add("")
    add("| Пак | Лицензия | Обязательства | Файлов задействовано |")
    add("|---|---|---|---|")
    used = defaultdict(int)
    for e in M.ALL:
        for f in e["files"]:
            if f.startswith("music/"):
                continue
            used[source_of(f)[0]] += 1
    for prefix, (title, lic, duty) in SOURCES.items():
        add(f"| {title} | {lic} | {duty} | {used.get(title, 0)} |")
    add("")
    add("Sonniss GDC-бандл в игре **не используется**: его файлы длинные и дизайнерские, их надо")
    add("резать на слух, а CC0-паки закрыли все дыры. Лежит как резерв.")
    add("")

    add("## Пайплайн")
    add("")
    add("```")
    add("scripts/audio/audio_map.py            ключ → категория → сэмплы (ЕДИНСТВЕННЫЙ источник правды)")
    add("  → build_source_audio.py             нормализация → FMOD Project/SourceAudio + manifest.json")
    add("  → build_populate.py                 → FMOD Project/Tooling/populate.js")
    add("  → fmodstudiocl -script populate.js  шины, события, микс, параметр TimeScale")
    add("  → fmodstudiocl -build -export-guids банки → Assets/StreamingAssets")
    add("  → меню Alebardium/Audio/Populate Catalog from Manifest   → AudioCatalog.asset")
    add("  → gen_audio_reference.py            → этот документ")
    add("```")
    add("")
    add("Проверки: `audit_samples.py` (технический брак), `clap_pick.py --verify` (соответствие")
    add("сэмпла его смыслу), EditMode-тесты `AudioCoverageTests` (код ↔ каталог ↔ манифест).")
    add("")
    add("## Связи")
    add("")
    add("- [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]] — как всё устроено и почему")
    add("- [[tech/10-reference/asset-inventory|Reference - Asset Inventory]] — паки в репозитории")
    add("- [[gdd/10-vision/audio-subbuses|Vision - Audio Sub-buses]] — решение о под-шинах")
    add("- [[gdd/10-vision/backlog-audio-sfx|Vision - Audio & SFX Backlog]] — техники на будущее")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")
    print(f"{os.path.relpath(OUT, REPO)}: {len(events)} событий, {files_total} сэмплов")
    return 0


if __name__ == "__main__":
    sys.exit(main())
