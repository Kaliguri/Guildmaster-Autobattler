---
title: "How-to - Project Setup"
order: 0
status: ready
updated: 2026-07-30
---

**Как поднять проект на новой машине.** Не история первичной настройки — она в git; здесь то, что
нужно сделать, чтобы проект собрался и тесты позеленели.

> [!note] Что сверено 30.07.2026, а что нет
> Всё, что проверяется по репозиторию — версия редактора, состав пакетов и плагинов, раскладка сборок,
> MCP-канон, скрипты, submodule, ветка `gh-pages` — сверено. **Не проверялось живьём:** активация
> лицензии Unity, секреты GitHub Actions, branch protection и FMOD Studio — они вне репозитория, и
> подтвердить их может только Макс на своей машине.
>
> Предыдущая версия дока описывала проект по состоянию на май 2026 и разошлась с ним почти во всём:
> пять сборок вместо 23, тесты в `Assets/Tests`, три пакета, пять MCP-серверов и структура вики с
> нумерованными русскими именами. Это тот случай, когда how-to вреднее отсутствия how-to.

---

## 1. Что нужно на машине

| Что | Версия / примечание |
|---|---|
| **Unity** | ровно **6000.4.8f1** (`ProjectSettings/ProjectVersion.txt` — источник правды) |
| **Git + Git LFS** | LFS обязателен: арт и аудио хранятся через него (см. [[tech/30-how-to/adding-assets\|How-to - Adding Assets]]) |
| **PowerShell 7+** (`pwsh`) | им гоняются `scripts/*.ps1` |
| **`uv` / `uvx`** | нужен MCP-серверу Unity (ставится с `uv`) |
| Лицензия Unity | Personal хватает; локально лежит в `C:\ProgramData\Unity\Unity_lic.ulf` |

## 2. Клонирование

**С сабмодулем** — иначе не соберётся сайт документации (`doxygen/doxygen-awesome-css`):

```bash
git clone --recurse-submodules <url>
```

Уже клонировал без него:

```bash
git submodule update --init --recursive
```

Ветки: `master` — релизная, работа идёт в `dev` и в фиче-ветках от него.

## 3. Пакеты — руками не ставим

Всё приходит из `Packages/manifest.json` при первом открытии, включая **пакеты по git-URL**:

- `jp.hadashikick.vcontainer` (DI, 1.18.0) и `com.cysharp.messagepipe` + `.vcontainer` (шина) —
  тянутся с GitHub, поэтому первое открытие требует сети;
- `com.cysharp.unitask`, `com.annulusgames.lit-motion`, `com.unity.localization`,
  `com.unity.netcode.gameobjects`, `com.unity.multiplayer.playmode` (MPPM), `com.unity.cinemachine`,
  `com.unity.render-pipelines.universal`, 2D-набор (animation / aseprite / psdimporter / tilemap),
  `com.unity.nuget.newtonsoft-json`, `com.unity.test-framework` и `com.unity.ui.test-framework`,
  `com.coplaydev.unity-mcp`.

**Плагины лежат в репозитории**, а не в Package Manager: `Assets/Plugins/` (FMOD, Easy Save 3,
Facepunch.Steamworks, Sirenix/Odin, Roslyn, QFSW) и `Assets/Shapes/` — отдельно от `Plugins`, так
исторически.

Готчи по стеку (что нельзя удалять и почему) — `CLAUDE.md`, раздел «Ловушки стека». Обоснование выбора
библиотек — запись
[[tech/00-meta/journal/2026-07-30-library-picks-and-the-alternatives-we-turned-down|Journal - Library Picks]].

## 4. MCP: один сервер, а не пять

**Канон — версионируемый корневой `.mcp.json`, в нём ровно `unityMCP`** (`mcpforunityserver==10.0.0`
через `uvx`, транспорт stdio). Внутри Unity — редакторный пакет `com.coplaydev.unity-mcp`; окно
**`Window → MCP for Unity`** должно быть открыто, мост слушает порт `6400`.

Проверка коннекта — ресурс `mcpforunity://instances`, `instance_count ≥ 1`.

Прочие серверы (github, git, filesystem) **сознательно не подключаются** — решение 2026-07-26: git-MCP
не умеет `git commit -F - -- <пути>` (наша защита от готчи «коммит берёт весь индекс»), filesystem-MCP
хуже родных инструментов, github закрыт живым `gh` CLI, а каждый сервер постоянно ест контекст
описаниями.

## 5. Проверка, что всё поднялось

```powershell
./scripts/run-tests.ps1
```

Прогон стоит на `scripts/unity-cli.ps1`: версия редактора берётся из проекта, а тесты гоняются в
**теневом проекте** (своя `Library` вне репо поверх junction на живые `Assets`) — поэтому закрывать
редактор не нужно. **Теневой режим только для чтения:** бенч, сохраняющий ассеты, в нём гонять нельзя.

Ориентир: EditMode зелёный целиком (на 30.07.2026 — 775 тестов).

## 6. Раскладка, которую стоит знать заранее

- Наш код и контент — только под `Assets/_Project/`; всё остальное в `Assets/` — вендор.
- Тесты — `Assets/_Project/Tests/{EditMode,PlayMode}` (не `Assets/Tests`).
- Сборок 23 (`Guildmaster.Core`, `.Data`, `.Combat`, `.Presentation`, `.Game`, `.UI`, `.Guild`, `.Net`,
  `.Balance`, `.DevTools`, `.MiniGames` + Editor-сборки + две тестовые). **Карта сборок — сами
  `.asmdef`**, отдельного документа с графом нет: он расходился с реальностью быстрее, чем правился.
- Зависимости — только вниз по графу; перед созданием скрипта смотри, в какую сборку он попадёт.

## 7. CI и сайт документации

Настроено и работает, повторять на новой машине нечего:

- `ci.yml` — `changes` → `test` (EditMode + PlayMode) + `build` (StandaloneWindows64) → `ci-gate`.
  Секреты `UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD` живут в GitHub Actions.
- `docs-lint.yml` — блокирующий гейт битых вики-ссылок; локально тот же прогон:
  `./scripts/check-wiki-links.ps1`.
- `docs.yml` — публикация сайта (Quartz + Doxygen) в ветку `gh-pages`; она существует и деплой идёт.

Детали сайта — [[tech/30-how-to/docs-site|How-to - Docs Site]].
