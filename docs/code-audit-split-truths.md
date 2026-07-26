# Реестр источников правды — аудит 2026-07-26

Правило Макса: **факт с двумя владельцами = дефект**. Формат каждой строки: факт → все текущие владельцы → кто ДОЛЖЕН владеть один → порядок сведения.

Порядок сведения важен: строки 1-4 — предпосылки для волн удаления (фолбэк можно удалить только после того, как основной владелец стал достижим).

---

## Сводный реестр с порядком сведения

Collapse order matters: rows 1–4 are prerequisites for the deletion waves (a fallback can only be deleted once the primary is reachable). Each row is "fact → single intended owner → what to do, in order".

| # | Duplicated fact | Owners | Single owner | Collapse order |
|---|---|---|---|---|
| T-1 | Which StatsConfig / ClassBalanceConfig feeds the stat cascade (base HP + MoveSpeed of every unit) | `CombatSystemsScene:453-454`, `CoreScene:295-296`, `RootLifetimeScope.cs:84` | **One registration in `RootLifetimeScope`**, `CombatLifetimeScope` resolves from parent (as it already does for `IContentDatabase`) | 1. assign in CoreScene → 2. move to a single Root registration → 3. delete the second scope's fields → 4. add the wiring guard test. The invariant currently lives only in a `[Tooltip]`, which nothing can check |
| T-2 | Body radius per Size (separation rest distance + every attack reach via `CombatPositioning.AttackReachCenter`) | `SimTuning.cs:79` (0.3), `SimTuningConfig.cs:17` (0.575), `SimTuningConfig.asset:15`, `UnitView.cs:1076/1084`, `SeparationSystem.cs:29` | **Authoring: the asset, mirrored by exactly one constant (`SimTuning.Default`). Runtime read: `CombatSimulation._tuning` snapshot** | 1. rewrite the SO initializer as `SimTuning.Default.X` → 2. re-point the guard test at asset-vs-`SimTuning.Default` → 3. `SeparationSystem` reads the snapshot passed into `Tick` like `MovementSystem` does → 4. gizmo reads `sim.Tuning`, not `SimTuning.Default` |
| T-3 | Which localization table owns `ui.*` | `LocalizationService.cs:18` (`"Content"`, both single-arg overloads), `MenuRouter.cs:460`, `UI Shared Data.asset:19`, `Content Shared Data.asset:287`, `DescriptionService.cs:18` | **The `UI` collection for screen text**, table names taken from `ContentKeys` | 1. `GetString(key)` stops defaulting to `Content` → 2. reader logs/throws on a miss instead of returning `""` → 3. move the misfiled rows → 4. delete the C# RU fallbacks except on the boot path |
| T-4 | Whether the battle is paused | `CombatSimulation.cs:455`, `TimeScaleService.cs:192`, `BattleInputController.cs:54`, `DeploymentController.cs:587`, `BattleBootstrap.cs:90`, `WorldStageController.cs:53`, `GuildmasterCommands.cs:110` | **`TimeScaleService`** (already composes GameSpeed × Cinematic × Paused and documents itself as the only writer of `Time.timeScale`) | 1. sim's `_isPaused` becomes a read of the service (or is deleted) → 2. every caller routes through the service → 3. `StartCombat` can no longer set half the pause |
| T-5 | Act map generation parameters | `MapGenConfig.cs:16`, `:76`, `ActConfig.asset:15`, `RootLifetimeScope.cs:67` | **`ActConfig.asset`** (the SO exists for exactly this: «дизайнер крутит глубину/ширину/зоны/якоря в инспекторе») | 1. assign in CoreScene → 2. `ToGenConfig()` returns a copy so `Validated()` stops writing clamps back into the asset → 3. `MapGenConfig` initializers survive only as a headless-test POCO |
| T-6 | The literal text of a screen label («Привал», «Продолжить», «К построению») | `ContinueScreen.uxml:8`, `MenuRouter.cs:747`, `CampScreen.uxml:4`, `CampScreenView.cs:41`, `UI_ru.asset:24` | **The string table alone** | after T-3: 1. empty (or `###`) the `text=` on localized labels → 2. delete C# RU fallbacks off the non-boot paths |
| T-7 | Act-map node colours (backing, rim, icon ramp, cleared/locked tints) | `tokens.primitives.uss:13`, `MapStyle.cs:82`, `MapStyle.asset:35` | **USS primitives** (where the HARD «тёплый свет» rule lives); MapStyle holds a small derived named set | 1. derive MapStyle's colours from the tokens once → 2. delete the free-form `Color` fields whose Tooltips name a token they no longer match |
| T-8 | Run economy (starting gold, relic capacity, restarts/act, shop prices, guild size) | `GameConfig.cs:34`, `GameConfig.asset:20`, `RunStateSaveTests.cs:87`, `RunStateRestartTests.cs:23`, `BattleNodeFlowTests.cs:45` | **`GameConfig.asset`** (project HARD rule) | 1. re-save the asset with all 21 fields → 2. initializers → neutral/zero → 3. the three tests load the shipped asset instead of `CreateInstance<GameConfig>()` |
| T-9 | Hit-squash depth | `CombatFeelConfig.cs:116`, `CombatFeelConfig.asset:54`, `UnitView.cs:837` | **The asset** | delete the `?: 0.4f` — the config is assigned in both scenes that use it, so the fallback cannot fire and is only ever a second value |
| T-10 | Auto-attack DPS of a kit | `ContentAuditor.cs:110`, `StatMath.cs:39`, `UnitStatPreview.cs:71` | **One quantized helper beside `AttackTiming` in the sim assembly** — the real cadence is `TickRate / AttackTiming.IntervalTicks`; any tool using raw AttackSpeed reports a number the game never produces | 1. add the helper → 2. auditor, hub and inventory panel all call it → 3. delete both copies |
| T-11 | A unit's base MaxHP before persona modifiers | `StatsConfig.asset:18` (1200), `ClassBalanceConfig.cs:26`, `ClassBalanceConfig.asset:16` (2000) | **`ClassBalanceConfig`** (declared 2nd cascade level, «Брузер = 100%» anchor) | 1. remove StatsConfig's MaxHP default → 2. fix `relic.base`'s hard 1200 Override (starting vessels are 40% under the anchor) |
| T-12 | HP-bar colour, ally and enemy | `CombatColorPalette.cs:22`, `CombatPresenter.cs:435`, `HealthBarView.cs:43` | **`CombatColorPalette`** | delete `DefaultHealthColor` and `_fallbackHpColor` — a missing palette is a wiring bug, not a case to degrade into the exact red the palette documents as rejected |
| T-13 | Shield colour | `CombatColorPalette.cs:32`, `HealthBarView.cs:44`, `CombatPresenter.cs:34`, `CombatStatusOverlay.cs:21` | **`CombatColorPalette._shield`** (its own docstring claims «единый источник правды») | the presenter already injects it and pushes it into the bar at `:264`; point the other two at the same field, delete the scene-serialized copy |
| T-14 | The mode-tag vocabulary ("map"/"inventory"/"battle") driving tab highlight + input context | `UiScreen.cs:20`, `MenuRouter.cs:250/263/307`, `UiRootBootstrap.cs:590`, `RunModeBarView.cs:48`, `RunModeBar.uxml:23` | **`UiScreen`'s constants** (one of three already exists as `MapModeTag`) | add `InventoryModeTag`/`BattleModeTag`, use them at all producers and consumers, derive `RunModeBarView`'s `"mode-" + key` lookup from them |
| T-15 | The pre-persist "pending battle" launch handshake | `BattleSession.cs:110`, `:117`, `BattleBootstrap.cs:59` | **Nobody — delete it.** `BindLaunch`/`RequestLaunch` is the live path; the interface comment says so («Заменяет связку SetPending+LoadBattleAsync») | wave 2 |
| T-16 | The modal-overlay frame (scrim + panel + title + divider + slot) | `ModalPanel.cs:11`, `CampScreen.uxml:3`, `EventScreen.uxml:3`, `LoadoutHubScreen.uxml:3`, `MainMenuScreen.uxml:3` | **Either every overlay UXML uses `<ModalPanel>`, or the control dies.** A `[UxmlElement]` no UXML instantiates is pure decoy | recommend delete (wave 2); adopting it is a UI-rework-scope decision |
| T-17 | The locale the game starts in | `GameConfig.cs:20`, `GameConfig.asset:18`, `LocalizationSettings.asset:40`, `ConfigValidationTests.cs:74` | **One of the two — and if `GameConfig`, something must call `SetLocale` at boot** | today the field is a decoy with a test guarding it: 1. delete the field + its test, or 2. wire boot to it. Either way configure a fallback locale (LT-14) |
| T-18 | The attack-speed clamp ceiling | `StatsConfig.cs:16` (2.5), `StatsConfig.asset:17` (4), `ContentAuditor.cs:89` (2.5) | **The asset — and only if someone applies it.** Zero sim readers today | 1. decide whether the clamp is real → 2. if yes, `Stats.Compose` applies it and the docstrings at `Stats.cs:30`/`StatType.cs:26` stop lying; if no, delete all three + the test |
| T-19 | The RNG seed a battle runs on | `RunState.cs:80`, `RootLifetimeScope.cs:57`, `CombatLifetimeScope.cs:112`, `RunStateService.cs:92` | **`RunState.Seed`** (the saved field; the map generator already derives `Seed + CurrentActIndex`) | both container RNGs derive from it plus a node/battle discriminator, not from the wall clock |
| T-20 | The battle seed as a DI-readable value | `BattleSeed.cs:11`, `CombatLifetimeScope.cs:117` | **Nobody — delete the type and the registration** (or give the desync probe a real reader) | wave 2 |
| T-21 | The relic stash — which relics the player owns | `RunState.cs:92`, `LoadoutViewModel.cs:50`, `MenuRouter.cs:278`, `RunStateService.cs:242` | **`RunState.RelicInventory` via `RunStateService`** (durable, saved, capacity-governed; RewardService/ShopController/EquipRelic already treat it as authoritative) | 1. `LoadoutViewModel.Relics` becomes a filter over it → 2. delete `RunStateService.SetSlotRelic`, whose own comment at `:226` says the two paths should merge and it should go away |
| T-22 | Which icon a unit tag shows on its chip | `TagData.cs:17`, `LoadoutInventoryView.cs:463`, `components.uss:2271` | **`TagData._icon` on the SO** — the only owner reachable from the tag id at runtime, and the one the running game reads | delete the 61 `.gm-tag--<slug>` USS rules (wave 3) |
| T-23 | What a «?» (`MapNodeType.Unknown`) turns out to be | `RandomEventFlow.cs:29` (five magic integers), `MapGenConfig.cs:80`, `ActConfig.asset:22` | **`ActConfig.asset`'s per-floor ZoneRule weights** — `MapGenerator.PickType` already resolves every other node type from there | after T-5: move the distribution into a zone-scoped table |
| T-24 | Which SO types are content and where their assets live | `ContentDomains.cs:16` (17 types), `ContentPaths.cs:17` (13 types), the disk layout | **`ContentDomains`** (runtime-visible, complete) | `ContentPaths` derives folder from domain; note the current Content Hub create menu offers exactly the dead types and hides the live ones |
| T-25 | The MapNodeType taxonomy | `RunState.cs:11`, `MapNode.prefab:379`, `MapNodePrefabTests.cs:15` | **The enum in `Guild`** | the prefab keeps per-type icon slots (Presentation genuinely cannot reference Guild, `MapNodeVisual.cs:21-23`); the test enumerates `Enum.GetValues` instead of hardcoding nine entries |
| T-26 | The three volume-row labels on the settings screen | `MenuRouter.cs:459`, `UiPreviewCatalog.cs:236`, `:474` | **A shared `SettingsScreenView.Build(...)`** resolving through loc keys — settings is the one screen with no shared builder while nine others are reused by the preview stand | after T-3 |
| T-27 | First-run `GameplaySettings` defaults | `GameplaySettings.cs:34`, `SettingsService.cs:132` | **`GameplaySettings.Defaults()`** (docstring already claims it) | `ReadFromDisk` seeds the gameplay half from `Defaults()` exactly as it seeds the audio half two lines above |
| T-28 | The name of the `Content`/`UI` tables | `ContentKeys.cs:16`, `LocalizationService.cs:18`, `DescriptionService.cs:18`, `TooltipContentFactory.cs:21` | **`ContentKeys`** (explicitly claims the role; `ContentLocalization.cs:21` already defers to it) | fold into T-3 |
| T-29 | "Return the world to a non-battle state" | `RunBeatStage.cs:51` + two other owners, one self-documented as a copy | **`RunBeatStage`** | 1. collapse to one → 2. restore the two tests commit `0410520c` deleted (TS-7) |
| T-30 | Event localization suffixes | `TextEventData.cs:31` (runtime), `ContentLocalization.cs:41` (editor) | **`ContentKeys`** (single-owner rule it currently bypasses) | fold into T-3/T-28 |
| T-31 | Map edge direction | `WorldMapController.cs:157` (generator stores one-way), view dedup + traversal docs (assume both) | **The generator's stored form**, made explicit | pick a direction convention, then fix the dedup |

