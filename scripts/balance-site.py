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
    "encounter_kits": "Энкаунтеры — цена боя (PvE)",
    "encounter_difficulty": "Энкаунтеры — сложность боёв",
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

# Справочные снимки: кто этот кит и что он умеет. Тоже не режимы — они не меряют, а называют.
CARDS_KIND = "content_cards"
ABILITIES_KIND = "content_abilities"

# Реестр проблем — единственный источник правды о том, что решено и что ждёт вердикта.
ISSUES_DOC = ROOT / "docs" / "balance-issues.md"

# Маркеры прогонов (см. scripts/balance-run.py) лежат рядом с отчётами, но отчётом не являются.
MARKERS_FILE = "runs.json"


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
    """Прогон — мини-коммит: снимки одного захода плюс название и что в нём меняли."""

    started: datetime
    snapshots: list[Snapshot] = field(default_factory=list)
    title: str = ""
    summary: str = ""

    @property
    def key(self) -> str:
        stamp = self.started.strftime("%Y-%m-%d %H:%M")
        return f"{stamp} · {self.title}" if self.title else stamp

    def units(self) -> set[str]:
        names: set[str] = set()
        for s in self.snapshots:
            if not s.is_matrix:
                names.update(s.by_unit().keys())
        return names


def read_snapshots() -> list[Snapshot]:
    snaps: list[Snapshot] = []
    for path in sorted(REPORTS.glob("*.json")):
        if path.name == MARKERS_FILE:
            continue   # это маркеры прогонов, а не отчёт бенча
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


def read_markers() -> list[dict]:
    """Маркеры прогонов из runs.json (их ставит scripts/balance-run.py). Отсортированы по времени."""
    path = REPORTS / MARKERS_FILE
    if not path.exists():
        return []
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (json.JSONDecodeError, OSError) as e:
        print(f"  маркеры прогонов не прочитались ({e}) — группирую по времени")
        return []

    out = []
    for m in data if isinstance(data, list) else []:
        try:
            out.append({
                "started": datetime.fromisoformat(m["started"]),
                "title": m.get("title", ""),
                "summary": m.get("summary", ""),
            })
        except (KeyError, TypeError, ValueError):
            continue
    out.sort(key=lambda m: m["started"])
    return out


def group_runs(snaps: list[Snapshot], markers: list[dict] | None = None) -> list[Run]:
    """
    Склеить снимки в прогоны. Новые прогоны — первыми.

    Границу задаёт МАРКЕР: снимок принадлежит последнему маркеру, поставленному до него. Временное
    окно осталось запасным путём для снимков, снятых до того, как маркеры появились, — задним числом
    приписывать им чужое название нельзя, они честно остаются безымянными.
    """
    markers = markers or []
    runs: list[Run] = []
    current_marker: dict | None = None

    for s in sorted(snaps, key=lambda x: x.generated_at):
        marker = None
        for m in markers:
            if m["started"] <= s.generated_at:
                marker = m
            else:
                break

        same_run = (
            runs
            and marker is current_marker
            and (marker is not None
                 or (s.generated_at - runs[-1].snapshots[-1].generated_at).total_seconds()
                 <= RUN_WINDOW_MINUTES * 60)
        )
        if same_run:
            runs[-1].snapshots.append(s)
        else:
            runs.append(Run(
                started=marker["started"] if marker else s.generated_at,
                snapshots=[s],
                title=marker["title"] if marker else "",
                summary=marker["summary"] if marker else "",
            ))
        current_marker = marker

    # Внутри прогона снимки одного вида могли сняться дважды — оставляем последний.
    for run in runs:
        latest: dict[str, Snapshot] = {}
        for s in run.snapshots:
            latest[s.kind] = s
        run.snapshots = sorted(latest.values(), key=lambda x: x.kind)

    runs.reverse()
    return runs


