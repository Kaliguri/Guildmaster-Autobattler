#!/usr/bin/env python3
"""Phase-1 Attack.anim cleanup: drop empty euler-editor stubs and constant-no-op curves."""

from __future__ import annotations

import re
import sys
from pathlib import Path

EPS = 1e-6

FLOAT_RE = re.compile(r"^\s+value:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\s*$")
VEC3_RE = re.compile(
    r"^\s+value:\s*\{x:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*y:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*z:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}"
)


def parse_scalar_values(block: list[str]) -> list[float]:
    values: list[float] = []
    for line in block:
        match = FLOAT_RE.match(line)
        if match:
            values.append(float(match.group(1)))
    return values


def parse_vec3_keys(block: list[str]) -> list[tuple[float, float, float]]:
    keys: list[tuple[float, float, float]] = []
    for line in block:
        match = VEC3_RE.match(line)
        if match:
            keys.append(tuple(float(g) for g in match.groups()))
    return keys


def is_constant(values: list[float]) -> bool:
    if len(values) < 2:
        return True
    base = values[0]
    return all(abs(v - base) < EPS for v in values)


def is_constant_vec(keys: list[tuple[float, float, float]]) -> bool:
    if len(keys) < 2:
        return True
    base = keys[0]
    return all(max(abs(a - b) for a, b in zip(key, base)) < EPS for key in keys)


def is_constant_zero(values: list[float]) -> bool:
    return is_constant(values) and values and all(abs(v) < EPS for v in values)


def parse_path_from_block(block: list[str]) -> str:
    for line in block:
        if line.strip().startswith("path:"):
            return line.split("path:", 1)[1].strip()
    return ""


def parse_attribute_from_block(block: list[str]) -> str:
    for line in block:
        if line.strip().startswith("attribute:"):
            return line.split("attribute:", 1)[1].strip()
    return ""


def remove_euler_editor_curves(lines: list[str]) -> tuple[list[str], int]:
    out: list[str] = []
    removed = 0
    i = 0
    while i < len(lines):
        if lines[i].strip() == "m_EulerEditorCurves:":
            i += 1
            while i < len(lines) and not lines[i].startswith("  m_HasGenericRootTransform:"):
                if lines[i].startswith("  - serializedVersion: 2"):
                    removed += 1
                i += 1
            continue
        out.append(lines[i])
        i += 1
    return out, removed


def process_editor_curves(lines: list[str]) -> tuple[list[str], int]:
    out: list[str] = []
    removed = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        if line.strip() != "m_EditorCurves:":
            i += 1
            continue

        i += 1
        while i < len(lines):
            if lines[i].startswith("  m_EulerEditorCurves:") or lines[i].startswith(
                "  m_HasGenericRootTransform:"
            ):
                break
            if not lines[i].startswith("  - serializedVersion: 2"):
                out.append(lines[i])
                i += 1
                continue

            block: list[str] = []
            while i < len(lines):
                block.append(lines[i])
                if lines[i].strip().startswith("flags:"):
                    break
                i += 1
            i += 1

            values = parse_scalar_values(block)
            if is_constant_zero(values):
                removed += 1
                continue
            out.extend(block)
    return out, removed


def process_vector_sections(lines: list[str], section_name: str) -> tuple[list[str], int]:
    out: list[str] = []
    removed = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        if line.strip() != f"{section_name}:":
            i += 1
            continue

        i += 1
        while i < len(lines) and lines[i].startswith("  - curve:"):
            block: list[str] = []
            while i < len(lines):
                block.append(lines[i])
                if lines[i].strip().startswith("path:"):
                    break
                i += 1
            i += 1

            keys = parse_vec3_keys(block)
            if is_constant_vec(keys):
                removed += 1
                continue
            out.extend(block)
    return out, removed


def process_anim(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)

    lines, editor_removed = process_editor_curves(lines)
    lines, euler_removed = process_vector_sections(lines, "m_EulerCurves")
    lines, pos_removed = process_vector_sections(lines, "m_PositionCurves")
    lines, euler_editor_removed = remove_euler_editor_curves(lines)

    path.write_text("".join(lines), encoding="utf-8")
    print(
        f"Updated {path}: removed "
        f"{euler_editor_removed} empty m_EulerEditorCurves blocks, "
        f"{editor_removed} constant-zero m_EditorCurves blocks, "
        f"{euler_removed} constant m_EulerCurves blocks, "
        f"{pos_removed} constant m_PositionCurves blocks"
    )


def main(argv: list[str]) -> int:
    root = Path(__file__).resolve().parents[1]
    targets = [Path(p) if Path(p).is_absolute() else root / p for p in argv[1:]]
    if not targets:
        targets = [root / "Assets/_Project/Prefabs/Bones/Attack.anim"]
    for target in targets:
        process_anim(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