---

---

## Сырые находки линзы (29 фактов, как их вернули агенты)

Оставлено для трассировки: сводный реестр выше — производная от этого, с домерженными фактами из других линз.

### T*-01 · P0 — Which StatsConfig / ClassBalanceConfig feeds the stat cascade (the base HP and MoveSpeed of every unit)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scenes/CombatSystemsScene.unity:453`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scenes/CombatSystemsScene.unity:454`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scenes/CoreScene.unity:295`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scenes/CoreScene.unity:296`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/RootLifetimeScope.cs:84`

**Должен владеть один:** One registration. RootLifetimeScope should resolve the configs from a single place both scopes read (register them in Root and let CombatLifetimeScope resolve from the parent, exactly as it already does for IContentDatabase and the camera rig) — the current arrangement keeps the invariant alive only in an inspector Tooltip ("ТОТ ЖЕ ассет, что в CombatLifetimeScope", RootLifetimeScope.cs:48-53), and a Tooltip cannot be checked by anything.

**Расхождение:** ALREADY DIVERGED IN THE SHIPPED SCENES. CombatSystemsScene assigns both (guid ad16ddf5… StatsConfig, c346355e… ClassBalanceConfig); CoreScene has `_statsConfig: {fileID: 0}` and `_classBalanceConfig: {fileID: 0}`. RootLifetimeScope.cs:84-85 therefore builds `new UnitStatPreview(null, null)`. UnitStatPreview.Build (UnitStatPreview.cs:63-65) then does `new Stats(null)` → Stats.DefaultOf falls to StatsConfig.NaturalDefault (Stats.cs:192) = 0 for MaxHP/MoveSpeed, and ClassBaseline.Apply returns immediately on a null config (ClassBaseline.cs:27). Concrete, player-visible: relic.defender (Relics/Defender.asset:34-52 — `_combatClass: 1`, its `_stats` block carries NO MaxHP and NO MoveSpeed) fights with MaxHP 2000×1.5 = 3000 and MoveSpeed 3×0.85 = 2.55, while the inventory detail panel (MenuRouter.cs:282 statsOf → LoadoutViewModel.cs:100 → LoadoutInventoryView.cs:493) prints «Здоровье 0» and «Скорость 0». This is precisely the failure the class docstring calls «таблица не врёт» and the Tooltip warns about.

