#!/usr/bin/env python3
"""Инструмент нарезки юнита: разбор .aseprite, проверка частей, правка слоёв.

Зачем: части юнита живут в .aseprite, а требования к ним — в размерной сетке
(docs/view-angle-progress.md). Глазами перекрытие у сустава и мусорный пиксель
внутри цела не поймать, а оба ломают анимацию: первое даёт щель на повороте,
второе раздувает габарит цела и уводит пивот.

Подкоманды:
  probe    — канва, дерево слоёв, габариты целов
  sheet    — контактный лист: сборка и каждая часть отдельно (PNG)
  check    — перекрытия у суставов, лишние острова, уровни против сетки
  clean    — убрать из цела всё, кроме крупнейшего острова, и ре-триммить
  addlayer — вписать PNG новым слоем (например линейку пропорций)
  bend     — согнуть сустав и показать, что вылезет: щель или спрятанный хвост

ВАЖНО: подкоманды `clean` и `addlayer` ПИШУТ в .aseprite. Запускать их можно
только при ЗАКРЫТОМ Aseprite — иначе редактор при своём следующем сохранении
перезапишет файл версией из памяти, и правка исчезнет. Перед записью рядом
кладётся бэкап.

Формат .aseprite разбирается вручную (заголовок 128 байт, кадры по 16, чанки
0x2004 слои и 0x2005 целы) — сторонней зависимости для этого не завозим.
"""

from __future__ import annotations

import argparse
import importlib.util
import shutil
import struct
import sys
import zlib
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parent.parent
GRID_SCRIPT = REPO / "scripts" / "art-proportion-ruler.py"


