#!/usr/bin/env python3
"""Reduce leg motion in Attack.anim: drop noisy leg curves, soften Rotation Point swing."""

from __future__ import annotations

import re
import sys
from pathlib import Path

SCALE_FACTOR = 0.35

REMOVE_POSITION_PATHS = {
    "Leg (Left)",
    "Leg (Right)",
    "Leg (Right)/Rotation Point/Leg_Down",
    "Leg (Right)/Rotation Point/Leg_Boots",
    "Leg (Left)/Rotation Point/Leg_Boots",
}

REMOVE_EULER_PATHS = {
    "Leg (Left)",
    "Leg (Left)/Rotation Point/Leg_Boots",
}

ROTATION_POINT_PATHS = {
    "Leg (Left)/Rotation Point",
    "Leg (Right)/Rotation Point",
}

FLOAT_RE = re.compile(r"^(\s*)value:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\s*$")
SLOPE_FLOAT_RE = re.compile(
    r"^(\s*)(inSlope|outSlope):\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\s*$"
)
VEC3_RE = re.compile(
    r"^(\s*)value:\s*\{x:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*y:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*z:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}\s*$"
)
VEC3_SLOPE_RE = re.compile(
    r"^(\s*)(inSlope|outSlope):\s*\{x:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*y:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*z:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}\s*$"
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


def scale_toward_rest(value: float, rest: float, factor: float) -> float:
    return rest + (value - rest) * factor


def scale_scalar_block(block: list[str], factor: float) -> list[str]:
    rest: float | None = None
    for line in block:
        core, _ = strip_eol(line)
        match = FLOAT_RE.match(core)
        if match:
            rest = float(match.group(2))
            break
    if rest is None:
        return block

    out: list[str] = []
    for line in block:
        core, eol = strip_eol(line)
        value_match = FLOAT_RE.match(core)
        if value_match:
            indent, raw = value_match.groups()
            scaled = scale_toward_rest(float(raw), rest, factor)
            out.append(f"{indent}value: {fmt_float(scaled)}{eol}")
            continue
        slope_match = SLOPE_FLOAT_RE.match(core)
        if slope_match:
            indent, name, raw = slope_match.groups()
            scaled = float(raw) * factor
            out.append(f"{indent}{name}: {fmt_float(scaled)}{eol}")
            continue
        out.append(line)
    return out


def scale_vec3_block(block: list[str], factor: float) -> list[str]:
    rest_z: float | None = None
    for line in block:
        core, _ = strip_eol(line)
        match = VEC3_RE.match(core)
        if match:
            rest_z = float(match.group(4))
            break
    if rest_z is None:
        return block

    out: list[str] = []
    for line in block:
        core, eol = strip_eol(line)
        value_match = VEC3_RE.match(core)
        if value_match:
            indent, x, y, z = value_match.groups()
            nz = scale_toward_rest(float(z), rest_z, factor)
            out.append(
                f"{indent}value: {{x: {x}, y: {y}, z: {fmt_float(nz)}}}{eol}"
            )
            continue
        slope_match = VEC3_SLOPE_RE.match(core)
        if slope_match:
            indent, name, x, y, z = slope_match.groups()
            nz = float(z) * factor
            out.append(
                f"{indent}{name}: {{x: {x}, y: {y}, z: {fmt_float(nz)}}}{eol}"
            )
            continue
        out.append(line)
    return out


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


def should_remove_vector_block(section_name: str, path: str) -> bool:
    if section_name == "m_PositionCurves":
        return path in REMOVE_POSITION_PATHS
    if section_name == "m_EulerCurves":
        return path in REMOVE_EULER_PATHS
    return False


def should_scale_vector_block(section_name: str, path: str) -> bool:
    return section_name == "m_EulerCurves" and path in ROTATION_POINT_PATHS


def should_remove_editor_block(path: str, attribute: str) -> bool:
    if path in REMOVE_POSITION_PATHS and attribute.startswith("m_LocalPosition."):
        return True
    if path == "Leg (Left)/Rotation Point/Leg_Boots" and (
        attribute.startswith("localEulerAnglesRaw.")
        or attribute.startswith("m_LocalEulerAngles.")
    ):
        return True
    if path == "Leg (Left)" and attribute == "localEulerAnglesRaw.z":
        return True
    return False


def should_scale_editor_block(path: str, attribute: str) -> bool:
    return path in ROTATION_POINT_PATHS and attribute in {
        "localEulerAnglesRaw.z",
        "m_LocalEulerAngles.z",
    }


def process_vector_sections(lines: list[str], section_name: str) -> tuple[list[str], int, int]:
  removed = 0
  scaled = 0
  out: list[str] = []
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

      path = parse_path_from_block(block)
      if should_remove_vector_block(section_name, path):
        removed += 1
        continue
      if should_scale_vector_block(section_name, path):
        block = scale_vec3_block(block, SCALE_FACTOR)
        scaled += 1
      out.extend(block)
  return out, removed, scaled


def process_editor_curves(lines: list[str]) -> tuple[list[str], int, int]:
  removed = 0
  scaled = 0
  out: list[str] = []
  i = 0
  while i < len(lines):
    line = lines[i]
    out.append(line)
    if line.strip() != "m_EditorCurves:":
      i += 1
      continue

    i += 1
    while i < len(lines) and not lines[i].startswith("  m_EulerEditorCurves:"):
      block: list[str] = []
      while i < len(lines):
        block.append(lines[i])
        if lines[i].strip().startswith("flags:"):
          break
        i += 1
      i += 1

      path = parse_path_from_block(block)
      attribute = parse_attribute_from_block(block)
      if should_remove_editor_block(path, attribute):
        removed += 1
        continue
      if should_scale_editor_block(path, attribute):
        block = scale_scalar_block(block, SCALE_FACTOR)
        scaled += 1
      out.extend(block)
  return out, removed, scaled


def process_anim(path: Path) -> None:
  lines = path.read_text(encoding="utf-8").splitlines(keepends=True)

  lines, pos_removed, _ = process_vector_sections(lines, "m_PositionCurves")
  lines, euler_removed, euler_scaled = process_vector_sections(lines, "m_EulerCurves")
  lines, editor_removed, editor_scaled = process_editor_curves(lines)

  path.write_text("".join(lines), encoding="utf-8")
  print(
    f"Updated {path}: "
    f"removed {pos_removed} position blocks, "
    f"{euler_removed} euler blocks, "
    f"{editor_removed} editor blocks; "
    f"scaled {euler_scaled} euler + {editor_scaled} editor rotation-point curves "
    f"at {SCALE_FACTOR:.0%}"
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
