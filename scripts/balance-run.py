#!/usr/bin/env python3
"""Маркеры прогонов баланса — превращают набор снимков в мини-коммит с названием.

Прогон объявляется ДО прогона бенчей: всё, что снято после маркера, принадлежит ему, пока не
объявлен следующий. Раньше снимки слипались по 30-минутному окну, и две правки подряд оказывались
одним безымянным «прогоном» — сравнивать было нечего с чем.

    python scripts/balance-run.py start "Ослабили Друида" "BAL-001, вариант 2: перевод в Дальника"
    python scripts/balance-run.py rename "Новое название"   # поправить последний маркер
    python scripts/balance-run.py list

Маркеры лежат в BalanceReports/runs.json и версионируются вместе с реестром проблем: история
«что пробовали» ценнее самих чисел, которые всегда можно перемерить.
"""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPORTS = ROOT / "BalanceReports"
MARKERS = REPORTS / "runs.json"


def load() -> list[dict]:
    if not MARKERS.exists():
        return []
    try:
        data = json.loads(MARKERS.read_text(encoding="utf-8-sig"))
    except (json.JSONDecodeError, OSError) as e:
        # Битый файл маркеров молча не переписываем: в нём история заходов, а не кэш.
        raise SystemExit(f"Не читается {MARKERS}: {e}")
    return data if isinstance(data, list) else []


def save(markers: list[dict]) -> None:
    REPORTS.mkdir(parents=True, exist_ok=True)
    MARKERS.write_text(json.dumps(markers, ensure_ascii=False, indent=2), encoding="utf-8")


def cmd_start(title: str, summary: str, at: str | None) -> int:
    markers = load()
    # --at нужен, когда маркер забыли поставить ДО бенчей: иначе уже снятые снимки останутся
    # безымянными навсегда, а приписывать их следующему прогону было бы враньём.
    started = datetime.fromisoformat(at) if at else datetime.now().replace(microsecond=0)
    markers.append({
        "started": started.isoformat(),
        "title": title,
        "summary": summary or "",
    })
    markers.sort(key=lambda m: m.get("started", ""))
    save(markers)
    print(f"Прогон открыт: {title}")
    return 0


def cmd_rename(title: str, summary: str) -> int:
    markers = load()
    if not markers:
        print("Маркеров нет — сначала открой прогон командой start.")
        return 1
    markers[-1]["title"] = title
    if summary:
        markers[-1]["summary"] = summary
    save(markers)
    print(f"Последний прогон теперь: {title}")
    return 0


def cmd_list() -> int:
    markers = load()
    if not markers:
        print("Маркеров нет.")
        return 0
    for m in markers:
        print(f"{m.get('started', '?')}  {m.get('title', '(без названия)')}")
        if m.get("summary"):
            print(f"    {m['summary']}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Маркеры прогонов баланса")
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_start = sub.add_parser("start", help="открыть новый прогон")
    p_start.add_argument("title")
    p_start.add_argument("summary", nargs="?", default="")
    p_start.add_argument("--at", help="время начала (ISO), если маркер ставится задним числом")

    p_rename = sub.add_parser("rename", help="переименовать последний прогон")
    p_rename.add_argument("title")
    p_rename.add_argument("summary", nargs="?", default="")

    sub.add_parser("list", help="показать маркеры")

    args = parser.parse_args()
    if args.cmd == "start":
        return cmd_start(args.title, args.summary, args.at)
    if args.cmd == "rename":
        return cmd_rename(args.title, args.summary)
    return cmd_list()


if __name__ == "__main__":
    raise SystemExit(main())