def load_grid():
    """Размерная сетка из документа-владельца через генератор линеек.

    Модуль подгружается по пути, а не импортом: в имени файла дефис, и
    `import art-proportion-ruler` невозможен. Дублировать разбор сетки нельзя —
    у неё один владелец.
    """
    spec = importlib.util.spec_from_file_location("art_proportion_ruler", GRID_SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.read_grid(REPO)


# --- Разбор файла --------------------------------------------------------

class Doc:
    """Разобранный .aseprite: слои деревом, целы картинками."""

    def __init__(self, path: Path):
        self.path = path
        self.data = bytes(path.read_bytes())
        (self.file_size, magic, self.frames,
         self.width, self.height, self.depth) = struct.unpack_from("<IHHHHH", self.data, 0)
        if magic != 0xA5E0:
            raise ValueError(f"{path.name}: не .aseprite (magic {magic:#x})")
        if self.depth != 32:
            raise ValueError(f"{path.name}: ожидалась глубина 32 бита, а не {self.depth}")
        if len(self.data) != self.file_size:
            raise ValueError(f"{path.name}: размер в заголовке {self.file_size} "
                             f"!= {len(self.data)} на диске")

        self.layers: list[dict] = []
        self.cels: dict[int, dict] = {}       # индекс слоя -> цел первого кадра
        self.frame_spans: list[tuple[int, int, int, int]] = []
        self.chunk_spans: list[tuple[int, int, int, int]] = []

        off = 128
        for frame_index in range(self.frames):
            fsize, fmagic, old_n, _dur = struct.unpack_from("<IHHH", self.data, off)
            if fmagic != 0xF1FA:
                raise ValueError(f"кадр в {off} не начинается с F1FA")
            count = struct.unpack_from("<I", self.data, off + 12)[0] or old_n
            self.frame_spans.append((off, fsize, old_n, count))
            p = off + 16
            for _ in range(count):
                csize, ctype = struct.unpack_from("<IH", self.data, p)
                self.chunk_spans.append((frame_index, p, csize, ctype))
                if frame_index == 0:
                    self._read_chunk(p, csize, ctype)
                p += csize
            off += fsize
        if off != len(self.data):
            raise ValueError(f"кадры не покрывают файл: {off} != {len(self.data)}")

        # Видимость наследуется от групп: скрытая группа гасит детей.
        stack: list[bool] = []
        for layer in self.layers:
            del stack[layer["child"]:]
            layer["effective_vis"] = layer["vis"] and all(stack)
            stack.append(layer["effective_vis"])

        # Полный путь слоя, чтобы Leg (Left) и Leg (Right) различались.
        names: list[str] = []
        for layer in self.layers:
            del names[layer["child"]:]
            names.append(layer["name"])
            layer["path"] = list(names)

    def _read_chunk(self, p: int, csize: int, ctype: int) -> None:
        body = p + 6
        if ctype == 0x2004:
            flags, ltype, child = struct.unpack_from("<HHH", self.data, body)
            nlen = struct.unpack_from("<H", self.data, body + 16)[0]
            name = self.data[body + 18:body + 18 + nlen].decode("utf-8", "replace")
            self.layers.append(dict(name=name, group=ltype == 1, child=child,
                                    vis=bool(flags & 1), editable=bool(flags & 2)))
        elif ctype == 0x2005:
            li, cx, cy, opacity, cel_type = struct.unpack_from("<HhhBH", self.data, body)
            if cel_type not in (0, 2):
                return                        # связанный или тайловый цел не разбираем
            zindex = struct.unpack_from("<h", self.data, body + 9)[0]
            cw, ch = struct.unpack_from("<HH", self.data, body + 16)
            blob = self.data[body + 20:p + csize]
            raw = zlib.decompress(blob) if cel_type == 2 else blob[:cw * ch * 4]
            self.cels[li] = dict(x=cx, y=cy, opacity=opacity, zindex=zindex,
                                 chunk=(p, csize), cel_type=cel_type,
                                 image=Image.frombytes("RGBA", (cw, ch), raw))

    def group_members(self, group: str) -> list[int]:
        """Индексы слоёв-рисунков внутри группы, в порядке снизу вверх."""
        start = next((i for i, l in enumerate(self.layers)
                      if l["name"] == group and l["group"]), None)
        if start is None:
            raise ValueError(f"группы «{group}» в файле нет")
        level = self.layers[start]["child"]
        members = []
        for i in range(start + 1, len(self.layers)):
            if self.layers[i]["child"] <= level:
                break
            if not self.layers[i]["group"]:
                members.append(i)
        return members

    def find_by_path(self, path: list[str]) -> int:
        for i, layer in enumerate(self.layers):
            if layer["path"] == path and not layer["group"]:
                return i
        raise ValueError(f"слоя по пути {' / '.join(path)} нет")

    def label(self, index: int) -> str:
        path = self.layers[index]["path"]
        return " / ".join(path[1:]) if len(path) > 1 else path[0]

    def assemble(self, members: list[int], visible_only: bool = True) -> Image.Image:
        canvas = Image.new("RGBA", (self.width, self.height), (0, 0, 0, 0))
        for i in members:
            if visible_only and not self.layers[i]["effective_vis"]:
                continue
            cel = self.cels.get(i)
            if cel:
                canvas.alpha_composite(cel["image"], (cel["x"], cel["y"]))
        return canvas


def islands(img: Image.Image) -> list[list[tuple[int, int]]]:
    """Связные области непрозрачных пикселей, крупные первыми (8-связность)."""
    seen = [[False] * img.width for _ in range(img.height)]
    found = []
    for sy in range(img.height):
        for sx in range(img.width):
            if seen[sy][sx] or img.getpixel((sx, sy))[3] == 0:
                continue
            queue, cells = deque([(sx, sy)]), []
            seen[sy][sx] = True
            while queue:
                x, y = queue.popleft()
                cells.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1),
                               (1, 1), (1, -1), (-1, 1), (-1, -1)):
                    nx, ny = x + dx, y + dy
                    if (0 <= nx < img.width and 0 <= ny < img.height
                            and not seen[ny][nx] and img.getpixel((nx, ny))[3] > 0):
                        seen[ny][nx] = True
                        queue.append((nx, ny))
            found.append(cells)
    return sorted(found, key=len, reverse=True)


def vertical_span(cel: dict) -> tuple[int, int]:
    """Верх и низ непрозрачного содержимого цела в координатах канвы."""
    img, cy = cel["image"], cel["y"]
    rows = [y for y in range(img.height)
            if any(img.getpixel((x, y))[3] > 0 for x in range(img.width))]
    return cy + rows[0], cy + rows[-1]


