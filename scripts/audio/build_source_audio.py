# -*- coding: utf-8 -*-
"""
Сборка нормализованного банка исходников для FMOD + манифест заливки.

Читает карту звука (audio_map.py), проверяет, что все исходники на месте, нормализует их
в `FMOD Project/SourceAudio/{category}/{event}_{NN}.wav` и пишет `FMOD Project/Scripts/manifest.json`.

Нормализация (spec: docs/wiki/tech/40-planning/sfx-round-2.md §3):
    gain = min(TARGET_RMS_DB - rms_active, TRUE_PEAK_DB - peak)
где rms_active — RMS сигнала выше -45 dB (для one-shot интегральный LUFS не считается).
Громкость никогда не покупается клиппингом: пиковый потолок жёстче цели.

Запуск:
    python scripts/audio/build_source_audio.py            # проверка + нормализация + манифест
    python scripts/audio/build_source_audio.py --check     # только проверка наличия исходников
"""
import json
import os
import re
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audio_map as M  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SOURCE_AUDIO = os.path.join(REPO, "FMOD Project", "SourceAudio")
MANIFEST = os.path.join(REPO, "FMOD Project", "Scripts", "manifest.json")
MUSIC_SRC = os.path.join(REPO, "FMOD Project", "MusicSource")  # сюда кладутся сырые лупы музыки

SILENCE_DB = -45


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="ignore")


def measure(path):
    """Возвращает (rms_active_db, peak_db) — RMS активной части и пик."""
    r = run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af",
             f"silenceremove=start_periods=1:start_threshold={SILENCE_DB}dB:"
             f"stop_periods=-1:stop_threshold={SILENCE_DB}dB:stop_duration=0.05,volumedetect",
             "-f", "null", "-"])
    log = r.stderr or ""
    mean = re.search(r"mean_volume: (-?[\d.]+)", log)
    peak = re.search(r"max_volume: (-?[\d.]+)", log)
    if not mean or not peak:
        return None, None
    return float(mean.group(1)), float(peak.group(1))


def normalize(src, dst, extra_gain_db=0.0):
    rms, peak = measure(src)
    if rms is None:
        print(f"  !! не измерить: {src}")
        return None
    gain = min(M.TARGET_RMS_DB - rms, M.TRUE_PEAK_DB - peak) + extra_gain_db
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    r = run(["ffmpeg", "-hide_banner", "-nostats", "-y", "-i", src,
             "-af", f"volume={gain:.2f}dB", "-ar", "44100", "-c:a", "pcm_s16le", dst])
    if r.returncode != 0:
        print(f"  !! ffmpeg упал на {src}: {r.stderr[-200:]}")
        return None
    return dict(src=os.path.relpath(src, REPO).replace("\\", "/"), gain=round(gain, 2),
                rms=round(rms, 1), peak=round(peak, 1))


def normalize_music(src, dst, target_lufs):
    """Музыка/амбиент: loudnorm по интегральному LUFS, выход ogg (длинные файлы, wav не нужен)."""
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    r = run(["ffmpeg", "-hide_banner", "-nostats", "-y", "-i", src,
             "-af", f"loudnorm=I={target_lufs}:TP=-1.5:LRA=11",
             "-ar", "44100", "-c:a", "libvorbis", "-q:a", "5", dst])
    if r.returncode != 0:
        print(f"  !! ffmpeg (music) упал на {src}: {r.stderr[-200:]}")
        return None
    return dict(src=os.path.relpath(src, REPO).replace("\\", "/"), gain="loudnorm",
                rms=target_lufs, peak=-1.5)


def event_name(entry):
    """Имя файла-семейства в SourceAudio: из пути события, без папок."""
    path = entry["path"] or ("event:/SFX/" + entry["key"])
    return path.rsplit("/", 1)[-1]


def main():
    check_only = "--check" in sys.argv
    missing, ok = [], 0
    music_missing = []

    for e in M.ALL:
        for f in e["files"]:
            if f.startswith("music/"):
                p = os.path.join(MUSIC_SRC, os.path.basename(f))
                if not os.path.isfile(p):
                    music_missing.append(f)
                continue
            p = os.path.join(REPO, f)
            if os.path.isfile(p):
                ok += 1
            else:
                missing.append(f"{e['key']}: {f}")

    print(f"исходников найдено: {ok}, потеряно: {len(missing)}")
    for m in missing:
        print("  ОТСУТСТВУЕТ", m)
    if music_missing:
        print(f"музыка ещё не положена ({len(music_missing)}): {', '.join(music_missing)}")
        print(f"  -> положить в {os.path.relpath(MUSIC_SRC, REPO)}")
    if missing:
        print("\nОстановка: почини карту (audio_map.py) — событий с битым материалом быть не должно.")
        return 1
    if check_only:
        return 0

    if os.path.isdir(SOURCE_AUDIO):
        shutil.rmtree(SOURCE_AUDIO)

    manifest_events, report = [], []
    for e in M.ALL:
        cat = M.CATEGORIES[e["category"]]
        name = event_name(e)
        files_out = []
        for i, f in enumerate(e["files"], start=1):
            if f.startswith("music/"):
                src = os.path.join(MUSIC_SRC, os.path.basename(f))
                if not os.path.isfile(src):
                    continue
            else:
                src = os.path.join(REPO, f)
            is_loop = cat.get("looping", False)
            ext = "ogg" if is_loop else "wav"
            dst_rel = f"{e['category']}/{name}_{i:02d}.{ext}"
            dst = os.path.join(SOURCE_AUDIO, dst_rel)
            if is_loop:
                target = M.MUSIC_LUFS if e["category"] == "music" else M.AMBIENT_LUFS
                info = normalize_music(src, dst, target)
            else:
                info = normalize(src, dst)
            if info is None:
                continue
            info["out"] = dst_rel
            report.append(info)
            files_out.append(dst_rel)
        if not files_out:
            print(f"  -- пропущено (нет файлов): {e['key']}")
            continue
        manifest_events.append(dict(
            key=e["key"], action=e["action"], isDefault=e["isDefault"],
            path=e["path"], category=e["category"], files=files_out,
        ))

    manifest = dict(
        sourceRoot=os.path.relpath(SOURCE_AUDIO, REPO).replace("\\", "/"),
        bank="SFX", musicBank="Music",
        targetRmsDb=M.TARGET_RMS_DB, truePeakDb=M.TRUE_PEAK_DB,
        buses=M.BUS_TREE, categories=M.CATEGORIES,
        timeScaleParam=M.TIME_SCALE_PARAM, events=manifest_events,
    )
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    with open(MANIFEST, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)

    gains = [r["gain"] for r in report if isinstance(r["gain"], float)]
    print(f"\nнормализовано файлов: {len(report)}; событий в манифесте: {len(manifest_events)}")
    if gains:
        print(f"гейн: {min(gains):+.1f} .. {max(gains):+.1f} dB")
    print(f"манифест: {os.path.relpath(MANIFEST, REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
