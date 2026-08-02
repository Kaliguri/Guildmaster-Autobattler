"""Хук компиляции: правка `.cs` ставит флаг, конец хода прогоняет `compile-check.ps1`.

Зачем. В CLAUDE.md это HARD-правило («тронул `.cs` — гони compile-check, не проси рефреш у Unity»),
но исполнялось оно памятью агента, а память — худший из возможных исполнителей правила. Симметрия с
`deny-broad-staging`: то, что нельзя забыть, держит механика, а не дисциплина.

Почему две половины, а не одна.

*Гонять на каждый `Edit`* — прогон стоит 2-15 секунд, а правки идут сериями по десять; ход
превратился бы в ожидание. *Гонять на каждый конец хода* — ещё хуже: `compile-check` смотрит ВСЁ
рабочее дерево, а там почти всегда висят незакоммиченные `.cs` параллельных сессий, поэтому «есть ли
изменённые файлы» отвечало бы «да» даже в разговорном ходе, где никто ничего не правил.

Отсюда флаг: `mark` роняет метку при правке `.cs` (быстро, ничего не запускает), `check` в конце
хода видит метку, гонит прогон один раз и метку снимает. Прогон случается ровно тогда, когда в этом
ходу действительно трогали код.

Контракт. `mark`: stdin — событие PostToolUse, выход всегда 0, молча. `check`: stdin — событие Stop;
компилируется — 0 и тишина; не компилируется — **код 2**, текст ошибок в stderr (агент увидит их как
обратную связь и починит в том же ходу). Повторный заход при `stop_hook_active` не блокирует: правило
страхует от забывчивости, а не запирает сессию в цикле.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile

FLAG_DIR = os.path.join(tempfile.gettempdir(), "claude", "compile-gate")


def flag_path(session_id):
    return os.path.join(FLAG_DIR, f"{session_id or 'default'}.flag")


def touched_cs(event):
    """Тронул ли инструмент файл `.cs`. Write и Edit кладут путь в одно и то же поле."""
    path = (event.get("tool_input") or {}).get("file_path") or ""
    return path.lower().endswith(".cs")


def mark(event):
    if not touched_cs(event):
        return 0
    os.makedirs(FLAG_DIR, exist_ok=True)
    with open(flag_path(event.get("session_id")), "w", encoding="utf-8") as handle:
        handle.write((event.get("tool_input") or {}).get("file_path", ""))
    return 0


def check(event):
    # Второй заход того же Stop: правило уже сработало, держать ход дальше незачем.
    if event.get("stop_hook_active"):
        return 0

    path = flag_path(event.get("session_id"))
    if not os.path.exists(path):
        return 0
    os.remove(path)

    project = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
    script = os.path.join(project, "scripts", "compile-check.ps1")
    if not os.path.exists(script):
        return 0

    # Именно pwsh 7, а не встроенный powershell 5.1: compile-check.ps1 в 5.1 не парсится вовсе
    # (ParserError на ровном месте), и хук молча получал бы «не скомпилировалось» на любой правке.
    shell = shutil.which("pwsh")
    if not shell:
        return 0  # fail-open: без семёрки прогнать нечем, но и ломать ход не за что

    result = subprocess.run(
        [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=project,
    )
    if result.returncode == 0:
        return 0

    # Хвост вывода, а не весь: интересны строки с ошибками, они идут последними. Шапка со списком
    # проверенных сборок в контексте агента только шумит.
    tail = "\n".join((result.stdout or "").strip().splitlines()[-25:])
    sys.stderr.write(
        "Компиляция не прошла (hook compile-check-gate). Проверь, твоя ли это сборка — прогон видит "
        "ВСЁ рабочее дерево, включая правки параллельных сессий.\n\n" + tail + "\n"
    )
    return 2


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "mark"
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0  # fail-open: хук страхует, ломать из-за него работу нельзя

    try:
        return mark(event) if mode == "mark" else check(event)
    except Exception:
        return 0


if __name__ == "__main__":
    sys.exit(main())
