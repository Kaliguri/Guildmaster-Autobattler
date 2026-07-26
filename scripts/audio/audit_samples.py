# -*- coding: utf-8 -*-
"""
Технический аудит сэмплов: ловит брак, который на слух замечаешь не сразу, а в бою он бесит.

Проверяет то, что можно проверить числами, БЕЗ ушей:
  clip        — пик выше потолка (после нормализации быть не должно вовсе)
  dc          — постоянная составляющая: щелчок на старте и потеря запаса громкости
  late        — тишина в начале: звук «опаздывает» за удар/клик на десятки миллисекунд
  cut         — обрубленный хвост: файл кончается на громком месте → щелчок на конце (мерим 5 мс)
  long/short  — длительность вне нормы категории (UI-клик на 3 секунды — почти всегда ошибка)
  quiet       — сэмпл заметно тише цели (нормализация упёрлась в пик)
  format      — не 44.1 кГц

Что НЕ проверяет: красоту. «Сочный удар или дохлый» числами не берётся — это к Максу.

Запуск:
    python scripts/audio/audit_samples.py              # аудит SourceAudio (то, что уехало в банк)
    python scripts/audio/audit_samples.py --raw        # аудит исходных паков (до нормализации)
"""
import os
import re
import subprocess
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audio_map as M  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SOURCE_AUDIO = os.path.join(REPO, "FMOD Project", "SourceAudio")

# Пороги. Подобраны под наш материал: one-shot 0.1-6 с, потолок -1 dBTP, цель -23 dB RMS.
PEAK_CEILING_DB   = -0.3    # выше — реальный риск клиппинга после кодирования банка
DC_OFFSET_MAX     = 0.003   # ~-50 dB постоянной составляющей
LATE_START_MS     = 30      # больше — звук ощутимо запаздывает за событием
TAIL_LEVEL_DB     = -25.0   # громче на последних 10 мс — обрыв слышен щелчком
QUIET_TOLERANCE   = 4.0     # насколько сэмпл может быть тише цели, прежде чем это заметно

# Разумная длительность по категориям, сек (нижняя, верхняя). Лупы не проверяются.
DURATION_LIMITS = {
    "impact": (0.05, 3.0), "whoosh": (0.05, 4.0), "tonal": (0.05, 7.0), "cast": (0.1, 8.0),
    "death": (0.1, 7.0), "stinger": (0.2, 10.0), "ui": (0.02, 2.5), "ui_soft": (0.02, 2.5),
}


def run(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="ignore")


def probe(path):
    """Числа по файлу: длительность, частота, пик, RMS, DC, тишина в начале, уровень хвоста."""
    r = run(["ffprobe", "-v", "error", "-show_entries", "format=duration:stream=sample_rate,channels",
             "-of", "default=nw=1:nk=1", path])
    lines = [l.strip() for l in (r.stdout or "").splitlines() if l.strip()]
    sample_rate = int(lines[0]) if lines else 0
    channels = int(lines[1]) if len(lines) > 1 else 0
    duration = float(lines[2]) if len(lines) > 2 else 0.0

    stats = run(["ffmpeg", "-hide_banner", "-nostats", "-i", path,
                 "-af", "astats=metadata=1:reset=0", "-f", "null", "-"]).stderr or ""

    def grab(label, default=0.0):
        m = re.search(rf"{label}: (-?[\d.]+|inf|nan)", stats)
        if not m or m.group(1) in ("inf", "nan"):
            return default
        return float(m.group(1))

    peak = grab("Peak level dB", -99.0)
    rms = grab("RMS level dB", -99.0)
    dc = abs(grab("DC offset"))

    # Тишина в начале: сколько ffmpeg срезает по порогу -45 dB.
    trimmed = run(["ffmpeg", "-hide_banner", "-nostats", "-i", path,
                   "-af", "silenceremove=start_periods=1:start_threshold=-45dB,astats=metadata=1:reset=0",
                   "-f", "null", "-"]).stderr or ""
    m = re.search(r"Number of samples: (\d+)", trimmed)
    trimmed_samples = int(m.group(1)) if m else 0
    lead_ms = 0.0
    if sample_rate > 0 and duration > 0 and trimmed_samples > 0:
        lead_ms = max(0.0, (duration - trimmed_samples / sample_rate) * 1000.0)

    # Уровень последних 10 мс: обрыв на громком месте = щелчок.
    tail_start = max(0.0, duration - 0.005)
    tail = run(["ffmpeg", "-hide_banner", "-nostats", "-ss", f"{tail_start:.3f}", "-i", path,
                "-af", "astats=metadata=1:reset=0", "-f", "null", "-"]).stderr or ""
    mt = re.search(r"RMS level dB: (-?[\d.]+)", tail)
    tail_db = float(mt.group(1)) if mt and mt.group(1) not in ("inf", "nan") else -99.0

    return dict(duration=duration, sample_rate=sample_rate, channels=channels,
                peak=peak, rms=rms, dc=dc, lead_ms=lead_ms, tail=tail_db)


