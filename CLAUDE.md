# Guildmaster — Autobattler: AI Agent Guide

Кооперативный автобатлер-рогалик в реальном времени (с паузой) на Unity 6. Этот файл читается Claude Code и Cursor AI автоматически. `AGENTS.md` в корне — указатель сюда же (для Codex); содержимое живёт только здесь.

## Проект

| Параметр | Значение |
|---|---|
| Движок | Unity 6000.4.8f1 |
| Язык | C# |
| Платформа | Windows / PC |
| Репозиторий | GitHub |

---

## Технологический стек

Полное обоснование — [`docs/wiki/tech/10-reference/tech-stack.md`](docs/wiki/tech/10-reference/tech-stack.md).

> Колонка «В коде» — используется ли пакет геймплейным кодом на самом деле. «Установлен» и «работает» — разные вещи, и агенты на этом путались.

### Архитектура / DI

| Пакет | Где лежит | В коде | Назначение |
|---|---|---|---|
| **VContainer** 1.18.0 | `Packages/manifest.json` | да | DI-контейнер. Никаких синглтонов. Зависимости — только через инъекцию. |
| **MessagePipe** | `Packages/manifest.json` | да | Pub/sub EventBus через VContainer. Развязка Combat → UI/Audio/VFX. |

### Async / Анимации

| Пакет | В коде | Назначение |
|---|---|---|
| **UniTask** | да | Zero-alloc async/await. Использовать вместо корутин для всего time-based. |
| **LitMotion** | точечно | Zero-alloc твины. Пока живёт в одном месте (`Presentation/UnitView.cs`) — UI-анимации и боевые цифры на него ещё не переведены. |

### Сохранения / Данные

| Пакет | В коде | Назначение |
|---|---|---|
| **Easy Save 3** | **нет** | **Референс, не бэкенд** (реш. 2026-07-26). Держим ради готовой модели версионирования и облака; сохраняем своим кодом. Из `Guildmaster.*` не вызывается: у ES3 нет asmdef. Тянет за собой `com.unity.visualscripting` — пакет удалять нельзя. |
| **Newtonsoft.Json** 3.2.2 | **нет** | Установлен, но DTO сериализуются `UnityEngine.JsonUtility`. |
| **Addressables** 2.3.16 | косвенно | Прямой загрузки по адресу в коде нет — работает только как основа Localization. |
| **Unity Localization** 1.5.3 | да | Локализация EN + RU (`Assets/_Project/Localization`). Ключи закладывать в SO сразу. |

> **Живой и единственный сейв — `JsonFileSaveService`** (`JsonUtility` в `Application.persistentDataPath`, атомарная запись через временный файл + `.bak`, битый файл в `.corrupt`) за интерфейсом `ISaveService`. Сохраняем **данные, а не объекты**: durable-состояние забега — плоский DTO по строковым id, боевые сущности собираются из него фабрикой. Поэтому сильные стороны ES3 (графы объектов, ссылки на `UnityEngine.Object`, полиморфизм) адресуют проблему, которой у нас нет, — решение 2026-07-26: бэкенд свой.
>
> Steam Cloud за это не платит: Auto-Cloud синхронизирует файлы по маске пути и не знает, кто их писал. Ближайший долг сейв-слоя — читать `RunState.SchemaVersion` и мигрировать (сейчас пишется, но не читается).

### Мультиплеер / Steam

| Пакет | Где лежит | Назначение |
|---|---|---|
| **NGO** 2.11.2 | `Packages/manifest.json` | Netcode for GameObjects — host-authoritative сетевой слой. |
| **Facepunch.Steamworks** | `Assets/Plugins/Facepunch.Steamworks/` | Steam-интеграция. `steam_api64.dll` в `redistributable_bin/win64/`. Инициализация — `Net/FacepunchTransportBootstrap.cs`. |
| **MPPM** 1.3.2 | `Packages/manifest.json` | Тест кооп в редакторе (до 4 виртуальных игроков). |

### Редактор / Инспектор

