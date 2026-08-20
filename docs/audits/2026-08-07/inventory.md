# Перепись живого состояния — 07.08.2026

Собрана под переписывание README. Всё с якорями на файлы и строки; числа считаны по дереву на дату прогона.
Мультиплеер не описывается по мандату прогона.

---
Перепись собрана по живому дереву. Всё ниже — с якорями.

# 1. ИГРОВОЙ ФЛОУ

**Сцен пять, в билде три** (`ProjectSettings/EditorBuildSettings.asset:9,12,15`): `CoreScene`, `WorldScene`, `CombatSystemsScene`. Вне билда — `Assets/_Project/Scenes/UiPreview.unity` (стенд UI) и `Assets/_Project/Scenes/MaxSceneForTests.unity`.

**Бут.** `Assets/_Project/Scripts/Game/GameBootstrap.cs:56` (объект `[Bootstrap]` в CoreScene) → архивирует лог прошлой сессии (`:61`) → показывает бут-экран, накрывающий загрузку (`:105`), под ним аддитивно грузятся `WorldScene` и `CombatSystemsScene` — обе **persist, не выгружаются** (`Assets/_Project/Scripts/Game/Services/SceneLoader.cs:20,36`). Три dev-разреза мимо меню: акт / текст-ивент / одиночный бой (`GameBootstrap.cs:111,118,125`).

**Путь игрока** (`Assets/_Project/Scripts/Game/Services/GameFlow.cs:173`):
бут-экран → экран профиля, если профиля нет (`:204`) → главное меню (`:206`) → «Создать игру» (режим + галочка лобби, `Assets/_Project/Scripts/UI/MenuRouter.cs:744`) → для Кампании выбор дома-гильдии (`MenuRouter.cs:766`) → Двор гильдии (`GameFlow.cs:280`) → `RunActAsync` (`:439`): генерация карты акта из сида, автосейв, петля обхода `ActRunner` (`Assets/_Project/Scripts/Game/Services/ActRunner.cs:41`) → выбор узла на карте → резолв узла в flow (`Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:68`) → бой/ивент/лавка/сундук/привал → награда → `MapTraversal.Advance` + автосейв (`ActRunner.cs:124`) → босс = `Completed`, поражение = `Defeated` → экран исхода, сейв удаляется (`GameFlow.cs:468`) → назад в главное меню.
Бой **не грузит сцену**: боевые системы подняты на буте, бой — команда в живой симуляции (`GameFlow.cs:24`, `Assets/_Project/Scripts/Game/Flow/BattleSession.cs:9`).
Два других режима из меню — Ристалище и PvP-матч (`Assets/_Project/Scripts/Guild/GameStartRequest.cs:13-23`), они идут мимо карты и сейва (`GameFlow.cs:337`).

**Экраны UI — 26 UXML** (`Assets/_Project/UI/Screens/`, `Assets/_Project/UI/Dev/`). **23 разведены в живом флоу** — сериализованными ссылками на `UiRootBootstrap` в CoreScene (`Assets/_Project/Scenes/CoreScene.unity:152-175`), у каждого есть живой публикатор запроса:

