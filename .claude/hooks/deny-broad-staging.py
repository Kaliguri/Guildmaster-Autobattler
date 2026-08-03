"""PreToolUse-хук: отклоняет широкий стейджинг git в общем рабочем дереве.

Зачем. Дерево репозитория одно на все параллельные сессии (агенты + Макс в редакторе).
`git add -A` / `git commit -a` заметают в коммит чужие незакоммиченные файлы: авторство
крадётся, чужая работа прячется внутри несвязанного коммита. Правило «стейджить точечно»
записано в `.cursor/rules/git-conventions.mdc`, но оно **односторонне** — соблюдая его, ты
защищаешь чужое от себя, а своё от чужого сweep'а не защищает ничто. Поэтому барьер.

Почему не однострочный grep по stdin (первая версия хука, отвергнута 2026-07-30). Grep видит
ВЕСЬ payload, а в него попадают и сообщения коммитов, и тестовые строки, и сам регекс хука.
Такой хук блокировал команду, которая всего лишь УПОМИНАЛА `git add -A` в тексте сообщения, —
то есть мешал работать и обучал обходить себя. Здесь разбирается ровно `tool_input.command`,
и только те его сегменты, которые являются вызовом git.

Контракт хука: stdin — JSON события, stdout — либо ничего (разрешено), либо решение `deny`
в формате `hookSpecificOutput`. Код возврата всегда 0: непредвиденная ошибка внутри хука не
должна ломать работу инструмента, поэтому при любом исключении мы молча разрешаем (fail-open).
Это осознанный выбор: хук — страховка от забывчивости, а не контур безопасности.
"""

import json
import re
import sys

# Флаги, означающие «всё, что есть в дереве». `.` проверяется отдельно — как аргумент-путь.
ADD_WIDE_FLAGS = {"-A", "--all", "-u", "--update", "--no-ignore-removal"}
COMMIT_WIDE_FLAGS = {"-a", "--all"}

# Разделители команд в одной строке Bash. Тело heredoc отсекается до разбиения (см. strip_heredoc).
SEGMENT_SPLIT = re.compile(r"(?:&&|\|\||[;\n|])")

# Оболочки, читающие скрипт со stdin: их heredoc-тело — команды, а не данные.
SHELL_READERS = ("bash", "sh", "pwsh", "powershell", "zsh")

DENY_REASON = (
    "Широкий стейджинг запрещён механически: рабочее дерево одно на все параллельные сессии, "
    "и -A / --all / -u / . заберут в коммит чужие незакоммиченные файлы (это уже случалось — "
    "готовый тест уехал в чужой коммит). Стейджить и коммитить ПЕРЕЧИСЛЕНИЕМ ФАЙЛОВ: "
    "git add -- <файлы> либо git commit -F - -- <файлы>. Каталог в pathspec хук пропустит, но он "
    "тоже опасен: внутри могут лежать чужие правки, а pathspec-коммит берёт файлы с диска, "
    "игнорируя индекс — вынуть их потом через restore --staged нельзя."
)


def strip_heredoc(command):
    """Убирает тело heredoc: для обычных команд это данные (сообщение коммита), не вызовы.

    Исключение — оболочка, читающая скрипт со stdin (`bash <<EOF ... EOF`): там тело как раз
    команды, и его надо проверять. Возвращает текст, пригодный для разбиения на сегменты.
    """
    match = re.search(r"<<-?\s*['\"]?(\w+)['\"]?", command)
    if not match:
        return command

    head = command[: match.start()]
    body_and_tail = command[match.end():]

    # `bash <<EOF` — тело исполняется, оставляем его в проверке.
    last_segment = SEGMENT_SPLIT.split(head)[-1].strip()
    if last_segment.split(" ")[0].lower().lstrip("\\\"'") in SHELL_READERS:
        return command

    # Иначе тело — данные. Хвост после закрывающего маркера может содержать ещё команды.
    terminator = match.group(1)
    tail = re.split(r"\n%s\b" % re.escape(terminator), body_and_tail, maxsplit=1)
    return head + (tail[1] if len(tail) > 1 else "")


def tokenize(segment):
    """Грубая разбивка сегмента на аргументы. Кавычки и скобки снимаются, склейка не важна:

    нас интересуют только имена флагов и одиночная точка, а они кавычками не оформляются.
    Скобки нужны из-за подоболочек: в `(cd /x; git add -A)` последний токен приходит как `-A)`.
    """
    return [token.strip("'\"()") for token in segment.split() if token.strip("'\"()")]


def is_wide_staging(segment):
    """True, если сегмент — вызов git, заметающий всё дерево."""
    tokens = tokenize(segment)
    if len(tokens) < 2 or tokens[0] != "git":
        return False

    # Глобальные опции между `git` и подкомандой (`git -C path add -A`).
    index = 1
    while index < len(tokens) and tokens[index].startswith("-"):
        index += 2 if tokens[index] in ("-C", "-c", "--git-dir", "--work-tree") else 1
    if index >= len(tokens):
        return False

    subcommand = tokens[index]
    args = tokens[index + 1:]
    if subcommand == "add":
        # `--dry-run` не спасает: привычка важнее одного безобидного прогона.
        return any(a in ADD_WIDE_FLAGS for a in args) or "." in args
    if subcommand == "commit":
        if any(a in COMMIT_WIDE_FLAGS for a in args):
            return True
        # Слипшиеся короткие флаги: -am, -aem. Осторожно с --amend (это длинный флаг).
        return any(
            a.startswith("-") and not a.startswith("--") and "a" in a[1:] for a in args
        )
    return False


def main():
    try:
        event = json.load(sys.stdin)
        command = (event.get("tool_input") or {}).get("command") or ""
        segments = SEGMENT_SPLIT.split(strip_heredoc(command))
        if any(is_wide_staging(segment.strip()) for segment in segments):
            json.dump(
                {
                    "hookSpecificOutput": {
                        "hookEventName": "PreToolUse",
                        "permissionDecision": "deny",
                        "permissionDecisionReason": DENY_REASON,
                    }
                },
                sys.stdout,
                # ensure_ascii=True (дефолт) намеренно: на Windows stdout уходит в cp1251, и
                # русский текст решения приходил бы в консоль и в контекст агента мусором.
                # \uXXXX-эскейпы — валидный JSON, потребитель распакует их обратно в кириллицу.
                ensure_ascii=True,
            )
    except Exception:
        pass  # fail-open, см. докстринг модуля
    return 0


if __name__ == "__main__":
    sys.exit(main())
