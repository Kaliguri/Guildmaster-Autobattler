#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Zamer palitry UI-referensov.

Otvechaet na vopros "kakaya u igry cvetovaya temperatura" chislami, a ne na glaz:
dominiruyushchie cveta s dolyami, svetlota, nasyshchennost, dolya tyomnyh pikselej
i otdelno akcentnyj cvet (samyj nasyshchennyj iz zametnyh).

Zapusk:
    python scripts/ui-ref-palette.py                      # vsya baza refov
    python scripts/ui-ref-palette.py --crop 0,0,320,720   # tolko levaya panel
    python scripts/ui-ref-palette.py --files "TFT HUD.jpg" "Guildrun HUD.jpg"

Rezultat pishetsya v Markdown ryadom s razborami; v konsol idyot tolko progress.
Zavisimost odna - Pillow.
"""

from __future__ import annotations

import argparse
import colorsys
import os
import sys
from pathlib import Path

from PIL import Image

REFS_DIR = Path(r"C:\My Projects\Guildmaster-Autobattler\Art_Dev\UI Refs")
OUT_FILE = REFS_DIR / "_teardowns" / "00-palette-measured.md"

# Skolko cvetov vytaskivaem iz kazhdoj kartinki. Vosem - kompromiss: chetyre
# slipayut fon s panelyu, shestnadcat rassypayutsya na ottenki odnogo i togo zhe.
PALETTE_SIZE = 8
# Kartinku szhimaem pered kvantovaniem: na dolyah cveta eto ne skazyvaetsya,
# a schitaet v desyatki raz bystree.
WORK_WIDTH = 320
# Cvet s doley menshe etogo v svodku ne popadaet - eto shum kompressii JPEG.
MIN_SHARE = 0.005
# Nizhe etoy svetloty piksel schitaetsya "tyomnym fonom".
DARK_L = 0.15
# Vilka svetloty, v kotoroj cvet voobshche mozhet byt akcentom. Nizhnyaya granica
# otsekaet pochti-chyornyj fon, verhnyaya - vybelennyj tekst i zasvety.
ACCENT_L_MIN = 0.22
ACCENT_L_MAX = 0.85
# Nizhe etoy nasyshchennosti piksel - eto seryj, a ne akcent.
ACCENT_S_MIN = 0.35
# Esli takih pikselej menshe etoy doli kadra - akcenta v kadre net voobshche.
ACCENT_MIN_SHARE = 0.002
# Shirina bina gistogrammy tonov v gradusah.
HUE_BIN = 10


def hex_of(rgb: tuple[int, int, int]) -> str:
    return "#{:02X}{:02X}{:02X}".format(*rgb)


def hsl_of(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = (c / 255.0 for c in rgb)
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    return h * 360.0, s, l


def find_accent(im: Image.Image, total: int) -> dict | None:
    """Akcentnyj cvet - kajma, podsvetka, aktivnaya knopka.

    Ishchetsya pryamym prohodom po pikselyam, a NE v kvantovannoy palitre. Prichina:
    akcent zanimaet dolyu procenta ploshchadi, i kvantovanie v vosem cvetov ego
    prosto ne vidit - v palitre okazyvayutsya fon, panel i tekst, a kajma slipaetsya
    s sosedom. Pervaya versiya etogo skripta merila imenno tak i vydavala serye
    #4D5856 s nasyshchennostyu 0.07 v kachestve "akcenta".

    Piksel schitaetsya akcentnym pri nasyshchennosti vyshe ACCENT_S_MIN i svetlote v
    rabochej vilke. Ton beryotsya modoy gistogrammy, a ne srednim: sredneye mezhdu
    krasnym i ziryonym dayot gryaznyj zhyoltyj, kotorogo na kartinke net.
    """
    hits = [hsl_of(px) for px in im.getdata()]
    hits = [(h, s, l) for h, s, l in hits
            if s >= ACCENT_S_MIN and ACCENT_L_MIN <= l <= ACCENT_L_MAX]
    if len(hits) / total < ACCENT_MIN_SHARE:
        return None

    bins: dict[int, list] = {}
    for h, s, l in hits:
        bins.setdefault(int(h // HUE_BIN), []).append((h, s, l))
    best = max(bins.values(), key=len)

    n = len(best)
    h = sum(x[0] for x in best) / n
    s = sum(x[1] for x in best) / n
    l = sum(x[2] for x in best) / n
    r, g, b = colorsys.hls_to_rgb(h / 360.0, l, s)
    rgb = (round(r * 255), round(g * 255), round(b * 255))
    return {"hex": hex_of(rgb), "share": n / total, "h": h, "s": s, "l": l}


def measure(path: Path, crop: tuple[int, int, int, int] | None) -> dict | None:
    """Palitra i statistika odnoj kartinki. None - esli fajl ne otkrylsya."""
    try:
        im = Image.open(path).convert("RGB")
    except Exception as exc:  # bityj fajl ne dolzhen ronyat ves progon
        print(f"  SKIP {path.name}: {exc}", file=sys.stderr)
        return None

    full_size = im.size
    if crop:
        im = im.crop(crop)
    if im.width > WORK_WIDTH:
        im = im.resize((WORK_WIDTH, max(1, im.height * WORK_WIDTH // im.width)), Image.LANCZOS)

    total = im.width * im.height
    quant = im.quantize(colors=PALETTE_SIZE, method=Image.MEDIANCUT)
    palette = quant.getpalette()
    counts = sorted(quant.getcolors(), key=lambda c: -c[0])

    entries = []
    for count, index in counts:
        share = count / total
        if share < MIN_SHARE:
            continue
        rgb = tuple(palette[index * 3: index * 3 + 3])
        h, s, l = hsl_of(rgb)
        entries.append({"hex": hex_of(rgb), "share": share, "h": h, "s": s, "l": l})

    # Svetlota i nasyshchennost vsej kartinki - vzveshennye po dolyam, a ne
    # srednee po palitre: inache redkij yarkij cvet vesit stolko zhe, skolko fon.
    lum = sum(e["l"] * e["share"] for e in entries)
    sat = sum(e["s"] * e["share"] for e in entries)
    dark = sum(e["share"] for e in entries if e["l"] < DARK_L)

    accent = find_accent(im, total)

    return {
        "name": path.name,
        "size": full_size,
        "entries": entries,
        "lum": lum,
        "sat": sat,
        "dark": dark,
        "accent": accent,
    }


def hue_name(h: float) -> str:
    for edge, label in (
        (15, "red"), (45, "orange"), (70, "yellow"), (160, "green"),
        (200, "cyan"), (255, "blue"), (290, "violet"), (345, "magenta"),
    ):
        if h < edge:
            return label
    return "red"


def render(results: list[dict], crop) -> str:
    lines: list[str] = []
    lines.append("# Zamer palitry referensov\n")
    lines.append("> Fajl generiruetsya `scripts/ui-ref-palette.py`. Rukami ne pravit.\n")
    if crop:
        lines.append(f"> Merilas oblast {crop} (x1,y1,x2,y2), a ne ves kadr.\n")
    lines.append(
        "\nStolbcy: **Lum** - vzveshennaya svetlota (0 chernyj, 1 belyj); **Sat** - vzveshennaya "
        "nasyshchennost; **Dark** - dolya ploshchadi temnee %.2f po svetlote; **Akcent** - samyj "
        "nasyshchennyj iz zametnyh cvetov.\n" % DARK_L
    )

    lines.append("\n## Svodka\n")
    lines.append("| Ref | Lum | Sat | Dark | Akcent | Ton akcenta |")
    lines.append("|---|---:|---:|---:|---|---|")
    for r in sorted(results, key=lambda r: r["lum"]):
        a = r["accent"]
        acc = f"`{a['hex']}` ({a['share']*100:.1f}%)" if a else "-"
        tone = f"{hue_name(a['h'])} h={a['h']:.0f} s={a['s']:.2f}" if a else "-"
        lines.append(
            f"| {r['name']} | {r['lum']:.3f} | {r['sat']:.3f} | {r['dark']*100:.0f}% | {acc} | {tone} |"
        )

    lines.append("\n## Palitra po kazhdomu refu\n")
    for r in sorted(results, key=lambda r: r["name"]):
        lines.append(f"\n### {r['name']}\n")
        lines.append(f"Ishodnik {r['size'][0]}x{r['size'][1]}. Lum {r['lum']:.3f}, "
                     f"Sat {r['sat']:.3f}, tyomnogo {r['dark']*100:.0f}%.\n")
        lines.append("| HEX | Dolya | Ton | S | L |")
        lines.append("|---|---:|---|---:|---:|")
        for e in r["entries"]:
            lines.append(
                f"| `{e['hex']}` | {e['share']*100:.1f}% | {hue_name(e['h'])} {e['h']:.0f} "
                f"| {e['s']:.2f} | {e['l']:.2f} |"
            )
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description="Palette measurement for UI reference screenshots")
    ap.add_argument("--crop", help="x1,y1,x2,y2 - merit tolko etu oblast kadra")
    ap.add_argument("--files", nargs="*", help="imena fajlov; po umolchaniyu - vsya papka")
    ap.add_argument("--out", help="kuda pisat otchyot")
    args = ap.parse_args()

    crop = tuple(int(v) for v in args.crop.split(",")) if args.crop else None
    if crop and len(crop) != 4:
        print("--crop zhdyot rovno chetyre chisla: x1,y1,x2,y2", file=sys.stderr)
        return 2

    if args.files:
        paths = [REFS_DIR / f for f in args.files]
    else:
        paths = sorted(p for p in REFS_DIR.iterdir()
                       if p.suffix.lower() in (".jpg", ".jpeg", ".png"))

    if not paths:
        print(f"V {REFS_DIR} kartinok ne najdeno", file=sys.stderr)
        return 1

    results = []
    for i, p in enumerate(paths, 1):
        print(f"[{i}/{len(paths)}] {p.name}")
        r = measure(p, crop)
        if r:
            results.append(r)

    out = Path(args.out) if args.out else OUT_FILE
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render(results, crop), encoding="utf-8")
    print(f"\nGotovo: {len(results)} refov -> {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
