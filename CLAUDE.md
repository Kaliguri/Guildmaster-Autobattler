# Guildmaster — Autobattler: AI Agent Guide

Кооперативный автобатлер-рогалик в реальном времени (с паузой) на Unity 6000.4.8f1, C#, Windows/PC.
Этот файл читается Claude Code и Cursor AI автоматически. `AGENTS.md` в корне — указатель сюда же
(для Codex); содержимое живёт только здесь.

**Что здесь есть и чего нет.** Здесь — правила и ловушки, которых не видно из файлов. Инвентарь
пакетов сюда не переписывается: он в `Packages/manifest.json` и `Assets/Plugins/`, обоснование
выбора — [`10-reference/tech-stack`](docs/wiki/tech/10-reference/tech-stack.md).

---

## Правила слоёв

Всё, что ниже, — про то, как к подсистеме обращаться. Обход шва считается дефектом, а не срезкой.

| Слой | Правило |
|---|---|
| DI | **VContainer**, никаких синглтонов: зависимости только инъекцией. |
| События | **MessagePipe** pub/sub через VContainer — развязка Combat → UI / Audio / VFX. |
| Асинхронность | **UniTask** вместо корутин для всего time-based. |
| Звук | Только за `IAudioService`. FMOD API из игровой логики не дёргать. |
| Ввод | Только за `IInputService` (`Guildmaster.Core.Input`): карты действий строятся в коде, контексты по фазе игры. См. [`10-reference/input-camera`](docs/wiki/tech/10-reference/input-camera.md). |
| Камера | **Cinemachine**, 4 режима (`Action` / `Overview` / `Dev` / `Map`), `CameraModeController`. Боевые режимы клампятся зоной арены, `Map` — своей. |
| Текст | Локализация EN + RU: ключи закладываются в SO сразу, не «потом». |
| Твины | **LitMotion**, но пока живёт точечно (`Presentation/UnitView.cs`) — UI-анимации и боевые цифры на него ещё не переведены. |

## Ловушки стека

То, на чём агенты уже спотыкались и споткнутся снова.

- **Easy Save 3 — референс, а не бэкенд** (решение 2026-07-26). Из `Guildmaster.*` не вызывается: у
  ES3 нет asmdef. **Тянет за собой `com.unity.visualscripting` — пакет удалять нельзя**, компиляция
  падает. Потребителей пакета искать по всему `Assets/`, не по `_Project`.
- **Newtonsoft.Json** авто-ссылается на все сборки (`isExplicitlyReferenced: 0`), правка asmdef не
  нужна. Готча: `Vector2` требует конвертера, иначе сериализатор уходит в рекурсию по `normalized`.
- **Addressables** прямой загрузки по адресу в коде не имеют — живут только как основа Localization.
- **Visual Effect Graph** установлен, но не используется: ни одного `.vfx`. Боевые VFX — свой слой
  (`VfxData` → префаб → пул). **ProBuilder** нужен как зависимость группы `probuilder` в Unity MCP.
- **Shapes** лежит в `Assets/Shapes/`, а не в `Plugins/`. **Odin** подключается в Editor-сборках через
  `overrideReferences` + `precompiledReferences`. **Roslyn** даёт дефайн `USE_ROSLYN` для
  `validate_script`.
- **Steam:** `steam_api64.dll` в `Facepunch.Steamworks/redistributable_bin/win64/`, инициализация —
  `Net/FacepunchTransportBootstrap.cs`. **MPPM** гоняет кооп в редакторе (до 4 виртуальных игроков).

## Сохранения

> Раздел держит правду в одиночку: `10-reference/tech-stack` и `10-reference/saves` в этой части
> отстали (числят бэкендом Easy Save и `persistentDataPath`). Их правка отложена до рефактора кода —
> реестр расхождений в `docs/tech-docs-sync-plan.md`.

Живой и единственный сейв — **`JsonFileSaveService`** за интерфейсом `ISaveService`: Newtonsoft,
каталог `Saves/` под корнем `GameDataPath`, атомарная запись через временный файл + `.bak`, битый
файл уезжает в `.corrupt`.

- **Корень — `LocalLow/Alebardium/Guildmaster/`, а НЕ `persistentDataPath`.** Последний растёт из
  `productName`, и переименование игры унесло бы сейвы игроков вместе с маской Steam Cloud. Кодовые
  имена в `GameDataPath` не менять никогда — инвариант держит `GameDataPathTests`.
- **Сохраняем данные, а не объекты:** durable-состояние забега — плоский DTO по строковым id, боевые
  сущности собираются из него фабрикой. Поэтому сильные стороны ES3 (графы объектов, ссылки на
  `UnityEngine.Object`, полиморфизм) решают проблему, которой у нас нет.
- **Ключ сейва — это путь** в дереве: `prefs`, `profiles/{id}/profile`,
  `profiles/{id}/guilds/{gid}/run`. Каждый файл — конверт (`schemaVersion` + `gameVersion` +
  `payload`); версия схемы объявляется атрибутом `[SaveSchema]` на DTO, не полем внутри состояния.
  Загрузка возвращает `SaveLoadResult<T>`: `Ok` / `Missing` / `Corrupted` / `TooNew` (сейв из более
  новой версии игры — **не грузим и не затираем**) / `Unsupported`.