| Экран | Где строится | Живой флоу |
|---|---|---|
| TitleCard (бут) | `Assets/_Project/Scripts/UI/TitleCardScreenView.cs` | да, `Game/Flow/TitleCardPresenter.cs` |
| MainMenu | `Assets/_Project/Scripts/UI/MainMenuScreenView.cs` | да, `Game/Flow/MainMenuPresenter.cs` |
| NewGame | `Assets/_Project/Scripts/UI/NewGameScreenView.cs` | да, `MenuRouter.cs:744` |
| GuildSelect | `Assets/_Project/Scripts/UI/GuildSelectScreenView.cs` | да, `MenuRouter.cs:766` |
| Profile | `Assets/_Project/Scripts/UI/ProfileScreenView.cs` | да, `Game/Flow/ProfilePresenter.cs` |
| Hub (двор) | `Assets/_Project/Scripts/UI/HubScreenView.cs` | да, `Game/Flow/HubPresenter.cs` — **заглушка с одной кнопкой** (`MenuRouter.cs:780`) |
| Pause + Settings + Confirm | `MenuRouter.cs:683,1013,868` | да |
| RunModeBar (топбар забега) | `Assets/_Project/Scripts/UI/RunModeBarView.cs:8` | да |
| Loadout / LoadoutInventory / RelicArcanaCard | `Assets/_Project/Scripts/UI/LoadoutInventoryView.cs` | да, `Game/DeploymentController.cs` |
| Reward | `Assets/_Project/Scripts/UI/RewardScreenView.cs` | да, `Game/Flow/RewardPresenter.cs` |
| Event | `Assets/_Project/Scripts/UI/EventScreenView.cs` | да, `Game/Flow/TextEventFlow.cs` |
| Shop | `Assets/_Project/Scripts/UI/ShopScreenView.cs` | да, `Game/Flow/ShopFlow.cs` |
| Chest | `Assets/_Project/Scripts/UI/ChestScreenView.cs` | да, `Game/Flow/ChestFlow.cs` |
| Camp | `Assets/_Project/Scripts/UI/CampScreenView.cs` | да, `Game/Flow/CampFlow.cs` |
| Continue | `Game/Flow/ContinuePresenter.cs` | да |
| Outcome | `Assets/_Project/Scripts/UI/OutcomeScreenView.cs` | да, `Game/Flow/OutcomePresenter.cs` |
| PeerLost | `Assets/_Project/Scripts/UI/PeerLostDialogView.cs` | да (сеть) |
| DevConsole (F1) / DevLog (F2) | `Assets/_Project/Scripts/UI/DevConsole/` | да, `MenuRouter.cs:571,626` |

Оставшиеся 3 — только вне живого флоу: `DevBattleBrowser.uxml` и `Dev/DevBattlePicker.uxml` разведены в `CombatSystemsScene.unity` (dev-полка), `Dev/UiPreviewRoot.uxml` — только в `UiPreview.unity`. Стенд превью: 5 пунктов меню (`Assets/_Project/Scripts/DevTools/UiPreviewMenu.cs` — Component Gallery, Guild Select, Hub, Loadout Inventory, New Game).

Навигация — стек с тремя типами экранов `Page / Modal / Sheet`, видимость и глушение ввода вычисляются из типа (`Assets/_Project/Scripts/UI/Navigation/ScreenKind.cs:8-17`).

# 2. БОЙ

**Тик 30 Гц, AI 10 Гц** (`Assets/_Project/Scripts/Core/Simulation/SimConstants.cs:10,16`). Пульс — аккумулятор на `Time.unscaledDeltaTime`, время Unity читается только там (`Assets/_Project/Scripts/Game/Services/CombatLoopService.cs:11`), сим уходит вперёд показа до 30 тиков за кадр (`:39`), догоняет не больше 5 (`SimConstants.cs:27`).

**Порядок систем за тик** (`Assets/_Project/Scripts/Combat/CombatSimulation.cs:16-20`): ApplyCommands → Brain → Ability → Movement → Displacement → Separation → SpatialHashRebuild → AutoAttack → Projectiles → Regen → Effects → ResolveCombatRounds → CommitEffects → Death → CheckOutcome.
Двухфазность: урон/лечение только заявляются, применяются одним коммитом через `TickLedger` (`CombatSimulation.cs:22-27`); закон видимости — наложенный эффект меняет статы не раньше CommitEffects (`:29-34`); реактивные цепочки капнуты 16 раундами (`:112`) и 512 событиями на дренаж (`:105`).

