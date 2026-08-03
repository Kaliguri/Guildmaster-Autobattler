#!/usr/bin/env python3
"""Offset left-leg animation keys to match BoneUnit_Standart prefab rest-pose change."""

from __future__ import annotations

import re
import sys
from pathlib import Path

# Old rest pose (pre-repose) -> new rest pose deltas from prefab diff.
LEG_LEFT_POS_DX = -0.0008
LEG_LEFT_POS_DY = 0.0242

LEG_BOOTS_POS_DX = -0.0167
LEG_BOOTS_POS_DY = 0.0025
LEG_BOOTS_ROT_DZ = -5.4

LEG_DOWN_ROT_DZ = -16.49

FLOAT_RE = re.compile(r"^(\s*)value:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\s*$")
VEC3_RE = re.compile(
    r"^(\s*)value:\s*\{x:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*y:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*z:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}\s*$"
)


def fmt_float(value: float) -> str:
    text = f"{value:.7f}".rstrip("0").rstrip(".")
    if text == "-0":
        return "0"
    return text


def strip_eol(line: str) -> tuple[str, str]:
    if line.endswith("\r\n"):
        return line[:-2], "\r\n"
    if line.endswith("\n"):
        return line[:-1], "\n"
    return line, ""


def offset_float_line(line: str, delta: float) -> str:
    core, eol = strip_eol(line)
    match = FLOAT_RE.match(core)
    if not match:
        return line
    indent, raw = match.groups()
    value = float(raw) + delta
    return f"{indent}value: {fmt_float(value)}{eol}"


def offset_vec3_line(line: str, dx: float, dy: float, dz: float = 0.0) -> str:
    core, eol = strip_eol(line)
    match = VEC3_RE.match(core)
    if not match:
        return line
    indent, x, y, z = match.groups()
    nx = float(x) + dx
    ny = float(y) + dy
    nz = float(z) + dz
    return f"{indent}value: {{x: {fmt_float(nx)}, y: {fmt_float(ny)}, z: {fmt_float(nz)}}}{eol}"


def process_editor_curves(lines: list[str]) -> list[str]:
    out: list[str] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        if line.strip() == "m_EditorCurves:":
            i += 1
            while i < len(lines) and not lines[i].startswith("  m_EulerEditorCurves:"):
                block_start = i
                block: list[str] = []
                while i < len(lines):
                    block.append(lines[i])
                    if lines[i].strip().startswith("flags:"):
                        break
                    i += 1
                i += 1

                path = ""
                attribute = ""
                for bline in block:
                    if bline.strip().startswith("path:"):
                        path = bline.split("path:", 1)[1].strip()
                    if bline.strip().startswith("attribute:"):
                        attribute = bline.split("attribute:", 1)[1].strip()

                processed = block[:]
                if path == "Leg (Left)" and attribute in {
                    "m_LocalPosition.x",
                    "m_LocalPosition.y",
                }:
                    delta = LEG_LEFT_POS_DX if attribute.endswith(".x") else LEG_LEFT_POS_DY
                    processed = [offset_float_line(b, delta) if "value:" in b else b for b in processed]
                elif path == "Leg (Left)/Rotation Point/Leg_Boots":
                    if attribute == "m_LocalPosition.x":
                        processed = [offset_float_line(b, LEG_BOOTS_POS_DX) if "value:" in b else b for b in processed]
                    elif attribute == "m_LocalPosition.y":
                        processed = [offset_float_line(b, LEG_BOOTS_POS_DY) if "value:" in b else b for b in processed]
                    elif attribute in {"localEulerAnglesRaw.z", "m_LocalEulerAngles.z"}:
                        processed = [offset_float_line(b, LEG_BOOTS_ROT_DZ) if "value:" in b else b for b in processed]
                elif path == "Leg (Left)/Rotation Point/Leg_Down" and attribute in {
                    "localEulerAnglesRaw.z",
                    "m_LocalEulerAngles.z",
                }:
                    processed = [offset_float_line(b, LEG_DOWN_ROT_DZ) if "value:" in b else b for b in processed]

                out.extend(processed)
            continue
        i += 1
    return out


def process_vector_curve_sections(lines: list[str], section_name: str) -> list[str]:
    out: list[str] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        if line.strip() == f"{section_name}:":
            i += 1
            while i < len(lines) and lines[i].startswith("  - curve:"):
                block: list[str] = []
                while i < len(lines):
                    block.append(lines[i])
                    if lines[i].strip().startswith("path:"):
                        break
                    i += 1
                i += 1

                path = ""
                for bline in block:
                    if bline.strip().startswith("path:"):
                        path = bline.split("path:", 1)[1].strip()
                        break

                processed = block[:]
                if path == "Leg (Left)":
                    processed = [
                        offset_vec3_line(b, LEG_LEFT_POS_DX, LEG_LEFT_POS_DY) if "value:" in b else b
                        for b in processed
                    ]
                elif path == "Leg (Left)/Rotation Point/Leg_Boots":
                    if section_name == "m_PositionCurves":
                        processed = [
                            offset_vec3_line(b, LEG_BOOTS_POS_DX, LEG_BOOTS_POS_DY) if "value:" in b else b
                            for b in processed
                        ]
                    else:
                        processed = [
                            offset_vec3_line(b, 0.0, 0.0, LEG_BOOTS_ROT_DZ) if "value:" in b else b
                            for b in processed
                        ]
                elif path == "Leg (Left)/Rotation Point/Leg_Down" and section_name == "m_EulerCurves":
                    processed = [
                        offset_vec3_line(b, 0.0, 0.0, LEG_DOWN_ROT_DZ) if "value:" in b else b
                        for b in processed
                    ]

                out.extend(processed)
            continue
        i += 1
    return out


def process_anim(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    lines = process_vector_curve_sections(lines, "m_EulerCurves")
    lines = process_vector_curve_sections(lines, "m_PositionCurves")
    lines = process_editor_curves(lines)
    path.write_text("".join(lines), encoding="utf-8")
    print(f"Updated {path}")


def main(argv: list[str]) -> int:
    root = Path(__file__).resolve().parents[1]
    targets = [Path(p) if Path(p).is_absolute() else root / p for p in argv[1:]]
    if not targets:
        targets = [
            root / "Assets/_Project/Prefabs/Bones/Attack.anim",
            root / "Assets/_Project/Prefabs/Bones/Attack_processed.anim",
        ]
    for target in targets:
        process_anim(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
