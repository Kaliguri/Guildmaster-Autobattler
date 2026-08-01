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
    return {"root": wiki.as_posix(), "count": len(notes), "notes": notes}


class LabHandler(SimpleHTTPRequestHandler):
    """Раздача сайта плюс единственный собственный маршрут."""

    def __init__(self, *args, wiki: Path, **kwargs):
        self.wiki = wiki
        super().__init__(*args, **kwargs)

    def do_GET(self):  # noqa: N802 — имя задано базовым классом
        if self.path.split("?")[0] == "/api/gdd-index":
            payload = json.dumps(build_index(self.wiki), ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(payload)
            return
        super().do_GET()

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
    args = parser.parse_args()

    handler = partial(LabHandler, directory=args.root, wiki=Path(args.wiki))
    with ThreadingHTTPServer(("127.0.0.1", args.port), handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nЛаборатория остановлена.")


if __name__ == "__main__":
    main()