Системы, по строке:
- `Combat/AI/BrainSystem.cs:6` — тик AI 10 Гц со стаггером по Id + событийный `BrainDirty`; профили — `Combat/AI/ProfileBrain.cs`.
- `Combat/Abilities/AbilitySystem.cs:11` — кулдауны и каст активок: списание ресурса, cooldown × CooldownEff, наложение эффектов.
- `Combat/Systems/AutoAttackSystem.cs:10` — двухфазная авто-атака: кулдаун → замах → удар на кадре контакта, всё на int-тиках; арифметика — `Systems/AttackTiming.cs:8`, потолок свинга 30 тиков, пол замаха 3 (`SimConstants.cs:34,40`), прощение досягаемости 0.35 (`:51`).
- `Combat/Systems/MovementSystem.cs:10` — интеграция позиций без физики Unity, ветвление по `Positioning` (Approach / Kite / Retreat).
- `Combat/FleeSteering.cs:8` — математика побега: threat + home + wall, скольжение вдоль стены.
- `Combat/Systems/SeparationSystem.cs:9` — расталкивание тел (локальное избегание, не поиск пути).
- `Combat/Systems/DisplacementSystem.cs:9` — принудительные смещения (толчки) с жёстким контролем на время полёта.
- `Combat/Systems/ProjectileSystem.cs:9` — снаряды методом swept circle-segment, деспавн по видимой зоне камеры.
- `Combat/Systems/EffectSystem.cs` (`Combat/Effects/EffectSystem.cs:11`) — длительности, стакинг, потенция, маска тегов, периодика, teardown.
- `Combat/Systems/RegenSystem.cs:7` — реген HP и ресурса.
- `Combat/Systems/SummonSystem.cs:5` — срок жизни призывов и связь с призывателем.
- `Combat/Systems/ConcealmentSystem.cs:9` — маскировка: сильнейшая ступень + радиус обнаружения.
- `Combat/Systems/DeathSystem.cs:6` — смерть, вычистка из хэша, событие.
- `Combat/Damage/DamagePipeline.cs:7` — raw → DamageDealtEff → броня/пробивание → сродство × тип существа → DamageTakenEff.
- `Combat/Spatial/SpatialHash.cs:6` — сетка запросов по радиусу без аллокаций.
- `Combat/Tape/BattleTape*.cs` — лента боя: запись, воспроизведение, диспетчер (используется в т.ч. для фонового боя за главным меню, `Game/Flow/MenuBattleDirector.cs:14`, 10 записей `Assets/StreamingAssets/Replays/*.gmrp`).

**Механики эффектов — 60 рантайм-компонентов** (`Assets/_Project/Scripts/Combat/Effects/Components/`): щиты, шипы, вампиризм, контроль, слепота, парирование, блок, стелс, заморозка/поджог/угли, метки, призывы на старте боя, отложенный взрыв, диспел, фазовый сдвиг и т.д. Активные способности — не отдельный ассет, а `AbilityData`, сериализованный на `RelicData` (`Assets/_Project/Scripts/Data/Definitions/AbilityData.cs:44-47`); 7 режимов таргетинга (`:8-40`).

**Расстановка**: владелец места и состава — `Assets/_Project/Scripts/Game/DeploymentController.cs:16`; фаза держится до общего согласия «Начать».

# 3. КОНТЕНТ (`Assets/_Project/ScriptableObjects/`, всего 316 `.asset`)

Точный счёт по guid скрипта, где считал по типу:

| Тип | Число | Папка |
|---|---|---|
| Эффекты (`EffectData`) | **108** | `Effects/` |
| Теги (`TagData`) | **56** | `Tags/` |
| Реликвии (`RelicData`) | **27** | `Relics/` (26 китов + `BaseRelic`) |
| AI-пресеты | 30 | `AiPresets/` |
| Враги (`EnemyData`) | **20** (19 файлов в `Enemies/` + 1 вне) | `Enemies/` |
| Ключевые слова | 12 | `Keywords/` |
| Боевые пресеты | 12 | `BattlePresets/` |
| Конфиги | 12 | `Configs/` |
| Энкаунтеры | 8 | `Encounters/` |
| VFX-данные | 8 | `Vfx/` |
| Курсоры | 6 (5 скинов + каталог) | `Cursors/` |
| DevTools-сценарии | 5 | `DevTools/` |
| Виды (`SpeciesData`) | 4 | `Species/` |
| Предметы | 3 | `Items/` |
| Облачения (`OutfitData`) | 2 | `Outfits/` |
| Архетипы анимаций | **1** (`SwordShield`) | `AnimationArchetypes/` |
| Текст-события | **1** (`event.wandering_merchant`) | `Events/` |
| Аудио-каталог / БД контента | 1 / 1 | `Audio/`, `Database/` |
| Сосуды (`VesselData`) | **0** — папка пуста | `Vessels/` |
| Черты (`TraitData`) | **0** нигде | — |

Прочее: 2388 файлов в `Art/Sprites/`, 102 в `Art/UI/`, 38 `.aseprite`, 20 префабов, 10 `.anim` + 2 `.controller` (`Assets/_Project/Prefabs/Bones/`), 13 шейдеров + 6 `.hlsl`. Локализация — две коллекции: UI (110 ключей, RU 110 / EN 75) и Content (402 ключа, RU 326 / EN 193), `Assets/_Project/Localization/Tables/`.

