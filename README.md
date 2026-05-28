# Guildmaster — Autobattler

> Пошаговый автобатлер-рогалик с кооперативным управлением гильдией. В разработке.

![Unity](https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity) ![Platform](https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue) ![Status](https://img.shields.io/badge/Status-In%20Development-yellow) ![License](https://img.shields.io/badge/License-All%20Rights%20Reserved-red)

---

## Об игре

**Guildmaster** — это пошаговый автобатлер с элементами рогалика, вдохновлённый *Slay the Spire*, *Across the Obelisk* и *Teamfight Manager*.

Вы возглавляете гильдию авантюристов, готовящихся к великому чемпионату. Перед каждой битвой вы распределяете осколки героев между бойцами, расставляете их на поле и настраиваете тактику — а дальше смотрите, как они сражаются сами. Управление исходом боя осуществляется через заклинания Гильдмастера.

Игра поддерживает **1–4 игроков**: соло — и вы управляете всей гильдией, в кооперативе — каждый отвечает за своих бойцов.

### Ключевые механики

- **Осколки героев** — экипируемые артефакты, превращающие обычного гильдийца в бойца с уникальными способностями
- **Гильдмастер** — не сражается, но кастует заклинания во время боя; его слоты ограничены и перезаряжаются между сражениями
- **Автобой** — бой идёт сам, без микроменеджмента атак
- **Карта в стиле STS** — ивенты, магазины, тренировки, боссы в конце акта
- **Кооператив** — от 1 до 4 игроков делят гильдийцев между собой

---

## Технический стек

| | |
|---|---|
| **Движок** | Unity 6 (6000.4.8f1) |
| **Язык** | C# |
| **Платформа** | Windows / PC |
| **CI/CD** | GitHub Actions + [GameCI](https://game.ci) |

---

## Архитектура проекта

```
Guildmaster - Autobattler/
├── Assets/
│   └── _Project/           # Весь игровой код и контент
│       ├── Scripts/         # C# — логика игры
│       ├── ScriptableObjects/ # Данные: юниты, осколки, настройки
│       ├── Prefabs/         # Игровые объекты
│       ├── Scenes/          # Сцены
│       └── UI/              # UI-ассеты
├── Assets/Tests/
│   ├── EditMode/            # Юнит-тесты
│   └── PlayMode/            # Интеграционные тесты
├── guildmaster-wiki/        # GDD и техническая документация (Obsidian Vault)
├── .github/workflows/       # CI: тесты и сборка
├── .cursor/                 # Конфигурация AI-агента (Cursor IDE)
│   ├── mcp.json             # MCP-серверы
│   └── rules/               # Стандарты кода и git-конвенции
└── scripts/
    └── run-tests.ps1        # Локальный запуск тестов
```

**Принципы кода:**
- `ScriptableObject` для всех игровых данных — никакого хардкода
- `ObjectPool<T>` вместо `Instantiate`/`Destroy` в горячих путях
- Компонентные ссылки кэшируются в `Awake()`, никогда в `Update()`
- Unity Input System (не Legacy Input)

---

## Документация

GDD и техническая документация находятся в папке [`guildmaster-wiki/`](guildmaster-wiki/) — открывать как Obsidian Vault.

| Документ | Содержание |
|---|---|
| [GDD/01-overview.md](guildmaster-wiki/GDD/01-overview.md) | Концепция, жанр, целевая аудитория |
| [GDD/02-стадии реализации проекта.md](guildmaster-wiki/GDD/02-стадии%20реализации%20проекта.md) | Дорожная карта разработки |
| [assembly-definitions.md](guildmaster-wiki/assembly-definitions.md) | Карта сборок C# |
| [agent-pipeline.md](guildmaster-wiki/agent-pipeline.md) | Настройка AI-агента и MCP |

---

## CI/CD

Тесты и сборка автоматизированы через [GameCI](https://game.ci) (GitHub Actions).

| Файл | Назначение |
|---|---|
| `.github/workflows/ci.yml` | Запуск Unity Test Runner и сборка при push/PR |

Локальный запуск тестов:
```powershell
./scripts/run-tests.ps1
```

---

## Лицензия

**© 2026 Max Gaida. Все права защищены.**

Проект опубликован в открытом доступе исключительно в целях демонстрации в портфолио.  
Использование, копирование, распространение или создание производных работ **без письменного разрешения автора запрещено**.

See [LICENSE](LICENSE) for details.
