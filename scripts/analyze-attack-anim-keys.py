#!/usr/bin/env python3
"""Analyze Attack.anim for redundant constant/empty animation curves."""

from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path

FLOAT_RE = re.compile(r"^\s+value:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\s*$")
VEC3_RE = re.compile(
    r"^\s+value:\s*\{x:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*y:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?),\s*z:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)\}"
)
TIME_RE = re.compile(r"^\s+time:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)")


def is_constant(vals: list[float], eps: float = 1e-6) -> bool:
    if len(vals) < 2:
        return True
    base = vals[0]
    return all(abs(v - base) < eps for v in vals)


def is_constant_vec(keys: list[tuple[float, float, float]], eps: float = 1e-6) -> bool:
    if len(keys) < 2:
        return True
    base = keys[0]
    return all(max(abs(a - b) for a, b in zip(k, base)) < eps for k in keys)


def parse_scalar_block(block: list[str]) -> dict:
    path = ""
    attr = ""
    values: list[float] = []
    times: list[float] = []
    empty_curve = False
    for line in block:
        if line.strip().startswith("path:"):
            path = line.split(":", 1)[1].strip()
        if line.strip().startswith("attribute:"):
            attr = line.split(":", 1)[1].strip()
        if line.strip() == "m_Curve: []":
            empty_curve = True
        m = FLOAT_RE.match(line)
        if m:
            values.append(float(m.group(1)))
        tm = TIME_RE.match(line)
        if tm:
            times.append(float(tm.group(1)))
    return {
        "path": path,
        "attr": attr,
        "values": values,
        "times": times,
        "empty": empty_curve,
        "keys": len(values),
    }


def parse_vec_block(block: list[str]) -> dict:
    path = ""
    keys: list[tuple[float, float, float]] = []
    times: list[float] = []
    for line in block:
        if line.strip().startswith("path:"):
            path = line.split(":", 1)[1].strip()
        m = VEC3_RE.match(line)
        if m:
            keys.append(tuple(float(g) for g in m.groups()))
        tm = TIME_RE.match(line)
        if tm:
            times.append(float(tm.group(1)))
    return {"path": path, "keys": keys, "times": times, "count": len(keys)}


