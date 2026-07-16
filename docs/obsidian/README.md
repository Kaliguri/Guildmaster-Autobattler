# Obsidian — локальные патчи vault / Local vault patches

Здесь лежат правки под наш GDD-vault (`docs/wiki`), которые нельзя выразить
настройками плагинов. `main.js` плагинов в `.gitignore`, поэтому патчи хранятся
здесь как **скрипты-накатчики** и применяются заново после переустановки/обновления.

Vault-specific tweaks that plugin settings can't express. Plugin `main.js` files
are gitignored, so patches live here as **re-appliable scripts**.

## `filetree-frontmatter-patch.py`

Патчит плагин **File Tree Alternative**:

1. В дереве показывается frontmatter `title` вместо имени файла.
2. Сортировка файлов — по frontmatter `order` (при равенстве — по `title`),
   а не по имени файла.

Так slug-имена на диске остаются короткими и стабильными, а в дереве видно
человекочитаемые заголовки в осмысленном порядке.

### Применение / Apply
```bash
python docs/obsidian/filetree-frontmatter-patch.py
```
Скрипт идемпотентен: повторный запуск ничего не ломает. После наката —
перезапустить Obsidian или переключить плагин.

### Когда запускать снова / When to re-run
- После обновления File Tree Alternative (обновление затирает `main.js`).
- После переустановки плагина или на новой машине.

> Договорённость: File Tree Alternative **не обновляем** без нужды — текущая
> версия (2.6.0) в порядке. Если обновили и дерево показывает сырые имена —
> прогнать скрипт заново.

## Связанное / Related
- Отображение `title` требует плагина **Front Matter Title** (красит встроенный
  проводник; File Tree красится этим патчем).
- Бэкап списка плагинов — `docs/obsidian-plugins-backup.md`.
