# -*- coding: utf-8 -*-
"""
Генерация звука под дыры, которых нет в бесплатных паках (яд, свет/тьма, узнаваемые ульты).

Через ElevenLabs Sound Effects API: текст → wav. Платно (порядка $0.002 за секунду), на платных
планах лицензия royalty-free без атрибуции — то есть результат можно класть прямо в игру.

Ключ: ELEVENLABS_API_KEY в .env репозитория (как PIXELLAB_SECRET).

Результат падает в `FMOD Project/Candidates/generated/` — это КАНДИДАТЫ, а не готовый контент:
дальше их надо послушать, выбрать, положить в пак и прописать в audio_map.py, после чего обычный
прогон пайплайна нормализует и зальёт их в FMOD.

Запуск:
    python scripts/audio/sfx_generate.py --prompt "poison bubbling, toxic acid hiss" --count 3
    python scripts/audio/sfx_generate.py --prompt "holy light healing chime" --duration 2.0 --loop
"""
import argparse
import json
import os
import sys
import urllib.request

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ENV = os.path.join(REPO, ".env")
OUT_DIR = os.path.join(REPO, "FMOD Project", "Candidates", "generated")
ENDPOINT = "https://api.elevenlabs.io/v1/sound-generation"


def api_key():
    if os.environ.get("ELEVENLABS_API_KEY"):
        return os.environ["ELEVENLABS_API_KEY"]
    if os.path.isfile(ENV):
        for line in open(ENV, encoding="utf-8"):
            if line.startswith("ELEVENLABS_API_KEY="):
                return line.split("=", 1)[1].strip()
    print("Нет ELEVENLABS_API_KEY. Положи ключ в .env репозитория (как PIXELLAB_SECRET).")
    print("Без подписки: бесплатная альтернатива — Stable Audio Open (self-host, лицензия Stability")
    print("Community: бесплатно при выручке < $1M). Это отдельная установка на несколько ГБ.")
    raise SystemExit(1)


def generate(prompt, duration, loop, influence, token):
    payload = {"text": prompt, "prompt_influence": influence, "loop": loop}
    if duration:
        payload["duration_seconds"] = duration
    request = urllib.request.Request(
        ENDPOINT,
        data=json.dumps(payload).encode("utf-8"),
        headers={"xi-api-key": token, "Content-Type": "application/json", "Accept": "audio/wav"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def main():
    parser = argparse.ArgumentParser(description="Генерация SFX под дыры каталога")
    parser.add_argument("--prompt", required=True, help="описание звука (по-английски работает лучше)")
    parser.add_argument("--count", type=int, default=3, help="сколько вариантов сгенерировать")
    parser.add_argument("--duration", type=float, default=None, help="длительность, сек (пусто = на усмотрение модели)")
    parser.add_argument("--loop", action="store_true", help="зацикленный звук (амбиент)")
    parser.add_argument("--influence", type=float, default=0.4,
                        help="0..1: насколько буквально следовать описанию (выше = точнее, но суше)")
    args = parser.parse_args()

    token = api_key()
    os.makedirs(OUT_DIR, exist_ok=True)
    slug = "".join(c if c.isalnum() else "_" for c in args.prompt)[:40]

    for i in range(1, args.count + 1):
        try:
            audio = generate(args.prompt, args.duration, args.loop, args.influence, token)
        except Exception as e:
            print(f"  !! вариант {i} не сгенерировался: {e}")
            continue
        dst = os.path.join(OUT_DIR, f"{slug}_{i:02d}.wav")
        with open(dst, "wb") as fh:
            fh.write(audio)
        print(f"  {os.path.relpath(dst, REPO)}  ({len(audio) / 1024:.0f} КБ)")

    print("\nЭто кандидаты: послушать, выбрать, положить в пак и прописать в audio_map.py.")
    print("Нормализацию и заливку в FMOD дальше делает обычный прогон пайплайна.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