def analyze(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines()
    sections = {
        "m_EditorCurves:": "editor",
        "m_EulerEditorCurves:": "euler_editor",
        "m_EulerCurves:": "euler_vec",
        "m_PositionCurves:": "pos_vec",
    }
    results: dict[str, list[dict]] = defaultdict(list)
    current: str | None = None
    i = 0
    while i < len(lines):
        line = lines[i]
        for marker, name in sections.items():
            if line.strip() == marker:
                current = name
                break

        if current in ("editor", "euler_editor") and line.startswith("  - serializedVersion: 2"):
            block: list[str] = []
            while i < len(lines):
                block.append(lines[i])
                if lines[i].strip().startswith("flags:"):
                    break
                i += 1
            results[current].append(parse_scalar_block(block))
            i += 1
            continue

        if current in ("euler_vec", "pos_vec") and line.startswith("  - curve:"):
            block = []
            while i < len(lines):
                block.append(lines[i])
                if lines[i].strip().startswith("path:"):
                    break
                i += 1
            i += 1
            results[current].append(parse_vec_block(block))
            continue

        if current in ("editor", "euler_editor") and line.startswith("  m_") and "Curves:" in line:
            if line.strip() not in ("m_EditorCurves:", "m_EulerEditorCurves:"):
                current = None

        i += 1

    print(f"File: {path}")
    print()

    for kind in ("editor", "euler_editor", "euler_vec", "pos_vec"):
        items = results[kind]
        if kind in ("editor", "euler_editor"):
            empty = [x for x in items if x["empty"] or x["keys"] == 0]
            const = [x for x in items if not x["empty"] and is_constant(x["values"])]
            anim = [x for x in items if not x["empty"] and not is_constant(x["values"])]
            print(f"[{kind}] total={len(items)} animated={len(anim)} constant={len(const)} empty={len(empty)}")
        else:
            const = [x for x in items if is_constant_vec(x["keys"])]
            anim = [x for x in items if not is_constant_vec(x["keys"])]
            print(f"[{kind}] total={len(items)} animated={len(anim)} constant={len(const)}")

    print("\n=== SAFE TO REMOVE CANDIDATES ===\n")

    print("1) m_EulerEditorCurves with m_Curve: [] (Unity placeholder duplicates)")
    for x in results["euler_editor"]:
        if x["empty"] or x["keys"] == 0:
            print(f"   - {x['path']} :: {x['attr']}")

    print("\n2) Constant m_EditorCurves where value is 0 (matches prefab default)")
    for x in results["editor"]:
        if not x["empty"] and is_constant(x["values"]) and x["values"] and abs(x["values"][0]) < 1e-6:
            print(f"   - {x['path']} :: {x['attr']} ({x['keys']} keys)")

    print("\n3) Constant m_EulerCurves / m_PositionCurves vec blocks (all keys identical)")
    for kind, label in (("euler_vec", "rotation"), ("pos_vec", "position")):
        for x in results[kind]:
            if is_constant_vec(x["keys"]):
                k0 = x["keys"][0] if x["keys"] else (0.0, 0.0, 0.0)
                print(f"   - [{label}] {x['path']} :: {k0} ({x['count']} keys)")

    print("\n4) Constant m_EditorCurves with NON-ZERO rest offset (prefab already holds value)")
    for x in results["editor"]:
        if not x["empty"] and is_constant(x["values"]) and x["values"] and abs(x["values"][0]) >= 1e-6:
            print(f"   - {x['path']} :: {x['attr']} = {x['values'][0]} ({x['keys']} keys)")

    print("\n=== DUPLICATE CHANNELS (same path+axis in multiple sections) ===\n")
    editor_by_path_attr = {(x["path"], x["attr"]): x for x in results["editor"] if not x["empty"]}
    for x in results["euler_vec"]:
        if not is_constant_vec(x["keys"]):
            print(f"   animated euler_vec: {x['path']} (also check editor localEulerAnglesRaw.z)")
    for x in results["pos_vec"]:
        if not is_constant_vec(x["keys"]):
            print(f"   animated pos_vec: {x['path']}")

    print("\n=== DISCUSS BEFORE REMOVING ===\n")
    print("Animated editor scalar curves:")
    for x in results["editor"]:
        if x["empty"] or is_constant(x["values"]):
            continue
        vmin, vmax = min(x["values"]), max(x["values"])
        print(f"   - {x['path']} :: {x['attr']} range {vmin:.3f}..{vmax:.3f} ({x['keys']} keys)")

    print("\nAnimated euler_vec blocks:")
    for x in results["euler_vec"]:
        if is_constant_vec(x["keys"]):
            continue
        zs = [k[2] for k in x["keys"]]
        print(f"   - {x['path']} z {min(zs):.2f}..{max(zs):.2f} ({x['count']} keys)")

    print("\nAnimated pos_vec blocks:")
    for x in results["pos_vec"]:
        if is_constant_vec(x["keys"]):
            continue
        print(f"   - {x['path']} start={x['keys'][0]} end={x['keys'][-1]} ({x['count']} keys)")

    # Keyframe density on animated curves
    print("\n=== KEYFRAME DENSITY (animated only) ===\n")
    for x in results["editor"]:
        if x["empty"] or is_constant(x["values"]):
            continue
        print(f"   {x['path']} :: {x['attr']} times={', '.join(f'{t:.3f}' for t in x['times'])}")


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    target = root / "Assets/_Project/Prefabs/Bones/Attack.anim"
    if len(sys.argv) > 1:
        target = Path(sys.argv[1])
    analyze(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
