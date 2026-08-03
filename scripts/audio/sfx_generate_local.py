# -*- coding: utf-8 -*-
"""
Локальная генерация звука через Stable Audio Open — бесплатная альтернатива ElevenLabs.

Лицензия Stability AI Community: свободно, пока годовая выручка меньше $1M. Веса закрыты
(gated) — их отдают только после принятия условий, поэтому нужен аккаунт Hugging Face:

  1. завести аккаунт на huggingface.co
  2. открыть https://huggingface.co/stabilityai/stable-audio-open-small и принять условия
  3. создать read-токен: https://huggingface.co/settings/tokens
  4. положить его в .env репозитория:  HF_TOKEN=hf_...

Модели: `small` (341M, окно 11 с, 8 шагов семплера pingpong — терпима на CPU) и `1.0`
(1.2B, окно 47 с, 50 шагов обычной диффузии, качество выше, но на CPU считает минутами).
По умолчанию — small.

Результат падает в `FMOD Project/Candidates/generated-local/` — это КАНДИДАТЫ: послушать,
выбрать, положить в пак и прописать в audio_map.py.

ОКРУЖЕНИЕ: генерация живёт в ОТДЕЛЬНОМ venv `scripts/audio/.venv-gen`, не вместе с CLAP.
Причина не вкусовая: `stable-audio-tools` пинит `laion-clap==1.1.4`, чей чекпоинт не сходится с
весами, которые качает CLAP, — в общем окружении они ломали друг друга по очереди (плюс numpy 2.x
против pandas и torch.load против весов CLAP). Разные задачи — разные окружения.

    python -m venv scripts/audio/.venv-gen
    scripts/audio/.venv-gen/Scripts/python -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu129
    scripts/audio/.venv-gen/Scripts/python -m pip install stable-audio-tools soundfile

Запуск:
    scripts/audio/.venv-gen/Scripts/python scripts/audio/sfx_generate_local.py --prompt "poison bubbling acid" --count 3
    scripts/audio/.venv-gen/Scripts/python scripts/audio/sfx_generate_local.py --prompt "holy light chime" --seconds 3 --model 1.0
"""
import argparse
import os
import sys
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ENV = os.path.join(REPO, ".env")
OUT_DIR = os.path.join(REPO, "FMOD Project", "Candidates", "generated-local")

MODELS = {
    "small": "stabilityai/stable-audio-open-small",
    "1.0": "stabilityai/stable-audio-open-1.0",
}


def hf_token():
    if os.environ.get("HF_TOKEN"):
        return os.environ["HF_TOKEN"]
    if os.path.isfile(ENV):
        for line in open(ENV, encoding="utf-8"):
            if line.startswith("HF_TOKEN="):
                return line.split("=", 1)[1].strip()
    print("Нет HF_TOKEN — веса Stable Audio Open закрыты лицензией (см. шапку файла).")
    raise SystemExit(1)


def main():
    parser = argparse.ArgumentParser(description="Локальная генерация SFX (Stable Audio Open)")
    parser.add_argument("--prompt", required=True, help="описание звука (по-английски работает лучше)")
    parser.add_argument("--count", type=int, default=3)
    parser.add_argument("--seconds", type=float, default=2.0, help="длительность результата")
    parser.add_argument("--steps", type=int, default=0,
                        help="шагов вывода; 0 = по умолчанию для модели (small: 8, 1.0: 50)")
    parser.add_argument("--model", choices=list(MODELS), default="small")
    parser.add_argument("--negative", default="low quality, noise, music, speech",
                        help="чего в звуке быть не должно")
    parser.add_argument("--seed", type=int, default=1234,
                        help="база случайности: тот же seed + промпт дают тот же звук")
    args = parser.parse_args()

    token = hf_token()
    os.environ["HF_TOKEN"] = token

    import soundfile
    import torch
    from einops import rearrange
    from stable_audio_tools import get_pretrained_model
    from stable_audio_tools.inference.generation import generate_diffusion_cond

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"модель {MODELS[args.model]} на {device} (первый запуск качает веса, это надолго)…")
    model, config = get_pretrained_model(MODELS[args.model])
    model = model.to(device)
    sample_rate = config["sample_rate"]
    sample_size = config["sample_size"]

    os.makedirs(OUT_DIR, exist_ok=True)
    slug = "".join(c if c.isalnum() else "_" for c in args.prompt)[:40]

    for i in range(1, args.count + 1):
        started = time.time()
        conditioning = [{"prompt": args.prompt, "seconds_start": 0, "seconds_total": args.seconds}]
        negative = [{"prompt": args.negative, "seconds_start": 0, "seconds_total": args.seconds}]
        # Seed задаём сами, и не только ради воспроизводимости: без него библиотека зовёт
        # np.random.randint(0, 2**32-1), а на Windows это выходит за int32 и падает.
        seed = (args.seed + i * 7919) % (2 ** 31 - 1)
        # У двух моделей РАЗНЫЕ режимы вывода, и перепутать их нельзя: small прошла
        # adversarial post-training и работает только с семплером pingpong на 8 шагах при
        # cfg_scale=1 (иначе стек падает на пустом результате), а полная 1.0 — обычная
        # диффузия с dpmpp-3m-sde и cfg_scale=7.
        if args.model == "small":
            output = generate_diffusion_cond(
                model,
                steps=args.steps or 8,
                cfg_scale=1.0,
                conditioning=conditioning,
                sample_size=sample_size,
                sampler_type="pingpong",
                device=device,
                seed=seed,
            )
        else:
            output = generate_diffusion_cond(
                model,
                steps=args.steps or 50,
                cfg_scale=7,
                conditioning=conditioning,
                negative_conditioning=negative,
                sample_size=sample_size,
                sigma_min=0.3,
                sigma_max=500,
                sampler_type="dpmpp-3m-sde",
                device=device,
                seed=seed,
            )
        output = rearrange(output, "b d n -> d (b n)")
        # Пик-нормализация в int16: дальше файл всё равно пройдёт нашу нормализацию к -23 dB,
        # здесь достаточно не отдать клиппованный кандидат.
        output = output.to(torch.float32).div(torch.max(torch.abs(output))).clamp(-1, 1)
        output = output.mul(32767).to(torch.int16).cpu()

        # Модель всегда отдаёт полное окно (11 или 47 с) — режем до запрошенного, иначе в файле
        # остаётся длинный тихий хвост, который потом ловит аудит как «обрубленную тишину».
        wanted = int(args.seconds * sample_rate)
        if output.shape[-1] > wanted:
            output = output[..., :wanted]

        dst = os.path.join(OUT_DIR, f"{slug}_{i:02d}.wav")
        # Пишем через soundfile, а не torchaudio.save: в свежих версиях torchaudio сохранение
        # переехало на TorchCodec, которого в окружении нет, и падает уже ПОСЛЕ генерации —
        # то есть впустую сжигает минуты счёта.
        soundfile.write(dst, output.numpy().T, sample_rate, subtype="PCM_16")
        print(f"  {os.path.relpath(dst, REPO)}  ({time.time() - started:.0f} с)")

    print("\nЭто кандидаты: послушать, выбрать, положить в пак и прописать в audio_map.py.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