def edge_darkness(cel: dict) -> tuple[float, float]:
    """Доля тёмных пикселей в верхней и нижней непрозрачной строке.

    Нужна, чтобы увидеть, замкнут ли контур на срезе: незамкнутый срез даёт
    на повороте белую дыру без обводки.
    """
    img = cel["image"]
    result = []
    rows = [y for y in range(img.height)
            if any(img.getpixel((x, y))[3] > 0 for x in range(img.width))]
    for y in (rows[0], rows[-1]):
        xs = [x for x in range(img.width) if img.getpixel((x, y))[3] > 0]
        dark = sum(1 for x in xs if sum(img.getpixel((x, y))[:3]) / 3 < 100)
        result.append(dark / len(xs))
    return result[0], result[1]


# --- Подкоманды ----------------------------------------------------------

def cmd_probe(args) -> int:
    doc = Doc(Path(args.file))
    print(f"канва {doc.width}x{doc.height}, кадров {doc.frames}, "
          f"глубина {doc.depth} бит, слоёв {len(doc.layers)}")
    for i, layer in enumerate(doc.layers):
        kind = "группа" if layer["group"] else "рисунок"
        hidden = "" if layer["effective_vis"] else "  (скрыт)"
        cel = doc.cels.get(i)
        geom = ""
        if cel:
            top, bottom = vertical_span(cel)
            geom = (f"   {cel['image'].width}x{cel['image'].height} "
                    f"@ ({cel['x']},{cel['y']}) содержимое {top}..{bottom}")
        print(f"  [{i:>2}] {'  ' * layer['child']}{layer['name']}   {kind}{hidden}{geom}")
    return 0


def cmd_sheet(args) -> int:
    doc = Doc(Path(args.file))
    members = doc.group_members(args.group)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    whole = doc.assemble(members)
    bbox = whole.getbbox()
    if bbox is None:
        print("в группе нет видимого содержимого", file=sys.stderr)
        return 1
    print(f"сборка: {bbox[2]-bbox[0]}x{bbox[3]-bbox[1]} в ({bbox[0]},{bbox[1]}), "
          f"низ y={bbox[3]}")

    zoom, pad = args.zoom, 10
    crop = whole.crop((bbox[0] - pad, bbox[1] - pad, bbox[2] + pad, bbox[3] + pad))
    plate = Image.new("RGBA", (crop.width * zoom, crop.height * zoom), (30, 30, 36, 255))
    plate.alpha_composite(crop.resize((crop.width * zoom, crop.height * zoom), Image.NEAREST))
    plate.save(out / "parts-whole.png")

    font = _font(14)
    zz = max(zoom - 2, 3)
    tiles = []
    for i in members:
        cel = doc.cels.get(i)
        if not cel:
            continue
        img = cel["image"]
        tile_w, tile_h = img.width * zz, img.height * zz
        tile = Image.new("RGBA", (max(tile_w, 210), tile_h + 40), (30, 30, 36, 255))
        draw = ImageDraw.Draw(tile)
        draw.text((3, 3), doc.label(i)[-26:], font=font, fill=(235, 235, 240))
        draw.text((3, 20), f"{img.width}x{img.height} @ ({cel['x']},{cel['y']})",
                  font=font, fill=(150, 155, 165))
        _checker(draw, 0, 40, tile_w, tile_h, 8 * zz)
        tile.alpha_composite(img.resize((tile_w, tile_h), Image.NEAREST), (0, 40))
        tiles.append(tile)

    _grid_sheet(tiles, 5, 12).save(out / "parts-sheet.png")
    print(f"файлы: {out / 'parts-whole.png'}, {out / 'parts-sheet.png'}")
    return 0


