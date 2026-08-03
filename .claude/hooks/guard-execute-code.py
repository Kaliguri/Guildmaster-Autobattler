"""PreToolUse-хук: ловит две готчи `execute_code`, которые память не успевает предотвратить.

Зачем. Обе готчи записаны — и обе всё равно срабатывают. Замер по архиву 02.08.2026: `using`
наверху ловился провалившимся вызовом в **27 разговорах** за три недели («Забыла свою же готчу»,
01.08), а `SaveAssets()` был вызван 01.08 агентом, который правило знал и процитировал в том же
ходу. Причина не в том, что готча не записана: память читается на старте сессии, а ошибка
совершается на сороковом вызове. Знание приходит ПОСЛЕ провала — значит дом такой готчи не заметка,
а барьер на самом инструменте.

Что ловим:

1. **`using`-директива.** `execute_code` компилирует тело метода, директив там быть не может.
   Коварство в том, что падение компиляции иногда возвращается как `Timeout receiving Unity
   response` — и читается как «редактор подвис», уводя диагностику совсем не туда.

2. **`AssetDatabase.SaveAssets()`.** Пишет ВСЕ грязные ассеты проекта, включая чужую
   незакоммиченную работу; 01.08.2026 так уехала на диск открытая в редакторе сцена Макса.
   Глобальный вызов не нужен никогда — есть точечный `SaveAssetIfDirty`.

Чего намеренно НЕ ловим: alias-директиву (`using Foo = System.Bar;`). Её не отличить дёшево от
законного `using var x = new Foo();`, а ложная блокировка учит обходить хук — ровно ошибка первой
версии `deny-broad-staging`, отвергнутая 30.07.2026.

Контракт: stdin — JSON события, stdout — либо ничего, либо `deny` в `hookSpecificOutput`. Код
возврата всегда 0, любое исключение = молчаливое разрешение (fail-open): хук страхует от
забывчивости, а не охраняет периметр.
"""

import json
import re
import sys

# Директива, а не оператор: после имени сразу `;`. `using (…)` и `using var x = …;` не подходят
# под шаблон и остаются разрешёнными — они законны в теле метода.
USING_DIRECTIVE = re.compile(r"^\s*using\s+(?:static\s+)?[A-Za-z_][\w.]*\s*;\s*$")

SAVE_ASSETS = re.compile(r"\bAssetDatabase\s*\.\s*SaveAssets\s*\(")

# Директивы стоят наверху. Ограничение окна убирает ложные срабатывания на строковых литералах
# в глубине длинного кода.
HEAD_LINES = 15

USING_REASON = (
    "В execute_code нельзя using-директивы: код компилируется как ТЕЛО МЕТОДА. Пиши полные имена — "
    "UnityEditor.Localization.LocalizationEditorSettings вместо using UnityEditor.Localization. "
    "Готча стоит внимания и потому, что падение компиляции иногда возвращается как «Timeout "
    "receiving Unity response» и читается как зависший редактор — диагностика уходит не туда. "
    "Проверить, что редактор жив, можно дешёвым вызовом: return \"alive\";"
)

SAVE_ASSETS_REASON = (
    "AssetDatabase.SaveAssets() запрещён: он пишет ВСЕ грязные ассеты проекта, включая чужую "
    "незакоммиченную работу — 01.08.2026 так уехала на диск открытая в редакторе сцена Макса. "
    "Сохраняй точечно: AssetDatabase.SaveAssetIfDirty(asset) по каждому затронутому объекту "
    "(для лок-таблиц — col.SharedData плюс цикл по col.StringTables). После правки ассетов "
    "проверь git status --short на файлы, которых ты не трогала."
)


def violation(code):
    """Первая найденная причина отказа, либо None."""
    lines = code.splitlines()
    for line in lines[:HEAD_LINES]:
        if USING_DIRECTIVE.match(line):
            return USING_REASON
    if SAVE_ASSETS.search(code):
        return SAVE_ASSETS_REASON
    return None


def main():
    try:
        event = json.load(sys.stdin)
        if event.get("tool_name") != "mcp__unityMCP__execute_code":
            return 0

        tool_input = event.get("tool_input") or {}
        # Проверяем только исполнение: get_history / clear_history кода не несут.
        if tool_input.get("action") not in (None, "execute", "replay"):
            return 0

        reason = violation(tool_input.get("code") or "")
        if reason:
            json.dump(
                {
                    "hookSpecificOutput": {
                        "hookEventName": "PreToolUse",
                        "permissionDecision": "deny",
                        "permissionDecisionReason": reason,
                    }
                },
                sys.stdout,
                # ensure_ascii=True намеренно: stdout на Windows уходит в cp1251, кириллица
                # пришла бы в контекст мусором. \uXXXX — валидный JSON, потребитель распакует.
                ensure_ascii=True,
            )
    except Exception:
        pass  # fail-open, см. докстринг модуля
    return 0


if __name__ == "__main__":
    sys.exit(main())