---

### T*-02 · P1 — Body radius per unit Size (drives separation rest distance and, through CombatPositioning.AttackReachCenter, every attack reach)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Core/Simulation/SimTuning.cs:79`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/SimTuningConfig.cs:17`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/SimTuningConfig.asset:15`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/UnitView.cs:1076`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Systems/SeparationSystem.cs:29`

**Должен владеть один:** The asset, mirrored by exactly one code constant (SimTuning.Default) that the guard test already pins. SimTuningConfig's field initializers must be written as `SimTuning.Default.X` (or the asset regenerated from it) so a third value cannot exist, and UnitView's gizmo must read the live snapshot the way DeploymentController does.

**Расхождение:** Three values for one number, two of them disagreeing by 92%: SimTuning.Default = 0.3f, the shipped asset = 0.3, but the SO's own C# initializer = 0.575f with a docstring one line above claiming «Дефолты полей = SimTuning.Default — при рассинхроне падает тест-страховка» (SimTuningConfig.cs:10). The guard test compares asset against SimTuning.Default only (ConfigValidationTests.cs:28) — both 0.3 — so it stays green while the initializer is wrong. Anyone who creates a SimTuningConfig from `Guildmaster/Config/Sim Tuning Config` ships bodyRadius 0.575: separation rest distance for two Size-1 units goes 0.6 → 1.15 and every melee reach grows by 0.55 world units. Fourth owner: UnitView.cs:1076 draws the orange "sim collision" gizmo from `SimTuning.Default.BodyRadiusPerSize` while DeploymentController's foot ring uses `CombatPositioning.BodyRadius(u, _sim.Tuning)` (DeploymentController.cs:651) — tune the asset and the two circles for the same unit differ.

---

### T*-03 · P1 — Which localization table owns the `ui.*` screen keys

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Services/LocalizationService.cs:18`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/MenuRouter.cs:460`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Localization/Tables/UI Shared Data.asset:19`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Localization/Tables/Content Shared Data.asset:287`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Descriptions/DescriptionService.cs:18`

**Должен владеть один:** One table for screen text (the `UI` collection, as ContinuePresenter.cs:32 and the UI table's own contents assume), and ILocalizationService must take the table from a single constant instead of defaulting every single-argument GetString to `Content`. Today the split is invisible because the reader silently returns an empty string on a miss (LocalizationService.cs:66).

**Расхождение:** `ui.*` keys are split across BOTH collections — 41 in `UI Shared Data` (ui.camp.*, ui.beat.*, ui.node.*, ui.kit.*, ui.loadout.slot.*) and 21 in `Content Shared Data` (ui.run.*, ui.reward.*, ui.hub.*, ui.titlecard.*) — while every screen localizes through `key => _loc?.GetString(key)` (MenuRouter.cs:114, 217, 276, 619, 652, 766, 784, 802, 821, 838, 871), i.e. the single-argument overload hardwired to `Content` (LocalizationService.cs:48). So all 41 UI-table entries are unreachable at runtime. Already-observed consequence: MenuRouter.ShowNodeFarewell assigns `title.text = _loc?.GetString(req.TitleKey)` with NO fallback (MenuRouter.cs:680-681); ShopFlow.cs:37 passes "ui.node.shop.title"/"ui.node.shop.farewell", which exist only in the UI table (lines 63/67 with RU text «Лавка» / «Торговец сворачивает лоток…» in UI_ru.asset) → the lookup misses, returns "", and the shop/chest/camp farewell frame renders with an empty title and empty body, overwriting even the UXML placeholder. Nothing tests this: ContentLocalization.Collection is hard-bound to the Content table (ContentLocalization.cs:21,31) and RequiredSuffixes only validates ContentDefinition keys, so no test notices that 77 `ui.*` keys referenced from code resolve to nothing.

---

### T*-04 · P1 — Whether the battle is paused

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/CombatSimulation.cs:455`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Services/TimeScaleService.cs:192`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Input/BattleInputController.cs:54`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/DeploymentController.cs:587`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/BattleBootstrap.cs:90`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/WorldStageController.cs:53`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/DevTools/GuildmasterCommands.cs:110`

**Должен владеть один:** TimeScaleService — it already composes GameSpeed × Cinematic × Paused and is documented as «единственный писатель Time.timeScale»; the sim's `_isPaused` should be driven from it (or removed in favour of it), so that no caller can set half of the pause.