def read_issues() -> list[dict]:
    """
    Разобрать реестр проблем в структуру для страницы.

    Парсер знает ровно тот формат, которым реестр и пишется (### BAL-001 · заголовок, затем
    размеченные блоки). Держать проблемы вторым файлом «специально для сайта» нельзя: у факта
    один владелец, и это markdown, который правит Макс.
    """
    if not ISSUES_DOC.exists():
        return []

    text = ISSUES_DOC.read_text(encoding="utf-8")
    issues: list[dict] = []
    section = ""

    # Режем по заголовкам записей, попутно запоминая раздел (## Доминаторы и т.п.).
    for chunk in re.split(r"^### ", text, flags=re.M)[1:]:
        head, _, body = chunk.partition("\n")
        code, _, title = head.partition("·")

        # Раздел, в котором лежит запись: последний ## перед ней.
        before = text.split("### " + head)[0]
        sections = re.findall(r"^## (.+)$", before, flags=re.M)
        if sections:
            section = sections[-1].strip()

        # Хвост записи после последнего блока — следующий раздел, он не наш.
        body = re.split(r"^## ", body, flags=re.M)[0]

        issues.append({
            "code": code.strip(),
            "title": title.strip(),
            "section": section,
            "status": _field(body, "Статус"),
            "symptom": _block(body, "Симптом"),
            "diagnosis": _block(body, "Диагноз"),
            # Что показал последний прогон по ЭТОЙ записи. Живёт в самой записи, а не сводной
            # таблицей: сводку страница не рендерит, и ревизия оставалась невидимой (03.08).
            "recheck": _block(body, "Перемер"),
            "options": _options(body),
            "verdict": _field(body, "Вердикт Макса"),
        })
    return issues


def _field(body: str, name: str) -> str:
    """Однострочное поле вида **Статус:** открыта."""
    m = re.search(r"\*\*" + re.escape(name) + r":\*\*\s*(.+)", body)
    return m.group(1).strip() if m else ""


def _block(body: str, name: str) -> str:
    """Абзац вида **Симптом.** текст до следующего жирного заголовка."""
    m = re.search(r"\*\*" + re.escape(name) + r"\.\*\*\s*(.+?)(?=\n\*\*|\n---|\Z)", body, flags=re.S)
    return _clean(m.group(1)) if m else ""


def _options(body: str) -> list[str]:
    """Нумерованный список вариантов правки из блока **Варианты.**"""
    m = re.search(r"\*\*Варианты\.\*\*\s*(.+?)(?=\n\*\*Вердикт|\n---|\Z)", body, flags=re.S)
    if not m:
        return []
    items = re.split(r"^\d+\.\s+", m.group(1), flags=re.M)[1:]
    return [_clean(i) for i in items if i.strip()]


def _clean(text: str) -> str:
    """Схлопнуть переносы строк markdown — на странице абзац рисуется целиком."""
    return re.sub(r"\s*\n\s*", " ", text).strip()


def build_payload(runs: list[Run]) -> dict:
    """Данные для страницы: прогоны, режимы, киты и все их числа."""
    payload = {"runs": [], "modeTitles": MODE_TITLES, "issues": read_issues()}

    for run in runs:
        entry = {
            "key": run.key, "title": run.title, "summary": run.summary,
            "modes": {}, "matrices": {}, "notes": {}, "norms": {}, "normsNote": "",
            "cards": {}, "abilities": {},
        }
        for s in run.snapshots:
            if s.kind == CARDS_KIND:
                entry["cards"] = s.by_unit()
                continue
            if s.kind == ABILITIES_KIND:
                # Строк на кита несколько (по одной на способность) — by_unit() схлопнул бы их в одну.
                for row in s.rows:
                    row_map = {h: row[i] for i, h in enumerate(s.headers) if i < len(row)}
                    entry["abilities"].setdefault(str(row_map.get("Relic", "")), []).append(row_map)
                continue
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

    runs = group_runs(snaps, read_markers())
    index = write_site(build_payload(runs))

    units = len(runs[0].units()) if runs else 0
    print(f"Собрано: прогонов {len(runs)}, снимков {len(snaps)}, китов в последнем прогоне {units}")
    print(f"Сайт: {index}")

    if args.open:
        webbrowser.open(index.as_uri())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
