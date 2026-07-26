# -*- coding: utf-8 -*-
"""
Добытчик сэмплов с Freesound: поиск по описанию с фильтром лицензии и скачивание превью.

Зачем: локальные CC0-паки закрывают большинство ключей, но не всё (яд, свет/тьма, редкие ульты).
Freesound — крупнейшая CC-библиотека, и по ней можно искать программно, а ранжировать кандидатов
потом через CLAP (clap_pick.py) — то есть без ушей.

ЛИЦЕНЗИИ: по умолчанию ищем только Creative Commons 0 — их можно класть в игру без атрибуции.
Другие лицензии (CC-BY) доступны флагом, но тогда автор ОБЯЗАН попасть в титры; скрипт пишет
их в manifest рядом с файлами.

СКАЧИВАНИЕ: полное качество требует OAuth2, поэтому по умолчанию тянем HQ-превью (mp3/ogg) —
для отбора кандидатов их достаточно, а финальный файл берётся руками со страницы звука.

Ключ API: FREESOUND_API_KEY в .env репозитория (получить: https://freesound.org/apiv2/apply/).

Запуск:
    python scripts/audio/freesound_fetch.py --query "poison bubbling toxic" --count 8
    python scripts/audio/freesound_fetch.py --query "holy light chime" --max-duration 3 --any-license
"""
import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ENV = os.path.join(REPO, ".env")
OUT_DIR = os.path.join(REPO, "FMOD Project", "Candidates")
API = "https://freesound.org/apiv2"

# Свободный лимит API: 60 запросов в минуту. Пауза между скачиваниями держит нас далеко под ним.
REQUEST_PAUSE = 1.1


def api_key():
    if os.environ.get("FREESOUND_API_KEY"):
        return os.environ["FREESOUND_API_KEY"]
    if os.path.isfile(ENV):
        for line in open(ENV, encoding="utf-8"):
            if line.startswith("FREESOUND_API_KEY="):
                return line.split("=", 1)[1].strip()
    print("Нет FREESOUND_API_KEY. Положи ключ в .env репозитория (как PIXELLAB_SECRET).")
    print("Получить: https://freesound.org/apiv2/apply/")
    raise SystemExit(1)


def get(url):
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def search(query, count, max_duration, any_license, token):
    filters = [f"duration:[0.05 TO {max_duration}]"]
    if not any_license:
        filters.append('license:"Creative Commons 0"')
    params = {
        "query": query,
        "filter": " ".join(filters),
        "sort": "score",
        "page_size": count,
        "fields": "id,name,license,duration,username,previews,url,avg_rating,num_ratings",
        "token": token,
    }
    return get(f"{API}/search/text/?{urllib.parse.urlencode(params)}").get("results", [])


def main():
    parser = argparse.ArgumentParser(description="Поиск кандидатов на Freesound")
    parser.add_argument("--query", required=True, help="описание звука словами (по-английски)")
    parser.add_argument("--count", type=int, default=8)
    parser.add_argument("--max-duration", type=float, default=6.0)
    parser.add_argument("--any-license", action="store_true",
                        help="брать не только CC0 (тогда автор обязан попасть в титры)")
    parser.add_argument("--no-download", action="store_true", help="только список, без скачивания превью")
    args = parser.parse_args()

    token = api_key()
    results = search(args.query, args.count, args.max_duration, args.any_license, token)
    if not results:
        print("Ничего не нашлось — попробуй другое описание.")
        return 0

    slug = "".join(c if c.isalnum() else "_" for c in args.query)[:40]
    folder = os.path.join(OUT_DIR, slug)
    if not args.no_download:
        os.makedirs(folder, exist_ok=True)

    manifest = []
    print(f"\nнайдено {len(results)} по «{args.query}»:\n")
    for i, item in enumerate(results, start=1):
        rating = f"{item.get('avg_rating', 0):.1f}/{item.get('num_ratings', 0)}"
        print(f"  {i:2}. [{item['license'].split('/')[-2] if '/' in item['license'] else item['license']}] "
              f"{item['name']}  ({item['duration']:.2f} с, рейтинг {rating})")
        print(f"      {item['url']}")
        entry = dict(id=item["id"], name=item["name"], license=item["license"],
                     author=item["username"], url=item["url"], duration=item["duration"])

        if not args.no_download:
            preview = item.get("previews", {}).get("preview-hq-ogg")
            if preview:
                dst = os.path.join(folder, f"{item['id']}_{slug}.ogg")
                try:
                    urllib.request.urlretrieve(preview, dst)
                    entry["file"] = os.path.relpath(dst, REPO).replace("\\", "/")
                except Exception as e:  # сеть — единственное, что тут реально ломается
                    print(f"      !! превью не скачалось: {e}")
                time.sleep(REQUEST_PAUSE)
        manifest.append(entry)

    if not args.no_download:
        with open(os.path.join(folder, "credits.json"), "w", encoding="utf-8") as fh:
            json.dump(dict(query=args.query, results=manifest), fh, ensure_ascii=False, indent=2)
        print(f"\nпревью и credits.json: {os.path.relpath(folder, REPO)}")
        print("Дальше: отранжировать через clap_pick.py --find, лучшее скачать в полном качестве")
        print("со страницы звука и положить в пак; превью в игру не идут (mp3-качество).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