**Расхождение:** Two independent latches, written together in exactly ONE place (BattleInputController.cs:54-56 on Space) and separately in eight others. Concrete failure: press Space mid-battle (sim `_isPaused = true`, `Time.timeScale = 0`), lose the node, enter the next battle's deployment (BattleBootstrap.cs:90 sets only the sim flag), press «Начать» → DeploymentController.StartCombat (line 587) clears only `_sim.SetPaused(false)` and does not touch TimeScaleService, which DeploymentController does not even hold. CombatLoopService.cs:51 accumulates `Time.deltaTime` = 0, so the battle is frozen while `_simulation.IsPaused == false`; the topbar timer (bound to `_sim.ElapsedSeconds`) is frozen too. The next Space press re-pauses the sim (`paused = !IsPaused` = true) and only the one after that restores timeScale — two presses to escape a state the UI reports as "running".

---

### T*-05 · P1 — Act map generation parameters (depth, column widths, zone weights, anchors)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/MapGenConfig.cs:16`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/MapGenConfig.cs:76`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/ActConfig.asset:15`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/RootLifetimeScope.cs:67`

**Должен владеть один:** The ActConfig asset — that is the whole point of wrapping MapGenConfig in a ScriptableObject (ActConfig.cs:5-10: «дизайнер крутит глубину/ширину/зоны/якоря в инспекторе, не трогая код»). MapGenConfig's initializers should stay only as a last-resort POCO for headless tests.

**Расхождение:** The authored asset is ORPHANED: its guid (dbc39cb776c7fd6469e9cd31b97af1ab) appears in no scene, prefab or asset — CoreScene.unity:294 has `_actConfig: {fileID: 0}`, so RootLifetimeScope.cs:67 registers a throwaway `ScriptableObject.CreateInstance<ActConfig>()` and the game plays the C# initializers (MapGenConfig.cs:16-51: Columns 15, EdgeColumnWidth 3, DefaultZones/DefaultAnchors). Today the two copies happen to hold identical numbers, which is what hides it: any edit to ActConfig.asset (Columns, a zone weight, an anchor floor) changes nothing in game and the designer has no way to tell. This is the same trap MapStyle's docstring says already cost a play-QA round twice — «Дважды подряд это стоило раунда play-QA (профиль ширины в ActConfig.asset…)» (MapStyle.cs:12) — and ActConfig itself was never fixed.

---

### T*-19 · P1 — Which relics the player actually owns (the run's stash)

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Guild/RunState.cs:92`
- `Assets/_Project/Scripts/UI/LoadoutViewModel.cs:50`
- `Assets/_Project/Scripts/UI/MenuRouter.cs:278`
- `Assets/_Project/Scripts/Guild/RunStateService.cs:242`

**Должен владеть один:** RunState.RelicInventory, read through RunStateService — it is the durable, saved, capacity-governed list that RewardService/ShopController/RunStateService.EquipRelic already treat as authoritative. LoadoutViewModel.Relics must be a filter over it, and RunStateService.SetSlotRelic (whose own comment at line 226 says the two paths 'should be merged and then SetSlotRelic goes away') must not exist as a second, stash-free equip path.

**Расхождение:** LoadoutViewModel.Relics returns `_content.All<RelicData>()` — every relic asset in the game — and MenuRouter.BuildInventory hands exactly that list to LoadoutInventoryView as the inventory grid. Dragging any card onto a unit publishes RelicDragEvent -> DeploymentController.cs:576 -> RunStateService.SetSlotRelic, which writes slot.RelicId directly and never touches RelicInventory. So the topbar «Инвентарь» screen lets the player equip a relic he never bought, never won and does not hold, for free, while the shop (ShopController.Buy, line 80-84) is spending gold and checking RelicInventoryFull for the same relics. The two owners already disagree the moment the run starts: RunState.RelicInventory is empty on a fresh run (RunStateService.NewRun sets only Guild/Gold/RelicCapacity) yet the inventory grid shows the full relic catalogue.

---

### T*-20 · P1 — Which icon a unit tag (tag.tank, tag.fire, …) shows on its chip

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Data/Definitions/TagData.cs:17`
- `Assets/_Project/Scripts/UI/LoadoutInventoryView.cs:463`
- `Assets/_Project/UI/Theme/components.uss:2271`

**Должен владеть один:** TagData._icon on the ScriptableObject — it is what the running game reads (TagChip -> Chip.SetIcon -> inline backgroundImage, Components/Chip.cs:40-44), it is filled in the shipped assets (ScriptableObjects/Tags/Tank.asset `_icon: {fileID: 21300000, guid: 086b2da...}`), and it is the only owner reachable from the tag id at runtime.

**Расхождение:** components.uss:2271-2325+ declares ~55 rules of the form `.gm-tag--tank .gm-chip__icon { background-image: url("../../Art/UI/Icons-gm/Tags/tank.png"); }`, one per tag. Nothing anywhere adds a `gm-tag--<slug>` class: grep over every .cs and .uxml finds exactly one hit, `gm-tag--more` (LoadoutInventoryView.cs:432). TagChip adds only `gm-chip--sm` and `gm-tag`. So the entire block is dead — and doubly so, because Chip.SetIcon writes an inline style that would beat the USS rule even if the class were applied. It reads as the wired icon table (it is literally next to the live `.gm-runbar__tab--map .gm-chip__icon` block at line 1727, which IS applied from RunModeBar.uxml), so the next person who changes a tag icon will edit the USS, see nothing change, and have no idea why.

---

### T*-21 · P1 — What kind of node a «?» (MapNodeType.Unknown) turns out to be

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Game/Flow/RandomEventFlow.cs:29`
- `Assets/_Project/Scripts/Guild/MapGenConfig.cs:80`
- `Assets/_Project/ScriptableObjects/Configs/ActConfig.asset:22`

**Должен владеть один:** ActConfig.asset's per-floor ZoneRule weights (via MapGenConfig) — it is the designer-facing, per-act pacing curve, and MapGenerator.PickType already resolves every other node from it. The «?» distribution belongs there as a zone-scoped table, not as five magic integers in a flow class.

**Расхождение:** They already disagree on the shipped asset. ActConfig.asset zone 1 (FromFloor 1, ToFloor 4) authorises exactly two types — Battle weight 70 and Unknown weight 30 — deliberately keeping Elite, Shop and Chest out of the early act. But 30% of those floor-1..4 nodes are «?», and RandomEventFlow.Roll hardcodes `Chest 12% / Shop 8% / Elite 5%` with no reference to the act, the floor or the zone. So ~25% of every early-act «?» resolves to precisely the three node types the zone table excludes, and a designer who re-tunes ActConfig.asset (or adds a new act with a different curve) changes nothing about it — the numbers are unreachable from any asset.

---

### T*-06 · P2 — The literal text of a screen label (e.g. «Привал», «Продолжить», «К построению»)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/ContinueScreen.uxml:8`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/MenuRouter.cs:747`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/CampScreen.uxml:4`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/CampScreenView.cs:41`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Localization/Tables/UI_ru.asset:24`

