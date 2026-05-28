# Подготовка нового Unity-проекта к разработке

Пошаговый чеклист. Проходить по порядку.

---

## Шаг 1 — Git и GitHub

- [x] Создать репозиторий на GitHub
- [x] Добавить `.gitignore` для Unity ([шаблон](https://github.com/github/gitignore/blob/main/Unity.gitignore))
- [x] Сделать первый коммит с базовой структурой проекта
- [x] Создать ветку `dev` от `master` и переключиться на неё
- [x] Запушить обе ветки в remote

```powershell
git checkout -b dev
git push -u origin master
git push -u origin dev
```

---

## Шаг 2 — Структура папок в Assets/

- [x] Создать `Assets/_Project/` со всеми подпапками
- [x] Создать `Assets/Tests/EditMode/` и `Assets/Tests/PlayMode/`

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

> Карта сборок и правила: `guildmaster-wiki/assembly-definitions.md`

---

## Шаг 3 — Unity пакеты

- [x] **Input System** — `com.unity.inputsystem`
- [x] **Test Framework** — `com.unity.test-framework`
- [x] **mcp-unity** — для интеграции с Cursor AI

> После установки mcp-unity: `Window → MCP Unity → Server Window` → запустить сервер.

---

## Шаг 4 — Cursor AI

- [x] Создать `.cursor/mcp.json` с серверами: unity, github, git, context7, filesystem
- [x] Создать `CLAUDE.md` — точка входа для агентов
- [x] Создать `.cursor/rules/project-context.mdc` — стандарты кода, рабочий процесс
- [x] Создать `.cursor/rules/git-conventions.mdc` — правила коммитов и веток
- [x] Создать `guildmaster-wiki/agent-pipeline.md` — этот файл

---

## Шаг 5 — Документация

- [x] Создать Obsidian Vault в папке `guildmaster-wiki/`
- [x] Создать GDD-документы: overview, core-loop, units, combat, guild, progression
- [x] Создать `guildmaster-wiki/assembly-definitions.md` — карта сборок

---

## Шаг 6 — CI/CD

- [x] Создать `.github/workflows/ci.yml` на основе [GameCI](https://game.ci)
- [x] Добавить секреты в GitHub репозитории: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`
- [x] Настроить branch protection: `master` и `dev` требуют прохождения CI
- [x] Создать `scripts/run-tests.ps1` для локального запуска тестов
- [x] Убедиться, что CI pipeline проходит зелёным ✅

### Активация Unity License для CI

Лицензия уже есть локально: `C:\ProgramData\Unity\Unity_lic.ulf`

Добавить секреты: `GitHub → Settings → Secrets and Variables → Actions`

| Секрет | Значение |
|---|---|
| `UNITY_LICENSE` | Содержимое `.ulf` файла |
| `UNITY_EMAIL` | Email Unity аккаунта |
| `UNITY_PASSWORD` | Пароль Unity аккаунта |

---

## Шаг 7 — Финальный коммит

```
chore: initial project setup with CI, MCP, and Cursor rules
```
