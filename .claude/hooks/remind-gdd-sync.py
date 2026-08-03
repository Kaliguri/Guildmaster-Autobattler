"""PostToolUse-хук: после коммита по контенту или боевой механике напоминает свериться с ГДД.

Зачем. Дизайн-документация отстаёт не потому, что её лень писать, а потому что момент «пора
свериться» ничем не отмечен: правишь ассет реликвии — и в голове это «правка кода», хотя карточка
в `docs/wiki/gdd/relics/` только что начала врать. Симметричный хук для tech-журнала уже висит на
`Assets/_Project` целиком; этот сужен до путей, за которыми стоит ЗАМЫСЕЛ, описанный в ГДД.

Почему пути, а не «перед каждым коммитом проверь ГДД». Широкий ритуал не срабатывает — ровно та
ошибка, за которую в tech отвергли триггер «по фиче» (журнал tech 2026-07-30). Узкий триггер задаёт
один конкретный вопрос про конкретный файл, и на него можно ответить «не врёт» за секунду.

Контракт: stdin — JSON события PostToolUse, stdout — либо ничего, либо `additionalContext`.
Код возврата всегда 0, любое исключение = молчание (fail-open): хук страхует от забывчивости,
ломать из-за него работу нельзя.
"""

import json
import re
import subprocess
import sys

# Пути, за которыми в ГДД стоит описанный замысел. `Configs` (StatsConfig, классовые коридоры)
# и `Encounters` намеренно НЕ включены: их правда живёт в балансных бенчах, а не в карточках.
CONTENT_PATHS = re.compile(
    r"^Assets/_Project/(?:"
    r"ScriptableObjects/(?:Relics|Enemies|Items|Effects|Species|Vessels|Events|Keywords)/"
    r"|Scripts/Combat/"
    r")"
)

REMINDER = (
    "ГДД: коммит тронул контент или боевую механику. Один вопрос — описанное в карточке или главе "
    "теперь ВРЁТ? Врёт и прав код — правишь карточку (только механику: числа, кд, что делает навык). "
    "Врёт, но замысел остаётся замыслом — расхождение идёт в docs/wiki/gdd/relics/implementation-status.md "
    "или enemies/implementation-status.md, НЕ в карточку. Не врёт — не пишем ничего. "
    "Провенанс и «почему» — journal-adr.md; неутверждённые числа — 00-meta/open-forks.md (open.md это "
    "личный инбокс Макса, туда не писать); что замерить — docs/balance-issues.md. "
    "Маршрутизатор целиком: CLAUDE.md, раздел «ГДД — своё правило»."
)


def committed_files():
    """Список файлов последнего коммита. Пустой список при любой осечке git."""
    result = subprocess.run(
        ["git", "show", "--name-only", "--format=", "HEAD"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        return []
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def main():
    try:
        event = json.load(sys.stdin)
        command = (event.get("tool_input") or {}).get("command") or ""
        # Хук навешен на Bash широко; интересует только фактический коммит.
        if "git commit" not in command:
            return 0
        if not any(CONTENT_PATHS.match(path) for path in committed_files()):
            return 0
        json.dump(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PostToolUse",
                    "additionalContext": REMINDER,
                }
            },
            sys.stdout,
            # ensure_ascii=True намеренно: stdout на Windows уходит в cp1251, и кириллица
            # пришла бы в контекст мусором. \uXXXX — валидный JSON, потребитель распакует.
            ensure_ascii=True,
        )
    except Exception:
        pass  # fail-open, см. докстринг модуля
    return 0


if __name__ == "__main__":
    sys.exit(main())
