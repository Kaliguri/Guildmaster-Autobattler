"""Находит границы 9-slice у рамок и пластин и печатает их JSON'ом.

Border ищется ЗАМЕРОМ, а не на глаз: столбец сравнивается с профилем середины кадра, и
границей объявляется место, где столбец перестаёт от него отличаться — дальше идёт та самая
однородная полоса, которую 9-slice и тянет. Считать руками по сорока сторонам значило бы
сорок раз ошибиться по-разному.

Запуск:  py scripts/find_slice_borders.py
Выход:   JSON в stdout — его применяет к импортёрам вызов AssetDatabase на стороне редактора.
"""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "Assets/_Project/Art/UI Textures"

# Пачки, у которых 9-slice вообще имеет смысл: у задника и маски тянется весь кадр.
SLICED = ("Frames", "Plates")

# Доля от максимального расхождения, ниже которой столбец считается «уже однородная полоса».
SETTLE = 0.12

# Страховка от вырождения: борта в сумме не должны съедать середину.
MAX_SHARE = 0.42

# Ниже этой доли стороны замер считается неуверенным, ниже BLIND_SHARE — слепым.
# MIN_SHARE выбран по самому мелкому реальному углу в пачке (завитки Frame_Scroll, ~55 px из 916).
MIN_SHARE = 0.06
BLIND_SHARE = 0.03


def axis_profile(im: Image.Image, horizontal: bool) -> list[float]:
    """Расхождение вдоль оси, замеренное В ПРИГРАНИЧНЫХ ПОЛОСАХ.

    Смотреть на весь столбец нельзя: у рамки он почти одинаков в углу и в середине грани —
    и там, и там это «линия рамки плюс заливка», так что расхождение возникало лишь в самых
    крайних пикселях и границы выходили по 2-9 px при угле в добрую сотню. Декор, который
    9-slice обязан не тянуть, сидит именно у краёв поперечной оси, поэтому профиль считается
    по двум полосам вдоль них, а не по всей глубине кадра.
    """
    w, h = im.size
    px = im.load()
    if horizontal:
        span, depth = w, h
        get = lambda i, j: px[i, j]
    else:
        span, depth = h, w
        get = lambda i, j: px[j, i]

    band = max(2, int(depth * 0.14))
    rows = list(range(0, band)) + list(range(depth - band, depth))
    step = max(1, len(rows) // 64)
    rows = rows[::step]

    # Эталон — ПРОФИЛЬ середины грани, а не одно её среднее: усреднение по столбцу теряет
    # структуру, и «перо в углу» давало то же число, что «линия на грани», из-за чего границы
    # у половины рамок выходили по 2 px.
    mid = range(int(span * 0.45), max(int(span * 0.55), int(span * 0.45) + 1))
    ref = [sum(get(i, j) for i in mid) / len(mid) for j in rows]
    return [sum(abs(get(i, j) - ref[k]) for k, j in enumerate(rows)) / len(rows) for i in range(span)]


def border_from(profile: list[float], span: int) -> tuple[int, int]:
    cap = int(span * MAX_SHARE)

    # Порог считается по перцентилю зоны поиска БЕЗ самой кромки: после обрезки на нулевом
    # пикселе стоит резкий скачок альфы, и если брать максимум, весь угловой декор оказывается
    # ниже порога — границы выходили по 2 px у половины рамок.
    zone = profile[3:cap] + profile[span - cap: span - 3]
    zone = sorted(zone)
    peak = zone[int(len(zone) * 0.95)] if zone else 1.0
    limit = max(peak * SETTLE, 0.5)

    # Сканируем ОТ СЕРЕДИНЫ НАРУЖУ и ищем, где профиль в последний раз поднимается над порогом:
    # это и есть внешняя кромка декора. Поиск от края внутрь давал 2 px, потому что у самой
    # границы кадра рамка начинается тонкой линией, и первое же падение ниже порога случалось
    # раньше, чем начинался угол.
    low = 0
    for i in range(cap, 0, -1):
        if profile[i - 1] > limit:
            low = i
            break

    high = 0
    for i in range(cap, 0, -1):
        if profile[span - i] > limit:
            high = i
            break

    return low, high


def main() -> None:
    result: dict[str, dict[str, int]] = {}
    for folder in SLICED:
        for f in sorted((WORK / folder).glob("*.png")):
            im = Image.open(f)
            # Меряем по альфе: она чисто отделяет фигуру от пустоты, а рельеф внутри фигуры
            # к границе слайса отношения не имеет.
            chan = im.split()[3] if im.mode == "RGBA" else im.convert("L")
            w, h = im.size
            left, right = border_from(axis_profile(chan, True), w)
            top, bottom = border_from(axis_profile(chan, False), h)
            key = f"{folder}/{f.name}"
            if folder == "Frames":
                # Рамка симметрична по построению, поэтому четыре замера сводятся к ОДНОМУ числу.
                # Берём ВТОРОЙ СНИЗУ, а не медиану и не среднее: промахи здесь бывают только
                # вверх — профиль цепляет соседний декор и уводит сторону вдвое (у Stepped верх
                # дал 290 против 151 по бокам), и медиана из двух средних такую пару не гасит.
                # Если и второй снизу мал, замер угла не увидел — тогда максимум, а совсем
                # слепой случай (тонкие завитки Scroll) добирает минимум.
                side = min(w, h)
                quad = sorted((left, right, top, bottom))
                value = quad[1]
                if value < side * MIN_SHARE:
                    value = max(quad)
                if value < side * BLIND_SHARE:
                    value = side * MIN_SHARE
                left = right = top = bottom = int(value)
            else:
                # У ленты своя симметрия: концы равны между собой, кромки — между собой.
                left = right = max(left, right)
                top = bottom = max(top, bottom)
            result[key] = {"left": left, "right": right, "top": top, "bottom": bottom}
            print(
                f"# {key:34s} {w}x{h}  L{left:4d} R{right:4d} T{top:4d} B{bottom:4d}",
                flush=True,
            )

    print(json.dumps(result, indent=1))


if __name__ == "__main__":
    main()
