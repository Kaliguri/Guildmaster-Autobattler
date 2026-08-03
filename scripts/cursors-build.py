# -*- coding: utf-8 -*-
"""Сборка скинов курсора из векторных исходников Kenney в единый нормализованный набор.

Зачем скрипт, а не «отмасштабировал руками»:

* исходники разъезжаются почти вдвое по величине фигуры при одинаковом холсте 32x32
  (pointer_a — 16x20, pointer_toon_a — 29x28), и на экране это читается как «один курсор жирный,
  другой тощий»;
* растровый апскейл x10 даёт лесенку на диагоналях, а курсоры Kenney векторные и гладкие —
  поэтому берём .svg и рендерим сразу в целевое разрешение;
* набор будет пополняться, и «нормальность» нового скина должна проверяться замером, а не глазом.

Правило нормализации: **у всех скинов совпадает диагональ фигуры**, а не размер холста. Холст —
общий и с запасом, чтобы стрелка любой формы влезла целиком.

Запуск: python scripts/cursors-build.py [--check]
    --check — ничего не пишет, только печатает замеры готовых файлов (гейт «размеры нормальные»).
"""

import argparse
import os
import sys

from PIL import Image
from reportlab.graphics import renderPM
from svglib.svglib import svg2rlg

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "Assets", "Kenney", "kenney_cursor-pack", "Vector", "Outline")
DST = os.path.join(REPO, "Assets", "_Project", "Art", "UI", "Cursors")

# Холст скина в пикселях. Курсор рисуется в UI размером порядка 40 логических пикселей на канве
# 1920x1080, так что 320 — восьмикратный запас: хватает и на 4K, и на будущий крупный скин.
CANVAS = 320

# Диагональ фигуры внутри холста. 0.78 от холста оставляет поле, в которое влезает диагональная
# стрелка целиком, не упираясь в край.
TARGET_DIAGONAL = CANVAS * 0.78

# Промежуточный рендер: чем крупнее, тем точнее замер фигуры и мягче край после сжатия.
RENDER = 1024

# Скины набора. Имя файла = имя скина; хотспот («остриё») задаётся вручную долями холста, потому что
# у стрелок разное направление и вывести его из картинки нельзя: у смотрящей вправо остриё справа.
SKINS = [
    ("pointer_a",       "classic", (0.0, 0.0)),
    ("pointer_c",       "wedge",   (0.0, 0.0)),
    ("pointer_toon_a",  "toon",    (0.0, 0.0)),
    ("pointer_toon_b",  "chunky",  (0.0, 0.0)),
    ("pointer_scifi_a", "shard",   (0.0, 0.0)),
]


def render_rgba(svg_path, size):
    """Отрендерить SVG в RGBA. renderPM не умеет прозрачный фон, поэтому альфу восстанавливаем
    из двух прогонов: на чёрном и на белом. Для пикселя цвета C с альфой a рендер на белом даёт
    C*a + 255*(1-a), на чёрном — C*a; разница целиком приходится на фон, отсюда и альфа."""
    def draw(bg):
        drawing = svg2rlg(svg_path)
        scale = size / max(drawing.width, drawing.height)
        drawing.scale(scale, scale)
        drawing.width *= scale
        drawing.height *= scale
        tmp = os.path.join(DST, f".__tmp_{bg:06x}.png")
        renderPM.drawToFile(drawing, tmp, fmt="PNG", bg=bg)
        image = Image.open(tmp).convert("RGB")
        data = list(image.getdata())
        os.remove(tmp)
        return image.size, data

    (w, h), on_black = draw(0x000000)
    _, on_white = draw(0xFFFFFF)

    out = Image.new("RGBA", (w, h))
    pixels = []
    for black, white in zip(on_black, on_white):
        alpha = 255 - (white[0] - black[0])
        alpha = max(0, min(255, alpha))
        if alpha == 0:
            pixels.append((0, 0, 0, 0))
            continue
        colour = tuple(min(255, int(c * 255 / alpha)) for c in black)
        pixels.append(colour + (alpha,))
    out.putdata(pixels)
    return out


def normalize(image):
    """Вписать фигуру в общий холст так, чтобы её диагональ совпала с целевой."""
    box = image.getbbox()
    if box is None:
        raise ValueError("пустая картинка: рендер не дал ни одного непрозрачного пикселя")

    figure = image.crop(box)
    width, height = figure.size
    diagonal = (width * width + height * height) ** 0.5
    factor = TARGET_DIAGONAL / diagonal

    figure = figure.resize((max(1, round(width * factor)), max(1, round(height * factor))),
                           Image.LANCZOS)

    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.alpha_composite(figure, (0, 0))  # остриё в левом верхнем углу холста
    return canvas


def measure(path):
    image = Image.open(path).convert("RGBA")
    box = image.getbbox()
    width, height = box[2] - box[0], box[3] - box[1]
    return image.size, (width, height), (width * width + height * height) ** 0.5


def build():
    os.makedirs(DST, exist_ok=True)
    for source, name, _ in SKINS:
        svg = os.path.join(SRC, f"{source}.svg")
        if not os.path.exists(svg):
            print(f"ПРОПУСК {name}: нет исходника {svg}")
            continue

        canvas = normalize(render_rgba(svg, RENDER))
        out = os.path.join(DST, f"cursor_{name}.png")
        canvas.save(out)
        print(f"собран {name:<9} <- {source}")


def check():
    """Печать замеров: холст, фигура, диагональ и расхождение с целевой."""
    worst = 0.0
    print(f"{'скин':<10}{'холст':<12}{'фигура':<12}{'диагональ':<12}{'откл.'}")
    for _, name, _ in SKINS:
        path = os.path.join(DST, f"cursor_{name}.png")
        if not os.path.exists(path):
            print(f"{name:<10}НЕТ ФАЙЛА")
            worst = 999
            continue
        canvas, figure, diagonal = measure(path)
        drift = abs(diagonal - TARGET_DIAGONAL) / TARGET_DIAGONAL * 100
        worst = max(worst, drift)
        print(f"{name:<10}{str(canvas):<12}{f'{figure[0]}x{figure[1]}':<12}{diagonal:<12.1f}{drift:.2f}%")

    print(f"\nцель диагонали {TARGET_DIAGONAL:.0f} px, худшее расхождение {worst:.2f}%")
    return 0 if worst <= 1.0 else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="только замер, без сборки")
    args = parser.parse_args()

    if not args.check:
        build()
    sys.exit(check())
