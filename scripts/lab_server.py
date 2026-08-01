"""Сервер локальной Лаборатории: раздача docs/lab плюс указатель по вики.

Запускается через scripts/lab-serve.ps1. Стандартная библиотека и ничего больше — сайт обязан
подниматься на голой машине через год.

Указатель по ГДД строится НА ЛЕТУ, а не лежит файлом. Причина: сайт не владеет текстом вики,
он владеет только маршрутом к нему. Сохранённый указатель — вторая копия оглавления, которая
разойдётся с диском при первом переименовании заметки и будет врать молча.
"""

from __future__ import annotations

import argparse
import json
import re
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

FRONTMATTER = re.compile(r"^---\s*\n(.*?)\n---\s*\n", re.S)
H1 = re.compile(r"^#\s+(.+)$", re.M)

# --gm-brass-500: rgb(184, 134, 59);   /* пояснение */
TOKEN = re.compile(
    r"^\s*(--[\w-]+)\s*:\s*([^;]+);(?:\s*/\*\s*(.*?)\s*\*/)?",
    re.M,
)


def read_meta(path: Path) -> dict:
    """Заголовок, статус и теги заметки. Только метаданные — тело остаётся в vault."""
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return {}

    meta: dict = {}
    head = FRONTMATTER.match(text)
    if head:
        for line in head.group(1).splitlines():
            if ":" not in line:
                continue
            key, _, value = line.partition(":")
            key = key.strip()
            value = value.strip().strip("\"'")
            if key in ("title", "status", "tags", "date"):
                if key == "tags":
                    value = [t.strip(" []'\"") for t in value.split(",") if t.strip(" []'\"")]
                meta[key] = value

    if "title" not in meta:
        found = H1.search(text)
        meta["title"] = found.group(1).strip() if found else path.stem

    meta["words"] = len(text.split())
    return meta


def build_index(wiki: Path) -> dict:
    """Дерево вики: раздел -> кластер -> заметки. Пути отдаём относительные, ссылки строит клиент."""
    notes = []
    for path in sorted(wiki.rglob("*.md")):
        rel = path.relative_to(wiki).as_posix()
        parts = rel.split("/")
        meta = read_meta(path)
        notes.append(
            {
                "path": rel,
                "vault": parts[0] if parts else "",
                "cluster": parts[1] if len(parts) > 2 else "",
                "file": parts[-1],
                "title": meta.get("title", path.stem),
                "status": meta.get("status", ""),
                "tags": meta.get("tags", []),
                "words": meta.get("words", 0),
            }
        )
    # Абсолютный путь и имя vault: по относительному Obsidian свой vault не находит и отвечает
    # «Vault not found». Клиент строит obsidian://open?path=<абсолютный файл>.
    root = wiki.resolve()
    return {"root": root.as_posix(), "vault": root.name, "count": len(notes), "notes": notes}


def build_palette(theme: Path) -> dict:
    """Токены темы как они лежат в проекте.

    Палитра читается ИЗ ПРОЕКТА, а не копируется в сайт: у цвета один владелец, а мир читает
    снимок. Скопированный список разошёлся бы с игрой на первой же правке — и врал бы молча,
    потому что визуально «примерно тот же оттенок» проверить нечем.
    """
    groups = []
    for name in ("tokens.primitives.uss", "tokens.semantic.uss"):
        path = theme / name
        if not path.exists():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue

        tokens = []
        for match in TOKEN.finditer(text):
            key, value, note = match.group(1), match.group(2).strip(), match.group(3)
            tokens.append({"name": key, "value": value, "note": note or ""})
        groups.append({"file": name, "tokens": tokens})

    return {"groups": groups}


class LabHandler(SimpleHTTPRequestHandler):
    """Раздача сайта плюс собственные маршруты, отдающие снимки проекта."""

    def __init__(self, *args, wiki: Path, theme: Path, **kwargs):
        self.wiki = wiki
        self.theme = theme
        super().__init__(*args, **kwargs)

    def do_GET(self):  # noqa: N802 — имя задано базовым классом
        route = self.path.split("?")[0]
        if route == "/api/gdd-index":
            self._json(build_index(self.wiki))
            return
        if route == "/api/palette":
            self._json(build_palette(self.theme))
            return
        super().do_GET()

    def _json(self, data: dict) -> None:
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def end_headers(self):
        # Правки в разделах должны быть видны по F5, иначе отладка превращается в спор с кешем.
        self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
        super().end_headers()

    def log_message(self, fmt, *args):
        if "/api/" in (args[0] if args else ""):
            super().log_message(fmt, *args)


def main() -> None:
    parser = argparse.ArgumentParser(description="Локальный сервер Лаборатории Guildmaster")
    parser.add_argument("--port", type=int, default=7400)
    parser.add_argument("--root", required=True, help="каталог сайта (docs/lab)")
    parser.add_argument("--wiki", required=True, help="каталог vault (docs/wiki)")
    parser.add_argument("--theme", required=True, help="каталог темы UI (Assets/_Project/UI/Theme)")
    args = parser.parse_args()

    handler = partial(LabHandler, directory=args.root, wiki=Path(args.wiki), theme=Path(args.theme))
    with ThreadingHTTPServer(("127.0.0.1", args.port), handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nЛаборатория остановлена.")


if __name__ == "__main__":
    main()