**Должен владеть один:** The string table alone. UXML `text=` attributes on localized labels should be empty (or a visible "###" marker), and the C# RU fallback should exist for at most the boot path — three owners per string means a translator's edit is a coin flip.

**Расхождение:** Three owners per string, and WHICH ONE WINS DIFFERS PER SCREEN. Continue button: UXML says text="Продолжить", UI_ru holds «Продолжить» for ui.beat.continue, and MenuRouter.Label (line 744-748) only overwrites when the lookup is non-empty — the lookup goes to the Content table and misses, so the UXML literal wins and both other copies are dead. Camp title: UXML says text="Привал", UI_ru holds «Привал» for ui.camp.title, and CampScreenView.cs:41 assigns `L("ui.camp.title", "Привал")` unconditionally — here the C# fallback wins and the UXML literal is dead. Same three-way pattern for «К построению» (ContinueScreen.uxml:9 / ui.beat.formation / ContinuePresenter.cs:33) and every camp action label (CampScreenView.cs:105-112 vs ui.camp.action.* ). LoadoutScreen.uxml:4 still carries the untranslated literal text="Loadout".

---

### T*-07 · P2 — Node visual colours on the act map (backing, rim, icon ramp, cleared/locked tints)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Theme/tokens.primitives.uss:13`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/Map/MapStyle.cs:82`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/MapStyle.asset:35`

