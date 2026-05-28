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

```
Assets/
├── _Project/
│   ├── Scripts/
│   ├── ScriptableObjects/
│   ├── Prefabs/
│   ├── Scenes/
│   ├── Art/
│   │   ├── Sprites/
│   │   └── Animations/
│   ├── UI/
│   └── Audio/
└── Tests/
    ├── EditMode/
    └── PlayMode/
```

---

## Шаг 3 — Unity пакеты

- [ ] **Input System** — `com.unity.inputsystem`
- [ ] **Test Framework** — `com.unity.test-framework`
- [ ] **mcp-unity** — для интеграции с Cursor AI

> После установки mcp-unity: `Window → MCP Unity → Server Window` → запустить сервер.

---

## Шаг 4 — Cursor AI

- [ ] Создать `.cursor/mcp.json` с серверами: unity, github, git, context7, filesystem
- [ ] Создать `CLAUDE.md` — точка входа для агентов
- [ ] Создать `.cursor/rules/project-context.mdc` — стандарты кода, рабочий процесс
- [ ] Создать `.cursor/rules/git-conventions.mdc` — правила коммитов и веток
- [ ] Создать `docs/agent-pipeline.md` — этот файл

---

## Шаг 5 — CI/CD

- [ ] Создать `.github/workflows/ci.yml` на основе [GameCI](https://game.ci)
- [ ] Добавить секреты в GitHub репозитории: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`
- [ ] Создать `scripts/run-tests.ps1` для локального запуска тестов
- [ ] Сделать тестовый push и убедиться, что pipeline проходит

---

## Шаг 6 — Финальный коммит

```
chore: initial project setup with CI, MCP, and Cursor rules
```
