"""Готовит grayscale-исходники UI к тому, чтобы их можно было положить в интерфейс.

Исходники приходят от генератора изображений и в UI не годятся втроём:
прозрачности у них нет (шахматка НАРИСОВАНА пикселями), они не совсем серые
(разброс каналов до 19 — при тонировании даст паразитный оттенок), и размеры
у них произвольные, не кратные 4, из-за чего Unity не сожмёт их в BC/DXT.

Скрипт читает `Art/Textures (Grayscale)` и пишет рядом `Art/UI Textures` —
исходники остаются нетронутыми, чтобы обработку можно было перегнать заново
с другими порогами.

Запуск:  py scripts/prep_ui_textures.py [--force]
"""

from __future__ import annotations

import argparse
import sys
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Assets/_Project/Art/Textures (Grayscale)"
DST = ROOT / "Assets/_Project/Art/UI Textures"

# Длинная сторона после даунсемпла. Одно число на весь конвейер вместо таблицы размеров:
# исходники избыточны раза в три (кнопка в 1080p занимает ~400 px при исходнике 1983).
LONG_SIDE = 1024

# Задник растягивается на весь кадр, поэтому его не ужимаем — только приводим к кратности.
KEEP_SIZE = {"Backdrops"}

# Пачки, которым вырез альфы не нужен: рисунок занимает кадр целиком, поля вокруг него нет.
# Баннеры сюда попали по замеру, а не по виду: тон угла 41-66 против тона тела 41-83, то есть
# тёмный ободок у них — это виньетка ткани, а не фон. Заливка от края съедала полотнище целиком
# (оставалось 2-3% кадра), потому что искала границу там, где её нет.
NO_CUTOUT = KEEP_SIZE | {"Banners"}

# Маски играют яркостью, а не силуэтом: у них светлота уезжает В АЛЬФУ целиком,
# а RGB становится белым — тогда маску красит токен палитры, а не запечённый серый.
AS_MASK = {"Masks"}

# Допуск заливки фона: насколько пиксель может отличаться от углового и всё ещё считаться полем.
FLOOD_TOLERANCE = 26


def to_multiple_of_four(value: int) -> int:
    return max(4, value - value % 4)


def target_size(im: Image.Image, folder: str) -> tuple[int, int]:
    w, h = im.size
    if folder not in KEEP_SIZE:
        scale = LONG_SIDE / max(w, h)
        if scale < 1.0:
            w, h = round(w * scale), round(h * scale)
    return to_multiple_of_four(w), to_multiple_of_four(h)


def background_mask(gray: Image.Image) -> Image.Image:
    """Заливка поля от краёв кадра. Возвращает маску: 255 там, где фон.

    Заливка, а не порог по яркости: на рельефе есть блики светлее 200, и порог сделал бы их
    полупрозрачными — дыры посреди фигуры. Заливка же идёт снаружи и внутрь фигуры не попадает.
    """
    w, h = gray.size
    px = gray.load()
    seen = bytearray(w * h)
    queue: deque[tuple[int, int]] = deque()

    # Стартуем со всей рамки кадра, а не с одного угла: у части исходников фигура
    # доходит до края, и один угол залил бы не всё поле.
    seeds = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)]
    seeds += [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    ref = px[0, 0]
    for x, y in seeds:
        if not seen[y * w + x] and abs(px[x, y] - ref) <= FLOOD_TOLERANCE:
            seen[y * w + x] = 1
            queue.append((x, y))

    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx]:
                if abs(px[nx, ny] - ref) <= FLOOD_TOLERANCE:
                    seen[ny * w + nx] = 1
                    queue.append((nx, ny))

    return Image.frombytes("L", (w, h), bytes(255 if v else 0 for v in seen))


