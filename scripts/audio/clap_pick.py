# -*- coding: utf-8 -*-
"""
Подбор и проверка сэмплов через CLAP — «уши» для того, у кого их нет.

CLAP (Contrastive Language-Audio Pretraining) кладёт текст и звук в одно векторное пространство,
поэтому по описанию словами можно ранжировать любую кучу файлов. Здесь это даёт две вещи:

  --find "ice shatter, crystalline"   подобрать кандидатов под новый ключ из всех паков репо
  --verify                            проверить, что уже назначенные сэмплы похожи на то, чем их
                                      объявили в карте (криомант — лёд, щит — металл, а не наоборот)

ГРАНИЦА ПРИМЕНИМОСТИ (проверена замером, не на глаз). CLAP обучен на полевых записях, где звук
идёт с контекстом и хвостом, поэтому на КОРОТКИХ сухих one-shot из игровых паков он ненадёжен:
корреляция длительности со скором на нашем материале +0.47, средняя длина топ-20 выдачи 2.2 с
против 0.45 с у дна. Пример: «glass shattering» ставит 2.7-секундный Ice_explosion на первое
место, а честный 0.24-секундный impactGlass_heavy — на 122-е из 380. Тайлинг короткого сэмпла
до трёх секунд не помогает (проверено: скор скорее падает).

Отсюда правило: скорам файлов короче SHORT_SAMPLE_SEC не верить, они помечаются в выводе.
Модель полезна на фактурных сэмплах (магия, слэши, амбиент) и на внешних библиотеках вроде
Freesound, где записи длиннее.

Чего модель не умеет вовсе: оценить красоту. «Сочный удар против дохлого» она не различит —
отсев кандидатов её работа, финальный выбор всё равно на слух.

Установка (одноразово, ~3 ГБ, свой venv, в .gitignore). Генерация звука живёт в ДРУГОМ окружении
(`.venv-gen`): `stable-audio-tools` пинит несовместимую версию laion-clap и ломает загрузку весов.

    python -m venv scripts/audio/.venv
    scripts/audio/.venv/Scripts/python -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu129
    scripts/audio/.venv/Scripts/python -m pip install laion-clap "numpy<2"

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

# Короче этого порога скор CLAP — шум (см. «граница применимости» в шапке).
SHORT_SAMPLE_SEC = 0.6


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


def duration_of(path):
    import subprocess
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                       capture_output=True, text=True)
    try:
        return float((r.stdout or "0").strip())
    except ValueError:
        return 0.0


def load_model():
    try:
        import laion_clap
    except ImportError:
        print("Нет laion_clap. Поставь в venv аудио-инструментов (см. шапку файла).")
        raise SystemExit(1)
    # torch 2.6+ грузит чекпоинты с weights_only=True, а веса CLAP содержат numpy-скаляр и
    # отвергаются. Источник доверенный (официальный чекпоинт LAION), поэтому разрешаем ровно
    # этот тип, а не выключаем проверку целиком.
    try:
        import functools
        import numpy as _np
        import torch as _torch

        allow = [_np.core.multiarray.scalar, _np.dtype]
        # Чекпоинт тянет за собой конкретные подтипы dtype (Float64DType и соседи) — перечислять
        # их поимённо бессмысленно, берём все, что есть в numpy.dtypes.
        allow += [t for t in vars(_np.dtypes).values() if isinstance(t, type)]
        _torch.serialization.add_safe_globals(allow)

        # Разрешённых глобалов всё равно не хватает: laion-clap зовёт torch.load без
        # weights_only, а дефолт с 2.6 сменился. Подменяем дефолт ТОЛЬКО на время загрузки
        # весов CLAP — источник официальный, а альтернатива — держать torch < 2.6 ради одной
        # библиотеки.
        if not getattr(_torch.load, "_clap_patched", False):
            original = _torch.load
            patched = functools.partial(original, weights_only=False)
            patched._clap_patched = True
            _torch.load = patched
    except Exception:
        pass   # на старом torch этого API нет и оно не нужно

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
    # Длительность кешируем вместе с векторами: она нужна, чтобы пометить сэмплы, чьему скору
    # верить нельзя, а звать ffprobe на каждый поиск заново — расточительно.
    durations = np.array([duration_of(f) for f in files], dtype=float)
    np.savez_compressed(INDEX, files=np.array(rel), embeds=matrix, durations=durations)
    print(f"индекс: {INDEX} ({matrix.shape[0]} векторов)")
    return 0


def load_index():
    import numpy as np
    if not os.path.isfile(INDEX):
        print("Индекса нет — сначала прогони --index")
        raise SystemExit(1)
    data = np.load(INDEX, allow_pickle=False)
    durations = data["durations"] if "durations" in data.files else np.zeros(len(data["files"]))
    return list(data["files"]), data["embeds"], durations


def similarity(text_embed, audio_embeds):
    import numpy as np
    a = audio_embeds / (np.linalg.norm(audio_embeds, axis=1, keepdims=True) + 1e-9)
    t = text_embed / (np.linalg.norm(text_embed) + 1e-9)
    return a @ t


def cmd_find(query, top):
    files, embeds, durations = load_index()
    model = load_model()
    text = model.get_text_embedding([query, ""], use_tensor=False)[0]
    scores = similarity(text, embeds)
    order = scores.argsort()[::-1][:top]
    print(f"\nкандидаты под «{query}»:\n")
    for rank, idx in enumerate(order, start=1):
        mark = "  (короткий — скору не верить)" if durations[idx] < SHORT_SAMPLE_SEC else ""
        print(f"  {rank:2}. {scores[idx]:+.3f}  {durations[idx]:.2f}с  {files[idx]}{mark}")
    print("\nСкор — похожесть на ОПИСАНИЕ, а не качество, и на коротких сэмплах он шумит.")
    return 0


def describe(entry):
    """Текст для сверки: явное описание из карты, иначе — читаемая расшифровка ключа."""
    if entry.get("desc"):
        return entry["desc"]
    key = entry["key"]
    return key.replace(".", " ").replace("_", " ")


def cmd_verify():
    import numpy as np
    files, embeds, durations = load_index()
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
        mean_len = float(np.mean([durations[i] for i in idx]))
        rows.append((float(np.mean(scores)), e["key"], describe(e), mean_len,
                     [(round(float(s), 3), os.path.basename(sources[j])) for j, s in enumerate(scores)]))

    # Два списка, а не один: у коротких сэмплов низкий скор — свойство модели, а не сигнал о звуке.
    trusted = sorted([r for r in rows if r[3] >= SHORT_SAMPLE_SEC], key=lambda r: r[0])
    short = [r for r in rows if r[3] < SHORT_SAMPLE_SEC]

    print(f"\nсверка {len(rows)} ключей ({len(trusted)} со скором, которому можно верить):\n")
    for score, key, desc, mean_len, per_file in trusted[:WEAK_LIMIT]:
        worst = min(per_file, key=lambda p: p[0])
        print(f"  {score:+.3f}  {key}   «{desc}»")
        print(f"          худший сэмпл: {worst[1]} ({worst[0]:+.3f}), средняя длина {mean_len:.2f}с")

    if trusted:
        print(f"\nсредний скор по проверяемым: {np.mean([r[0] for r in trusted]):+.3f}")
    print(f"пропущено как слишком короткие: {len(short)} ключей "
          f"(CLAP на one-shot < {SHORT_SAMPLE_SEC} с шумит, см. шапку файла)")
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