Экономика забега из `GameConfig.asset`: старт 100 золота, награда за бой 20, цены реликвий 50/100/150 ± 20%, продажа 25%, реролл лавки 50, перезапусков на акт 2, гильдия 4 сосуда, инвентарь реликвий 12→16, профилей 4, домов на профиль 8, ростер дома 8→64.

# 4. МЕТА-СЛОИ

- **Карта акта — реализовано.** Граф из сида, 15 колонок, ширина 5–7, до 4 рёбер на узел, типы по зонам и якорям (`Assets/_Project/Scripts/Guild/MapGenerator.cs:8`, `Assets/_Project/ScriptableObjects/Configs/ActConfig.asset:16-70`). 9 типов узлов (`Assets/_Project/Scripts/Guild/RunState.cs:11-22`). Карта рисуется в мире (`Presentation/Map/WorldMapView.cs`).
- **Бой как узел — реализовано.** `Game/Flow/BattleFlow.cs:10`, `BattleNodeFlow.cs`, пул перезапусков акта тратится при поражении (`NodeResolver.cs:104`), элитка даёт 2 награды подряд (`:105`).
- **Награды — реализовано.** Витрина N-из-пула, детерминирована сидом, тир по типу узла (`Game/Flow/RewardService.cs:15`, `:8-13`).
- **Лавка — реализовано.** Витрина тем же пулом, цены `RelicPricer`, реролл, продажа, все деньги через `RunStateService` (`Game/Flow/ShopController.cs:9`).
- **Сундук — реализовано.** Фасад → клик → награда 1-из-3 (`Game/Flow/ChestFlow.cs:7`).
- **Текст-события — реализовано механикой, пусто контентом.** Флоу и применение последствий есть (`Game/Flow/TextEventFlow.cs`, `EventEffectApplier.cs`), ассет ровно один.
- **Привал — ЧАСТИЧНО (каркас).** Бюджет 8 действий, цена 2 (`Assets/_Project/Scripts/Guild/CampSession.cs:30,33`), но: «действие '{action}' (-{ActionCost})… Эффект пока не реализован (каркас привала)» — `CampSession.cs:81-82`; список действий (усиление, копия реликвии, снятие последствия, найм) объявлен в `:10-17`.
- **«?»-узел — реализовано** переброской на себя же (`NodeResolver.cs:135`); нерешённые типы падают в `CompletedStubFlow` (`:190`).
- **Сохранения — реализовано.** `JsonFileSaveService` под `Alebardium/Guildmaster/Saves/` (`Assets/_Project/Scripts/Core/Persistence/GameDataPath.cs:24,27`, `Game/Services/JsonFileSaveService.cs:23`), ключ = путь `profiles/{p}/guilds/{g}/run` (`Game/Services/ProfileService.cs:13`), автосейв на каждом переходе узла (`ActRunner.cs:125`), `SaveLoadResult` с `TooNew`/`Corrupted` (`GameFlow.cs:260-269`), отдельное локальное хранилище вне облака (`LocalJsonFileSaveService`).
- **Слой гильдии — ЧАСТИЧНО.** DTO полны: `GuildState` с ростером, валютой, вместимостью, возвышением, апгрейдами, летописью (`Assets/_Project/Scripts/Guild/ProfileState.cs:94-140`), «Книга гильдии» — Летопись/Хроника/Мемориал отдельным ключом (`Assets/_Project/Scripts/Guild/GuildBook.cs:7`). Двор — заглушка на одну кнопку (`MenuRouter.cs:780`). Найма, казарм, смертности в коде нет.
- **Метапрогрессия — ТОЛЬКО ДИЗАЙН (поля без читателей).** `UnlockedPregenIds`, `UnlockedFateIds`, `MaxAscensionUnlocked`, `Upgrades`, `CompendiumSeenIds`, `Currency`, `HiredVeteran` встречаются **только в самом `ProfileState.cs`/`VesselState.cs`** — ни один игровой код их не читает (проверено грепом по `Assets/_Project/Scripts`). Исключения: `RosterCapacity` читает `Game/Services/ProfileService.cs`, `Ascension` — `GuildBook.cs` и `Data/Definitions/RunModifierData.cs`.
- **Прогрессия Сосуда — только структура.** Уровней/витрины/Обетов в коде нет (`Vow`, `VesselLevel` — 0 файлов); `VesselData` несёт теги и модификаторы Судьбы (`Data/Definitions/VesselData.cs:18,26`), но ассетов Сосудов ноль.