def despeckle_checker(alpha: Image.Image) -> Image.Image:
    """Гасит НАРИСОВАННУЮ шахматку, если она в этой альфе есть.

    Генератор рисует «прозрачный фон» шахматным узором пикселями, и у маски он уезжает прямо
    в альфу — на покрашенной маске видно клетки. Размер клетки заранее не известен (у пачки
    23.08 это 21 px после даунсемпла при амплитуде в 21 тон), поэтому период ищется
    автокорреляцией, а не задаётся числом: угаданная вдвое меньшая медиана узор не берёт.
    Гасится СРЕДНИМ по окну ровно в период, а не медианой: у шахматки светлых и тёмных клеток
    поровну, поэтому медиана выбирает одну из них целиком и узор остаётся — проверено, клетки
    пережили и медиану 9, и медиану 21.
    """
    w, h = alpha.size
    px = alpha.load()
    row = [px[x, h // 2] for x in range(w)]

    # Работаем по РАЗНОСТЯМ соседей, а не по самому сигналу: у сырой автокорреляции максимум
    # всегда на наименьшем лаге (маска — гладкий градиент), и период узора она не находит вовсе.
    # Разности снимают этот тренд, и у периодического узора остаётся честный пик на периоде.
    dev = [row[i + 1] - row[i] for i in range(len(row) - 1)]
    energy = sum(v * v for v in dev) / len(dev)
    if energy < 0.5:
        return alpha.filter(ImageFilter.GaussianBlur(1.0))

    best_lag, best_score = 0, 0.0
    for lag in range(6, 48):
        score = sum(dev[i] * dev[i + lag] for i in range(len(dev) - lag))
        score /= (len(dev) - lag) * energy
        if score > best_score:
            best_lag, best_score = lag, score

    # Узором считаем только выраженную периодику: у мягкой маски без шахматки разности
    # некоррелированы и пик не поднимается.
    if best_lag == 0 or best_score < 0.30:
        return alpha.filter(ImageFilter.GaussianBlur(1.0))

    return alpha.filter(ImageFilter.BoxBlur(best_lag / 2)).filter(ImageFilter.GaussianBlur(1.0))


def process(path: Path, folder: str) -> tuple[Image.Image, str]:
    im = Image.open(path).convert("RGB")
    tw, th = target_size(im, folder)
    im = im.resize((tw, th), Image.LANCZOS)

    # Обесцвечивание принудительное: исходники «почти серые», а почти — при тонировании грязь.
    gray = ImageOps.grayscale(im)

    if folder in AS_MASK:
        # Полярность решает, что считать «полным» краем маски: светлое или тёмное.
        edge = [gray.getpixel((x, 1)) for x in range(0, tw, max(1, tw // 64))]
        alpha = gray if sum(edge) / len(edge) < 128 else ImageOps.invert(gray)
        alpha = despeckle_checker(alpha)
        out = Image.merge("RGBA", (
            Image.new("L", (tw, th), 255),
            Image.new("L", (tw, th), 255),
            Image.new("L", (tw, th), 255),
            alpha,
        ))
        return out, "маска: светлота -> альфа"

    if folder in NO_CUTOUT:
        return Image.merge("RGB", (gray, gray, gray)), "кадр целиком: без альфы"

    bg = background_mask(gray)
    # Размытие в полпикселя возвращает краю сглаживание, которое заливка съедает: она
    # решает про каждый пиксель «да или нет», а край исходника был мягким.
    alpha = ImageOps.invert(bg).filter(ImageFilter.GaussianBlur(0.6))
    out = Image.merge("RGBA", (gray, gray, gray, alpha))
    covered = sum(alpha.point(lambda v: 1 if v > 127 else 0).getdata()) / (tw * th)
    return out, f"вырез: фигура занимает {covered * 100:.0f}% кадра"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="перезаписать уже готовые файлы")
    args = ap.parse_args()

    if not SRC.is_dir():
        print(f"нет папки {SRC}", file=sys.stderr)
        return 1

    done = 0
    for folder in sorted(p for p in SRC.iterdir() if p.is_dir()):
        out_dir = DST / folder.name
        for f in sorted(folder.glob("*.png")):
            out_path = out_dir / f.name
            if out_path.exists() and not args.force:
                continue
            out_dir.mkdir(parents=True, exist_ok=True)
            img, note = process(f, folder.name)
            img.save(out_path)
            print(f"{folder.name}/{f.stem}: {img.size[0]}x{img.size[1]} — {note}")
            done += 1

    print(f"\nготово: {done} файлов в {DST.relative_to(ROOT).as_posix()}")
    if done == 0:
        print("(всё уже собрано — перегнать заново: --force)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