| Пакет | Где лежит | Назначение |
|---|---|---|
| **Odin Inspector** | `Assets/Plugins/Sirenix/` | Расширенный Inspector. `[SerializeReference]`-дропдауны для полиморфных данных. Подключается в Editor-сборках через `overrideReferences` + `precompiledReferences`. |
| **Quantum Console** (QFSW) | `Assets/Plugins/QFSW/` | Рантайм-консоль dev-команд. Команды — в `Guildmaster.DevTools`. |
| **Roslyn** | `Assets/Plugins/Roslyn/` | Даёт дефайн `USE_ROSLYN` для `validate_script` Unity MCP. |

### Аудио

| Пакет | Где лежит | Назначение |
|---|---|---|
| **FMOD** | `Assets/Plugins/FMOD/` | Адаптивная музыка и звук. **Всегда за интерфейсом `IAudioService`** — не дёргать FMOD API напрямую из игровой логики. Живая реализация — `FmodAudioService`, банки в `Assets/StreamingAssets`. |

### Ввод и камера

| Пакет | Где лежит | Назначение |
|---|---|---|
| **Input System** 1.19.0 | `Packages/manifest.json` | Весь игровой ввод — за интерфейсом `IInputService` (`Guildmaster.Core.Input`), карты действий строятся в коде, контексты по фазе игры. Не дёргать Input System напрямую из геймплея. См. [`10-reference/input-camera`](docs/wiki/tech/10-reference/input-camera.md). |
| **Cinemachine** 3.1.7 | `Packages/manifest.json` | Камера: 4 режима (`Action` / `Overview` / `Dev` / `Map`), `CameraModeController` в `Guildmaster.Presentation`. Боевые режимы клампятся зоной арены, `Map` — своей зоной карты. |

### 2D-пайплайн

| Пакет | Назначение |
|---|---|
| **2D Aseprite** 4.0.2, **PSD Importer** 13.0.3, **2D Animation** 14.0.4 | Импорт арта и скелетная анимация персонажей. Скрипты экспорта — каталог `Aseprite/`. |
| **Shapes** (Freya Holmer) | `Assets/Shapes/` (не `Plugins/`). Векторная отрисовка, используется в `Guildmaster.Presentation`. |

### Установлено, но в коде не используется

| Пакет | Статус |
|---|---|
| **ProBuilder** 6.1.2 | В геймплей-коде не используется; нужен как зависимость группы `probuilder` в Unity MCP. |
| **Visual Effect Graph** 17.4.0 | Не используется: ни одного `.vfx`-ассета. Боевые VFX сделаны своим слоем (`VfxData` → префаб → пул), не на VFX Graph. |

---

## Правила и конвенции

Детальные правила — в `.cursor/rules/`. Помеченные `alwaysApply` применяются к каждому запросу.

| Файл | Содержание |
|---|---|
| `project-context.mdc` | Стандарты кода C#/Unity, рабочий процесс агента, HARD-правила проекта |
| `git-conventions.mdc` | Формат коммитов, стратегия веток |
| `agent-workflows.mdc` | Пайплайн `refresh_unity` / `.meta`, нарезка спрайтов, работа с Unity MCP |
| `phase-design-pipeline.mdc` | Design-first: план идёт впереди кода |
| `obsidian-conventions.mdc` | Конвенции vault `docs/wiki` |
| `unity-csharp.mdc` | Правила по `**/*.cs` |

Чеклист настройки проекта — [`30-how-to/project-setup`](docs/wiki/tech/30-how-to/project-setup.md).

---

## Скиллы-контуры

Под `.claude/skills/` живут проектные скиллы `xgaida-x-nixi-*` — по одному на подсистему. Каждый владеет своей территорией: `combat-sim` (боевая симуляция), `data-authoring` (SO/данные/id), `gamefeel-vfx` (джус и VFX), `audio` (звук за `IAudioService`), `uitk` (UI Toolkit), `balance` (петля баланса), `content-design` (оркестрация нового контента), плюс два писаря — `gdd-scribe` (ведёт `docs/wiki/gdd`) и `tech-scribe` (ведёт `docs/wiki/tech`).