def cmd_check(args) -> int:
    doc = Doc(Path(args.file))
    members = doc.group_members(args.group)
    grid = load_grid()
    if args.tier not in grid:
        print(f"тира {args.tier} в сетке нет, есть {sorted(grid)}", file=sys.stderr)
        return 1
    tier = grid[args.tier]
    overlap_required = tier["overlap"]

    whole = doc.assemble(members)
    bbox = whole.getbbox()
    sole = args.sole if args.sole is not None else bbox[3]
    print(f"сборка {bbox[2]-bbox[0]}x{bbox[3]-bbox[1]}, подошва y={sole}, "
          f"тир {args.tier}, перекрытие по сетке {overlap_required} px\n")

    spans, problems = {}, []
    print(f"{'часть':40} {'габарит':10} {'верх..низ':11} островов контур")
    for i in members:
        cel = doc.cels.get(i)
        if not cel:
            continue
        label = doc.label(i)
        top, bottom = vertical_span(cel)
        spans[label] = (top, bottom)
        isles = islands(cel["image"])
        junk = [x for x in isles[1:] if len(x) * 10 < len(isles[0])]
        top_dark, bottom_dark = edge_darkness(cel)
        note = ""
        if junk:
            spots = "; ".join(f"{len(x)} px в "
                              f"({cel['x'] + min(a for a, _ in x)},"
                              f"{cel['y'] + min(b for _, b in x)})" for x in junk)
            note = f"  <-- МУСОР: {spots}"
            problems.append(f"мусор в «{label}»: {spots}")
        if min(top_dark, bottom_dark) < 0.5:
            problems.append(f"срез «{label}» не обведён "
                            f"(верх {top_dark:.0%}, низ {bottom_dark:.0%})")
        print(f"{label[-40:]:40} {cel['image'].width:>2}x{cel['image'].height:<3}  "
              f"{top:>3}..{bottom:<3}   {len(isles):>5}   "
              f"{top_dark:.0%}/{bottom_dark:.0%}{note}")

    if args.chain:
        print(f"\n{'родитель -> ребёнок':58} перекрытие")
        for pair in args.chain:
            parent, child = pair.split("->")
            parent, child = parent.strip(), child.strip()
            found = {}
            for label in spans:
                for key in (parent, child):
                    if label.endswith(key) or label == key:
                        found.setdefault(key, []).append(label)
            if parent not in found or child not in found:
                print(f"{pair}: часть не найдена")
                continue
            parents, children = found[parent], found[child]
            # Один родитель на нескольких детей (торс и обе руки) — размножаем
            # его, иначе zip молча проверил бы только первую пару.
            if len(parents) == 1 and len(children) > 1:
                parents = parents * len(children)
            for a_label, b_label in zip(parents, children):
                a, b = spans[a_label], spans[b_label]
                # Пересечение диапазонов: ветвление «кто ниже» врёт для
                # наплечника, который сидит в ВЕРХНЕЙ части торса.
                overlap = min(a[1], b[1]) - max(a[0], b[0]) + 1
                verdict = "ок" if overlap >= overlap_required else (
                    "МАЛО" if overlap > 0 else "ЩЕЛЬ")
                if verdict != "ок":
                    problems.append(f"стык {a_label} -> {b_label}: {overlap} px, "
                                    f"нужно {overlap_required}")
                print(f"{a_label.split(' / ')[-1]:26} -> {b_label.split(' / ')[-1]:26} "
                      f"{overlap:>4} px   {verdict}")

    print()
    if problems:
        print(f"проблем: {len(problems)}")
        for line in problems:
            print(f"  - {line}")
        return 1
    print("проблем не найдено")
    return 0


def cmd_clean(args) -> int:
    path = Path(args.file)
    doc = Doc(path)
    index = doc.find_by_path(args.layer.split("/"))
    cel = doc.cels.get(index)
    if not cel:
        print(f"у слоя «{args.layer}» нет цела в первом кадре", file=sys.stderr)
        return 1

    img = cel["image"]
    isles = islands(img)
    print(f"слой {index} «{doc.label(index)}»: {img.width}x{img.height} "
          f"в ({cel['x']},{cel['y']}), островов {[len(x) for x in isles]}")
    if len(isles) < 2:
        print("чистить нечего: остров всего один")
        return 0

    cleaned = img.copy()
    removed = 0
    for isle in isles[1:]:
        for x, y in isle:
            cleaned.putpixel((x, y), (0, 0, 0, 0))
            removed += 1
    bbox = cleaned.getbbox()
    trimmed = cleaned.crop(bbox)
    new_x, new_y = cel["x"] + bbox[0], cel["y"] + bbox[1]
    print(f"станет: {trimmed.width}x{trimmed.height} в ({new_x},{new_y}), "
          f"убрано {removed} px")
    if args.dry_run:
        print("сухой прогон, файл не тронут")
        return 0

    pixels = zlib.compress(trimmed.tobytes(), 9)
    payload = struct.pack("<HhhBHh5s", index, new_x, new_y, cel["opacity"], 2,
                          cel["zindex"], b"\0" * 5)
    payload += struct.pack("<HH", trimmed.width, trimmed.height) + pixels
    chunk = struct.pack("<IH", 6 + len(payload), 0x2005) + payload

    start, size = cel["chunk"]
    out = bytearray(doc.data[:start] + chunk + doc.data[start + size:])
    frame_off, fsize, _old_n, _new_n = doc.frame_spans[0]
    struct.pack_into("<I", out, frame_off, fsize + len(chunk) - size)
    struct.pack_into("<I", out, 0, len(out))
    _write(path, out, ".bak-before-clean")
    return 0


