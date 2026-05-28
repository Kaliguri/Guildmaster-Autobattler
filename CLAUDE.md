# Guildmaster — Autobattler: AI Agent Guide

Пошаговая автобатлер-игра на Unity 6. Этот файл читается Claude Code и Cursor AI автоматически.

## Проект

| Параметр | Значение |
|---|---|
| Движок | Unity 6000.4.8f1 |
| Язык | C# |
| Платформа | Windows / PC |
| Репозиторий | GitHub |

---

## Доступные MCP-инструменты

Все серверы настроены в `.cursor/mcp.json` и активны автоматически.

### Когда какой MCP использовать

| Задача | MCP-сервер | Инструменты |
|---|---|---|
| Работа со сценами Unity, объектами, компонентами, play mode | **unity** (mcp-unity) | `get_hierarchy`, `select_object`, `run_play_mode` и др. |
| Создание PR, issues, просмотр workflows, releases | **github** | `create_pull_request`, `create_issue`, `list_workflows` и др. |
| Локальные git-операции: коммиты, ветки, diff, лог | **git** | `git_commit`, `git_diff`, `git_log`, `git_create_branch` и др. |
| Актуальная документация библиотек (Unity, C#, пакеты) | **context7** | `resolve-library-id`, `get-library-docs` |
| Чтение и запись файлов проекта | **filesystem** | `read_file`, `write_file`, `list_directory` и др. |

### Приоритет использования MCP

1. **Unity MCP** — для любых операций с Editor (сцены, объекты, тесты через редактор)
2. **Git/GitHub MCP** — для всех git-операций вместо Shell-команд, когда возможно
3. **Context7** — перед ответами о Unity API, C# или зависимостях из `Packages/manifest.json` — всегда проверяй актуальную документацию
4. **Filesystem MCP** — как альтернатива Shell для файловых операций

---

## Структура проекта

```
Guildmaster - Autobattler/
├── .cursor/
│   ├── mcp.json          # MCP-серверы
│   └── rules/            # Правила для Cursor AI
├── .github/
│   └── workflows/ci.yml  # GameCI pipeline (тесты + сборка)
├── Assets/               # Игровые ассеты и скрипты Unity
├── Packages/             # Unity Package Manager
├── ProjectSettings/      # Настройки Unity
└── scripts/
    └── run-tests.ps1     # Локальный запуск тестов
```

---

## Стандарты кода C# / Unity

- **Именование**: PascalCase для публичных членов и классов, `_camelCase` для приватных полей
- **Компоненты**: `MonoBehaviour` для логики GameObject, `ScriptableObject` для данных
- **Кэширование**: всегда кэшировать ссылки на компоненты в `Awake()` — никогда `GetComponent` в `Update()`
- **Пулинг объектов**: использовать `ObjectPool<T>` вместо `Instantiate`/`Destroy` в горячих путях
- **Данные**: хардкод значений запрещён — только `ScriptableObject` или конфиг-файлы
- **Input**: использовать Unity Input System, не Legacy Input
- **Async**: Coroutines для time-based операций; `async/await` с `UniTask` если подключён

### Запрещено

- `GetComponent`, `Find`, `FindObjectOfType` в `Update()` / `FixedUpdate()`
- `Instantiate`/`Destroy` в циклах
- Выделение памяти (LINQ с аллокациями, строки) в горячих путях
- Хардкод игровых значений в коде

---

## CI/CD

Сборки и тесты через GameCI (GitHub Actions).

```powershell
# Локальный запуск тестов
./scripts/run-tests.ps1
```

Для GameCI нужны секреты в GitHub: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.

---

## Рабочий процесс агента

1. Перед ответами об Unity API — используй **Context7** для актуальной документации
2. Изменения в Unity-сцене/объектах — через **Unity MCP**, не напрямую редактируя `.unity` файлы
3. Git-операции — через **Git MCP** или стандартные Shell-команды
4. После изменений кода — запускай тесты через `scripts/run-tests.ps1`
