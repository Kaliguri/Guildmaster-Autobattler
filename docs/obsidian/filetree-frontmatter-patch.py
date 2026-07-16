#!/usr/bin/env python3
"""
Патч плагина File Tree Alternative (Obsidian) под наш GDD-vault.
Patch for the File Tree Alternative plugin (Obsidian) for our GDD vault.

Зачем / Why:
  1. Показывать frontmatter `title` вместо имени файла в дереве.
     Show frontmatter `title` instead of the file name in the tree.
  2. Сортировать файлы по frontmatter `order` (при равенстве — по title),
     а не по имени файла.
     Sort files by frontmatter `order` (ties broken by title) instead of file name.

File Tree Alternative из коробки этого не умеет. Плагин минифицирован, поэтому
патч точечный и идемпотентный: ищет по коду, а не по позиции.

ВАЖНО: патч слетает при обновлении плагина. Мы его НЕ обновляем; если обновили —
просто прогнать этот скрипт заново.
IMPORTANT: updating the plugin wipes the patch. We do NOT update it; if you did,
re-run this script.

Запуск / Run (из корня репозитория):
    python docs/obsidian/filetree-frontmatter-patch.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MAIN_JS = os.path.normpath(os.path.join(
    HERE, "..", "wiki", ".obsidian", "plugins", "file-tree-alternative", "main.js"))

# --- Патч 1: отображаемое имя = frontmatter.title ?? прежняя логика ---
TITLE_OLD = ("return plugin.settings.showFileNameAsFullPath ? "
             "getFileNameAndExtension(file.path).fileName : file.basename;")
TITLE_NEW = ("return plugin.app.metadataCache.getFileCache(file)?.frontmatter?.title ?? "
             "(plugin.settings.showFileNameAsFullPath ? "
             "getFileNameAndExtension(file.path).fileName : file.basename);")
TITLE_MARK = "getFileCache(file)?.frontmatter?.title"

# --- Патч 2: сортировка по order, затем по title ---
SORT_PAT = re.compile(
    r"return plugin\.settings\.showFileNameAsFullPath\s*\?\s*"
    r"a\.path\.localeCompare\(b\.path, 'en', \{ numeric: true \}\)\s*:\s*"
    r"a\.basename\.localeCompare\(b\.basename, 'en', \{ numeric: true \}\);")
SORT_NEW = (
    "{const _fa=plugin.app.metadataCache.getFileCache(a),"
    "_fb=plugin.app.metadataCache.getFileCache(b);"
    "const _oa=(_fa&&_fa.frontmatter&&typeof _fa.frontmatter.order==='number')?_fa.frontmatter.order:Infinity,"
    "_ob=(_fb&&_fb.frontmatter&&typeof _fb.frontmatter.order==='number')?_fb.frontmatter.order:Infinity;"
    "if(_oa!==_ob)return _oa-_ob;"
    "const _na=((_fa&&_fa.frontmatter&&_fa.frontmatter.title)||a.basename)+'',"
    "_nb=((_fb&&_fb.frontmatter&&_fb.frontmatter.title)||b.basename)+'';"
    "return _na.localeCompare(_nb, undefined, { numeric: true });}")
SORT_MARK = "_oa!==_ob"


def main():
    if not os.path.isfile(MAIN_JS):
        print(f"[ERR] не найден main.js: {MAIN_JS}")
        return 1
    s = open(MAIN_JS, encoding="utf-8").read()
    changed = False

    # Патч 1
    if TITLE_MARK in s:
        print("[skip] title-патч уже применён")
    elif TITLE_OLD in s:
        s = s.replace(TITLE_OLD, TITLE_NEW, 1)
        changed = True
        print("[ok]   title-патч применён")
    else:
        print("[WARN] точка title не найдена (версия плагина изменилась?)")

    # Патч 2
    if SORT_MARK in s:
        print("[skip] sort-патч уже применён")
    elif len(SORT_PAT.findall(s)) == 1:
        s = SORT_PAT.sub(lambda _: SORT_NEW, s, count=1)
        changed = True
        print("[ok]   sort-патч применён")
    else:
        print("[WARN] точка sort не найдена (версия плагина изменилась?)")

    if changed:
        open(MAIN_JS, "w", encoding="utf-8").write(s)
        print("Готово. Перезапусти Obsidian или переключи плагин, чтобы подхватить.")
    else:
        print("Изменений нет.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
