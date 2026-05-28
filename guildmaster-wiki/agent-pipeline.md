# Подготовка нового Unity-проекта к разработке

Пошаговый чеклист. Проходить по порядку.

---

## Шаг 1 — Git и GitHub

- [ ] Создать репозиторий на GitHub
- [ ] Добавить `.gitignore` для Unity ([шаблон](https://github.com/github/gitignore/blob/main/Unity.gitignore))
- [ ] Сделать первый коммит с базовой структурой проекта
- [ ] Создать ветку `dev` от `master` и переключиться на неё
- [ ] Запушить обе ветки в remote

```powershell
git checkout -b dev
git push -u origin master
git push -u origin dev
```

---

## Шаг 2 — Структура папок в Assets/

- [ ] Создать `Assets/_Project/` со всеми подпапками
- [ ] Создать `Assets/Tests/EditMode/` и `Assets/Tests/PlayMode/`

```
Assets/
├── _Project/
│   ├── Scripts/
│   │   ├── Core/         ← Guildmaster.Core.asmdef
│   │   ├── Units/        ← Guildmaster.Units.asmdef
│   │   ├── Combat/       ← Guildmaster.Combat.asmdef
│   │   ├── Guild/        ← Guildmaster.Guild.asmdef
│   │   └── UI/           ← Guildmaster.UI.asmdef
│   ├── ScriptableObjects/
│   ├── Prefabs/
│   ├── Scenes/
│   ├── Art/
│   │   ├── Sprites/
│   │   └── Animations/
│   ├── UI/
│   └── Audio/
└── Tests/
    ├── EditMode/         ← Guildmaster.Tests.EditMode.asmdef
    └── PlayMode/         ← Guildmaster.Tests.PlayMode.asmdef
```

---

## Шаг 3 — Assembly Definitions

- [ ] Создать `.asmdef` файлы для каждого модуля в `Scripts/`
- [ ] Создать `.asmdef` для `Tests/EditMode/` и `Tests/PlayMode/`
- [ ] Создать `wiki/assembly-definitions.md` с картой сборок и правилами

Зависимости:
```
Core ← Units ← Combat
               Guild ← UI
```

---

## Шаг 4 — Unity пакеты

- [ ] **Input System** — `com.unity.inputsystem`
- [ ] **Test Framework** — `com.unity.test-framework`
- [ ] **mcp-unity** — для интеграции с Cursor AI

> После установки mcp-unity: `Window → MCP Unity → Server Window` → запустить сервер.

---

## Шаг 5 — Cursor AI

- [ ] Создать `.cursor/mcp.json` с серверами: unity, github, git, context7, filesystem
- [ ] Создать `CLAUDE.md` — точка входа для агентов
- [ ] Создать `.cursor/rules/project-context.mdc` — стандарты кода, рабочий процесс
- [ ] Создать `.cursor/rules/git-conventions.mdc` — правила коммитов и веток
- [ ] Создать `wiki/agent-pipeline.md` — этот файл

---

## Шаг 6 — Документация (Obsidian Vault)

- [ ] Создать папку `wiki/` как Obsidian Vault
- [ ] Создать `wiki/.obsidian/app.json` с базовыми настройками
- [ ] Создать `wiki/index.md` — главная страница Vault
- [ ] Создать GDD-документы: `GDD/01-overview`, `02-core-loop`, `03-units`, `04-combat`, `05-guild`, `06-progression`
- [ ] Добавить в `.gitignore`: `wiki/.obsidian/workspace.json`, `workspace-mobile.json`, `cache`

---

## Шаг 7 — CI/CD

- [ ] Создать `.github/workflows/ci.yml` на основе [GameCI](https://game.ci)
- [ ] Добавить секреты в GitHub: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`
- [ ] Настроить branch protection на `master` и `dev` (требует прохождения CI)
- [ ] Создать `scripts/run-tests.ps1` для локального запуска тестов
- [ ] Сделать тестовый push и убедиться, что pipeline проходит

### Активация Unity License

Лицензия находится локально: `C:\ProgramData\Unity\Unity_lic.ulf`

Если файла нет: Unity Hub → Preferences → Licenses → Add → Get a free personal license.

Добавить секреты: `GitHub → Settings → Secrets and Variables → Actions`

| Секрет | Значение |
|---|---|
| `UNITY_LICENSE` | Содержимое `.ulf` файла (весь XML) |
| `UNITY_EMAIL` | Email Unity аккаунта |
| `UNITY_PASSWORD` | Пароль Unity аккаунта |

> Branch protection требует публичного репозитория или GitHub Pro.

---

## Шаг 8 — Финальный коммит

```
chore: initial project setup with CI, MCP, and Cursor rules
```