**Должен владеть один:** The USS primitives are the palette of record (the project's HARD «тёплый свет» rule lives there); MapStyle's colours should be a small named set derived from them once, not free-form Color fields whose Tooltips claim a token they no longer match.

**Расхождение:** All three copies now disagree, and the C# Tooltips assert an equality that is false. `_nodeBacking`: Tooltip says «--gm-ink-600», C# default (0.141,0.102,0.071) equals rgb(36,26,18) = --gm-ink-600, but the shipped asset holds (0.115,0.093,0.07) = rgb(29,24,18) — a token that does not exist. `_nodeRim`: Tooltip «--gm-brass-600», C# (0.627,0.435,0.188) = rgb(160,111,48), asset (0.72,0.52,0.24) ≈ rgb(184,133,61) = --gm-brass-500. `_iconLight`: Tooltip «--gm-parchment-100» = rgb(239,226,196), asset (0.97,0.93,0.84) = rgb(247,237,214), lighter than any token. `_cleared` 0.62/0.58/0.50 in code vs 0.78/0.74/0.66 in the asset; `_locked` 0.42/0.40/0.36 vs 0.62/0.60/0.56. A designer re-tuning --gm-ink-600 changes the UI panels and not the map.

---

### T*-08 · P2 — Run economy numbers (starting gold, relic capacity, restarts per act, shop prices, guild size)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/GameConfig.cs:34`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Tests/EditMode/Run/RunStateSaveTests.cs:87`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Tests/EditMode/Guild/RunStateRestartTests.cs:23`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Tests/EditMode/Guild/BattleNodeFlowTests.cs:45`

**Должен владеть один:** The asset (project HARD rule). GameConfig's initializers should be zero/neutral and the asset must carry every field; tests that assert economy behaviour must load the shipped asset, not `ScriptableObject.CreateInstance<GameConfig>()`.

**Расхождение:** Observed drift on the one field the asset does override: `_relicCapacityBase` is 8 in C# and 12 in GameConfig.asset:20. The asset overrides only 6 of the ~20 serialized fields — `_localPlayerTeam`, `_partyBannerSlots`, `_startGold`, `_battleGoldReward`, `_priceCommon/_priceCursed/_priceDivine`, `_priceSpread`, `_sellPercent`, `_shopRerollCost`, `_restartsPerAct`, `_guildSize`, `_startingRelicId` are absent from the YAML entirely, so the C# initializer is the de-facto owner for all of them. Every economy test builds its own config with CreateInstance and therefore validates the CODE side (RunStateRestartTests.cs:23 «RestartsPerAct код-дефолт = 2»; BattleNodeFlowTests.cs:45 «+20 (GameConfig.BattleGoldReward код-дефолт)»; RunStateSaveTests.cs:87-98 walks capacity 8→16, while the game walks 12→16, i.e. 4 upgrades not 8). Changing the asset to balance the run leaves the whole suite green and vice versa.

---

### T*-09 · P2 — Hit-squash depth (how far a struck unit flattens)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs:116`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/CombatFeelConfig.asset:54`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/UnitView.cs:837`

**Должен владеть один:** The asset. The in-view `?: 0.4f` fallback should be deleted (the config is assigned in WorldScene.unity:2683 and MaxSceneForTests.unity:2636, so it never fires) rather than kept as a silently different second value.

**Расхождение:** Not just a duplicated fallback — the two code copies are both WRONG relative to the shipped asset, by 2×: `_squashAmount = 0.4f` in CombatFeelConfig.cs:116 and the same 0.4f hardcoded in UnitView.cs:837 (`_feel != null ? _feel.SquashAmount : 0.4f`), while CombatFeelConfig.asset:54 plays 0.2. A reader tuning the C# default (or trusting the fallback as the documented feel) sets the squash to twice the shipped value; a fresh CombatFeelConfig created from the CreateAssetMenu ships 0.4.

---

### T*-10 · P2 — Auto-attack DPS of a kit

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:110`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/EditorTools/ContentHub/Core/StatMath.cs:39`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Stats/UnitStatPreview.cs:71`

**Должен владеть один:** One helper next to AttackTiming in the sim assembly (the quantized form), called by the auditor, the hub and the inventory panel alike — the sim's real cadence is `TickRate / AttackTiming.IntervalTicks`, so any tool using raw AttackSpeed is reporting a number the game never produces.

**Расхождение:** Three implementations, one of them arithmetically different. ContentAuditor.cs:110 computes `autoAtk × Mathf.Clamp(AttackSpeed, cfg.AttackSpeedMin, cfg.AttackSpeedMax) × DamageDealtEff` (line 89) — raw, clamped, un-quantized. StatMath.cs:31-36 and UnitStatPreview.cs:71-76 are two verbatim copies of the quantized form. With the shipped StatsConfig (AttackSpeedMax = 4) a kit at AttackSpeed 4 audits as 4.0 attacks/sec while the sim and both preview paths give `30 / round(30/4=7.5) = 30/8 = 3.75` — the audit's RawDPS column is 6.25% high, and it applies a clamp that the sim never applies at all, so any content authored above 4.0 audits at 4.0 and fights at its raw speed.

---

### T*-11 · P2 — A unit's base MaxHP before persona modifiers

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:18`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/ClassBalanceConfig.cs:26`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset:16`

**Должен владеть один:** ClassBalanceConfig — it is declared the 2nd cascade level and the anchor («Брузер = 100%»). StatsConfig's MaxHP default should be removed (or set to the same anchor) so there is no second answer for units that miss the class layer.

**Расхождение:** Two different "base HP" values live side by side: StatsConfig.asset's `_defaults` list carries `Stat: 0 (MaxHP) → 1200`, while ClassBalanceConfig.asset holds `_baseHp: 2000` with class multipliers 1.0/1.5/0.75/0.65 (Bruiser 2000, Tank 3000, backline 1300). ClassBaseline.Apply always pushes an Override for MaxHP (ClassBalanceConfig.cs:47), so 1200 is shadowed for every unit that has UnitData AND a ClassBalanceConfig — which makes it invisible until one of those is missing, and then a unit silently weighs 1200 HP instead of 2000-3000. That is exactly the path the CoreScene wiring above takes (null class config → the StatsConfig layer is all that is left), and the same asymmetry is what makes the balance auditor's HP column meaningless.

---

### T*-12 · P2 — The locale the game starts in

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/GameConfig.cs:20`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:18`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Localization/LocalizationSettings.asset:40`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Tests/EditMode/Content/ConfigValidationTests.cs:74`

**Должен владеть один:** One of the two, and if it is GameConfig then something must actually call ILocalizationService.SetLocale at boot. Right now the field is a decoy with a test guarding it.

**Расхождение:** `GameConfig._defaultLocale` ("en", documented as «Локаль по умолчанию … Пусто = авто из системы») has zero readers — `grep DefaultLocale` finds only the getter and ConfigValidationTests.cs:74, which asserts it is non-empty and thereby makes the dead field look load-bearing. The locale is in fact chosen by LocalizationSettings.asset's startup selectors (CommandLineLocaleSelector → SystemLocaleSelector → SpecificLocaleSelector "en", lines 40-46). Divergence: a player on a Russian OS gets `ru` from SystemLocaleSelector no matter what GameConfig says; setting GameConfig to "ru" to test the RU build does nothing; and `m_UseFallback: 0` on the string database means a missing key in the selected locale falls through to LocalizationService's empty-string path rather than to English.

---

### T*-13 · P2 — The pre-persist "pending battle" launch handshake

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/BattleSession.cs:110`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/BattleSession.cs:117`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/BattleBootstrap.cs:59`

**Должен владеть один:** Nothing — delete it. `BindLaunch`/`RequestLaunch` is the live path; the pending pair is left over from the cancelled load-a-battle-scene design (its own interface comment says so: «Заменяет связку SetPending+LoadBattleAsync»).

**Расхождение:** `IBattleSession.SetPending` has NO production caller anywhere in the repo (only the test stub at Tests/EditMode/Run/BattleFlowTests.cs:160), so `_hasPending` is always false and the branch at BattleBootstrap.cs:59-60 — commented «Legacy-совместимость: бой, положенный через SetPending (старый путь до persist), запустить» — can never run. It reads as a second, still-supported way to start a battle: a future author adding a launch path will wire SetPending, see nothing happen, and debug the wrong seam. Dead surface: two interface members (BattleSession.cs:20, 23), two fields (`_pending`, `_hasPending`, lines 100-101), two method bodies (110-124) and the bootstrap branch.

---

### T*-14 · P2 — The modal-overlay frame (scrim + panel + title + divider + content slot)

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/Components/ModalPanel.cs:11`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/CampScreen.uxml:3`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/EventScreen.uxml:3`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/LoadoutHubScreen.uxml:3`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/MainMenuScreen.uxml:3`

**Должен владеть один:** Either every overlay UXML uses `<ModalPanel>`, or the control is deleted. A [UxmlElement] that no UXML instantiates is pure decoy.

**Расхождение:** ModalPanel is dead: its name appears in exactly two files in the whole project — its own source and Assets/_Project/UI/AGENTS.md:51, which advertises it in the component table. No `.uxml`, `.uss`, `.unity`, `.prefab` or other `.cs` references it (checked by grepping the whole Assets tree, not just C#). Its docstring claims «Дедуплицирует раму, которую повторяет каждый оверлей (награда/ивент/хаб/настройки/пауза)», yet all six screens still hand-roll `gm-panel` + `gm-panel__title` + `gm-divider` in their own UXML. 78 lines of custom control plus a documentation entry that will send the next author to a control that has never been used.

---

### T*-15 · P2 — The RNG seed a battle runs on

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunState.cs:80`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/RootLifetimeScope.cs:57`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/CombatLifetimeScope.cs:112`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunStateService.cs:92`

**Должен владеть один:** RunState.Seed — it is the saved field and the map generator already derives from it (`Seed + CurrentActIndex`, RunStateService.cs:92). Both container-level RNGs should be derived from it (plus a node/battle discriminator), not from the wall clock.

**Расхождение:** Three independent seed sources for one run. RunState.Seed is persisted and used only by map generation; RootLifetimeScope.cs:57 registers `new XorShiftRng(GenerateRootSeed())`; CombatLifetimeScope.cs:112 makes a THIRD one, `GenerateBattleSeed()` = `DateTime.UtcNow.Ticks ^ (UnityEngine.Random.Range(...) << 32)` (lines 181-182), unless the scene's `_fixedSeed` inspector field is non-zero — a fourth owner living in CombatSystemsScene. Load the same save twice and the map is identical while every battle, reward roll and shop stock differs; the scope's own TODO at line 176 assumes the seed will one day arrive from the host, which cannot work while the value is minted locally from the clock.

---

### T*-16 · P2 — The battle seed as a DI-readable value

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Core/Random/BattleSeed.cs:11`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/CombatLifetimeScope.cs:117`

**Должен владеть один:** Nobody — delete the type and the registration, or give the sim/desync probe an actual reader. As it stands the seed is readable in two ways (the injected XorShiftRng and BattleSeed) and used through neither.

**Расхождение:** `BattleSeed` is registered as an instance (CombatLifetimeScope.cs:117, commented «Сид доступен из DI (лог/реплей/MP)») and is never injected or resolved anywhere: the only occurrences of the identifier in the repo are its own declaration and that one registration line. It is a wrapper whose stated purpose («чтобы позже писать реплеи/спектейт … его нужно уметь прочитать») is unmet, and its existence makes the seed look shared when the only real consumer is the private XorShiftRng registered on the next line.

---

### T*-22 · P2 — The shield colour of the combat UI

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Presentation/Design/CombatColorPalette.cs:32`
- `Assets/_Project/Scripts/Presentation/HealthBarView.cs:44`
- `Assets/_Project/Scripts/Presentation/CombatPresenter.cs:34`
- `Assets/_Project/Scripts/Presentation/CombatStatusOverlay.cs:21`

**Должен владеть один:** CombatColorPalette._shield (the SO, whose docstring calls itself 'единый источник правды' and whose asset carries 0.62/0.86/1.0) — CombatPresenter already injects _colorPalette and pushes it into the bar at CombatPresenter.cs:264, so both other consumers can read the same field.

**Расхождение:** Already-observed disagreement, two distinct blues on screen at once: the shield segment of the HP bar is CombatColorPalette._shield = (0.62, 0.86, 1.0) (asset `_shield: {r: 0.62, g: 0.86, b: 1, a: 1}`), while the floating '-N' shield-absorb number is CombatPresenter._shieldColor = new Color(0.4f, 0.7f, 1f) (used at CombatPresenter.cs:345) and the shield status ring is CombatStatusOverlay.ShieldColor = new Color(0.4f, 0.7f, 1f, 0.9f). HealthBarView._fallbackShieldColor is a fourth copy that happens to match the palette today. Retuning the palette moves the bar and nothing else. Supersedes R1-32, which named only two of the four owners and did not name the observed value split.

---

### T*-23 · P2 — The HP-bar colour for ally and enemy units

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Presentation/Design/CombatColorPalette.cs:22`
- `Assets/_Project/Scripts/Presentation/CombatPresenter.cs:435`
- `Assets/_Project/Scripts/Presentation/HealthBarView.cs:43`

**Должен владеть один:** CombatColorPalette (asset `_allyHp: 0.3/0.85/0.35`, `_enemyHp: 1/0.4/0.13`). CombatPresenter.DefaultHealthColor and HealthBarView._fallbackHpColor should be deleted, not kept in sync — the palette is a serialized dependency of the presenter and a missing one is a wiring bug, not a case to degrade gracefully.

**Расхождение:** The copies have already drifted, and the drift is hidden behind a comment that asserts the opposite. CombatPresenter.cs:434 says «Фолбэк-цвет HP-бара … (совпадает с дефолтами SO)», but DefaultHealthColor returns enemy = new Color(0.90f, 0.25f, 0.25f) while CombatColorPalette._enemyHp is new Color(1.0f, 0.40f, 0.13f) and the shipped asset is {r: 1, g: 0.4, b: 0.13} — a dull red versus the vermilion the palette's own tooltip argues for at length. The ally value (0.30/0.85/0.35) still matches in all three places, which makes the enemy mismatch look intentional rather than rotten. If the palette field is ever left unassigned on the scene, every enemy bar silently switches colour.

---

### T*-24 · P2 — The live body radius per unit Size that the running simulation uses

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Combat/Systems/SeparationSystem.cs:29`
- `Assets/_Project/Scripts/Combat/CombatSimulation.cs:32`
- `Assets/_Project/Scripts/Presentation/UnitView.cs:1084`

**Должен владеть один:** CombatSimulation._tuning (the immutable SimTuning snapshot). SeparationSystem should read the radius from the snapshot passed into Tick, exactly as MovementSystem/AutoAttackSystem do via CombatPositioning, instead of holding its own writable copy; the dev gizmo should read sim.Tuning, not SimTuning.Default.

**Расхождение:** CombatPositioning documents a hard invariant (CombatPositioning.cs:23-25): 'reach считается из того же BodyRadiusPerSize, что и расталкивание', so the separation rest distance always lies inside attack reach. That invariant is enforced only by PushSeparationTuning (CombatSimulation.cs:206-212) copying the snapshot into the mutable field once. GuildmasterCommands.SepRadius (DevTools/GuildmasterCommands.cs:211) writes `_simulation.Separation.BodyRadiusPerSize` and nothing else — _tuning is untouched, so AutoAttackSystem.cs:82/138 and MovementSystem.cs:88 keep computing reach from the OLD radius. Run `gm_sep_radius 1.2` with the shipped 0.3 and melee units are pushed apart to 2.4 world units while their reach still assumes 0.6: they stop at a distance the separation pass immediately undoes and swing forever in vain. A third copy, UnitView.cs:1084, draws the debug collision circle from `SimTuning.Default.BodyRadiusPerSize` and so lies about the sim as soon as SimTuningConfig.asset is tuned away from 0.3.

---

### T*-25 · P2 — The set of map node types (the MapNodeType taxonomy)

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Guild/RunState.cs:11`
- `Assets/_Project/Prefabs/Map/MapNode.prefab:379`
- `Assets/_Project/Tests/EditMode/Presentation/MapNodePrefabTests.cs:15`

**Должен владеть один:** The MapNodeType enum in Guild. The prefab must keep its per-type icon slots (the Presentation assembly genuinely cannot reference Guild, as MapNodeVisual.cs:21-23 explains), but the test should enumerate Enum.GetValues(typeof(MapNodeType)) and assert the prefab covers exactly that set, instead of hardcoding its own nine-entry dictionary.

**Расхождение:** Add a tenth MapNodeType (the codebase already anticipates this — the enum's own comment says values are only appended). WorldMapController.cs:144 stringifies it into MapNodeVisual.Kind; MapNodeView.ShowKind (MapNodeView.cs:90) finds no matching variant, disables every icon and emits NO warning (the warning at line 84-86 only fires for a null Icon on an existing variant); so the new node type draws as a blank disc on the act map. MapNodePrefabTests stays green, because its only cross-check is `ExpectedSprites.Count == variants.arraySize` — nine against nine — and it never asks the enum. The same silence covers a rename: change `Unknown` to `Mystery` and every «?» node loses its icon with no compile error, no warning and no failing test.

---

### T*-26 · P2 — Which ScriptableObject types are content, and where their assets live

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs:16`
- `Assets/_Project/Scripts/Data/Editor/ContentPaths.cs:17`
- `Assets/_Project/ScriptableObjects`

**Должен владеть один:** One table. ContentDomains already maps type -> domain for all 17 content types and is the runtime-visible one; ContentPaths should derive its folder from that entry (e.g. domain -> folder) rather than keep a second, shorter dictionary that the disk layout then contradicts.

**Расхождение:** They already disagree in both directions. ContentDomains registers 17 types; ContentPaths registers 13 and omits SpeciesData, EncounterData, BattlePresetData and TextEventData — yet 21 assets of exactly those types are on disk in ScriptableObjects/Species (1), Encounters (8), BattlePresets (11) and Events (1). Consequences: ContentHubWindow.Browser.cs:337 drives the hub's 'create content' menu from ContentPaths.CreatableTypes, so those four types cannot be created from the hub at all; and ContentCrudService.cs:24 calls ContentPaths.FolderFor, whose fallback (ContentPaths.cs:42) returns `Assets/_Project/ScriptableObjects/Misc` — creating a SpeciesData through CRUD files it in a Misc folder that does not exist, away from the Species folder holding the existing asset. In the other direction ContentPaths declares Traits, Consequences, Guildmasters and RunModifiers folders that have never been created.

---

### T*-27 · P2 — The labels of the three volume rows on the settings screen («Общий» / «Музыка» / «Звук»)

**Владельцы сейчас:**
- `Assets/_Project/Scripts/UI/MenuRouter.cs:459`
- `Assets/_Project/Scripts/DevTools/UiPreviewCatalog.cs:236`
- `Assets/_Project/Scripts/DevTools/UiPreviewCatalog.cs:474`

**Должен владеть один:** A shared `SettingsScreenView.Build(...)` alongside the other screens, resolving the labels through localization keys. Settings is the one screen with no shared builder: RewardScreenView, EventScreenView, LoadoutHubView, LoadoutInventoryView, ShopScreenView, ChestScreenView, OutcomeScreenView, MainMenuScreenView and TitleCardScreenView are all reused by the preview stand (UiPreviewCatalog.cs:96-327); only settings is rebuilt by hand, three times.

**Расхождение:** The same method that hardcodes the three slider labels localizes every other label eight lines below it — MenuRouter.cs:469-472 uses `L("ui.settings.card_anim", …)` for the toggles while lines 459-461 assign bare Russian literals. The preview stand then re-hardcodes the same three strings twice more, at UiPreviewCatalog.cs:236-238 (screen preview) and 474-476 (component gallery), and the screen preview omits the toggle rows entirely — so 'the preview shows the real screen' is already false. Localizing the settings screen requires finding and editing three separate places; missing one silently ships Russian into every locale. Supersedes R1-18 by naming the structural cause (no shared builder) and the other two owners.

---

### T*-28 · P2 — The mode-tag vocabulary that decides which topbar tab is highlighted and which input context is active ("map" / "inventory" / "battle")

**Владельцы сейчас:**
- `Assets/_Project/Scripts/UI/Navigation/UiScreen.cs:20`
- `Assets/_Project/Scripts/UI/MenuRouter.cs:250`
- `Assets/_Project/Scripts/UI/MenuRouter.cs:263`
- `Assets/_Project/Scripts/UI/MenuRouter.cs:307`
- `Assets/_Project/Scripts/UI/UiRootBootstrap.cs:590`
- `Assets/_Project/Scripts/UI/RunModeBarView.cs:48`
- `Assets/_Project/UI/Screens/RunModeBar.uxml:23`

**Должен владеть один:** UiScreen's constants — the const already exists for exactly one of the three tags (MapModeTag) and is honoured in two of the four places that need it. Add InventoryModeTag/BattleModeTag beside it, use them at every producer and consumer, and derive the RunModeBarView chip lookup name (`"mode-" + key`) from the same constants.

**Расхождение:** The one const that exists is bypassed inside its own file: MenuRouter.cs:338 pushes the map screen with `modeTag: UiScreen.MapModeTag` and UiNavigator.cs:240 compares against it, but MenuRouter.cs:250 (`HasMapInStack => _nav.AnyScreen(s => s.ModeTag == "map")`) hardcodes the literal. Change UiScreen.MapModeTag to "act-map" and the code still compiles: the push and the input-context switch follow the const, HasMapInStack silently returns false forever (UiRootBootstrap.GoToMap logs it and mis-decides), and RunModeBarView's chip dictionary — keyed off the literals at lines 48-50 and matching UXML element names `mode-map`/`mode-battle`/`mode-inventory` — never highlights the map tab again. Nothing fails to build and no test covers it.

---

### T*-29 · P2 — The first-run defaults of GameplaySettings (card animations on, attack animation on)

**Владельцы сейчас:**
- `Assets/_Project/Scripts/Core/Settings/GameplaySettings.cs:34`
- `Assets/_Project/Scripts/Game/Services/SettingsService.cs:132`

**Должен владеть один:** GameplaySettings.Defaults() — its docstring already claims the role ('значения по умолчанию задаёт Defaults, а не безымянный конструктор'), and SettingsService.ReadFromDisk should seed its PersistModel from `GameplaySettings.Defaults()` the same way it seeds the audio half from `Defaults()` two lines above (SettingsService.cs:126).

**Расхождение:** ReadFromDisk builds its pre-read PersistModel with the literals `CardAnimations = true, CardAttackAnimation = true` (SettingsService.cs:132-134) instead of reading GameplaySettings.Defaults(), even though the audio defaults on the neighbouring line DO go through the shared Defaults(). The copies agree today, so the bug is latent and asymmetric: flip GameplaySettings.Defaults() to `new GameplaySettings(true, false)` and a player who presses «Сброс» gets attack animation off, while every player whose existing settings.json predates the field (JsonUtility.FromJsonOverwrite only touches present fields — the comment at line 121-123 spells this out) gets it on. Same divergence for any gameplay toggle added later: adding it to Defaults() alone leaves existing installs on `default(bool)`.

---

### T*-17 · P3 — The name of the `Content` / `UI` localization tables

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/ContentKeys.cs:16`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Services/LocalizationService.cs:18`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Descriptions/DescriptionService.cs:18`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/Tooltips/TooltipContentFactory.cs:21`

**Должен владеть один:** ContentKeys — it explicitly claims the role («Суффиксы записаны здесь один раз; редакторная политика ссылается сюда, чтобы „что создаём“ и „что читаем“ не разъехались», ContentKeys.cs:7-13) and ContentLocalization.cs:21 already defers to it. The runtime reader and the UI-table readers must too.

**Расхождение:** `ContentKeys.TableName = "Content"` is honoured only by the editor side; the runtime reader keeps its own private copy (`LocalizationService.ContentTable = "Content"`) and the UI table name is written out twice more as private consts. Rename the collection in ContentKeys and the editor keeps creating keys in the new table while LocalizationService keeps asking for the old one — every lookup returns Entry == null, which by design (LocalizationService.cs:63-66) becomes an empty string, so the entire game silently falls back to its C# RU literals with no error anywhere. This missing single owner is the mechanism behind the stranded UI-table entries above.

---

### T*-18 · P3 — The attack-speed clamp ceiling

**Владельцы сейчас:**
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/StatsConfig.cs:16`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:17`
- `C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:89`

**Должен владеть один:** The asset, and only if someone applies it. Right now the ceiling is authored in two places and enforced in none of the ones that matter.

**Расхождение:** `_attackSpeedMax` is 2.5f in C# and 4 in the shipped asset — a 60% difference in the only knob that claims to cap attack speed (StatType.cs:26 documents AttackSpeed as «клампится из StatsConfig»). The single non-test reader is the editor auditor (ContentAuditor.cs:89), so the two values decide nothing in game but silently decide the audit's RawDPS ceiling; a fresh StatsConfig created from the CreateAssetMenu would audit content against 2.5 while the shipped one audits against 4.

---

