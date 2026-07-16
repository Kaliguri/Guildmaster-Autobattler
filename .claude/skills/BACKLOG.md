# Бэклог проектных скиллов — Guildmaster

Живой список скиллов под `.claude/skills/`. Скилл заводим под РЕАЛЬНЫЙ повторяющийся
контур работы с готчами и HARD-инвариантами, а не «на будущее» (принцип Anthropic: скилл
закрывает наблюдаемый пробел). Формат каждого — процедура-чеклист + `references/` для
прогрессивного раскрытия (образец — `uitk`, `combat-sim`).

Статусы: **[готов]** · **[план]** утверждён, ждёт реализации · **[идея]** кандидат, не решён.

## Готовы

| Скилл | Покрывает |
|---|---|
| **uitk** [готов] | UI Toolkit: экраны UXML/USS, дизайн-система токенов, компоненты, MVVM, UI-тесты. |
| **combat-sim** [готов] | Боевая симуляция: детерминированное ядро 30 Гц, эффекты, displacement/separation, способности, юнит-POCO + контракт развязки sim→presentation. |
| **gdd-scribe** [готов] | Ведение ГДД (Obsidian-vault): роль писарь-редактор; append-only ADR-журнал (`0.7`), решено→разнесено (single source), термины через глоссарий (`0.4`), мета в frontmatter (`title`/`order`/`status`); артефакты — vision/столпы/Mermaid/Dataview-дашборд; целевые папки-кластеры + латинские слаги. **Тянет за собой миграцию vault (см. ниже).** |
| **data-authoring** [готов] | Контент-слой: 3 слоя `SO→POCO→DTO`, `id = domain.name` (закрытый `ContentDomains`), контент-SO (`UnitData`/`RelicData`/`EffectData`/…), стат-блок через `Override` + `StatsConfig`, реестр `IContentDatabase`, лок-ключи `{id}.suffix` (RU-only), валидация (`ContentValidationService`), запреты (Odin **Serializer**, curve в тике, `Resources.Load`, тихий null-fallback, мутация SO), Addressables только под Loc, source-namespace шов под моды (не построен). | Владеет ОПРЕДЕЛЕНИЕМ (SO/баланс/id/loc/состав); ПОВЕДЕНИЕ эффекта — `combat-sim`; ОКНО Content Hub — `content-hub`; плумбинг сейвов — `save-system`. Odin **Inspector** разрешён, Serializer забанен. |
| **gamefeel-vfx** [готов] | Джус и визуальный фидбэк: `CombatFeelDirector` (политика значимости — kill-slowmo/heavy-shake/финишер), per-hit фидбэк презентера (hitstop/вспышка/сплющивание/боевые цифры), пиксельные VFX (`CombatVfx`/`PixelBurst`), `DeathShatter`, screen shake за `IScreenShake`, feel-SO (`CombatFeelConfig`/`CombatColorPalette`/`PixelBurstPreset`), целевой шов префаб-VFX. HARD: **все VFX — префабы** (SO→префаб→пул→точка-сокет); global-feel только в FeelDirector; значения из feel-SO; presentation читает sim через события/MessagePipe, не влияет на детерминизм. | Владеет ПОВЕДЕНИЕМ джуса. **Боевое время (`TimeScaleService`) — за `combat-sim`** (джус — потребитель Cinematic-API; шов под хрономанта); звук — `audio`; ОПРЕДЕЛЕНИЕ `VfxData` SO — `data-authoring` (целевой шов, не построен). Визуальная приёмка — Макса. |
| **tech-scribe** [готов] | Ведение технической вики (`docs/wiki/tech`): роль тех-писарь-синхронизатор, **источник правды = КОД**; HARD — сверка факта с живым кодом (не по памяти), `40-planning` = архив замысла (правим только статус-шапки), мета в frontmatter (`title`/`order`/`status` из 6), починка ВСЕХ ссылок при переносе (tech+gdd кросс-вики), append-only ADR-changelog (дата+причина+куда разнесено). Автоматизация: Dataview-дашборд готовности в MOC, `scripts/check-wiki-links.ps1` + `.github/workflows/docs-lint.yml` (блокирующий гейт битых ссылок, Obsidian-aware). Diátaxis-кластеры с нумерованными папками (`00-meta`/`10-reference`/`20-explanation`/`30-how-to`/`40-planning`). | Владеет ДОКОЙ о коде; сам КОД — реализационные скиллы (делегируют синхронизацию доки СЮДА); дизайн-доку `gdd/` — `gdd-scribe`; API-автоген (Doxygen) — инфра-контур `docs-site`, вне скилла. Зеркалит `gdd-scribe` (источник правды другой). |
| **audio** [готов] | Весь аудио-контур за `IAudioService` (Core-фасад): `FmodAudioService`/`UnityAudioService`, `AudioPresenter` (слушает бой), резолвер `{contentId}.{action}` (точная→дефолт→тишина), `AudioCatalog`+`EventReference`, `AudioAction` (ординалы!), `AudioParameters` (`TimeScale`-питч), шины/громкости (`SettingsService`), банки в StreamingAssets, готча `fmodstudiocl`. HARD: всё через фасад (FMOD не трогать из геймплея); ключ строковый, не FMOD-тип; `AudioAction` только в конец. | Владеет звуковым контрактом. `TimeScale`-параметр пишет `TimeScaleService` (combat-sim); `id` контента — `data-authoring` (звук лишь резолвит); FMOD-Studio-проект/сведение — Макс. Переименован из `audio-sfx` (охват — весь звук, не только боевой SFX). |