def audit_file(path, category):
    p = probe(path)
    issues = []

    if p["peak"] > PEAK_CEILING_DB:
        issues.append(f"clip: пик {p['peak']:+.1f} dB")
    if p["dc"] > DC_OFFSET_MAX:
        issues.append(f"dc: смещение {p['dc']:.4f}")
    if p["lead_ms"] > LATE_START_MS:
        issues.append(f"late: тишина в начале {p['lead_ms']:.0f} мс")
    if p["tail"] > TAIL_LEVEL_DB and p["duration"] > 0.05:
        issues.append(f"cut: хвост обрывается на {p['tail']:.1f} dB")
    if p["sample_rate"] and p["sample_rate"] != 44100:
        issues.append(f"format: {p['sample_rate']} Гц")

    limits = DURATION_LIMITS.get(category)
    if limits:
        lo, hi = limits
        if p["duration"] < lo:
            issues.append(f"short: {p['duration']:.2f} с (норма {lo}-{hi})")
        elif p["duration"] > hi:
            issues.append(f"long: {p['duration']:.2f} с (норма {lo}-{hi})")

    is_loop = M.CATEGORIES.get(category, {}).get("looping", False)
    if not is_loop and p["rms"] < M.TARGET_RMS_DB - QUIET_TOLERANCE:
        issues.append(f"quiet: RMS {p['rms']:.1f} dB (цель {M.TARGET_RMS_DB})")

    return p, issues


def main():
    raw = "--raw" in sys.argv
    targets = []   # (метка, путь, категория)

    if raw:
        for e in M.ALL:
            for f in e["files"]:
                if f.startswith("music/"):
                    continue
                targets.append((f"{e['key']} ← {os.path.basename(f)}", os.path.join(REPO, f), e["category"]))
    else:
        if not os.path.isdir(SOURCE_AUDIO):
            print("Нет FMOD Project/SourceAudio — сначала прогони build_source_audio.py")
            return 1
        for category in sorted(os.listdir(SOURCE_AUDIO)):
            folder = os.path.join(SOURCE_AUDIO, category)
            if not os.path.isdir(folder):
                continue
            for name in sorted(os.listdir(folder)):
                targets.append((f"{category}/{name}", os.path.join(folder, name), category))

    print(f"аудит: {len(targets)} файлов ({'исходники' if raw else 'нормализованные'})\n")

    flagged, by_kind = 0, defaultdict(int)
    for label, path, category in targets:
        if not os.path.isfile(path):
            print(f"  ОТСУТСТВУЕТ {label}")
            continue
        _, issues = audit_file(path, category)
        if not issues:
            continue
        flagged += 1
        for i in issues:
            by_kind[i.split(":")[0]] += 1
        print(f"  {label}\n      " + "\n      ".join(issues))

    print(f"\nс замечаниями: {flagged} из {len(targets)}")
    if by_kind:
        print("по типам: " + ", ".join(f"{k}={v}" for k, v in sorted(by_kind.items())))
    print("\nПорог замечания ≠ приговор: короткий UI-клик с обрывом хвоста может быть нормой,")
    print("а вот 'late' и 'clip' стоит чинить всегда — их слышно.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
