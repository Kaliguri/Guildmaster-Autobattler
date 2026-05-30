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

## Технологический стек

Все пакеты установлены. Полное обоснование — `guildmaster-wiki/Игровая документация - Техническая часть/5. Технологический стек и архитектура.md`.

### Архитектура / DI

| Пакет | Где лежит | Назначение |
|---|---|---|
| **VContainer** 1.18.0 | `Packages/manifest.json` | DI-контейнер. Никаких синглтонов. Зависимости — только через инъекцию. |
| **MessagePipe** | `Packages/manifest.json` | Pub/sub EventBus через VContainer. Развязка Combat → UI/Audio/VFX. |

### Async / Анимации

| Пакет | Назначение |
|---|---|
| **UniTask** | Zero-alloc async/await. Использовать вместо корутин для всего time-based. |
| **LitMotion** | Zero-alloc твины. UI-анимации, HP-бары, damage numbers, фидбэк. |

### Сохранения / Данные

| Пакет | Назначение |
|---|---|
| **Easy Save 3** | Плумбинг сейвов (диск + Steam Cloud). Сами пишем DTO-слой. |
| **Newtonsoft.Json** 3.2.2 | JSON-сериализация DTO. |
| **Addressables** 2.3.1 | Загрузка контента по адресу. Основа для Localization. |
| **Unity Localization** 1.5.3 | Локализация EN + RU. Ключи закладывать в SO сразу. |

### Мультиплеер / Steam

| Пакет | Где лежит | Назначение |
|---|---|---|
| **NGO** 2.11.2 | `Packages/manifest.json` | Netcode for GameObjects — host-authoritative сетевой слой. |
| **Facepunch.Steamworks** 2.5.2 | `Assets/Plugins/Facepunch.Steamworks/` | Steam-интеграция. `steam_api64.dll` в `redistributable_bin/win64/`. |
| **MPPM** 1.3.2 | `Packages/manifest.json` | Тест кооп в редакторе (до 4 виртуальных игроков). |

### Редактор / Инспектор

| Пакет | Где лежит | Назначение |
|---|---|---|
| **Odin Inspector** 3.x | `Assets/Plugins/Sirenix/` | Расширенный Inspector. `[SerializeReference]`-дропдауны для полиморфных данных. |

### Аудио

| Пакет | Где лежит | Назначение |
|---|---|---|
| **FMOD** | `Assets/Plugins/FMOD/` | Адаптивная музыка и звук. **Всегда за интерфейсом `IAudioService`** — не дёргать FMOD API напрямую из игровой логики. |

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

| Задача | MCP-сервер | Server ID | Инструменты |
|---|---|---|---|
| Работа со сценами Unity, объектами, компонентами, play mode, тесты | **unityMCP** | `user-unityMCP` | `manage_scene`, `manage_gameobject`, `read_console`, `run_tests`, `refresh_unity` и др. |
| Создание PR, issues, просмотр workflows, releases | **github** | `project-0-Guildmaster_-_Autobattler-github` | `create_pull_request`, `create_issue`, `list_workflows` и др. |
| Локальные git-операции: коммиты, ветки, diff, лог | **git** | `project-0-Guildmaster_-_Autobattler-git` | `git_commit`, `git_diff`, `git_log`, `git_create_branch` и др. |
| Актуальная документация библиотек (Unity, C#, пакеты) | **context7** | `project-0-Guildmaster_-_Autobattler-context7` | `resolve-library-id`, `get-library-docs` |
| Чтение и запись файлов проекта | **filesystem** | `project-0-Guildmaster_-_Autobattler-filesystem` | `read_file`, `write_file`, `list_directory` и др. |

> Unity MCP: `mcpforunityserver==9.7.1`, порт `8080` (user-level, не в `.cursor/mcp.json` проекта)

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
