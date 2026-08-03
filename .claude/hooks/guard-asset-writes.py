"""PreToolUse-хук: запрещает править YAML-ассеты Unity, пока открыт редактор.

Зачем. Это HARD-правило из `CLAUDE.md` («пока редактор открыт, ассеты правим ТОЛЬКО через него»),
и до 03.08.2026 оно было **единственным HARD-правилом без единого гейта** — при том что цена его
нарушения выше всех остальных: правка не падает и не ругается, она **молча теряется**. Unity держит
объект в памяти и при следующем сохранении перезаписывает файл своей версией; с выключенным Auto
Refresh редактор вдобавок не перечитывает диск, так что правка не подхватится даже до перезаписи.
Со стороны это выглядит так, будто её не было вовсе, — и поиск уходит в «почему не применилось».

Правило держалось на внимании, а внимание не работает: обход стоит одного невнимательного Write,
и обнаруживается пропажа не сразу, а когда работа уже потеряна.

Как определяем, что редактор открыт: `Temp/UnityLockfile` существует ровно пока Unity держит
проект. При закрытом редакторе файла нет, и хук молчит — правка ассетов мимо редактора тогда
законна (так работает `statdb.ps1`).

Контракт: stdin — JSON события, stdout — либо ничего, либо `deny` в `hookSpecificOutput`. Код
возврата всегда 0, любое исключение = молчаливое разрешение (fail-open): хук страхует от
забывчивости, а не охраняет периметр.
"""

import json
import os
import sys

# Ровно те расширения, что перечислены в HARD-правиле. Список намеренно не расширяется «на глаз»:
# ложная блокировка учит обходить хук, а это дороже пропущенного случая.
ASSET_SUFFIXES = (".asset", ".prefab", ".unity", ".anim", ".controller")

REASON = (
    "Редактор Unity открыт (есть Temp/UnityLockfile), а ты правишь YAML-ассет напрямую — правка "
    "МОЛЧА пропадёт: Unity держит объект в памяти и перезапишет файл своей версией, а с выключенным "
    "Auto Refresh даже не перечитает диск. Правь через редактор: MCP (execute_code, manage_asset, "
    "manage_prefabs, manage_scriptable_object) или руками в инспекторе. Мимо редактора ассеты можно "
    "править только при закрытом Unity. Результат любой правки сверяй НА ДИСКЕ (git diff), а не по "
    "ответу инструмента — domain reload откатывает несохранённое."
)


def project_dir(event):
    """Корень проекта: из события, иначе из окружения хука."""
    return (
        event.get("cwd")
        or os.environ.get("CLAUDE_PROJECT_DIR")
        or os.getcwd()
    )


def main():
    try:
        event = json.load(sys.stdin)
        if event.get("tool_name") not in ("Edit", "Write", "MultiEdit", "NotebookEdit"):
            return 0

        path = (event.get("tool_input") or {}).get("file_path") or ""
        if not path.lower().endswith(ASSET_SUFFIXES):
            return 0

        lockfile = os.path.join(project_dir(event), "Temp", "UnityLockfile")
        if not os.path.exists(lockfile):
            return 0  # редактор закрыт — правка мимо него законна

        json.dump(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PreToolUse",
                    "permissionDecision": "deny",
                    "permissionDecisionReason": REASON,
                }
            },
            sys.stdout,
            # ensure_ascii=True намеренно: stdout на Windows уходит в cp1251, кириллица пришла бы
            # в контекст мусором. \uXXXX — валидный JSON, потребитель распакует.
            ensure_ascii=True,
        )
    except Exception:
        pass  # fail-open, см. докстринг модуля
    return 0


if __name__ == "__main__":
    sys.exit(main())