Правило разделения: реализационные скиллы владеют **кодом**, писари — **документацией о коде**. Правишь систему — обновление её тех-доки делегируется `tech-scribe`.

---

## Доступные MCP-инструменты

**Канон — версионируемый корневой `.mcp.json`.** В нём описан ровно один сервер: `unityMCP`.

| Задача | Сервер | Инструменты |
|---|---|---|
| Сцены, объекты, компоненты, play mode, тесты | **unityMCP** (`.mcp.json`) | `manage_scene`, `manage_gameobject`, `read_console`, `run_tests`, `refresh_unity` и др. |

> Unity MCP — это [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp).
> - **Сервер:** `mcpforunityserver==10.0.0` через `uvx`, команда `mcp-for-unity`, транспорт **`stdio`** (не HTTP).
> - **Редакторный пакет:** `com.coplaydev.unity-mcp`, ставится по git URL `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.0.0` (Package Manager → Add from git URL).
> - **Мост:** StdioBridgeHost внутри Unity, порт `6400`. `Window → MCP for Unity` показывает статус подключения; окно должно быть открыто.
> - Проверка коннекта: ресурс `mcpforunity://instances` (`instance_count ≥ 1` = редактор подключён).

**Прочие серверы приходят из личного окружения агента, а не из проекта.** `.cursor/mcp.json` — в `.gitignore`, то есть у каждого свой и в репозитории его нет. Не опирайся на то, что какой-то сервер будет: проверь свой список инструментов.

Что делать, когда сервера нет (обычный случай):

| Нужно | Чем делать |
|---|---|
| Git-операции | `git` через шелл. **Осторожно:** `git commit` забирает весь индекс — при параллельных сессиях коммить точечно: `git commit -F - -- <пути>` |
| PR, issues, workflows | `gh` CLI (авторизован) |
| Файлы: чтение, правка, поиск | Родные инструменты Read / Write / Edit / Grep / Glob — они лучше любого filesystem-сервера |
| Документация библиотек | `context7`, если он есть в твоём окружении; иначе веб-поиск |

---

## Структура проекта

```
Guildmaster - Autobattler/
├── .claude/skills/                   # проектные скиллы-контуры + BACKLOG.md
├── .cursor/rules/                    # 6 файлов правил (см. таблицу выше)
├── .github/workflows/
│   ├── ci.yml                        # GameCI: тесты (editmode + playmode) + сборка плеера + гейт
│   ├── docs-lint.yml                 # блокирующий гейт битых вики-ссылок
│   └── docs.yml                      # публикация сайта документации
├── Assets/                           # игровые ассеты и скрипты Unity
├── Aseprite/                         # скрипты экспорта арта
├── docs/                             # вся документация; docs/wiki — Obsidian-vault
├── Packages/                         # Unity Package Manager
├── ProjectSettings/                  # настройки Unity
├── quartz-config/, doxygen/          # конфиги сайта документации
└── scripts/
    ├── run-tests.ps1                 # локальный запуск тестов
    ├── check-wiki-links.ps1          # проверка ссылок vault (тот же гейт, что в CI)
    └── statdb.ps1                    # правка статов в YAML-ассетах мимо Unity
```

---

## CI/CD

`ci.yml`: `changes` (paths-filter) → `test` (`game-ci/unity-test-runner@v4`, editmode + playmode) + `build` (`game-ci/unity-builder@v4`, StandaloneWindows64) → `ci-gate`.

Сборка гоняется **только на pull request и на master** — она дороже тестов, а push в dev ими и так закрыт. Артефакт билда не публикуется: job отвечает на вопрос «собирается ли», а не «дай поиграть». Плеер собирается на Linux-раннере, потому что Standalone у нас на **Mono** (IL2CPP в `ProjectSettings` задан только для Android); переезд Standalone на IL2CPP потребует windows-раннера.

```powershell
./scripts/run-tests.ps1
```

Нужные секреты в GitHub: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.

Документацию обслуживают отдельные workflow: `docs-lint.yml` (блокирует PR при битых ссылках в vault) и `docs.yml` (публикация сайта).