- **Steam Auto-Cloud** синхронизирует по маске пути и не знает, кто писал файл. Маска —
  `Saves/**/*.json`, поэтому служебные суффиксы идут ПОСЛЕ расширения (`run.json.bak`).
- **Данные компьютера — отдельное хранилище:** `ILocalSaveService` → `Local/` (вне облачной маски),
  там `machine.json` за `IDisplayService`. Синхронизировать нельзя: чужое разрешение на втором ПК в
  худшем случае даёт чёрный экран. Частоту обновления Unity меняет только в эксклюзивном
  полноэкранном; разрешение Unity сам пишет в реестр Windows до первой сцены — владельцем остаётся
  наш файл, потому что мы применяем поверх.

Полное ТЗ и фазы — [`40-planning/save-system`](docs/wiki/tech/40-planning/save-system.md).

---

## Правила и конвенции

**Как писать код — [`10-reference/code-standards`](docs/wiki/tech/10-reference/code-standards.md).**
Там инварианты («чего никогда»), именование, документирование, детерминизм, политика фолбэков и
корень редакторного меню. Читать перед первой правкой `.cs` в сессии; здесь эти правила намеренно
не продублированы.

Процедуры работы — в `.cursor/rules/`: `git-conventions` (коммиты и ветки),
`agent-workflows` (пайплайн `refresh_unity` / `.meta`, нарезка спрайтов, готчи Quartz),
`phase-design-pipeline` (design-first), `obsidian-conventions` (vault),
`project-context` (тонкий указатель для Cursor). Cursor читает `.cursor/rules` и `AGENTS.md`,
но не этот файл — поэтому владельцем правил кода назначен документ вики, общий для обоих.

Чеклист настройки проекта — [`30-how-to/project-setup`](docs/wiki/tech/30-how-to/project-setup.md).

## Скиллы-контуры

Под `.claude/skills/` живут проектные скиллы `xgaida-x-nixi-*` — по одному на подсистему:
`combat-sim`, `data-authoring`, `gamefeel-vfx`, `audio`, `uitk`, `balance`, `content-design`, плюс два
писаря — `gdd-scribe` (`docs/wiki/gdd`) и `tech-scribe` (`docs/wiki/tech`).

Правило разделения: реализационные скиллы владеют **кодом**, писари — **документацией о коде**.
Правишь систему — обновление её тех-доки делегируется `tech-scribe`.

## MCP-инструменты

**Канон — версионируемый корневой `.mcp.json`, в нём ровно один сервер: `unityMCP`**
([CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp), `mcpforunityserver==10.0.0` через
`uvx`, транспорт `stdio`). Редакторный пакет — `com.coplaydev.unity-mcp`, мост StdioBridgeHost внутри
Unity на порту `6400`; окно `Window → MCP for Unity` должно быть открыто. Проверка коннекта — ресурс
`mcpforunity://instances` (`instance_count ≥ 1`).

**Прочие серверы приходят из личного окружения агента, а не из проекта** (`.cursor/mcp.json` в
`.gitignore`). Не рассчитывай, что сервер будет — проверь свой список инструментов. Без него:
git и `gh` CLI через шелл, файлы — родными Read / Write / Edit / Grep / Glob, документация библиотек
— `context7` или веб-поиск.

**Осторожно с git:** `git commit` забирает весь индекс. При параллельных сессиях коммитить точечно —
`git commit -F - -- <пути>`.

---

## Где что лежит

Дерево смотри сам; неочевидно только это:

- `Assets/_Project/` — наш код и контент; всё остальное под `Assets/` — вендор.
- `docs/` — вся документация, из неё `docs/wiki` — единственный Obsidian-vault (`gdd` + `tech`).
- `Aseprite/` — скрипты экспорта арта; `quartz-config/`, `doxygen/` — конфиги сайта документации.
- `scripts/` — `run-tests.ps1` (локальный прогон), `check-wiki-links.ps1` (тот же гейт, что в CI),
  `statdb.ps1` (правка статов в YAML-ассетах мимо Unity).

## CI/CD

`ci.yml`: `changes` (paths-filter) → `test` (`unity-test-runner`, editmode + playmode) + `build`
(`unity-builder`, StandaloneWindows64) → `ci-gate`. Секреты: `UNITY_LICENSE`, `UNITY_EMAIL`,
`UNITY_PASSWORD`.

Сборка гоняется **только на pull request и на master** — она дороже тестов, а push в dev ими и так
закрыт. Артефакт не публикуется: job отвечает на вопрос «собирается ли». Плеер собирается на
**Linux**-раннере, потому что Standalone у нас на Mono (IL2CPP в `ProjectSettings` задан только для
Android) — переезд Standalone на IL2CPP потребует windows-раннера.

Документацию обслуживают `docs-lint.yml` (блокирует PR при битых ссылках в vault) и `docs.yml`
(публикация сайта).

```powershell
./scripts/run-tests.ps1
```