# 5. ИНСТРУМЕНТЫ

**`scripts/` (PowerShell):** `run-tests.ps1` (прогон в теневом проекте), `fast-tests.ps1` (тесты мимо редактора), `compile-check.ps1` (Roslyn-компиляция без редактора), `unity-cli.ps1` (общая основа: версия редактора, теневой проект), `editor-health.ps1` (цена domain reload), `balance-headless.ps1` (круг бенчей), `statdb.ps1` (правка статов в YAML при закрытом Unity), `map-dump.ps1` + `map-shots.ps1` (дамп карт акта и снимки), `lab-serve.ps1` + `lab-shot.ps1` + `lab_server.py` (лаборатория в браузере), `balance-site.ps1`-часть — `balance-site/` (index.html + app.js + style.css) и `balance-run.py`, `journals.ps1` (карта журналов + гейт), `check-wiki-links.ps1`, `check-wiki-frontmatter.ps1`, `check-journal-quotes.ps1`, `steam-publish.ps1` + `steam-credentials.ps1`, плюс питон-утилиты арта и звука (`scripts/audio/*.py` — 10 файлов, `cursors-build.py`, `ui-ref-palette.py`, `art-proportion-ruler.py`, `aseprite_parts.py`, анализаторы клипов атаки).

**`tools/`:** `MapDump` (сборка `MapGenerator` из исходников, дамп карт в JSON) и `FastTests` (раннер тестов вне Unity).

**Редакторные окна и пункты — 44 `[MenuItem]`, все под `Alebardium/`:**
- Баланс (`Scripts/Balance/Editor/BalanceMenu.cs`, `BalanceSite.cs`): 16 пунктов — аудит контента, нормы классов, карточки контента, DPS-бенч, бенч выживаемости, энкаунтер-бенч PvE, дуэльные матрицы 1v1/3v3/4v4, парная синергия, Squad Swap, свой сценарий, полный круг, трасса одного боя, отчёт-сайт (открыть/пересобрать).
- Анимация (`Scripts/EditorTools/AnimationLab/`): Animation Lab (окно), пересборка клипов и контроллеров костяного юнита, Rig Profile, Fit Rig To Art (dry run/apply), Measure Locomotion Stride, Validate Rig Clips, гизмо якорей/стресса/дуги.
- UI: Contact Sheet, Colour Ladder, Lightness Ladder (`Scripts/EditorTools/UI/UiContactSheetMenu.cs`), Test → Build & Run, Toggle Maximized Game View (`TestPlayMenu.cs`), UI Preview × 5 (`Scripts/DevTools/UiPreviewMenu.cs`).
- Прочее: Content Hub (окно, `Scripts/EditorTools/ContentHub/ContentHubWindow.cs`), Palette Remapper (`Scripts/EditorTools/PaletteRemap/`), Post FX Lab (`Scripts/Presentation/Editor/PostFxLabWindow.cs`), Data → Sync Content Database, Дизайн-система → Пересобрать палитру, Audio → Populate Catalog from Manifest.

**Dev-консоль в игре** — 30 команд в 5 наборах (`Scripts/DevTools/ArenaDevCommands.cs`, `GuildmasterCommands.cs`, `MapDevCommands.cs`, `DiagCommands.cs`, `VisualFxCommands.cs`): арена/демо/свап, hp/win/restart/seed, тюнинг разделения тел, карта (goto/nodes/pawn/hide), диагностика, fx.

# 6. КАЧЕСТВО

