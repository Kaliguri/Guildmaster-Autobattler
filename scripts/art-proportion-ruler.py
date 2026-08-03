#!/usr/bin/env python3
"""Генератор пиксельных линеек пропорций юнита под Aseprite.

Числа НЕ живут в этом файле: они читаются из размерной сетки, владелец
которой один --- раздел "Сетка размеров частей" в docs/view-angle-progress.md.
Скрипт парсит оттуда три таблицы (вертикальный разбор роста, размеры частей,
правила сетки) и падает, если разбор не сошёлся: молча нарисовать линейку по
устаревшим числам --- худшее, что он может сделать с художником.

Выход (docs/art-refs/view-angle/drawing/rulers/):
  ruler-<тир>-tight.png   слой ровно по высоте фигуры
  ruler-<тир>-canvas.png  слой по сборочному канвасу, фигура по центру
  ruler-<тир>-legend.png  та же линейка x6 с подписями --- читать глазами

Запуск: python scripts/art-proportion-ruler.py
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

GRID_DOC = "docs/view-angle-progress.md"
OUT_DIR = "docs/art-refs/view-angle/drawing/rulers"

# Пояс уже плеч на четверть --- правило сетки, сформулированное словами
# ("пояс уже на 1/4"), отдельной строкой с числами оно не задано.
WAIST_FRACTION = 0.75

# Технические цвета: намеренно вне арт-палитры, чтобы линейка не читалась
# как часть рисунка. KEY --- опорные уровни, SEG --- границы сегментов,
# ARM --- рука, AXIS и WIDTH --- вертикали.
COLOR_KEY = (255, 0, 140, 255)
COLOR_SEG = (0, 220, 255, 255)
COLOR_ARM = (255, 210, 0, 255)
COLOR_AXIS = (120, 255, 80, 255)
COLOR_WIDTH = (110, 190, 120, 255)


class GridError(RuntimeError):
    """Разбор документа-владельца не сошёлся --- рисовать нельзя."""


# --- Чтение сетки из документа-владельца ---------------------------------

def _cells(line: str) -> list[str]:
    """Ячейки markdown-строки без обрамления и без жирного выделения."""
    return [c.replace("**", "").strip() for c in line.strip().strip("|").split("|")]


def _tables(text: str) -> list[list[list[str]]]:
    """Все markdown-таблицы документа как списки строк-ячеек."""
    tables, current = [], []
    for line in text.splitlines():
        if line.lstrip().startswith("|"):
            cells = _cells(line)
            # Строка-разделитель заголовка (---, :---) в данные не идёт.
            if not all(set(c) <= set("-: ") for c in cells):
                current.append(cells)
        elif current:
            tables.append(current)
            current = []
    if current:
        tables.append(current)
    return tables


def _find_table(tables: list[list[list[str]]], *header_words: str) -> list[list[str]]:
    for table in tables:
        header = " ".join(table[0]).lower()
        if all(word.lower() in header for word in header_words):
            return table
    raise GridError(f"в {GRID_DOC} нет таблицы с заголовком {header_words}")


def _tier_columns(header: list[str]) -> dict[int, int]:
    """Номера колонок по тиру: заголовки вида '96 крупный'."""
    columns = {}
    for index, cell in enumerate(header):
        match = re.match(r"^(\d+)\b", cell)
        if match:
            columns[int(match.group(1))] = index
    if not columns:
        raise GridError(f"в {GRID_DOC} не опознаны колонки тиров: {header}")
    return columns


def _wh(cell: str) -> tuple[int, int]:
    """'12x16' или '14x58 (46/12)' --> (12, 16). Разделитель --- знак умножения."""
    match = re.match(r"^(\d+)\s*[x×*]\s*(\d+)", cell)
    if not match:
        raise GridError(f"ячейка {cell!r} не читается как ШИРИНАxВЫСОТА")
    return int(match.group(1)), int(match.group(2))


def _px(cell: str) -> int:
    match = re.match(r"^(\d+)", cell)
    if not match:
        raise GridError(f"ячейка {cell!r} не читается как число пикселей")
    return int(match.group(1))


def _row(table: list[list[str]], starts_with: str) -> list[str]:
    for row in table[1:]:
        if row[0].lower().startswith(starts_with.lower()):
            return row
    raise GridError(f"в таблице нет строки, начинающейся с {starts_with!r}")


def read_grid(repo: Path) -> dict[int, dict]:
    """Сетка по тирам из документа-владельца, с проверкой сходимости."""
    text = (repo / GRID_DOC).read_text(encoding="utf-8")
    tables = _tables(text)

    heights = _find_table(tables, "Тир", "Голень", "Сумма")
    parts = _find_table(tables, "Часть")
    rules = _find_table(tables, "Правило")

    # В вертикальном разборе тир --- это первая ячейка строки, а не колонка,
    # поэтому сегменты ищем по именам колонок, а не по их порядку.
    header = [c.lower() for c in heights[0]]
    segments = {}
    for name in ("голова", "шея", "торс", "бедро", "голень", "стопа", "сумма"):
        index = next((i for i, cell in enumerate(header) if cell.startswith(name)), None)
        if index is None:
            raise GridError(f"в вертикальном разборе нет колонки {name!r}")
        segments[name] = index

    grid: dict[int, dict] = {}
    for row in heights[1:]:
        tier = _px(row[0])
        seg = {name: _px(row[index]) for name, index in segments.items()}
        figure = seg.pop("сумма")
        if sum(seg.values()) != figure:
            raise GridError(f"тир {tier}: сегменты {seg} не дают заявленный рост {figure}")
        if tier != figure:
            raise GridError(f"тир {tier} и рост фигуры {figure} разошлись")
        grid[tier] = dict(
            figure=figure,
            foot=seg["стопа"], shin=seg["голень"], thigh=seg["бедро"],
            torso=seg["торс"], neck=seg["шея"], head=seg["голова"],
        )

    parts_col = _tier_columns(parts[0])
    rules_col = _tier_columns(rules[0])
    for tier, t in grid.items():
        if tier not in parts_col or tier not in rules_col:
            raise GridError(f"тир {tier} есть в разборе роста, но не в таблице частей или правил")
        pc, rc = parts_col[tier], rules_col[tier]

        w_head, h_head = _wh(_row(parts, "Голова")[pc])
        w_shoulders, h_torso = _wh(_row(parts, "Торс")[pc])
        upper = _wh(_row(parts, "Плечо")[pc])[1]
        fore = _wh(_row(parts, "Предплечье")[pc])[1]
        hand = _wh(_row(parts, "Кисть")[pc])[1]

        # Таблица частей и вертикальный разбор описывают одно и то же тело ---
        # расхождение значит, что одну из них правили в отрыве от другой.
        # Спрайт головы несёт и шею, поэтому он выше сегмента головы ровно на неё.
        if h_head != t["head"] + t["neck"] or h_torso != t["torso"]:
            raise GridError(
                f"тир {tier}: части говорят голова+шея {h_head} и торс {h_torso}, "
                f"разбор роста — {t['head']}+{t['neck']}={t['head'] + t['neck']} "
                f"и торс {t['torso']}")
        t.update(
            arm=(upper, fore, hand),
            w_head=w_head, w_shoulders=w_shoulders,
            w_thigh=_wh(_row(parts, "Бедро")[pc])[0],
            w_shin=_wh(_row(parts, "Голень")[pc])[0],
            w_foot=_wh(_row(parts, "Стопа")[pc])[0],
            overlap=_px(_row(rules, "Перекрытие")[rc]),
            far_side=_px(_row(rules, "Дальняя сторона")[rc]),
            canvas=_wh(_row(rules, "Сборочный канвас")[rc])[0],
            head_ratio=f"1/{t['figure'] / t['head']:.2g}",
        )
    return grid


# --- Геометрия линейки ---------------------------------------------------

def levels(t: dict) -> list[tuple[int, str, tuple[int, int, int, int], bool]]:
    """Горизонтальные уровни: (высота от подошвы, подпись, цвет, сплошная ли).

    Опорные (сплошные) --- подошва, таз, плечи, макушка: по ним фигура
    строится. Остальные пунктиром, чтобы меньше мешали рисованию.
    """
    ankle = t["foot"]
    knee = ankle + t["shin"]
    hip = knee + t["thigh"]
    shoulders = hip + t["torso"]
    chin = shoulders + t["neck"]
    crown = chin + t["head"]

    upper, fore, hand = t["arm"]
    elbow = shoulders - upper
    wrist = elbow - fore
    fingers = wrist - hand

    return [
        (0, "подошва, земля", COLOR_KEY, True),
        (ankle, "голеностоп, верх стопы", COLOR_SEG, False),
        (knee, "колено", COLOR_SEG, False),
        (fingers, "конец пальцев, около середины бедра", COLOR_ARM, False),
        (wrist, "запястье", COLOR_ARM, False),
        (hip, "таз и пах, середина роста", COLOR_KEY, True),
        (elbow, "локоть", COLOR_ARM, False),
        (shoulders, "линия плеч, основание шеи", COLOR_KEY, True),
        (chin, "подбородок, верх шеи", COLOR_SEG, False),
        (crown, "верх головы", COLOR_KEY, True),
    ]


def verticals(t: dict) -> list[tuple[int, str]]:
    """Вертикали: (полуширина от оси, подпись). 0 --- сама ось симметрии."""
    waist = round(t["w_shoulders"] * WAIST_FRACTION)
    return [
        (0, "ось симметрии"),
        (t["w_head"] // 2, f"голова, ширина {t['w_head']}"),
        (waist // 2, f"пояс, ширина {waist}"),
        (t["w_shoulders"] // 2, f"плечи, ширина {t['w_shoulders']}"),
    ]


def dashed_row(draw: ImageDraw.ImageDraw, y: int, x0: int, x1: int, color) -> None:
    for x in range(x0, x1, 4):
        draw.point((x, y), fill=color)
        draw.point((min(x + 1, x1 - 1), y), fill=color)


def dashed_col(draw: ImageDraw.ImageDraw, x: int, y0: int, y1: int, color,
               step: int = 4, dash: int = 2) -> None:
    for y in range(y0, y1, step):
        for d in range(min(dash, y1 - y)):
            draw.point((x, y + d), fill=color)


def render(t: dict, tight: bool = True, size: int | None = None,
           sole: int | None = None, axis: int | None = None) -> Image.Image:
    """Прозрачный PNG-слой с линейкой.

    tight --- канвас ровно по фигуре, иначе сборочный. size, sole и axis
    перекрывают раскладку под чужой файл: size --- сторона канваса,
    sole --- отступ подошвы от НИЗА канваса, axis ---x оси симметрии
    (в чужом файле фигура редко стоит по центру канвы).
    """
    figure = t["figure"]
    if size is None:
        size = figure if tight else t["canvas"]
    # Запас сборочного канваса делится пополам: снизу под тень и стойку,
    # сверху под замах и древко. Это раскладка слоя, а не канонное число.
    pad = sole if sole is not None else (0 if tight else (size - figure) // 2)

    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    center = size // 2 if axis is None else axis

    for h, _label, color, solid in levels(t):
        y = size - 1 - pad - h
        if not 0 <= y < size:
            continue
        if solid:
            draw.line([(0, y), (size - 1, y)], fill=color)
        else:
            dashed_row(draw, y, 0, size, color)

    # Ось --- частым пунктиром, парные ширины --- редким и глуше: иначе
    # четыре пары вертикалей забивают фигуру и мешают рисовать.
    top = max(size - 1 - pad - figure, 0)
    bottom = min(size - pad, size)
    for half, _label in verticals(t):
        axis = half == 0
        for x in {center - half, center + half}:
            if 0 <= x < size:
                dashed_col(draw, x, top, bottom,
                           COLOR_AXIS if axis else COLOR_WIDTH,
                           step=4 if axis else 10, dash=2)

    return img


def font(px: int) -> ImageFont.ImageFont:
    """Системный шрифт; при его отсутствии --- встроенный битмапный."""
    for name in ("consola.ttf", "arial.ttf", "DejaVuSansMono.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            continue
    return ImageFont.load_default()


def render_legend(t: dict, zoom: int = 6) -> Image.Image:
    """Читаемая версия: линейка крупно плюс подписи уровней и вертикалей."""
    figure = t["figure"]
    big = render(t, tight=True).resize((figure * zoom, figure * zoom), Image.NEAREST)

    footer = (f"Сегменты снизу вверх: стопа {t['foot']}, голень {t['shin']}, "
              f"бедро {t['thigh']},",
              f"торс {t['torso']}, шея {t['neck']}, голова {t['head']}.",
              f"Шея — сегмент роста, но часть спрайта головы: он {t['w_head']}x"
              f"{t['head'] + t['neck']}.",
              f"Рука: плечо-локоть {t['arm'][0]}, предплечье {t['arm'][1]}, "
              f"кисть {t['arm'][2]}.",
              f"Перекрытие у сустава {t['overlap']} px, дальняя сторона уже "
              f"на {t['far_side']} px.",
              f"Сборочный канвас {t['canvas']}x{t['canvas']}.",
              f"Числа читаются из {GRID_DOC}.")

    pad_l, pad_t, gap, text_w = 56, 44, 24, 540
    # Высота --- по тому, что выше: линейка мелкого тира короче своих подписей.
    text_h = (30 + len(levels(t)) * 22 + 14
              + 30 + len(verticals(t)) * 22 + 14
              + len(footer) * 20)
    w = pad_l + big.width + gap + text_w
    h = pad_t + max(big.height, text_h) + 28
    img = Image.new("RGBA", (w, h), (24, 24, 30, 255))
    draw = ImageDraw.Draw(img)

    f_small, f_head = font(15), font(19)
    draw.text((pad_l, 12), f"Линейка пропорций, тир {figure} --- фигура {figure} px, "
                           f"голова {t['head_ratio']} роста", font=f_head, fill=(235, 235, 240))

    # Шахматка под линейкой: видно, где прозрачно, а где линия.
    for cy in range(0, big.height, 8 * zoom):
        for cx in range(0, big.width, 8 * zoom):
            tone = 74 if ((cx // (8 * zoom)) + (cy // (8 * zoom))) % 2 == 0 else 58
            draw.rectangle([pad_l + cx, pad_t + cy,
                            pad_l + min(cx + 8 * zoom, big.width) - 1,
                            pad_t + min(cy + 8 * zoom, big.height) - 1],
                           fill=(tone, tone, tone + 6))
    img.alpha_composite(big, (pad_l, pad_t))

    # Шкала слева: отметка каждые 8 px фигуры, счёт от подошвы.
    for h_mark in range(0, figure + 1, 8):
        y = pad_t + big.height - 1 - h_mark * zoom
        draw.line([(pad_l - 10, y), (pad_l - 2, y)], fill=(150, 150, 160))
        draw.text((pad_l - 44, y - 8), f"{h_mark:>3}", font=f_small, fill=(150, 150, 160))

    tx = pad_l + big.width + gap
    ty = pad_t
    draw.text((tx, ty), "Уровни, счёт от подошвы", font=f_head, fill=(235, 235, 240))
    ty += 30
    for h_lvl, label, color, solid in sorted(levels(t), reverse=True):
        draw.rectangle([tx, ty + 6, tx + 22, ty + 8], fill=color[:3])
        weight = "" if solid else "  (пунктир)"
        draw.text((tx + 32, ty), f"{h_lvl:>3}  {label}{weight}", font=f_small,
                  fill=(225, 225, 232) if solid else (170, 170, 180))
        ty += 22

    ty += 14
    draw.text((tx, ty), "Вертикали", font=f_head, fill=(235, 235, 240))
    ty += 30
    for half, label in verticals(t):
        color = COLOR_AXIS if half == 0 else COLOR_WIDTH
        draw.rectangle([tx, ty + 6, tx + 22, ty + 8], fill=color[:3])
        draw.text((tx + 32, ty), f"+-{half:<3} {label}", font=f_small, fill=(200, 220, 195))
        ty += 22

    ty += 14
    for line in footer:
        draw.text((tx, ty), line, font=f_small, fill=(160, 165, 175))
        ty += 20

    return img


def main(argv: list[str] | None = None) -> int:
    # Консоль Windows по умолчанию не UTF-8, а сообщения об ошибках здесь
    # русские: без этого разбор сетки жалуется кракозябрами.
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--tier", type=int, help="рост фигуры: линейка только этого тира")
    parser.add_argument("--size", type=int, help="сторона канваса под чужой файл")
    parser.add_argument("--sole", type=int, help="отступ подошвы от низа канваса")
    parser.add_argument("--axis", type=int, help="x оси симметрии на чужой канве")
    parser.add_argument("--name", help="имя выходного файла для режима --size")
    args = parser.parse_args(argv)

    repo = Path(__file__).resolve().parent.parent
    try:
        grid = read_grid(repo)
    except (GridError, OSError) as error:
        print(f"сетка не прочитана: {error}", file=sys.stderr)
        return 1

    if args.tier is not None and args.tier not in grid:
        print(f"тира {args.tier} в сетке нет, есть {sorted(grid)}", file=sys.stderr)
        return 1

    out = repo / OUT_DIR
    out.mkdir(parents=True, exist_ok=True)

    # Разовая линейка под чужой файл: своя канва и своя высота подошвы.
    if args.size is not None:
        if args.tier is None:
            print("--size требует --tier: без него неясен рост фигуры", file=sys.stderr)
            return 1
        t = grid[args.tier]
        sole = args.sole if args.sole is not None else (args.size - t["figure"]) // 2
        img = render(t, size=args.size, sole=sole, axis=args.axis)
        name = args.name or f"ruler-{args.tier}-on-{args.size}.png"
        img.save(out / name)
        print(f"{name}  {img.width}x{img.height}, подошва в {sole} px от низа, "
              f"ось x={args.axis if args.axis is not None else args.size // 2}")
        return 0

    for tier, t in sorted(grid.items()):
        if args.tier is not None and tier != args.tier:
            continue
        for name, img in (
            (f"ruler-{tier}-tight.png", render(t, tight=True)),
            (f"ruler-{tier}-canvas.png", render(t, tight=False)),
            (f"ruler-{tier}-legend.png", render_legend(t)),
        ):
            img.save(out / name)
            print(f"{name}  {img.width}x{img.height}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
