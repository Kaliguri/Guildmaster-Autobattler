# Guildmaster — Autobattler: AI Agent Guide

Кооперативный автобатлер-рогалик в реальном времени (с паузой) на Unity 6. Этот файл читается Claude Code и Cursor AI автоматически.

## Проект

| Параметр | Значение |
|---|---|
| Движок | Unity 6000.4.8f1 |
| Язык | C# |
| Платформа | Windows / PC |
| Репозиторий | GitHub |

---

## Правила и конвенции

Детальные правила вынесены в отдельные файлы:

| Файл | Содержание |
|---|---|
| `.cursor/rules/git-conventions.mdc` | Формат коммитов, стратегия веток |
| `.cursor/rules/project-context.mdc` | Стандарты кода C#/Unity, рабочий процесс агента |
| `guildmaster-wiki/Игровая документация - Техническая часть/0.3. Подготовка проекта (Unity).md` | Чеклист настройки проекта, структура документации |

---

## Доступные MCP-инструменты

Все серверы настроены в `.cursor/mcp.json` и активны автоматически.

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
3. **Context7** — перед ответами о Unity API, C# или зависимостях из `Packages/manifest.json`
4. **Filesystem MCP** — как альтернатива Shell для файловых операций

---

## Структура проекта

```
Guildmaster - Autobattler/
├── .cursor/
│   ├── mcp.json                      # MCP-серверы
│   └── rules/
│       ├── project-context.mdc       # Стандарты кода, рабочий процесс
│       └── git-conventions.mdc       # Коммиты и ветки
├── .github/
│   └── workflows/ci.yml              # GameCI pipeline (тесты + сборка)
├── Assets/                           # Игровые ассеты и скрипты Unity
├── Packages/                         # Unity Package Manager
├── ProjectSettings/                  # Настройки Unity
└── scripts/
    └── run-tests.ps1                 # Локальный запуск тестов
```

---

## CI/CD

Сборки и тесты через GameCI (GitHub Actions).

```powershell
./scripts/run-tests.ps1
```

Для GameCI нужны секреты в GitHub: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.