## Ближайшая очередь (утверждено)

| Скилл | Покрывает | Границы / стык |
|---|---|---|
| **project-conventions** [план] | Bundle мелких HARD-правил: тесты-под-игру (не наоборот), loc-ключи RU-only, git-flow master/dev + squash→delete + коммиты только от Max Gaida. | Сквозной; gdd вынесен в отдельный `gdd-scribe`. |

## Кандидаты (обсудить)

| Скилл | Зачем | Заметка |
|---|---|---|
| **content-hub** [идея] | Редакторный UITK-инструмент (окно Content Hub, P0–P9): браузер/CRUD/валидация/Balance-бейк дата-слоя. | Editor-tooling, отдельно от рантайм-`uitk`. |
| **run-flow** [идея] | Макро-петля забега: `RunState`, `BattleFlow(Prep→Combat→Outcome)`, text events, reward-ramp, экономика/рарность. | Пограничит с `data-authoring`; резать по «поток vs данные». |
| **unity-mcp-ops** [идея] | Безопасная работа с редактором через MCP: `Step` для UITK-скринов, nested-ref только in-editor, не писать в открытый префаб, `run_tests`, `set_active_instance`. | Обсуждался, отложен. Чистый свод процедурных готчей. |
| **pixel-art-pipeline** [идея] | PixelLab (топ-даун east), ТЗ, техника пайплайна (я звоню API, Макс QA). | Арт кладёт человек; скилл = техника. |
| **netcode** [идея] | Host-authoritative кооп, `SimSyncProbe`/checksum, детерминизм по сети. | Пока припаркован; поднять при возврате к коопу. |

## Связанные задачи (не скиллы, но зафиксировано)

- **Миграция GDD-vault** — **Фаза 1 (`818be538`) + Фаза 2 (`c97c0f78`) сделаны.** Главы →
  слаги+кластеры+frontmatter; служебные/roster-доки → слаги+`title`; README→`index` в каждой
  папке; все ссылки починены (верифицировано, 0 новых битых). Решено по месту: карточки
  `relics/roster/enemies` НЕ слугифицированы (их показывает query-based `.base` по `file.name`);
  каталоги НЕ вложены в `40-content` (было бы 5 уровней). **Осталось [gated]:** Quartz Explorer
  `sortFn` по `order` (untested TS, нужен билд сайта); опц. слаги карточек (если решим менять
  `.base` на `title`); 2 pre-existing мёртвые relic-ссылки в `tech/impl/05`.
- **Завести vision + дизайн-столпы** [план] — новые артефакты `10-vision/`; пишет Макс,
  оформляю я.

## Как решаем, что скилл нужен

- Повторяющийся контур работы с накопленными готчами (боль в памяти/истории).
- Есть HARD-инварианты, нарушение которых = переделка.
- Чёткие границы — не пересекается с соседними скиллами (стык оформляем взаимной ссылкой).
- Триггерится по типу задачи (пути/слова в `description`).
