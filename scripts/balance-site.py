#!/usr/bin/env python3
"""Собирает локальный сайт балансных отчётов из JSON-снимков SimBench.

Читает BalanceReports/*.json (их пишет ReportWriter рядом с CSV и Markdown), группирует
снимки в ПРОГОНЫ по времени и раскладывает данные по китам, режимам и корзинам метрик.
Результат — статические страницы в BalanceReports/site/, открываются файлом, без сервера.

Почему так, а не «просто таблица»: голое число балансу ничего не говорит. Смысл появляется
от сравнения — с прошлым прогоном (что сделала правка) и с классовой нормой (насколько кит
выбивается из роли). Поэтому сайт хранит ВСЮ историю и умеет сравнивать любые два прогона,
а не только соседние.

Запуск:
    python scripts/balance-site.py            # собрать сайт
    python scripts/balance-site.py --open     # собрать и открыть в браузере
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import webbrowser
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPORTS = ROOT / "BalanceReports"
SITE = REPORTS / "site"

# Снимки, попавшие в одно окно, считаются одним прогоном: бенчи запускаются из меню по
# одному, между ними проходят секунды, и склеивать их вручную было бы издевательством.
RUN_WINDOW_MINUTES = 30

# Как называются режимы в интерфейсе. Ключ — префикс kind из имени файла отчёта.
MODE_TITLES = {
    "duel": "Дуэль 1v1",
    "solo_duel": "Дуэль 1v1",
    "trio_duel": "Тройки 3v3",
    "squad_duel": "Отряды 4v4",
    "team_duel": "Команды (устар. формат)",
    "super_team_duel": "Большие команды 6v6",
    "squad_swap": "Замена в отряде 4v4",
    "pair_synergy": "Синергия пар",
    "bench_dps": "Урон (DPS)",
    "bench_survivability": "Выживаемость",
    "audit_content": "Аудит контента",
    "scenario": "Сценарий",
}

# Колонка с именем кита в каждом виде отчёта — по ней данные сшиваются между режимами.
UNIT_COLUMNS = ("Relic", "Unit", "Kit", "Name")

# Снимок классовых норм — не режим, а линейка: он не получает вкладку, а раскладывается
# в run.norms и подмешивается к числам всех прочих таблиц.
NORMS_KIND = "balance_norms"


@dataclass
class Snapshot:
    """Один отчёт одного бенча: что мерили, когда и с какими числами."""

    kind: str
    title: str
    generated_at: datetime
    notes: str
    headers: list[str]
    rows: list[list]
    path: Path

    @property
    def mode_key(self) -> str:
        """Ключ режима: `squad_duel_ranking` → `squad_duel`, `bench_dps` → `bench_dps`."""
        for suffix in ("_ranking", "_matrix"):
            if self.kind.endswith(suffix):
                return self.kind[: -len(suffix)]
        return self.kind

    @property
    def is_matrix(self) -> bool:
        return self.kind.endswith("_matrix")

    def unit_column(self) -> int | None:
        for i, h in enumerate(self.headers):
            if h in UNIT_COLUMNS:
                return i
        return None

    def by_unit(self) -> dict[str, dict[str, object]]:
        """Строки, разложенные по имени кита: {кит: {колонка: значение}}."""
        col = self.unit_column()
        if col is None:
            return {}
        out: dict[str, dict[str, object]] = {}
        for row in self.rows:
            if col >= len(row):
                continue
            name = str(row[col])
            out[name] = {h: row[i] for i, h in enumerate(self.headers) if i < len(row)}
        return out


@dataclass
class Run:
    """Прогон — все снимки, снятые примерно в одно время."""

    started: datetime
    snapshots: list[Snapshot] = field(default_factory=list)

    @property
    def key(self) -> str:
        return self.started.strftime("%Y-%m-%d %H:%M")

    def units(self) -> set[str]:
        names: set[str] = set()
        for s in self.snapshots:
            if not s.is_matrix:
                names.update(s.by_unit().keys())
        return names


def read_snapshots() -> list[Snapshot]:
    snaps: list[Snapshot] = []
    for path in sorted(REPORTS.glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (json.JSONDecodeError, OSError) as e:
            print(f"  пропущен {path.name}: {e}")
            continue

        stamp = data.get("generatedAt")
        try:
            when = datetime.fromisoformat(stamp)
        except (TypeError, ValueError):
            # Штампа нет или он битый — берём время файла, чтобы снимок не выпал из истории.
            when = datetime.fromtimestamp(path.stat().st_mtime)

        snaps.append(
            Snapshot(
                kind=data.get("kind", path.stem),
                title=data.get("title", path.stem),
                generated_at=when,
                notes=data.get("notes") or "",
                headers=[str(h) for h in data.get("headers", [])],
                rows=data.get("rows", []),
                path=path,
            )
        )
    return snaps


def group_runs(snaps: list[Snapshot]) -> list[Run]:
    """Склеить снимки в прогоны по временному окну. Новые прогоны — первыми."""
    runs: list[Run] = []
    for s in sorted(snaps, key=lambda x: x.generated_at):
        if runs and (s.generated_at - runs[-1].snapshots[-1].generated_at).total_seconds() <= RUN_WINDOW_MINUTES * 60:
            runs[-1].snapshots.append(s)
        else:
            runs.append(Run(started=s.generated_at, snapshots=[s]))

    # Внутри прогона снимки одного вида могли сняться дважды — оставляем последний.
    for run in runs:
        latest: dict[str, Snapshot] = {}
        for s in run.snapshots:
            latest[s.kind] = s
        run.snapshots = sorted(latest.values(), key=lambda x: x.kind)

    runs.reverse()
    return runs


def build_payload(runs: list[Run]) -> dict:
    """Данные для страницы: прогоны, режимы, киты и все их числа."""
    payload = {"runs": [], "modeTitles": MODE_TITLES}

    for run in runs:
        entry = {"key": run.key, "modes": {}, "matrices": {}, "notes": {}, "norms": {}, "normsNote": ""}
        for s in run.snapshots:
            if s.kind == NORMS_KIND:
                # Норм у прогона может не быть (снят до появления линейки) — тогда коридоров не
                # будет вовсе. Подставлять нормы из соседнего прогона нельзя: линейка меняется
                # вместе с классовым профилем, и чужая солгала бы про отклонение.
                entry["norms"] = s.by_unit()
                entry["normsNote"] = s.notes
                continue
            if s.is_matrix:
                entry["matrices"][s.mode_key] = {"headers": s.headers, "rows": s.rows}
                continue
            entry["modes"][s.mode_key] = {
                "title": s.title,
                "headers": s.headers,
                "units": s.by_unit(),
                # Сырые строки — для отчётов, у которых нет колонки кита (синергия пар: строка про
                # ПАРУ, а не про одного). Без них такая вкладка молча оказалась бы пустой.
                "rows": s.rows,
            }
            entry["notes"][s.mode_key] = s.notes
        payload["runs"].append(entry)

    return payload


def write_site(payload: dict) -> Path:
    SITE.mkdir(parents=True, exist_ok=True)
    (SITE / "data.js").write_text(
        "window.BALANCE_DATA = " + json.dumps(payload, ensure_ascii=False) + ";",
        encoding="utf-8",
    )

    template = Path(__file__).parent / "balance-site" / "index.html"
    shutil.copyfile(template, SITE / "index.html")
    shutil.copyfile(template.parent / "style.css", SITE / "style.css")
    shutil.copyfile(template.parent / "app.js", SITE / "app.js")
    return SITE / "index.html"


def main() -> int:
    parser = argparse.ArgumentParser(description="Сборка сайта балансных отчётов")
    parser.add_argument("--open", action="store_true", help="открыть собранный сайт в браузере")
    args = parser.parse_args()

    if not REPORTS.exists():
        print(f"Нет папки {REPORTS} — сначала прогоните бенчи (меню Alebardium/Balance).")
        return 1

    snaps = read_snapshots()
    if not snaps:
        print("JSON-снимков не найдено. Прогоните бенчи после обновления ReportWriter.")
        return 1

    runs = group_runs(snaps)
    index = write_site(build_payload(runs))

    units = len(runs[0].units()) if runs else 0
    print(f"Собрано: прогонов {len(runs)}, снимков {len(snaps)}, китов в последнем прогоне {units}")
    print(f"Сайт: {index}")

    if args.open:
        webbrowser.open(index.as_uri())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