def cmd_addlayer(args) -> int:
    path = Path(args.file)
    doc = Doc(path)
    img = Image.open(args.png).convert("RGBA")
    if img.size != (doc.width, doc.height):
        print(f"PNG {img.size} не совпадает с канвой {doc.width}x{doc.height}",
              file=sys.stderr)
        return 1

    name = args.name.encode("utf-8")
    flags = 1 | (2 if args.editable else 0)   # видимый, по умолчанию не редактируемый
    layer_payload = struct.pack("<HHHHHHB3s", flags, 0, 0, 0, 0, 0, 255, b"\0\0\0")
    layer_payload += struct.pack("<H", len(name)) + name
    layer_chunk = struct.pack("<IH", 6 + len(layer_payload), 0x2004) + layer_payload

    pixels = zlib.compress(img.tobytes(), 9)
    cel_payload = struct.pack("<HhhBHh5s", len(doc.layers), 0, 0, 255, 2, 0, b"\0" * 5)
    cel_payload += struct.pack("<HH", doc.width, doc.height) + pixels
    cel_chunk = struct.pack("<IH", 6 + len(cel_payload), 0x2005) + cel_payload

    added = layer_chunk + cel_chunk
    frame_off, fsize, old_n, new_n = doc.frame_spans[-1]
    out = bytearray(doc.data)
    out[frame_off + fsize:frame_off + fsize] = added
    struct.pack_into("<I", out, frame_off, fsize + len(added))
    if old_n + 2 <= 0xFFFF:
        struct.pack_into("<H", out, frame_off + 6, old_n + 2)
    struct.pack_into("<I", out, frame_off + 12, new_n + 2)
    struct.pack_into("<I", out, 0, len(out))
    print(f"слой «{args.name}» получит индекс {len(doc.layers)}, "
          f"цел сжат до {len(pixels)} байт")
    if args.dry_run:
        print("сухой прогон, файл не тронут")
        return 0
    _write(path, out, ".bak-before-addlayer")
    return 0


def cmd_bend(args) -> int:
    """Гнёт сустав и показывает результат: щель и спрятанные хвосты частей."""
    doc = Doc(Path(args.file))
    members = doc.group_members(args.group)
    by_label = {doc.label(i): i for i in members}

    def plane(label: str) -> Image.Image:
        index = next((i for l, i in by_label.items() if l.endswith(label)), None)
        if index is None:
            raise ValueError(f"части «{label}» нет в группе")
        cel = doc.cels[index]
        layer = Image.new("RGBA", (doc.width, doc.height), (0, 0, 0, 0))
        layer.alpha_composite(cel["image"], (cel["x"], cel["y"]))
        return layer

    joint = (args.joint[0], args.joint[1])
    frame = Image.new("RGBA", (doc.width, doc.height), (0, 0, 0, 0))
    for label in args.static:
        frame.alpha_composite(plane(label))
    for label in args.rotate:
        frame.alpha_composite(plane(label).rotate(args.angle, resample=Image.NEAREST,
                                                  center=joint))

    bbox = frame.getbbox()
    pad = 8
    box = (max(bbox[0] - pad, 0), max(bbox[1] - pad, 0),
           min(bbox[2] + pad, doc.width), min(bbox[3] + pad, doc.height))
    crop = frame.crop(box)
    zoom = args.zoom
    big = crop.resize((crop.width * zoom, crop.height * zoom), Image.NEAREST)
    plate = Image.new("RGBA", (big.width, big.height + 26), (28, 28, 34, 255))
    draw = ImageDraw.Draw(plate)
    draw.text((4, 4), f"{', '.join(args.rotate)} вокруг {joint} на {args.angle}°",
              font=_font(15), fill=(235, 235, 240))
    _checker(draw, 0, 26, big.width, big.height, 8 * zoom)
    plate.alpha_composite(big, (0, 26))
    mx, my = (joint[0] - box[0]) * zoom, (joint[1] - box[1]) * zoom + 26
    draw.ellipse([mx - 10, my - 10, mx + 10, my + 10], outline=(255, 60, 160, 255), width=3)
    plate.save(args.out)
    print(f"сохранено {args.out}")
    return 0


