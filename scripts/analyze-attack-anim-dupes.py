#!/usr/bin/env python3
"""Compare m_EulerCurves/m_PositionCurves vs m_EditorCurves duplication in Attack.anim."""

from __future__ import annotations

import re
from pathlib import Path

path = Path(r"C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Prefabs/Bones/Attack.anim")
text = path.read_text(encoding="utf-8")

# Parse editor curves into map[(path, attr)] -> values
editor: dict[tuple[str, str], list[float]] = {}
for block in re.finditer(
    r"- serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve:(.*?)    attribute: ([^\n]+)\n    path: ([^\n]+)",
    text,
    re.S,
):
    body, attr, bone_path = block.group(1), block.group(2).strip(), block.group(3).strip()
    if "m_Curve: []" in body:
        continue
    vals = [float(v) for v in re.findall(r"value: (-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)", body)]
    if vals:
        editor[(bone_path, attr)] = vals

# Parse euler vec z per path
euler_z: dict[str, list[float]] = {}
for block in re.finditer(
    r"- curve:\n      serializedVersion: 2\n      m_Curve:(.*?)    path: ([^\n]+)",
    text.split("m_PositionCurves:", 1)[0].split("m_EulerCurves:", 1)[1],
    re.S,
):
    body, bone_path = block.group(1), block.group(2).strip()
    zs = [float(z) for z in re.findall(r"value: \{x: [^,]+, y: [^,]+, z: (-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)", body)]
    if zs:
        euler_z[bone_path] = zs

print("Rotation duplication (m_EulerCurves.z vs editor localEulerAnglesRaw.z):")
for bone_path, zs in sorted(euler_z.items()):
    ed = editor.get((bone_path, "localEulerAnglesRaw.z"))
    if ed is None:
        print(f"  ONLY euler_vec: {bone_path} z={zs}")
        continue
    same = len(zs) == len(ed) and all(abs(a - b) < 0.02 for a, b in zip(zs, ed))
    tag = "DUPLICATE" if same else "MISMATCH"
    print(f"  {tag}: {bone_path} keys={len(zs)}")

print("\nEditor-only animated rotation (no m_EulerCurves block):")
for (bone_path, attr), vals in sorted(editor.items()):
    if attr != "localEulerAnglesRaw.z":
        continue
    if bone_path not in euler_z:
        print(f"  {bone_path} range {min(vals):.2f}..{max(vals):.2f}")

# Count removable keys
const_zero = sum(1 for (_, _), vals in editor.items() if len(vals) >= 2 and max(abs(v) for v in vals) < 1e-6)
const_nonzero = sum(
    1 for (_, _), vals in editor.items() if len(vals) >= 2 and max(abs(v - vals[0]) for v in vals) < 1e-6 and abs(vals[0]) >= 1e-6
)
animated = sum(
    1
    for (_, _), vals in editor.items()
    if len(vals) >= 2 and max(abs(v - vals[0]) for v in vals) >= 1e-6
)
empty_euler_editor = text.count("m_Curve: []")

print(f"\nEditor curves: animated={animated} constant_zero={const_zero} constant_nonzero={const_nonzero}")
print(f"Empty m_EulerEditorCurves blocks: {empty_euler_editor}")
print(f"Total editor keyframes: {sum(len(v) for v in editor.values())}")

# Constant vec blocks in euler/position sections
const_euler = 0
const_pos = 0
euler_section = text.split("m_EulerCurves:", 1)[1].split("m_PositionCurves:", 1)[0]
pos_section = text.split("m_PositionCurves:", 1)[1].split("m_ScaleCurves:", 1)[0]
for block in re.finditer(r"- curve:\n      serializedVersion: 2\n      m_Curve:(.*?)    path: ([^\n]+)", euler_section, re.S):
    vals = re.findall(r"value: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", block.group(1))
    if vals and all(v == vals[0] for v in vals):
        const_euler += 1
for block in re.finditer(r"- curve:\n      serializedVersion: 2\n      m_Curve:(.*?)    path: ([^\n]+)", pos_section, re.S):
    vals = re.findall(r"value: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", block.group(1))
    if vals and all(v == vals[0] for v in vals):
        const_pos += 1
print(f"Constant m_EulerCurves blocks: {const_euler}")
print(f"Constant m_PositionCurves blocks: {const_pos}")

print("\nPosition duplication (m_PositionCurves vs editor m_LocalPosition.*):")
for block in re.finditer(
    r"- curve:\n      serializedVersion: 2\n      m_Curve:(.*?)    path: ([^\n]+)", pos_section, re.S
):
    body, bone_path = block.group(1), block.group(2).strip()
    keys = re.findall(
        r"value: \{x: (-?\d+(?:\.\d+)?(?:e[+-]?\d+)?), y: (-?\d+(?:\.\d+)?(?:e[+-]?\d+)?), z: (-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}",
        body,
    )
    if not keys:
        continue
    tuples = [(float(a), float(b), float(c)) for a, b, c in keys]
    xs, ys, zs = zip(*tuples)
    for axis, arr, attr in [
        ("x", xs, "m_LocalPosition.x"),
        ("y", ys, "m_LocalPosition.y"),
        ("z", zs, "m_LocalPosition.z"),
    ]:
        ed = editor.get((bone_path, attr))
        spread = max(abs(v - arr[0]) for v in arr)
        if spread < 1e-6:
            continue
        if ed is None:
            print(f"  ONLY pos_vec: {bone_path}.{axis}")
            continue
        same = len(arr) == len(ed) and all(abs(a - b) < 0.02 for a, b in zip(arr, ed))
        print(f"  {'DUPLICATE' if same else 'MISMATCH'}: {bone_path}.{axis}")
