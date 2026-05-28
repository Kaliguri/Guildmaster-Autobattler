# Guildmaster — Autobattler

Пошаговая автобатлер-игра, разрабатываемая в Unity.

---

## Версия движка

| Параметр | Значение |
|---|---|
| **Unity** | 6000.4.8f1 (Unity 6) |
| **Платформа** | Windows / PC |

---

## Подключённые инструменты и интеграции

### MCP-серверы (Model Context Protocol)

Конфигурация находится в `.cursor/mcp.json`. Серверы активны в Cursor IDE автоматически.

| Сервер | Пакет | Назначение |
|---|---|---|
| **Unity MCP** | `mcp-unity` | Прямое взаимодействие с Unity Editor — сцены, объекты, компоненты, запуск play mode |
| **GitHub MCP** | `@modelcontextprotocol/server-github` | Работа с репозиторием: PR, issues, workflows, releases |
| **Git MCP** | `mcp-server-git` (uvx) | Локальные git-операции: коммиты, ветки, diff, история |
| **Context7** | `@upstash/context7-mcp` | Актуальная документация библиотек прямо в контексте агента |
| **Filesystem MCP** | `@modelcontextprotocol/server-filesystem` | Чтение и запись файлов проекта через AI-агент |

#### Первоначальная настройка

**Unity MCP** требует установки Unity-пакета в проект:
```
https://github.com/CoderGamester/mcp-unity.git
```
Добавить через `Window → Package Manager → Add package from git URL`.

**GitHub MCP** требует токен. В `.cursor/mcp.json` заменить `<YOUR_GITHUB_TOKEN>` на Personal Access Token с правами `repo` и `workflow`:
```
GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens
```

---

### CI/CD — GameCI

Сборки и тесты автоматизированы через [GameCI](https://game.ci) на базе GitHub Actions.

Конфигурация: `.github/workflows/`

| Файл | Назначение |
|---|---|
| `ci.yml` | Запуск тестов (Unity Test Runner) и сборка проекта при push/PR |

Для работы GameCI необходимо добавить следующие секреты в репозиторий (`Settings → Secrets and variables → Actions`):

| Секрет | Описание |
|---|---|
| `UNITY_LICENSE` | Содержимое `.ulf` файла лицензии Unity |
| `UNITY_EMAIL` | Email аккаунта Unity |
| `UNITY_PASSWORD` | Пароль аккаунта Unity |

---

### Unity Test Runner via CLI

Запуск тестов локально без открытия редактора:

```powershell
# EditMode тесты
& "C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe" `
  -runTests -testPlatform EditMode `
  -projectPath "c:\Gamedev\Guildmaster - Autobattler" `
  -testResults "TestResults-EditMode.xml" `
  -batchmode -quit

# PlayMode тесты
& "C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe" `
  -runTests -testPlatform PlayMode `
  -projectPath "c:\Gamedev\Guildmaster - Autobattler" `
  -testResults "TestResults-PlayMode.xml" `
  -batchmode -quit
```

Скрипт-обёртка доступен в `scripts/run-tests.ps1`.

---

## Структура проекта

```
Guildmaster - Autobattler/
├── .cursor/
│   ├── mcp.json          # MCP-серверы для Cursor IDE
│   └── rules/            # Правила для AI-агента
├── .github/
│   └── workflows/
│       └── ci.yml        # GameCI pipeline
├── Assets/               # Игровые ассеты и скрипты
├── Packages/             # Unity Package Manager
├── ProjectSettings/      # Настройки Unity проекта
└── scripts/
    └── run-tests.ps1     # Запуск тестов через CLI
```

---

## Быстрый старт

1. Клонировать репозиторий
2. Открыть проект в Unity Hub (версия `6000.4.8f1`)
3. Открыть проект в Cursor IDE — MCP-серверы подключатся автоматически
4. Установить Unity MCP пакет через Package Manager (см. выше)
5. Добавить `GITHUB_TOKEN` в `.cursor/mcp.json`