# --- Мелкая помощь -------------------------------------------------------

def _font(size: int):
    for name in ("consola.ttf", "arial.ttf", "DejaVuSansMono.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _checker(draw: ImageDraw.ImageDraw, x0: int, y0: int, w: int, h: int, cell: int) -> None:
    """Шахматка под частью: без неё прозрачная щель читается как фон."""
    for by in range(0, h, cell):
        for bx in range(0, w, cell):
            tone = 70 if ((bx // cell) + (by // cell)) % 2 == 0 else 54
            draw.rectangle([x0 + bx, y0 + by, x0 + bx + cell - 1, y0 + by + cell - 1],
                           fill=(tone, tone, tone + 6))


def _grid_sheet(tiles: list[Image.Image], cols: int, gap: int) -> Image.Image:
    rows = (len(tiles) + cols - 1) // cols
    col_w = max(t.width for t in tiles) + gap
    row_h = max(t.height for t in tiles) + gap
    sheet = Image.new("RGBA", (col_w * min(cols, len(tiles)), row_h * rows), (30, 30, 36, 255))
    for idx, tile in enumerate(tiles):
        sheet.alpha_composite(tile, ((idx % cols) * col_w, (idx // cols) * row_h))
    return sheet


def _write(path: Path, out: bytearray, suffix: str) -> None:
    backup = path.with_suffix(path.suffix + suffix)
    shutil.copy2(path, backup)
    path.write_bytes(out)
    print(f"бэкап: {backup.name}\nзаписано: {len(out)} байт")


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("probe", help="канва, слои, габариты целов")
    p.add_argument("file")
    p.set_defaults(func=cmd_probe)

    p = sub.add_parser("sheet", help="контактный лист частей")
    p.add_argument("file")
    p.add_argument("group")
    p.add_argument("--out", default=".")
    p.add_argument("--zoom", type=int, default=7)
    p.set_defaults(func=cmd_sheet)

    p = sub.add_parser("check", help="перекрытия, острова, уровни против сетки")
    p.add_argument("file")
    p.add_argument("group")
    p.add_argument("--tier", type=int, default=128)
    p.add_argument("--sole", type=int, help="y подошвы; по умолчанию низ сборки")
    p.add_argument("--chain", nargs="*", default=[],
                   help="пары «родитель->ребёнок» по концу имени, например \"Top->Down\"")
    p.set_defaults(func=cmd_check)

    p = sub.add_parser("clean", help="убрать мусорные острова из цела (ПИШЕТ в файл)")
    p.add_argument("file")
    p.add_argument("layer", help="путь слоя через /, например \"Human/Legs/Leg (Left)/Leg (Down)\"")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(func=cmd_clean)

    p = sub.add_parser("addlayer", help="вписать PNG новым слоем (ПИШЕТ в файл)")
    p.add_argument("file")
    p.add_argument("png")
    p.add_argument("name")
    p.add_argument("--editable", action="store_true",
                   help="разрешить рисование в слое; по умолчанию только видимость")
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(func=cmd_addlayer)

    p = sub.add_parser("bend", help="согнуть сустав и посмотреть, что вылезет")
    p.add_argument("file")
    p.add_argument("group")
    p.add_argument("--joint", type=int, nargs=2, required=True, metavar=("X", "Y"))
    p.add_argument("--angle", type=float, required=True)
    p.add_argument("--rotate", nargs="+", required=True, help="части, которые едут")
    p.add_argument("--static", nargs="*", default=[], help="части, которые стоят")
    p.add_argument("--out", default="bend.png")
    p.add_argument("--zoom", type=int, default=8)
    p.set_defaults(func=cmd_bend)

    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (ValueError, OSError) as error:
        print(f"ошибка: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