- **Тесты:** 219 файлов EditMode (`Assets/_Project/Tests/EditMode/`, 12 кластеров: Audio, Balance, Combat, Content, ContentHub, Core, Guild, Net, Presentation, Run, UI) + 10 файлов PlayMode. Атрибутов: **1249 `[Test]`, 19 `[UnityTest]`, 36 `[TestCase]`**.
- **Сборок 25 `.asmdef`**: 22 рантайм/эдитор + 3 тестовых.
- **CI** (`.github/workflows/ci.yml`): `changes` (paths-filter по `Assets/**`, `Packages/**`, `ProjectSettings/**`) → `test` (EditMode всегда; **PlayMode только на master**, `:80`) + `build` (StandaloneWindows64 на ubuntu, **только PR и master**, `:114-116`) → `ci-gate` (единственная required-проверка, `:166`). Артефакт плеера **не публикуется** намеренно (`:159-161`), выгружаются только результаты тестов (`:93`).
- Ещё три workflow: `docs-lint.yml` (гейт ссылок вики), `docs.yml` (публикация сайта документации из `docs/wiki` + Doxygen по `Assets/_Project/Scripts`), `steam-deploy.yml` (выкладка в Steam: кнопкой на ветки `dev_happy_guildmasters`/`playtest` или тегом `v*`, очередь через concurrency).
- Публикуется: сайт документации и Steam-сборка по тегу. Игровой билд из CI — нет.

# 7. ТЕХСТЕК

**Реально используется** (счёт файлов нашего кода):
VContainer 1.18.0 — 85 файлов; MessagePipe (+ .VContainer) — 42; UniTask — 33; Shapes — 8; Odin/Sirenix — 7; Steamworks (Facepunch) — 4; FMOD — 3 (+ 4 банка в `Assets/StreamingAssets/`, 110 событий в `AudioCatalog.asset`); Tilemaps — 3; Cinemachine 3.1.7 — 2; LitMotion — 1 (точечно). Плюс: URP 17.4.0 (шейдеры/Volume), UI Toolkit (26 UXML / 37 USS), Input System 1.19.0 за `IInputService`, Localization 1.5.3 (две коллекции), Newtonsoft-Json 3.2.2 (сейвы), 2D Aseprite 4.0.2 (38 `.aseprite`), 2D Sprite/Tilemap/Tilemap Extras, Test Framework 1.6.0, Roslyn (`Assets/Plugins/Roslyn`), MCP for Unity v10.0.0.

**Установлено, но в нашем коде не используется:**
- `com.unity.visualeffectgraph` 17.4.0 — **0 файлов `.vfx`**, 0 упоминаний `UnityEngine.VFX`; боевые VFX — свой слой (`VfxData` → префаб → пул, `Assets/_Project/Prefabs/Vfx/` 8 штук).
- `com.unity.addressables` 2.3.16 — 0 упоминаний `UnityEngine.AddressableAssets`; живёт как основа Localization.
- `com.unity.probuilder` 6.1.2 — 0 упоминаний; зависимость группы в Unity MCP.
- `com.unity.timeline` 1.8.12 — 0 `.playable`, 0 упоминаний.
- `com.unity.2d.animation` 14.0.4 и `com.unity.2d.psdimporter` 13.0.3 — 0 упоминаний `UnityEngine.U2D.Animation`, ни одного `SpriteSkin` в префабах/сценах. Костяной риг сделан на обычной иерархии: `Assets/_Project/Prefabs/Bones/BoneUnit_SwordShield.prefab` — 39 Transform, 16 SpriteRenderer, 1 Animator.
- Вендор под `Assets/`, не в `Plugins/`: `Shapes/`, `Cainos/`, `Honeti/`, `Kenney/`.

# 8. ЧЕГО ЕЩЁ НЕТ

- Сосудов как контента: 0 ассетов `VesselData`, 0 `TraitData` — гильдия набирается пустыми слотами с `relic.base` (`Guild/RunStateService.cs:113-123`).
- Прогрессии Сосуда в забеге: уровней, витрины улучшений, Обетов в коде нет.
- Метапрогрессии профиля: открытия, возвышение, апгрейды дома существуют только полями DTO, читателей нет.
- Двора гильдии как места: экран-заглушка с единственной кнопкой «Начать забег».
- Эффектов привала: бюджет тратится, действия ничего не делают (`Guild/CampSession.cs:81-82`).
- Найма, казарм, смертности ростера, ветеранов — только поля (`Guild/VesselState.cs:56,63`).
- Контента текст-событий: один ассет на весь пул.
- Больше одного акта: `ActConfig` один, `CurrentActIndex` не растёт нигде, кроме DTO.
- Экрана «сейв заблокирован/битый»: пока лог вместо экрана (`Game/Services/GameFlow.cs:263-268`).
- Мини-игр: сборка `Guildmaster.MiniGames` существует, файлов в ней ноль.
- Архетипов анимации, кроме `SwordShield` (1 ассет), и облачений, кроме двух.

Мультиплеер — в работе, не описываю.

