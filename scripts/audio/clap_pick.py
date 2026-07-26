# -*- coding: utf-8 -*-
"""
Подбор и проверка сэмплов через CLAP — «уши» для того, у кого их нет.

CLAP (Contrastive Language-Audio Pretraining) кладёт текст и звук в одно векторное пространство,
поэтому по описанию словами можно ранжировать любую кучу файлов. Здесь это даёт две вещи:

  --find "ice shatter, crystalline"   подобрать кандидатов под новый ключ из всех паков репо
  --verify                            проверить, что уже назначенные сэмплы похожи на то, чем их
                                      объявили в карте (криомант — лёд, щит — металл, а не наоборот)

Что модель НЕ умеет: оценить красоту. «Сочный удар против дохлого» она не различит — отсев
кандидатов её работа, финальный выбор всё равно на слух.

Установка (одноразово, ~3 ГБ, отдельный venv, в .gitignore):
    python -m venv scripts/audio/.venv
    scripts/audio/.venv/Scripts/python -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cpu
    scripts/audio/.venv/Scripts/python -m pip install laion-clap

Запуск (через venv, не системным python):
    scripts/audio/.venv/Scripts/python scripts/audio/clap_pick.py --index
    scripts/audio/.venv/Scripts/python scripts/audio/clap_pick.py --find "sword slash, metal whoosh"
    scripts/audio/.venv/Scripts/python scripts/audio/clap_pick.py --verify
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audio_map as M  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".cache")
INDEX = os.path.join(CACHE, "clap_index.npz")

# Где искать кандидатов: склады паков целиком, а не только то, что уже в карте.
POOLS = [M.KI, M.KUI, M.KRPG, M.KIMP, M.RPGE]
AUDIO_EXT = (".ogg", ".wav", ".mp3", ".flac")

# Сколько слабых соответствий показывать в --verify.
WEAK_LIMIT = 15


def collect_pool():
    files = []
    for pool in POOLS:
        root = os.path.join(REPO, pool)
        if not os.path.isdir(root):
            continue
        for dirpath, _, names in os.walk(root):
            for name in names:
                if name.lower().endswith(AUDIO_EXT):
                    files.append(os.path.join(dirpath, name))
    return sorted(files)


def load_model():
    try:
        import laion_clap
    except ImportError:
        print("Нет laion_clap. Поставь в venv аудио-инструментов (см. шапку файла).")
        raise SystemExit(1)
    model = laion_clap.CLAP_Module(enable_fusion=False)
    model.load_ckpt()   # первый запуск скачивает веса (~2 ГБ) в кеш пользователя
    return model


def build_index():
    import numpy as np
    files = collect_pool()
    if not files:
        print("Пул пуст — проверь пути паков в audio_map.py")
        return 1
    print(f"индексирую {len(files)} файлов…")
    model = load_model()

    # Пачками: список целиком модель тянет в память, а у нас сотни файлов.
    embeds = []
    batch = 32
    for i in range(0, len(files), batch):
        chunk = files[i:i + batch]
        embeds.append(model.get_audio_embedding_from_filelist(x=chunk, use_tensor=False))
        print(f"  {min(i + batch, len(files))}/{len(files)}")
    matrix = np.concatenate(embeds, axis=0)

    os.makedirs(CACHE, exist_ok=True)
    rel = [os.path.relpath(f, REPO).replace("\\", "/") for f in files]
    np.savez_compressed(INDEX, files=np.array(rel), embeds=matrix)
    print(f"индекс: {INDEX} ({matrix.shape[0]} векторов)")
    return 0


def load_index():
    import numpy as np
    if not os.path.isfile(INDEX):
        print("Индекса нет — сначала прогони --index")
        raise SystemExit(1)
    data = np.load(INDEX, allow_pickle=False)
    return list(data["files"]), data["embeds"]


def similarity(text_embed, audio_embeds):
    import numpy as np
    a = audio_embeds / (np.linalg.norm(audio_embeds, axis=1, keepdims=True) + 1e-9)
    t = text_embed / (np.linalg.norm(text_embed) + 1e-9)
    return a @ t


def cmd_find(query, top):
    files, embeds = load_index()
    model = load_model()
    text = model.get_text_embedding([query, ""], use_tensor=False)[0]
    scores = similarity(text, embeds)
    order = scores.argsort()[::-1][:top]
    print(f"\nкандидаты под «{query}»:\n")
    for rank, idx in enumerate(order, start=1):
        print(f"  {rank:2}. {scores[idx]:.3f}  {files[idx]}")
    print("\nСкор — это похожесть на ОПИСАНИЕ, а не качество. Слушать всё равно придётся.")
    return 0


def describe(entry):
    """Текст для сверки: явное описание из карты, иначе — читаемая расшифровка ключа."""
    if entry.get("desc"):
        return entry["desc"]
    key = entry["key"]
    return key.replace(".", " ").replace("_", " ")


def cmd_verify():
    import numpy as np
    files, embeds = load_index()
    by_path = {f: i for i, f in enumerate(files)}
    model = load_model()

    rows = []
    for e in M.ALL:
        sources = [f for f in e["files"] if not f.startswith("music/")]
        idx = [by_path[f] for f in sources if f in by_path]
        if not idx:
            continue
        text = model.get_text_embedding([describe(e), ""], use_tensor=False)[0]
        scores = similarity(text, embeds[idx])
        rows.append((float(np.mean(scores)), e["key"], describe(e),
                     [(round(float(s), 3), os.path.basename(sources[j])) for j, s in enumerate(scores)]))

    rows.sort(key=lambda r: r[0])
    print(f"\nсверка {len(rows)} ключей с их описанием — слабейшие сверху:\n")
    for score, key, desc, per_file in rows[:WEAK_LIMIT]:
        worst = min(per_file, key=lambda p: p[0])
        print(f"  {score:.3f}  {key}   «{desc}»")
        print(f"          худший сэмпл: {worst[1]} ({worst[0]})")
    print(f"\nсредний скор по всем ключам: {np.mean([r[0] for r in rows]):.3f}")
    print("Низкий скор ≠ плохой звук: описание может быть кривым. Читать как «сюда стоит заглянуть».")
    return 0


def main():
    parser = argparse.ArgumentParser(description="CLAP: подбор и сверка сэмплов")
    parser.add_argument("--index", action="store_true", help="построить индекс эмбеддингов по всем пакам")
    parser.add_argument("--find", metavar="QUERY", help="найти кандидатов по описанию")
    parser.add_argument("--top", type=int, default=10, help="сколько кандидатов показать")
    parser.add_argument("--verify", action="store_true", help="сверить назначенные сэмплы с их ключами")
    args = parser.parse_args()

    if args.index:
        return build_index()
    if args.find:
        return cmd_find(args.find, args.top)
    if args.verify:
        return cmd_verify()
    parser.print_help()
    return 0


if __name__ == "__main__":
    sys.exit(main())
