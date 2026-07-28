---
title: "Аудит - шрифты и прочее"
status: ready
updated: 2026-07-20
---

> Приложение к [[tech/10-reference/asset-inventory|Reference - Asset Inventory]]. Собрано разведкой 2026-07-20.

## Шрифты и прочее (собрано Никси, не агентом)

### Шрифты — `Assets/_Project/UI/Fonts`

14 ttf, но запечено в TMP-атласы только три: `FiraSans-Regular SDF`, `FiraSans-Bold SDF`,
`CormorantGaramond-Medium SDF`.

Подключены в `Theme/components.uss` **по пути, а не по guid**:

| Строка | Шрифт | Где |
|---|---|---|
| `components.uss:827` | FiraSans-Regular SDF | базовый текст |
| `components.uss:833` | CormorantGaramond-Medium SDF | заголовки |
| `components.uss:1569` | CormorantGaramond-Medium SDF | второй блок |

ВАЖНАЯ ГОТЧА для будущих проверок: раз шрифты подключены через `-unity-font-definition: url(...)`,
поиск ссылок ПО GUID их не находит — файл выглядит «ничей», хотя используется. Проверять по имени.

`FiraSans-Bold SDF` запечён, но в USS отдельно не подключён (жирность идёт через `-unity-font-style: bold`).

**Лежат без дела (ttf без атласа, никуда не подключены):** CormorantGaramond.ttf,
CormorantGaramond-SemiBold.ttf, Forum-Regular.ttf, Handjet.ttf, PixelifySans.ttf, PTSans-Bold.ttf,
PTSans-Regular.ttf, YesevaOne-Regular.ttf. Это остатки подбора шрифтовой пары — не мусор, а запас,
но и не «у нас есть шрифты», пока не запечены.

### UI-тема — `Assets/_Project/UI`

- `GuildmasterPanelSettings.asset` — общий PanelSettings; `Theme/GuildmasterRuntimeTheme.tss` тянет
  дефолтную тему Unity + нашу `theme.uss`.
- USS-слои: `tokens.primitives.uss` → `tokens.semantic.uss` → `components.uss` (дизайн-система:
  пергамент, чернила, латунь).
- 17 uxml-экранов, 5 uss.
- `Dev/PreviewPanelSettings.asset` + `Dev/DevBattlePicker.uss` — дев-инструменты.

### Мусор и кандидаты на чистку

| Что | Размер | В git | Вердикт |
|---|---|---|---|
| `Assets/UI/RT_MCP_UI_Render_78796.renderTexture` | — | нет | Мусор: render texture, оставленная MCP-инструментом. Удалить вместе с папкой `Assets/UI`. |
| `Assets/Screenshots` | 6.3 МБ, 172 файла | нет | Рабочие скрины сессий. Не в git и правильно — но лежат в `Assets/`, то есть Unity их импортирует. Перенести из `Assets/` или занести в .gitignore явно. |

### Не разбиралось (код, не контент)

`Assets/Plugins` (333 МБ) — Odin, FMOD, Facepunch.Steamworks, Easy Save. Инструменты, не контент.
`Assets/TextMesh Pro` — служебные ресурсы TMP (LiberationSans как fallback).
`Assets/Settings` — профили URP и Volume.
