"""Собирает инвентарь grayscale-текстур UI в вики-справочник.

Зачем скрипт, а не рукописная таблица: список файлов отстаёт от диска за неделю — ровно так
было с картой журналов, пока её не начали генерировать. Тот же приём, что у
`scripts/audio/gen_audio_reference.py`.

Запуск:  py scripts/gen_texture_inventory.py
Выход:   docs/wiki/tech/10-reference/ui-texture-inventory.md  (руками не править)
"""

from __future__ import annotations

import datetime as _dt
from pathlib import Path

from PIL import Image, ImageChops, ImageStat

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Assets/_Project/Art/Textures (Grayscale)"
WORK = ROOT / "Assets/_Project/Art/UI Textures"
OUT = ROOT / "docs/wiki/tech/10-reference/ui-texture-inventory.md"

# Роль папки -> зачем эта пачка нужна. Держится здесь, потому что из имён файлов не выводится.
FOLDER_ROLE = {
    "Backdrops": "задник экранов меты — кадр целиком, растягивается по экрану",
    "Banners": "вертикальное полотнище под колонку выбора (приём Slay the Spire)",
    "Frames": "рамка панели, орнамент в углах — годится под 9-slice",
    "Plates": "лента-пластина с орнаментальными концами — 9-slice по горизонтали",
    "Masks": "маска затемнения и свечения, играет яркостью, а не рисунком",
}


def channel_spread(im: Image.Image) -> tuple[int, float]:
    """Насколько картинка НЕ серая: максимальный и средний разброс каналов.

    Под тонирование через `-unity-background-image-tint-color` разброс должен быть нулевым,
    иначе тинт красит уже подкрашенное и даёт грязь.
    """
    r, g, b = im.convert("RGB").resize((256, 256)).split()
    hi = ImageChops.lighter(ImageChops.lighter(r, g), b)
    lo = ImageChops.darker(ImageChops.darker(r, g), b)
    d = ImageChops.difference(hi, lo)
    return d.getextrema()[1], ImageStat.Stat(d).mean[0]


def polarity(im: Image.Image) -> str:
    """На чём лежит рисунок: на светлом поле или на тёмном. Решает, как вырезать альфу."""
    g = im.convert("L")
    w, h = g.size
    edge = [g.getpixel((x, 2)) for x in range(0, w, max(1, w // 64))]
    edge += [g.getpixel((2, y)) for y in range(0, h, max(1, h // 64))]
    avg = sum(edge) / len(edge)
    if avg > 200:
        return "на белом"
    if avg < 55:
        return "на чёрном"
    return "без поля"


def main() -> None:
    if not SRC.is_dir():
        raise SystemExit(f"нет папки {SRC}")

    folders = sorted(p for p in SRC.iterdir() if p.is_dir())
    total = sum(len(list(f.glob("*.png"))) for f in folders)
    weight = sum(p.stat().st_size for f in folders for p in f.glob("*.png"))

    lines: list[str] = []
    lines.append("---")
    lines.append('title: "Reference - UI Texture Inventory"')
    lines.append("order: 57")
    lines.append("status: ready")
    lines.append(f"updated: {_dt.date.today().isoformat()}")
    lines.append("---")
    lines.append("")
    lines.append("**Статус:** ready — СГЕНЕРИРОВАН `scripts/gen_texture_inventory.py` с диска.")
    lines.append("Руками не править: правка уедет при следующем прогоне. Владелец правды — сами файлы")
    lines.append(f"в `{SRC.relative_to(ROOT).as_posix()}`.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("Что за grayscale-текстуры лежат в проекте, какого они размера и в каком состоянии.")
    lines.append("Серые они затем, чтобы краситься токеном палитры в рантайме, а не плодить по файлу")
    lines.append("на каждый цвет. Почему так и чем платим — запись журнала")
    lines.append("[[tech/00-meta/journal/2026-08-23-grayscale-textures-get-names-that-say-the-motif|Journal - Grayscale Textures Get Names That Say The Motif]].")
    lines.append("")
    lines.append("## Сводка")
    lines.append("")
    lines.append("| | |")
    lines.append("|---|---|")
    lines.append(f"| Текстур | {total} |")
    lines.append(f"| Пачек | {len(folders)} |")
    lines.append(f"| Вес на диске | {weight / 1024 / 1024:.1f} МБ |")
    lines.append("")

    work_files = sorted(WORK.rglob("*.png")) if WORK.is_dir() else []
    lines.append(f"| Прогнано конвейером | {len(work_files)} из {total} |")
    lines.append("")
    lines.append("## Исходники")
    lines.append("")
    lines.append("Как пришли от генератора. В UI напрямую не годятся — колонки ниже показывают, чем.")
    lines.append("Обработку делает `scripts/prep_ui_textures.py`, результат — раздел «Рабочая пачка».")
    lines.append("")

    for folder in folders:
        files = sorted(folder.glob("*.png"))
        if not files:
            continue
        lines.append(f"### {folder.name}")
        lines.append("")
        role = FOLDER_ROLE.get(folder.name)
        if role:
            lines.append(f"Роль: {role}.")
            lines.append("")
        lines.append("| Файл | Размер | Альфа | Поле | Разброс каналов | Вес |")
        lines.append("|---|---|---|---|---|---|")
        for f in files:
            im = Image.open(f)
            mx, mean = channel_spread(im)
            alpha = "есть" if im.mode in ("RGBA", "LA") else "**нет**"
            lines.append(
                f"| `{f.stem}` | {im.size[0]}x{im.size[1]} | {alpha} | {polarity(im)} "
                f"| max {mx}, сред. {mean:.1f} | {f.stat().st_size // 1024} КБ |"
            )
        lines.append("")

    if work_files:
        lines.append("## Рабочая пачка")
        lines.append("")
        lines.append(f"`{WORK.relative_to(ROOT).as_posix()}` — то, что кладётся в UI: поле вырезано в")
        lines.append("альфу, картинка обесцвечена, размер приведён к кратному четырём. Пересобрать —")
        lines.append("`py scripts/prep_ui_textures.py --force`; редактировать эти файлы руками бессмысленно.")
        lines.append("")
        lines.append("| Файл | Размер | Альфа | Вес |")
        lines.append("|---|---|---|---|")
        for f in work_files:
            im = Image.open(f)
            alpha = "есть" if im.mode in ("RGBA", "LA") else "нет (кадр целиком)"
            lines.append(
                f"| `{f.parent.name}/{f.stem}` | {im.size[0]}x{im.size[1]} | {alpha} "
                f"| {f.stat().st_size // 1024} КБ |"
            )
        lines.append("")

    lines.append("## Как читать колонки")
    lines.append("")
    lines.append("- **Альфа «нет»** — прозрачности у файла не существует. Шахматка на картинке")
    lines.append("  нарисована пикселями: так генератор изображает прозрачный фон, которого не отдаёт.")
    lines.append("  Такую текстуру нельзя класть поверх фона, пока белое поле не вырезано в альфу.")
    lines.append("- **Поле** — на чём лежит рисунок. Решает, что вырезать: белое или чёрное.")
    lines.append("- **Разброс каналов** — насколько картинка отличается от честно серой. Всё, что выше")
    lines.append("  нуля, при тонировании даёт паразитный оттенок.")
    lines.append("- **Размер не кратен 4** — Unity не сожмёт такую текстуру в BC/DXT и положит её")
    lines.append("  в RGBA32. Приводить к кратности до того, как текстура попадёт в экран.")
    lines.append("")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"{OUT.relative_to(ROOT).as_posix()}: {total} текстур в {len(folders)} пачках")


if __name__ == "__main__":
    main()
