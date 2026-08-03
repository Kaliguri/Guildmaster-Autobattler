"""Снимки карты акта: те же карты, что в Лаборатории, но картинками для разговора.

Зачем отдельный рендер. Стенд живёт в браузере, а сравнивать варианты надо в чате — значит нужен
файл. Ставить headless-браузер ради этого дороже, чем нарисовать те же примитивы на PIL.

Расплата честная и её надо знать: рисовалок становится ДВЕ — эта и docs/lab/src/sections/map-act.ts.
Топология у них общая (обе читают один дамп), а вот вид может разъехаться. Поэтому здесь не
заводится ничего, чего нет в стенде: те же поля листа, тот же шаг дорожки, тот же радиус узла.
Расходится вид — правится обе, и первым признаком служит именно сравнение картинки со стендом.

Запуск — scripts/map-shots.ps1. Дамп берётся готовый (scripts/map-dump.ps1), сам не пересобирается:
снимки обязаны показывать ровно то, что сейчас видно на сайте.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# Вид узла: цвет и знак. Держится в согласии с LOOK в map-act.ts — расхождение сразу видно глазом.
LOOK = {
    "Start":     ("#FFFFFF", "\u25cf", "старт"),
    "Battle":    ("#B8863B", "\u00d7", "бой"),
    "Elite":     ("#E2725B", "\u2726", "элита"),
    "TextEvent": ("#93805E", "\u2026", "ивент"),
    "Shop":      ("#FFCC33", "$", "лавка"),
    "Boss":      ("#FF6B5A", "\u2605", "босс"),
    "Chest":     ("#FFCC33", "\u25a3", "сундук"),
    "Unknown":   ("#93805E", "?", "неизвестность"),
    "Camp":      ("#8CFFA6", "\u25b2", "привал"),
}

BG = "#0C0B09"
SHEET_FILL = (232, 220, 196, 16)
SHEET_EDGE = (184, 134, 59, 90)
DOT = (184, 134, 59, 140)
NODE_FILL = "#18140E"

FONT_DIR = Path("C:/Windows/Fonts")


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    path = FONT_DIR / name
    if not path.exists():
        return ImageFont.load_default()
    return ImageFont.truetype(str(path), size)


def place(nodes: list[list[int]], step_x: float, step_y: float) -> list[tuple[float, float, int]]:
    """Раскладка: этаж по X, ряд центрируется по фактической ширине этажа. Формула из MapLayout."""
    width_of: dict[int, int] = {}
    for floor, row, _ in nodes:
        width_of[floor] = max(width_of.get(floor, 0), row + 1)
    out = []
    for floor, row, kind in nodes:
        width = width_of[floor]
        out.append((floor * step_x, (row - (width - 1) * 0.5) * step_y, kind))
    return out


def draw_map(profile: dict, node_types: list[str], map_data: dict, size: tuple[int, int],
             frame: bool = False, floor: int = -1) -> Image.Image:
    w, h = size
    # Радиус узла приходит из дампа (префаб MapNode) — своего числа здесь нет намеренно.
    node_r = profile["style"]["nodeRadius"]
    img = Image.new("RGB", (w, h), BG)
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    style = profile["style"]
    step_x = style["stepX"]
    step_y = style["stepY"]
    placed = place(map_data["nodes"], step_x, step_y)

    xs = [p[0] for p in placed]
    ys = [p[1] for p in placed]
    min_x, max_x = min(xs), max(xs)
    min_y, max_y = min(ys), max(ys)

    graph_w = max_x - min_x + node_r * 2
    graph_h = max_y - min_y + node_r * 2
    sheet_w = graph_w * style["sheetPadX"]
    sheet_h = graph_h * style["sheetPadY"]

    head = 44                                    # полоса под подпись сверху
    margin = 24
    if frame:
        # Рабочий кадр игры: камера держит floorsInView этажей, а не весь акт. Только в нём и виден
        # настоящий вопрос про воздух — на общем плане больший шаг просто мельчит узлы.
        floors = style["floorsInView"]
        k = (w - margin * 2) / (floors * step_x)
        # Смотрим не в геометрический центр, а на заданный этаж: середина акта занята привалом,
        # и кадр по ней показывал бы веер, а не типичный участок пути.
        cx = (floor if floor >= 0 else (min_x + max_x) / 2 / step_x) * step_x
        cy = 0.0
    else:
        k = min((w - margin * 2) / sheet_w, (h - head - margin * 2) / sheet_h)
        cx, cy = (min_x + max_x) / 2, (min_y + max_y) / 2
    ox, oy = w / 2, head + (h - head) / 2

    def sx(x: float) -> float:
        return ox + (x - cx) * k

    def sy(y: float) -> float:
        return oy + (y - cy) * k

    # лист: на общем плане он рамка карты, в рабочем кадре его края за экраном
    if not frame:
        x0, y0 = sx(cx) - sheet_w * k / 2, sy(cy) - sheet_h * k / 2
        d.rectangle([x0, y0, x0 + sheet_w * k, y0 + sheet_h * k], fill=SHEET_FILL, outline=SHEET_EDGE)

    # дорожки точками
    spacing = style["dotSpacing"]
    clearance = style["dotClearance"] * node_r
    dot_r = max(1.0, style["dotRadius"] * k)
    for a_i, b_i in map_data["edges"]:
        ax, ay, _ = placed[a_i]
        bx, by, _ = placed[b_i]
        length = math.hypot(bx - ax, by - ay)
        if length <= clearance * 2:
            continue
        ux, uy = (bx - ax) / length, (by - ay) / length
        t = clearance
        while t <= length - clearance:
            px, py = sx(ax + ux * t), sy(ay + uy * t)
            d.ellipse([px - dot_r, py - dot_r, px + dot_r, py + dot_r], fill=DOT)
            t += spacing

    # узлы
    r = node_r * k
    mark_font = font("seguisym.ttf", max(8, int(r * 1.15)))
    for x, y, kind in placed:
        name = node_types[kind] if kind < len(node_types) else ""
        color, mark, _ = LOOK.get(name, ("#93805E", "\u00b7", ""))
        px, py = sx(x), sy(y)
        d.ellipse([px - r, py - r, px + r, py + r], fill=NODE_FILL,
                  outline=color, width=max(1, int(r * 0.14)))
        if r > 5:
            d.text((px, py), mark, font=mark_font, fill=color, anchor="mm")

    img = Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB")

    # подпись: чей профиль, какой сид, какие числа
    d2 = ImageDraw.Draw(img)
    title = font("consolab.ttf", 22)
    small = font("consola.ttf", 18)
    d2.text((margin, 12), profile["title"].upper(), font=title, fill="#B8863B")
    facts = (
        f"{'кадр игры, ' + str(style['floorsInView']) + ' этажа' if frame else 'весь акт'}  ·  "
        f"сид {map_data['seed']}  ·  шаг {step_x}x{step_y}  ·  "
        f"веер до {profile['config']['maxEdgesPerNode']}  ·  "
        f"ширина {profile['config']['minColumnWidth']}-{profile['config']['maxColumnWidth']}  ·  "
        f"узлов {len(map_data['nodes'])}"
    )
    d2.text((margin + 230, 15), facts, font=small, fill="#93805E")
    return img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", default="docs/lab/data/act-maps.json")
    ap.add_argument("--out", default="Temp/map-shots")
    ap.add_argument("--profiles", default="", help="через запятую; пусто = первые два из дампа")
    ap.add_argument("--seeds", default="", help="через запятую; пусто = три случайных из дампа")
    ap.add_argument("--count", type=int, default=3, help="сколько сидов брать, если они не заданы")
    ap.add_argument("--view", default="all", choices=["all", "frame"],
                    help="all — весь акт, frame — рабочий кадр камеры")
    ap.add_argument("--floor", type=int, default=4, help="на какой этаж смотрит рабочий кадр")
    ap.add_argument("--width", type=int, default=0)
    ap.add_argument("--height", type=int, default=0)
    args = ap.parse_args()

    # Кадр игры снимается в пропорциях экрана: растянутый кадр врал бы про то, сколько узлов
    # помещается по высоте. Общий план шире — там задача показать акт целиком.
    width = args.width or (1600 if args.view == "frame" else 2200)
    height = args.height or (900 if args.view == "frame" else 820)

    dump = json.loads(Path(args.dump).read_text(encoding="utf-8"))
    profiles = {p["id"]: p for p in dump["profiles"]}

    if args.profiles:
        wanted = [p.strip() for p in args.profiles.split(",")]
    else:
        wanted = [p["id"] for p in dump["profiles"]][:2]
    for pid in wanted:
        if pid not in profiles:
            raise SystemExit(f"Нет профиля '{pid}'. Есть: {', '.join(profiles)}")

    seeds_all = dump["seeds"]
    if args.seeds:
        seeds = [int(s) for s in args.seeds.split(",")]
        for s in seeds:
            if s not in seeds_all:
                raise SystemExit(f"Сида {s} нет в дампе (есть {seeds_all[0]}..{seeds_all[-1]}).")
    else:
        seeds = sorted(random.sample(seeds_all, min(args.count, len(seeds_all))))

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    # Чистим только свой вид: снимки общего плана и рабочего кадра нужны рядом, а не по очереди.
    for old in out.glob(f"*-{args.view}.png"):
        old.unlink()

    made = []
    for pid in wanted:
        profile = profiles[pid]
        by_seed = {m["seed"]: m for m in profile["maps"]}
        for seed in seeds:
            img = draw_map(profile, dump["nodeTypes"], by_seed[seed], (width, height),
                           frame=args.view == "frame", floor=args.floor)
            path = out / f"{pid}-{seed}-{args.view}.png"
            img.save(path)
            made.append(path)

    # Пути печатаем по одному на строку: их забирает ps1 и отдаёт дальше.
    print(",".join(str(s) for s in seeds))
    for path in made:
        print(path.as_posix())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
