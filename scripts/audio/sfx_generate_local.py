# -*- coding: utf-8 -*-
"""
Локальная генерация звука через Stable Audio Open — бесплатная альтернатива ElevenLabs.

Лицензия Stability AI Community: свободно, пока годовая выручка меньше $1M. Веса закрыты
(gated) — их отдают только после принятия условий, поэтому нужен аккаунт Hugging Face:

  1. завести аккаунт на huggingface.co
  2. открыть https://huggingface.co/stabilityai/stable-audio-open-small и принять условия
  3. создать read-токен: https://huggingface.co/settings/tokens
  4. положить его в .env репозитория:  HF_TOKEN=hf_...

Модели: `small` (341M, до 11 с звука — годится для our one-shot и терпима на CPU) и `1.0`
(1.2B, до 47 с, качество выше, но на CPU считает минутами). По умолчанию — small.

Результат падает в `FMOD Project/Candidates/generated-local/` — это КАНДИДАТЫ: послушать,
выбрать, положить в пак и прописать в audio_map.py.

Запуск (venv аудио-инструментов, не системный python):
    scripts/audio/.venv/Scripts/python scripts/audio/sfx_generate_local.py --prompt "poison bubbling acid" --count 3
    scripts/audio/.venv/Scripts/python scripts/audio/sfx_generate_local.py --prompt "holy light chime" --seconds 3 --model 1.0
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
    parser.add_argument("--steps", type=int, default=50, help="шагов диффузии: меньше — быстрее и грязнее")
    parser.add_argument("--model", choices=list(MODELS), default="small")
    parser.add_argument("--negative", default="low quality, noise, music, speech",
                        help="чего в звуке быть не должно")
    args = parser.parse_args()

    token = hf_token()
    os.environ["HF_TOKEN"] = token

    import torch
    import torchaudio
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
        output = generate_diffusion_cond(
            model,
            steps=args.steps,
            cfg_scale=7,
            conditioning=conditioning,
            negative_conditioning=negative,
            sample_size=sample_size,
            sigma_min=0.3,
            sigma_max=500,
            sampler_type="dpmpp-3m-sde",
            device=device,
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
        torchaudio.save(dst, output, sample_rate)
        print(f"  {os.path.relpath(dst, REPO)}  ({time.time() - started:.0f} с)")

    print("\nЭто кандидаты: послушать, выбрать, положить в пак и прописать в audio_map.py.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
