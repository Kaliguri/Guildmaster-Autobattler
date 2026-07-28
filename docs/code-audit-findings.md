# Реестр находок аудита кода — 2026-07-26

Полный реестр, машинно собранный из двух заходов аудита. **Ничего не выброшено**, включая P3 и опровергнутое (опровергнутое помечено).
Главный документ с корнями и приоритетами — [code-audit-2026-07-26.md](code-audit-2026-07-26.md).
Вердикты скептиков — [code-audit-verification.md](code-audit-verification.md).

Как читать `severity`: это **оценка аудитора**, и она систематически завышена. Верификация понизила 25 из 25 проверенных заявок (в среднем на две ступени), но подтвердила механизм у всех 25. Доверять описанию механизма, не ярлыку.

| Префикс | Линза |
|---|---|
| `R1-01..83` | заход 1, срезы по подсистемам |
| `C-01..08` | заход 1, критик на полноту |
| `UA-*` | непокрытые сборки и граф asmdef |
| `TS-*` | тесты как объект аудита |
| `AC-*` | ассеты против кода |
| `LT-*` | локализация и текст |
| `RL-*` | целостность петли забега |
| `BE-*` | билд против редактора |
| `CD-*` | контент как данные |

---

### AC-1 · P0 · truth — CoreScene leaves StatsConfig and ClassBalanceConfig unassigned, so the inventory panel shows Здоровье 0 / Скорость 0 for every hero kit

`Assets/_Project/Scenes/CoreScene.unity:295` · линза `assets-vs-code`

```csharp
CoreScene.unity:294-296 — `_actConfig: {fileID: 0}` / `_statsConfig: {fileID: 0}` / `_classBalanceConfig: {fileID: 0}`. The very field carries the warning: RootLifetimeScope.cs:47-48 `"Стат-конфиг (дефолты статов). ТОТ ЖЕ ассет, что в CombatLifetimeScope — иначе панель инвентаря покажет числа, не совпадающие с боем."`. RootLifetimeScope.cs:84-85 passes them straight through with no fallback: `builder.Register<IUnitStatPreview>(_ => new UnitStatPreview(_statsConfig, _classBalanceConfig), Lifetime.Singleton);`. By contrast CombatSystemsScene.unity:453-454 has both assigned (`guid: ad16ddf55214b4f4f9fc8305dadd705e` / `c346355e64f53d444b5cc7a9fe184c84`).
```

**Чем стреляет.** UnitStatPreview.Build (UnitStatPreview.cs:61-69) does `new Stats(null)` → Stats.DefaultOf (Stats.cs:190-191) falls back to `StatsConfig.NaturalDefault`, which returns 0 for MaxHP/MoveSpeed/armor; then ClassBaseline.Apply short-circuits on `config == null` (ClassBaseline.cs:26). Concrete case: relic.flame_swordsman (`_combatClass: 0`, and its `_stats` block carries no MaxHP and no MoveSpeed) renders in the loadout detail column via LoadoutViewModel.cs:103 `_statPreview.Basic(r)` as Здоровье 0, Магическая броня 0, Скорость 0 — while the same kit in battle gets MaxHP 2000 and MoveSpeed 3 from the asset-wired CombatLifetimeScope. The stat panel's whole contract ("таблица не врёт") is inverted for all 11 relics.

**Куда править.** Assign StatsConfig.asset and ClassBalanceConfig.asset to the CoreScene RootLifetimeScope fields (same guids CombatSystemsScene uses). Then make the seam loud instead of silent: throw or Debug.LogError in Configure when either is null, and add an EditMode guard that loads CoreScene and asserts both references are non-null and identical to the ones in CombatSystemsScene — the same asset-pinning discipline SimTuningConfig already has in ConfigValidationTests.

---

### BE-1 · P0 · truth — Root scope ships with StatsConfig and ClassBalanceConfig unwired — the loadout stat panel prints Здоровье 0 / Скорость 0, and the editor preview stand hides it

`Assets/_Project/Scenes/CoreScene.unity:295` · линза `build-vs-editor`

```csharp
CoreScene.unity:291-296 — `_contentDatabase: {fileID: 11400000, ...}` / `_gameConfig: {...}` / `_audioCatalog: {...}` / `_actConfig: {fileID: 0}` / `_statsConfig: {fileID: 0}` / `_classBalanceConfig: {fileID: 0}`.
RootLifetimeScope.cs:48-53 warns in the very tooltip: «Стат-конфиг (дефолты статов). ТОТ ЖЕ ассет, что в CombatLifetimeScope — иначе панель инвентаря покажет числа, не совпадающие с боем.»
RootLifetimeScope.cs:84-85: `builder.Register<IUnitStatPreview>(_ => new UnitStatPreview(_statsConfig, _classBalanceConfig), Lifetime.Singleton);` — the only registration of IUnitStatPreview in the project.
Stats.cs:190-191: `private float DefaultOf(StatType stat) => _config != null ? _config.GetDefault(stat) : StatsConfig.NaturalDefault(stat);` and StatsConfig.cs:46-64 `NaturalDefault` returns 0 for MaxHP/MoveSpeed.
ClassBaseline.cs:26-30: `if (stats == null || data == null || config == null) return;` — a silent no-op, so the entire class layer of the cascade is skipped.
CombatSystemsScene.unity:453-454 by contrast: `_statsConfig: {fileID: 11400000, guid: ad16ddf55214b4f4f9fc8305dadd705e}` / `_classBalanceConfig: {fileID: 11400000, guid: c346355e64f53d444b5cc7a9fe184c84}`.
StatsConfig.asset:16-27 holds `Stat: 0 / Value: 1200` (MaxHP) and `Stat: 20 / Value: 3` (MoveSpeed).
Relics/Assassin.asset:34-46 authors only PhysArmor/AutoAttackDamage/AttackSpeed/AttackRange — no MaxHP, no MoveSpeed entry at all.
```

**Чем стреляет.** Open Инвентарь, select Assassin: LoadoutViewModel.ResolveStats (LoadoutViewModel.cs:102-103) → UnitStatPreview.Basic (UnitStatPreview.cs:50-57) builds stats with a null StatsConfig and a null ClassBalanceConfig, so base MaxHP = NaturalDefault = 0 and the class baseline that owns HP/MoveSpeed never applies. The panel renders «Здоровье 0» and «Скорость 0» while the same unit fights with 1200-plus HP from CombatLifetimeScope. This is invisible in the one place the panel is actually eyeballed: the editor-only stand builds its own preview from the real assets — UiPreviewCatalog.cs:183-184 `new Guildmaster.Combat.UnitStatPreview(LoadFirst<StatsConfig>(), LoadFirst<ClassBalanceConfig>())` (AssetDatabase.FindAssets, UiPreviewCatalog.cs:553-555). So Alebardium/UI Preview shows correct numbers and the game does not.

**Куда править.** Assign StatsConfig.asset and ClassBalanceConfig.asset on the RootLifetimeScope component in CoreScene (same GUIDs as CombatSystemsScene.unity:453-454). Then make the silence impossible: have RootLifetimeScope.Configure throw or Debug.LogError when either is null instead of handing `new UnitStatPreview(null, null)` to the container, and add an EditMode guard test that loads CoreScene + CombatSystemsScene and asserts both scopes reference the identical StatsConfig and ClassBalanceConfig instances.

---

### R1-13 · P0 · correctness — Topbar tabs stay clickable over an awaited Page and permanently bury it — run loop soft-locks

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:277` · линза `ui-coordination`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P1` (уверенность high)

```csharp
_layerScreens      = AddLayer(root, "layer-screens");
_layerTopbar       = AddLayer(root, "layer-topbar");
_layerModal        = AddLayer(root, "layer-modal");
```

**Чем стреляет.** Page screens (shop/chest/camp/reward) go into _layerScreens, which is added BEFORE _layerTopbar, so the topbar draws and picks above them; nothing disables the mode chips based on the top screen (RefreshShell only calls SetActiveMode/SetMenuActive). A shop node runs with BattlePhase.None (RunBeatStage.EnterNode), so CanEnterSandbox() passes: while `ShowShopAsync` awaits `_nav.ShowAsync`, one click on «Бой» publishes SetTestZoneRequest(true) -> DeploymentController.EnterSandbox -> TestZoneChangedEvent(true) -> `_router.ShowTestZone()` pushes a Sheet on top. UiNavigator.SyncVisibility then hides the shop Page (`hidden = pageAbove || (sheetAbove && s.Kind == ScreenKind.Page)`), and the Page never resolves. Every tab (GoToBattle/GoToMap/GoToInventory) leaves at least one Sheet on the stack, so the buried Page can never become visible again — the only exit is abandoning the run from the ESC menu.

**Куда править.** Gate the topbar on the navigator: when `_nav.Top.Kind == ScreenKind.Page`, either disable the mode chips (SetEnabled(false) from RefreshShell) or let the topbar live in a layer below Page screens. Ideally the navigator should refuse to Push a Sheet while an awaited Page is on top.

---

### R1-72 · P0 · correctness — Balance auditor builds stats without the class/species cascade — every MaxHP and MoveSpeed in the audit report is wrong

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:82` · линза `dead-and-bloat`

```csharp
var stats = new Stats(config);
            if (data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);

            float maxHp = stats.Get(StatType.MaxHP);
```

**Чем стреляет.** This is the 4th copy of the stat cascade and the only one missing two levels. The other three (RuntimeUnitFactory.cs:69-75, UnitStatPreview.cs:63-67, StatMath.cs:21-26) all do `new Stats(config)` -> `ClassBaseline.Apply` -> `EnemyScalers.Apply` -> `AddModifiersFrom(data, data.Stats)`. ClassBaseline injects MaxHP/MoveSpeed as Override modifiers from ClassBalanceConfig (Bruiser 2000 HP anchor, Tank x1.5, backline x0.65). Without it, Audit Content prints the raw StatsConfig default HP for every relic/enemy with no explicit MaxHP modifier — Bruiser and Tank come out identical, the z-score outlier flags are computed on fictional numbers, and both EHP columns inherit the error. BalanceAssets.LoadClassBalanceConfig() already exists (used at SimEnvironment.cs:48), so the config is one call away. The class docstring claims 'реюз боевого Stats + StatsConfig, НЕ свои формулы'.

**Куда править.** Replace BuildRow's stat assembly with StatMath.BuildEffective(data, config, BalanceAssets.LoadClassBalanceConfig()) and thread the class config down from Run().

---

### RL-1 · P0 · architecture — Pause has two owners; the run loop resets only one, so a Space-pause freezes every later battle in the session

`Assets/_Project/Scripts/Game/Services/TimeScaleService.cs:192` · линза `run-loop-integrity`

```csharp
Two independent stores answer "is the game paused". (1) CombatSimulation._isPaused — written by the flow: BattleBootstrap.cs:90,110,120 (`_sim.SetPaused(true)`), WorldStageController.cs:53, DeploymentController.cs:587 (`_sim.SetPaused(false)`). (2) TimeScaleService._paused — `public void SetPaused(bool paused) { _paused = paused; Apply(); }` (TimeScaleService.cs:192) → `Time.timeScale = eff` (line 201), written by EXACTLY ONE caller: BattleInputController.cs:52-57 `bool paused = !_simulation.IsPaused; _simulation.SetPaused(paused); _time.SetPaused(paused);`. Nothing in the run loop ever clears (2). TimeScaleService.Reset() refuses on purpose: "Игровую скорость и паузу игрока НЕ трогаем (это его выбор, переживает рестарт)". The only restore is Dispose() (`Time.timeScale = 1f;`, line 234), and TimeScaleService is `Lifetime.Scoped` in CombatLifetimeScope.cs:67 — a scope GameBootstrap.cs:53 raises once (`await _sceneLoader.LoadCombatSystemsAsync();`) and never unloads, so Dispose never runs during a session. CombatLoopService.StartAsync accumulates `_accumulator += Time.deltaTime;` — scaled — so timeScale 0 means zero sim ticks regardless of _isPaused.
```

**Чем стреляет.** Repro: battle node → «Начать» → Space (timeScale 0) → ESC → «В главное меню». Space cannot undo it from there: it is gated by GameplaySuppressed while the pause modal is up (InputService.cs:192), and after GameFlow.RunActAsync's finally does `_session.SetPhase(BattlePhase.None)` (GameFlow.cs:197) UiNavigator.WorldContextOf maps None → InputContext.None (UiNavigator.cs:239-246), which disables the whole Combat action map that owns Space (InputService.cs:143-146). Start a new run and press «Начать»: the topbar flips to the battle timer, the timer stays 00:00 and nothing moves, for this and every later battle in the session. Only a double-tap of Space (once to set paused=true, once to clear it) recovers.

**Куда править.** Give pause one owner: let TimeScaleService be the only store and have CombatSimulation read it (or vice versa), and clear it on every run-boundary transition. Minimum: call `_time.SetPaused(false)` from BattleBootstrap.ResetToWorld and DeploymentController.StartCombat, and drop the "пауза игрока переживает рестарт" exemption in TimeScaleService.Reset for run-level resets.

---

### RL-13 · P0 · gap — Camp node is a mandatory stop on every act map and its screen asset is unassigned — the node resolves to nothing, silently

`Assets/_Project/Scenes/CoreScene.unity:160` · линза `run-loop-integrity`

```csharp
CoreScene.unity:160 `_campScreen: {fileID: 0}` — the only UiRootBootstrap in the project leaves it empty. CampScreen.uxml is referenced by ZERO scenes/prefabs/assets (I resolved its guid 3dfc93592132fd343b4d7174836192c7 and grepped all .unity/.prefab/.asset — 0 hits; every other screen uxml has exactly 1). MenuRouter.cs:807 `if (_root == null || _campUxml == null || req.Session == null) { req.OnLeave?.Invoke(); return; }` — no log, no warning. ActConfig.asset Anchors: `- Floor: 8 / Type: 8 / Width: 3` and `- Floor: 13 / Type: 8 / Width: 3`; MapNodeType index 8 = Camp (RunState.cs:21), and MapGenerator.RollNodeType:113-114 `if (cfg.Anchors[i].Floor == col) return cfg.Anchors[i].Type;` makes the anchor override the whole column.
```

**Чем стреляет.** Two full columns of Camp are anchored into EVERY generated act, and floor 13 is the last row before the Boss at floor 14 — the player cannot route around it. CampFlow, CampSession, CampScreenView and CampScreen.uxml are therefore all unreachable content, while the farewell frame (which uses the assigned _eventUxml) still fires, so the player gets the "you leave the camp" text for a camp that never opened.

**Куда править.** Assign CampScreen.uxml to `_campScreen` on the UiRootBootstrap in CoreScene, and turn the silent `req.OnLeave()` guards in MenuRouter (camp/shop/chest/reward/outcome/main-menu) into `Debug.LogError` — a missing screen asset must be loud, not a node that completes itself.

---

### UA-13 · P0 · truth — The loadout stat panel is wired to a null stat cascade: CoreScene leaves StatsConfig and ClassBalanceConfig unassigned, so every relic shows HP 0 / Speed 0

`Assets/_Project/Scenes/CoreScene.unity:295` · линза `uncovered-assemblies`

```csharp
CoreScene.unity:294-296 (the RootLifetimeScope MonoBehaviour block that starts at line 275):
  _actConfig: {fileID: 0}
  _statsConfig: {fileID: 0}
  _classBalanceConfig: {fileID: 0}

RootLifetimeScope.cs:48-50 warns about exactly this: "Стат-конфиг (дефолты статов). ТОТ ЖЕ ассет, что в CombatLifetimeScope — иначе панель инвентаря покажет числа, не совпадающие с боем."
RootLifetimeScope.cs:84-85: `builder.Register<IUnitStatPreview>(_ => new UnitStatPreview(_statsConfig, _classBalanceConfig), Lifetime.Singleton);`
UnitStatPreview.cs:63-64: `var stats = new Stats(_config); ClassBaseline.Apply(stats, data, _classBalance);` — Stats(null) falls to StatsConfig.NaturalDefault (0 for MaxHP/MoveSpeed) and ClassBaseline.Apply is `if (stats == null || data == null || config == null) return;` (ClassBaseline.cs:27).
Grep of the whole Assets tree for the StatsConfig guid ad16ddf55214b4f4f9fc8305dadd705e and ClassBalanceConfig guid c346355e64f53d444b5cc7a9fe184c84 returns exactly one hit each — CombatSystemsScene.unity:453-454. Nothing else references them.
```

**Чем стреляет.** HARD rule: the SO asset is what the game plays. The C# is correct and the CombatSystemsScene copy is correct, but the copy the inventory panel reads is null, so the panel silently prints the natural defaults instead of the cascade. LoadoutViewModel.ResolveStats (LoadoutViewModel.cs:102-103) is the non-tooltip caller: the three-column inventory detail pane.

**Куда править.** Assign ScriptableObjects/Configs/StatsConfig.asset and ClassBalanceConfig.asset on the RootLifetimeScope component in CoreScene, and add an EditMode guard test that loads CoreScene's RootLifetimeScope and asserts both fields resolve to the same assets CombatSystemsScene uses. Better root fix: register the two configs once (ContentDatabase-style lookup or a shared ConfigSet asset) instead of duplicating the same two object references across two scenes.

---

### AC-13 · P1 · architecture — Every unit's AI is stored twice — an assigned preset asset and an invisible legacy inline block — and 8 of 15 already disagree

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/UnitData.cs:97` · линза `assets-vs-code`

```csharp
UnitData.cs:95-97 — "// Легаси inline-профиль AI: источник миграции в AIPresetData (§3.2). Удаляется после назначения\n// пресетов (отдельный шаг пакета 3). До тех пор — фолбэк для Ai, если пресет ещё не назначен.\n[SerializeField, HideInInspector] private AIProfile _ai = new AIProfile();" and UnitData.cs:139 "public AIProfile Ai => _aiPreset != null ? _aiPreset.Profile : _ai;". The deletion step never happened: all 15 unit assets still carry a full _ai block. Byte-diffing each inline block against its assigned preset shows 8 disagreements, e.g. Relics/LightShepherd.asset:87 "_autoAttackMode: 1" (Heal) vs AiPresets/LightShepherd.asset:18 "_autoAttackMode: 0" (Damage), and Relics/Druid.asset:82ff Retreat "Enabled: 0" vs AiPresets/Druid.asset "Enabled: 1 / FleeAtHpPct: 0.3". Two assets have no preset at all — Relics/BaseRelic.asset:42 "_aiPreset: {fileID: 0}" and Enemies/TrainingDummy.asset:42 — so for them the [HideInInspector] legacy copy is the live source.
```

**Чем стреляет.** relic.base is the kit on all four starting vessels (RunStateService.cs:60 falls back to "relic.base"), so the entire starting party's targeting, retreat and passive-trigger behaviour is driven by a field the Inspector refuses to show and that no designer can edit; meanwhile the 13 preset assets they CAN edit are silently ignored for those two units. For the other 13 the reverse holds: editing the visible values is impossible and the stale copy sits in the file waiting for the next person who greps '_ai:' to 'fix' the wrong owner.

**Куда править.** Delete the _ai field and its serialized blocks (the migration's own comment says to), author ai_preset.base and ai_preset.training_dummy so nothing falls back, and make UnitData.Ai throw/validate on a null preset instead of silently reaching for legacy data.

---

### AC-14 · P1 · dead — The Vessel cascade level is a fully wired seam with zero assets: VesselId is only ever written as empty, and a test locks that in

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs:77` · линза `assets-vs-code`

```csharp
ScriptableObjects/Vessels/ is an empty folder — zero VesselData assets exist. The only writer of the id is RunStateService.cs:68 "VesselId      = string.Empty,"; the only reader is GuildRoster.cs:44 "if (!string.IsNullOrEmpty(rs.VesselId)) content.TryGet(rs.VesselId, out vessel);". So `vessel` is null on every path, and RuntimeUnitFactory.cs:77 "if (vessel?.PerkModifiers != null && vessel.PerkModifiers.Length > 0)" never fires. All 11 BattlePreset assets pin it too (PresetAssassin.asset:19 "_vessel: {fileID: 0}", and 4× in PresetPartyVsRaid.asset). A test freezes the dead state: Tests/EditMode/Guild/GuildRosterTests.cs:43 "Assert.IsEmpty(s.VesselId, \"Сосуд-контента пока нет → VesselId пуст.\")".
```

**Чем стреляет.** VesselData is threaded through 8 production sites — RuntimeUnitFactory.Create's signature, EncounterLoader.PlayerSpawn, RuntimeUnit.Vessel (RuntimeUnit.cs:120), DeploymentController.Slot, LoadoutMessages.OpenLoadoutRequest, BattlePresetData._vessel, RunState.VesselId — and the class docstring sells it as the 4th level of the documented stat cascade. A reader tracing why a stat is wrong will walk all of it before discovering there is no data behind any of it, and the green test says the emptiness is correct.

**Куда править.** Either author the vessel assets the cascade documents, or delete VesselData and drop the parameter from the eight call sites; deleting the GuildRosterTests assertion that certifies the hole.

---

### AC-15 · P1 · dead — Four whole content types have zero assets and zero readers — they exist only as two registry rows and a CreateAssetMenu each

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/GuildmasterData.cs:11` · линза `assets-vs-code`

```csharp
GuildmasterData (GuildmasterData.cs:11), TraitData (TraitData.cs:11), RunModifierData (RunModifierData.cs:11) and ConsequenceData (ConsequenceData.cs:11) each carry a [CreateAssetMenu(menuName = "Guildmaster/Content/…")], a domain row in ContentDomains.cs:26-31 ({ typeof(TraitData), "trait" }, { typeof(ConsequenceData), "consequence" }, { typeof(GuildmasterData), "guildmaster" }, { typeof(RunModifierData), "run_mod" }) and a folder row in ContentPaths.cs:26-31. Grepping the whole of Scripts+Tests for each type name returns ONLY those two registry lines. Zero .asset files of any of the four exist under ScriptableObjects/ (the named folders Guildmasters/Traits/Consequences/RunModifiers do not exist). Their properties confirm it: GuildmasterData.Spells/UniqueEffects/StartingRelicIds/StartingGold, TraitData.SelectionWeight/ExclusiveGroup, RunModifierData.RewardMult, ConsequenceData.HealCostGold all have no reader anywhere.
```

**Чем стреляет.** ConsequenceData in particular is a decoy: TextEventData does NOT use it (events carry inline EventEffect arrays, TextEventData.cs:48), so a reader adding event outcomes will reasonably start from the type named 'Consequence' and build on nothing. The four types also pollute the Content Hub / content-validation enumeration with permanently empty categories.

**Куда править.** Delete the four .cs files and their ContentDomains/ContentPaths rows. If any is a deliberate placeholder, it belongs on the roadmap, not in the content registry where it looks wired.

---

### AC-16 · P1 · truth — The .gitignore rule added to hide the personal scratch scene does not match its filename, and the 976 KB near-duplicate of WorldScene is still exposed

`C:/My Projects/Guildmaster-Autobattler/.gitignore:137` · линза `assets-vs-code`

```csharp
.gitignore:135-138 — "# Personal scratch scenes (per-developer sandboxes, never shared)." … "Assets/_Project/Scenes/*Scene For Tests.unity". The file on disk is Assets/_Project/Scenes/MaxSceneForTests.unity — no spaces — so the glob never matches: `git check-ignore -v` returns rc=1 (no rule) and `git status --porcelain` reports "?? Assets/_Project/Scenes/MaxSceneForTests.unity". The scene is 976429 bytes against WorldScene.unity's 980470 and is a stale copy of it: its CameraModeController block (MaxSceneForTests.unity:32048) has "_overviewCam: {fileID: 0}", "_devCam: {fileID: 0}", "_mapCam: {fileID: 0}" where WorldScene.unity:32185-32187 has all three wired.
```

**Чем стреляет.** The rule reads as protection but provides none — one `git add -A` (a move the project's own notes warn against for CoreScene) commits a megabyte of a broken world-scene clone. And because it sits beside WorldScene.unity with a plausible name, it is exactly the file someone opens and edits by mistake; three null vcams mean Overview/Dev/Map camera modes silently do nothing there, which reads as a code bug rather than a scene defect.

**Куда править.** Change the pattern to something that matches (e.g. Assets/_Project/Scenes/*SceneForTests.unity plus the spaced variant, or ignore the exact filename), and delete MaxSceneForTests.unity from the working tree once it is genuinely ignored.

---

### AC-2 · P1 · gap — The whole Camp-node feature is switched off by one {fileID: 0} in CoreScene, and every act map has two guaranteed Camp floors

`Assets/_Project/Scenes/CoreScene.unity:160` · линза `assets-vs-code`

```csharp
CoreScene.unity:160 — `_campScreen: {fileID: 0}`, while every sibling screen is wired (`_shopScreen`, `_chestScreen`, `_outcomeScreen`, … all carry guids). MenuRouter.cs:805 bails out silently: `if (_root == null || _campUxml == null || req.Session == null) { req.OnLeave?.Invoke(); return; }`. The content exists and is complete: Assets/_Project/UI/Screens/CampScreen.uxml, CampScreenView.cs (105 lines), CampFlow.cs, CampSession, and all 8 loc keys are authored (`UI Shared Data.asset:19` `m_Key: ui.camp.title` … `ui.camp.action.move_on`).
```

**Чем стреляет.** MapGenConfig.DefaultAnchors (MapGenConfig.cs:105-110) pins `new AnchorRule(8, MapNodeType.Camp, width: 3)` and `new AnchorRule(13, MapNodeType.Camp, width: 3)`, and ActConfig.asset:57-63 mirrors them (`Floor: 8 / Type: 8`, `Floor: 13 / Type: 8` — 8 = Camp). NodeResolver.cs:116 routes those nodes to `new CampFlow(...)`. So on every single run the player walks onto floor 8 and floor 13, the screen never opens, `OnLeave` fires immediately and the node completes with no interaction — the 'привал перед боссом' beat that MapGenConfig.cs:99-104 describes at length simply does not happen. CampScreenView.Build would also NRE on `uxml.CloneTree()` (CampScreenView.cs:32) if the guard were ever removed.

**Куда править.** Assign CampScreen.uxml to `_campScreen` on the UI Root component in CoreScene. Because a null UXML degrades a designed beat into a no-op rather than an error, replace the silent `return` in MenuRouter.OpenCamp with a Debug.LogError, and add a guard test that asserts every VisualTreeAsset field on the CoreScene UiRootBootstrap is assigned — this class of bug is invisible in play until someone notices a node that does nothing.

---

### AC-3 · P1 · truth — ActConfig.asset is referenced by nothing, so the act map is generated from C# defaults while a guard test pins the orphaned file

`Assets/_Project/ScriptableObjects/Configs/ActConfig.asset:13` · линза `assets-vs-code` · переоформляет R1-49

```csharp
ActConfig.asset's guid `dbc39cb776c7fd6469e9cd31b97af1ab` (from ActConfig.asset.meta) appears in no scene, prefab or asset anywhere under Assets/ or ProjectSettings/ — I swept every text file in the tree for it. CoreScene.unity:294 is `_actConfig: {fileID: 0}`, and RootLifetimeScope.cs:67 therefore takes the other branch: `builder.RegisterInstance(_actConfig != null ? _actConfig : ScriptableObject.CreateInstance<ActConfig>());`. Yet ActConfigAssetTests.cs:9-15 states the opposite in its own docstring: `"Проверяет КОНФИГ, ПО КОТОРОМУ РЕАЛЬНО ИДЁТ ИГРА, а не дефолты из кода … забег генерируется из ActConfig.asset"`, and ActConfigAssetTests.cs:18 hardcodes `AssetPath = "Assets/_Project/ScriptableObjects/Configs/ActConfig.asset"`.
```

**Чем стреляет.** Every one of the five tests in ActConfigAssetTests inspects a file the runtime never loads. Today the asset happens to equal `new MapGenConfig()` field-for-field (I diffed all 6 scalars, 3 zones and 3 anchors), so the suite is green and nobody notices — that is precisely why it misleads. The moment a designer tunes Columns or a zone weight in the Inspector, the test keeps guarding the edited asset while the game keeps generating from MapGenConfig.cs, i.e. the tuning silently has zero effect and the one test written to catch exactly that (twice burned, per its docstring) cannot see it. This also makes ActConfig's own docstring wrong (ActConfig.cs:8-9 'Не назначен в скоуп → фолбэк на дефолтный POCO' describes a fallback that is in fact the only path).

**Куда править.** Assign ActConfig.asset to `_actConfig` in CoreScene — that makes both the docstring and the test true. Then have ActConfigAssetTests read the reference out of CoreScene rather than by path, so an unassigned field fails the test instead of hiding from it. Supersedes R1-49 at the root: the in-place `Validated()` mutation R1-49 describes cannot reach the asset today for the simpler reason that the asset is never loaded.

---

### AC-4 · P1 · architecture — Localization has two String Tables but only one is reachable: 23 authored keys in the "UI" table are resolved against "Content" and always miss

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48` · линза `assets-vs-code`

```csharp
LocalizationService.cs:18 `private const string ContentTable = "Content";` and :48 `public string GetString(string key) => GetString(ContentTable, key);`. Every screen goes through that single-argument overload — MenuRouter.cs:114, 217, 276, 619, 652, 766, 784, 805, 838, 871; UiRootBootstrap.cs:323; LoadoutViewModel.cs:81. Meanwhile 23 non-tooltip keys are authored only in `UI Shared Data.asset` (e.g. :19 `m_Key: ui.camp.title`, plus ui.camp.* ×8, ui.beat.continue, ui.beat.formation, ui.node.{camp,chest,shop}.{title,farewell} ×6, ui.loadout.slot.ability.empty, ui.loadout.slot.upgrade.empty, ui.loadout.tags.more, ui.event.result.fallback). The intent is written down and contradicted: ContinuePresenter.cs:31-33 — `// Подписи кнопок бита (таблица UI): RU заполнен, остальные локали — прочерк до перевода` above `ContinueKey = "ui.beat.continue"`.
```

**Чем стреляет.** GetTableEntryAsync("Content", "ui.beat.continue") returns no entry, LocalizationService.cs:66 turns that into `string.Empty`, and MenuRouter.Label (:745-748) then keeps the UXML literal: ContinueScreen.uxml:8-9 `text="Продолжить"` / `text="К построению"`. So the RU strings a translator filled into the UI table are dead bytes and the shipped text is the hardcoded UXML fallback — switching locale changes nothing on these elements. Six further keys in the tables have no reader at all and are pure dead weight: ui.run.hub, ui.run.settings, ui.run.gold, ui.event.continue, ui.reward.continue (all in Content) and ui.dev.stat_probe (in UI). Two tables own the `ui.` namespace; exactly one can win.

**Куда править.** Give `ui.` a single owner. Either move all 41 UI-table entries into the Content table and delete the UI table, or add a table argument to the ILocalizationService calls that need it — but not both conventions at once. Delete the six unread keys. Then add an EditMode test that walks every `"ui.*"` literal in Scripts/** and asserts the key resolves in the table the runtime will actually query; that single test collapses this finding and the next one into a build-time failure.

---

### AC-5 · P1 · convention — 51 ui.* keys referenced by code exist in neither String Table, so whole screens ship as hardcoded Russian and ignore the locale

`Assets/_Project/Scripts/UI/ShopScreenView.cs:42` · линза `assets-vs-code` · переоформляет R1-18

```csharp
Cross-checking every `"ui.*"` literal in Scripts/** against both tables leaves 51 keys with no entry anywhere. Whole screens are affected: shop (ui.shop.title/gold/buy/sell/sold/reroll/leave/no_space — ShopScreenView.cs:42-127), outcome (ui.outcome.victory/defeat/victory_sub/defeat_sub/to_menu — OutcomeScreenView.cs:30-36), main menu (ui.mainmenu.start/continue/settings/quit — MainMenuScreenView.cs:39-47), loadout (ui.loadout.filter.relics/items/banners, .search, .sort.name, .basics, .skills, .upgrades, .stats, .video — LoadoutInventoryView.cs:67-90), the run topbar (ui.mode.map/battle/inventory/tactics/compendium/menu, ui.run.floor — RunModeBarView.cs:48-105), the stat panel (ui.stat.hp/parmor/marmor/dmg/aspd/range/move — UnitStatPreview.cs:50-57), pause (ui.menu.quit, ui.menu.to_main_menu — MenuRouter.cs:428-434) and chest (ui.chest.title/hint — ChestScreenView.cs:29-30). Every call site is of the shape `L("ui.shop.title", "Лавка")`, so the second argument is what the player sees.
```

**Чем стреляет.** Project rule is HARD: all player-facing text through localization keys with RU filled. Here the key exists only as a string literal in C# — the table row was never created — so the RU literal in code is the single source of the shipped string and `SetLocale("en")` provably changes nothing on the shop, the outcome screen, the main menu, the loadout, the topbar or the stat table. Because the fallback always fires, nobody can tell a missing translation from a working one, and a future translator has no rows to fill. Supersedes R1-18 (settings volume labels), which is one instance of this pattern rather than the pattern.

**Куда править.** Create the 51 rows in whichever table becomes the single owner (see previous finding), with RU filled from the existing code fallbacks and other locales dashed. Keep the `L(key, ru)` fallback as a crash guard but add the key-coverage EditMode test so an unauthored key fails CI instead of silently shipping.

---

### AC-6 · P1 · legacy — relic.base still hard-Overrides MaxHP to 1200, so every run's four starting vessels sit 40% under the Bruiser balance anchor

`Assets/_Project/ScriptableObjects/Relics/BaseRelic.asset:34` · линза `assets-vs-code`

```csharp
BaseRelic.asset:15-16 `_id: relic.base` / `_combatClass: 0` (Bruiser), and :34-36 `- Stat: 0` / `Op: 3` / `Value: 1200` — StatType.MaxHP with ModifierOp.Override. ClassBalanceConfig.asset:15 sets `_baseHp: 2000` and :18-20 gives Bruiser `HpMult: 1`, and ClassBalanceConfig.cs:27 documents it as the anchor: `"Базовое HP золотой середины (Брузера) … Танк ×1.5 = 3000"`. The cascade rollout stripped this override everywhere else — FlameSwordsman.asset (also `_combatClass: 0`) has a `_stats` block of AutoAttackDamage/AttackSpeed/AttackRange/PhysArmor and no MaxHP entry at all; 9 of 11 relics are the same. relic.base and enemy.training_dummy are the only two assets left carrying it.
```

**Чем стреляет.** ClassBaseline.Apply adds the class Override first and ModifierOp.cs:23-24 says `"При нескольких Override на один стат берётся последний"`, so the asset's 1200 wins over the class's 2000. GameConfig.cs:71-72 makes relic.base the kit of every empty vessel (`_startingRelicId = "relic.base"`) and GuildRoster.cs:16,32 substitutes it for any unset slot, so all four vessels the player starts a run with have 1200 HP where the balance anchor says 2000 — a 40% shortfall against every enemy tuned to that anchor. No test can catch it: ClassBaselineTests builds its own profile table rather than reading the asset (R1-55), and the same asset is also one of only two left on the legacy inline AI path (`_aiPreset: {fileID: 0}` at BaseRelic.asset:42, falling back to `_ai` which UnitData.cs:95-96 marks `"Легаси inline-профиль AI … Удаляется после назначения пресетов"`).

**Куда править.** Delete the `Stat: 0 / Op: 3 / Value: 1200` entry from BaseRelic.asset so the empty kit inherits the Bruiser anchor, and assign it an AIPresetData so the legacy `_ai` field can finally be deleted as its own comment promises. Decide the same for enemy.training_dummy. Then add a guard test that walks every RelicData asset and fails on an Override of MaxHP or MoveSpeed — those two stats belong to ClassBalanceConfig alone, and that is the invariant the rollout was supposed to establish.

---

### AC-7 · P1 · dead — The reward rarity ramp and the shop price tiers are inert: every relic asset is Common on both axes

`Assets/_Project/Scripts/Game/Flow/RewardService.cs:61` · линза `assets-vs-code`

```csharp
All 11 relic assets carry `_kitPower: 0` (KitPower.Common) and `_dropRarity: 1` (DropRarity.Common) — verified by grepping the field out of every file in ScriptableObjects/Relics. RewardService.cs:61 `if (r.DropRarity == DropRarity.Unique) uniques.Add(r);` therefore always leaves `uniques` empty, and the per-slot roll at :70-79 always lands in `regular` via the fallback `wantUnique ? (uniques.Count > 0 ? uniques : regular) : …`. The documented behaviour at RewardService.cs:20-22 is `"шанс уникальной реликвии в слоте витрины — 10% за рядовой бой, 20% за элиту, 100% за босса … у босса вся витрина уникальная"`. Same for pricing: RelicPricer.cs:26-30 branches on KitPower.Divine/Cursed, and no asset is either.
```

**Чем стреляет.** RewardTier, UniqueChance, the per-slot `_rng.NextFloat()` draw, and the boss/elite distinction in the reward showcase have no observable effect — the boss's three-relic offer is drawn from the identical pool as a floor-1 trash mob's. GameConfig.PriceCursed (100) and PriceDivine (150) are likewise unreachable, so every relic in every shop costs PriceCommon ± spread. Four documented economy features (unique ramp, boss payoff, cursed and divine price tiers) look wired end-to-end in code and are switched off entirely by the content, which is exactly the failure mode that hides for months.

**Куда править.** Either author the content — mark the intended relics DropRarity.Unique and set KitPower on the cursed/divine kits — or delete the ramp and the price branches until the content exists. Whichever way, add a content-validation test asserting the pools the code branches on are non-empty (at least one Unique relic, at least one non-Common KitPower), so 'the mechanic exists but no asset triggers it' fails at edit time.

---

### AC-8 · P1 · dead — No BattlePreset asset is marked elite, so Elite nodes fall back to the whole preset pool — including the training-dummy dev preset

`Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159` · линза `assets-vs-code`

```csharp
Not one of the 11 files in ScriptableObjects/BattlePresets contains an `_isElite` line at all (grep -l returns 0), so every preset resolves IsElite = false. NodeResolver.cs:158-159 `foreach (var p in all) if (p != null && p.IsElite == wantElite) pool.Add(p);` therefore yields an empty pool for every Elite node, and :161-166 takes the fallback: `if (wantElite) Debug.LogWarning("[NodeResolver] - нет элит-пресетов → беру обычный бой …"); return all[ctx.Rng.NextInt(0, all.Count)];`. That unfiltered pool contains PresetBaseKit.asset, whose `_encounter` guid `5c30ce46fe77f14448c5e9b6b0b6ea3f` is DummyTrio.asset — `_enemyId: enemy.training_dummy`, `_count: 3` (DummyTrio.asset:18-21).
```

**Чем стреляет.** Two failures from one missing asset value. First, Elite nodes are indistinguishable from Battle nodes apart from `rewardCount = 2` (NodeResolver.cs:96) — the elite filter, its warning branch and BattlePresetData._isElite are a seam no asset exercises, and ActConfig zones put Elite at up to 28% weight on floors 9-13. Second, both node types roll from the raw pool, so a live run has a 1-in-11 chance per battle node of fighting three training dummies (a dev fixture whose only stats are MaxHP 1200 / AutoAttackDamage 100), and PresetDeployDemo — a deployment demo — is equally eligible.

**Куда править.** Mark the elite presets `_isElite: 1` in the assets (or delete the flag and pick elites some other way), and separate the dev presets from the run pool — a `_devOnly` flag honoured by PickBattlePreset, or move PresetBaseKit/PresetDeployDemo/Dummy* out of ContentDatabase so only the dev picker can reach them. Add a validation test asserting the elite pool is non-empty and that no run-eligible preset points at a Dummy* encounter.

---

### BE-2 · P1 · correctness — CampScreen.uxml is referenced by nothing — every camp node on the act map silently completes without showing a screen

`Assets/_Project/Scenes/CoreScene.unity:160` · линза `build-vs-editor`

```csharp
CoreScene.unity:152-170 lists every screen UXML wired onto UiRootBootstrap; line 160 is the sole exception: `_campScreen: {fileID: 0}`.
Guid sweep: `Assets/_Project/UI/Screens/CampScreen.uxml` guid `3dfc93592132fd343b4d7174836192c7` appears in exactly one file in the whole project — its own `.meta`. No scene, prefab, asset or other UXML references it.
MenuRouter.cs:803-807: `public void OpenCamp(OpenCampRequest req) { if (_root == null || _campUxml == null || req.Session == null) { req.OnLeave?.Invoke(); return; } ShowCampAsync(req).Forget(); }` — no log, no warning.
MapGenConfig.cs:106-110 anchors camp columns on two floors of every act: `new AnchorRule(8, MapNodeType.Camp, width: 3)` and `new AnchorRule(13, MapNodeType.Camp, width: 3)`.
CampFlow.cs:31 publishes `new OpenCampRequest(session, () => tcs.TrySetResult(), ctx.Cancellation)`; UiRootBootstrap.cs:230 forwards it: `_openCampSubscription = _openCampSub?.Subscribe(req => _router.OpenCamp(req));`
```

**Чем стреляет.** Walk a run to floor 8 (a guaranteed 3-wide camp column) and pick any node: CampFlow publishes OpenCampRequest, MenuRouter.OpenCamp sees `_campUxml == null`, immediately invokes OnLeave and returns. The node is marked cleared, the CampSession budget is discarded unspent, and no camp UI ever appears — the player experiences two dead nodes per act. Nothing logs, so this cannot be spotted from the console either; only reading the scene YAML finds it. CampScreenView.cs (a full screen builder) and CampSession.TryPerform are dead code as a consequence.

**Куда править.** Assign CampScreen.uxml to `_campScreen` on UiRootBootstrap in CoreScene. Then turn the silent bail at MenuRouter.cs:805 into `Debug.LogError` before OnLeave — a missing screen asset should scream, not skip content — and add an EditMode test over CoreScene that asserts every `VisualTreeAsset` field on UiRootBootstrap is non-null.

---

### BE-3 · P1 · truth — ActConfig.asset is an orphan: the act layout has two owners and the code copy is the one that plays, so editing the designer-facing SO changes nothing

`Assets/_Project/Scenes/CoreScene.unity:294` · линза `build-vs-editor`

```csharp
CoreScene.unity:294 — `_actConfig: {fileID: 0}`.
Guid sweep over every .asset/.unity/.prefab/.uxml in Assets/_Project: `ActConfig.asset` guid is referenced by nothing but its own `.meta`.
RootLifetimeScope.cs:65-67: «Ассет не назначен → дефолтный инстанс (POCO-дефолты с зонами/якорями), игра не падает» / `builder.RegisterInstance(_actConfig != null ? _actConfig : ScriptableObject.CreateInstance<ActConfig>());`
Owner A (code, live): MapGenConfig.cs:16-51 fields plus `DefaultZones()` at MapGenConfig.cs:82-105 and `DefaultAnchors()` at MapGenConfig.cs:106-111.
Owner B (asset, dead): ScriptableObjects/Configs/ActConfig.asset:16-62 — `Columns: 15`, `EdgeColumnWidth: 3`, `MaxEdgesPerNode: 4`, zones 1-4 / 5-8 / 9-13 with the same weights, anchors `Floor: 7 Type: 6`, `Floor: 8 Type: 8`, `Floor: 13 Type: 8`. Byte-for-byte the same layout as the code defaults (MapNodeType per RunState.cs:11-21: Battle=1, Elite=2, Shop=4, Chest=6, Unknown=7, Camp=8).
ActConfig.cs:6-9 states the whole purpose: «дизайнер крутит глубину/ширину/зоны/якоря в инспекторе, не трогая код».
```

**Чем стреляет.** Two copies of the act layout exist and they currently agree, which is exactly why nobody notices. The moment a designer edits ActConfig.asset — move the pre-boss camp from floor 13, widen the waist, retune zone weights — the generated map does not change, because `_actConfig` is null so GameFlow.cs:167 (`_runStates.BeginAct(_actConfig != null ? _actConfig.ToGenConfig() : null)`) passes null and MapGenerator runs on MapGenConfig's field initialisers. The debugging cost is a full afternoon: the asset is right, the map is wrong, and no log fires. Note this also neutralises R1-49 (ToGenConfig handing out the SO's own instance for Validated() to mutate) — that path is currently unreachable, which is worse news, not better.

**Куда править.** Pick one owner. Assign ActConfig.asset to `_actConfig` in CoreScene and strip MapGenConfig's `DefaultZones()`/`DefaultAnchors()` down to empty arrays so the asset is the only place the layout exists; make RootLifetimeScope.cs:67 log an error instead of silently minting a runtime ActConfig. Add a guard test asserting the registered ActConfig instance is the project asset, not a CreateInstance temp.

---

### BE-4 · P1 · correctness — A shipped build picks its language from the OS with no fallback and no in-game switch; the UI table's English column is nine literal dashes

`Assets/_Project/Localization/LocalizationSettings.asset:41` · линза `build-vs-editor`

```csharp
LocalizationSettings.asset:15-17 startup chain, in order: rid …121 = `CommandLineLocaleSelector` (`m_CommandLineArgument: -language=`), rid …122 = `SystemLocaleSelector` (line 41), rid …123 = `SpecificLocaleSelector` with `m_LocaleId: {m_Code: en}` (lines 44-47). Locales present: only `en.asset` and `ru.asset`.
LocalizationSettings.asset:72 — `m_UseFallback: 0` on the string database, so `ru` never backfills `en`.
UI_en.asset:20-54 — nine entries, every one a placeholder: `m_Localized: "—"` (x2) then `m_Localized: '-'` (x7). UI_ru.asset has 41 entries against 41 keys in `UI Shared Data.asset`.
Mapping those nine ids through `UI Shared Data.asset`: `ui.beat.continue`, `ui.beat.formation`, `ui.event.result.fallback`, `ui.node.shop.title`, `ui.node.shop.farewell`, `ui.node.chest.title`, `ui.node.chest.farewell`, `ui.node.camp.title`, `ui.node.camp.farewell`.
MenuRouter.cs:465 — `string L(string key, string ru) { string v = _loc?.GetString(key); return string.IsNullOrEmpty(v) ? ru : v; }` (same shape in MainMenuScreenView.cs:21-25, RunModeBarView.cs:132, and six other views): a dash is not empty, so the RU fallback is bypassed.
No escape hatch exists: `ILocalizationService.SetLocale` (declared ILocalizationService.cs:49, implemented LocalizationService.cs:81-87) has zero callers anywhere; `GameConfig._defaultLocale = "en"` (GameConfig.cs:20) and its getter `DefaultLocale` (GameConfig.cs:77) have zero readers; grep for language/locale/язык in SettingsScreen.uxml and SettingsViewModel.cs returns nothing.
```

**Чем стреляет.** Ship to a player on an English-locale Windows: SystemLocaleSelector resolves `en`, `m_UseFallback: 0` blocks the ru table, and the between-nodes rest beat — hit after every single node — renders its two buttons as the literal characters «—» and «—» (ui.beat.continue / ui.beat.formation), while the shop, chest and camp headers read «-». The remaining 32 UI keys have no `en` row at all, so `GetString` returns empty and the Russian hardcoded fallback shows instead: the screen is a mix of dashes and Russian, and the player has no way to switch, because nothing calls SetLocale. Play mode never reproduces it — the editor uses the developer's pinned locale, and on a Russian OS the selector picks `ru` anyway.

**Куда править.** Two independent gaps, fix both. (1) Make ru the safety net: set `m_UseFallback: 1` on the string database and either delete the nine dash rows from UI_en.asset (missing → empty → the existing RU fallback fires) or fill them. (2) Close the dead seam: either wire `GameConfig.DefaultLocale` + `ILocalizationService.SetLocale` into a language control on the settings screen, or delete `_defaultLocale`/`DefaultLocale`/`SetLocale`/`AvailableLocales` so nobody believes locale switching exists. Add a validation test asserting no locale table entry is `-` or `—`.

---

### BE-5 · P1 · dead — ModalPanel is a dead UITK control that claims to own the frame every overlay repeats

`Assets/_Project/Scripts/UI/Components/ModalPanel.cs:11` · линза `build-vs-editor`

```csharp
ModalPanel.cs:5-11: «Каркас модального оверлея: затемняющий scrim + центр-панель + заголовок + дивайдер + тело. Дедуплицирует раму, которую повторяет каждый оверлей (награда/ивент/хаб/настройки/пауза).» followed by `[UxmlElement] public partial class ModalPanel : VisualElement`.
Proof of death: the string `ModalPanel` appears in exactly one file in Assets — its own. Zero hits across every `*.uxml` and `*.uss` under Assets/_Project/UI (the only two UXML directories in the project are UI/Dev and UI/Screens), zero hits in any `*.cs`, `*.unity` or `*.prefab`. No `new ModalPanel()`, no `<Guildmaster.UI.Components.ModalPanel>` tag. By contrast the live sibling controls resolve: SlantedChip → RunModeBar.uxml + components.uss; AspectBox → 2 UXML; SliderRow/ToggleRow/Slot/Chip → 1-3 UXML each.
The frame it claims to dedupe is in fact hand-built per screen — e.g. MenuRouter.cs:811-812 constructs the camp screen through `CampScreenView.Build(...)`, and each of RewardScreenView / EventScreenView / ChestScreenView / OutcomeScreenView / MainMenuScreenView clones its own UXML root.
```

**Чем стреляет.** 62 lines of UITK control, with two `[UxmlAttribute]` properties (`Title`, `PanelModifier`) and a `contentContainer` override, that no markup instantiates. The cost is not the bytes: the docstring asserts a shared modal frame exists, so the next person adding an overlay either hunts for the non-existent central place or 'follows the pattern' and adds an eleventh hand-rolled scrim. That is the case for P1 rather than P2 — the dead thing actively misdirects.

**Куда править.** Delete ModalPanel.cs and its .meta. If the deduplication is still wanted, it has to be done for real — extract the scrim+panel+title+divider frame into a UXML template that RewardScreen/EventScreen/ChestScreen/OutcomeScreen/SettingsScreen/PauseScreen actually include — but that is a separate change, not a reason to keep an unreferenced control.

---

### C-01 · P1 · correctness — Corrupt run save is indistinguishable from no save: «Продолжить» becomes a permanent no-op that only logs a warning

`Assets/_Project/Scripts/Game/Services/GameFlow.cs:129` · линза `critic`

```csharp
if (choice == MainMenuChoice.Continue)
{
    if (_runStates.Load() == null) { Debug.LogWarning("[GameFlow] - нет автосейва → назад в меню"); continue; }
}
```

**Чем стреляет.** JsonFileSaveService.Load catches every exception and returns `default` (JsonFileSaveService.cs:38-42), so an unparsable or truncated run.json returns null — the exact same value as "file absent". But the menu's Continue button is gated on `RunStateService.HasSave`, which is `_save.Exists(SaveKey)` — a pure File.Exists. A player whose save got truncated (crash or power loss mid-write, see the sibling finding) sees Continue offered, clicks it, and lands back on the same menu with no message. Clicking it again does the same thing, forever. There is no way to clear the bad file from inside the game (DeleteSave is only reached on run end) and no UI feedback at all — the warning goes to a console the retail player does not have.

**Куда править.** Give ISaveService a three-way load result (Ok / Absent / Corrupt) or have JsonFileSaveService rethrow a typed SaveCorruptException. On corrupt, tell the player and offer to delete the file so Continue stops lying; keep a `.bak` of the last good write so recovery is possible.

---

### C-02 · P1 · correctness — Save writes are non-atomic: File.WriteAllText truncates the only run slot in place, so an interrupted write destroys the run

`Assets/_Project/Scripts/Game/Services/JsonFileSaveService.cs:22` · линза `critic`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
public void Save<T>(string key, T value)
{
    try
    {
        File.WriteAllText(PathFor(key), JsonUtility.ToJson(value, prettyPrint: true));
    }
    catch (IOException e)
```

**Чем стреляет.** There is exactly one run slot (`RunStateService.SaveKey = "run"`) and Autosave fires on every node transition (ActRunner.cs:113,122; GameFlow.cs:99,168,181). File.WriteAllText opens the live save with FileMode.Create — the old bytes are gone the instant the handle opens. A crash, kill, or power loss anywhere in that window leaves a zero-length or half-written run.json, and the whole act is lost. The catch clause is also too narrow: it only handles IOException, so UnauthorizedAccessException (OneDrive/antivirus lock on persistentDataPath, common on Windows) escapes Save and propagates into the flow, where nothing catches it either.

**Куда править.** Write to `key + ".tmp"`, flush, then `File.Replace(tmp, path, path + ".bak")` — atomic on NTFS and leaves a recoverable previous generation. Widen the catch to `Exception` to match the Load side.

---

### C-03 · P1 · architecture — The entire game runs inside one un-guarded UniTaskVoid: a single non-cancellation exception kills the run loop permanently with no recovery path

`Assets/_Project/Scripts/Game/GameBootstrap.cs:40` · линза `critic`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
private void Start()
{
    StartBootAsync().Forget();
}
```

**Чем стреляет.** `StartBootAsync` ends in `await _gameFlow.RunGameAsync()`, whose `while (true)` menu loop is the only thing that can ever return the player to a menu. The only catch in the whole chain is `catch (OperationCanceledException)` at GameFlow.cs:140. ActRunner.cs:94 calls `_resolver.Resolve(node, ctx)`, which reaches `ContentRegistry.Get` — and that throws `KeyNotFoundException` by design (ContentRegistry.cs:32) for any node payload id missing from ContentDatabase, which is exactly the state a designer produces by adding an SO and forgetting to run Sync Content Database. That exception unwinds ActRunner's finally, RunActAsync's finally, and RunGameAsync, then lands on UniTaskVoid as an unobserved exception: one console error, and the game is frozen on whatever screen was last pushed. No menu, no quit, and the autosave is left mid-run. In a retail build there is no console, so it is a silent hang.

**Куда править.** Wrap the `while (true)` body in GameFlow.RunGameAsync with `catch (Exception e)` that logs and continues to the next menu iteration, and pass `this.GetCancellationTokenOnDestroy()` into StartBootAsync so teardown does not run continuations against destroyed objects.

---

### C-04 · P1 · convention — Guildmaster.DevTools ships in the retail player build with 33 unguarded cheat commands, including instant-win

`Assets/_Project/Scripts/DevTools/Guildmaster.DevTools.asmdef:17` · линза `critic`

```csharp
"includePlatforms": [],
  "excludePlatforms": [],
```

**Чем стреляет.** Empty includePlatforms means the assembly compiles into every platform, including the Windows player. Of the ten files in DevTools only the three UiPreview* ones are wrapped in `#if UNITY_EDITOR`; GuildmasterCommands.cs (22 commands), MapDevCommands.cs (6) and VisualFxCommands.cs (5) have no guard at all. Among them: `gm_skip_battle` ("Мгновенно завершить бой в пользу команды A", line 448), `gm_set_hp` (line 428), `gm_rng_seed` (line 141). Quantum Console (QFSW.QC) is a referenced runtime package, so a shipped player who opens the console wins any encounter. Secondary cost: the whole DevTools assembly, its Presentation/UI/Guild/Game references and seven serialized RelicData refs are dead weight in the build.

**Куда править.** Set `"includePlatforms": ["Editor"]` on the asmdef, or if the commands are wanted in dev builds, add `"defineConstraints": ["UNITY_EDITOR || DEVELOPMENT_BUILD"]`. Either way the retail player must not see gm_* commands.

---

### CD-1 · P1 · gap — Elite and Boss nodes have no content at all — the whole difficulty axis of the act is authored but empty

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159` · линза `content-data-integrity`

```csharp
NodeResolver.PickBattlePreset filters `if (p != null && p.IsElite == wantElite) pool.Add(p);` (line 159) and on an empty pool logs "[NodeResolver] - нет элит-пресетов → беру обычный бой" and returns `all[ctx.Rng.NextInt(0, all.Count)]` (lines 163-164). None of the 11 preset assets carries an `_isElite` key at all (grep -l "_isElite" over Assets/_Project/ScriptableObjects/BattlePresets/*.asset returns nothing) → IsElite is false for every preset. Boss is worse: `bool wantElite = node.Type == MapNodeType.Elite;` (line 71) so a Boss node asks for the NON-elite pool. ActConfig.asset authors Elite (Type 2) at weight 18 in floors 5-8 and 28 in floors 9-13, and MapGenerator.cs:46 always appends `NewNode($"c{last}r0", MapNodeType.Boss, last, row: 0)`. The reward side is empty too: RewardService.cs:61 `if (r.DropRarity == DropRarity.Unique) uniques.Add(r);` while all 11 relic assets carry `_dropRarity: 1` (Common) — e.g. Assets/_Project/ScriptableObjects/Relics/Assassin.asset:78 — so UniqueChance(Boss)=1f (RewardService.cs:41) falls through to the regular pool every time. EncounterData.Tier is authored only as 0/1 across all 8 encounters and has no gameplay reader (only DevBattlePickerView.cs:57), and EncounterTier.Finalist is used by no asset.
```

**Чем стреляет.** Roughly a quarter of the generated act (elite floors) plus the act finale resolve to a randomly drawn ordinary battle with a console warning, and the boss reward — the one moment the economy promises a guaranteed unique — hands out the same commons as floor 1. The player cannot tell an elite from a trash fight, and the act has no climax.

**Куда править.** Author at least one `_isElite: 1` preset per act tier and a Finalist/boss preset, and give NodeResolver a Boss branch that asks for it; mark at least one relic `_dropRarity: 2` so the Unique ramp has a pool. Until then make the empty-pool path a hard error in an EditMode content test rather than a Debug.LogWarning at runtime.

---

### CD-13 · P1 · correctness — Every Shop/Chest/Camp node ends on a completely blank farewell card — its keys live in the UI table, the renderer reads the Content table, and there is no fallback

`Assets/_Project/Scripts/UI/MenuRouter.cs:691` · линза `content-data-integrity`

```csharp
ShopFlow.cs:36-37 publishes `new OpenNodeFarewellRequest("ui.node.shop.title", "ui.node.shop.farewell", ctx.NodeCancellation)` (ChestFlow.cs:36 and CampFlow.cs:36 do the same with ui.node.chest.* / ui.node.camp.*). NodeFarewellMessages.cs:21 documents the contract: `/// <summary>Лок-ключ заголовка кадра (таблица UI).</summary>`. But MenuRouter.BuildNodeFarewellScreen renders it with `title.text = _loc?.GetString(req.TitleKey) ?? string.Empty;` / `body.text = _loc?.GetString(req.BodyKey) ?? string.Empty;` (lines 691-692), and LocalizationService.cs:48 is `public string GetString(string key) => GetString(ContentTable, key);` with ContentTable = "Content" (line 18). All six keys exist ONLY in Assets/_Project/Localization/Tables/UI Shared Data.asset (ui.node.shop.title/farewell, ui.node.chest.title/farewell, ui.node.camp.title/farewell) with filled RU values in UI_ru.asset ("Лавка", "Сундук", "Привал", …) — none of them is in Content Shared Data.asset. GetString explicitly returns string.Empty for a missing entry (LocalizationService.cs:66-67 comment: «Отсутствующий ключ → пустая строка, чтобы вызывающий применил свой RU-фолбэк»), so `?? string.Empty` never fires and the assignment overwrites the UXML defaults (EventScreen.uxml:4 `text="Событие"`, line 10 `text=""`).
```

**Чем стреляет.** The farewell card is the mandated single exit ritual of every non-battle node («узел не сваливает игрока в мир, а сворачивается в кадр с текстом»). Today the player finishes a shop, a chest or a camp and gets an empty panel with no title and no text, while the authored Russian copy sits unused one table over. Unlike every other screen this call site has no `L(key, ru)` safety net, so nothing masks it.

**Куда править.** Resolve node-farewell keys through the UI table: `_loc?.GetString("UI", req.TitleKey)`. Keep the explicit table name at the request boundary (the struct already documents it) rather than moving the six keys into Content, so the ui.* namespace stops leaking into the content table.

---

### CD-14 · P1 · truth — The ui.* namespace has two String Table owners; 16 authored, RU-filled UI keys are read from the wrong table and silently lose to hardcoded Russian literals in C# and UXML

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48` · линза `content-data-integrity`

```csharp
`GetString(string key)` defaults to the Content table. Content Shared Data.asset holds 21 ui.* keys (ui.reward.*, ui.hub.*, ui.run.*, ui.titlecard.*, ui.mainmenu.title, ui.event.continue) — these resolve. UI Shared Data.asset holds 42 more ui.* keys, and only two call sites ever name the UI table explicitly (DescriptionService.cs:18 and TooltipContentFactory.cs:21). Every screen uses the Content overload: MenuRouter.cs:814 passes `key => _loc?.GetString(key)` into CampScreenView, whose CampScreenView.cs:42 asks for `L("ui.camp.title", "Привал")`; MenuRouter.cs:662 does the same for EventScreenView.cs:61 `L("ui.event.result.fallback", "Вы двинулись дальше.")`; MenuRouter.cs:281 for LoadoutInventoryView.cs:95-96,451; MenuRouter.cs:465 `string L(string key, string ru) { string v = _loc?.GetString(key); … }` for line 472 `L("ui.settings.tooltip_details", …)`; MenuRouter.Label (line 755-760) for ui.beat.continue / ui.beat.formation. All of those keys are UI-table-only and UI_ru.asset has real Russian values for them. UI_en.asset supplies dashes only for 9 of the 42.
```

**Чем стреляет.** Sixteen strings a designer wrote and translated are dead data; the game always renders the hardcoded Russian literal that sits beside the key (and, for the beat buttons, a THIRD copy in ContinueScreen.uxml:8-9 `text="Продолжить"`). The EN locale for these screens is Russian. Worse, the split is invisible: adding a new key to whichever table looks right is a coin flip, and getting it wrong fails silently because the service returns an empty string instead of erroring.

**Куда править.** Pick one owner for the ui.* namespace — the UI table — move the 21 ui.* keys out of Content, and make the UI screens' `localize` delegate `key => _loc?.GetString("UI", key)`. Then delete the per-call Russian literals so the table is the only owner; add an EditMode test that asserts no key starting with "ui." exists in the Content table.

---

### CD-15 · P1 · gap — The whole reward-rarity ramp is inert: not one shipped relic is DropRarity.Unique, so a boss showcase is identical to a trash-fight showcase

`Assets/_Project/Scripts/Game/Flow/RewardService.cs:61` · линза `content-data-integrity`

```csharp
RollChoices splits the pool with `if (r.DropRarity == DropRarity.Unique) uniques.Add(r); else regular.Add(r);` and then rolls `bool wantUnique = _rng.NextFloat() < uniqueChance` where UniqueChance is Boss=1f / Elite=0.2f / Battle=0.1f (lines 39-44). Every one of the 11 relic assets is authored Common: `_dropRarity: 1` in Assassin/BaseRelic/Cryomancer/Defender/Druid/FlameSwordsman/IronSpearman/LightShepherd/Ranger/Treant/WhirlMonk (e.g. Assets/_Project/ScriptableObjects/Relics/Ranger.asset:109). With `uniques.Count == 0` the fallback at line 78 `(uniques.Count > 0 ? uniques : regular)` sends every roll back to the regular pool. The only DropRarity.Unique values in the repo are in Assets/_Project/Tests/EditMode/Run/RewardServiceTests.cs:82-84, which builds its own pool.
```

**Чем стреляет.** NodeResolver.cs:130-132 carefully maps Elite→RewardTier.Elite and Boss→RewardTier.Boss, and BattleNodeFlow gives an elite two showcases — but the payoff axis those tiers exist to drive does not exist in the data. Beating the act boss offers the same three commons as the first fight. RelicCard.SetRarity (RelicCard.cs:131) paints every card identically for the same reason. The green RewardServiceTests suite gives false confidence because it never touches the shipped pool.

**Куда править.** Author DropRarity on the real relics (at minimum promote 2-3 to Unique and demote filler to Trash), and add a content-validation test that asserts the shipped RelicData pool contains at least one Unique — otherwise UniqueChance and the whole tier plumbing should be deleted as dead.

---

### CD-16 · P1 · truth — "Is this an elite fight" has two owners: EncounterData._tier is authored but read only by a dev label, while BattlePresetData._isElite — the field selection actually reads — is unauthored on all 11 presets

`Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159` · линза `content-data-integrity`

```csharp
Selection reads the preset: `if (p != null && p.IsElite == wantElite) pool.Add(p);`. `_isElite` (BattlePresetData.cs:71) does not appear in the YAML of ANY of the 11 preset assets (grep over Assets/_Project/ScriptableObjects/BattlePresets/*.asset finds no `_isElite`), so it is false everywhere and the elite pool is永 empty. Meanwhile EncounterData carries its own 4-value axis `EncounterTier { Common, Elite, Finalist, Special }` (EncounterData.cs:7-17) and it IS authored non-default: Assets/_Project/ScriptableObjects/Encounters/GoblinRaid.asset:16 `_tier: 1` and DummyScatter.asset:16 `_tier: 1`. Repo-wide the only reader of `EncounterData.Tier` is a picker caption — DevBattlePickerView.cs:57 `$"{Short(e.Id)}  ·  {e.Tier}"`.
```

**Чем стреляет.** Two fields answer the same design question and already disagree: the designer marked GoblinRaid as an elite encounter, the game does not know it. The `Finalist` and `Special` tiers ("Special — только из ивентов; на карте сам не спавнится") have no enforcement at all, so nothing prevents an event-only encounter from being rolled onto the map once such content exists. This is the root under the round-1 observation that Elite/Boss nodes have no content — the axis is not merely unauthored, it is authored on the wrong owner.

**Куда править.** Make EncounterData.Tier the single owner (it is where a designer naturally marks a fight) and derive the preset's elite-ness from `preset.Encounter.Tier == EncounterTier.Elite` in PickBattlePreset; delete BattlePresetData._isElite and its CreateRuntime parameter. Add a validation test that a Boss node can only pick an encounter of tier Finalist.

---

### CD-2 · P1 · correctness — The act's live battle pool contains dev slices — a Battle/Elite/Boss node can roll three training dummies

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/BattlePresets/PresetBaseKit.asset:16` · линза `content-data-integrity`

```csharp
PresetBaseKit.asset:15-16 is `_id: battle_preset.base_kit` with `_encounter: {guid: 5c30ce46fe77f14448c5e9b6b0b6ea3f}` = Assets/_Project/ScriptableObjects/Encounters/DummyTrio.asset (`_id: encounter.dummy_trio`, three `enemy.training_dummy`). NodeResolver.PickBattlePreset (NodeResolver.cs:154-166) draws uniformly from `_content.All<BattlePresetData>()` with no filter other than IsElite, and NodeResolver.cs:82-90 keeps `preset.Encounter` untouched while swapping only the player roster for the guild. All 11 presets are registered in ContentDatabase.asset, and 8 of them (PresetAssassin, PresetCryomancer, PresetDefender, PresetMonk, PresetRanger, PresetShepherd, PresetSpearman, PresetBaseKit) are single-hero dev slices; nothing in BattlePresetData marks a preset as dev-only.
```

**Чем стреляет.** With 11 presets in the pool a real run has a ~1-in-11 chance per battle node of fighting encounter.dummy_trio, and the enemy-side variety is actually only 6 distinct encounters (GoblinWarband appears in 4 presets, GoblinScouts in 3) — while Encounters/DummyPair.asset and Encounters/DummyScatter.asset are referenced by nothing but the database.

**Куда править.** Give BattlePresetData an explicit act-pool flag (or move dev slices out of ContentDatabase into an editor-only list) and have PickBattlePreset filter on it; the eight single-hero presets exist for the SimBench/dev picker, not for the run loop.

---

### CD-3 · P1 · dead — The whole item/banner content axis is dead: three authored items, full combat plumbing, and no code path that can ever grant one

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunStateService.cs:263` · линза `content-data-integrity`

```csharp
RunStateService exposes TryAddVesselItem (line 263), RemoveVesselItem (line 275), VesselItems (line 256), MaxVesselItems (line 253), TryAddBanner (line 294), RemoveBanner (line 306), MaxPartyBanners (line 287) — a project-wide grep for these names outside RunStateService.cs returns zero callers. The only producer-shaped hook is EventEffectApplier.cs:59-62, `case EventEffectKind.GrantItem:` whose body is `Debug.Log($"[EventEffect] - (заглушка) выдать предмет '{e.ContentId}' — проводка в бой позже")` with a `// TODO(D1): проводка предмета в бой (RuntimeUnitFactory/party)` comment — but that plumbing already exists and works (GuildRoster.ResolveItems at GuildRoster.cs:52, RuntimeUnitFactory.cs:82-88 applying item.Mods, RuntimeUnitFactory.cs:117 RegisterItemPassives). ShopController sells only relics (ShopController.cs:119-121 rolls RelicData) and ChestFlow.cs:34 gives relics. ItemData.Cost, ItemData.ShopWeight, ItemData.ActiveAbility and ItemData.Charges (ItemData.cs:34-37) have zero readers project-wide, yet Items/item.oaken_charm.asset authors `_cost: 60`, item.swift_boots `_cost: 55`, item.war_banner `_cost: 90`.
```

**Чем стреляет.** A stub that says the wiring is missing sits directly on top of working wiring — the next reader will re-implement RuntimeUnitFactory item support that already ships. Meanwhile GameConfig.VesselItemSlots and PartyBannerSlots, three authored items with prices and shop weights, and the RunState.VesselItemIds/PartyItemIds save fields all cost maintenance and pay nothing.

**Куда править.** Either wire GrantItem to `_runStates.TryAddVesselItem` / `TryAddBanner` (a two-line change that turns 3 dead assets live) and stock items in the shop, or delete the item axis: the 3 assets, ItemData, the equip API, the RunState fields and the factory branches.

---

### CD-4 · P1 · dead — The Vessel ("pilot") layer is dead end to end — empty folder, VesselId hardcoded to empty, perk path unreachable

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunStateService.cs:68` · линза `content-data-integrity`

```csharp
Assets/_Project/ScriptableObjects/Vessels/ is an empty directory — zero VesselData assets exist. The only writer of RosterSlot.VesselId is RunStateService.cs:68, `VesselId = string.Empty,` in NewRunWithDefaultGuild; nothing else assigns it (grep VesselId returns only that write plus two readers). GuildRoster.cs:44 therefore always skips: `if (!string.IsNullOrEmpty(rs.VesselId)) content.TryGet(rs.VesselId, out vessel);`. Consequently RuntimeUnitFactory.cs:77-78 `if (vessel?.PerkModifiers != null && vessel.PerkModifiers.Length > 0) stats.AddModifiersFrom(vessel, vessel.PerkModifiers);` can never execute in game, and VesselData.PerkModifiers (VesselData.cs:20, tagged "Плейсхолдер перков… Фаза 2/4") has no other consumer. VesselData still occupies a domain (ContentDomains.cs:21), a folder (ContentPaths.cs:21), a field on PlayerSlot (BattlePresetData.cs:25), a field on RuntimeUnit (RuntimeUnit.cs:120), the OpenLoadoutRequest message (LoadoutMessages.cs:19) and two UI fallbacks (LoadoutHubView.cs:82, UiPreviewCatalog.cs:148).
```

**Чем стреляет.** A stat-cascade layer that looks wired — it appears in the factory between class/species scalers and item mods — is unreachable, so anyone reading the cascade believes vessel perks are live. It is also the reason ClassBaselineTests.cs:120 can only exercise it with a hand-built ScriptableObject.CreateInstance<VesselData>().

**Куда править.** Either author the first VesselData asset and give RunStateService a real VesselId, or strip the layer to a single seam: drop the `vessel` domain, the empty folder, PlayerSlot._vessel and the factory branch, and keep the perk idea in the GDD until Phase 2/4 actually lands.

---

### CD-5 · P1 · truth — Two registries own "content type → where it lives", and the Content Hub create menu offers exactly the dead types while hiding the live ones

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Editor/ContentPaths.cs:17` · линза `content-data-integrity`

```csharp
ContentDomains.Domains (ContentDomains.cs:16-35) registers 17 concrete content types. ContentPaths.Folders (ContentPaths.cs:17-32) registers only 13 — SpeciesData, EncounterData, BattlePresetData and TextEventData are missing, even though those four have live authored assets in ScriptableObjects/Species, /Encounters, /BattlePresets, /Events. ContentPaths.FolderFor falls through to `return $"{Root}/Misc";` (line 42), and ContentCrudService.Create uses exactly that path (ContentCrudService.cs:24-27) — there is no Misc folder in the project, so EnsureFolder would create one. ContentHubWindow.Browser.cs:337 builds the whole create menu from `foreach (Type type in ContentPaths.CreatableTypes)` = Folders.Keys, so the menu offers VesselData, TraitData, ConsequenceData, GuildmasterData and RunModifierData (zero assets, zero consumers each) and cannot create an Encounter, a BattlePreset, a TextEvent or a Species.
```

**Чем стреляет.** The one fact "which folder does content type X live in" has two owners that already disagree. A designer using the Content Hub literally cannot author the four types the run loop consumes, and if the tables are ever reconciled by adding the missing types, new assets land in a Misc folder that the existing ones are not in — splitting each domain across two directories.

**Куда править.** Derive the folder from the domain (one table): keep ContentDomains as the single owner and make ContentPaths a pure function domain → folder name, or at minimum add the four missing types and replace the silent Misc fallback with a throw, mirroring ContentDomains.GetDomain.

---

### LT-1 · P1 · truth — The game boots into the EN locale by default, where 132 of 212 content keys are blank and 19 more are literally «—»

`Assets/_Project/Localization/LocalizationSettings.asset:44` · линза `localization-text`

```csharp
Startup selector chain (m_StartupSelectors, lines 15-18 → RefIds): CommandLineLocaleSelector(-language=), SystemLocaleSelector, then `SpecificLocaleSelector … m_LocaleId: m_Code: en` (lines 44-47); `m_ProjectLocaleIdentifier: m_Code: en` (lines 27-28). Both Locale assets exist (Locales/en.asset m_Code: en, Locales/ru.asset m_Code: ru). Nothing in production ever calls ILocalizationService.SetLocale — the only references are Tests/EditMode/Content/LocalizationServiceTests.cs:26,30 and SmartStatStringTests.cs:42,67; the settings screen has three tabs (Игра/Графика/Звук, SettingsScreen.uxml:9-11) and no language option. Content_en.asset holds 80 entries for 212 shared keys: 19 of them are "—" and 10 are '-'; domains kw (66 keys), tag (54), item (6), species (2) have NO en entry at all. LocalizationService.cs:66 `if (res.Entry == null) return string.Empty;`
```

**Чем стреляет.** On any machine whose OS language is not Russian, SystemLocaleSelector resolves to the available `en` locale (or the Specific selector forces `en`), and there is no way for the player to switch back. LoadoutViewModel.cs:81 `Name(RelicData r) => _loc.GetString(r.Id + ".name")` has no fallback, so relic cards in the inventory/reward/shop render an empty label wherever the en cell is missing; tag chips fall back to the raw id (LoadoutInventoryView.cs:478-479 strips "tag." → "anti_summon"); keyword markup renders the id (KeywordMarkup.Word: "Пусто — показываем id") → «[Kw.burn]» inside descriptions; and every UI label falls through to its hardcoded Russian C# literal. The result is a half-Russian, half-blank interface on the default locale, not a fallback to a complete language.

**Куда править.** Make `ru` the project locale and the SpecificLocaleSelector target until EN is actually authored, or drop the `en` Locale asset from AvailableLocales so SystemLocaleSelector cannot land on it. Add an EditMode guard asserting that the startup-selected locale has a non-empty value for every shared key.

---

### LT-13 · P1 · dead — The player can never choose a language: the whole locale-switching half of ILocalizationService has zero production callers

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:81` · линза `localization-text`

```csharp
LocalizationService.cs:81 `public void SetLocale(string localeCode)`. Repo-wide callers of SetLocale / AvailableLocales / CurrentLocale outside the service itself: only Tests (LocalizationServiceTests.cs:26/30, SmartStatStringTests.cs:42/67, DescriptionTests.cs:33 fake). MenuRouter.BuildSettingsScreen (MenuRouter.cs:452-509) builds exactly three sliders, three toggles and three tabs (`tab-game`/`tab-video`/`tab-audio`, WireSettingsTabs:513) — no language row; grep -i for "язык|language" across Assets/_Project/UI, Scripts/UI, Core/Settings and SettingsService.cs returns only USS/comment prose. LocalizationSettings.asset:35-43 wires CommandLineLocaleSelector → SystemLocaleSelector → SpecificLocaleSelector(en), so the boot locale is whatever the OS culture says. UiRootBootstrap.cs:246 `if (_loc != null) _loc.LocaleChanged += RebuildTopBar;` with the comment «смена языка на лету перестраивает персистентный топбар» — plumbing for an event nothing in the game can raise.
```

**Чем стреляет.** Supersedes R1's «boots into the EN locale by default» at its root: the boot locale is not a fixed EN default, it is decided by the player's OS — the same build shows Russian on a Russian Windows and the 132-key-blank English on everything else — and in NEITHER case is there any in-game control to change it. A Russian player on an English OS has no recourse; a QA tester cannot reproduce the other locale without editing the asset. SetLocale, AvailableLocales, CurrentLocale and the LocaleChanged→RebuildTopBar seam are dead code that looks fully wired.

**Куда править.** Either add a language row to the Settings «Игра» tab bound to SetLocale/AvailableLocales and persist the choice in GameplaySettings, or delete SetLocale/AvailableLocales/CurrentLocale from ILocalizationService and the LocaleChanged subscription in UiRootBootstrap.cs:246/639 and pin the locale in the asset. Do not leave the half-seam.

---

### LT-14 · P1 · gap — No fallback locale is configured, so every blank EN entry renders as an empty string instead of falling back to RU

`Assets/_Project/Localization/LocalizationSettings.asset:61` · линза `localization-text`

```csharp
LocalizationSettings.asset:61 `m_UseFallback: 0` (LocalizedAssetDatabase) and :72 `m_UseFallback: 0` (LocalizedStringDatabase). Both Locale assets carry no fallback metadata: Locales/en.asset:17-18 `m_Metadata:\n    m_Items: []`, same for ru.asset. Measured from the table YAML: Content_en has 80 non-empty rows against 212 shared keys; UI_en has 9 against 41. LocalizationService.cs:66 `if (res.Entry == null) return string.Empty;` — and an entry that EXISTS with an empty value returns "" from GetLocalizedString() as well.
```

**Чем стреляет.** This is the root under R1's «132 of 212 content keys are blank in EN». Unity Localization can serve the RU string for a missing EN entry, but that requires m_UseFallback: 1 AND a FallbackLocale metadata item on the en Locale — neither exists. So a player booting EN gets a blank name for every relic, enemy, keyword and item rather than a Russian one, and the 63 C# RU-fallback literals only cover the 12 UI files that bothered with them; DescriptionService.Name (Descriptions/DescriptionService.cs:36-40) has no fallback at all and returns string.Empty.

**Куда править.** Set m_UseFallback: 1 on the LocalizedStringDatabase and add a FallbackLocale metadata item pointing at ru on the en Locale asset (Locales/en.asset:17). This is the one-line change that makes the 132 blank EN rows degrade to readable Russian instead of nothing while translation is pending.

---

### LT-15 · P1 · correctness — Every fighter's name label in battle prints the ScriptableObject file name, bypassing localization entirely

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:442` · линза `localization-text`

```csharp
CombatPresenter.cs:440-444 `private static string NameFor(RuntimeUnit unit) { if (unit.Unit != null) return unit.Unit.name; return (unit.Team == 0 ? "Ally " : "Enemy ") + unit.Id; }` — `unit.Unit` is a UnitData (RuntimeUnit.cs:117), so `.name` is UnityEngine.Object.name, i.e. the .asset filename. Called at CombatPresenter.cs:254 `view.SetLabel(NameFor(unit));` on every spawned view; UnitView.cs:213-216 writes it straight to `_nameLabel.text`, and `_nameLabel` IS wired in the shipped prefab (Prefabs/UnitView.prefab:162 → NameLabel.prefab, m_IsActive: 1). Meanwhile Relics/Defender.asset:15 `_id: relic.defender` and Content Shared Data.asset holds relic.defender.name with a filled RU value.
```

**Чем стреляет.** Above every unit's HP bar the game shows «Defender», «FlameSwordsman», «GoblinArcher», «WhirlMonk» — Latin CamelCase asset filenames — in a game whose only filled locale is Russian. It is not merely untranslated: it can never be translated, because the string is not a key and renaming the asset silently renames the in-game label. This is the single most visible player-facing string in the game (shown for 6-10 units simultaneously, the whole battle) and it is invisible to every loc guard test, since no key is involved.

**Куда править.** Inject IDescriptionService (or ILocalizationService) into CombatPresenter and make NameFor return `_descriptions.Name(unit.Unit)` / `_loc.GetString(ContentKeys.NameKey(unit.Unit))`, keeping the «Ally N/Enemy N» branch only for dummies with no UnitData. Add a guard test that no production code reads `.name` on a ContentDefinition.

---

### LT-16 · P1 · correctness — The node-farewell card renders with a blank title and blank body after every shop, chest and camp node

`Assets/_Project/Scripts/UI/MenuRouter.cs:691` · линза `localization-text`

```csharp
MenuRouter.cs:691-692 `if (title != null) title.text = _loc?.GetString(req.TitleKey) ?? string.Empty; if (body != null) body.text = _loc?.GetString(req.BodyKey) ?? string.Empty;`. The keys come from ShopFlow.cs:37 / ChestFlow.cs:36 / CampFlow.cs:36 as "ui.node.shop.title"/"ui.node.shop.farewell" etc. Those six keys exist ONLY in the UI table (UI Shared Data.asset, all six filled in UI_ru) while `_loc.GetString(key)` is the one-arg overload that resolves against Content (LocalizationService.cs:48 `GetString(ContentTable, key)`), and Content Shared Data contains no ui.node.* row. LocalizationService.cs:66 returns string.Empty (not null) for a missing entry, so `?? string.Empty` never fires — the assignment overwrites the UXML default. EventScreen.uxml:4 ships `<ui:Label name="event-title" text="Событие" .../>` and event-body with text="".
```

**Чем стреляет.** Three of the five node types end on a full-screen panel with no title and no text — the «Событие» placeholder from the UXML is actively erased. This is distinct from the table-split problem I already reported: MenuRouter's own Label() helper four lines below (MenuRouter.cs:755-759 `if (!string.IsNullOrEmpty(text)) button.text = text;`) deliberately KEEPS the markup default when the key misses, and every other screen builder has an RU code fallback. Only this one screen was written to clobber unconditionally, so it fails hardest of all of them.

**Куда править.** Use the same guard as Label(): only assign when the resolved string is non-empty, and resolve node keys through the UI table (`_loc.GetString("UI", key)`) or move the six ui.node.* rows into Content. A blank card must never be a reachable state.

---

### LT-2 · P1 · architecture — ui.* strings are split across two String Tables while every screen resolves against Content — 21 of 41 UI-table rows are unreachable, and 5 filled Content rows are read by nobody

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:18` · линза `localization-text`

```csharp
`private const string ContentTable = "Content";` … `public string GetString(string key) => GetString(ContentTable, key);` (lines 18, 48). Every screen gets exactly that overload: MenuRouter.cs:114, 217, 276, 443, 460, 619, 652, 680-681, 747, 766, 784, 802, 821, 838, 871 all pass `key => _loc?.GetString(key)`. Only two call sites pass a table: DescriptionService.cs:99-101 (`UiTable`) and TooltipContentFactory.cs:190. So the 41 rows of `UI Shared Data.asset` split into 19 reachable (ui.kit.* 3, ui.kw.category.* 5, ui.tag.category.* 4, ui.stat.*.desc 7 — all read with the "UI" argument), 1 test-only (ui.dev.stat_probe, referenced only from Tests/EditMode/Content/SmartStatStringTests.cs:21) and 21 UNREACHABLE: ui.camp.* (8, consumed at CampScreenView.cs:41-42,67,98-102), ui.beat.continue/formation (2, ContinuePresenter.cs:32-33 → MenuRouter.cs:747), ui.event.result.fallback (1, EventScreenView.cs:61), ui.node.{shop,chest,camp}.{title,farewell} (6, ShopFlow.cs:37 / ChestFlow.cs:36 / CampFlow.cs:36 → MenuRouter.cs:680-681), ui.loadout.slot.ability.empty + slot.upgrade.empty + tags.more (3, LoadoutInventoryView.cs:95-96,451), ui.settings.tooltip_details (1, MenuRouter.cs:467). Conversely 21 ui.* rows were duplicated into `Content Shared Data.asset` (ui.hub.*, ui.reward.*, ui.run.*, ui.mainmenu.title, ui.titlecard.*), and 5 of those are referenced by no code or markup at all: ui.run.gold, ui.run.hub, ui.run.settings, ui.event.continue, ui.reward.continue.
```

**Чем стреляет.** Two owners for one fact — 'which table holds a UI string'. Every RU line a translator types into the 21 unreachable rows is dead on arrival: the camp screen ships its C# literals ("Привал", "Действий осталось: {0} из {1}", "Нанять сосуда (или заменить старого)") even though byte-identical RU text sits in UI_ru.asset, and the moment the two copies diverge the table copy silently loses. The 21 duplicated rows in Content are the empirical proof of the confusion — whoever wired the working screens had to re-add them to the other table. Nothing detects either half: SmartStringFlagTests only walks rows that exist.

**Куда править.** Pick one table for UI strings (the `UI` table, given the name) and route the screens through an `ILocalizationService.GetUiString(key)` that passes it; move the 16 live ui.* rows out of Content, delete the 5 orphans and the test-only probe row. Then add an EditMode test that every `"ui.*"` literal in Guildmaster.UI resolves in that one table.

---

### LT-3 · P1 · convention — 51 of the 100 ui.* keys the code asks for exist in no String Table — the Russian text of half the interface lives only as C# fallback literals

`Assets/_Project/Scripts/UI/MenuRouter.cs:441` · линза `localization-text`

```csharp
The project-wide pattern is `private string Loc(string key, string ru) { string v = _loc?.GetString(key); return string.IsNullOrEmpty(v) ? ru : v; }` (MenuRouter.cs:441-445), duplicated verbatim as a local `L(key, fallback)` in ShopScreenView.cs:24-28, RewardScreenView.cs:39-42, CampScreenView.cs:26-30, ChestScreenView.cs, OutcomeScreenView.cs, MainMenuScreenView.cs:21-25, TitleCardScreenView.cs:21-25, EventScreenView.cs:29-33, LoadoutHubView.cs, LoadoutInventoryView.cs, RunModeBarView.cs:132-136. I extracted 75 (key, RU-literal) pairs from those call sites. Of the 100 distinct `"ui.*"` keys referenced in C#: 16 resolve from the Content table, 12 from the UI table via the tooltip factory, 21 point at UI-table rows the default Content lookup can never reach, and 51 exist in NEITHER table — every ui.shop.* (8: ShopScreenView.cs:42,43,55,83,87,112,124,127), every ui.outcome.* (5: OutcomeScreenView.cs:30,34,35,36), every ui.mainmenu.* button (4: MainMenuScreenView.cs:39-47), every ui.mode.* (5: RunModeBarView.cs:48-57), ui.menu.to_main_menu/quit (MenuRouter.cs:428,434), ui.settings.card_anim/card_attack (MenuRouter.cs:464-465), 10 ui.loadout.* (LoadoutInventoryView.cs:67-90), ui.chest.title/hint, ui.hub.hint, ui.run.floor, the 7 ui.stat.* labels (UnitStatPreview.cs:50-57) and the 3 ui.unit.* unit suffixes (DescriptionService.cs:19-21, fallback `UnitLabels.Ru => new UnitLabels("%", "с", "/с")`, StatFormat.cs:25).
```

**Чем стреляет.** Supersedes R1-18 at its root: the volume labels are not an oversight, they are the majority case. The HARD rule is 'all player text through localization keys, RU filled' — here the key is present but the STRING is in the assembly, so a translator handed the two tables gets 49 of 100 UI strings and cannot even see the other 51. Nothing fails: the fallback makes a missing row invisible in RU, so the debt is unmeasurable from inside the game and unbounded in growth (three RewardScreenView/OutcomeScreenView strings were added this way as recently as the current tree).

**Куда править.** Generate the missing 51 rows from the (key, fallback) pairs once — the literals ARE the RU column — then make the fallback a loud one in the editor (log or magenta «#key») so a missing row is visible during play, and add an EditMode test that every `"ui.*"` literal in the UI assembly has a row.

---

### R1-01 · P1 · correctness — AttackSpeed clamp from StatsConfig is never applied — shipped content already breaks the ceiling

`Assets/_Project/Scripts/Combat/Stats/Stats.cs:186` · линза `combat-sim`

**Проверка скептиком:** ОПРОВЕРГНУТО → `P3` (уверенность high)

```csharp
private static float Compose(float baseVal, float flat, float percentAdd, float multAccum)
    => (baseVal + flat) * (1f + percentAdd) * multAccum;
```

**Чем стреляет.** Stats.Get's own contract (line 30: «Итоговое значение стата после всех модификаторов и клампов») and StatType.cs:26 («AttackSpeed … клампится из StatsConfig») promise a clamp that does not exist: Compose/RebuildCache never touch StatsConfig._attackSpeedMin/_attackSpeedMax (0.1 / 2.5), and a grep over the whole codebase shows the only readers of AttackSpeedMin/Max are Balance/Editor/ContentAuditor.cs:43-44. Shipped content already exceeds it: FlameSwordsman base AttackSpeed Override 1.2, BlazingBladesRamp (permanent, StackRule.Stack, MaxStacks 20, AttackSpeed PercentAdd +0.05 → +100%) and PyreRush (AttackSpeed PercentAdd +1.0) are all PercentAdd, so a ramped swordsman reaches 1.2×(1+1.0+1.0)=3.6 atk/s — 44% over the documented cap, i.e. IntervalTicks 8 instead of 12. The missing floor is worse in kind: AttackTiming.IntervalTicks(attackSpeed<=0) returns int.MaxValue, and EnterWindup writes that straight into unit.AttackCooldownTicks, so any future ≥−100% AttackSpeed debuff would disable the unit's auto-attack permanently, long after the debuff expires.

**Куда править.** Clamp in Stats.RebuildCache after Compose (per-stat clamp table from StatsConfig), so both the sim and Explain/tooltips see the same clamped value; at minimum clamp AttackSpeed to [AttackSpeedMin, AttackSpeedMax] there rather than in an editor auditor.

---

### R1-02 · P1 · correctness — Equipping a relic in the test-zone/interlude sandbox silently does nothing (Load bails on null encounter)

`Assets/_Project/Scripts/Combat/Units/EncounterLoader.cs:80` · линза `combat-sim`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P1` (уверенность high)

```csharp
if (encounter == null)
{
    Debug.LogWarning("[EncounterLoader] - Load: encounter == null");
    return;
}
```

**Чем стреляет.** DeploymentController.EnterSandbox sets `_encounter = null;     // без врагов — полигон` (line 262), and DeploymentController.RebuildPreview (line 564-571) is the only respawn path after a kit change: `_loader.Load(_encounter, side); // ResetBattle + enqueue`. In the sandbox that call hits the guard above and returns before ResetBattle/EnqueueUnitSpawn, so EquipOn (line 552-560) writes the new relic into the slot and into the guild save (SetSlotRelic) while the live RuntimeUnit on the arena keeps its old UnitData — kit, stats, passives and view all stale — and every equip spams a LogWarning. The battle-deployment path works only because `_encounter` happens to be non-null there.

**Куда править.** Make the sandbox path explicit instead of piggybacking on Load: in RebuildPreview call `_simulation.ResetBattle(); _factory.ResetIds(); _loader.SpawnPlayerSide(side);` (those public seams already exist for persist-world) and only call Load when `_encounter != null`.

---

### R1-03 · P1 · gap — Per-source damage type is only half-threaded: PhysicalSubtype/MagicElement never reach DamageRequest

`Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs:38` · линза `combat-sim`

```csharp
/// <summary>Школа урона — определяет, какая броня используется (Physical/Magical/True).</summary>
public readonly DamageSchool School;
...
/// <summary>Сродство урона (Яд/Свет/Тьма). Бронёй не гасится …</summary>
public readonly DamageAffinity Affinity;
```

**Чем стреляет.** Data already models the full per-source type (Definitions/DamageType.cs: School + PhysicalSubtype + MagicElement + Affinity) and DamageCategories has Resolve overloads for subtype and element (CombatCategories.cs:138, :150). But the sim's damage seam carries only School+Affinity: AbilitySystem.cs:326-327 resolves `SchoolOverride` and `AffinityOverride` and drops the other two, and a grep for PhysicalSubtypeOverride/MagicElementOverride finds zero consumers outside Data/Definitions itself. Consequence today: the whole Pierce/Slash/Blunt and Fire/Ice/Lightning axis exists only as UI chips (UnitTagResolver → LoadoutInventoryView), so DamageResult (which presentation uses for hit-flash by School) cannot distinguish a Fire nuke from an Ice one, and any rule keyed on subtype/element (armor interactions, elemental resist, on-hit conversions) has no seam to hook — every future call site would need a signature change through DamageRequest, DamagePipeline, DamageResult, OnDamageDealt and all ~12 producers.

**Куда править.** Replace `DamageSchool School` in DamageRequest/DamageResult with the existing `DamageType` struct and have each producer build it (AbilitySystem via all four DamageCategories.Resolve overloads, AutoAttackSystem from ResolveAutoAttackDamageType, the components from their already-present DamageType properties). DamagePipeline keeps branching on `.School`, so behaviour is unchanged while the seam becomes real.

---

### R1-04 · P1 · correctness — sys.airborne hard-CC leaks permanently when DisplacementSystem drops the request

`Assets/_Project/Scripts/Combat/CombatSimulation.cs:448` · линза `combat-sim`

**Проверка скептиком:** ОПРОВЕРГНУТО → `not-a-bug` (уверенность high)

```csharp
if (req.Target != null && !req.Target.IsDead)
    _effectSystem.Apply(req.Target, _airborneEffect, req.Source, this);
_displacementSystem.Add(in req);
```

**Чем стреляет.** `_airborneEffect` is built with `baseDuration: -1` (permanent) and `unremovable: true` plus ControlComponent(preventAct, preventMove, preventCast) (CombatSimulation.cs:58-65), and its ONLY removal path is the ctor hook `_displacementSystem.OnDisplacementEnded += … RemoveByTag(target, KnockUp)` (line 182-186). Displace applies the marker unconditionally, then Add re-validates with a *wider* guard set and can silently discard the flight: `if (target == null || target.IsDead || req.Ticks <= 0) return;` (DisplacementSystem.cs:53). So any DisplaceRequest with Ticks ≤ 0 — e.g. an AbilityData authored with DisplaceTicks 0, which nothing validates — leaves the target stunned, rooted and silenced for the whole battle, undispellable (Unremovable is honoured by MatchesDispel) and invisible to any log. The dead-target branch in Tick (line 84-90) has the same asymmetry: it removes the Active and deliberately skips OnDisplacementEnded, so the marker also survives on a unit that dies mid-flight.

**Куда править.** Make Displace and Add share one validation: check `req.Ticks > 0` (and target liveness) once in CombatSimulation.Displace before applying the marker, or have Add return bool and roll the marker back with RemoveByTag when it refuses the request.

---

### R1-14 · P1 · architecture — Push ignores ScreenKind, so a Sheet can land above the pause Modal: input un-suppresses and ESC pops the wrong screen

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:226` · линза `ui-coordination`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
UiScreen top = Top;
bool modal = top != null && top.Kind != ScreenKind.Sheet;
_input.GameplaySuppressed = modal;
```

**Чем стреляет.** `Push` appends unconditionally (`_stack.Add(screen)`, line 124) while `LayerFor` assigns z by Kind — so stack order and visual order can disagree. WorldMapController.BeginChoose publishes WorldMapSpaceChangedEvent(true) from the run loop, independent of the UI stack (WorldMapController.cs:102), and UiRootBootstrap turns that into `_router.ShowMapSpace()`. If the pause Modal is open at that moment the map Sheet becomes Top: GameplaySuppressed flips to false and context becomes InputContext.Map, so camera/gameplay input is live under the visible pause menu (Modals are never hidden by SyncVisibility). Worse, MenuRouter.ToggleSystemMenu then does `_nav.Pop()` (line 386), which removes the TOP — the map Sheet, not the pause screen — nulling `_mapSpaceScreen` via OnExit while WorldMapController._visible stays true; since SetVisible is idempotent (`if (_visible == visible) return;`), the map mode tag and its input context can never be restored for the rest of the run. UiNavigatorTests covers Modal-over-Sheet but not Sheet-over-Modal.

**Куда править.** Make Push insert by Kind (Sheets/Pages below the lowest Modal) or compute suppression from the highest-priority Kind in the stack rather than from Top alone; and have ToggleSystemMenu remove the pause screen by reference (`_nav.Remove(pauseScreen)`) instead of `Pop()`.

---

### R1-15 · P1 · correctness — Rest-beat «Продолжить» screen is a fullscreen pickable Modal — it blocks the topbar and kills camera input for the whole Interlude

`Assets/_Project/Scripts/UI/MenuRouter.cs:708` · линза `ui-coordination`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P1` (уверенность high)

```csharp
var screen = new RouterResultScreen<bool>(ScreenKind.Modal, false, resolve =>
{
    var body = FillRoot(_continueUxml.CloneTree());
```

**Чем стреляет.** `FillRoot` stretches the TemplateContainer to 0/0/0/0 with default PickingMode.Position (ContinueScreen.uxml sets no picking-mode, and `.gm-continue-screen` is itself absolute fullscreen in components.uss:1115-1121), and Kind=Modal puts it in _layerModal — above _layerTopbar. So during the entire rest beat every pick hits it: the mode tabs are unreachable, contradicting IContinuePresenter.ShowRestBeat's own contract («Оба места достижимы и табами — кнопки лишь короткий путь»), and IInputService.PointerOverUI (panel.Pick != null) is true everywhere, which WorldMapView gates on (WorldMapView.cs:593,726). Additionally Kind != Sheet sets GameplaySuppressed = true, and InputService.CameraPan/CameraZoomDelta return zero when suppressed — so pan/zoom is dead through the whole Interlude, exactly the phase WorldContextOf maps to InputContext.Combat «мир на экране, камера должна жить».

**Куда править.** Make the rest beat a Sheet (transparent, non-suppressing) and set picking-mode="Ignore" on the ContinueScreen root so only the buttons pick — same pattern as BuildTestZoneSpace/BuildMapSpace.

---

### R1-16 · P1 · correctness — Main-menu panel is hidden with an inline display that SyncVisibility overwrites on the very next Push

`Assets/_Project/Scripts/UI/MenuRouter.cs:861` · линза `ui-coordination`

**Проверка скептиком:** ОПРОВЕРГНУТО → `not-a-bug` (уверенность high)

```csharp
VisualElement menuPanel = screen?.Root;
if (menuPanel != null) menuPanel.style.display = DisplayStyle.None;
PushScreen(BuildSettingsScreen, ScreenKind.Modal, scrimless: true,
    onExit: () => { if (menuPanel != null) menuPanel.style.display = DisplayStyle.Flex; });
```

**Чем стреляет.** `style.display` is owned by UiNavigator.SyncVisibility, which runs inside that very Push: iterating top-down it computes `hidden = pageAbove || (sheetAbove && s.Kind == ScreenKind.Page)` — a Modal sets neither flag, so the main-menu Page gets `display = Flex` again immediately (UiNavigator.cs:298-300). Result: the settings panel is drawn on top of a still-visible main menu, and because SuppressScrim=true there is no scrim to hide it either — the exact bug class the comment in UiScreen.cs:29-33 documents for `gm-screen--scrimless`, repeated with `display`.

**Куда править.** Express this as a screen property the navigator honours (e.g. a `HidesBelow`/`ReplacesPanel` flag consumed by SyncVisibility) instead of writing `style.display` from the router.

---

### R1-17 · P1 · correctness — Reward drop-list resolves relic names from the offered choices, so the player sees raw content ids

`Assets/_Project/Scripts/UI/RewardScreenView.cs:127` · линза `ui-coordination`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
string id = currentInventory[i];
string label = nameOf != null ? Coalesce(nameOf(FindById(choices, id)), id) : id;
```

**Чем стреляет.** `currentInventory` holds the ids of relics already in the stash, but `FindById` searches `choices` — the 3 relics being offered. Those sets are disjoint in practice, so FindById returns null, `LoadoutViewModel.Name(null)` returns string.Empty (LoadoutViewModel.cs:81), Coalesce falls through to `id`, and the «выбери, что сбросить» list renders raw ids like `relic.flame_swordsman`. This is the only screen where the player must identify their own relics, and it is also unlocalized text reaching the UI.

**Куда править.** Pass a name resolver keyed by id (or the stash RelicData list) into Build and look the id up in the content database instead of in `choices`.

---

### R1-24 · P1 · correctness — Inter-tick interpolation alpha is a frame-rate ratio, not the tick phase — units render at a fixed offset and never interpolate

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:181` · линза `presentation`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
float alpha = Time.deltaTime / Guildmaster.Core.Simulation.SimConstants.TickDelta;
alpha = UnityEngine.Mathf.Clamp01(alpha);

foreach (var kvp in _views)
{
    kvp.Value.UpdateInterpolation(alpha);
}
```

**Чем стреляет.** alpha must be the sim accumulator phase (0..1 inside the current tick), but here it is frameTime/tickTime, which is constant for a stable frame rate. At 60 fps alpha == 0.5 every frame, at 144 fps == 0.21, at 30 fps == 1.0. Since MovementSystem.cs:44 snapshots PreviousPosition = Position once per tick, both endpoints are constant between ticks, so transform.position is constant for the whole inter-tick interval: motion updates at 30 Hz with a fixed half-tick lag instead of being smoothed to render rate. The same broken alpha feeds ProjectileView.Tick (CombatPresenter.cs:195), so bullets step too. UnitView's own doc comment ("Интерполирует позицию между тиками (сим 30 Hz, рендер 60+ fps)") describes behaviour the code does not produce.

**Куда править.** Expose the tick phase from CombatLoopService (it already owns _accumulator: `public float TickPhase => _accumulator / SimConstants.TickDelta;`), inject it into CombatPresenter and pass that as alpha. Do not derive alpha from Time.deltaTime.

---

### R1-25 · P1 · correctness — Every LitMotion feel tween runs on scaled time while the config and comments promise unscaled — hit feel freezes during the finisher pause and stretches 10x in slowmo

`Assets/_Project/Scripts/Presentation/UnitView.cs:794` · линза `presentation`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
_nudgeHandle = LMotion.Create(0f, 1f, dur)
    .WithEase(Ease.Linear)
    .Bind(this, static (v, self) =>
    {
        float w = v < 0.5f ? v * 2f : (1f - v) * 2f;
        self._nudgeOffset = self._nudgePeak * w;
```

**Чем стреляет.** LitMotion's MotionScheduler.DefaultScheduler is `Update` (MotionTimeKind.Time — scaled); the project never calls WithScheduler anywhere (grep over Assets returns zero hits), so all six tweens in UnitView (flash 751, squash 776, nudge 794, attack offset 821, flip 655, acquire 906) advance on Time.timeScale. But CombatFeelConfig.cs:52/83/87/58 label these durations "(unscaled)" and TimeScaleService drives timeScale to 0 for `_finisherPause = 1f` and to `_finisherShatterFactor = 0.1f` for 3 s. Concretely: on a finishing blow the 0.25 s hit flash takes 2.5 s of wall clock and the squash/nudge arcs freeze mid-flight for a full second; a facing flip interrupted by the pause leaves `_flipAnimActive = true` (line 610) so ApplyFacing refuses to turn the unit until the pause ends. The unscaled parts of the same feature (hitstop 367, HoldHitFrame 567, idle breath 850) prove the intended time base.

**Куда править.** Add `.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)` to the six LMotion.Create calls in UnitView (and DeathShatter's Update, which uses Time.deltaTime deliberately — decide per effect), or set MotionScheduler.DefaultScheduler once at boot if unscaled is the project-wide default.

---

### R1-36 · P1 · correctness — "Начать" in between-nodes formation mode starts a phantom enemy-less battle (un-pauses sim, flips phase to Fighting) while the act loop is still waiting for a node choice

`Assets/_Project/Scripts/Game/DeploymentController.cs:147` · линза `di-lifecycle`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P1` (уверенность high)

```csharp
_session.BindStart(() => { if (_deploying && !_testZone) StartCombat(); });
```

**Чем стреляет.** Three modes share `_deploying`, and only the gray test zone sets `_testZone`. "К построению" (RunBeatStage.cs:55 -> OnSetFormation -> EnterSandbox(grayZone:false)) leaves `_deploying=true, _testZone=false, _encounter=null` and sets Phase=Deployment (line 268). UiRootBootstrap.cs:376 then shows the Start button for Deployment (RunModeBarView.cs:124 displays btn-start when !fighting), and it is the only affordance on screen because ContinuePresenter's rest-beat buttons were removed when "К построению" was pressed. Clicking it -> BattleSession.RequestStart -> this delegate -> StartCombat() -> `_sim.SetPaused(false)` + SetPhase(Fighting) with no enemies spawned, in the middle of ActRunner's `_chooser.ChooseAsync` await. Sim ends instantly, BattleBootstrap.OnBattleEnded pushes Phase=Interlude and calls ReportOutcome into a stale/absent TCS. Same path is reachable via Enter (ReadyPressed, line 594).

**Куда править.** Guard on the actual discriminator instead of `!_testZone`: only start when a battle is staged, e.g. `if (_deploying && _encounter != null) StartCombat();`. Better, replace `_deploying`/`_testZone`/`_encounter==null` with one explicit mode enum (Battle / Formation / Sandbox) and bind Start only in Battle.

---

### R1-37 · P1 · correctness — Formation mode has no exit: SetFormationRequest(false) is never published by anyone, so `_deploying` stays true across node selection and non-battle nodes

`Assets/_Project/Scripts/Game/DeploymentController.cs:229` · линза `di-lifecycle`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
else if (_deploying && _testZone == false && _encounter == null)
            {
                ExitTestZone(); // вышли из построения тем же путём (арена и так боевая)
            }
```

**Чем стреляет.** The only publisher of SetFormationRequest in the whole tree is RunBeatStage.cs:55, and it publishes `new SetFormationRequest(true)`. The topbar tabs publish SetTestZoneRequest/SetWorldMapRequest only (UiRootBootstrap.cs:558 `RequestTestZone(false)`), and OnSetTestZone's exit branch is `if (_deploying && _testZone) ExitTestZone()` (line 212) — false in formation mode, so "Карта"/"Бой" cannot leave it. Consequence: after "К построению" the player picks the next node, ActRunner calls `_beat.EnterNode()` -> Phase=None, but `_deploying` is still true and `_view` still active — deployment zones, rings and unit dragging stay live on top of a shop/event/chest screen (Tick only early-returns on GameplaySuppressed/PointerOverUI), and `_slots[].LiveUnitId` goes stale after the next ResetToWorld respawn. State only recovers if the next node happens to be a battle (OnFreeDeployment).

**Куда править.** Publish SetFormationRequest(false) from the topbar tab handlers (GoToMap/GoToBattle) and/or have RunBeatStage/ActRunner force-exit deployment on EnterNode; make the exit branch key off the real mode rather than the `_testZone==false && _encounter==null` triple.

---

### R1-38 · P1 · correctness — Root IRngService is seeded from wall-clock time and never reseeded from RunState.Seed, so node payloads, reward showcases and shop stock are not reproducible from a save

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs:57` · линза `di-lifecycle`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);
...
        private static ulong GenerateRootSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks;
        }
```

**Чем стреляет.** This singleton is the `_rng` GameFlow puts into every RunContext (GameFlow.cs:191) and the one RewardService/ShopController/RandomEventFlow take. NodeResolver picks node content from it — `all[ctx.Rng.NextInt(0, all.Count)]` (NodeResolver.cs:144, PickBattlePreset:166) — and its own doc says "детерминировано через RunContext.Rng"; RewardService.cs:17 claims "тот же seed → та же витрина; для реплея/коопа". Only the map graph is deterministic (RunStateService.BeginAct seeds a local XorShiftRng from `Current.Seed + CurrentActIndex`). So: Continue from autosave, or "В меню" then re-enter the act, gives different battle presets / text events / reward choices / shop stock for the same node ids. `IRngService.Reseed` exists (Core/Random/IRngService.cs:42) and is called nowhere but the ctor. Under NGO this also diverges host vs client.

**Куда править.** Derive the run-scoped RNG from RunState.Seed: call `Reseed(Seed + streamSalt)` in RunStateService.NewRun/Load, or register a run-scoped IRngService that GameFlow seeds per act/per node instead of a session singleton on DateTime.UtcNow.Ticks.

---

### R1-48 · P1 · correctness — ContentAuditor bakes stats WITHOUT the class/species cascade — MaxHP and EHP columns are wrong for every unit

`Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:82` · линза `data-guild-balance`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
var stats = new Stats(config);
            if (data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);

            float maxHp = stats.Get(StatType.MaxHP);
```

**Чем стреляет.** The real path (RuntimeUnitFactory.Create:69-72 and UnitStatPreview.Build:64-65) calls ClassBaseline.Apply + EnemyScalers.Apply before the persona stat block; the auditor calls neither. Assassin.asset has no MaxHP/MoveSpeed entry in _stats (only Stat 3/7/8/9, all Op:3), so its HP comes purely from ClassBalanceConfig: 2000×0.75 = 1500 in game. The auditor instead falls back to StatsConfig.asset default `Stat: 0 / Value: 1200`. A Tank reads 1200 instead of 3000 (2.5× off), goblin species ×0.4 is ignored entirely, EHP_phys/EHP_elem derive from that MaxHP, and the z-score outlier flags are computed over the wrong column — so the flags fire on noise and miss real outliers. The docstring claims the opposite: "реюз боевого Stats + StatsConfig, НЕ свои формулы".

**Куда править.** In BuildRow, call ClassBaseline.Apply(stats, data, BalanceAssets.LoadClassBalanceConfig()) and EnemyScalers.Apply(stats, data) before AddModifiersFrom, or better: reuse UnitStatPreview.Build so there is exactly one cascade assembler.

---

### R1-49 · P1 · correctness — ActConfig.ToGenConfig() hands out the SO's own MapGenConfig and Validated() mutates it in place — clamped values get written back into the asset

`Assets/_Project/Scripts/Guild/ActConfig.cs:18` · линза `data-guild-balance`

```csharp
public MapGenConfig ToGenConfig() => (_map ?? new MapGenConfig()).Validated();
```

**Чем стреляет.** MapGenConfig.Validated() (MapGenConfig.cs:54-67) mutates `this`: `if (Columns < 3) Columns = 3; ... if (EdgeColumns * 2 > middle) EdgeColumns = middle / 2; Zones ??= Array.Empty<ZoneRule>();`. Since `_map` is a reference-type SerializeField, the object returned IS the ScriptableObject's serialized data. GameFlow.RunActAsync:179 calls `_actConfig.ToGenConfig()` on every act, so entering play with Columns=2 or EdgeColumns=6 permanently rewrites ActConfig.asset to the clamped values on the next asset save — the classic Unity "play-mode edit leaked into the asset" bug, and exactly the SO-vs-code-defaults drift this project has already been burned by. MapGenerator.Generate:26 then calls Validated() a second time on the same live object.

**Куда править.** Return a copy: either make Validated() non-mutating (return a new MapGenConfig) or deep-clone `_map` in ToGenConfig before validating.

---

### R1-50 · P1 · architecture — "relic.base" is hardcoded in five non-test files while only RunStateService honours GameConfig.StartingRelicId

`Assets/_Project/Scripts/Game/Flow/RewardService.cs:27` · линза `data-guild-balance`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
private const string BaseRelicId = "relic.base";
```

**Чем стреляет.** GameConfig.StartingRelicId (GameConfig.cs:72) is read in exactly one place — RunStateService.cs:57 and :173 (`string.IsNullOrEmpty(_config.StartingRelicId) ? "relic.base" : _config.StartingRelicId`). Everything else pins the literal: RewardService.cs:27 (excludes the base relic from reward shelves), GuildRoster.cs:16 (empty-kit fallback), LoadoutHubView.cs:36, RunState.cs:62 (`public string RelicId = "relic.base"`), UiPreviewCatalog.cs:91/150/153/177/260/337. Set StartingRelicId to "relic.dummy" in the asset and the run starts on relic.dummy while RewardService keeps hiding relic.base and starts offering relic.dummy as a reward, and the loadout hub stops recognising the equipped kit as "empty". No test asserts that StartingRelicId resolves to an existing RelicData either (ConfigValidationTests.GameConfig_ValuesInSaneRanges does not check it).

**Куда править.** Expose the base-relic id from one owner (a `BaseRelicId` property on RunStateService or a ContentIds constant fed by GameConfig) and inject it into RewardService/GuildRoster/the views; add a validation test that GameConfig.StartingRelicId resolves in the ContentDatabase.

---

### R1-60 · P1 · correctness — View interpolation alpha is computed from Time.deltaTime instead of the tick accumulator — units render in 30 Hz steps, not smoothly

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:181` · линза `cross-cutting`

```csharp
float alpha = Time.deltaTime / Guildmaster.Core.Simulation.SimConstants.TickDelta;
alpha = UnityEngine.Mathf.Clamp01(alpha);

foreach (var kvp in _views)
{
    kvp.Value.UpdateInterpolation(alpha);
}
```

**Чем стреляет.** UnitView.cs:255 does `_renderPosition = Vector2.Lerp(_unit.PreviousPosition, _unit.Position, alpha)`. TickDelta is a constant 1/30 (SimConstants.cs:14) and deltaTime is roughly constant within a frame-rate regime, so alpha is a CONSTANT (~0.5 at 60 fps, ~0.23 at 144 fps), not a rising 0→1 sub-tick fraction. Result: between two sim ticks the render position never changes, so the sprite teleports once per tick (visible 30 Hz stepping) and sits a fixed fraction of a tick behind the sim; frame-time jitter turns straight into position jitter. The whole PreviousPosition/interpolation mechanism is inert.

**Куда править.** Expose the sub-tick fraction from CombatLoopService (it already owns `_accumulator`, CombatLoopService.cs:51-71) as `Alpha => _accumulator / SimConstants.TickDelta` and inject/read it in CombatPresenter.Update instead of deriving alpha from Time.deltaTime.

---

### R1-61 · P1 · correctness — UiNavigator discards every CancellationTokenRegistration — the run-long CTS accumulates callbacks that retain each closed screen for the whole act

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:211` · линза `cross-cutting`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Push(screen);

            if (ct.CanBeCanceled)
                ct.Register(() => screen.ResolveDefaultIfPending());

            return tcs.Task;
```

**Чем стреляет.** The token handed in is `ctx.Cancellation` — the run-wide token from GameFlow's `_runCts`, alive for the entire act (GameFlow.cs:188-210); e.g. ShopFlow.cs:31 and CampFlow.cs:31 pass `ctx.Cancellation`. The returned registration is thrown away, so each screen shown (reward/shop/chest/camp/continue/farewell — several per node) leaves a permanent closure on that CTS capturing the UiScreen and its whole VisualElement tree. Over a 12-node act the CTS holds a growing list of dead screens; they are only freed when the run ends. The same discard exists in Push (line 138-139).

**Куда править.** Store the `CancellationTokenRegistration` returned by `ct.Register(...)` on the UiScreen and Dispose it in RemoveScreen/OnExit, so a screen's registration dies with the screen instead of with the run.

---

### R1-62 · P1 · gap — PlayMode integration test ticks the sim in an unbounded while loop with no tick cap and no yield — a stalemate regression hangs the test runner

`Assets/_Project/Tests/PlayMode/Battle/BattleIntegrationTest.cs:94` · линза `cross-cutting`

```csharp
while (sim.Outcome == BattleOutcome.Ongoing)
            {
                sim.Tick(SimConstants.TickDelta);
            }

            yield return null;
```

**Чем стреляет.** The sibling test one method above deliberately caps at `ticks < MaxTicks` (line 75) precisely because a battle may fail to resolve. This one does not, and never yields, so it runs on the main thread forever: any balance/AI change that produces a stalemate (two identical teams that can't finish each other, or a kiting/separation regression) freezes the Unity Editor or hangs CI instead of reporting a red test. This is the only PlayMode test in the project, so it is also the only thing gating the sim end-to-end.

**Куда править.** Reuse the MaxTicks bound and periodic `yield return null` from Battle_StartsAndEndsWithWinner, then Assert that the battle actually ended before checking survivors.

---

### R1-63 · P1 · architecture — The whole Guildmaster.Net assembly is unreferenced dead code, and its Steam bootstrap does not do what its name and summary claim

`Assets/_Project/Scripts/Net/FacepunchTransportBootstrap.cs:9` · линза `cross-cutting`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
/// Инициализирует Steam через Facepunch.Steamworks и Netcode for GameObjects.
    /// Устанавливает Facepunch Transport как транспорт NGO — Steam relay / NAT бесплатно.
...
            if (!SteamClient.IsValid)
            {
                try
                {
                    SteamClient.Init(_appId, false);
```

**Чем стреляет.** The class never touches NetworkManager or any NetworkTransport — it only calls SteamClient.Init/RunCallbacks/Shutdown, so nothing installs the transport. All three files under Scripts/Net (FacepunchTransportBootstrap, NetworkCommandRelay, _Parked/SimSyncProbe) have GUIDs that appear in zero .unity/.prefab/.asset files and are registered in no LifetimeScope, and NetworkCommandRelay's own remarks (NetworkCommandRelay.cs:15-19) state its broadcast-ClientRpc design is lockstep and contradicts the chosen host-authoritative model. Guildmaster.Game.asmdef nonetheless references Guildmaster.Net, dragging Unity.Netcode.Runtime + Facepunch into the main gameplay compile for zero live callers. Anyone reading this believes co-op has a transport seam; it has none.

**Куда править.** Either park Net alongside SimSyncProbe (drop the Guildmaster.Net reference from Guildmaster.Game.asmdef) or fix the summary to say it only boots SteamClient; do not leave a class whose name promises transport wiring it never performs.

---

### R1-64 · P1 · complexity — Co-op seams IReadyGate and IPlayerIntentSource are threaded through every flow but never called anywhere

`Assets/_Project/Scripts/Game/Flow/RunFlowSeams.cs:11` · линза `cross-cutting`

```csharp
public interface IReadyGate
    {
        UniTask WhenAllReady();
    }
...
    public interface IPlayerIntentSource
    {
        /// <summary>Мы ли авторитет исполнения (host). Соло — всегда true.</summary>
        bool IsLocalAuthority { get; }
    }
```

**Чем стреляет.** Grepping the whole tree: `WhenAllReady` appears only in its own declaration/implementation and in the three GameFlow lines that stuff it into RunContext (GameFlow.cs:107, 191, 233); `IsLocalAuthority` appears only at its declaration and SoloPlayerIntentSource. No flow (BattleFlow, ActRunner, RunBeatStage, NodeResolver, CampFlow, ShopFlow) ever awaits the gate or checks authority — `RunContext.ReadyGate`/`.Intents` are read by nothing (only mentioned in a doc comment at RunFlow.cs:30). So the seam gives a false sign that node transitions are gated on all players; when NGO lands, every transition still has to be found and gated by hand. Meanwhile the constructor, DI registrations and RunContext.ForNode all pay to carry them.

**Куда править.** Either put `await ctx.ReadyGate.WhenAllReady()` at the actual transition points now (node entry, battle start, reward confirm) so the seam is load-bearing, or delete both interfaces until the MP phase and stop threading them through RunContext.

---

### R1-73 · P1 · gap — AbilityData exposes three displacement knobs that no code reads — the live values sit on WhirlDashLandingComponent

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/AbilityData.cs:164` · линза `dead-and-bloat`

```csharp
public bool Displaces => _displaces;
        public float DisplaceDistance => _displaceDistance;
        public int DisplaceTicks => _displaceTicks;
        public float DisplaceDamageMult => _displaceDamageMult;
        public float DisplaceWidth => _displaceWidth;
```

**Чем стреляет.** Only Displaces and DisplaceTicks are ever read. The single displacement call site hardcodes the rest: AbilitySystem.cs:147-149 does `ctx.Displace(new DisplaceRequest(caster, caster, dashDir, dashDist, data.DisplaceTicks, cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 0f))` — dashDist is computed geometrically, not from DisplaceDistance. The knobs that actually drive knockback are private duplicates with the same names on WhirlDashLandingComponent.cs:21-27. A designer editing DisplaceWidth or DisplaceDamageMult on an ability SO (both carry Tooltips promising a damage 'ядро' and its line width) gets zero effect, and AbilityData.cs:105 still advertises 'Отталкивает цель (Knockback) на DisplaceDistance'.

**Куда править.** Delete _displaceDistance/_displaceDamageMult/_displaceWidth and their getters from AbilityData (the reactive component owns them), or make AbilitySystem.ApplyDisplace feed them into DisplaceRequest. Fix the Displaces tooltip either way.

---

### R1-74 · P1 · architecture — Stat cascade assembly copy-pasted into three live sites; StatMath.AttacksPerSecond is a verbatim duplicate of UnitStatPreview's

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/EditorTools/ContentHub/Core/StatMath.cs:19` · линза `dead-and-bloat`

```csharp
public static Stats BuildEffective(UnitData data, StatsConfig config, ClassBalanceConfig classConfig = null)
        {
            var stats = new Stats(config);
            ClassBaseline.Apply(stats, data, classConfig);
            EnemyScalers.Apply(stats, data);
            if (data != null && data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);
            return stats;
        }
```

**Чем стреляет.** UnitStatPreview.cs:61-69 (Build) is the same four statements, and RuntimeUnitFactory.cs:65-75 is the same prefix before vessel/item modifiers. AttacksPerSecond is duplicated too: StatMath.cs:31-36 and UnitStatPreview.cs:71-76 both do IntervalTicks -> guard -> SimConstants.TickRate / interval. Both files carry a docstring promising 'значения совпадают по построению', which is only true while three copies are kept in sync by hand — ContentAuditor.cs:82 is the proof it does not hold: the fourth copy already drifted. StatMath is in an editor assembly that can reference Guildmaster.Combat, so no asmdef wall forces the copy.

**Куда править.** Pick one owner: expose the cascade once on the runtime side (UnitStatPreview or a Stats factory helper) and have StatMath, ContentAuditor and RuntimeUnitFactory's prefix call it; keep AttacksPerSecond in exactly one place.

---

### R1-75 · P1 · complexity — ISceneLoader is threaded through GameFlow -> NodeResolver -> BattleFlow only to be ignored; its unload path is reachable only from a dead method

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/BattleFlow.cs:21` · линза `dead-and-bloat`

```csharp
private readonly ISceneLoader     _scenes;
...
            _scenes            = scenes;
...
            // Persist-мир: боевой скоуп уже жив (BattleScene загружена на буте и не выгружается). «Запуск боя»
            // = команда в живой sim (доспавн врагов + снятие паузы), а не загрузка сцены.
```

**Чем стреляет.** _scenes is assigned at line 34 and never read again in the file — persist-world replaced scene loading with _session.RequestLaunch. The dependency still costs: GameFlow.cs:108 and NodeResolver.cs:94 pass it (NodeResolver's field at lines 28/52 exists only to forward it), and BattleFlowTests.cs builds `new FakeScenes()` eight times (lines 32,45,59,72,86,88,99,111) purely to satisfy an unused parameter. On the same seam, SceneLoader.UnloadBattleAsync (SceneLoader.cs:49) and ISceneLoader.UnloadBattleAsync have exactly one caller in the repo — GameFlow.OnBattleEndedAsync (GameFlow.cs:90) — which itself has zero callers, so the whole 'unload the battle scene' branch is unreachable.

**Куда править.** Drop the ISceneLoader parameter from BattleFlow (and the forwarding field in NodeResolver plus FakeScenes in the tests). Delete GameFlow.OnBattleEndedAsync, and with it UnloadBattleAsync from ISceneLoader/SceneLoader unless the legacy BootAsync path still needs the pair.

---

### RL-14 · P1 · architecture — The save has two commit points: node loot autosaves immediately, node progress only after the flow returns — quit-to-menu mid-node farms it

`Assets/_Project/Scripts/Game/Services/ActRunner.cs:122` · линза `run-loop-integrity`

```csharp
ActRunner.cs:121-122 commits progress only after the node flow returns: `MapTraversal.Advance(map, node.Id); _runStates.Autosave();`. But eight other sites write the same save mid-node: ShopController.cs:86 (Buy), :97 (Reroll), :110 (Sell), RewardPresenter.cs:69 (relic taken), EventEffectApplier.cs:28 (event effects), DeploymentController.cs:704 (roster). MapTraversal.cs:52-57 `Advance` is documented as "единая точка мутации карты", but it is not the single commit point for the run.
```

**Чем стреляет.** MenuRouter.BuildPauseScreen:434 `toMenu.clicked += () => { Pop(); _runControl?.RequestReturnToMainMenu(); }` cancels _runCts, GameFlow.cs:139-143 catches OperationCanceledException and returns to the menu WITHOUT deleting the save. Concrete sequence: enter a Shop node, buy a relic (ShopController.cs:83-86 spends gold, adds the relic, writes the save), press ESC → «В главное меню», then «Продолжить». RunStateService.Load rehydrates the disk state, MapTraversal.AvailableNext still offers the same shop node (Cleared was never set), the shelf is rerolled from a fresh _shopSeed — and the player keeps the relic bought in the discarded attempt. Same for the second relic of an Elite (BattleNodeFlow.cs:61-62 loops rewardCount twice; the first RewardPresenter.Autosave lands before the second screen) and for TextEvent gold/relic grants.

**Куда править.** Move every mid-node Autosave behind a node-scoped journal: mutate RunState in memory during the node and let ActRunner's post-Advance Autosave be the only writer, or write a 'node in progress' marker that Load rolls back. Whichever way, one owner decides when a node is committed.

---

### RL-15 · P1 · architecture — "A run is active" has no owner — RunStateService.Current is never cleared, so the run shell outlives the run and buries the one screen with no cancellation token

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:372` · линза `run-loop-integrity` · переоформляет R1-13

```csharp
UiRootBootstrap.cs:371-373 `RunState run = _runStates?.Current; bool runActive = run != null && !_mainMenuOpen; _topBar.Root.style.display = runActive ? Flex : None;`. `Current` is assigned in RunStateService.cs:39 (NewRun) and :79 (Load) and is NEVER set back to null — DeleteSave (RunStateService.cs:144) only removes the file. GameFlow.cs:187-188 `await _outcomePresenter.ShowAsync(...); _runStates.DeleteSave();` — the await happens while Current is still non-null and _mainMenuOpen is still false. OutcomePresenter.cs:23-28 takes NO CancellationToken, and MenuRouter.ShowOutcomeAsync:853 `await _nav.ShowAsync(screen);` passes none either.
```

**Чем стреляет.** During the victory/defeat screen the run topbar is drawn over it with live tabs. Clicking «Карта» → UiRootBootstrap.GoToMap:568 → a map Sheet is pushed; UiNavigator.SyncVisibility:304 `bool hidden = pageAbove || (sheetAbove && s.Kind == ScreenKind.Page)` sets the outcome Page to display:None. Every tab that could remove that Sheet pushes another one (GoToBattle:556 RequestTestZone(true) pushes the test-zone Sheet, GoToInventory:541 pushes the inventory Sheet), so a Sheet is always on the stack and «В меню» can never be clicked. Unlike shop/reward, there is no rescue: ESC → «В главное меню» cancels _runCts, but nothing awaits that token here — GameFlow.RunActAsync never returns and the game is dead until Alt+F4. The root is that the shell reads 'run active' from a field the run loop never clears, instead of from the flow's own lifetime.

**Куда править.** Give the run one owner: have GameFlow clear RunStateService.Current (or flip an explicit RunPhase to Ended) before awaiting the outcome screen, and thread ctx.Cancellation into IOutcomePresenter/IMainMenuPresenter/ITitleCardPresenter so no awaited screen is outside the cancellation net.

---

### RL-16 · P1 · truth — GameConfig.asset stores 6 of 21 serialized fields; the one it does store contradicts the C# default that five tests pin

`Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20` · линза `run-loop-integrity`

```csharp
GameConfig.asset contains only `_defaultMasterVolume/_defaultMusicVolume/_defaultSfxVolume/_defaultLocale/_vesselItemSlots/_relicCapacityBase/_relicCapacityMax`. Absent: `_localPlayerTeam, _partyBannerSlots, _startGold, _battleGoldReward, _priceCommon, _priceCursed, _priceDivine, _priceSpread, _sellPercent, _shopRerollCost, _restartsPerAct, _guildSize, _startingRelicId` — every economy and guild number the run loop reads. And the one economy-adjacent field that IS stored disagrees: asset `_relicCapacityBase: 12` vs GameConfig.cs:35 `private int _relicCapacityBase = 8;`. ConfigValidationTests.cs:67-75 `GameConfig_ValuesInSaneRanges` checks only volumes, VesselItemSlots and locale — there is no `GameConfig_MatchesCodeDefaults` twin of the SimTuningConfig guard at ConfigValidationTests.cs:22.
```

**Чем стреляет.** Five EditMode tests construct `ScriptableObject.CreateInstance<GameConfig>()` and assert the C# initializer values as if they were the game's: BattleNodeFlowTests.cs:45 `before + 20 ... GameConfig.BattleGoldReward код-дефолт`, RunStateSaveTests.cs:81 `дефолты: base=8, max=16`, EventEffectApplierTests.cs:24 `Base=8, Max=16`, RelicPricerTests.cs:20, RunStateRestartTests.cs:23. The shipped game reaches RelicInventoryFull at 12 relics (RunStateService.cs:149), the suite is green on 8 — the exact 'right default, wrong asset' failure the project rule names. Everything else is worse: it is not pinned anywhere, so editing an initializer silently reballances a shipped run with no asset diff and no failing test.

**Куда править.** Re-serialize GameConfig.asset so it carries all fields (touch and save it in the Inspector), decide 8 vs 12 for RelicCapacityBase, and add the missing `GameConfig_MatchesCodeDefaults` guard next to `SimTuningConfig_MatchesCodeDefaults`, or make the tests load the asset instead of CreateInstance.

---

### RL-2 · P1 · architecture — DeploymentController._deploying is a second owner of BattlePhase; three other classes write the phase, so quitting mid-deployment strands the controller

`Assets/_Project/Scripts/Game/DeploymentController.cs:188` · линза `run-loop-integrity` · переоформляет R1-37

```csharp
`_deploying` is set true at DeploymentController.cs:188 (OnFreeDeployment, together with `_session.SetPhase(BattlePhase.Deployment)` at 190) and 266 (EnterSandbox, 268), and false at exactly two places: ExitTestZone:279 and StartCombat:583. BattlePhase is also written by three OTHER classes that know nothing about `_deploying`: GameFlow.cs:197 `_session.SetPhase(BattlePhase.None);` in RunActAsync's finally, RunBeatStage.cs:59 `EnterNode() => _session.SetPhase(BattlePhase.None)`, BattleBootstrap.cs:139 `_session.SetPhase(BattlePhase.Interlude);`. Every public entry point of the controller is gated on the stale copy: Tick:343 `if (!_deploying) return;`, OnSetTestZone:206 `if (_deploying) { … return; }`, OnSetFormation:225 `if (_deploying) return;`, OnRelicDrag:451, OnEquipAtCursor:546, EquipOn:554, and the topbar «Начать» binding Start():147 `_session.BindStart(() => { if (_deploying && !_testZone) StartCombat(); });`.
```

**Чем стреляет.** Sequence: battle node → deployment phase → ESC → «В главное меню». MenuRouter.cs:429 pops the modal and calls `_runControl.RequestReturnToMainMenu()` → GameFlow cancels `_runCts` → BattleFlow's WaitOutcomeAsync throws OCE → RunActAsync's finally sets phase None. `_deploying` stays true and `_view.SetActive(false)` (only at 283 and 586) never runs, so the DeploymentView zone overlay and all support rings keep rendering, and `_slots[i].LiveUnitId` keeps ids of RuntimeUnits that ResetToWorld's DeployParty→`_loader.Load`→ResetBattle just destroyed and respawned. Start a new run and the «Бой» tab (UiRootBootstrap.cs:549 → RequestTestZone(true) → OnSetTestZone:206) and the rest-beat «К построению» button (OnSetFormation:225) are permanent no-ops until the first real battle node, while the «Начать» binding can still fire StartCombat with `_encounter == null`.

**Куда править.** Delete `_deploying` and `_testZone` as stored state; derive them from IBattleClock.Phase (plus one `_sandboxKind` enum for gray/formation) and subscribe to IBattleClock.PhaseChanged to run the teardown ExitTestZone does today. The phase is already the single fact the navigator, topbar and input contexts read.

---

### RL-3 · P1 · truth — Four flow presenters document a "no UI listener → resolve immediately" fallback that does not exist; three have no CancellationToken, so a missing subscriber hangs the game at boot forever

`Assets/_Project/Scripts/Game/Flow/MainMenuPresenter.cs:27` · линза `run-loop-integrity` · переоформляет C-03

```csharp
MainMenuPresenter.cs:15-16 promises "Без слушателя UI возвращает Quit, чтобы headless-запуск не завис", but the body is `_pub.Publish(new OpenMainMenuRequest(hasSave, c => tcs.TrySetResult(c), null)); return await tcs.Task;` (lines 27-28) — a MessagePipe Publish with zero subscribers is a no-op, the TCS is never completed, and there is no CancellationToken and no timeout. Identical false promise and identical shape in OutcomePresenter.cs:15 / 26-27 ("Без слушателя UI завершается сразу (headless/тесты)"), TitleCardPresenter.cs:15 / 26-27, and ContinuePresenter.cs:28 / 43-44 ("Без слушателя UI (нет CoreScene/роутера) гейт завершается сразу — петля не виснет"). The only real fallback lives on the UI side and covers a different case — a missing UXML asset: MenuRouter.cs:848 `if (_root == null || _mainMenuUxml == null) { req.OnChoice?.Invoke(MainMenuChoice.Quit); return; }`.
```

**Чем стреляет.** UiRootBootstrap.Start has a documented early-out that registers none of the subscriptions: lines 199-204 `if (_router == null || _input == null) { Debug.LogWarning("[UiRootBootstrap] Нет инъекции … Рантайм-меню отключено для этого объекта."); return; }`. When it fires (UI object outside the RootLifetimeScope hierarchy, injection-order change, object disabled) GameFlow.RunGameAsync's very first statement `await _titleCardPresenter.ShowAsync();` (GameFlow.cs:119) never returns: blank screen, one yellow warning, no further log, and no token that RequestReturnToMainMenu could use — these three are the only awaits in the run loop that cancellation cannot unwind.

**Куда править.** Either make the fallback real (check for subscribers, or give every presenter a ct plus a UniTask.Delay watchdog that logs and resolves the documented default) or delete the four docstring claims. Cheapest correct version: thread the boot/run CancellationToken through ShowAsync on all four and resolve to the documented default (Quit / dismiss) on cancellation.

---

### RL-4 · P1 · dead — The SetPending/TryConsumePending battle-queue seam is dead, and BattleBootstrap's "legacy" launch branch it feeds is unreachable

`Assets/_Project/Scripts/Game/Flow/BattleSession.cs:110` · линза `run-loop-integrity`

```csharp
`IBattleSession.SetPending` (BattleSession.cs:20, impl 110-115) has no production caller: `grep -rn "SetPending\b" Assets/_Project` returns only the declaration, the implementation, two comments, and a test fake (Assets/_Project/Tests/EditMode/Run/BattleFlowTests.cs:160 `public void SetPending(BattlePresetData preset) { }`). Since nothing sets it, `_hasPending` is always false, so `TryConsumePending` always returns false, so BattleBootstrap.cs:58-60 — `// Legacy-совместимость: бой, положенный через SetPending (старый путь до persist), запустить.` / `if (_session.TryConsumePending(out BattlePresetData pending) && pending != null) LaunchBattle(pending);` — can never execute. Dead with it: fields `_pending`/`_hasPending` (lines 100-101), `TryConsumePending` (117-124), and the ArmOutcome() call inside SetPending.
```

**Чем стреляет.** A seam that looks wired but is not. Its own doc says it is how a battle is queued ("root → child: поставить бой в очередь (перед загрузкой боевой сцены). Взводит ожидание исхода"), so anyone tracing "how does a battle start" finds two live-looking entry points where only RequestLaunch exists, and will believe the dev-panel path goes through SetPending when it actually goes through RestartInPlace/RequestLaunch.

**Куда править.** Delete SetPending, TryConsumePending, `_pending` and `_hasPending` from the interface and implementation, delete BattleBootstrap.cs:58-60, and drop the member from the test fake.

---

### RL-5 · P1 · dead — ActRunner's `IRunBeatStage beat = null` default and its two null-guards are a dead fallback on a DI-registered type

`Assets/_Project/Scripts/Game/Services/ActRunner.cs:30` · линза `run-loop-integrity`

```csharp
`public ActRunner(INodeResolver resolver, IMapNodeChooser chooser, RunStateService runStates, IRunBeatStage beat = null)` (ActRunner.cs:29-30), documented "null = петля без мира (headless/тесты) — стыки просто не оформляются" (lines 25-28). But ActRunner is DI-constructed (`builder.Register<ActRunner>(Lifetime.Singleton);`, RootLifetimeScope.cs:191) and IRunBeatStage is registered one line above (`builder.Register<RunBeatStage>(Lifetime.Singleton).As<IRunBeatStage>();`, RootLifetimeScope.cs:190), so VContainer always injects a real instance; the only other construction site passes it explicitly (`new ActRunner(resolver, new AutoFirstNodeChooser(), _runStates, _beat)`, Assets/_Project/Tests/EditMode/Guild/ActRunnerTests.cs:46). Nobody takes the default, so both guards — ActRunner.cs:69 `if (!actEntry) _beat?.EnterRestBeat(beatCts.Token);` and 101 `_beat?.EnterNode();` — are dead branches that read as safety.
```

**Чем стреляет.** Two costs. The documented "loop without a world" mode does not exist, so anyone debugging a missing rest beat chases a null that cannot happen. And it is the project's banned pattern — a constructor default on a DI-registered type: if the RunBeatStage registration is ever dropped (exactly what `= null` invites), VContainer fails the whole registration branch at container build instead of quietly passing null, and the symptom appears nowhere near this file.

**Куда править.** Make the parameter required (`IRunBeatStage beat`), drop both `?.` guards, and have any test that wants a beat-less loop pass a no-op IRunBeatStage.

---

### TS-1 · P1 · legacy — StaggeredBrainSpikeTests is 259 lines of self-declared throwaway spike, verbatim superseded by BrainTests

`Assets/_Project/Tests/EditMode/Combat/StaggeredBrainSpikeTests.cs:16` · линза `tests-as-subject`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Its own docstring: "Весь код ниже — throwaway: прото-шов ISpikeBrain + каденс-система крутятся в обход CombatSimulation … Настоящие IUnitBrain/BrainSystem внутри Tick — шаг 3 после зелёного гейта." Phase 3 shipped: BrainSystem.cs:29-33 is the real cadence rule, and BrainTests.cs:93 is headed "Каденс / детерминизм (S1, продовый BrainSystem)" with StaggeredBrain_SameChecksum_AcrossTwoRuns (BrainTests.cs:96), TargetDeath_SetsBrainDirty_ReevaluatesNextTick (:104), IntentPersistsBetweenAiTicks_UnitKeepsMoving (:127), RunStaggered (:156), MakeTwoTeams (:183), MakeUnit (:193) and Checksum (:215) — the same test names, the same fixtures, the same helper bodies as the spike file, run against the production system. `grep -rn "ISpikeBrain|SpikeBrainSystem|NearestEnemySpikeBrain"` across Assets/ returns hits only inside this one file: the three internal types it defines have no consumer anywhere.
```

**Чем стреляет.** It is not inert. SpikeBrainSystem.Tick (line 249) gates on `currentTick % SimConstants.AiTickInterval == u.Id % SimConstants.AiTickInterval` while the shipped BrainSystem.cs:32 gates on `u.BrainDirty || …` against `u.BrainPhase` — the spike has already drifted off the real rule (Id vs BrainPhase, no explicit dirty flag). A maintainer changing the AI cadence will find two green suites named identically, edit the wrong one, and get a false pass. It also carries the third copy of the checksum formula (line 172-185, the second copy being BrainTests.cs:215-227), so the desync probe's hash has three owners.

**Куда править.** Delete Assets/_Project/Tests/EditMode/Combat/StaggeredBrainSpikeTests.cs and its .meta outright — every assertion in it exists in BrainTests.cs against the production BrainSystem. Then collapse the remaining checksum duplication: expose CombatSimulation's hash (or a small static ChecksumOf(units, tick)) and have BrainTests call it instead of keeping its own copy.

---

### TS-13 · P1 · truth — StatMathTests pins the cascade-aware bake to the cascade-FREE formula — the test blesses exactly the bug R1-48/R1-72 reports

`Assets/_Project/Tests/EditMode/ContentHub/StatMathTests.cs:77` · линза `tests-as-subject` · переоформляет R1-48

```csharp
BuildEffective_MatchesDirectStatsBake builds `var expected = new Stats(config); expected.AddModifiersFrom(relic, mods);` (lines 77-78) and asserts `expected.Get(st) == eff.Get(st)` for every StatType, where `eff = StatMath.BuildEffective(relic, config)` (line 75) — the 2-arg overload, so `classConfig` takes its default `null` (StatMath.cs:19). ClassBaseline.Apply returns immediately when `config == null` (ClassBaseline.cs:28: `if (stats == null || data == null || config == null) return;`) and EnemyScalers.Apply returns immediately for a non-EnemyData (EnemyScalers.cs:21). So BuildEffective degenerates to literally the two lines the test typed as `expected`. The production caller passes three args: ContentIndex.cs:126 `StatMath.BuildEffective(e.Unit, _statsConfig, _classBalanceConfig)`. And `new Stats(config) + AddModifiersFrom(data, data.Stats)` is verbatim what ContentAuditor.BuildRow does (ContentAuditor.cs:82-84) — the formula round 1 calls wrong.
```

**Чем стреляет.** The only test of the project's stated "таблица не врёт" contract (StatMath.cs:9-12: «переиспользует Stats из сим-кода, а не переписывает формулу») compares the method to its own body under the one argument combination that erases both cascade layers. Worse, the value it pins as correct IS the auditor's cascade-free bake. Concrete failure: change ClassBaseline/EnemyScalers ordering, or drop the ClassBaseline.Apply call out of BuildEffective entirely, and all four StatMathTests stay green — while every MaxHP/MoveSpeed the Content Hub shows for a Tank relic (asset says HpMult 1.5 → 3000) silently becomes the StatsConfig default 1200. Conversely, if someone fixed ContentAuditor by routing it through BuildEffective WITH the class config, this test would go red — so the suite actively defends the broken formula.

**Куда править.** Change the fixture to call the 3-arg overload with a ClassBalanceConfig loaded from Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset, and assert the cascade result (Tank relic → MaxHP 3000, MoveSpeed 2.55) rather than a hand-rebuilt `new Stats(config)`. Add an EnemyData case with a SpeciesData scaler. Then delete the `classConfig = null` default parameter on StatMath.cs:19 so no caller (auditor included) can silently opt out of the cascade.

---

### TS-14 · P1 · gap — RequiredLocalizationKeys_Exist checks only that the key exists, not that RU has text — 7 required keys ship blank right now

`Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:97` · линза `tests-as-subject`

```csharp
The guard is `IReadOnlyList<string> missing = ContentLocalization.MissingKeys(def); Assert.IsEmpty(missing, ...)`. MissingKeys does only `if (!col.SharedData.Contains(key)) missing.Add(key);` (ContentLocalization.cs:106) — SharedData is the key registry, not the RU table. Diffing 'Content Shared Data.asset' (212 m_Key entries) against Content_ru.asset (205 m_Id entries) by id: item.oaken_charm.name, item.oaken_charm.desc, item.swift_boots.name, item.swift_boots.desc, item.war_banner.name, item.war_banner.desc, species.goblins.desc have a shared key and no ru row. All are `default: return NameAndDesc` domains (ContentLocalization.cs:57), i.e. required. LocalizationService resolves a missing entry to empty string — pinned by LocalizationServiceTests.cs:54.
```

**Чем стреляет.** Violates the project's HARD rule ("RU filled") while the test named after that rule is green. Three of the five shipped items — Oaken Charm, Swift Boots, War Banner — render with an empty name AND empty description everywhere the player sees them (shop shelf, reward card, vessel item slots), and the goblin species tooltip has no body. The suite already knows the right assertion: KeywordContentTests.cs:49 does `if (string.IsNullOrEmpty(ContentLocalization.GetValue(Ru, kw.Id + "." + ContentKeys.NameSuffix))) missing.Add(...)` — the general content guard just never got it.

**Куда править.** Make MissingKeys (ContentLocalization.cs:98-109) the single owner of the whole rule: for every required suffix report the key when it is absent from SharedData OR when `GetValue("ru", key)` is null/empty. That fixes ContentValidationTests and the inspector's «Create missing keys» button at once; then fill the seven RU strings.

---

### TS-15 · P1 · truth — GameConfig.asset carries only 7 of its 20 serialized fields, disagrees with the code default the whole test tier uses, and nothing pins it

`Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20` · линза `tests-as-subject` · переоформляет R1-55

```csharp
The shipped YAML ends at line 21 and contains exactly: _defaultMasterVolume, _defaultMusicVolume, _defaultSfxVolume, _defaultLocale, _vesselItemSlots, _relicCapacityBase: 12, _relicCapacityMax. Absent entirely: _localPlayerTeam, _partyBannerSlots, _startGold, _battleGoldReward, _priceCommon, _priceCursed, _priceDivine, _priceSpread, _sellPercent, _shopRerollCost, _restartsPerAct, _guildSize, _startingRelicId (all declared in GameConfig.cs:29-72). CoreScene.unity is the only referrer of guid 3f479309a3dcb31429f94c65a88fb275, so this asset is what the game plays. Nine test fixtures instead build `ScriptableObject.CreateInstance<GameConfig>()` — RunStateSaveTests.cs:81/111/141, RelicPricerTests.cs:20, ShopControllerTests.cs:28, GuildRosterTests.cs:29, RunStateEquipTests.cs:24, RunStateRestartTests.cs:23, ActRunnerTests.cs:29, BattleNodeFlowTests.cs:26, EventEffectApplierTests.cs:24 — and the comments state the code default: RunStateSaveTests.cs:81 «дефолты: base=8, max=16», EventEffectApplierTests.cs:24 «Base=8, Max=16». The asset says 12. ConfigValidationTests.GameConfig_ValuesInSaneRanges (ConfigValidationTests.cs:67-75) is the one test that loads the asset and it asserts only volume ranges, VesselItemSlots >= 1 and a non-empty locale.
```

**Чем стреляет.** Two defects in one. (a) A live disagreement: the guild ships with 12 relic slots, every capacity test proves the rules at 8 — RelicCapacity_EnforcedAndUpgradable (RunStateSaveTests.cs:79) and the RelicInventoryFull→reward-drop path would behave differently at the played number, and nothing would go red. (b) The deeper root: because 13 fields were added after the asset was last serialized, they are not in the file at all — so today the whole economy (prices, spread, sell %, reroll cost, start gold, restarts per act, guild size, relic.base id) runs off C# initializers, and the first time anyone touches GameConfig in the Inspector all 13 get written to disk at whatever the initializers then say. RelicPricerTests hardcodes 40/50/60/25 from those initializers (RelicPricerTests.cs:47-59), so after that re-serialization the tests and the asset become two independent owners of the price table with no guard between them.

**Куда править.** Add GameConfig to the asset-vs-code consistency test next to SimTuningConfig_MatchesCodeDefaults (ConfigValidationTests.cs:21) — load the single asset and assert every public getter against a fresh CreateInstance, which fails today on RelicCapacityBase and forces a decision on 8-vs-12. Re-serialize GameConfig.asset so the missing 13 fields are on disk. Then switch RelicPricerTests/ShopControllerTests/RunStateSaveTests to the loaded asset so they test the numbers the game plays.

---

### TS-2 · P1 · dead — IBattleSession.SetPending has zero producers, so BattleBootstrap's "legacy-compat" launch branch can never run

`Assets/_Project/Scripts/Game/Flow/BattleBootstrap.cs:58` · линза `tests-as-subject`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
BattleBootstrap.Start: "// Legacy-совместимость: бой, положенный через SetPending (старый путь до persist), запустить." then `if (_session.TryConsumePending(out BattlePresetData pending) && pending != null) LaunchBattle(pending);`. `grep -rn "SetPending|TryConsumePending" --include=*.cs Assets/` returns exactly six sites: the interface declarations (BattleSession.cs:20, :23), the implementations (BattleSession.cs:110, :117), this single TryConsumePending consumer, and the empty stubs in the test fake (BattleFlowTests.cs:160-161: `public void SetPending(BattlePresetData preset) { }`). Nothing anywhere calls SetPending, so `_hasPending` (BattleSession.cs:101) is永 false and TryConsumePending always returns had=false.
```

**Чем стреляет.** This is the shape the brief calls out as escalating to P1: a fallback that looks like safety but never runs. It reads as "boot picks up a queued battle", so a reader debugging a battle that failed to launch will spend time on a branch that is provably unreachable. SetPending also calls ArmOutcome(), giving the dead path an apparent side effect on outcome-waiting. And the two dead members are load-bearing tax: every IBattleSession implementer must stub them, which is why the test double at BattleFlowTests.cs:160-161 exists at all.

**Куда править.** Delete SetPending/TryConsumePending from IBattleSession (BattleSession.cs:20,23), their bodies (BattleSession.cs:110-124), the `_pending`/`_hasPending` fields (BattleSession.cs:100-101), the BattleBootstrap.cs:58-60 branch, and the two stubs in BattleFlowTests.cs:160-161. RequestLaunch/BindLaunch is the live path.

---

### TS-3 · P1 · gap — Only 2 of 9 config assets have an asset-vs-code guard, and two of the unguarded ones already disagree with the defaults every test uses

`Assets/_Project/Tests/EditMode/Content/ConfigValidationTests.cs:67` · линза `tests-as-subject` · переоформляет R1-55

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Assets/_Project/ScriptableObjects/Configs/ holds 9 assets (ActConfig, ClassBalanceConfig, CombatColorPalette, CombatFeelConfig, GameConfig, MapStyle, SimTuningConfig, StatsConfig, WorldCameraBlends). Guards exist for exactly two: SimTuningConfig_MatchesCodeDefaults (ConfigValidationTests.cs:21) and ActConfigAssetTests. GameConfig_ValuesInSaneRanges (ConfigValidationTests.cs:67) checks three volumes, VesselItemSlots>=1 and a non-empty locale — nothing else. Meanwhile GameConfig.asset:22 has `_relicCapacityBase: 12` against GameConfig.cs:34 `= 8`, and StatsConfig.asset:17 has `_attackSpeedMax: 4` against StatsConfig.cs:21 `= 2.5f`. Every test that exercises those numbers builds a code-default instance: RunStateSaveTests.cs:81 `var config = ScriptableObject.CreateInstance<GameConfig>(); // дефолты: base=8, max=16`, RelicPricerTests.cs:20 `// код-дефолты: 50/100/150`, ClassBaselineTests.cs:23 MakeConfig() which never sets _baseHp/_baseMoveSpeed at all.
```

**Чем стреляет.** ActConfigAssetTests.cs:10-14 documents this exact failure twice from play-QA ("Ровно так карта и осталась по 2-3 узла на этаже, когда профиль в коде уже был 5-6") but the lesson was applied only to the map generator. RelicCapacity_EnforcedAndUpgradable (RunStateSaveTests.cs:79) proves the capacity ladder at 8→16 (8 upgrade steps) while the shipped game runs 12→16 (4 steps) — a capacity bug reported from play is not reproducible in EditMode, and "fixing" the C# default changes nothing the player sees. GameConfig.asset also omits the whole economy block (_startGold, _priceCommon/Cursed/Divine, _priceSpread, _sellPercent, _shopRerollCost, _restartsPerAct, _guildSize, _startingRelicId, _partyBannerSlots, _localPlayerTeam are absent from the YAML), so those live only in C# initializers with no inspector row to tune. MapStyle.asset is likewise the played value for map layout while all nine MapLayoutTests use MapLayout.Default.

**Куда править.** Add one guard fixture per config in the ConfigValidationTests style — load the single shipped asset via AssetDatabase and assert every scalar against the C# initializer, the same shape as SimTuningConfig_MatchesCodeDefaults. Start with GameConfig, StatsConfig, ClassBalanceConfig and MapStyle. Then decide per field which side is the owner and delete the loser (see the StatsConfig and AttackSpeed findings) instead of pinning both forever.

---

### TS-4 · P1 · truth — StatsConfig.asset's MaxHP/MoveSpeed defaults are dead and contradict ClassBalanceConfig, and no test ever reads the shipped asset

`Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:18` · линза `tests-as-subject`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
StatsConfig.asset `_defaults:` contains `- Stat: 0 / Value: 1200` (MaxHP) and `- Stat: 20 / Value: 3` (MoveSpeed). ClassBalanceConfig.asset carries `_baseHp: 2000`, `_baseMoveSpeed: 3` and per-class multipliers, and ClassBaseline.Apply (Assets/_Project/Scripts/Combat/ClassBaseline.cs:25-29) unconditionally adds `config.GetBaseModifiers(data.CombatClass)` — a group of ModifierOp.Override modifiers — as the FIRST group for every unit where `data != null && config != null`. RuntimeUnitFactory.cs:69 calls it before anything else. "Последний Override побеждает", so the 1200 is overwritten for every unit that has UnitData. The only tests touching StatsConfig defaults use a fresh empty instance: StatMathTests.cs:96-102 `var config = ScriptableObject.CreateInstance<StatsConfig>(); … Assert.AreEqual(config.GetDefault(st), eff.Get(st))` — an asset with an empty _defaults array.
```

**Чем стреляет.** One fact (a unit's base HP) with two owners that already disagree: 1200 in StatsConfig.asset versus 2000 (Bruiser anchor) in ClassBalanceConfig.asset. A designer who opens StatsConfig, sees MaxHP 1200 and tunes it gets zero effect in game and zero test failure — and Content Hub's ConfigDiff will happily list it as "накручено". The 1200 survives only for a unit created with `data == null`, i.e. the synthetic dummies in tests and benches, which is why every fixture that hand-builds a RuntimeUnit sets MaxHP via a Flat modifier instead.

**Куда править.** Delete the MaxHP and MoveSpeed rows from StatsConfig.asset's _defaults (ClassBalanceConfig owns both axes per the cascade docstring in ClassBalanceConfig.cs:14-19), and add the guard test from the previous finding so the removal stays removed. If a class-less fallback is genuinely wanted, make ClassBaseline emit it rather than leaving a second silent authoring surface.

---

### TS-5 · P1 · dead — StatsConfig attack-speed clamp has three owners, zero sim readers, and a test that makes the dead knob look alive

`Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:128` · линза `tests-as-subject` · переоформляет R1-01

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
`grep -rn "AttackSpeedMin|AttackSpeedMax|_attackSpeedMin|_attackSpeedMax"` over Assets/ returns every reader: StatsConfig.asset:16-17 (`0.1` / `4`), StatsConfig.cs:20-21 (`= 0.1f` / `= 2.5f`), StatsConfig.cs:28-29 (the getters), ContentAuditor.cs:43-44 (`float asMin = config != null ? config.AttackSpeedMin : 0.1f; float asMax = config != null ? config.AttackSpeedMax : 2.5f;`), and ContentValidationTests.cs:133 `Assert.Less(cfg.AttackSpeedMin, cfg.AttackSpeedMax)`. Nothing in Guildmaster.Combat reads either: Stats.cs's RebuildCache/Compose never touch them, and AttackTiming.IntervalTicks takes a raw float. So the ceiling exists three times — 4 (asset, authoritative), 2.5 (C# initializer), 2.5 (auditor's hardcoded fallback) — and is applied nowhere.
```

**Чем стреляет.** The guard test is what keeps the corpse warm. StatsConfig_AttackSpeedClampOrdered is green because 0.1 < 4, which reads to any maintainer as "the clamp is validated, therefore the clamp exists". The docstrings reinforce the lie: Stats.cs:30 says "после всех модификаторов и клампов" and StatType.cs:26 says "клампится из StatsConfig". Meanwhile ContentAuditor's audit column flags content against a ceiling of 4 that the sim will never enforce, and its own fallback disagrees with the asset by 1.5x — so an audit run without the config silently switches ceiling.

**Куда править.** Pick one owner and delete the other two. Either implement the clamp in Stats.Compose (then the asset's 4 is the single owner, ContentAuditor's 2.5f fallback goes away, and the test becomes meaningful), or delete _attackSpeedMin/_attackSpeedMax from StatsConfig.cs and the asset, drop ContentValidationTests.cs:128-136, drop ContentAuditor.cs:43-44, and correct the two lying docstrings.

---

### TS-6 · P1 · correctness — Damage-affinity tests mirror AffinityTable.VulnerableMult, so the +30% vulnerability magnitude is pinned nowhere

`Assets/_Project/Tests/EditMode/Combat/DamagePipelineTests.cs:284` · линза `tests-as-subject`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Three of the four affinity tests assert against the production constant rather than a literal: line 284 `float expected = 100f * AffinityTable.VulnerableMult;`, line 298 `Assert.AreEqual(100f * AffinityTable.VulnerableMult, …)`, line 314 same. AffinityTable.cs:15 is `public const float VulnerableMult = 1.3f;` with the docstring "Множитель для «уязвим» (+30%)". `grep -rn "VulnerableMult|1\.3f"` across Assets/_Project/Tests, Scripts/Combat and Scripts/Data returns only those three test lines and the const itself — 1.3 appears as a literal nowhere.
```

**Чем стреляет.** Set VulnerableMult to 1.0f and Light_VulnerableAgainstUndeadAndDemon, Dark_VulnerableAgainstLiving and Affinity_AppliesOnTopOfTrueDamage_AndIsNotMitigatedByArmor all stay green while the entire school-vs-creature-type mechanic (GDD «8» §«Школа vs сродство») silently evaporates: Light on Undead, Light on Demon and Dark on Living become neutral, and the third test's whole point — that affinity multiplies even True damage past full armour — degenerates to `100 == 100`. The neighbouring immunity tests get this right (Poison_ImmuneAgainstUndeadAndConstruct at line 259 asserts a hard `0f`, not ImmuneMult), which is what makes the vulnerability half's mirroring an inconsistency rather than a house style.

**Куда править.** Replace `AffinityTable.VulnerableMult` with the literal 130f in DamagePipelineTests.cs:284, 298 and 314, and add one test asserting `AffinityTable.VulnerableMult` is 1.3f so the GDD number has exactly one pinned owner. Same treatment for NeutralMult at any site that reads it back.

---

### TS-7 · P1 · gap — Commit 0410520c deleted both tests guarding "the arena returns to the world when a node ends" and its new owner has no tests at all

`Assets/_Project/Tests/EditMode/Guild/BattleNodeFlowTests.cs:76` · линза `tests-as-subject`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
`git show 0410520c -- Assets/_Project/Tests/` removes `Win_ResetsArena_OnlyAfterReward` (assertions: `Assert.AreEqual(0, reward.ResetsSeenAtReward, "Пока игрок выбирает награду, поле боя ещё живое.")` and `Assert.AreEqual(1, session.ResetCount, "Уход с узла возвращает арену во вне-боевое состояние.")`) and `Defeat_ResetsArena_Too` (`Assert.AreEqual(1, session.ResetCount, "Поражение тоже уводит с узла — арена не должна залипнуть.")`), plus the ResetSpyReward and CountingSession doubles that made the ordering observable. The mechanic did not go away — it moved to RunBeatStage.EnterRestBeat (Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:49-57: `_session.RequestReset(); _session.SetPhase(BattlePhase.Interlude); _continue.ShowRestBeat(...)`). `grep -rn RunBeatStage` over Assets/_Project/Tests returns one hit: ActRunnerTests.cs:221 `private sealed class SpyBeat : IRunBeatStage` whose whole body is `RestBeats++` / `NodeEntries++`, asserted only as a count in RunAct_RestBeat_BetweenNodes_NotOnActEntry (ActRunnerTests.cs:126).
```

**Чем стреляет.** Two invariants that had regression tests now have none. (1) Ordering: the reward must be picked over a still-living battlefield. ActRunner.cs places `if (!actEntry) _beat?.EnterRestBeat(beatCts.Token)` at the top of the next iteration, so it is currently correct by construction — but nothing stops a future edit from moving the call before `await flow.Run(...)`, which is precisely the "досмотр добивания шёл по пустому полю" regression BattleNodeFlow.cs:18-20 records as already having happened once. (2) Defeat: ActRunner returns EventResult.Defeated straight out of the loop without ever entering a rest beat, so RequestReset on the losing path now depends entirely on GameFlow.RunActAsync's `finally` (GameFlow.cs:196) firing after the outcome screen — a longer, untested path for the exact "арена залипла" symptom the deleted test named. FakeSession.ResetCount still sits in BattleFlowTests.cs:176 with no reader, the fossil of the removed coverage.

**Куда править.** Add a RunBeatStage fixture (it takes IBattleSession + IContinuePresenter + two IPublishers — all trivially fakeable) asserting EnterRestBeat calls RequestReset and sets BattlePhase.Interlude, and EnterNode sets None. Then restore the ordering guard at the ActRunner level: a spy beat that records the reward-presenter call count at EnterRestBeat time, asserting the reward already ran. Cover the defeat path explicitly.

---

### UA-1 · P1 · dead — The whole sim command pipeline is dead: queue, three commands and the paused-tick hack are reachable only from a NetworkBehaviour that is in no scene

`Assets/_Project/Scripts/Combat/ICombatCommand.cs:10` · линза `uncovered-assemblies` · переоформляет R1-82

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
`public interface ICombatCommand : ISimCommand` (ICombatCommand.cs:10) is implemented by exactly three classes: PauseCommand.cs:4, ResumeCommand.cs:4, SpawnUnitCommand.cs:6. `CombatSimulation.EnqueueCommand` (CombatSimulation.cs:500) has exactly two non-test callers repo-wide — none: the only production caller is `Net/NetworkCommandRelay.cs:68`. NetworkCommandRelay's script guid `d5c295c98501a84409171559b523d8ab` appears in zero .unity/.prefab/.asset/.uxml files (grepped the asset text). `SpawnUnitCommand` has zero callers even there — the relay's switch (NetworkCommandRelay.cs:59-64) only builds Pause/Resume. Real pause goes straight through `BattleInputController.cs:55` -> `sim.SetPaused`, and real spawning straight through `EncounterLoader.cs:190/228` -> `EnqueueUnitSpawn`, both bypassing the queue. The sim carries dead weight for it: `_commandQueue` (line 51), `ApplyDueCommands` (line 606), and the special paused branch at lines 241-247 whose comment ("счётчик тиков продолжает идти, ПОКА в очереди есть команды — иначе ResumeCommand с будущим TargetTick никогда не наступит") exists solely to serve ResumeCommand.
```

**Чем стреляет.** ISimCommand's docstring claims "Все мутации сим во время боя входят через команды: это шов host-authoritative мультиплеера и реплеев". That invariant is already false everywhere, so the seam is not a seam — it is a decoy that will be trusted when MP is built, and the paused-tick hack is untestable live logic on the hot tick path serving a code path nobody can reach.

**Куда править.** Delete SpawnUnitCommand, PauseCommand, ResumeCommand, ICombatCommand, ISimCommand, `_commandQueue`, `EnqueueCommand`, `ApplyDueCommands` and the `if (_commandQueue.Count > 0) _currentTick++` branch, together with NetworkCommandRelay. Re-introduce a command seam when the MP phase actually routes through it, and at that point make it the ONLY writer (EncounterLoader/DevTools would have to go through it too).

---

### UA-14 · P1 · gap — Every Camp node in an act is a silent no-op: CoreScene never assigns CampScreen.uxml, so the flow's null-guard immediately calls OnLeave

`Assets/_Project/Scenes/CoreScene.unity:160` · линза `uncovered-assemblies`

```csharp
CoreScene.unity:160 (inside the UiRootBootstrap block at 141-168, every sibling screen IS assigned): `  _campScreen: {fileID: 0}`
MenuRouter.cs:807: `if (_root == null || _campUxml == null || req.Session == null) { req.OnLeave?.Invoke(); return; }`
UiRootBootstrap.cs:49 `[SerializeField] private VisualTreeAsset _campScreen;` → passed at line 215 into `_router.Initialize(..., _campScreen, ...)`.
CampScreen.uxml guid 3dfc93592132fd343b4d7174836192c7 appears in no .unity/.prefab/.asset in the project, and no C# loads it by path (grep "CampScreen" hits only CampScreenView.cs and MenuRouter comments).
The nodes are guaranteed: MapGenConfig.cs:107-110 `new AnchorRule(8, MapNodeType.Camp, width: 3)` and `new AnchorRule(13, MapNodeType.Camp, width: 3)` — six Camp nodes per act, one row of three right before the boss. NodeResolver.cs:115-116 routes them to CampFlow.
```

**Чем стреляет.** A whole authored feature (CampSession + CampFlow + CampScreenView + CampScreen.uxml + 5 RU-filled loc keys + camp.action.ui/camp.denied.ui audio keys) is unreachable. The player walks onto a camp node and it instantly counts as cleared with no screen and no feedback — and the guard that produces this is the kind of "safety" fallback that hides the breakage instead of reporting it.

**Куда править.** Assign UI/Screens/CampScreen.uxml to UiRootBootstrap._campScreen in CoreScene. Then make the guard loud: MenuRouter's `_xxxUxml == null` early-returns should Debug.LogError the screen name rather than silently resolving the flow, so the next unassigned screen fails visibly.

---

### UA-15 · P1 · truth — UI localization is a facade: screens query the Content table while 41 ui.* keys live in the UI table, and 52 of 60 keys do not exist at all — the real owner of every string is a hardcoded Russian literal

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48` · линза `uncovered-assemblies`

```csharp
LocalizationService.cs:18,48: `private const string ContentTable = "Content";` / `public string GetString(string key) => GetString(ContentTable, key);` — every screen uses this single-arg overload (MenuRouter.cs:448 `string v = _loc?.GetString(key);`, CampScreenView, ChestScreenView, RewardScreenView, LoadoutHubView, RunModeBarView).
`Localization/Tables/UI Shared Data.asset` holds 41 ui.* keys (ui.camp.title, ui.beat.continue, ui.node.camp.title, ui.kit.*, ui.tag.category.*, …); `Content Shared Data.asset` holds a disjoint set of 21 ui.* keys. `comm -12` over the two key lists is EMPTY — no key is in both. So every ui.* key that lives in the UI table is unreachable from the screens.
Of the 60 distinct keys passed to the `L(key, ru)` helpers in Scripts/UI, 52 are absent from the UI table (ui.mainmenu.*, ui.outcome.*, ui.reward.*, ui.shop.*, ui.loadout.*, ui.run.*, ui.titlecard.*, ui.menu.quit, …).
LocalizationService.cs:66: `if (res.Entry == null) return string.Empty;` and MenuRouter.cs:446-450 `Loc(key, ru) { string v = _loc?.GetString(key); return string.IsNullOrEmpty(v) ? ru : v; }` — the miss is swallowed and the C# literal wins.
A third owner exists in markup: 15 of 19 files under UI/Screens/*.uxml carry Cyrillic literals, e.g. ContinueScreen.uxml:8 `<ui:Button name="btn-continue" text="Продолжить" …/>`, and MenuRouter.Label (MenuRouter.cs:755-760) deliberately keeps the UXML text when the key misses.
```

**Чем стреляет.** HARD rule: all player-facing text through localization keys. In practice the shipped RU text is owned by C# string literals and UXML attributes; the String Tables are decoration. Concretely: UI_ru.asset already translates ui.camp.title as "Привал", but CampScreenView asks the Content table, gets empty, and prints its own "Привал" — editing the table changes nothing, and switching locale to EN leaves the entire interface Russian with no warning anywhere.

**Куда править.** Pick one owner. Add a `UiTable` constant and a `GetUi(key)` path so screens hit the UI table (DescriptionService already does this — DescriptionService.cs:121-123 passes UiTable explicitly), move the 21 stray ui.* keys out of the Content table, backfill the 52 missing keys, strip the RU literals from the `L(key, ru)` call sites and the UXML `text=` attributes, and make a missing key log once instead of returning string.Empty.

---

### UA-16 · P1 · correctness — The node-farewell card renders completely blank: its two keys exist only in the UI table and this call site has no fallback at all

`Assets/_Project/Scripts/UI/MenuRouter.cs:691` · линза `uncovered-assemblies`

```csharp
MenuRouter.cs:691-692:
            if (title != null) title.text = _loc?.GetString(req.TitleKey) ?? string.Empty;
            if (body  != null) body.text  = _loc?.GetString(req.BodyKey)  ?? string.Empty;
The keys come from the flows: CampFlow.cs:35-36 `_farewellPub?.Publish(new OpenNodeFarewellRequest("ui.node.camp.title", "ui.node.camp.farewell", ctx.NodeCancellation));` (ChestFlow.cs:35 and ShopFlow.cs:36 are identical with ui.node.chest.* / ui.node.shop.*).
All six of ui.node.{camp,chest,shop}.{title,farewell} exist ONLY in `Localization/Tables/UI Shared Data.asset`; none is in `Content Shared Data.asset`. GetString's single-arg overload targets Content (LocalizationService.cs:48) and returns string.Empty on a miss (line 66).
```

**Чем стреляет.** Unlike the `L(key, ru)` screens, this one has no literal to fall back to, so the QA #48/#49 "единый ритм конца узла" frame is pushed as a full Page with an empty title and empty body after every chest and shop node — a visibly broken screen, not a degraded one.

**Куда править.** Route these two lookups through the UI table (same fix as the table split above). Until then they must not ship with `?? string.Empty` as the only guard — an empty title should suppress the screen or log.

---

### UA-17 · P1 · truth — ActConfig.asset is orphaned — the act map the game generates comes from C# defaults, so designer edits to the act config are silently ignored

`Assets/_Project/Scenes/CoreScene.unity:294` · линза `uncovered-assemblies` · переоформляет R1-49

```csharp
CoreScene.unity:294 `  _actConfig: {fileID: 0}`.
RootLifetimeScope.cs:67: `builder.RegisterInstance(_actConfig != null ? _actConfig : ScriptableObject.CreateInstance<ActConfig>());`
A project-wide grep for the ActConfig.asset guid dbc39cb776c7fd6469e9cd31b97af1ab across every .unity/.prefab/.asset returns nothing — the asset is referenced from no scene, no prefab, no other asset.
The asset is fully authored (ScriptableObjects/Configs/ActConfig.asset: Columns 15, EdgeColumnWidth 3, MinColumnWidth 5, MaxColumnWidth 7, MaxEdgesPerNode 4, three ZoneRules, anchors Chest@7 / Camp@8 / Camp@13) and today it happens to match MapGenConfig.cs's DefaultZones()/DefaultAnchors() exactly — which is precisely why nobody has noticed.
```

**Чем стреляет.** HARD rule: the asset is what plays. Right now the two copies agree, so the defect is invisible; the moment a designer retunes zone weights or act depth in the inspector, the game keeps generating the old map and the bug looks like a generator bug. This also supersedes R1-49 at the root: ActConfig.ToGenConfig() calling Validated() in place can never dirty the asset, because the asset is never the instance being validated — a throwaway CreateInstance is.

**Куда править.** Assign ScriptableObjects/Configs/ActConfig.asset to RootLifetimeScope._actConfig in CoreScene, and add a guard test asserting the scene's ActConfig reference is non-null and that its ToGenConfig() matches the shipped asset (not the code defaults). Then delete the DefaultZones()/DefaultAnchors() code duplicates, or keep them only as the CreateAssetMenu seed.

---

### UA-2 · P1 · truth — "Is the battle paused" is stored in two places; StartCombat un-pauses only the sim and leaves Time.timeScale at 0

`Assets/_Project/Scripts/Game/DeploymentController.cs:587` · линза `uncovered-assemblies`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P2` (уверенность high)

```csharp
Owner A: `CombatSimulation._isPaused` (Combat/CombatSimulation.cs:72), written by `SetPaused` (line 455), read by `Tick` (line 241) and `IsPaused` (line 136). Owner B: `TimeScaleService._paused`, written by `SetPaused` (Game/Services/TimeScaleService.cs:192) which sets `Time.timeScale = Effective` (line 200). Only ONE caller writes both: `BattleInputController.OnPauseToggle` (Game/Input/BattleInputController.cs:54-56). Every other writer touches the sim alone: DeploymentController.cs:169, 263, 570 and **587** (`_sim.SetPaused(false)` inside `StartCombat`), BattleBootstrap.cs:90/110/120, WorldStageController.cs:53, GuildmasterCommands.cs:110/117. `TimeScaleService.Reset()` (line 216-228) explicitly documents "Игровую скорость и паузу игрока НЕ трогаем", and it is registered `Lifetime.Scoped` in CombatLifetimeScope.cs:67 — a scope that outlives individual battles — so `Dispose()` (timeScale = 1) does not run between battles.
```

**Чем стреляет.** With `Time.timeScale == 0`, `CombatLoopService` accumulates `Time.deltaTime` (Game/Services/CombatLoopService.cs:51) and therefore never reaches `TickDelta`. Repro: pause with Space during a battle (both owners = paused), reach the deployment phase again (dev-R restart -> BattleBootstrap.RestartBattle -> RequestDeployment, or the rest-beat "К построению"), press "Начать" -> line 587 sets `_isPaused = false` while timeScale stays 0. The battle is frozen with the sim reporting itself un-paused, and because `OnPauseToggle` reads `_simulation.IsPaused` (now false) the next Space press re-pauses instead of resuming: the player must press Space twice to unfreeze.

**Куда править.** Make TimeScaleService the single owner of pause and delete `CombatSimulation._isPaused`/`SetPaused`/`IsPaused` and the paused branch in `Tick` — with timeScale 0 the loop already stops ticking, so the sim's own flag buys nothing. Every current `_sim.SetPaused(...)` call site becomes `_time.SetPaused(...)`; `BattleInputController` reads `_time.IsPaused`.

---

### UA-3 · P1 · truth — BodyRadiusPerSize has three owners and they already disagree — the SO's own C# default is 0.575 while code, asset and guard test all say 0.3

`Assets/_Project/Scripts/Data/Definitions/SimTuningConfig.cs:17` · линза `uncovered-assemblies`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Owner 1 `Core/Simulation/SimTuning.cs:79`: `bodyRadiusPerSize: 0.3f`. Owner 2 `Data/Definitions/SimTuningConfig.cs:17`: `[SerializeField] private float _bodyRadiusPerSize = 0.575f;` with the tooltip on line 16 asserting "Size 1.0 → 0.575 (диаметр 1.15)". Owner 3 the played asset `ScriptableObjects/Configs/SimTuningConfig.asset:15`: `_bodyRadiusPerSize: 0.3`. The class docstring (SimTuningConfig.cs:10) claims "Дефолты полей = SimTuning.Default — при рассинхроне падает тест-страховка" — false: `ConfigValidationTests.SimTuningConfig_MatchesCodeDefaults` (Tests/EditMode/Content/ConfigValidationTests.cs:23-28) loads the ASSET via `ToSnapshot()` and compares it to `SimTuning.Default`; it never reads the field initialiser, so the drift is invisible to it. A fourth reader takes the wrong owner entirely: `Presentation/UnitView.cs:1076` draws the sim-collision debug disc from `SimTuning.Default.BodyRadiusPerSize` instead of the live snapshot.
```

**Чем стреляет.** Two concrete costs. (1) `AssetDatabase.FindAssets` in `LoadSingle<T>` asserts exactly one SimTuningConfig exists, so the moment anyone creates a second one via `Guildmaster/Config/Sim Tuning Config` it comes out at 0.575 — body radius nearly doubled, which changes separation, `CombatPositioning.BodyRadius` and therefore every melee reach — and the guard test reports "Ожидается ровно один ассет" rather than the drift. (2) The inspector tooltip a designer reads while tuning states a diameter (1.15) that is 92% larger than what the game actually plays (0.6). (3) The moment the asset is tuned away from 0.3, UnitView's collision gizmo silently keeps drawing the old radius — the debug tool lies about the sim it exists to visualise.

**Куда править.** Drop every field initialiser in SimTuningConfig (a serialized field's initialiser only ever affects freshly created assets, which is exactly the drift vector) and fix the tooltips to stop quoting numbers; leave `SimTuning.Default` as the single code owner and let the guard test keep pinning asset==Default. Change UnitView.cs:1076 to read the injected/live `SimTuning` snapshot.

---

### UA-4 · P1 · legacy — A completed one-shot migration still sits on an Alebardium menu item and would destroy current balance if clicked

`Assets/_Project/Scripts/Data/Editor/Migrations/Phase4Package3StatsBaseMigration.cs:33` · линза `uncovered-assemblies`

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
`[MenuItem("Alebardium/Data/Migrations/Phase 4 - Package 3 (StatsConfig base + relic diffs)", priority = 422)]` on a class whose own docstring says "ВЫПОЛНЕНО: пакет 3. Оставлена по правилу 0.8; не удалять." (line 15). `Run()` does two irreversible writes with no Undo and no confirmation dialog: `WriteStatsConfigDefaults` (line 44) overwrites `StatsConfig._defaults` with a hardcoded six-stat table (`MaxHP 120f`, line 23), and `RewriteRelicsAsDiffs` (line 64) walks every UnitData via reflection on the private `_stats` field and rewrites each Flat modifier as `m.Value - base` (line 82), then `AssetDatabase.SaveAssets()` (line 39).
```

**Чем стреляет.** The stat cascade has moved on since this ran: `ClassBalanceConfig.cs:26` now anchors MaxHP at `_baseHp = 2000f` via Override modifiers. Clicking this menu item today writes MaxHP 120 into StatsConfig and subtracts the hardcoded base a SECOND time from every relic's Flat stats — e.g. a relic whose Flat MaxHP is already a small diff becomes a large negative one. It is one mis-click in a menu the team uses daily (21 MenuItems all live under Alebardium), with no undo and no dry run.

**Куда править.** Delete the file. If the historical record matters, the git commit that ran it is the record; a live editor button is not documentation. If it must be kept, at minimum strip the [MenuItem] and put the body behind an explicit EditorUtility.DisplayDialog + a guard that refuses to run when ClassBalanceConfig exists.

---

### UA-5 · P1 · truth — HP/shield bar colour has four owners and the enemy colour already disagrees — the fallback paints the exact red the palette documents as rejected

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:431` · линза `uncovered-assemblies` · переоформляет R1-32

**Проверка скептиком:** ПОДТВЕРЖДЕНО → `P3` (уверенность high)

```csharp
Owner 1 (intended): `ScriptableObjects/Configs/CombatColorPalette.asset` — `_allyHp: {0.3, 0.85, 0.35}`, `_enemyHp: {1, 0.4, 0.13}`, `_shield: {0.62, 0.86, 1}`. Owner 2: the C# initialisers `Presentation/Design/CombatColorPalette.cs:22/26/32`. Owner 3: `Presentation/CombatPresenter.cs:431-433` `DefaultHealthColor(bool) => isAllyOfViewer ? new Color(0.30f,0.85f,0.35f) : new Color(0.90f,0.25f,0.25f)`, taken whenever the serialized `_colorPalette` (line 43) is null — a state the field's own tooltip (line 42) declares supported: "Пусто = фолбэк-цвета по умолчанию". Owner 4: `Prefabs/UI/HealthBar.prefab:264-265` — `_fallbackHpColor: {0.3, 0.85, 0.35}`, `_fallbackShieldColor: {0.62, 0.86, 1}`, backing `HealthBarView.cs:43-44`.
```

**Чем стреляет.** The ally colour is copied identically in four places, so a designer editing the palette silently gets the old colour on any scene where the presenter's palette reference is unset. Worse, the enemy colour is ALREADY inconsistent: the palette's asset value is vermilion `(1, 0.4, 0.13)` and its docstring (CombatColorPalette.cs:24-25) spends three lines justifying vermilion specifically because plain red collides with the red unit bodies — while the fallback at CombatPresenter.cs:433 hardcodes `(0.90, 0.25, 0.25)`, plain red. The fallback path therefore reintroduces the readability bug the palette was created to fix, and the shield fallback path skips SetShieldColor entirely (CombatPresenter.cs:259-260 is guarded by `_colorPalette != null`), so the prefab value wins.

**Куда править.** Make the palette SO the only owner: delete `DefaultHealthColor`, delete `HealthBarView._fallbackHpColor`/`_fallbackShieldColor` (and the matching prefab entries) and `ManaBarView._fallbackFillColor`, and make the missing palette reference a loud failure (assert in CombatPresenter's Start) rather than a silent second palette. Also drop the now-unused `CombatColorPalette.AllyHp`/`EnemyHp` getters (lines 34-35) — `HealthBarColor` is the only used accessor.

---

### AC-10 · P2 · dead — Dead authoring surface on content SOs, including authored ThreatPoints values nothing reads and a validation rule that does not exist

`Assets/_Project/Scripts/Data/Definitions/EnemyData.cs:29` · линза `assets-vs-code`

```csharp
EnemyData.cs:29-30 `public int ThreatPoints => _threatPoints;` / `public int GoldBounty => _goldBounty;` — repo-wide grep for both identifiers returns only these two declaration lines, no reader in Scripts, Tests or Editor tooling. The data is authored: GoblinGrunt.asset:64 `_threatPoints: 1`, GoblinArcher.asset:70 `_threatPoints: 2`, GoblinWarrior.asset:64 `_threatPoints: 3`, while `_goldBounty: 0` on all five enemies. Same for RelicData.cs:38-39 `public EffectData[] RunEffects => _runEffects;` / `public AIPresetData[] AltAiPresets => _altAiPresets;` — zero readers, `_runEffects: []` and `_altAiPresets: []` in all 11 relic assets. EncounterData.cs:71 `public EncounterTier Tier => _tier;` has exactly one reader, a dev label: DevBattlePickerView.cs:57 `$"{Short(e.Id)}  ·  {e.Tier}"`. Its tooltip (EncounterData.cs:60) promises `"Special — только из ивентов (валидация §8.9)"`; grepping `Special` across Scripts and Tests returns only the enum member (EncounterData.cs:16) and that tooltip.
```

**Чем стреляет.** ThreatPoints is the worst of these: a designer has filled in 1/2/3 across the enemy roster, so the field reads as a live encounter-budget input, and the class docstring reinforces it (EnemyData.cs:6-7 'мета врага (очки опасности, награда) … для превью боя и бюджетов генерации'). Nothing consumes it, so map generation and encounter selection ignore difficulty entirely — DummyTrio and GoblinRaid are equally likely on floor 1. The claimed §8.9 validation for EncounterTier.Special is a safety net that was never built, so a Special encounter authored today would silently enter the normal pool. GoldBounty is 0 everywhere and unread, i.e. two owners of 'gold per battle' with one of them permanently inert next to GameConfig.BattleGoldReward.

**Куда править.** Delete ThreatPoints, GoldBounty, RunEffects, AltAiPresets and their serialized fields, plus EncounterTier.Special and the tooltip that advertises a rule nobody wrote — or implement one consumer each and the missing validation. If encounter difficulty is genuinely wanted, ThreatPoints should feed NodeResolver.PickBattlePreset, which currently picks uniformly at random; that is the single owner the data is waiting for.

---

### AC-11 · P2 · dead — MapLayout's jitter and relax machinery is dead by a decision the comment records: both the asset and the code default are zero

`Assets/_Project/Scripts/Presentation/Map/MapLayout.cs:80` · линза `assets-vs-code`

```csharp
MapStyle.asset:15-21 `_layout: StepX: 6.5 / StepY: 4.2 / JitterY: 0 / JitterX: 0 / MinDistance: 2.2 / RelaxIterations: 10`, matching MapLayout.cs:45-53 `JitterY = 0f, JitterX = 0f`. MapStyle.asset is the only MapStyle asset in the project. MapLayout.cs:40-42 records the decision: `"Дефолты (Макс, 2026-07-20, второй раунд): РОВНАЯ сетка … Разброс убран целиком"`. Lines 80-81 are the only callers of Hash and Signed: `x += Signed(Hash(n.Id, seed, 0)) * JitterX * StepX;` / `y += Signed(Hash(n.Id, seed, 1)) * JitterY * StepY;`. Hash is a 15-line FNV-1a (MapLayout.cs:136-149) with its own comment about cross-run stability, Signed is :150.
```

**Чем стреляет.** Multiplying by JitterX/JitterY = 0 makes both hashes and the whole `long seed` parameter of Resolve inert — WorldMapView passes a run seed that provably cannot move a pixel. Relax (MapLayout.cs:96-131, ~35 lines with an O(n²) loop over 10 iterations) is equally unreachable: with jitter zero the closest any two nodes get is StepY = 4.2 within a floor and StepX = 6.5 across floors, both above MinDistance = 2.2, so `if (sqr >= minSqr) continue;` always continues and no node is ever pushed. Nine of the ten MapLayoutTests exercise `MapLayout.Default`, so the tests confirm the geometry rather than the machinery. A reader tuning map spread will edit Jitter fields, see nothing happen, and go looking for a bug elsewhere.

**Куда править.** Delete JitterX, JitterY, Hash, Signed, the `long seed` parameter and its call-site threading, and MinDistance/RelaxIterations/Relax — the decision to remove spread was made and written down; the code is what is left over from it. Drop the corresponding four lines from MapStyle.asset's `_layout` block. If spread ever returns it belongs behind a non-zero default, not behind a field that is zero in the only asset that exists.

---

### AC-12 · P2 · dead — Dead assets in the repo: two unreferenced UnitVisual SOs, an abandoned baked font atlas, and eight unreferenced .ttf files

`Assets/_Project/ScriptableObjects/Visuals/FantasyWarrior.asset:13` · линза `assets-vs-code`

```csharp
Sweeping every guid under ScriptableObjects/UI/Prefabs against every text file in Assets/, ProjectSettings/ and Packages/: FantasyWarrior.asset (`m_Name: FantasyWarrior`, Guildmaster.Presentation.UnitVisual, with four AnimationClip references at :15-18) and ForestMushroom/ForestMushroom.asset plus its ForestMushroom.overrideController have zero referrers — while the other nine UnitVisual assets each resolve to relics or enemies (MedievalWarrior.asset → 4 assets, GoblinFighter → 3, WizardPack → 2, …). On the font side, components.uss references exactly two atlases — :1106 `-unity-font-definition: url("../Fonts/FiraSans-Regular SDF.asset");` and :1112/:2181 `url("../Fonts/CormorantGaramond-Medium SDF.asset")` — leaving `FiraSans-Bold SDF.asset` unreferenced, along with eight .ttf files nothing points at: CormorantGaramond.ttf, CormorantGaramond-SemiBold.ttf, Forum-Regular.ttf, Handjet.ttf, PTSans-Bold.ttf, PTSans-Regular.ttf, PixelifySans.ttf, YesevaOne-Regular.ttf.
```

**Чем стреляет.** Each dead UnitVisual keeps a whole animation-clip chain alive behind it, so the two orphans are the only reason their source art survives an unused-asset pass — and a reader browsing ScriptableObjects/Visuals cannot tell FantasyWarrior (dead) from MedievalWarrior (used by relic.base, TrainingDummy, Druid and WhirlMonk) without a guid sweep. FiraSans-Bold SDF is a baked atlas from an abandoned bold-face decision: Assets/_Project/UI/AGENTS.md:25 states the shipped pair is `"Тело — Fira Sans, заголовки — Cormorant Garamond Medium"`, and UITK synthesises the bold weight the theme actually uses. The eight loose .ttf files are font-shopping leftovers; per the project's own static-bake rule fonts enter the build only through a TextCore atlas, so these can never be used as they stand and only cost repo weight and reviewer attention.

**Куда править.** Delete FantasyWarrior.asset, ForestMushroom/ (asset + overrideController) and any clips/sprites left unreferenced afterwards; delete FiraSans-Bold SDF.asset and the eight unreferenced .ttf files, keeping only FiraSans-Regular.ttf and CormorantGaramond-Medium.ttf as the sources of the two live atlases. Then add the guid sweep as an editor utility under the Alebardium menu root so the next orphan is found by a command rather than by an audit.

---

### AC-17 · P2 · architecture — GameConfig.asset was never re-saved after the economy block was added: it holds 7 of 21 fields, and the one economy value it does hold disagrees with the code

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20` · линза `assets-vs-code`

```csharp
The live asset (CoreScene.unity:291 "_gameConfig: {fileID: 11400000, guid: 3f479309a3dcb31429f94c65a88fb275}") contains exactly seven fields, ending at line 21 "_relicCapacityMax: 16". Absent from the file entirely: _localPlayerTeam, _partyBannerSlots, _startGold, _battleGoldReward, _priceCommon, _priceCursed, _priceDivine, _priceSpread, _sellPercent, _shopRerollCost, _restartsPerAct, _guildSize, _startingRelicId — every economy and guild number the run reads (RunStateService.cs:42-43, 59-60, 93, 120; RelicPricer.cs:26-35; ShopController.cs:46). The one economy-adjacent value that IS authored already disagrees: GameConfig.asset:20 "_relicCapacityBase: 12" against GameConfig.cs:34 "[SerializeField] private int _relicCapacityBase = 8;". Nothing pins the asset: every test builds its own config — RunStateSaveTests.cs:141 "var config = ScriptableObject.CreateInstance<GameConfig>(); // PartyBannerSlots = 2", RunStateRestartTests.cs:23, GuildRosterTests.cs:38, ShopControllerTests.cs:84 — and BattleNodeFlowTests.cs:45 even asserts "+20 (GameConfig.BattleGoldReward код-дефолт)".
```

**Чем стреляет.** This is the project's own HARD rule inverted: the asset is what the game plays, yet for 13 of 21 knobs there is nothing in the asset to play and the numbers live only in C#, while the single field somebody did touch in the Inspector has silently drifted 12-vs-8. A designer opening GameConfig and tweaking Start Gold changes the asset; a programmer reading GameConfig.cs sees 100 and believes it; the tests certify the programmer's view. RelicCapacityBase 12 also costs four of the shop's capacity upgrades (RunStateService.cs:172 caps at RelicCapacityMax 16).

**Куда править.** Re-save GameConfig.asset so every serialized field is authored on disk, decide whether base relic capacity is 8 or 12 and make the other side follow, and add an asset-loading guard test (like ConfigValidationTests.LoadSingle) instead of CreateInstance in the economy tests.

---

### AC-18 · P2 · architecture — BodyRadiusPerSize has three owners with two different values, and the guard test is aimed at the pair that agrees

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/SimTuningConfig.cs:17` · линза `assets-vs-code`

```csharp
Three declarations of the same number: SimTuningConfig.cs:16-17 "[Tooltip(\"Радиус тела = Size × это (мировые ед.). Size 1.0 → 0.575 (диаметр 1.15).\")] [SerializeField] private float _bodyRadiusPerSize = 0.575f;", SimTuning.cs:79 "bodyRadiusPerSize:         0.3f," and SimTuningConfig.asset:15 "_bodyRadiusPerSize: 0.3". The class docstring claims they are one ("Дефолты полей = SimTuning.Default — при рассинхроне падает тест-страховка"), but ConfigValidationTests.cs:28 compares the ASSET to SimTuning.Default — the two that match — so the SO's own initializer is unguarded. The 0.575 has already leaked into a test's reasoning: SpearmanSliceTests.cs:53 "// Длина линии = досягаемость с учётом тел (AttackRange + радиусы обоих тел ≈ 5 + 0.575·2 ≈ 6.15)."
```

**Чем стреляет.** Creating a SimTuningConfig from the CreateAssetMenu entry yields an asset that instantly fails ConfigValidationTests, with the failure pointing at balance drift rather than at a stale initializer. Worse for design: the Inspector tooltip tells the designer bodies are 1.15 units across while the game separates them at 0.6 — half — and body radius feeds separation, spearman line reach (CombatPositioning.cs:28) and the deployment pick radius (DeploymentController.cs:632).

**Куда править.** Make the serialized field initialize from SimTuning.Default (or drop the initializer), correct the tooltip to the value the game plays, and extend ConfigValidationTests to compare a freshly CreateInstance'd SimTuningConfig against SimTuning.Default so the third owner is covered.

---

### AC-19 · P2 · dead — EffectData ships two authoring fields for a buff bar that does not exist; all 22 effect assets leave them empty

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/EffectData.cs:45` · линза `assets-vs-code`

```csharp
EffectData.cs:43-47 — "[Header(\"Presentation / info\")] [Tooltip(\"Иконка для бафф-бара HUD (опциональна: у скрытых/технических эффектов пустая).\")] [SerializeField] private Sprite _icon;" and "[Tooltip(\"Информационные теги для тултипов.\")] [SerializeField] private TagData[] _infoTags;". Repo-wide there is no buff bar: grepping Scripts+UI for buff-bar/BuffBar/«бафф-бар» hits only EffectData.cs itself and ContentLocalization.cs. `.Icon` on an EffectData has exactly one reader, the editor-only ContentLocalization.cs:55 "return def is EffectData e && e.Icon != null ? NameAndDesc : NameOnly;"; `.InfoTags` on an EffectData has none at all (the InfoTags readers all go through UnitData). Every one of the 22 assets under ScriptableObjects/Effects has "_icon: {fileID: 0}" and an empty _infoTags.
```

**Чем стреляет.** The empty icon quietly rewrites the loc contract: because Icon is null everywhere, RequiredSuffixes returns NameOnly, so 21 of 22 effects are never required to have a .desc key — the validation reports green while every effect description is missing. Anyone assigning an icon to make the effect readable would instead trip 21 new missing-key failures and still see nothing in game.

**Куда править.** Delete _icon and _infoTags from EffectData (and the Icon branch in ContentLocalization.RequiredSuffixes) until a buff bar exists; decide the .desc policy on something other than whether a sprite happens to be assigned.

---

### AC-20 · P2 · convention — The run topbar ships two hardcoded placeholder labels — the studio name as the guild name — that no code ever binds

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/UI/Screens/RunModeBar.uxml:9` · линза `assets-vs-code`

```csharp
RunModeBar.uxml:9-10 — "<ui:Label name=\"guild-name\" text=\"Alebardium\" class=\"gm-loadout__guild-name\" />" and "<ui:Label name=\"guild-asc\" text=\"· ASC IV\" class=\"gm-loadout__guild-asc\" />". The file's own header comment (line 6) promises "Строки-плейсхолдеры RU; локализованные проставляет RunModeBarView (ключи ui.*)", but RunModeBarView.Bind queries only topbar-gold, topbar-act, topbar-floor, topbar-timer, battle-timer, topbar-hp, btn-start, btn-menu and "mode-"+key (RunModeBarView.cs:40-72) — "guild-name" and "guild-asc" appear nowhere else in the repo.
```

**Чем стреляет.** The topbar is on screen for the whole run, so every player sees the studio name presented as their guild and a fabricated ascension rank; both strings are also raw literals in markup, which the project's HARD rule forbids for player-facing text. The comment above them asserts a code owner that does not exist, so the defect reads as 'already handled'.

**Куда править.** Either bind both labels in RunModeBarView from RunState through ui.* keys, or delete the two Labels until the guild-identity and ascension features exist.

---

### AC-21 · P2 · dead — Five effect behaviour components are reachable only from tests — no asset references them and no game code constructs them

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Effects/Components/ThornsComponent.cs:18` · линза `assets-vs-code`

```csharp
Extracting every [SerializeReference] managedReference type name from the 22 effect assets yields 14 distinct components; ThornsComponent, ShieldComponent, PeriodicHealComponent, LifestealComponent and DispelComponent are not among them. Outside their own files their only mentions are `new X()` inside Tests/EditMode (EffectComponentTests.cs:114 "new ShieldComponent().With(\"_amount\", new ScalableValue(50f))", ReactiveEffectTests.cs:29, EffectDispelTests.cs:94, EffectComponentTests.cs:94) plus prose in other components' docstrings. ThornsComponent is explicitly superseded — ArmorThornsComponent.cs:10-12 says "Урон шипов масштабируется от БРОНИ носителя (а не от доли полученного удара, как <see cref=\"ThornsComponent\"/>) — по карточке ГДД «100% статы брони»" — and it is ArmorThornsComponent that Effects/SpikedTree.asset actually carries.
```

**Чем стреляет.** Two near-identical thorns implementations sit side by side with only a docstring saying which one the design chose; the next reader wiring a thorns relic has a 50% chance of picking the one no content uses and whose ping-pong semantics the design rejected. The other four are green-tested code paths the shipped game never executes, which inflates the apparent coverage of the effect system.

**Куда править.** Delete ThornsComponent and its tests outright (ArmorThornsComponent is the decided design); for Shield/PeriodicHeal/Lifesteal/Dispel either author the content that uses them or delete them with their tests.

---

### AC-22 · P2 · architecture — The attack-speed ceiling has three owners: the asset says 4, the SO initializer and the auditor fallback both say 2.5

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:17` · линза `assets-vs-code`

```csharp
StatsConfig.asset:16-17 "_attackSpeedMin: 0.1 / _attackSpeedMax: 4" against StatsConfig.cs:22-23 "[SerializeField] private float _attackSpeedMin = 0.1f; [SerializeField] private float _attackSpeedMax = 2.5f;" and a third copy in ContentAuditor.cs:44 "float asMax = config != null ? config.AttackSpeedMax : 2.5f;". The only test that touches the pair does not pin either value — ContentValidationTests.cs:133 "Assert.Less(cfg.AttackSpeedMin, cfg.AttackSpeedMax, …)" is satisfied by any ordered pair.
```

**Чем стреляет.** The balance auditor's outlier column — the tool the designer trusts to say 'this kit is over the ceiling' — reports against 4 att/s, while both places a programmer would read say the ceiling is 2.5; a 60% difference in the same documented invariant. StatType.cs:26 further advertises "AttackSpeed = 8, // [Ф1] атак/сек, клампится из StatsConfig", so the number also documents a clamp that will be applied against whichever owner wins whenever it is finally wired.

**Куда править.** Pick one ceiling, write it into the asset, initialize the SO field from it (or delete the initializer), delete the 2.5f fallback in ContentAuditor, and pin the asset value in ContentValidationTests.

---

### AC-23 · P2 · dead — Eight more content-SO fields are authored or serialized with no reader anywhere in the project

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/EncounterData.cs:73` · линза `assets-vs-code`

```csharp
A whole-repo pass over every public accessor in Scripts/Data/Definitions, counting references outside the declaring file, turns up (beyond the ThreatPoints case I reported in round 1): EncounterData.cs:69/73 "_arenaId" / "public string ArenaId => _arenaId;" — serialized as "_arenaId: " in all 8 encounter assets and read by nobody, so encounters cannot pick an arena; EnemyData.cs:30 GoldBounty (authored 0 in all 5 enemies, gold comes from GameConfig.BattleGoldReward); RelicData.cs:38 RunEffects ("_runEffects: []" in all 11 relics); ContentDatabase.cs:27 SchemaVersion ("_schemaVersion: 1" in the asset, never read — the same shape of dead versioning as R1-53); ClassBalanceConfig.cs:34/36 BaseHp and BaseMoveSpeed; AIPresetData.cs:20 ArchetypeTags ("_archetypeTags: []" in all 13 presets); UnitVisual.cs:58 HasClips; GameConfig.cs:77 DefaultLocale, whose only reference in the entire repo is the test that guards it — ConfigValidationTests.cs:74 "Assert.IsFalse(string.IsNullOrEmpty(g.DefaultLocale), \"GameConfig.DefaultLocale пуст.\")".
```

**Чем стреляет.** Each is an Inspector row that invites authoring and does nothing with it; ArenaId is the worst because an encounter designer will fill it in and get the default arena. DefaultLocale is actively misleading — a passing test makes it look like the boot locale is wired, so nobody checks why the game does not honour it while RU is the only filled table.

**Куда править.** Delete each field and accessor (and the DefaultLocale assertion) unless it is about to be consumed; for ArenaId and DefaultLocale decide which, since both have an obvious intended consumer that was never written.

---

### AC-9 · P2 · dead — The item/banner layer is a fully built seam nothing ever fills, down to three item assets no code path can reach

`Assets/_Project/Scripts/Guild/RunStateService.cs:257` · линза `assets-vs-code`

```csharp
The three ItemData assets (item.oaken_charm, item.swift_boots, item.war_banner) are referenced by exactly one file — ContentDatabase.asset, the registry that indexes everything; I resolved each guid across the tree. No BattlePresetData asset has an `_items` or `_partyItems` line, so RuntimeUnitFactory.RegisterItemPassives (RuntimeUnitFactory.cs:117) always receives an empty list. The write API — RunStateService.cs:257 `TryAddVesselItem`, :268 `RemoveVesselItem`, :285 `TryAddBanner`, :298 `RemoveBanner`, plus `MaxVesselItems` (:246) and `MaxPartyBanners` (:279) — has callers only in Assets/_Project/Tests/EditMode/Run/RunStateSaveTests.cs (lines 118-154); MaxVesselItems and MaxPartyBanners have no callers at all. And the six item loc keys are the only content keys with an absent RU value: item.oaken_charm.name/.desc, item.swift_boots.name/.desc, item.war_banner.name/.desc are all missing from Content_ru.asset (species.goblins.desc is the seventh).
```

**Чем стреляет.** Nothing in the shop, camp, reward or event flow grants an item or a banner, so `RosterSlot.VesselItemIds` and `RunState.PartyItemIds` are permanently empty arrays, GameConfig._vesselItemSlots (3) and _partyBannerSlots gate nothing, and LoadoutHubView's banners column with its ui.hub.banners key renders an always-empty section. The missing RU strings prove it: nobody has ever seen these items on screen, because no path puts them there. A reader who finds RegisterItemPassives, the equip API and the loc keys will reasonably assume items work.

**Куда править.** Pick one: wire a grant path (shop stock / camp action / reward slot) and fill the six RU strings, or delete the layer — the three assets, RunStateService's six item/banner methods, RegisterItemPassives, the two GameConfig slot fields, the hub banners column and its key. Leaving it half-built is what makes the next reader wire a UI against an inventory that can never be non-empty.

---

### BE-10 · P2 · dead — The bar shader's Shader.Find fallback is both dead and broken: prefabs always supply the material, and SegmentedHealthBar is not in Always Included Shaders

`Assets/_Project/Scripts/Presentation/HealthBarView.cs:31` · линза `build-vs-editor`

```csharp
HealthBarView.cs:30-33 tooltip: «Пусто → Shader.Find (для билда шейдер должен быть Always Included).» Implementation HealthBarView.cs:82-88: `if (_barMaterial != null) _mat = new Material(_barMaterial); else { Shader sh = Shader.Find(ShaderName); if (sh != null) _mat = new Material(sh); }` with `ShaderName = "Guildmaster/UI/SegmentedHealthBar"` (line 24). ManaBarView.cs:19/25-26/63-69 repeats the identical pattern verbatim.
The stated build requirement is not met: ProjectSettings/GraphicsSettings.asset:30-39 `m_AlwaysIncludedShaders` holds nine entries, of which only two are project shaders — guid `4189084331317b64d8eaf9c54ddf8ab9` = Art/Shaders/SH_Sprite_Shatter.shader (line 38) and one further guid. `Assets/_Project/Art/Shaders/SegmentedHealthBar.shader` is absent from the list.
The fallback is also unreachable: both bar components ship as nested prefab instances of UI/HealthBar.prefab and UI/ManaBar.prefab (UnitView.prefab:762 `m_SourcePrefab: {guid: cffa6693769cc314a98167d4cbf01187}`, :681 `{guid: 914bb8f630f5cfc4e965296b85274c21}`), and those sources assign the material — HealthBar.prefab:261 `_barMaterial: {fileID: 2100000, guid: 87850d551b3fed04383a71ecc12dd629, type: 2}`, ManaBar.prefab:261 `_barMaterial: {fileID: 2100000, guid: 33ed55e40cb0c2c4f85a503c1d687609, type: 2}`. Neither prefab instance overrides the field to null.
```

**Чем стреляет.** A guarded-looking safety net that has never executed and would fail if it ever did. Clear `_barMaterial` on HealthBar.prefab and the editor still works (Shader.Find sees every shader in the project), then the build ships with `_mat == null`, `PushDynamicProps` returns at HealthBarView.cs:191, and every unit's HP/shield bar renders as an unshaded white Image — a defect that reproduces only in the player. As written the code invites exactly that: the tooltip tells an author it is safe to leave the slot empty.

**Куда править.** Delete the `Shader.Find` branches in HealthBarView.EnsureMaterial (HealthBarView.cs:85-88) and ManaBarView.EnsureMaterial (ManaBarView.cs:65-69) plus the `_fallbackHpColor`/`_fallbackShieldColor`/`_fallbackFillColor` fields they exist to serve, and log an error when `_barMaterial` is null so a broken prefab fails loudly in both editor and build. Also hoist the duplicated `ShaderName` literal (HealthBarView.cs:24, ManaBarView.cs:19) to one constant. If the fallback is kept instead, add SegmentedHealthBar.shader to GraphicsSettings' Always Included list so the promise in the tooltip is true.

---

### BE-11 · P2 · gap — CI never builds a player, so every editor-versus-build defect above is structurally invisible to the pipeline that gates merges

`.github/workflows/ci.yml:27` · линза `build-vs-editor`

```csharp
.github/workflows/ci.yml declares exactly three jobs — `changes:` (line 10), `test:` (line 27), `ci-gate:` (line 91). The `test` job runs `game-ci/unity-test-runner@v4` twice, `testMode: editmode` and `testMode: playmode`. There is no `game-ci/unity-builder` step, no `BuildPipeline` invocation, no artifact other than `test-results/`.
The gate asserts nothing about buildability: ci.yml lines 100-106 — `if [ "${{ needs.changes.outputs.code }}" = "true" ] && [ "${{ needs.test.result }}" != "success" ]; then … exit 1; fi`.
CLAUDE.md:128 describes the same file as `.github/workflows/ci.yml  # GameCI pipeline (тесты + сборка)` and CLAUDE.md:140 as «Сборки и тесты через GameCI (GitHub Actions).»
The only player build in the repo is a hand-run editor menu item: EditorTools/UI/TestPlayMenu.cs:24-47, `[MenuItem("Alebardium/Test/Build & Run (Windows, fullscreen)")]`, and it builds with `options = BuildOptions.Development | BuildOptions.AutoRunPlayer` — a development build, so `DEVELOPMENT_BUILD` is defined and nothing exercises release-define behaviour either.
Both test assemblies are editor-bound: Tests/EditMode/Guildmaster.Tests.EditMode.asmdef:22 `"includePlatforms": ["Editor"]`, and Tests/PlayMode runs inside the editor, not a player.
```

**Чем стреляет.** Every finding in this report — a null `_statsConfig` in CoreScene, an unwired `_campScreen`, an orphaned ActConfig, a locale chain that resolves differently on an English OS, an assembly shipping for nothing — is a property of the built player or of scene asset wiring, and none of them can fail a job that only runs editor tests. The pipeline has never once compiled the thing players run, so a change that breaks the build (a runtime assembly referencing an Editor-only type, a stripped shader, a missing scene) merges green and is discovered by hand at `Alebardium/Test/Build & Run`. CLAUDE.md's claim that the pipeline builds makes the gap worse: the one document an agent reads first says the coverage exists.

**Куда править.** Add a `build` job to ci.yml using `game-ci/unity-builder@v4` with `targetPlatform: StandaloneWindows64` (no `BuildOptions.Development`), and add it to `ci-gate`'s `needs` so a build failure blocks the merge. Correct CLAUDE.md:128 and :140 to describe what the pipeline actually does. Longer term, the scene-wiring class of defect wants a cheap EditMode guard that opens each scene in EditorBuildSettings and asserts no serialized reference on the bootstrap components is null — that would have caught findings 1, 2 and 3 in one test.

---

### BE-6 · P2 · dead — UnityAudioService is a Debug.Log-only IAudioService that is never registered — a fallback that looks like safety and cannot run

`Assets/_Project/Scripts/Game/Services/UnityAudioService.cs:10` · линза `build-vs-editor`

```csharp
UnityAudioService.cs:7-19: «Заглушка IAudioService на Unity Audio. Фаза 1 — только Debug.Log.» / `public sealed class UnityAudioService : IAudioService` / `Debug.Log($"[UnityAudioService] - Play: {soundKey}");`
Proof of death: repo-wide grep for `UnityAudioService` over every file type in Assets and ProjectSettings returns four hits — three inside UnityAudioService.cs itself, and one doc reference in FmodAudioService.cs:13 («тот же приём, что <see cref="UnityAudioService"/>»). No scene, prefab, asset or asmdef mentions it.
The single binding is unconditional: RootLifetimeScope.cs:74 — `builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>();` — with no `#if`, no platform branch and no null-catalog alternative (the catalog itself is defended one line earlier at RootLifetimeScope.cs:72).
```

**Чем стреляет.** Reading RootLifetimeScope, the presence of two IAudioService implementations reads as 'FMOD in the real build, Unity Audio when banks are missing'. There is no such switch. So the FMOD path is the only path, and its failures are swallowed rather than degraded — FmodAudioService.cs:48 already catches every exception bare (R1-70). Anyone chasing silent audio will spend time looking for the stub selector that does not exist, and anyone adding a headless/CI audio path will assume it is already wired.

**Куда править.** Delete UnityAudioService.cs and its .meta, and drop the `<see cref="UnityAudioService"/>` reference from FmodAudioService.cs:13. If a null implementation is genuinely wanted for tests or bank-less boots, register it explicitly behind a condition in RootLifetimeScope so the seam is visible at the registration site.

---

### BE-7 · P2 · dead — CameraModeController.SetDevAccess/DevAccess have no callers, and the Dev camera is gated on Application.isEditor — the whole dev-camera branch is unreachable in a build

`Assets/_Project/Scripts/Presentation/Camera/CameraModeController.cs:130` · линза `build-vs-editor`

```csharp
CameraModeController.cs:128-130: «В редакторе dev-камера доступна сразу (удобно тестить); в билде — гейтед, выдаётся через gm_cam_dev (вики «16» §6).» / `_devAccess = Application.isEditor;`
CameraModeController.cs:197-206: `/// <summary>Выдать/забрать доступ к dev-камере (QFSW: gm_cam_dev)…` / `public void SetDevAccess(bool granted)`.
Proof of death: repo-wide grep for `gm_cam_dev|SetDevAccess|DevAccess` over Assets returns only three hits inside CameraModeController.cs itself (lines 106, 129, 198) — the rest are docs. The three QFSW command files (DevTools/GuildmasterCommands.cs, DevTools/MapDevCommands.cs, DevTools/VisualFxCommands.cs) declare no `gm_cam_dev` command, so nothing can ever flip the flag.
`_devAccess` is read in exactly one place — CameraModeController.cs:331 `_mode = NextMode(_mode, _devAccess);` with NextMode at :335-344 returning `devAccess ? CameraMode.Dev : CameraMode.Action` from Overview.
```

**Чем стреляет.** In a player build `Application.isEditor` is false and no code path can grant access, so Tab cycles Action↔Overview only and `CameraMode.Dev` is dead: the serialized `_devCam` vcam slot, `_devMaxZoom` (line 52), `_devPanSpeed` (line 63), the `case CameraMode.Dev:` in Update (line 365) and the whole unclamped `DriveManual(..., clampToZone: false)` branch never execute. Meanwhile the public `SetDevAccess`/`DevAccess` pair reads as a wired capability gate that QA can toggle in a real build; it is a no-op API with zero callers. The concrete cost: any camera bug reproduced in the editor's Dev mode is unreproducible in the build QA is testing, and the 'grant access' lever named in the docstring does not exist.

**Куда править.** Either implement the gate — add a `gm_cam_dev` [Command] in DevTools/VisualFxCommands.cs that resolves CameraModeController and calls SetDevAccess, and drop the `Application.isEditor` shortcut so editor and build behave identically — or delete `SetDevAccess`, the `DevAccess` getter, `_devCam`, `_devMaxZoom`, `_devPanSpeed`, the `CameraMode.Dev` enum member and its Update/NextMode branches, and remove the gm_cam_dev promise from the docstring.

---

### BE-8 · P2 · dead — Guildmaster.Balance compiles into the player build for one editor-only ScriptableObject that has zero assets, taking a bench and a menu item down with it

`Assets/_Project/Scripts/Balance/Guildmaster.Balance.asmdef:8` · линза `build-vs-editor`

```csharp
Guildmaster.Balance.asmdef:8-9 — `"includePlatforms": []`, `"excludePlatforms": []` (all platforms, i.e. shipped). Its only referencers across all 21 asmdefs are Editor-only ones: Guildmaster.Balance.Editor (includePlatforms `["Editor"]`) and Guildmaster.Balance.Tests (`["Editor"]`, defineConstraints `UNITY_INCLUDE_TESTS`). No runtime assembly lists it.
The assembly contains exactly one file: Balance/BalanceScenarioData.cs (51 lines). Its own docstring at :10-12 admits the arrangement — «Гоняется editor-раннером (Guildmaster.Balance.Editor) — SO лежит в runtime-сборке только чтобы ассеты сериализовались.»
Proof the content is empty: BalanceScenarioData's script guid `d896b3dbdd3ef594a952cfa527d0e065` is referenced by no `.asset` anywhere in Assets — not one BalanceScenario asset exists.
Consequently BalanceMenu.cs:39 `private static bool RunScenarioValidate() => Selection.activeObject is BalanceScenarioData;` can never return true, so the `Alebardium/Balance/Run Selected Scenario` item (BalanceMenu.cs:27) is permanently greyed out and ScenarioBench.cs (85 lines) is unreachable.
```

**Чем стреляет.** The justification in the docstring is wrong: an Editor-platform assembly serialises ScriptableObject assets perfectly well — being in a runtime assembly buys nothing here, it only ships a managed DLL and a CreateAssetMenu entry («Guildmaster/Balance/Scenario», BalanceScenarioData.cs:14) into the retail player. And the feature it exists for has never been used: zero scenario assets, so 136 lines of bench plus a menu entry sit permanently dead behind a validate that always fails. The trap for a future reader is the CreateAssetMenu — it advertises a working balance-scenario workflow whose runner has never executed.

**Куда править.** Move BalanceScenarioData.cs under Balance/Editor (or set `"includePlatforms": ["Editor"]` on Guildmaster.Balance.asmdef) so nothing balance-related ships. If no scenario asset is ever going to be authored, delete BalanceScenarioData.cs, ScenarioBench.cs, the `Run Selected Scenario` menu pair at BalanceMenu.cs:27-40 and the whole Guildmaster.Balance assembly, leaving the procedural DPS/survivability/duel benches that are actually driven.

---

### BE-9 · P2 · dead — SpawnUnitCommand has no callers and the sim's deferred-command queue is fed only by the dead Net assembly — the tick loop carries a pause-counter special case for a path that cannot run

`Assets/_Project/Scripts/Combat/Commands/SpawnUnitCommand.cs:6` · линза `build-vs-editor`

```csharp
SpawnUnitCommand.cs:6 `public sealed class SpawnUnitCommand : ICombatCommand` — repo-wide grep for `SpawnUnitCommand` returns hits only inside its own file. Not referenced by any script, test, scene, prefab or asset.
The two surviving commands are constructed in exactly two places: production — NetworkCommandRelay.cs:59-68 (`CommandType.Pause => new PauseCommand(targetTick)` … `_simulation.EnqueueCommand(command)`); tests — CombatSimulationTests.cs:108 and :122-123.
That production consumer cannot run: NetworkCommandRelay's script guid `d5c295c98501a84409171559b523d8ab`, FacepunchTransportBootstrap's `c459789c7b4121b4aacb49051575b0ed` and SimSyncProbe's `6a96136653c099243a3b67001aded853` appear in no `.unity` or `.prefab`; grep for `NetworkManager` across Assets/_Project/Scenes/*.unity returns nothing.
The cost inside the sim is concrete — CombatSimulation.cs:51 `private readonly List<ICombatCommand> _commandQueue`, :239 `ApplyDueCommands();`, :500 `public void EnqueueCommand(ICombatCommand command)`, and the special case at :241-248: «счётчик тиков продолжает идти, ПОКА в очереди есть команды — иначе ResumeCommand с будущим TargetTick никогда не наступит» / `if (_commandQueue.Count > 0) _currentTick++;`
BattleInputController.cs:51 records that the live path bypasses all of it: «MP-путь пойдёт через PauseCommand/ResumeCommand (NetworkCommandRelay) — здесь хост-локально.»
```

**Чем стреляет.** This reframes R1-63 ('the Guildmaster.Net assembly is unreferenced dead code') at a deeper root: the deadness is not confined to Net, it has been threaded into the deterministic sim. The 30 Hz tick loop carries a branch — advance _currentTick while paused iff the command queue is non-empty — that exists solely to let a future-tick ResumeCommand arrive, and no shipping code ever enqueues one. Meanwhile SpawnUnitCommand is dead even in tests. The cost lands on whoever next touches tick ordering or pause semantics: they must reason about, and preserve, a paused-tick-advance rule whose only justification is a network path that has never been wired.

**Куда править.** Delete SpawnUnitCommand.cs outright — `sim.EnqueueUnitSpawn` is already called directly. Then decide the seam once: if host-authoritative MP is the accepted model (per SimSyncProbe.cs:9-14 it is), delete Guildmaster.Net together with `_commandQueue`, `EnqueueCommand`, `ApplyDueCommands`, PauseCommand/ResumeCommand and the CombatSimulation.cs:241-248 special case, and move CombatSimulationTests' pause coverage onto `SetPaused`. If the queue is being kept deliberately, say so at CombatSimulation.cs:500 and put NetworkCommandRelay on a prefab so the path is at least exercised.

---

### C-05 · P2 · architecture — SettingsService opens its own second disk path, bypassing the ISaveService seam that documents itself as the single point between game and disk

`Assets/_Project/Scripts/Game/Services/SettingsService.cs:102` · линза `critic`

```csharp
File.WriteAllText(FilePath, JsonUtility.ToJson(model));
...
    private static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");
```

**Чем стреляет.** ISaveService.cs:7-9 states "Единственная точка между игрой и диском — реализация прячет бэкенд ... ES3 + Steam Cloud — отложенная замена за этим же интерфейсом". SettingsService is registered in the same container as ISaveService (RootLifetimeScope.cs:78 and :123) and could inject it, but re-implements the identical JsonUtility+persistentDataPath plumbing inline, and repeats the same ES3-not-reachable excuse in its own docstring. Concrete cost: when the ES3/Steam Cloud backend lands behind ISaveService as planned, run saves become cloud-synced and settings silently do not — a player on a second machine loses their volume/accessibility settings with no code change that would flag it. The two paths already diverge on error policy (LogWarning here vs LogError in JsonFileSaveService) and neither is atomic.

**Куда править.** Inject ISaveService into SettingsService and store PersistModel under key `"settings"`. Deletes ~25 lines and leaves exactly one place to swap the backend.

---

### C-06 · P2 · correctness — gm_sep_* mutate live simulation tuning without marking the battle TAINTED, the guarantee gm_tuning_rebake exists to enforce

`Assets/_Project/Scripts/DevTools/GuildmasterCommands.cs:209` · линза `critic`

```csharp
[Command("gm_sep_radius", "Радиус тела на единицу Size (live)")]
        public void SepRadius(float radiusPerSize)
        {
            if (_simulation == null) { ... return; }
            _simulation.Separation.BodyRadiusPerSize = Mathf.Max(0.01f, radiusPerSize);
```

**Чем стреляет.** SimTuning.cs:5-6 states the contract: "Из тика читается ТОЛЬКО этот снапшот — не SO (детерминизм: правка SO в play mode применяется к идущему бою лишь явным re-bake, помечающим бой tainted)", and gm_tuning_rebake honours it — its description says "(бой становится TAINTED)" and it logs "реплей невалиден" at line 514. The four gm_sep_* commands (209, 218, 227, 236) write the same four tuning knobs straight onto the live SeparationSystem and log nothing but the new values. A balance session where Max dials in separation by hand therefore produces a battle whose recorded metrics, checksum and any future replay are silently invalid — the exact failure the taint marker was added to prevent. Compounded by the finding above, these commands also ship to players.

**Куда править.** Route all four through the same code path as gm_tuning_rebake so they set the tainted flag and emit the same warning, or make SeparationSystem's knobs read-only outside a re-bake.

---

### C-08 · P2 · gap — The PlayMode tier — the only one that can exercise DI, MonoBehaviour lifecycle and UI navigation — references none of those assemblies and holds one test file

`Assets/_Project/Tests/PlayMode/Guildmaster.Tests.PlayMode.asmdef:4` · линза `critic`

```csharp
"references": [
    "UnityEngine.TestRunner",
    "Guildmaster.Core",
    "Guildmaster.Data",
    "Guildmaster.Combat",
    "Guildmaster.Game"
  ],
```

**Чем стреляет.** Cross-cutting filed the coverage gap against the EditMode asmdef, but EditMode is Editor-only and cannot run a real container or a real frame loop — PlayMode is the tier that can, and it references neither VContainer, MessagePipe, Guildmaster.UI, Guildmaster.Presentation nor Guildmaster.Guild, and contains exactly one file (BattleIntegrationTest.cs). Test files by area confirm the shape: Combat 37, Content 9, ContentHub 6, Guild 12, Run 5, Presentation 4, Core 3, UI 2. Every P0/P1 this audit surfaced lives in the untestable half — the topbar burying an awaited Page (UiRootBootstrap.cs:277), Push ignoring ScreenKind, the phantom enemy-less battle from formation mode, the persist-world phase machine — and each was found by a human or an LLM reading code because no tier can reach it. That is also why round-5 play-QA kept producing regressions in exactly this code.

**Куда править.** Add VContainer, MessagePipe, Guildmaster.UI, Guildmaster.Presentation and Guildmaster.Guild to the PlayMode asmdef and land two smoke tests first: (a) build RootLifetimeScope + WorldLifetimeScope and assert every registration resolves, (b) drive UiNavigator Push/Pop across ScreenKind and assert GameplaySuppressed and stack order. Both are cheap and both would have caught the P0.

---

### CD-10 · P2 · truth — GameConfig.asset carries 7 of the 20 serialized fields — the entire run economy is owned by C# field initializers, not by data

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:15` · линза `content-data-integrity` · переоформляет R1-50

```csharp
The whole asset body is GameConfig.asset:15-21: `_defaultMasterVolume`, `_defaultMusicVolume`, `_defaultSfxVolume`, `_defaultLocale`, `_vesselItemSlots`, `_relicCapacityBase: 12`, `_relicCapacityMax: 16`. Missing from the YAML entirely — i.e. supplied by the C# initializer at load — are `_localPlayerTeam`, `_partyBannerSlots`, `_startGold = 100`, `_battleGoldReward = 20`, `_priceCommon = 50`, `_priceCursed = 100`, `_priceDivine = 150`, `_priceSpread = 0.2f`, `_sellPercent = 0.25f`, `_shopRerollCost = 50`, `_restartsPerAct = 2`, `_guildSize = 4` and `_startingRelicId = "relic.base"` (GameConfig.cs:25-72). These are read by live systems: RelicPricer.cs:24-29 (PriceCommon/Cursed/Divine), RunStateService.cs:120 (BattleGoldReward), RunStateService.cs:61-62 (GuildSize, StartingRelicId). Note also `_relicCapacityBase: 12` in the asset vs `= 8` in code — proof that the asset, when it does own a field, disagrees with the default.
```

**Чем стреляет.** The project's own HARD rule is that the SO asset is what the game plays; here 13 of 20 knobs have no asset value at all, so every balance number in the run economy lives in C# and a designer editing GameConfig in the inspector sees defaults with no provenance. It is also the deeper root of the relic.base duplication: StartingRelicId is not merely bypassed by five hardcoded copies, it has no data owner to bypass.

**Куда править.** Open GameConfig.asset in the inspector and re-save so every field is serialized (or hand-add the keys), then add a guard test that pins the economy values the balance docs quote — the same shape ClassBalanceConfig needs.

---

### CD-11 · P2 · truth — Every unit asset stores its AI profile twice — the legacy inline `_ai` block beside the assigned AiPreset — and the two already disagree

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/UnitData.cs:97` · линза `content-data-integrity`

```csharp
UnitData.cs:97 keeps `[SerializeField, HideInInspector] private AIProfile _ai` with the comment "Легаси inline-профиль AI: источник миграции… Удаляется после назначения пресетов", and UnitData.cs:138 resolves `Ai => _aiPreset != null ? _aiPreset.Profile : _ai`. All 11 relic assets and all 5 enemy assets still serialize a fully populated `_ai:` block (e.g. Relics/Assassin.asset:52-65 beside `_aiPreset:` on line 51). The copies are stale: Assassin.asset:65 has `_passiveThresholdPct: 0.25` while the live owner AiPresets/Assassin.asset has `_passiveThresholdPct: 0`; Defender.asset:310 has `0.15` while AiPresets/Defender.asset has `0`. Two assets still read the legacy copy because they have no preset — Relics/BaseRelic.asset:97 `_aiPreset: {fileID: 0}` and Enemies/TrainingDummy.asset:1256 `_aiPreset: {fileID: 0}` — so the field is neither fully live nor safely deletable. ContentValidationTests.Relics_AiProfileRanges (ContentValidationTests.cs:111) validates `relic.Ai`, i.e. the preset, so the stale inline copies are unvalidated, and it never runs over EnemyData at all.
```

**Чем стреляет.** One fact (a unit's behaviour profile) has two owners in the same asset; the inspector hides one of them (HideInInspector), so a reader diffing the YAML sees thresholds that the game does not use, and the two units that DO use the legacy path are exactly the ones nobody thinks about. Any future edit to a relic's retreat/kite numbers has a 50/50 chance of landing in the dead copy.

**Куда править.** Author AiPresets for relic.base and enemy.training_dummy, then delete `UnitData._ai` and the `Ai` fallback, and strip the `_ai:` blocks from the 16 assets in one migration; extend Relics_AiProfileRanges to cover UnitData (enemies included).

---

### CD-12 · P2 · dead — Authored unit/encounter meta with zero readers: KitPower's whole Cursed/Divine payload, relic legacy tags, enemy threat/bounty, arena id, AI archetype tags

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/RelicData.cs:38` · линза `content-data-integrity`

```csharp
Project-wide greps (Scripts + Tests + assets) find no reader for any of these: RelicData.RunEffects (RelicData.cs:38) — the Cursed-penalty / Divine-bonus payload, so KitPower can never mean anything mechanically, and all 11 relics are `_kitPower: 0` (Assassin.asset:77 etc.) making RelicPricer.cs:28-29 PriceCursed/PriceDivine unreachable; RelicData.AltAiPresets (RelicData.cs:39); RelicData.Tags — the legacy string[] (RelicData.cs:35) still authored in 10 assets (Assassin.asset:73-76 `assassin/melee/common`), whose only would-be reader LoadoutViewModel.Tags actually reads InfoTags (LoadoutViewModel.cs:108); EnemyData.ThreatPoints and GoldBounty (EnemyData.cs:29-30) authored 1-3 on the four goblins; EncounterData.ArenaId (EncounterData.cs:73), empty in all 8 encounters and documented as "ЗАДЕЛ: пока не читается"; AIPresetData.ArchetypeTags (AIPresetData.cs:20); UnitData.ResourceType (UnitData.cs:116) authored Mana/Rage on six relics but read only by ContentHubWindow.Browser.cs:265 — Rage and Mana behave identically. Two adjacent traps in the same data: UnitTagResolver.cs:139 resolves the id "tag.arcane" but no Tags/*.asset carries that id (54 tag assets, none `tag.arcane`), and AutoAttackSystem.cs:178 branches only on `attackType == AttackType.Melee`, so AttackType.ProjectileAoe and AttackType.ProjectilePierce (CombatCategories.cs:174-177) would silently resolve as a plain single-target projectile.
```

**Чем стреляет.** Each of these reads as a working knob in the inspector. A designer marking a relic Divine, giving an enemy a gold bounty, or choosing ProjectileAoe gets no error and no effect; a magic-Arcane unit silently loses its element chip because AddById drops the miss (UnitTagResolver.cs:33).

**Куда править.** Delete the fields that have no design left (RelicData.Tags, EnemyData.GoldBounty, AIPresetData.ArchetypeTags, EncounterData.ArenaId) and implement or delete the rest — RunEffects is the one that matters, since without it KitPower is decoration. Add Tags/Arcane.asset (id `tag.arcane`) and either implement ProjectileAoe/ProjectilePierce or drop the two enum values.

---

### CD-17 · P2 · architecture — The act's enemy-variety distribution is an accident of how many dev BattlePresets happen to point at each encounter — the preset's roster and mode are thrown away

`Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:87` · линза `content-data-integrity`

```csharp
For a live run (guild roster non-empty) the picked preset contributes exactly one field: `effective = BattlePresetData.CreateRuntime(preset.Encounter, guildRoster, DeploymentMode.Free, party, preset.IsElite, …)` — roster and DeploymentMode come from RunState, not the asset. PickBattlePreset (line 154-166) rolls uniformly over `_content.All<BattlePresetData>()`, i.e. over 11 assets. Those 11 assets reference only 6 distinct encounters: GoblinWarband is the encounter of PresetDefender, PresetDeployDemo, PresetPartyVsWarband and PresetRanger (4/11); GoblinScouts of PresetCryomancer, PresetShepherd, PresetSpearman (3/11); GoblinAmbush, GoblinRaid, GoblinSkirmishLine and DummyTrio 1/11 each.
```

**Чем стреляет.** The frequency with which the player meets a warband versus a scout patrol versus three training dummies is set by how many single-hero test slices a developer happened to author against each encounter, and nothing in the data says so. Round 1 noted the dummy slice is in the pool; the deeper defect is that the pool is the wrong collection entirely — BattlePresetData is a dev harness (hero + encounter + fixed positions), and the only part of it a run consumes is the encounter reference.

**Куда править.** Roll the run's fights over `All<EncounterData>()` filtered by EncounterTier (see the tier finding) instead of over BattlePresetData, and mark the dev presets so they can never enter a run pool — or move them out of ContentDatabase entirely, since the ContentDatabase registration is their only non-dev reference.

---

### CD-18 · P2 · dead — DeploymentTier.Extended is dead end to end — no zone in any scene or prefab is Extended, and all 11 relics deny the right to use it

`Assets/_Project/Scripts/Core/Arena/DeploymentService.cs:32` · линза `content-data-integrity`

```csharp
The gate is `if (z.Tier == DeploymentTier.Extended && !canUseExtended) continue;`. Data side: grep for `Tier: 1` across every .unity and .prefab under Assets/_Project returns nothing — the only two authored zones live in WorldScene.unity:27106-27111 and MaxSceneForTests.unity:26905-26910, both `Tier: 0`. Relic side: `_canUseExtendedDeployment: 0` on all 11 relic assets (e.g. Assets/_Project/ScriptableObjects/Relics/Assassin.asset:81). Dead consumers that hang off it: RelicData.cs:33+40 (field + property), DeploymentController.cs:670-671 `CanUseExtended` and its two call sites at 375 and 526, DeploymentController.cs:514 and 543 `SetExtendedHighlight`, DeploymentView.cs:23 `ZoneExtendedCol`, 65-74 `SetExtendedHighlight`, 152-159 `DimExtended`, plus gizmo branches at ArenaLayoutAuthoring.cs:122 and ArenaLayoutAuthoringEditor.cs:97.
```

**Чем стреляет.** About forty lines across five files, an authored SO field and an inspector info-box all describe a placement tier that cannot occur. A reader touching deployment has to reason about a second zone class and a per-champion permission that no content exercises, and the highlight code looks wired (it is called every drag) while being provably a no-op.

**Куда править.** Either author it — give one or two mobile champions `_canUseExtendedDeployment: true` and add an Extended zone to WorldScene's ArenaLayoutAuthoring — or delete DeploymentTier.Extended, RelicData._canUseExtendedDeployment, CanUseExtended, SetExtendedHighlight and the two gizmo branches. Do not leave it half-wired.

---

### CD-19 · P2 · dead — Two of the three dummy encounter assets are unreachable — nothing but the content database references DummyPair and DummyScatter

`Assets/_Project/ScriptableObjects/Encounters/DummyPair.asset:15` · линза `content-data-integrity`

```csharp
A guid reference scan over every .asset/.prefab/.unity/.cs/.uxml/.uss under Assets (excluding ContentDatabase.asset, which lists all 146 definitions unconditionally) finds zero referrers for Encounters/DummyPair.asset (`_id: encounter.dummy_pair`) and Encounters/DummyScatter.asset (`_id: encounter.dummy_scatter`). The only dummy encounter that is wired is DummyTrio, referenced by BattlePresets/PresetBaseKit.asset. Nor are they reachable by id: the only id-based encounter lookup is NodeResolver.PickContent via `node.PayloadId`, and MapGenerator.NewNode (MapGenerator.cs:77) sets `PayloadId = string.Empty` for every node it creates, so no map node ever names an encounter.
```

**Чем стреляет.** Two assets that a reader will assume are live content, plus their entries in the database and the dev picker. DummyScatter also carries `_tier: 1` (Elite), so it reads as authored elite content while being unreachable — exactly the kind of dead thing that misleads.

**Куда править.** Delete both assets and re-run Tools/Guildmaster/Sync Content Database. If a scatter layout is wanted for feel-testing, keep it but wire it to a preset so its liveness is visible.

---

### CD-20 · P2 · truth — UnitData.ResourceType is a second, unread owner of "does this unit have a resource" — the runtime gate is the MaxResource stat, and the Mana/Rage distinction reaches nothing

`Assets/_Project/Scripts/Presentation/ManaBarView.cs:83` · линза `content-data-integrity`

```csharp
The bar decides purely from stats: `float max = unit.Stats.Get(StatType.MaxResource); bool hasResource = max > 0f; gameObject.SetActive(hasResource);` (ManaBarView.cs:81-84), and UnitView.cs:277 feeds it the same stat. Spending and gating also ignore the field: AbilitySystem.cs:59 `if (caster.CurrentResource < data.ResourceCost) return false;` and AutoAttackSystem.cs:268 `float onHit = unit.Unit != null ? unit.Unit.ResourceOnHit : 0f;`. Repo-wide, the only readers of `UnitData.ResourceType` are two editor displays: ContentHubWindow.Browser.cs:265-266 (a badge) and ContentHubWindow.Coverage.cs:29 (a grouping). Yet it is authored on six relics — Assets/_Project/ScriptableObjects/Relics/IronSpearman.asset:23 `_resourceType: 2` (Rage), Cryomancer/Defender/LightShepherd/Ranger/WhirlMonk `_resourceType: 1` (Mana).
```

**Чем стреляет.** The Iron Spearman is authored as a Rage user and behaves exactly like a mana user — same 15-per-hit fill, same blue bar, same cost model — because nothing branches on the enum. And the two owners can silently diverge: authoring ResourceType.Mana without a MaxResource override yields a champion with a resource on paper and no bar or castable ability in the fight, with no validation to catch it.

**Куда править.** Either make ResourceType load-bearing (bar colour/label and a gain model per type) or delete the field and let MaxResource be the single owner. Until then, add a content-validation assert that `ResourceType != None ⟺ the stat block sets MaxResource > 0`.

---

### CD-21 · P2 · gap — The affinity/creature-type axis cannot fire in shipped content: every unit is Living except one Construct, so the ×1.3 vulnerability branch is unreachable and Poison only ever means immunity

`Assets/_Project/Scripts/Data/Definitions/AffinityTable.cs:30` · линза `content-data-integrity`

```csharp
AffinityTable.Multiplier returns ImmuneMult for Poison against Undead/Construct, VulnerableMult (1.3) for Light against Undead/Demon and for Dark against Living, NeutralMult otherwise. Across all 16 authored units `_creatureType: 0` (Living) everywhere except Assets/_Project/ScriptableObjects/Enemies/TrainingDummy.asset:20 `_creatureType: 2` (Construct) — no Undead, Demon or Beast exists. Only two affinities are authored at all: LightShepherd.asset `_affinity: 2` (Light) on its auto-attack, and Druid.asset's spore_burst `_affinityOverride: 2` (Poison) plus Effects/SporeCloud.asset `_affinity: 1`.
```

**Чем стреляет.** Two of the three affinity rules are unreachable and the third is a pure downside: the Druid's entire poison kit — spore_burst and the SporeCloud DoT — multiplies to zero against the training dummy, and PresetBaseKit/DummyTrio is one of the eleven presets a Battle node can roll. So a run can hand a Druid player a fight where their ultimate does literally nothing, with no feedback. The Shepherd's Light affinity is decoration.

**Куда править.** Author at least one Undead or Demon enemy so the vulnerability branch exists, and stop shipping the training-dummy encounter in the run pool (see the encounter-pool finding). If no non-Living enemy is planned this act, cut the affinity fields from the shipped relics rather than leaving a stat that can only subtract.

---

### CD-22 · P2 · correctness — UnitTagResolver resolves "tag.arcane", which has no asset — a dangling id whose only handling is a silent skip

`Assets/_Project/Scripts/Data/Definitions/UnitTagResolver.cs:139` · линза `content-data-integrity`

```csharp
SpecificTagId maps `MagicElement.Arcane => "tag.arcane"`. Assets/_Project/ScriptableObjects/Tags holds 54 TagData assets and none has `_id: tag.arcane` (the element tags present are tag.fire, tag.ice, tag.lightning). The miss is swallowed at line 32-33: `void AddById(string id) { if (id == null || !seen.Add(id)) return; if (db.TryGet(id, out TagData tag) && tag != null) result.Add(tag); }` — the class comment even codifies it: «отсутствующий ассет тега молча пропускается (не роняем UI из-за тега)». MagicElement.Arcane is fully authorable today: CombatCategories.cs:56 declares it and MagicElementOverride.Arcane (line 109) is resolvable per-ability via AbilityData._magicElementOverride.
```

**Чем стреляет.** The first champion or ability authored as Arcane gets a card with its damage-type chip missing, no console warning and nothing failing — the exact class of bug the id system exists to prevent. Fifteen of the resolver's nineteen hardcoded tag ids are matched by an asset; this one is a typo-equivalent that the silent-skip policy hides forever.

**Куда править.** Add Tags/Arcane.asset with `_id: tag.arcane, _category: 1`, and add an EditMode test that every id string returned by UnitTagResolver.RoleTagId/UmbrellaTagId/SpecificTagId/AffinityTagId resolves in the content database — the silent skip is fine at runtime only if a test guarantees the set is complete.

---

### CD-23 · P2 · gap — relic.base — the champion every run is guaranteed to start with — is a stat-for-stat clone of the training dummy, and is the only relic with no AI preset

`Assets/_Project/ScriptableObjects/Relics/BaseRelic.asset:15` · линза `content-data-integrity`

```csharp
Diffing BaseRelic.asset against Enemies/TrainingDummy.asset leaves only the script guid, the id, creature type, view prefab and tint: identical `_visual` (guid 5dd8088d40927484fa5fcc2ea362c773), identical stat block `Stat 0 / Op 3 (Override) / 1200` and `Stat 7 / Op 3 / 100`, both with `_grantedEffects: []`, `_abilities: []`, `_aiPreset: {fileID: 0}`, `_infoTags: []`, `_icon: {fileID: 0}`. Because `_aiPreset` is null it is the only player unit that falls through UnitData.cs:138 `public AIProfile Ai => _aiPreset != null ? _aiPreset.Profile : _ai;` onto the legacy inline block — the other ten relics all point at an AiPresets asset. Its MaxHP Override of 1200 also bypasses its own Bruiser class baseline of 2000 (ClassBalanceConfig.asset:15 `_baseHp: 2000`, Bruiser HpMult 1). BattlePresetData.cs:16 documents it as the intended filler kit («у «пустого» сосуда стоит базовый релик без особенностей»), and RunStateService seeds it as the starting relic.
```

**Чем стреляет.** The first minutes of every run are played with a unit that has no ability, no passive, no tags, no icon, 40% below its own class HP baseline, and behaviour driven by the one AI path the migration was supposed to retire. Round 1 flagged the legacy `_ai` block as a duplicate; the sharper problem is that on this one asset the legacy block is not a duplicate at all — it is the only source, so deleting it silently changes how the starting champion fights.

**Куда править.** Give relic.base an AiPreset (the migration's stated end state), drop the MaxHP Override so the Bruiser baseline applies, and author a minimal identity (icon, one info tag, one modest passive). Then delete UnitData._ai and its fallback in the `Ai` property, since relic.base was the last unit keeping it alive.

---

### CD-24 · P2 · truth — Base MaxHP has two config owners that already disagree — StatsConfig says 1200, ClassBalanceConfig says 2000, and the StatsConfig value is reachable by nothing that has UnitData

`Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:19` · линза `content-data-integrity`

```csharp
StatsConfig.asset:19-20 authors `- Stat: 0 / Value: 1200` (MaxHP) and lines 29-30 `- Stat: 20 / Value: 3` (MoveSpeed). ClassBalanceConfig.asset:15-16 authors `_baseHp: 2000` and `_baseMoveSpeed: 3`, and ClassBalanceConfig.cs:42-50 emits both as `ModifierOp.Override` modifiers. RuntimeUnitFactory.cs:65-69 builds `new Stats(_config)` and then immediately calls `ClassBaseline.Apply(stats, data, _classBalance)` → `stats.AddModifiersFrom(config, config.GetBaseModifiers(data.CombatClass))` (ClassBaseline.cs:28). Since Override replaces the base term, any unit with a UnitData and a non-null ClassBalanceConfig gets 2000×classMult and never sees 1200; ClassBaseline.Apply is a no-op only when `data == null || config == null` (line 27).
```

**Чем стреляет.** Two assets answer "how much HP does a baseline unit have" with numbers 67% apart. The 1200 is not merely redundant, it is a trap: it is the number the auditor-style fallbacks and any data-less unit path would use, and a designer retuning global HP will naturally edit StatsConfig — the file literally named "stat defaults" — and see nothing change in the game. The same shape holds for MoveSpeed, where the two happen to agree today and so hide the duplication.

**Куда править.** Delete the MaxHP and MoveSpeed rows from StatsConfig._defaults so ClassBalanceConfig is the single owner of the class-driven statics, and add a guard test asserting StatsConfig._defaults contains no entry for StatType.MaxHP or StatType.MoveSpeed.

---

### CD-6 · P2 · dead — Four ContentDefinition types ship with CreateAssetMenu, a domain and a folder — and zero assets, zero readers

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/TraitData.cs:11` · линза `content-data-integrity`

```csharp
TraitData.cs:11, ConsequenceData.cs:11, GuildmasterData.cs:11 and RunModifierData.cs:11 each declare `[CreateAssetMenu(menuName = "Guildmaster/Content/…")]`. No .asset of any of these types exists (the full asset listing under Assets/_Project/ScriptableObjects has no Traits/Consequences/Guildmasters/RunModifiers folder). A project-wide grep for each type name outside its own file returns only the two registration tables: ContentDomains.cs:26/27/29/31 and ContentPaths.cs:26/27/29/31 — no runtime code, no editor tool, no test, no prefab/scene/UXML reference. Together they carry 24 serialized fields with public getters (e.g. GuildmasterData.StartingRelicIds, ConsequenceData.HealCostGold, RunModifierData.RewardMult) that nothing reads.
```

**Чем стреляет.** They appear in the Content Hub create menu (via ContentPaths.CreatableTypes) as if they were authorable systems, so a designer can produce assets that no system will ever load, and every future refactor of StatModifier/EffectData has to keep compiling four types nobody uses.

**Куда править.** Delete the four types, their domain and folder rows; the designs live in the GDD (Traits/Consequences/Guildmasters/RunModifiers) and cost nothing there. Re-add them with their first real asset.

---

### CD-7 · P2 · dead — Five IRuntimeEffectComponent implementations are never authored on any effect — they exist only for their own tests

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Effects/Components/ShieldComponent.cs:1` · линза `content-data-integrity`

```csharp
Twenty component files live in Assets/_Project/Scripts/Combat/Effects/Components/. Scanning the [SerializeReference] type records of every asset (`grep -rhoE "class: [A-Za-z]+Component" Assets/_Project/ScriptableObjects`) yields 14 distinct classes across the 22 effect assets. The five never named by any asset are ShieldComponent, ThornsComponent, LifestealComponent, PeriodicHealComponent and DispelComponent. Their only references outside their own files are EditMode tests: EffectComponentTests.cs:114 (`new ShieldComponent().With("_amount", …)`), EffectComponentTests.cs:94 (PeriodicHealComponent), ReactiveEffectTests.cs:29/66 (LifestealComponent), ReactiveEffectTests.cs:47/108 (ThornsComponent), EffectDispelTests.cs:94 (DispelComponent). Nothing in DevTools or Balance constructs them either (grep over Scripts/DevTools and Scripts/Balance returns nothing).
```

**Чем стреляет.** They are indistinguishable from live components in the [SerializeReference] dropdown, so a designer picking ShieldComponent instead of the actually-shipped MissingHpShieldComponent gets a mechanic no content ever validated. And EffectData.CleanseTier (EffectData.cs:35) exists mostly to feed DispelComponent, which no asset uses.

**Куда править.** Delete the five components and their tests, or — for the ones the GDD still wants (a flat Shield, a HoT) — author one effect asset each so the sim path is exercised by content and not only by a unit test.

---

### CD-8 · P2 · correctness — The monk's root is the only control effect authored Neutral without the Debuff tag — tenacity and cleanse silently skip it

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/Effects/VortexHold.asset:16` · линза `content-data-integrity`

```csharp
VortexHold.asset:15-17 — `_id: effect.vortex_hold`, `_polarity: 2` (EffectPolarity.Neutral), `_tags: 4` (EffectTag.Control alone). Its component is `ControlComponent { _preventMove: 1 }` and it is applied to the ENEMY: WhirlMonk.asset:955-956 puts it in the `whirl_push` ability's `_effects`, and AbilitySystem.ApplyDisplace calls `ApplyEffects(target, data, caster, ctx)` (AbilitySystem.cs:141). The two sibling stuns are authored the other way: IceChainsStun.asset:16-17 `_polarity: 1` / `_tags: 6` (Control|Debuff) and ResoluteStrikeStun.asset:16-17 the same. Consequence 1: EffectSystem.DurationMultiplier (EffectSystem.cs:490-498) returns 1f for Neutral, so ReceiveDebuffEff/ApplyDebuffEff never scale the monk's root while they do scale the other two. Consequence 2: EffectSystem.MatchesDispel (EffectSystem.cs:476-478) matches `req.Polarity == DispelTargetPolarity.Debuff` only against `def.Polarity == EffectPolarity.Debuff`, so a future cleanse cannot free a rooted unit; and any consumer filtering `EffectTag.Debuff` on the unit's tag mask misses it.
```

**Чем стреляет.** A 0.4s hard root that ignores tenacity is a balance hole that only shows up on units with ReceiveDebuffEff, and it is invisible in review because the effect looks correct in isolation — the defect is only visible next to its two siblings.

**Куда править.** Set VortexHold.asset `_polarity: 1` and `_tags: 6` (Control|Debuff) to match IceChainsStun/ResoluteStrikeStun, and add an EditMode invariant to ContentValidationTests: any effect whose components include a ControlComponent that prevents act/move/cast must be Polarity.Debuff and carry EffectTag.Debuff|Control (or be explicitly self-targeted).

---

### CD-9 · P2 · dead — AbilityData.VisualSlot and UnitVisual.SkillClip are read by nothing that runs — no cast animation is ever played, but a test forces the clips to be authored

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/AbilityData.cs:122` · линза `content-data-integrity` · переоформляет R1-73

```csharp
AbilityData.cs:121-122 declares `_visualSlot` with the tooltip "Слот визуала каста: проигрывается клип UnitVisual.SkillClip(этот слот)", exposed at line 171. A project-wide grep for `VisualSlot` finds exactly three non-declaration hits, all outside the runtime: AnimationValidationTests.cs:83-84 and (indirectly) ContentHubWindow.Visual.cs:94. `SkillClip` has the same reader set — ContentHubWindow.Visual.cs:94 and AnimationValidationTests.cs:61/83. UnitView, the only thing that drives the Animator, knows exactly four states: HashFor(...) covers Run/Attack/Death/Idle (UnitView.cs:430-435) and never touches a skill clip; UnitVisual.HitClip (UnitVisual.cs:51) likewise has a single reader, ContentHubWindow.Visual.cs:92. All nine authored abilities carry `_visualSlot: 0`, and AnimationValidationTests.AbilityVisualSlots_PointToNonEmptySlot asserts that slot 0 is a non-null clip on every unit that has a Visual — a red test demanding content for a code path that does not exist.
```

**Чем стреляет.** This goes wider than the displacement knobs already reported: the visual half of AbilityData is inert too, and unlike a silently-ignored number this one has a guard test forcing artists to fill Skill1 on every unit whose ability slot points there. Anyone reading AbilityData reasonably concludes casts are animated.

**Куда править.** Either play the clip (UnitView needs a Cast state fed from AbilitySystem.OnAbilityCast + ability.VisualSlot) or delete `_visualSlot`, `UnitVisual._skillClips`/`SkillClip`, `_hitClip`/`HitClip`, and the two tests that pin them.

---

### LT-10 · P2 · convention — Combat floating text prints the English word «evade» straight into the battle HUD

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:367` · линза `localization-text`

```csharp
`private void HandleAttackEvaded(RuntimeUnit target) { // Полный негейт удара («Изворотливость») — урона нет, показываем «evade». SpawnNumber(AnchorFor(target), "evade", _evadeColor); }` (cs:364-368). Every other SpawnNumber call passes a number (cs:341,345,357). Subscribed from the sim's evade event, so it fires on any hit negated by DodgeComponent/StealthComponent.
```

**Чем стреляет.** A hardcoded, untranslated, English player-facing word in the most-watched surface of the game — the only string in the combat HUD, and it is in the wrong language for the only filled locale. The HARD rule admits no exception for one-word strings, and floating text has no fallback path that would ever reveal the omission.

**Куда править.** Route it through the loc layer like the rest of the HUD would need to be — add `ui.combat.evade` (RU «уклон») to the UI table and resolve it in the presenter, or, since Presentation has no ILocalizationService today, pass the resolved string in with the CombatFeel/HUD strings the presenter already receives.

---

### LT-11 · P2 · truth — Stat numbers have three formatters and one of them is culture-dependent, so the same relic reads «1,2» on one screen and «1.2» on another — plus untranslated English stat abbreviations

`Assets/_Project/Scripts/UI/LoadoutViewModel.cs:132` · линза `localization-text`

```csharp
Owner A (documented single owner): StatFormat — «Единственное место, где решается, как выглядит число — иначе одна и та же величина на разных экранах покажется по-разному» (StatFormat.cs:63-66), and it is careful: `CultureInfo c = CultureInfo.InvariantCulture` (cs:124), `rounded.ToString(decimals >= 2 ? "0.##" : "0.#", c)` (cs:150). Owner B: UnitStatPreview.Num — `v % 1f == 0f ? ((int)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture)` (cs:81-83). Owner C: LoadoutViewModel.Mathf — `private static string Mathf(float v) => v % 1f == 0f ? ((int)v).ToString() : v.ToString("0.0");` (cs:132) — byte-identical to B minus the culture. It feeds live UI: MenuRouter.cs:551 `detailStats.text = _loadoutVm.StatsSummary(r)` on the double-click Loadout screen, via StatsSummary (cs:118-130). The same method also emits untranslated abbreviations: `StatType.MaxHP => "HP", AutoAttackDamage => "DMG", AttackSpeed => "AS", AttackRange => "RNG", MoveSpeed => "MS", PhysArmor => "ARM"` (cs:134-143), with no loc keys — while UnitStatPreview.cs:50-57 already owns full localized labels for the same six stats («Здоровье», «Физическая броня», …) behind ui.stat.* keys.
```

**Чем стреляет.** One fact (how a stat value is rendered) with three owners, and they already disagree: on a Russian Windows (CurrentCulture ru-RU) `Mathf` yields «AS 1,2» on the Loadout screen while the inventory panel shows «Скорость атаки 1.2» for the identical relic. The English abbreviations are a second, unlocalized vocabulary for stats that the project already localized elsewhere.

**Куда править.** Delete LoadoutViewModel.Mathf/StatLabel/StatsSummary/Tags (all four are legacy paths of the old LoadoutScreen, per the method's own docstring at cs:103) and feed that screen from IUnitStatPreview like the inventory does, or at minimum route it through StatFormat.Value so the invariant-culture decision has one owner.

---

### LT-17 · P2 · architecture — Six production sites build localization keys by string concatenation, against ContentKeys' own written single-owner claim

`Assets/_Project/Scripts/UI/LoadoutViewModel.cs:83` · линза `localization-text`

```csharp
ContentKeys.cs:11-12 states the rule: «Суффиксы записаны здесь один раз; редакторная политика ссылается сюда, чтобы „что создаём" и „что читаем" не разъехались», and exposes KeyFor/NameKey/DescKey (ContentKeys.cs:37-47). Yet: LoadoutViewModel.cs:83 `_loc.GetString(r.Id + ".name")`; LoadoutViewModel.cs:90 `_loc.GetString(r.Id + ".desc")`; LoadoutViewModel.cs:114 `_loc.GetString(r.InfoTags[i].Id + ".name")`; LoadoutHubViewModel.cs:77 `_loc?.GetString($"{id}.name")`; LoadoutInventoryView.cs:337 `L(t.Id + ".name", TagFallback(t.Id))`; TextEventData.cs:31/34/37/40 `Id + ".title"`, `Id + ".body"`, `$"{Id}.choice{index}.label"`, `$"{Id}.choice{index}.result"`. Only DescriptionService.cs:38/44/50/61/65 and RuntimeEffect.cs:22 go through ContentKeys.
```

**Чем стреляет.** Supersedes and widens my round-1 «TextEventData builds suffixes at runtime, ContentLocalization builds them again in the editor» — that was two owners of one suffix set; the real shape is seven owners of the key format, six of which are raw concatenation. Concretely: ContentKeys.NameSuffix cannot be changed (e.g. to "title" or a namespaced form) without silently breaking five screens, because the editor's CreateMissingKeys follows ContentKeys while the readers do not. A typo in any of the six (".nam", ".Desc") produces a blank string with no error, since LocalizationService returns "" for unknown keys.

**Куда править.** Route all six through ContentKeys.NameKey/DescKey (they take a ContentDefinition, which all six sites already hold), and add the event suffixes (title/body/choiceN.label/choiceN.result) to ContentKeys so TextEventData and ContentLocalization.EventSuffixes read the same constants.

---

### LT-18 · P2 · gap — The required-key policy enforces 2 of the 6 suffixes ContentKeys defines — case forms and desc.full are authored by hand and guarded by nothing

`Assets/_Project/Scripts/Data/Editor/ContentLocalization.cs:41` · линза `localization-text`

```csharp
ContentKeys.cs:17-34 defines six suffixes: name, desc, desc.full (FullDescSuffix), name.gen, name.acc, name.plural. ContentLocalization.RequiredSuffixes (ContentLocalization.cs:41-59) can only ever return NameOnly = {name}, NameAndDesc = {name, desc}, EventSuffixes, or empty — FullDescSuffix and the three case suffixes appear nowhere. KeywordDefinition falls into `default: return NameAndDesc`. Consequences measured in the table: all 12 keywords carry .name.gen and .name.acc and .desc.full, but only 7 carry .name.plural (kw.armor, kw.magical, kw.physical, kw.stealth, kw.threat, kw.true have none). ContentValidationTests.cs:97 and ContentDefinitionEditor.cs:72 both drive off MissingKeys → RequiredSuffixes, so «Create missing keys» never creates them and no test notices.
```

**Чем стреляет.** DescriptionService.KeywordForm (Descriptions/DescriptionService.cs:64-65) silently falls back to the nominative when a case form is absent, and DescribeFull (:55) silently falls back to the short description. So the failure mode is grammatically wrong Russian inside a sentence («снимает 2 стака Броня» instead of «Брони») and a missing compendium article — never an error, never a red test. Every keyword added from today gets only name+desc from the editor button, and the whole падежи system that was deliberately put in DATA quietly stops being populated.

**Куда править.** Add FullDescSuffix and the three case suffixes to RequiredSuffixes for KeywordDefinition (or a dedicated KeywordSuffixes list, the way EventSuffixes works), so both the inspector button and ContentValidationTests cover the forms the runtime actually reads.

---

### LT-19 · P2 · dead — tag.arcane is a dangling id: no Tag asset, no loc row, and the resolver drops it without a sound

`Assets/_Project/Scripts/Data/Definitions/UnitTagResolver.cs:139` · линза `localization-text`

```csharp
UnitTagResolver.cs:136-139 maps `MagicElement.Arcane => "tag.arcane"` (Arcane = 4, CombatCategories.cs:56). ScriptableObjects/Tags holds 54 .asset files; extracting every `_id: tag.*` from them gives 54 ids and comparing against the 19 ids hardcoded in the resolver leaves exactly one orphan: tag.arcane. Content Shared Data holds 54 tag.*.name rows — none is tag.arcane. UnitTagResolver.cs:31-35 `void AddById(string id) { ... if (db.TryGet(id, out TagData tag) && tag != null) result.Add(tag); }` — a miss is a silent no-op, as its own docstring admits («отсутствующий ассет тега молча пропускается»). UnitTagResolverTests.cs has no Arcane case.
```

**Чем стреляет.** A branch that looks fully wired and pays nothing. The moment anyone authors a unit with MagicElement.Arcane — the enum value exists and DamageCategories.Resolve(MagicElementOverride.Arcane, …) is already tested (DamageTypeResolverTests.cs:62) — that unit's DamageType axis loses its chip in the loadout panel with no warning anywhere, and the designer sees an incomplete tag row and has no way to tell it from «this unit has no element».

**Куда править.** Either add Tags/Arcane.asset with _id: tag.arcane plus the tag.arcane.name row, or drop the MagicElement.Arcane arm from RoleTagId's sibling switch. Additionally, add an EditMode guard that every tag id referenced in UnitTagResolver resolves in the ContentDatabase — 19 hardcoded ids against 54 assets is exactly the kind of pair that rots.

---

### LT-20 · P2 · dead — The entire Smart-Format named-argument path is exercised by one dev-only table row that only a test reads

`Assets/_Project/Localization/Tables/UI_ru.asset:230` · линза `localization-text`

```csharp
UI_ru.asset:228-233 contains the file's only SmartFormatTag, attached to shared entry id 5619882303676416 = `ui.dev.stat_probe` («Урон: {dmg}»), whose own comment in SmartStatStringTests.cs:31 says «Живёт ради этой проверки — игроку не показывается». Content_ru.asset contains zero SmartFormatTag entries. Every production call into the args overloads passes null: LoadoutViewModel.cs:90 `_descriptions?.Describe(r, null)`; the only other Describe/DescribeFull callers are TooltipContentFactory (all `null`). DescriptionService.Localized (Descriptions/DescriptionService.cs:110) therefore always takes the no-args branch, so ILocalizationService.GetString(key, args) and GetString(table, key, args) (ILocalizationService.cs:34/46) have no production caller at all.
```

**Чем стреляет.** Reframes my round-1 «StatValueFormatter/FormattedStat/DescribeStat/UnitLabels have zero production callers» at its root: it is not one dead formatter, it is the whole named-argument substitution mechanism — two of the four ILocalizationService overloads, the StatValueFormatter registration inside LocalizationSettings, SmartStatStringTests, and a dev row shipped inside the player build's UI String Table. The 15-line remark in ILocalizationService.cs:36-44 arguing why named args beat positional ones is defending a path nothing walks — while the one row that DOES take arguments (ui.camp.budget) uses the positional form that remark forbids.

**Куда править.** Decide one way. Either wire a real description through it (a relic desc with {dmg}, which is what the whole StatValue/FormattedStat layer was built for) or delete ui.dev.stat_probe from the shipped table, delete SmartStatStringTests, and drop the two args overloads plus StatValueFormatter.

---

### LT-21 · P2 · architecture — Table names have two owners each, and the "UI" table has no owner at all

`Assets/_Project/Scripts/Game/Services/LocalizationService.cs:18` · линза `localization-text`

```csharp
ContentKeys.cs:16 `public const string TableName = "Content";` — the declared single owner, referenced by ContentLocalization.cs:21. But LocalizationService.cs:18 declares its own `private const string ContentTable = "Content";` and it is the copy that actually decides resolution (LocalizationService.cs:48/51). The second table name is owned by nobody: DescriptionService.cs:18 `private const string UiTable = "UI";` and TooltipContentFactory.cs:21 `private const string UiTable = "UI";` are two independent literals, and ContentKeys has no UI counterpart.
```

**Чем стреляет.** ContentKeys.cs:7-13 is explicit that it exists so the editor and the runtime cannot disagree about keys — but the runtime resolver does not import it, so renaming the collection in the Localization window requires finding four literals in four assemblies. More importantly the absent UI owner is why the ui.* strings drifted in the first place: with no constant to point at, MenuRouter's 17 `key => _loc?.GetString(key)` lambdas all silently picked Content, which is the root of the 21 unreachable UI rows I reported in round 1.

**Куда править.** Add `public const string UiTableName = "UI";` next to ContentKeys.TableName, delete LocalizationService.ContentTable and both UiTable literals in favour of it, and give ILocalizationService a two-arg convenience so screen builders must state which table they mean.

---

### LT-22 · P2 · truth — The Smart-flag guard test is blind to the one placeholder style the project actually ships

`Assets/_Project/Tests/EditMode/Content/SmartStringFlagTests.cs:23` · линза `localization-text`

```csharp
SmartStringFlagTests.cs:23 `private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{[A-Za-z_][A-Za-z0-9_.:]*\}", RegexOptions.Compiled);` — the character class after `{` requires a letter or underscore, so `{0}` and `{1}` never match. The only production row in either table that carries placeholders is UI_ru.asset:33-35, `ui.camp.budget` = «Действий осталось: {0} из {1}», with `m_Metadata: m_Items: []` (no Smart flag). It is consumed by CampScreenView.cs:75 `budget.text = string.Format(L("ui.camp.budget", "Действий осталось: {0} из {1}"), session.Remaining, session.Budget);` — resolved plainly, then formatted outside the localization system.
```

**Чем стреляет.** The test's own docstring (SmartStringFlagTests.cs:11-15) says a placeholder without the Smart flag «резолвится в ПУСТО … Проверено на живом ключе 2026-07-26» — yet it passes today over the single row that has placeholders, because that row uses positional slots. So the guard reports green on a table it does not actually cover, and it simultaneously enshrines a rule that ILocalizationService.cs:36-44 forbids for exactly the reason that bites here: a translator reordering «{1} из {0}» in another language has no way to know which number is which. Whichever convention wins, right now the project holds both and checks neither.

**Куда править.** Extend the regex to `\{[A-Za-z0-9_][A-Za-z0-9_.:]*\}` so positional slots are seen, then decide: either convert ui.camp.budget to named Smart args ({remaining}/{budget}, flag it Smart, pass a dictionary) — which is what the interface's rule demands — or add an explicit allowlist and document that positional+string.Format is the sanctioned form.

---

### LT-23 · P2 · convention — The percent unit has three owners: a UI literal, a C# constant, and a loc key that exists in no table

`Assets/_Project/Scripts/UI/Components/SliderRow.cs:78` · линза `localization-text`

```csharp
SliderRow.cs:78 `_value.text = Mathf.RoundToInt(t * 100f) + "%";` (and the initial `new Label("0%")` at :67). StatFormat.cs:25 `public static UnitLabels Ru => new UnitLabels("%", "с", "/с");` used by StatFormat.cs:128/134/136. DescriptionService.cs:19-21 declares the intended owner — `PercentKey = "ui.unit.percent"`, SecondsKey, PerSecondKey — read at :99-101 from the UI table; none of those three keys exists in UI Shared Data (41 keys, verified) nor in Content Shared Data (212 keys), so the Or(...) fallback to UnitLabels.Ru always wins.
```

**Чем стреляет.** Three copies of one fact, and the one that was declared authoritative is unreachable. The concrete divergence is already live in the shipped build: StatFormat.cs:128 emits value + Nbsp + "%" (non-breaking space before the sign) while SliderRow.cs:78 emits value + "%" with no space — the same unit is typeset two different ways on two screens. A translator moving to a locale where the percent sign precedes the number, or where «с» is «s», can fix neither, because two of the three owners are compiled constants.

**Куда править.** Author ui.unit.percent / ui.unit.seconds / ui.unit.per_second in the UI table (they are already asked for), have SliderRow take its suffix from IDescriptionService/ILocalizationService instead of the literal, and keep UnitLabels.Ru as the last-resort fallback only.

---

### LT-4 · P2 · convention — 22 player-facing strings live only in UXML text= and no code ever overwrites them — including the whole double-click Loadout screen, which is in untranslated English

`Assets/_Project/UI/Screens/LoadoutScreen.uxml:4` · линза `localization-text`

```csharp
Of 72 `text="…"` attributes across 16 .uxml files, 22 are never assigned from C#. LoadoutScreen.uxml is entirely hardcoded ENGLISH: line 4 `<ui:Label text="Loadout" …/>`, lines 7-10 `text="Relic"/"Items"/"Upgrades"/"AI"`, lines 28-30 `text="Accept"/"Save"/"Close"`. Its builder MenuRouter.BuildLoadoutScreen (cs:533-599) only sets detail-* labels, calls `Disable(screen.Q<Button>("tab-items"))` (591-593) and wires `.clicked` (596-598) — no text assignment. The screen is live: DeploymentController.cs:495 `if (doubleClick) { OpenLoadout(unit); return; }` → :533 publishes OpenLoadoutRequest → UiRootBootstrap.cs:212 → MenuRouter.cs:189-193 `PushScreen(BuildLoadoutScreen, ScreenKind.Page)`, and LoadoutScreen.uxml (guid 15c3d6ae3d7b0564e9f3085e3abba396) is serialized into Assets/_Project/Scenes/CoreScene.unity. Same class elsewhere: SettingsScreen.uxml lines 4/9/10/11/26/40/41/42 ("Настройки", "Игра", "Графика", "Звук", "Настройки графики — скоро", "Сохранить", "Отмена", "Сброс") — BuildSettingsScreen (cs:447-504) + WireSettingsTabs (508-531) touch only classes and clicks; PauseScreen.uxml:4,9,10 ("Меню", "Продолжить", "Настройки") while lines 13-14 of the same file ARE localized at MenuRouter.cs:428,434; ShopScreen.uxml:19 "Продать реликвии".
```

**Чем стреляет.** Player-facing text with no loc key at all, in violation of the HARD rule, and the markup is where nobody greps. The pause screen is the sharpest evidence that this is invisible drift, not a decision: two of its five buttons go through `Loc(...)` and three do not, in the same file. A player double-clicking a unit in deployment gets an English dialog inside a Russian game.

**Куда править.** Assign every text-bearing element from a loc key in its builder and strip the `text=` attributes from UXML (or keep them only as editor-preview placeholders and add a test that fails when a named element with `text=` has no assignment). LoadoutScreen itself is a candidate for deletion — LoadoutInventoryScreen replaced it everywhere except the double-click path.

---

### LT-5 · P2 · dead — The run topbar permanently shows the studio name as the player's guild and a fake «ASC IV» rank — both never assigned

`Assets/_Project/UI/Screens/RunModeBar.uxml:9` · линза `localization-text`

```csharp
`<ui:Label name="guild-name" text="Alebardium" class="gm-loadout__guild-name" />` and line 10 `<ui:Label name="guild-asc" text="· ASC IV" class="gm-loadout__guild-asc" />`. RunModeBarView's constructor queries exactly seven elements — topbar-gold, topbar-act, topbar-floor, topbar-timer, battle-timer, topbar-hp, btn-start (cs:40-46) — plus mode-* chips and btn-menu; `guild-name` and `guild-asc` appear in no C# file (`grep -rn "guild-name\|guild-asc" Assets/_Project/Scripts` → the USS class only). The file's own header comment says «Строки-плейсхолдеры RU; локализованные проставляет RunModeBarView (ключи ui.*)» (uxml:6).
```

**Чем стреляет.** Two hardcoded strings that the header promises are placeholders but that ship as final text on the always-visible app shell: the player's guild is labelled with the developer's studio name, and «ASC IV» advertises an ascension system the run has no data for. The comment makes a future reader believe the wiring exists, so the bug survives review.

**Куда править.** Either bind both labels from RunState (guild name) and the ascension source, or delete the two elements until that data exists. Do not leave a promised-but-absent assignment.

---

### LT-6 · P2 · dead — The whole «numbers inside descriptions» pipeline — StatValueFormatter, FormattedStat, DescribeStat, UnitLabels — has zero production callers

`Assets/_Project/Scripts/Data/Stats/StatFormat.cs:87` · линза `localization-text`

```csharp
`StatFormat.Describe(in FormattedStat)` is called from exactly two places: DescriptionService.cs:70 (`DescribeStat`) and StatValueFormatter.cs:35. `DescribeStat` / `IDescriptionService.Explain` have no caller outside the interface and Tests (`grep -rn "DescribeStat\|\.Explain("` → DescriptionService.cs:69,76, IDescriptionService.cs:42,48, Tests/EditMode/Combat/StatsExplainTests.cs, Tests/EditMode/Content/DescriptionTests.cs). StatValueFormatter can only fire when a Smart string receives a FormattedStat argument, but every production `Describe`/`DescribeFull` passes null args: LoadoutViewModel.cs:88 `_descriptions?.Describe(r, null)`, TooltipContentFactory.cs:73,94,128,138 — all `null`. And there is nothing for it to format anyway: scanning Content_ru.asset for Smart placeholders `(?<!\{)\{[A-Za-z_][A-Za-z0-9_.:]*\}` returns 0 hits in 205 entries. The formatter is nevertheless registered in the shipped asset (LocalizationSettings.asset:210 `type: {class: StatValueFormatter, ns: Guildmaster.Game.Services, asm: Guildmaster.Game}`).
```

**Чем стреляет.** ~200 lines of indirection (StatFormat's term decomposition, MaxDetailedTerms collapsing, Nbsp handling, UnitLabels + its localized lookup of ui.unit.percent/seconds/per_second, FormattedStat's 'units ride inside the value' contract, the Smart-Format extension and its asset registration) pay nothing today, and they look wired: a reader sees a registered formatter plus a `{dmg}` contract in ILocalizationService's docstring and assumes descriptions carry live numbers. It also created three loc keys (ui.unit.*) that exist in no table and one table row (ui.dev.stat_probe) whose only consumer is a test.

**Куда править.** Either land the feature (author one relic desc with `{dmg}` and pass args from LoadoutViewModel/TooltipContentFactory) or delete the branch: DescribeStat/Explain from IDescriptionService, FormattedStat, UnitLabels, StatValueFormatter + its LocalizationSettings registration, the ui.unit.* keys and the ui.dev.stat_probe row. Keep StatFormat.Value/Scalar, which the surviving stat panel needs.

---

### LT-7 · P2 · gap — No data-authored stat source can be named to the player: ContentDefinition never implements IModifierSource, so the required {id}.name keys are authored and never read

`Assets/_Project/Scripts/Combat/Stats/Stats.cs:104` · линза `localization-text`

```csharp
`terms[i] = new StatTerm((sources[i] as IModifierSource)?.ModifierSourceLocKey, …)`. The only implementation in the repo is `public sealed class RuntimeEffect : IModifierSource` (Combat/Effects/RuntimeEffect.cs:12, `ModifierSourceLocKey => ContentKeys.NameKey(Def)`). Every other modifier group passes a plain object: ClassBaseline.cs:28 `AddModifiersFrom(config, …)` (ClassBalanceConfig), EnemyScalers.cs:23,26 `AddModifiersFrom(enemy.Species, …)` (SpeciesData), RuntimeUnitFactory.cs:75,78 `AddModifiersFrom(data, …)` / `AddModifiersFrom(vessel, …)`, UnitStatPreview.cs:67, SyntheticUnits.cs:22 `AddModifiersFrom("synthetic", …)`. All of those cast to null → the term is anonymous. The contributions are real, not Override-only: ScriptableObjects/Species/Goblins.asset `_scalers: - Stat: 0, Op: 2, Value: -0.6` (Op 2 = PercentMult per Data/Stats/ModifierOp.cs:18), so a goblin's MaxHP breakdown is a named-source term with a null key. Meanwhile ContentLocalization.RequiredSuffixes (cs:41-58) sends `species` down the `default: return NameAndDesc` branch, so species.goblins.name AND .desc are mandatory — and `grep -rn "\.Species\b"` shows SpeciesData is consumed only by EnemyScalers, i.e. neither key is ever resolved.
```

**Чем стреляет.** One fact — 'the display name of a stat source' — has a seam (IModifierSource) wired for exactly one of six producers. The player-facing consequence is a breakdown that reads «100 - 60 % = 40» with no «(Гоблины)», and the authoring consequence is that the validator forces translators to fill species/vessel/class names and descriptions that no code path reads. species.goblins.desc is already empty in Content_ru.asset while the suite is green.

**Куда править.** Implement IModifierSource on ContentDefinition (`ModifierSourceLocKey => ContentKeys.NameKey(this)`) so class/species/relic/vessel terms name themselves, and add ClassBalanceConfig to the same seam; then either narrow RequiredSuffixes for `species` to name-only or make the desc key actually surface somewhere.

---

### LT-8 · P2 · gap — RequiredLocalizationKeys_Exist checks that keys exist, not that RU is filled — 7 required content keys already have no RU value and the suite is green

`Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:93` · линза `localization-text`

```csharp
The test body is `IReadOnlyList<string> missing = ContentLocalization.MissingKeys(def); Assert.IsEmpty(missing, …)` (cs:95-101), and MissingKeys only asks `if (!col.SharedData.Contains(key)) missing.Add(key)` (Data/Editor/ContentLocalization.cs:106) — it never looks at a locale table. Cross-referencing `Content Shared Data.asset` (212 keys) against `Content_ru.asset` (205 entries) leaves 7 required keys with no RU row at all: item.oaken_charm.name/.desc, item.swift_boots.name/.desc, item.war_banner.name/.desc, species.goblins.desc. All three ItemData assets exist and are registered (only referrer: ScriptableObjects/Database/ContentDatabase.asset). The one test that does read values, KeywordContentTests.cs:49-51, applies this check to keywords ONLY (`string.IsNullOrEmpty(ContentLocalization.GetValue(Ru, kw.Id + "." + NameSuffix))`), proving the pattern was available and simply not generalized.
```

**Чем стреляет.** The HARD rule is 'RU filled, others dashes', and the only automated guard verifies the weaker half. An empty RU cell resolves to string.Empty at LocalizationService.cs:66, and the two readers differ: LoadoutViewModel.cs:81 shows a blank label, LoadoutHubViewModel.cs:77 has a fallback. So the failure mode is a silently nameless item, discovered only by looking at it.

**Куда править.** Extend the test to assert a non-empty value for the run's authoring locale — the KeywordContentTests loop already does exactly this — and fill the seven cells (or delete the three unreachable item assets, which no preset, vessel or acquisition path references).

---

### LT-9 · P2 · architecture — Event localization suffixes have two owners: TextEventData builds them at runtime, ContentLocalization builds them again in the editor, bypassing the ContentKeys single-owner rule

`Assets/_Project/Scripts/Data/Definitions/TextEventData.cs:31` · линза `localization-text`

```csharp
Runtime owner: `public string TitleKey => Id + ".title";` / `BodyKey => Id + ".body";` / `ChoiceLabelKey(int index) => $"{Id}.choice{index}.label";` / `ChoiceResultKey(int index) => $"{Id}.choice{index}.result";` (cs:31-40). Editor owner: ContentLocalization.EventSuffixes (cs:65-76) re-derives the same strings from different literals — `{ "title", "body" }` and `suffixes.Add($"choice{i}.label"); suffixes.Add($"choice{i}.result");` — and RequiredSuffixes routes the whole `event` domain there (cs:52-53). This is the one domain that escapes ContentKeys, whose own docstring states the rule: «Суффиксы записаны здесь один раз; редакторная политика ссылается сюда, чтобы 'что создаём' и 'что читаем' не разъехались» (ContentKeys.cs:11-12), and ContentLocalization.cs:19-23 re-declares NameSuffix/DescSuffix from ContentKeys precisely to avoid this.
```

**Чем стреляет.** Two places compute one fact — the shape of an event's keys. Renaming `choiceN.label` on either side leaves the validator green and the screen blank: EventScreenView.cs:69 falls back to `$"Вариант {i + 1}"` for the label and to `string.Empty` for the result text (cs:70), so a divergence shows up as generically-named buttons with no consequence text rather than as an error. The 8 keys of event.wandering_merchant are currently correct only because both literals happen to match.

**Куда править.** Move the four event suffixes into ContentKeys (TitleSuffix/BodySuffix/ChoiceLabelSuffix(i)/ChoiceResultSuffix(i)) and have both TextEventData and ContentLocalization.EventSuffixes call them, exactly as the name/desc suffixes already do.

---

### R1-05 · P2 · correctness — Effects are keyed by Def alone, so a second caster silently inherits the first caster's attribution and potency

`Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs:105` · линза `combat-sim`

```csharp
RuntimeEffect existing = FindEffect(target, def);
if (existing != null)
{
    ApplyStacking(existing, def, source, target, combat);
    return;
}
```

**Чем стреляет.** FindEffect compares only `effects[i].Def == def` (line 370-378), so in 4-player co-op with duplicate relics (a case the code explicitly anticipates elsewhere — RuntimeUnit.cs:147-150 keeps a per-projectile refcount precisely for «два одинаковых крио/кооп-дубля») two Cryomancers or two Flame Swordsmen collapse into one RuntimeEffect owned by whoever applied first. RefreshDuration then makes it worse by mixing owners inside one call: `ResolveDurationTicks(def, source, target)` (line 418) uses the NEW caster's ApplyDebuffEff, while `effect.Source` and the frozen `ScaledPotency` snapshot stay the OLD caster's. Net effect: player B's Burn ticks deal damage scaled by player A's stats, are attributed to A (so A's Lifesteal/AllyMend/UnitKilled reactives fire, B's don't), and B's debuff-duration stat silently retimes A's effect.

**Куда править.** Key the effect instance by (Def, Source) in FindEffect for non-self-sourced effects — or, if merging is intended, refresh `existing.Source` + recompute ScaledPotency when the refreshing source wins, so duration, potency and attribution all come from one owner.

---

### R1-06 · P2 · gap — ComputeChecksum covers only positions/HP/attack timers — ability cooldowns, effects, shields and resource are invisible to the desync probe

`Assets/_Project/Scripts/Combat/CombatSimulation.cs:525` · линза `combat-sim`

```csharp
for (int i = 0; i < _units.Count; i++)
{
    RuntimeUnit u = _units[i];
    hash ^= (ulong)(u.Id * 1000003);
    hash ^= (ulong)(long)(u.Position.x * 1000f) * 2246822519UL;
    hash ^= (ulong)(long)(u.Position.y * 1000f) * 3266489917UL;
    hash ^= (ulong)(long)(u.CurrentHP  * 100f)  * 668265263UL;
```

**Чем стреляет.** The hash omits CurrentShield, CurrentResource, ActiveEffects (count/RemainingTicks/Stacks/PeriodicTicks), and every AbilityRuntime.CooldownRemaining. That is exactly the state most likely to diverge, because ability cooldowns are the one clock in the sim kept in float seconds instead of int ticks — AbilitySystem.cs:34 `if (ability.CooldownRemaining > 0f) ability.CooldownRemaining -= dt;` — while AutoAttackSystem, EffectSystem and RuntimeEffect.PeriodicTicks all deliberately use integer ticks (RuntimeEffect.cs:44-47 states float accumulators break periodic determinism). A host/client whose cooldown accumulators diverge by one tick will cast at different ticks and produce a fully different battle, and SimSyncProbe will report checksums matching until the resulting position/HP drift finally shows up many ticks later, pointing the investigation at the wrong system.

**Куда править.** Convert AbilityRuntime.CooldownRemaining to int ticks (AbilityData.BaseCooldown × CooldownEff → RoundToInt AwayFromZero, like AttackTiming.RecoveryTicks) and fold cooldown ticks, shield, resource and per-effect RemainingTicks/Stacks into ComputeChecksum.

---

### R1-07 · P2 · complexity — Stats.RebuildCache allocates five arrays per dirty rebuild, on the tick's hot read path

`Assets/_Project/Scripts/Combat/Stats/Stats.cs:145` · линза `combat-sim`

```csharp
float[] flat        = new float[StatCount];
float[] percentAdd  = new float[StatCount];
float[] multAccum   = new float[StatCount];
float[] overrideVal = new float[StatCount];
bool[]  hasOverride = new bool[StatCount];
```

**Чем стреляет.** StatCount is 30 (StatType.cs), so each rebuild allocates 4×float[30] + bool[30] ≈ 0.5 KB, and rebuilds are driven by effect churn, not by frame boundaries: AddModifiersFrom/RemoveModifiersFrom set `_dirty = true` (lines 45, 138), and StatModifierComponent does both on every stack change through EffectSystem.Reapply. With BlazingBladesRamp that is one Remove+Add per auto-attack per swordsman (plus another `new StatModifier[]` in ScaleByStacks, StatModifierComponent.cs:33), and the next `Stats.Get` inside the same tick pays the five allocations. Every other hot path in this sim is deliberately zero-alloc via reused buffers (EffectSystem's five buffers, AutoAttackSystem._lineTargets, SeparationSystem._neighbors, SpatialHash's list pool), so this is the one place that quietly generates per-tick garbage.

**Куда править.** Allocate the five accumulator arrays once as readonly fields next to `_cache` and Array.Clear/refill them in RebuildCache; cache the ×Stacks modifier array on the RuntimeEffect instead of rebuilding it in ScaleByStacks.

---

### R1-08 · P2 · architecture — CombatSimulation creates an undestroyed ScriptableObject per instance; balance benches build one sim per matchup

`Assets/_Project/Scripts/Combat/CombatSimulation.cs:58` · линза `combat-sim`

```csharp
private readonly Data.Definitions.EffectData _airborneEffect =
    Data.Definitions.EffectData.CreateRuntime(
        "sys.airborne",
        Data.Definitions.EffectPolarity.Neutral,
        Data.Definitions.EffectTag.KnockUp | Data.Definitions.EffectTag.Control,
```

**Чем стреляет.** EffectData.CreateRuntime calls `CreateInstance<EffectData>()` (EffectData.cs:69), so this *instance field initializer* mints a live Unity Object for every CombatSimulation ever constructed, and nothing ever destroys it — in a class whose own docstring positions it as the POCO deterministic core (RuntimeUnit.cs:10: «POCO — без MonoBehaviour и ScriptableObject»). Balance/Editor/DuelMatrixBench.cs:99 constructs `new SimEnvironment(Seed, config)` inside RunDuel, which the matrix calls n² times over the relic list (14 relics → 196 sims → 196 leaked EffectData objects per report), on top of DpsBench/SurvivabilityBench doing one per unit. ArmorThornsComponent.CooldownMarker() (line 116) has the same shape but worse timing: the first thorns proc calls CreateInstance in the middle of a sim tick.

**Куда править.** Hoist the two system EffectData markers into a static lazily-built singleton (or a plain non-SO runtime effect definition type) shared by all sims, so construction is once per domain and the sim core stops minting Unity Objects.

---

### R1-09 · P2 · correctness — Positions are mutated after SpatialHash.Rebuild in the same tick, so DeathSystem.Remove no-ops and queries read stale cells

`Assets/_Project/Scripts/Combat/Systems/DeathSystem.cs:25` · линза `combat-sim`

```csharp
unit.IsDead = true;
unit.CurrentTarget = null;
spatialHash.Remove(unit);
```

**Чем стреляет.** SpatialHash.Remove derives the bucket from the unit's CURRENT position (`long key = CellKey(unit.Position);` SpatialHash.cs:45), so it only works if the position is unchanged since indexing. The tick order rebuilds the hash at CombatSimulation.cs:255 and then teleports units twice afterwards: AutoAttackSystem.Resolve calls `CombatPositioning.TeleportBehind(unit, target)` (AutoAttackSystem.cs:175) and VortexEntryComponent does the same during DrainEventQueue (VortexEntryComponent.cs:42), both writing `attacker.Position = target.Position + behindDir * offset` with no re-index. A blinking assassin that dies later in the same tick is therefore looked up in the wrong cell and never removed from the hash, and every radius query in the remainder of that tick — ArmorThorns retaliation, AllyMend's wounded-ally pick, MarkTransfer's nearest-enemy search, all of which run in DrainEventQueue after the teleport — sees the blinked unit at its pre-blink cell, silently including or excluding it from AoE.

**Куда править.** Give SpatialHash a `Move(unit, oldPos)` (or store the indexed cell key on RuntimeUnit) and call it from TeleportBehind and DisplacementSystem; failing that, re-Rebuild after DrainEventQueue and before DeathSystem.

---

### R1-10 · P2 · correctness — EffectSystem.Reapply drops stat modifiers when one effect carries more than one StatModifierComponent

`Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs:443` · линза `combat-sim`

```csharp
else if (components[i] is IRuntimeEffectComponent rc)
{
    rc.OnExpire(in ctx);
    rc.OnApply(in ctx);
}
```

**Чем стреляет.** Reapply iterates components and gives each the default OnExpire→OnApply pair, but StatModifierComponent keys its modifier group by the *effect*, not by the component: `ctx.Target.Stats.AddModifiersFrom(ctx.Effect, mods)` / `RemoveModifiersFrom(ctx.Effect)` (StatModifierComponent.cs:23, :28), and Stats.RemoveModifiersFrom removes ALL groups whose source matches (Stats.cs:133-140). With two StatModifierComponents on one EffectData, a single added stack runs: i=0 OnExpire (removes both groups) → i=0 OnApply (adds mods0) → i=1 OnExpire (removes the mods0 just added) → i=1 OnApply (adds mods1). The first component's modifiers are gone for the rest of the effect's life, silently, and only after the first restack. No shipped asset has two StatModifierComponents yet (checked all of ScriptableObjects/Effects — HuntersMark and SporeCloud have two components but only one is a StatModifier), so this is a landmine armed for the next authoring pass, not a live bug.

**Куда править.** Key the modifier group per component instead of per effect (e.g. AddModifiersFrom on a (Effect, componentIndex) token), or make StatModifierComponent implement IStackableComponent and adjust its group in OnStacksChanged instead of relying on the blind expire/apply default.

---

### R1-11 · P2 · correctness — AbilitySystem.ApplyDisplace dereferences a target the caller may legitimately have left null

`Assets/_Project/Scripts/Combat/Abilities/AbilitySystem.cs:128` · линза `combat-sim`

```csharp
RuntimeUnit anvil = NearestEnemyTo(target.Position, caster.Team, target, ctx);
Vector2 throwDir = anvil != null
    ? anvil.Position - target.Position
    : target.Position - caster.Position;
```

**Чем стреляет.** TryCast's null-target guard is deliberately narrow — `else if (data.AreaShape != AreaShape.Circle && target == null) return false;` (line 83) — and the two non-single-target modes set `target = null` on purpose (line 69-71: `isMassTag || isAllyAura ? null`). ApplyDisplace is then dispatched FIRST, ahead of the isMassTag/isAllyAura/Circle branches (line 95-104), so it is the only branch that receives a possibly-null target without checking. Any AbilityData authored with Displaces = true plus TargetMode = AllEnemiesWithTag/AlliesInRadius, or plus AreaShape = Circle, throws NullReferenceException on the first cast and kills the sim tick. Currently latent: WhirlMonk.asset is the only Displaces ability and uses _targetMode 1 (NearestEnemy) with _areaShape 0 (None), which the line-83 guard covers.

**Куда править.** Add `if (target == null || target.IsDead) return false;` in TryCast under `if (data.Displaces)` before the resource/cooldown is spent, so the guard sits with the other precondition checks rather than depending on AreaShape.

---

### R1-18 · P2 · convention — Settings volume labels are hardcoded Russian with no localization key at all

`Assets/_Project/Scripts/UI/MenuRouter.cs:454` · линза `ui-coordination`

```csharp
master.LabelText = "Общий";
music.LabelText  = "Музыка";
sfx.LabelText    = "Звук";
```

**Чем стреляет.** Every other label in this same method goes through `L(key, ru)` four lines below (`cardAnim.LabelText = L("ui.settings.card_anim", ...)`), and every other view in the slice uses the key+RU-fallback pattern. These three have no key, so they can never be translated and will be missed by any string-table sweep — a HARD project rule (loc keys for all player-facing text from day one).

**Куда править.** Route them through the same helper: `L("ui.settings.volume.master", "Общий")` etc., and add the keys to the UI string table with dashes for non-RU locales.

---

### R1-19 · P2 · complexity — CancellationToken registrations in Push/ShowAsync are never disposed — every screen shown in a run stays alive until the run ends

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:138` · линза `ui-coordination`

```csharp
if (ct.CanBeCanceled)
    ct.Register(() => RemoveScreen(screen)); // RemoveScreen идемпотентен (уже снят → no-op)
```

**Чем стреляет.** `ct` here is the run-scoped token (MenuRouter passes `req.Cancellation` for text events, node farewells, reward/shop/chest/camp — see MenuRouter.cs:635, 669, 625). The returned CancellationTokenRegistration is discarded, so the closure — and through it the UiScreen and its whole built VisualElement tree — is rooted on the token for the entire run even after the screen is popped. Same in ShowAsync (line 211-212). Over a 12-node act that pins every screen tree ever shown, and on cancellation it invokes dozens of no-op callbacks.

**Куда править.** Keep the registration and dispose it when the screen is removed: store `CancellationTokenRegistration` on the screen (or in a per-screen DisposableBag) and dispose it in RemoveScreen/Pop/PopAll.

---

### R1-20 · P2 · correctness — RefreshShell dereferences _topBar without the null guard Update has — NRE on every stack change if the topbar UXML is unassigned

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:450` · линза `ui-coordination`

```csharp
private void RefreshShell()
{
    if (_clock == null) return;
    ...
    _topBar.SetActiveMode(ActiveMode(phase));
```

**Чем стреляет.** `CreateAndPlaceTopBar` returns early when `_runModeBar == null` (line 320), leaving `_topBar` null; `Update` defends against exactly this (`if (_topBar == null || _clock == null) return;`, line 360) but RefreshShell does not. RefreshShell is called at the end of Start (line 266) and on every `_router.Changed`, so an unassigned serialized field turns every Push/Pop into a NullReferenceException — and it fires from inside UiNavigator.Changed, aborting the rest of the navigator's post-push work for other subscribers.

**Куда править.** Add `if (_topBar == null) return;` (after the backdrop publish, which does not need the topbar) or hard-fail in Start when `_runModeBar` is missing.

---

### R1-21 · P2 · complexity — Dead hub branch: OpenHub + LoadoutHubView + LoadoutHubViewModel are reachable only when a serialized field is unassigned

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:530` · линза `ui-coordination`

```csharp
if (_loadoutInventoryScreen == null) { _router.OpenHub(); return; } // фолбэк на старый хаб, если ассет не назначен
```

**Чем стреляет.** This is the only caller of MenuRouter.OpenHub (verified by grep across Scripts/, the other hits being UiPreviewCatalog and the type declarations). It costs a registered singleton (RootLifetimeScope.cs:99 `builder.Register<LoadoutHubViewModel>`), a constructor dependency `_hubVm` on MenuRouter, ~25 lines of rebuild-closure in OpenHub and the whole LoadoutHubView file — all kept alive to cover a misconfigured scene, which would be better surfaced as an error than silently rendered as a different screen.

**Куда править.** Drop the fallback (log an error if the asset is missing) and delete OpenHub/LoadoutHubView/LoadoutHubViewModel, or keep LoadoutHubView only for UiPreviewCatalog. Same file also has dead code in MenuRouter.cs:913 (`private static string Percent` — no callers).

---

### R1-26 · P2 · correctness — ScreenShake amplitude is broadcast to standby vcams whose decay only runs when Cinemachine happens to evaluate them

`Assets/_Project/Scripts/Presentation/Camera/ScreenShake.cs:58` · линза `presentation`

```csharp
if (stage != CinemachineCore.Stage.Finalize || _amplitude <= 0.0001f) return;
...
_amplitude = Mathf.Max(0f, _amplitude - decayPerSec * Time.unscaledDeltaTime);
```

**Чем стреляет.** CameraModeController.Shake (line 155) sets _amplitude on all four shakers ("активная тряхнётся, прочие вхолостую"), but decay lives inside PostPipelineStageCallback, which the pipeline only invokes for cameras it evaluates, and it uses Time.unscaledDeltaTime rather than the `deltaTime` the pipeline passes. WorldScene.unity sets StandbyUpdate: 2 (RoundRobin) on all vcams, so a standby camera gets ~1/3 of the callbacks and therefore decays ~3x slower than ShakeDecayPerSec = 2 promises; with StandbyUpdate = Never it would never decay. ResetShake is only called on OnBattleReset (CombatFeelDirector.cs:68). Failure: BattleEndShake = 0.75 fires, the player presses Tab or the map opens within the next second, and the freshly-live camera replays leftover shake it never earned.

**Куда править.** Decay with the `deltaTime` argument the pipeline supplies, and have CameraModeController.Shake target only the live shaker (or zero the others) instead of broadcasting.

---

### R1-27 · P2 · convention — Split damage number uses StartCoroutine/WaitForSeconds — untracked, survives battle reset, and violates the UniTask+token rule

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:344` · линза `presentation`

```csharp
if (shield > 0) StartCoroutine(DelayedNumber(anchor, "-" + hp, _damageColor, _splitDelay, hpScale));
else            SpawnNumber(anchor, "-" + hp, _damageColor, hpScale);
...
private IEnumerator DelayedNumber(Vector3 worldPosition, string text, Color color, float delay, float sizeScale = 1f)
{
    yield return new WaitForSeconds(delay);
```

**Чем стреляет.** This is the only coroutine left in the whole Scripts tree (grep for StartCoroutine returns exactly these two lines) in a project whose convention is UniTask with a threaded CancellationToken. It is also fire-and-forget: HandleBattleReset (line 132) destroys views, clears _views and cancels in-flight FloatingTexts, but cannot cancel this coroutine — a hit that split into shield+HP within _splitDelay of a reset pops a damage number from the dead battle at a stale world anchor. Each split hit also allocates a WaitForSeconds, and the delay is scaled time so it drifts under slowmo.

**Куда править.** Replace with `UniTask.Delay(TimeSpan.FromSeconds(_splitDelay), DelayType.UnscaledDeltaTime, cancellationToken: ct)` driven by a CancellationTokenSource the presenter cancels in HandleBattleReset and OnDisable.

---

### R1-28 · P2 · architecture — CombatPresenter subscribes to the sim in OnEnable behind a silent null guard — the exact injection-order trap its two siblings documented and moved to Start

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:95` · линза `presentation`

```csharp
private void OnEnable()
{
    if (_simulation == null) return;
    _simulation.OnUnitSpawned       += HandleUnitSpawned;
    ...
    EnsureStatusOverlay();
    EnsureVfx();
}
```

**Чем стреляет.** CombatPresenter is wired with RegisterComponentInHierarchy (CombatLifetimeScope.cs:161), so _simulation arrives during the scope's Build in Awake. WorldMapView.cs:151 and CameraModeController.cs:120 both carry explicit comments that OnEnable ran before injection and silently lost their subscriptions, and both moved to Start for that reason. Here the same race is papered over by an early return: if OnEnable wins the race the presenter never subscribes and never spawns a single UnitView, with no error logged — a battle that renders nothing. OnDisable has the mirrored guard, so nothing self-heals.

**Куда править.** Move the subscribe/unsubscribe pair to Start/OnDestroy like WorldMapView and CameraModeController, and make a null _simulation a Debug.LogError rather than a silent return.

---

### R1-29 · P2 · complexity — CombatVfx.Spawn allocates a closure per VFX spawn, so the zero-alloc pool GCs on every hit

`Assets/_Project/Scripts/Presentation/CombatVfx.cs:37` · линза `presentation`

```csharp
PooledVfx vfx = pool.Get();
_active.Add(vfx);
vfx.Play(worldPos, scale, dirDeg, layerId, data.SortingOrder, released =>
{
    _active.Remove(released);
    pool.Release(released);
});
```

**Чем стреляет.** The lambda captures `pool` (and `this`), so a fresh display class + delegate is allocated on every Spawn call. CombatPresenter spawns VfxHitSpark on every damage event, VfxImpactDust on every melee hit, VfxMuzzle on every projectile and VfxContactDust on every run start/stop — hundreds of allocations per battle inside the system whose stated purpose is pooling ("пул боевых VFX-префабов"). `_active.Remove(released)` is additionally a linear scan of the active list per completion.

**Куда править.** Cache one `Action<PooledVfx>` per pool (store it next to the pool in the dictionary value, or give PooledVfx a back-reference to its pool and a single shared release delegate), and swap `_active` for an index/swap-back removal.

---

### R1-30 · P2 · complexity — Projectile views are Instantiated and Destroyed per shot while text and VFX are pooled

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:224` · линза `presentation`

```csharp
var view = Instantiate(_bulletPrefab, origin, Quaternion.identity, transform);
...
if (_projViews.TryGetValue(_deadProj[i], out var pv) && pv != null) Destroy(pv.gameObject);
_projViews.Remove(_deadProj[i]);
```

**Чем стреляет.** Bullets are the highest-frequency spawn in a ranged fight (one per ranged auto-attack, several per second per archer), and each one is a full GameObject instantiate plus a Destroy on impact — the churn the file already avoids for FloatingText (ObjectPool, line 411) and VFX (CombatVfx). ProjectileView.Bind already re-initialises all of its state (_originOffset, tint, rotation), so it is pool-ready as written.

**Куда править.** Reuse the UnityEngine.Pool.ObjectPool pattern from EnsureTextPool for _bulletPrefab; Bind already resets everything a reused instance needs.

---

### R1-31 · P2 · complexity — UnitView pushes color, sorting order and two bar materials every frame for state that only changes on events

`Assets/_Project/Scripts/Presentation/UnitView.cs:356` · линза `presentation`

```csharp
private void Update()
{
    ApplyColor(); // вспышка + альфа инвиза видны даже в hitstop/паузе (единый писатель _sprite.color)
...
    if (_healthBar != null)
        _healthBar.UpdateBar(_unit.CurrentHP, _unit.Stats.Get(Data.Stats.StatType.MaxHP), _unit.CurrentShield);
```

**Чем стреляет.** ApplyColor writes `_sprite.color` unconditionally every frame (line 682) even when neither tint nor stealth changed; UpdateInterpolation writes `_sprite.sortingOrder` every frame (line 264); and both bars re-push four material floats per frame each (HealthBarView.PushDynamicProps 189, ManaBarView 120) plus a per-frame Stats.Get(MaxHP)/Stats.Get(MaxResource) poll — including for units whose mana bar is deactivated (ManaBarView.Bind line 84). With 20 units that is ~160 redundant material writes and 40 dirty-renderer writes per frame while MessagePipe DamageDealtEvent/UnitSpawnedEvent already exist to drive this on change. WorldMapView.Update does the same shape of work: UpdateFogReveal (line 733) does a GetPropertyBlock/SetPropertyBlock round-trip every frame even though fog is off by default (_fogOn = false, line 55) and _pawnAt only moves on a step; MenuBackdropView.LateUpdate (134) re-fits the quad and rewrites four shader floats every frame for an aspect that changes only on resize.

**Куда править.** Gate each writer on a change check: keep last-written color/order/frac and skip if unchanged; drive bar values from the damage/heal events instead of per-frame polling; in UpdateFogReveal and MenuBackdropView.Fit early-out unless _pawnAt / _camera.aspect actually moved.

---

### R1-32 · P2 · architecture — Shield colour has two owners: a scene-serialized field on the presenter and the design-system palette

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:34` · линза `presentation`

```csharp
[Tooltip("Цвет цифры урона по щиту (-N).")]
[SerializeField] private Color _shieldColor = new Color(0.4f, 0.7f, 1f);
...
// Цвет щита — общий из палитры (не зависит от принадлежности).
if (_colorPalette != null)
    view.SetShieldColor(_colorPalette.Shield);
```

**Чем стреляет.** The shield bar takes its colour from CombatColorPalette.Shield (0.62, 0.86, 1.0) while the shield damage number takes it from _shieldColor serialized on the scene component (0.4, 0.7, 1.0) — they already disagree, so the same shield reads as two different blues. Worse, _damageColor/_healColor/_evadeColor/_splitDelay/_localViewerTeam live on the scene component too, which is precisely the trap MapStyle.cs:9-13 documents as having cost two rounds of play-QA ("настройки, лежащие прямо на компоненте, уходят в сериализацию СЦЕНЫ, и тогда дефолты в C# перестают что-либо значить"). Editing the C# defaults here changes nothing in game.

**Куда править.** Move the four number colours and _splitDelay into CombatColorPalette / CombatFeelConfig and read Shield from the palette in both places; leave only object references on the component.

---

### R1-33 · P2 · complexity — UnitView is eight separable concerns in one 1118-line MonoBehaviour, five of whose public members have no callers

`Assets/_Project/Scripts/Presentation/UnitView.cs:300` · линза `presentation`

```csharp
public bool SpriteContainsWorldPoint(Vector2 world)
...
public bool TryGetSpriteBounds(out Bounds bounds)
...
public Vector3 HeadPoint => _headPoint != null ? _headPoint.position : transform.position;
public int BodySortingLayerId => _sprite != null ? _sprite.sortingLayerID : 0;
public int BodySortingOrder => _sprite != null ? _sprite.sortingOrder : 0;
```

**Чем стреляет.** Distinct responsibilities with line ranges: (1) 22-145 inspector config + ~40 feel state fields; (2) 146-245 sim binding + animator/visual init; (3) 247-271 per-frame interpolation, Y-sort, bar polling; (4) 273-354 socket & geometry query API; (5) 356-506 Update pump + animator state machine (UpdateAttackPhase/DriveAnimation/HashFor); (6) 508-593 attack/battle-end/finisher entry points (DriveFreeRun/DriveHoldHitFrame); (7) 595-672 facing + flip-squash tween; (8) 674-914 hit feel (flash MPB, squash, nudge, anticipation, lunge, composed scale, idle breath, contact dust, acquire tell); (9) 916-1035 death sequence + DeathShatter spawn; (10) 1037-1116 editor gizmos that call UnityEditor.Selection on every OnDrawGizmos. Concretely separable today: 674-914 + 595-672 into a `UnitFeel` sibling component (it already receives its config through ApplyFeelConfig and touches only _sprite, _squashTarget and _healthBar); 916-1035 into `UnitDeathSequence`; 273-354 into `UnitSockets` (its only external consumers are DeploymentController.cs:437/635 and CombatPresenter.cs:213/336/361/392); 1037-1116 into an editor-only companion. The five members quoted above are dead across the entire Assets tree.

**Куда править.** Delete the five dead members, then split the feel block (674-914 + 595-672) into a `UnitFeel` component and the death block (916-1035) into `UnitDeathSequence`; UnitView keeps binding, interpolation and the animator state machine.

---

### R1-39 · P2 · architecture — Rest-beat overlay is ScreenKind.Modal, so the whole Interlude phase runs with GameplaySuppressed=true and InputContext.Menu — the camera is dead exactly where the code says it must live

`Assets/_Project/Scripts/UI/MenuRouter.cs:708` · линза `di-lifecycle`

```csharp
var screen = new RouterResultScreen<bool>(ScreenKind.Modal, false, resolve =>
```

**Чем стреляет.** UiNavigator.SyncInput treats anything that is not a Sheet as modal: `bool modal = top != null && top.Kind != ScreenKind.Sheet; _input.GameplaySuppressed = modal; if (modal) { _input.SetContext(InputContext.Menu); return; }` (UiNavigator.cs:226-229). RunBeatStage.EnterRestBeat sets Phase=Interlude and immediately shows this two-button corner overlay, so for the entire rest beat pan/zoom return neutral (InputService.cs:155-160 gate on GameplaySuppressed) and DeploymentController.Tick early-returns. That directly contradicts WorldContextOf's stated intent two lines below: "Бой и передышка между узлами — один контекст: мир на экране, камера должна жить (осмотреть поле, досмотреть добивание, походить по арене)" (UiNavigator.cs:242-244). The comment at MenuRouter.cs:707 notes suppression is unchanged from Page but not that Interlude is the one phase that needs the world input alive.

**Куда править.** Give the rest-beat overlay ScreenKind.Sheet (it is a corner overlay with no scrim, `pickingMode` already lets clicks through elsewhere), or split ScreenKind's "hides pages" axis from its "suppresses gameplay" axis so a non-scrim modal can leave world input alive.

---

### R1-40 · P2 · convention — DeploymentController reads Keyboard.current and Camera.main directly, bypassing IInputService contexts and the injected camera rig

`Assets/_Project/Scripts/Game/DeploymentController.cs:594` · линза `di-lifecycle`

```csharp
private static bool ReadyPressed()
        {
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        }
```

**Чем стреляет.** Two concrete costs. (1) Enter is not in any InputActionMap, so InputService.SetContext cannot gate it: it fires in Map/Combat/None contexts alike, gated only by GameplaySuppressed — which is exactly the second trigger for the phantom-battle path above (Tick line 346 calls StartCombat directly). Rebinding and the "all input behind IInputService" rule are both bypassed. (2) `if (_camera == null) _camera = Camera.main;` (line 658) resolves the camera by tag even though `CameraModeController` is already injected (line 107) and owns the rig in WorldLifetimeScope; if the MainCamera tag is missing, ScreenToWorld returns raw screen pixels (line 659) and every pick/drag silently no-ops in a build with no error.

**Куда править.** Add a "Ready/Confirm" action to the Deployment map in InputService and expose it as an event on IInputService; take the camera from the injected CameraModeController (or register the Camera in WorldLifetimeScope) instead of Camera.main, and log loudly instead of returning screen coords.

---

### R1-41 · P2 · convention — InputService resolves the UI panel with FindFirstObjectByType<UIDocument>() and caches whichever it finds first — the whole UI-vs-world click split hangs on it

`Assets/_Project/Scripts/Game/Input/InputService.cs:173` · линза `di-lifecycle`

```csharp
if (_uiDoc == null) _uiDoc = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
                return _uiDoc != null ? _uiDoc.rootVisualElement?.panel : null;
```

**Чем стреляет.** This is gameplay-layer code in the root scope doing a scene scan, against the project's no-FindObjectOfType rule, and the panel it needs is the one UiRootBootstrap already owns (`[RequireComponent(typeof(UIDocument))]`, UiRootBootstrap.cs:21/194) and which is registered in DI via RegisterComponentInHierarchy (RootLifetimeScope.cs:105). `PointerOverUI` is the single seam that keeps deployment clicks from firing through panels (DeploymentController.cs:356, InputService.cs:196); if a second UIDocument ever exists in the loaded scenes (dev/preview overlays, a future world-space document) the scan can bind the wrong panel and Pick() answers about the wrong tree, with no error anywhere.

**Куда править.** Have UiRootBootstrap push its panel into IInputService (a `BindPanel(IPanel)` seam called in Start, cleared in OnDestroy), or inject the UIDocument-owning component; drop the scan.

---

### R1-42 · P2 · gap — Half the serialized configs in the lifetime scopes have runtime fallbacks, the other half will NullReference inside Configure and take the whole container down

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs:60` · линза `di-lifecycle`

```csharp
builder.RegisterInstance<IContentDatabase>(new ContentRegistry(_contentDatabase.Entries));
```

**Чем стреляет.** `_actConfig`, `_audioCatalog` (lines 67, 72) and CombatLifetimeScope's `_feelConfig`, `_classBalanceConfig` all get an explicit "пусто = игра не падает" fallback, but `_contentDatabase` here is dereferenced eagerly, and CombatLifetimeScope dereferences `_statsConfig.ArmorConstantK` (line 139) and `_simTuningConfig.ToSnapshot()` (line 141) the same way. An unassigned reference — a merge of the CoreScene/BattleScene YAML, a fresh scene, a prefab variant — throws inside Configure, which means no container at all: no IInputService, no MenuRouter, no GameFlow. UiRootBootstrap's only diagnosis for that state is the generic "Нет инъекции ... RootLifetimeScope?" warning (line 201), so the actual cause is a bare NRE in the console. There is no editor-time validation of these fields anywhere.

**Куда править.** Either give the three hard fields the same explicit guard + loud error as the soft ones, or add an OnValidate/EditMode guard test asserting every scope field in CoreScene/BattleScene is assigned (the project already uses guard tests for SO defaults).

---

### R1-43 · P2 · complexity — DeploymentController mixes six concerns behind four ad-hoc booleans; guild persistence and relic-equip routing are genuinely separate, and the mode flags duplicate BattleSession.Phase

`Assets/_Project/Scripts/Game/DeploymentController.cs:74` · линза `di-lifecycle`

```csharp
private bool _deploying;
        private bool _testZone; // QA #2: текущая расстановка — СЕРЫЙ полигон вне забега (не боевой узел, не построение)
        private BattlePhase _sandboxReturnPhase = BattlePhase.None; // куда вернуть фазу, выйдя из расстановки-без-боя
```

**Чем стреляет.** Responsibilities actually in this one class: (a) mode state machine for three modes encoded as `_deploying` + `_testZone` + `_encounter==null` + `_sandboxReturnPhase`, plus a shadow copy of the phase it also writes via `_session.SetPhase`; (b) pointer picking math (PickUnit/FigureHit/Overlaps/ScreenToWorld, lines 604-662); (c) drag/ghost/ring render commands (UpdateUnitRings/ShowDragGhost/DrawRelicDragGhost); (d) relic equipping from three different message paths (OnEquip, OnEquipAtCursor, OnRelicDrag) plus preview rebuild; (e) durable guild persistence — SetSlotPosition/SetSlotRelic/`_rosterDirty`/FlushRoster/Autosave (lines 552-688); (f) camera framing (FrameCameraForDeployment) and DeploymentView GameObject lifecycle. The two P1 bugs above are both direct consequences of (a): the mode is not representable, so guards test the wrong flag. (e) is the other clean seam — it is the only writer of RunState roster data from the arena and has nothing to do with pointer input.

**Куда править.** Extract the mode into one enum owned by a small DeploymentSession (Battle/Formation/Sandbox + return phase), and pull the guild-write path (UpdateSlotPos/EquipOn/FlushRoster) into a RosterEditor collaborator. Leave picking/ghost math where it is — that is cohesive.

---

### R1-44 · P2 · architecture — GlobalMessagePipe static provider is set on every container build but has zero consumers; with domain reload disabled it pins the previous play session's container

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs:199` · линза `di-lifecycle`

```csharp
var options = builder.RegisterMessagePipe();
            builder.RegisterBuildCallback(c => GlobalMessagePipe.SetProvider(c.AsServiceProvider()));
```

**Чем стреляет.** `GlobalMessagePipe` appears exactly once in Assets/_Project — this line. Nothing reads it, so it is a static service-locator hook contradicting the no-singleton rule while buying nothing. It is not inert either: ProjectSettings/EditorSettings.asset has `m_EnterPlayModeOptionsEnabled: 1` / `m_EnterPlayModeOptions: 1` (DisableDomainReload), and there is no [RuntimeInitializeOnLoadMethod] or static-reset hook anywhere in the tree, so between Stop and Play the static keeps the whole disposed DI graph alive until the next Configure overwrites it. `options` is also assigned and never used.

**Куда править.** Delete the RegisterBuildCallback and the unused `options` local (keep `builder.RegisterMessagePipe();`). If a global escape hatch is ever wanted, clear it in a scope Dispose/OnDestroy so it cannot outlive its container.

---

### R1-51 · P2 · correctness — MapGenerator's MaxEdgesPerNode cap is bypassed at width transitions — the default chest-waist node always gets 5-7 incoming edges

`Assets/_Project/Scripts/Guild/MapGenerator.cs:166` · линза `data-guild-balance`

```csharp
if (si == ws - 1 && ti == wt - 1) break;
                if (si == ws - 1) { ti++; continue; }   // источники кончились — идём по целям
                if (ti == wt - 1) { si++; continue; }   // цели кончились — идём по источникам
```

**Чем стреляет.** These two early-continue branches run BEFORE `canFan`/`canMerge` are consulted (lines 177-181), so the cap is only honoured while both pointers are mid-column. With the shipped defaults (MapGenConfig.cs:107 `new AnchorRule(7, MapNodeType.Chest, width: 1)` after a floor 6 rolled to MinColumnWidth 5..MaxColumnWidth 7), ConnectColumns runs with ws=5..7 and wt=1: ti==wt-1 on the first iteration, so si++ fires every step and the single chest node accumulates 5-7 incoming edges against MaxEdgesPerNode=4. This is the exact defect the comment at lines 170-173 says was fixed ("один узел набирал веер рёбер через полкарты — именно это делало карту нечитаемой"), and it reproduces on every generated act.

**Куда править.** Move the cap check ahead of the exhausted-pointer shortcuts, or when the narrow side is saturated stop adding edges to it (leave the remaining wide-side nodes to connect through an already-linked neighbour) — and add a generator test asserting incoming/outgoing ≤ MaxEdgesPerNode across an anchor width drop.

---

### R1-52 · P2 · gap — Act progression is unreachable: CurrentActIndex is never incremented and BeginAct's early return makes a second act's map impossible

`Assets/_Project/Scripts/Guild/RunStateService.cs:88` · линза `data-guild-balance`

```csharp
if (Current.Map != null && Current.Map.Nodes.Length > 0) return; // уже сгенерирована/загружена

            Current.RestartsRemaining = _config.RestartsPerAct; // пул перезапусков на акт (реш. №65)

            var rng = new XorShiftRng(unchecked((ulong)(Current.Seed + Current.CurrentActIndex)));
```

**Чем стреляет.** Grepping the whole project, RunState.CurrentActIndex is written nowhere and read in only two places: this sub-seed and UiRootBootstrap.cs:396 `_topBar.SetAct(run.CurrentActIndex + 1)` — so the top bar is hardcoded to "Act 1" and the sub-seed is always Seed+0. Worse, the moment someone does increment it, this guard fires (Map is non-null and full from act 1) and act 2 silently replays act 1's map with act 1's Cleared flags; nothing anywhere clears RunState.Map. GameFlow.RunActAsync also DeleteSave()s and returns to the main menu on EventOutcome.Completed, so the boss currently ends the whole run.

**Куда править.** Add an explicit `AdvanceAct()` on RunStateService that increments CurrentActIndex and resets Map/RestartsRemaining, and key BeginAct's idempotence on the act index rather than on "a map exists".

---

### R1-53 · P2 · gap — RunState.SchemaVersion is written into every save but never read — there is no migration hook

`Assets/_Project/Scripts/Guild/RunState.cs:78` · линза `data-guild-balance`

```csharp
public int    SchemaVersion = 1;
```

**Чем стреляет.** Project-wide grep finds this field only at its declaration (ContentDatabase.cs:28 is an unrelated SchemaVersion). RunStateService.Load() does `Current = _save.Load<RunState>(SaveKey)` with no version branch, and JsonFileSaveService uses JsonUtility, which silently ignores unknown JSON fields and leaves removed/renamed fields at their C# initializer value. So the first schema change ships as silent data corruption rather than a migration: e.g. a pre-RelicCapacity save loads with RelicCapacity=0, which makes RelicInventoryFull (RunStateService.cs:142) permanently true and TryAddRelic/UnequipRelic reject everything, with no error anywhere.

**Куда править.** Read SchemaVersion in RunStateService.Load(): if it is below the current constant, run a migration chain (or refuse the save and delete it with a player-visible message); add a test that loads a v0 fixture.

---

### R1-54 · P2 · correctness — ContentEditService documents "SerializedObject + Undo" but registers no Undo — every balance edit is irreversible

`Assets/_Project/Scripts/Data/Editor/ContentEditService.cs:17` · линза `data-guild-balance`

```csharp
/// этот — правкой полей внутри ассета (статы, кулдауны, поля эффектов). Всё через <see cref="SerializedObject"/>
    /// + Undo; каждая правка возвращает <see cref="Change"/> (было→стало) для аудита.
```

**Чем стреляет.** `grep -rn "Undo\."` over Assets/_Project/Scripts/Data and .../Balance returns nothing: every mutator (ScaleStat:85, SetStat:104/116, SetFloat:132, AddFloat:147, AddAbilityCooldown:161, SetEffectComponentFloat:185) calls so.ApplyModifiedProperties() + EditorUtility.SetDirty() with no Undo.RecordObject/RegisterCompleteObjectUndo. A scripted balance pass over N relics cannot be Ctrl+Z'd — after ContentEditService.Save() (AssetDatabase.SaveAssets) the only rollback is git, and the tool's own change log is a .md file outside the assets. The docstring makes the caller believe otherwise.

**Куда править.** Wrap each mutator in Undo.RecordObject(asset, "Balance edit") (or use so.ApplyModifiedProperties() after Undo.RegisterCompleteObjectUndo), or delete the "+ Undo" claim from the docstring so callers know to commit before running a pass.

---

### R1-55 · P2 · gap — ClassBalanceConfig has no asset-vs-code guard test — the cascade tests build their own profile table and would stay green on a wrong asset

`Assets/_Project/Tests/EditMode/Combat/ClassBaselineTests.cs:23` · линза `data-guild-balance`

```csharp
private static ClassBalanceConfig MakeConfig()
        {
            var cfg = ScriptableObject.CreateInstance<ClassBalanceConfig>();
            SetField(cfg, "_profiles", new[]
            {
                new ClassBalanceConfig.ClassProfile(UnitClass.Bruiser,  1.00f, 1.00f),
```

**Чем стреляет.** Every assertion about the 2000/3000/1300 HP grid runs against a table constructed in the test, never against Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset — the file that actually drives the game. ConfigValidationTests already establishes the right pattern for exactly this hazard (SimTuningConfig_MatchesCodeDefaults compares the committed asset field-by-field against SimTuning.Default), but nothing equivalent exists for ClassBalanceConfig or for its _baseHp/_baseMoveSpeed anchors. Delete a row from the asset and ClassBalanceConfig.GetMultipliers silently returns (1f, 1f) (ClassBalanceConfig.cs:63) — the whole class loses its identity, every test stays green, and the only symptom is play-QA feel.

**Куда править.** Add a test that loads the single ClassBalanceConfig asset and asserts BaseHp/BaseMoveSpeed plus one row per UnitClass value (no missing class, no duplicate, HpMult>0, MoveSpeedMult>0).

---

### R1-56 · P2 · convention — Tag names are player-facing but the "tag" domain is exempted from required loc keys, and the key is built by string concatenation

`Assets/_Project/Scripts/Data/Editor/ContentLocalization.cs:46` · линза `data-guild-balance`

```csharp
case "tag":
                case "ai_preset":
                case "encounter":
                case "battle_preset":
                case "vfx":
                    return Array.Empty<string>();
```

**Чем стреляет.** The policy comment justifies this with "tag / ai_preset — техническая таксономия и ИИ, игроку не видны", but LoadoutInventoryView.cs:337 renders tag names straight onto the inventory chips: `string name = L(t.Id + ".name", TagFallback(t.Id));` with TagFallback (line 478) stripping the "tag." prefix — i.e. a tag whose key is missing shows the raw latin snake_case id ("tank_buster") to the player. Because RequiredSuffixes returns empty for the domain, ContentValidationTests.RequiredLocalizationKeys_Exist can never catch it and CreateMissingKeys never creates it: every new TagData ships unlocalised by default and only manual authoring keeps it working. The key is also assembled by concatenation instead of ContentKeys.NameKey(t), so the one place that owns the {id}.name convention is bypassed.

**Куда править.** Move "tag" out of the exempt list (NameOnly at minimum) so validation and key creation cover it, and use ContentKeys.NameKey(t) at LoadoutInventoryView.cs:337.

---

### R1-65 · P2 · convention — Three MenuRouter fire-and-forget UniTaskVoid paths await with no CancellationToken at all

`Assets/_Project/Scripts/UI/MenuRouter.cs:824` · линза `cross-cutting`

```csharp
await _nav.ShowAsync(screen);
            req.OnDismiss?.Invoke();
```

**Чем стреляет.** ShowTitleCardAsync (line 824), ShowOutcomeAsync (line 840) and ShowMainMenuAsync (line 888) are launched via `.Forget()` (lines 812, 832, 849) and then await `_nav.ShowAsync(screen)` with no token, unlike every sibling which passes `req.Cancellation` (lines 625, 733, 769, 786, 804). Their completion is driven only by a user click, so a shutdown/scene teardown while the title card or outcome screen is up leaves the continuation to fire against a torn-down navigator and invoke OnDismiss/OnToMenu/OnChoice into a dead flow. It also means these three screens are the only ones a run-abort cannot close.

**Куда править.** Add a Cancellation field to OpenTitleCardRequest/OpenOutcomeRequest/OpenMainMenuRequest (or pass the router's own lifetime token) and thread it into `_nav.ShowAsync(screen, ct)` like the other six.

---

### R1-66 · P2 · correctness — UiRootBootstrap.RefreshShell dereferences _topBar without the null guard Update has, so a missing RunModeBar asset NREs on the very first frame

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:450` · линза `cross-cutting`

```csharp
_topBar.SetActiveMode(ActiveMode(phase));
            // Настройки — не режим, а модалка: у их таба своё состояние «нажат, пока меню открыто» (раунд 2, п.6).
            _topBar.SetMenuActive(_router.IsSystemMenuOpen);
```

**Чем стреляет.** CreateAndPlaceTopBar returns early when the serialized `_runModeBar` VisualTreeAsset is null (line 320), leaving `_topBar` null. Update explicitly guards for exactly that case (`if (_topBar == null || _clock == null) return;`, line 360), but Start calls RefreshShell() at line 266 immediately after InitTopBar, and RefreshShell is also wired to `_router.Changed` (line 238) — both hit line 450 unguarded. In any scene/preview stand where the topbar UXML is not assigned, the UI layer dies with a NullReferenceException at boot and every subsequent stack change.

**Куда править.** Add `if (_topBar == null) return;` after the `_clock` check at the top of RefreshShell, matching the guard already in Update.

---

### R1-67 · P2 · convention — Per-hit damage numbers use StartCoroutine + WaitForSeconds in the combat hot path instead of UniTask/LitMotion, and stall during hitstop

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:344` · линза `cross-cutting`

```csharp
if (shield > 0) StartCoroutine(DelayedNumber(anchor, "-" + hp, _damageColor, _splitDelay, hpScale));
...
        private IEnumerator DelayedNumber(Vector3 worldPosition, string text, Color color, float delay, float sizeScale = 1f)
        {
            yield return new WaitForSeconds(delay);
            SpawnNumber(worldPosition, text, color, sizeScale);
        }
```

**Чем стреляет.** This is the only coroutine left in Scripts/ and it fires on every hit that splits across shield+HP — each call allocates an enumerator plus a WaitForSeconds instance, in the same path the rest of the file deliberately keeps zero-alloc (see the pooled FloatingText, 'zero-alloc в бою' comment at line 404). WaitForSeconds is scaled time, and the very same handler drives hitstop (`view.OnHitstop(stop)`, line 309) via TimeScaleService, so the HP number is delayed by the slowmo factor instead of the intended 60 ms and can land visibly after the shield number's animation.

**Куда править.** Replace with `UniTask.Delay(TimeSpan.FromSeconds(_splitDelay), DelayType.UnscaledDeltaTime, cancellationToken: destroyToken)` (or a LitMotion delayed callback), threading the component's destroy token.

---

### R1-68 · P2 · complexity — RelicCardVisualRig reaches into UnitView private fields by string reflection across the UI↔Presentation assembly seam, with nothing guarding the names

`Assets/_Project/Scripts/UI/Components/RelicCardVisualRig.cs:181` · линза `cross-cutting`

```csharp
if (mb == null || mb.GetType().Name != "UnitView") continue;
                    System.Type t = mb.GetType();
                    var fh = t.GetField("_recommendedHeight", F);
                    if (fh != null && fh.GetValue(mb) is float h && h > 0.01f) height = h;
                    var ff = t.GetField("_feetPoint", F);
```

**Чем стреляет.** Guildmaster.UI.asmdef intentionally does not reference Guildmaster.Presentation, so the rig binds to UnitView by type NAME and to `_recommendedHeight`, `_feetPoint` (line 185) and `_sprite` (line 229) by field name. All three lookups fail silently — renaming or refactoring any of them in UnitView.cs leaves relic/reward cards quietly mis-framed and untinted with no compile error, and no test in Tests/EditMode covers this path. The reflection also runs per card `Acquire`, walking GetComponentsInChildren<MonoBehaviour>(true) each time.

**Куда править.** Put a tiny public read-only contract in Guildmaster.Core (e.g. `ICardFramingSource { float RecommendedHeight; Transform FeetPoint; SpriteRenderer Body; }`) that UnitView implements, and have the rig fetch that interface — no reflection, and a compile error when the shape changes.

---

### R1-69 · P2 · gap — Zero automated coverage of the VContainer object graph — the EditMode test assembly cannot even reference VContainer

`Assets/_Project/Tests/EditMode/Guildmaster.Tests.EditMode.asmdef:5` · линза `cross-cutting`

```csharp
"references": [
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "Guildmaster.Core",
    "Guildmaster.Data",
    "Guildmaster.Data.Editor",
    "Guildmaster.Combat",
    "Guildmaster.Presentation",
    "Guildmaster.Game",
```

**Чем стреляет.** There is no "VContainer" entry here and grepping Tests/** for `LifetimeScope`/`ContainerBuilder`/`VContainer` returns nothing, so none of the ~45 registrations in RootLifetimeScope.cs (lines 57-200) are ever built in a test. That is exactly the blast radius of the project's documented DI gotcha (a default ctor argument makes VContainer try to resolve it and kills the whole registration branch) — a failure that today can only be discovered by entering play mode and noticing a subsystem is silently missing. 348 EditMode tests cover sim/data/UI-navigator logic and zero cover wiring.

**Куда править.** Add "VContainer" to this asmdef and write one guard test that builds a ContainerBuilder with the same registrations (or resolves the scope's root types) and asserts every service interface resolves; it turns a play-mode-only mystery into a red test.

---

### R1-76 · P2 · complexity — ContentEditService (278 lines, 9 public write APIs) has zero callers, zero tests and no menu entry

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Editor/ContentEditService.cs:20` · линза `dead-and-bloat`

```csharp
public static class ContentEditService
...
        public static Change ScaleStat(UnitData unit, StatType stat, float factor)
        public static Change SetFloat(ScriptableObject asset, string propertyPath, float value)
        public static Change AddAbilityCooldown(UnitData unit, string abilityId, float delta)
        public static Change SetEffectComponentFloat(EffectData effect, string fieldName, float value)
        public static string WriteChangeLog(IEnumerable<Change> changes, string title)
```

**Чем стреляет.** `rg -w ContentEditService` over Assets/_Project returns only its own file (line 20 declaration, line 274 log string). Its declared twin ContentCrudService IS wired — ContentHubWindow.Browser.cs:342/353/364/377 call Create/Duplicate/FindUsages/TryDelete. So the read side (SimBench, Content Hub Balance) and the CRUD side have UI, while the write side has none: no [MenuItem], no test. Six methods reach fields by string propertyPath/fieldName, which will silently start returning SKIP the moment a serialized field is renamed, and nothing exercises them to notice.

**Куда править.** Either wire it where it was meant to live (a Content Hub Balance apply action or an Alebardium/Balance menu item) with one smoke test over ScaleStat + WriteChangeLog, or delete it and keep the read-only loop.

---

### R1-77 · P2 · gap — NullScreenShake is never registered — the documented 'no camera rig' fallback does not exist

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/Camera/NullScreenShake.cs:4` · линза `dead-and-bloat`

```csharp
public sealed class NullScreenShake : IScreenShake
    {
        public void Shake(float intensity) { }
        public void ResetShake() { }
    }
```

**Чем стреляет.** `rg -w NullScreenShake` finds two hits total: this declaration and the doc comment at IScreenShake.cs:5 promising 'если рига в сцене нет, регистрируется NullScreenShake'. The only registration is unconditional — WorldLifetimeScope.cs:41-42 `builder.RegisterComponentInHierarchy<Presentation.CameraModeController>().AsSelf().As<Presentation.IScreenShake>();`. With no rig in WorldScene that resolves to nothing, so CombatFeelDirector's IScreenShake injection (CombatFeelDirector.cs:36) fails and takes the whole registration branch down — exactly the crash the interface was introduced to prevent. The class is dead and the comment is a false guarantee.

**Куда править.** Either register the fallback (if the rig is absent, RegisterInstance<IScreenShake>(new NullScreenShake())) or delete NullScreenShake and remove the promise from IScreenShake's docstring.

---

### R1-78 · P2 · correctness — ArcanaTitle duplicated between the game and the UI preview stand, with different id-splitting semantics

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/UI/MenuRouter.cs:366` · линза `dead-and-bloat`

```csharp
private static string ArcanaTitle(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            int dot = id.LastIndexOf('.');
            string s = (dot >= 0 ? id.Substring(dot + 1) : id).Replace('_', ' ');
```

**Чем стреляет.** UiPreviewCatalog.cs:214-224 implements the same rule (TitleCase over Short(id), prefixed with 'The '), but its Short at line 506 uses `id.IndexOf('.')` (first dot) where MenuRouter uses LastIndexOf, and the empty-id case yields "The " instead of "—". Both are passed into the same LoadoutInventoryView.Build(titleOf:) parameter — MenuRouter.cs:274 vs UiPreviewCatalog.cs:188 — so the preview stand, whose whole job is to show what the game shows, renders a different card title for any id containing more than one dot, and differs on blank ids in every case. The same id-strip helper is a third copy at DevBattlePickerView.cs:89-94.

**Куда править.** Move ArcanaTitle and one id-domain-strip helper into a place both the UI assembly and DevTools already reference (the id/domain layer next to ContentDomains), and delete the copies.

---

### R1-79 · P2 · complexity — GuildmasterCommands: seven near-identical gm_spawn_* commands plus seven hardcoded relic refs, ~180 of 534 lines, while a data-driven picker already exists

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/DevTools/GuildmasterCommands.cs:243` · линза `dead-and-bloat`

```csharp
[Command("gm_spawn_spearman", "...")]
        public void SpawnSpearman(int enemies = 3)
        {
            if (_simulation == null) { Debug.LogWarning("...Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("...RuntimeUnitFactory не внедрён"); return; }
            if (_spearmanRelic == null) { Debug.LogWarning("...Не задан _spearmanRelic в инспекторе"); return; }
            ResetForNewBattle();
            _simulation.EnqueueUnitSpawn(_factory.Create(_spearmanRelic, null, team: 0, new Vector2(-5f, 0f)));
```

**Чем стреляет.** SpawnSpearman/Shepherd/Cryomancer/Defender/Ranger/Assassin/Monk (lines 243-422) repeat the same six-statement block seven times — three null guards, ResetForNewBattle, factory.Create(relic), a MakeDummy fan-out loop, _lastBattleSetup, Debug.Log — differing only in relic field, spawn x and spacing. Each also costs a serialized inspector slot (lines 21-40) that must be re-wired by hand in the scene. The class itself proves the data-driven alternative: MakeDummy (line 532) resolves 'enemy.training_dummy' from IContentDatabase with the comment 'Резолвится из контент-БД, поэтому не нужен serialized-ref в сцене', and DevBattlePickerView.Populate lists every BattlePresetData/EncounterData straight from the content DB. Adding an 8th hero today means editing C# and the scene instead of adding an asset.

**Куда править.** Collapse to one `gm_spawn(string relicId, int enemies)` resolving RelicData through the injected IContentDatabase (as MakeDummy does), keep the fan-out shape as a parameter, and delete the seven serialized RelicData fields.

---

### R1-80 · P2 · complexity — YSortSprite is a dead MonoBehaviour duplicating UnitView's Y-sort formula

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Presentation/YSortSprite.cs:25` · линза `dead-and-bloat`

```csharp
private void LateUpdate()
        {
            if (_renderer == null) return;
            _renderer.sortingOrder = -Mathf.RoundToInt((transform.position.y + _yPivot) * _precision);
        }
```

**Чем стреляет.** Its script GUID (0bbab7e7cb2205344a8faf17ee18db26, from YSortSprite.cs.meta) appears in no .prefab, .unity or .asset under Assets — nothing has the component attached — and no C# references the type. Its own docstring narrows it to 'пропов, декора, ручных объектов', a use that never materialised, while the live implementation is UnitView.cs:264 `_sprite.sortingOrder = -Mathf.RoundToInt(_renderPosition.y * YSortPrecision);`. Keeping it means two Y-sort conventions (a _precision default of 100 vs UnitView's YSortPrecision constant, plus a _yPivot offset UnitView lacks) that will disagree the first time someone attaches it.

**Куда править.** Delete YSortSprite.cs and its .meta. If prop sorting is wanted later, expose UnitView's precision constant and reuse it rather than re-deriving the formula.

---

### R1-81 · P2 · convention — Dev consoles locate the root DI scope by comparing GetType().Name to the string "RootLifetimeScope", duplicated in two files

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/DevTools/VisualFxCommands.cs:65` · линза `dead-and-bloat`

```csharp
foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>(FindObjectsSortMode.None))
            {
                if (scope.GetType().Name != "RootLifetimeScope" || scope.Container == null) continue;
                try { return scope.Container.Resolve(typeof(VisualToggles)) as VisualToggles; }
                catch { return null; }
```

**Чем стреляет.** MapDevCommands.cs:88-89 is the same scan with the same literal (`if (scope.GetType().Name == "RootLifetimeScope") return scope;`). A rename, or a subclass of RootLifetimeScope, silently turns both command families into 'Реестр эффектов недоступен' / 'Нет забега' with no compile error — and the swallowing `catch { return null; }` hides the real resolve failure on top. RootLifetimeScope is a type both files could reference (they already use Guildmaster.Game.Flow and Guildmaster.Presentation.Map), so the string is not working around an assembly boundary.

**Куда править.** Extract one internal DevScope.Root()/Resolve<T>() helper in DevTools using `scope is RootLifetimeScope` (typed, not name-matched) and have both command classes call it.

---

### RL-10 · P2 · gap — EventOutcome.Aborted is produced in five places and consumed nowhere — an aborted act returns to the main menu silently with the save intact

`Assets/_Project/Scripts/Game/Services/GameFlow.cs:185` · линза `run-loop-integrity`

```csharp
GameFlow.RunActAsync branches on only two of three outcomes: `if (result.Outcome == EventOutcome.Completed || result.Outcome == EventOutcome.PlayerDefeated) { await _outcomePresenter.ShowAsync(…); _runStates.DeleteSave(); }` (GameFlow.cs:185-189). Producers of the third: ActRunner.cs:43-45 ("карта не сгенерирована → Aborted"), 60-62 ("тупик … → Aborted"), 89-92 ("выбран недоступный узел → Aborted"), 104-108 (a node flow returned Aborted), BattleFlow.cs:39-43 (`preset == null`) and 48-52 ("некому запустить бой (боевой скоуп не поднят) → Aborted"), TextEventFlow.cs:29-34. Each ends as a Debug.LogWarning plus a return value the only caller drops; `EventResult.Aborted` (RunFlow.cs:24) has no other reader.
```

**Чем стреляет.** When the reachable producer fires — BattleFlow.cs:48, `!_session.RequestLaunch(_preset)`, i.e. BattleBootstrap has not bound launch because the combat-systems scope is not up or was disposed — the run vanishes into the main menu with no outcome screen, no player-visible message, and the autosave still on disk. «Продолжить» re-enters the same act (BeginAct is a no-op because the map exists: GameFlow.cs:167 → RunStateService.cs:88) and aborts again, so the button becomes an unbreakable bounce with nothing explaining it. GameFlow's own docstring at line 160 advertises the handling that is missing: "Aborted — сбой (пустая карта/тупик)".

**Куда править.** Give Aborted a handler: surface it (an error page, or at minimum a player-visible message) and decide the save policy explicitly — keep the save but mark the run unplayable so «Продолжить» is disabled, or delete it. If Aborted is meant to be impossible, delete the enum member and the five branches rather than leave unreachable warnings.

---

### RL-11 · P2 · dead — RunSingleBattleAsync is a second, divergent copy of the battle-node pipeline, reachable only from a dev flag, with two parameters nobody passes

`Assets/_Project/Scripts/Game/Services/GameFlow.cs:89` · линза `run-loop-integrity`

```csharp
`public async UniTask<EventResult> RunSingleBattleAsync(BattlePresetData preset, RewardTier tier = RewardTier.Battle, bool presentReward = true)` (GameFlow.cs:89-90) has exactly one caller — GameBootstrap.cs:71, behind the serialized dev flag `_runBattleFlowOnBoot` — always with defaults, so `tier` and `presentReward` are dead knobs. The body re-implements what BattleNodeFlow owns, differently: no `_runStates.AwardBattleReward()` (BattleNodeFlow.cs:50), no post-win beat and no `_continue.WaitForContinueAsync` bridge (BattleNodeFlow.cs:55-59), no restart pool (`new BattleFlow(preset, _session, _localPlayer)` at GameFlow.cs:96 omits the `tryConsumeRestart` argument that NodeResolver.cs:92-93 supplies), and it duplicates the arena teardown with a comment admitting the split: `// Арена живёт всё время после боя … в петле акта это делает RunBeatStage; здесь (dev-разрез одного боя) петли нет, поэтому возвращаем сами.` (GameFlow.cs:105-108).
```

**Чем стреляет.** Two owners of "run one battle node". Any change to the node contract — gold, the reward bridge, the restart pool, the phase the arena is left in — has to be made twice, and the dev path plays by different rules than the game, so a bug reproduced with `_runBattleFlowOnBoot` is not the code the player runs. The two default parameters make it look like a configurable API when nothing configures it.

**Куда править.** Delete RunSingleBattleAsync together with GameBootstrap's `_runBattleFlowOnBoot`/`_devStartPreset` branch, or reduce it to `new BattleNodeFlow(new BattleFlow(...), tier, _reward, _runStates, _continue).Run(ctx)` so there is one pipeline. Drop the unused `tier`/`presentReward` parameters either way.

---

### RL-17 · P2 · dead — EquipRelicAtCursorRequest is a dead second owner of "drop a relic card on a unit" — subscriber, handler and DI wiring, zero publishers

`Assets/_Project/Scripts/Game/DeploymentController.cs:561` · линза `run-loop-integrity`

```csharp
Repo-wide (all .cs/.uxml/.unity/.prefab) the only hits for the type are: its declaration in LoadoutMessages.cs:73, and DeploymentController.cs:45 `private readonly ISubscriber<EquipRelicAtCursorRequest> _equipAtCursorSub;`, :102 (ctor param), :137 `_equipAtCursorSubscription = _equipAtCursorSub.Subscribe(OnEquipAtCursor);`, :561 `private void OnEquipAtCursor(EquipRelicAtCursorRequest req)`. There is no `IPublisher<EquipRelicAtCursorRequest>` and no `new EquipRelicAtCursorRequest(` anywhere. Its docstring (LoadoutMessages.cs:67-69) claims a publisher that does not exist: "публикует UITK-панель расстановки на дропе карточки релика в поле".
```

**Чем стреляет.** The live path is RelicDragEvent's Drop branch (DeploymentController.cs:462-467), which computes the identical rule — `PickUnit(ScreenToWorld(_input.PointerScreenPosition))` then `EquipOn(target.Id, e.Relic)` — so the same decision has two implementations, one of which is unreachable. A future reader wiring relic drop from a new panel will find the seam whose comment promises it works, publish into it, and get a second (subtly different: no reject sound, no _relicDrag reset) code path. It also costs a mandatory constructor dependency that VContainer must resolve for a real gameplay EntryPoint.

**Куда править.** Delete the struct, the field, the constructor parameter, the Subscribe/Dispose pair and OnEquipAtCursor; RelicDragEvent.Drop is the single owner.

---

### RL-18 · P2 · dead — IScreenTransition.Cancel() is a documented run-abort path with zero production callers — the ink curtain survives the run it belonged to

`Assets/_Project/Scripts/Core/Flow/IScreenTransition.cs:94` · линза `run-loop-integrity`

```csharp
IScreenTransition.cs declares `void Cancel();` with the docstring "Оборвать переход и открыть кадр немедленно. Для выхода из забега: держать чернила на экране, когда мира под ними уже нет, нельзя." Repo-wide grep for `IScreenTransition|_transition|ScreenTransitionRunner|.Busy` finds exactly one production consumer, WorldMapView.cs:621/635 (`_transition.Busy`, `_transition.Play(...)`) — `Cancel()` is called only from ScreenTransitionRunnerTests. Nothing in GameFlow, ActRunner, MenuRouter or WorldMapController touches it.
```

**Чем стреляет.** The runner is a root-scope ITickable (RootLifetimeScope.cs:177) that by design outlives its requester. Sequence: click a map node → WorldMapView.BeginStep:635 starts a ~0.85s close (MapStyle TransitionInSeconds) → during it press ESC → «В главное меню» (the ink element is pickingMode Ignore, UiRootBootstrap.cs:293, so the topbar and ESC stay live under it) → _runCts cancels, the chooser unwinds, the map hides. The transition keeps ticking through In→Hold→Out, publishing progress=1 for the whole hold, so the main menu comes up under a fully opaque ink curtain for roughly a second, and CameraModeController.SurfaceMap only runs when the delayed OnStepCovered fires. The abort path exists, is named for exactly this, and no one calls it.

**Куда править.** Call `_transition.Cancel()` from GameFlow.RunActAsync's finally (the same place that does RequestReset/SetPhase(None)) — or delete Cancel() and the claim in its docstring.

---

### RL-19 · P2 · dead — The whole run-timer chain is dead: accumulated every frame, formatted, and written into a label the stylesheet sets to display:none

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:400` · линза `run-loop-integrity`

```csharp
UiRootBootstrap.cs:400 `_runElapsed += UnityEngine.Time.unscaledDeltaTime;` and :404 `_topBar.SetRunTime(FormatTime(_runElapsed));` run every frame of every run. RunModeBarView.cs:109 `public void SetRunTime(string timerText) => SetText(_runTimer, timerText);` with the comment "Время забега выключено (реш. 2026-07-20): узел скрыт классом, сеттер держит шов живым". RunModeBar.uxml:43 gives that label class `gm-runbar__runtime`, and components.uss:1743-1745 is `.gm-runbar__runtime { display: none; }`.
```

**Чем стреляет.** Deeper than 'the timer restarts at 00:00 on every main-menu visit' — the value is not merely wrong, it is never rendered at all, so nothing can ever reveal that it is wrong. Five members (_runElapsed, its reset at :398, FormatTime's second caller, SetRunTime, RunModeBarView._runTimer) plus a per-frame string allocation exist to feed an invisible element, and the 'seam' costs more than re-adding a label would.

**Куда править.** Delete _runElapsed, SetRunTime, _runTimer and the uxml node; if a run timer is wanted later, store elapsed seconds on RunState (where it survives «Продолжить») and add the label back then.

---

### RL-20 · P2 · dead — UiScreenContext is indirection nobody reads — both its members have zero call sites across six Build implementations

`Assets/_Project/Scripts/UI/Navigation/UiScreenContext.cs:15` · линза `run-loop-integrity`

```csharp
Repo-wide grep for `.Localize` and `ScreensLayer` returns only UiScreenContext.cs:15 and :22 — the declaration and the constructor assignment. Nothing ever reads either property. The object is nonetheless threaded through MenuRouter.cs:119 `_nav.Initialize(screensLayer, modalLayer, new UiScreenContext(screensLayer, key => _loc?.GetString(key)))`, stored as UiNavigator.cs:32 `private UiScreenContext _context;`, passed at UiNavigator.cs:127 `screen.Build(_context)`, and ignored by every implementation: MenuRouter.cs:145 `public override void Build(UiScreenContext ctx) => Root = _build();`, MenuRouter.cs:185, UiNavigatorTests.cs:66/73/97, ScrimPolicyTests.cs:33.
```

**Чем стреляет.** An abstract-method parameter that is always ignored is the most expensive kind of dead seam: every future screen class must accept and name it, and its docstring ("Здесь — только то, что нужно ВСЕМ экранам") invites the next author to add fields to a channel with no receivers. The localizer it carries is already delivered a second way — each router builder closes over `key => _loc?.GetString(key)` directly.

**Куда править.** Drop the parameter to `Build()` and delete UiScreenContext plus the third argument of UiNavigator.Initialize; reinstate it only when a screen actually reads from it.

---

### RL-21 · P2 · dead — Two run-topbar tabs, «Тактика» and «Компендиум», are rendered, clickable and wired to empty lambdas

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:333` · линза `run-loop-integrity`

```csharp
UiRootBootstrap.cs:333-334 `onTactics: () => { },       // задел под будущий экран AI-тактики` and `onCompendium: () => { },    // задел под компендиум`. RunModeBarView.cs:51-52 wires them: `WireMode("tactics", "ui.mode.tactics", "Тактика", onTactics); WireMode("compendium", "ui.mode.compendium", "Компендиум", onCompendium);` → `chip.RegisterCallback<ClickEvent>(_ => action?.Invoke())` (RunModeBarView.cs:75). RunModeBar.uxml:26-27 declares both chips with no hidden class, and components.uss:1688-1689 gives each a real icon.
```

**Чем стреляет.** Two of the five mode tabs in the permanent run shell are visibly enabled and do nothing on click — indistinguishable from a broken build. They also never receive `SetActiveMode` (UiRootBootstrap.ActiveMode:586-592 can only return "inventory"/"map"/"battle"), so they can never even light up. In the same shell, the outcome-screen soft-lock (finding above) has no escape partly because no tab clears the stack — these two are the natural candidates and are inert.

**Куда править.** Remove the two chips from RunModeBar.uxml, their USS icon rules, the two WireMode calls and the two empty lambdas until the screens exist; a placeholder that looks pressable is worse than an absent one.

---

### RL-22 · P2 · convention — Hardcoded player-facing placeholder text in the permanent run topbar: "Alebardium" and "· ASC IV", no loc key, no code owner

`Assets/_Project/UI/Screens/RunModeBar.uxml:9` · линза `run-loop-integrity`

```csharp
RunModeBar.uxml:9-10 `<ui:Label name="guild-name" text="Alebardium" class="gm-loadout__guild-name" />` and `<ui:Label name="guild-asc" text="· ASC IV" class="gm-loadout__guild-asc" />`. Repo-wide grep for `guild-name`/`guild-asc` finds only these two lines — RunModeBarView.cs queries `topbar-gold`, `topbar-act`, `topbar-floor`, `topbar-timer`, `battle-timer`, `topbar-hp`, `btn-start` (lines 40-46) and never touches either. Sibling labels in the same row DO have code owners and loc fallbacks (`SetAct` → `L("ui.run.act", "Акт")`, `SetFloor` → `L("ui.run.floor", "Веха")`).
```

**Чем стреляет.** Every frame of every run the player sees the studio name presented as their guild name and a fake ascension rank. This is untranslatable (no key), unreachable from RU/EN string tables, and lies about run state — there is no ascension system and RunState carries no guild name. It is also a third 'fact' about the run rendered from a source that is neither RunState nor a config.

**Куда править.** Either bind both labels from RunState through RunModeBarView with `ui.run.guild_name` / `ui.run.ascension` keys, or delete the two nodes until the guild-identity and ascension features exist.

---

### RL-23 · P2 · dead — WorldMapController.IsChoosing — the one honest answer to "is the loop waiting for a node right now" — has no readers

`Assets/_Project/Scripts/Game/Flow/WorldMapController.cs:46` · линза `run-loop-integrity`

```csharp
WorldMapController.cs:45-46 `/// <summary>Ждёт ли петля выбор узла прямо сейчас (узлы горят и кликаются).</summary>` `public bool IsChoosing => _choosable != null;`. Repo-wide grep for `IsChoosing` returns only this declaration — no consumer in Scripts or Tests.
```

**Чем стреляет.** The class is registered as the single owner of map display (RootLifetimeScope.cs:172, docstring: "Один владелец на ресурс: два независимых источника показа неизбежно разъезжаются флагами (цена этого урока — РАУНД 5 play-QA)"), and _choosable is the only variable that truly knows whether the act loop is parked in ChooseAsync. Meanwhile UiRootBootstrap decides map-related behaviour from a stack scan (MenuRouter.cs:250 `HasMapInStack => _nav.AnyScreen(s => s.ModeTag == "map")`, documented as distinguishing 'return to the loop map' from read-only viewing) — an approximation of the same fact that goes stale the moment the map Sheet is removed while _choosable is still set (GoToBattle at UiRootBootstrap.cs:554 does exactly that). The accurate owner is published and ignored; the inaccurate proxy is what the shell consults.

**Куда править.** Either expose IsChoosing to the shell (through IWorldMapView/a read-side seam) and delete HasMapInStack, or delete IsChoosing. Do not keep two answers to one question with the wrong one wired.

---

### RL-6 · P2 · architecture — "Is the act map on screen" has two owners; the main menu's PopAll tears down one of them behind the other's back

`Assets/_Project/Scripts/UI/MenuRouter.cs:881` · линза `run-loop-integrity` · переоформляет R1-14

```csharp
Owner A: `WorldMapController._visible` (WorldMapController.cs:28), protected by an equality early-out — `public void SetVisible(bool visible) { if (_visible == visible) return; …` (lines 64-65) — and the only thing that calls `_view.Hide()`, which is also what restores the camera (`_cameraModes?.ExitMap(); // вернуть взгляд туда, откуда пришли`, WorldMapView.cs:282). Owner B: `MenuRouter._mapSpaceScreen` (MenuRouter.cs:318), pushed by ShowMapSpace:329-336, nulled from the screen's own OnExit (`onExit: () => _mapSpaceScreen = null`, line 334). They are kept in step only by the event round-trip WorldMapSpaceChangedEvent → UiRootBootstrap.cs:250-255. MenuRouter.ShowMainMenuAsync:881 calls `_nav.PopAll();`, a UI-layer teardown that removes the map Sheet and fires OnExit, nulling owner B without telling owner A.
```

**Чем стреляет.** Repro: during a battle node press the «Карта» tab (`_visible` = true, Sheet pushed, camera in CameraMode.Map). The loop is inside flow.Run, so WorldMapNodeChooser is not awaiting and its `finally { _map.EndChoose(); }` (WorldMapNodeChooser.cs:39) — the only other clearer of `_visible` — does not run. ESC → «В главное меню»: GameFlow cancels, and ShowMainMenuAsync's PopAll drops the Sheet. Result: `_visible` is true with no map screen in the stack — the abandoned run's map stays rendered in the world and the camera stays parked in CameraMode.Map across the whole main menu, and any SetVisible(true) in that window is swallowed by the equality guard.

**Куда править.** Stop letting the UI layer destroy state the flow layer owns: route the main-menu cleanup through the owners (publish SetWorldMapRequest(false) / SetTestZoneRequest(false), or expose one run-UI reset that WorldMapController and DeploymentController subscribe to). Alternatively make WorldMapController the single owner and make the Sheet a projection that cannot be popped independently.

---

### RL-7 · P2 · legacy — MainMenuVisibilityChangedEvent is a MessagePipe round-trip inside one assembly that duplicates a bool, its documented consumer does not exist, and its handler's comment and log state the opposite of what it does

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:260` · линза `run-loop-integrity`

```csharp
Three defects on one seam. (1) Duplicate fact: `MenuRouter._mainMenuOpen` (MenuRouter.cs:148, written at 885 and 896) is published and copied into `UiRootBootstrap._mainMenuOpen` (UiRootBootstrap.cs:132, written at 263) — both classes live in Guildmaster.UI and the publisher is already a constructor dependency of the subscriber (`_router`), which the bootstrap otherwise queries directly (`_router.IsInventoryOpen`, `IsMapSpaceOpen`, `HasVisiblePage`, `IsSystemMenuOpen`). (2) Dead rationale: the message's remarks say it sits in Core so the presentation layer can hear it ("слушает презентационный слой", MenuVisibilityMessages.cs:4-12), but a repo-wide grep shows exactly two consumers — the publishing router and the bootstrap; MenuBackdropView subscribes to ScreenBackdropChangedEvent instead (MenuBackdropView.cs:45,65). (3) The handler lies: UiRootBootstrap.cs:259-264 reads `// Главное меню открыто → гасим непрозрачную подложку` and logs `backdrop {(e.Visible ? "off" : "on")}`, while the RefreshShell it then calls computes `bool needBackdrop = (_mainMenuOpen || _router.HasVisiblePage) && …` (line 440) — main menu open ⇒ backdrop ON, which RefreshShell's own comment at 429-436 confirms is the intended behaviour.
```

**Чем стреляет.** A developer reading the trace while chasing a backdrop bug is told "backdrop off" on the exact frame the code turns it on; the comment is a leftover from before QA #50 moved the backdrop from a UI fill to the presentation table. The two mirrored bools stay in step today only because both writes sit in one try/finally — any future route into the main menu desyncs the topbar-visibility gate (UiRootBootstrap.cs:366 `bool runActive = run != null && !_mainMenuOpen;`) from the router's ESC gate (MenuRouter.cs:384 `if (_mainMenuOpen) return;`).

**Куда править.** Delete MainMenuVisibilityChangedEvent, the publisher field, the subscriber field and the subscription; expose `MenuRouter.IsMainMenuOpen` and read it in RefreshShell/Update like the other four router flags. Delete the stale comment and the inverted UiTrace line. Also delete `OpenMainMenuRequest.OnSettings` (MainMenuMessages.cs:27) — MainMenuPresenter.cs:27 always passes `null` and MenuRouter builds its own OpenSettingsFromMainMenu (MenuRouter.cs:859,874), so the field is never read.

---

### RL-8 · P2 · dead — MenuRouter carries four unreferenced members, one of them a decision nothing makes

`Assets/_Project/Scripts/UI/MenuRouter.cs:392` · линза `run-loop-integrity`

```csharp
Verified by repo-wide grep over .cs, .uxml, .uss, .prefab and .unity: (a) `private void CloseAll() => _nav.PopAll();` (line 392) — zero call sites; the only other occurrences of the name are two comments (417, 424) explaining why it is NOT used, and its doc claims it is the "внутренний close-callback текстового ивента", which BuildTextEventScreen:638-662 does not use. (b) `private static string Percent(float v01)` (line 913) — zero call sites. (c) `public bool IsOpen => _nav.IsOpen;` (line 72) — zero call sites (UiNavigatorTests asserts on nav.IsOpen, not the router). (d) `public bool HasMapInStack => _nav.AnyScreen(s => s.ModeTag == "map");` (line 245) — its only three references are inside UiTrace.Log interpolations (UiRootBootstrap.cs:529,546,556), i.e. a trace that is off.
```

**Чем стреляет.** (d) is the misleading one: its summary says it "Отличает «вернуться на карту петли» (выйти из боя) от read-only просмотра (нет карты петли)" — a branch that no longer exists, since UiRootBootstrap.GoToMap:554-563 calls RequestWorldMap(true) unconditionally. Someone implementing the next map behaviour will wire it to this property believing the distinction already works, when it is only ever evaluated to build a string nobody prints.

**Куда править.** Delete all four members and drop `hasMap={_router.HasMapInStack}` from the three UiTrace lines.

---

### RL-9 · P2 · dead — Map edge direction has two contradictory owners: the generator stores one-way edges while the view's dedup and the traversal's docs assume both directions

`Assets/_Project/Scripts/Game/Flow/WorldMapController.cs:157` · линза `run-loop-integrity`

```csharp
MapGenerator stores each edge ONE way, parent→child: `AddEdge(edges, source[si].Id, target[ti].Id);` (MapGenerator.cs:161, source = column c, target = column c+1) and AddEdge only appends to `edges[from]` (MapGenerator.cs:195-204); MapNode has a single `public string[] Edges` and no reverse list (RunState.cs:36). WorldMapController.BuildEdges:148-163 asserts the opposite and pays for it with a guard that can never fire: `// Ребро рисуем один раз: граф хранит связь с обеих сторон, и без этого каждая линия шла бы дважды.` plus the `seen` HashSet and ordinal key. MapTraversal.AvailableNext documents the undirected reading too — "соседи по MapNode.Edges текущего узла, ещё не пройденные" (MapTraversal.cs:26-28) — while its correctness depends entirely on the edges being directed.
```

**Чем стреляет.** The dedup HashSet and its key construction are dead code born of a wrong belief about the data, and they make the wrong belief look verified. The latent cost is concrete: the field is named `Edges`, the view's comment says the graph is bidirectional, and AvailableNext filters only on `!node.Cleared`, never on `Floor`. The first change that makes the adjacency symmetric (the natural reading, and what any "draw edges from either endpoint" refactor implies) instantly lets the player walk BACKWARD down the act: standing on a floor-2 node whose parents are A1 (cleared) and A2 (not taken), AvailableNext returns A2, WorldMapController.StateOf:169 lights it as Available, CanEnter passes, and Advance:53-59 sets CurrentNodeId back to floor 1 and it is autosaved (ActRunner.cs:121-122).

**Куда править.** Delete the `seen`/key dedup in BuildEdges and correct its comment to say the graph is directed parent→child. Then enforce the invariant instead of assuming it: filter AvailableNext on `node.Floor > current.Floor` (or rename the field `Children`), and add an EditMode test that no generated edge points to a lower floor.

---

### TS-10 · P2 · convention — Six hand-rolled ISaveService doubles, five byte-identical, all reference-based — the run-loop tier cannot see a serialization or save-ordering bug

`Assets/_Project/Tests/EditMode/Guild/ActRunnerTests.cs:229` · линза `tests-as-subject`

```csharp
`grep -rn "ISaveService" Assets/_Project/Tests` finds six independent implementations: InMemorySave (ActRunnerTests.cs:229), and five byte-identical `MemSave` classes in BattleNodeFlowTests.cs:100, GuildRosterTests.cs:130, RunStateEquipTests.cs:121, RunStateRestartTests.cs:55, ShopControllerTests.cs:150 — all exactly `private readonly Dictionary<string, object> _s = new(); public void Save<T>(string key, T value) => _s[key] = value; public T Load<T>(string key) => _s.TryGetValue(key, out var v) ? (T)v : default;` etc. — plus a null implementation FakeSave (EventEffectApplierTests.cs:111) that returns default and never stores. The shipped service is JsonFileSaveService (JSON via JsonUtility to disk), exercised by exactly one test: JsonFileSaveService_DiskRoundTrip (RunStateSaveTests.cs:54).
```

**Чем стреляет.** Two costs. First, one interface change means editing seven files. Second and worse, `_s[key] = value` stores the *live reference*: RunStateService.Autosave() (RunStateService.cs:131-133) passes `Current`, so `Load<RunState>("run")` hands back the very object the test is still mutating. That makes the whole class of "we autosaved at the wrong moment" bugs invisible — RunAct_Autosaves_DuringTraversal (ActRunnerTests.cs:168) can only assert `_save.Exists("run")`, never that the snapshot matched the state at that instant. It also hides anything JsonUtility would drop; the one round-trip test that does go through JSON (RunStateSaveTests.cs:16) sets CurrentActIndex=2, Difficulty=1 and AiPresetId="ai_preset.spearman" and then asserts none of them back, so even there act progression is unverified.

**Куда править.** Extract one shared test double into the EditMode assembly (e.g. Tests/EditMode/Support/FakeSaveService.cs) that round-trips through JsonUtility exactly as JsonFileSaveService does — `_s[key] = JsonUtility.ToJson(value)` on Save, FromJson on Load. Delete the six copies. Then extend RunState_JsonRoundTrip_PreservesFields to assert every field it sets, CurrentActIndex first.

---

### TS-11 · P2 · truth — ContentValidationTests re-implements the id-format regex and the domain rule that ContentValidationService owns

`Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:18` · линза `tests-as-subject`

```csharp
ContentValidationTests.cs:18 `private static readonly Regex IdFormat = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.Compiled);` is a character-for-character copy of ContentValidationService.cs:32 `private static readonly Regex IdFormat = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.Compiled);`. The domain rule is duplicated too: the test does `string expectedDomain = ContentDomains.GetDomain(def.GetType()); Assert.IsTrue(def.Id.StartsWith(expectedDomain + "."))` (lines 33-35) while ContentValidationService.ValidateIdString performs the same two checks (ContentValidationService.cs:39 onward) and is separately unit-tested by ContentValidationServiceTests.cs:112-136 against string literals only, never against a shipped asset.
```

**Чем стреляет.** One fact — what a legal content id looks like — with two owners, and each owner is tested only against itself. Loosen the regex in ContentValidationService and Content Hub starts accepting ids that ContentValidationTests will still reject from its private copy, so the commit that broke the hub goes in green and the failure surfaces later from an unrelated test. Tighten the test's copy and the hub silently disagrees the other way. The two suites are also complementary in the wrong direction: the service test never sees a real asset, and the asset test never calls the service.

**Куда править.** Delete the regex and the domain check from ContentValidationTests.cs:18 and 31-35 and have AllContent_HasValidId call `ContentValidationService.ValidateIdString(def.Id, def.GetType())` and assert the result is empty. One owner, and the asset sweep and the unit tests then guard the same rule.

---

### TS-12 · P2 · gap — Four tests in the suite are structurally incapable of failing

`Assets/_Project/Tests/EditMode/Core/XorShiftRngTests.cs:180` · линза `tests-as-subject`

```csharp
(1) SimConstantsTests.TickDelta_IsReciprocalOfTickRate: `Assert.AreEqual(1f / SimConstants.TickRate, SimConstants.TickDelta)` where SimConstants.cs:13 is literally `public const float TickDelta = 1f / TickRate;` — it compares an expression to its own definition. Its fixture docstring claims "защита от случайного рассогласования частот", and no test anywhere pins TickRate == 30 (the project's HARD 30 Hz invariant); `grep -rn "TickRate" Assets/_Project/Tests` shows every other use is `for (…; i < SimConstants.TickRate; …)`, i.e. parametric. (2) StatMathTests.BuildEffective_MatchesDirectStatsBake (line 60) calls `StatMath.BuildEffective(relic, config)` with classConfig omitted, so ClassBaseline.Apply and EnemyScalers.Apply are both no-ops (StatMath.cs:19-27) and BuildEffective reduces to exactly the `new Stats(config); expected.AddModifiersFrom(relic, mods)` the test compares it against. (3) AttackTimingTests.WindupTicks_IsDeterministic (line 114) calls a pure static function five times with identical arguments. (4) ConfigDiffTests.FreshInstance_HasNoDiff (line 72) asserts Diff is empty for a fresh instance, while ConfigDiff.Diff builds its baseline as `ScriptableObject.CreateInstance(asset.GetType())` (ConfigDiff.cs:24).
```

**Чем стреляет.** Each occupies the slot a real guard should hold. The StatMath one is the costly case: its fixture header promises the contract "таблица не врёт" — that Content Hub's numbers come out of the same pipeline as the sim — but because classConfig defaults to null in both tests, the class/species cascade (the only part of BuildEffective that can actually diverge from RuntimeUnitFactory.cs:69-75, and the part the sole production caller ContentIndex.cs:126 always passes) is never compared at all. A Content Hub table showing 1200 HP for a Tank the game builds at 3000 passes both StatMath tests. The TickDelta one leaves the headline determinism constant unpinned while looking like it pins it.

**Куда править.** Replace TickDelta_IsReciprocalOfTickRate with `Assert.AreEqual(30, SimConstants.TickRate)` and `Assert.AreEqual(1f/30f, SimConstants.TickDelta, 1e-7f)`. Give BuildEffective_MatchesDirectStatsBake a real ClassBalanceConfig plus an EnemyData with species scalers and compare against RuntimeUnitFactory.Create's output, not a hand-rolled bake. Delete WindupTicks_IsDeterministic and FreshInstance_HasNoDiff, or reshape the latter to diff the shipped StatsConfig.asset against code defaults (which would immediately catch the _attackSpeedMax 4-vs-2.5 drift).

---

### TS-16 · P2 · correctness — StatMathTests.AttacksPerSecond_MatchesTickQuantization re-types the body of the method it is testing

`Assets/_Project/Tests/EditMode/ContentHub/StatMathTests.cs:28` · линза `tests-as-subject` · переоформляет R1-74

```csharp
Test: `int interval = AttackTiming.IntervalTicks(speed); float expected = (float)SimConstants.TickRate / interval; Assert.AreEqual(expected, StatMath.AttacksPerSecond(speed), 1e-4f)`. Implementation (StatMath.cs:33-35): `int interval = AttackTiming.IntervalTicks(attackSpeed); if (interval <= 0 || interval == int.MaxValue) return 0f; return (float)SimConstants.TickRate / interval;`. Identical expression, same two symbols.
```

**Чем стреляет.** A wrong quantization stays green twice. If AttackTiming.IntervalTicks starts rounding the other way (ceil vs floor), both sides move together and the assertion holds — while the Content Hub's DPS column and the sim's real cadence would still be consistent with each other but wrong versus the GDD. The test name promises it matches tick quantization; it only proves the two expressions were typed the same day. Round 1 (R1-74) reported the StatMath/UnitStatPreview duplication; this is the test that was supposed to catch it and structurally cannot.

**Куда править.** Replace the recomputation with pinned pairs from the sim contract at 30 Hz: speed 1.0 → 30 ticks → 1.0 aps; speed 2.5 → 12 ticks → 2.5 aps; speed 0.9 → 33 ticks → 0.909… aps. Numbers derived from the GDD/sim, written as literals, so a change in IntervalTicks has to be argued for.

---

### TS-17 · P2 · architecture — The sim's two wiring constants (ArmorK, SpatialHash cell size) are copied into 17 test files; the assets that own them are pinned by nothing

`Assets/_Project/Tests/EditMode/Combat/EffectTestSupport.cs:269` · линза `tests-as-subject`

```csharp
`public float ArmorK => 100f;` in the shared mock, repeated as a literal or const in AssassinSliceTests.cs:21+250, DefenderSliceTests.cs:23+270, ShepherdSliceTests.cs:29+319, SpearmanSliceTests.cs:253, WindupAutoAttackTests.cs:346, MonkSliceTests.cs:21, RangerSliceTests.cs:21, CombatSimulationTests.cs:19, DamagePipelineTests.cs:15, DotBattleIntegrationTests.cs:20, BattleIntegrationTest.cs:19 — plus two production copies: SimEnvironment.cs:22 `private const float DefaultArmorK = 100f;` and ContentAuditor.cs:42 `config != null ? config.ArmorConstantK : 100f`. The single real owner is StatsConfig.asset `_armorConstantK: 100`, read exactly once at CombatLifetimeScope.cs:144 `.WithParameter("armorK", _statsConfig.ArmorConstantK)`. Same shape for cell size: `private const float CellSize = 3f;` in AssassinSliceTests.cs:22, CombatSimulationTests.cs:20, DefenderSliceTests.cs:24, DotBattleIntegrationTests.cs:19, MonkSliceTests.cs:22, RangerSliceTests.cs:22, ShepherdSliceTests.cs:30, BattleIntegrationTest.cs:18, versus the owner CombatLifetimeScope.cs:38 `_spatialHashCellSize = 3f` (CombatSystemsScene.unity:456: 3).
```

**Чем стреляет.** Retune armour in the asset — say K=60 to make armour bite harder — and the game changes while sixteen files keep asserting mitigation at K=100. DamagePipelineTests.PhysicalDamage_ArmorHalfsMitigation (line 72) would still prove "armour 100 halves damage", which would then be false in the shipped build; nothing in the suite reads StatsConfig.ArmorConstantK at all (only ContentValidationTests.cs:133 reads the attack-speed pair). Identically, widening the spatial-hash cell in the scene changes which neighbours the sim finds, and every slice test keeps querying a 3-unit grid.

**Куда править.** Give the tests one owner: a `SimTestConfig` helper in EffectTestSupport that loads StatsConfig.asset and reads ArmorConstantK and the scene's cell size once, and have every fixture use it. At minimum add the missing consistency assertion to ConfigValidationTests (StatsConfig.asset ArmorConstantK == the value the fixtures assume) so a retune goes red instead of silently invalidating sixteen files.

---

### TS-18 · P2 · gap — EffectSystem's stacking suite never applies one effect from two sources or an effect with two StatModifierComponents — the two stacking bugs round 1 found are invisible by construction

`Assets/_Project/Tests/EditMode/Combat/EffectSystemTests.cs:166` · линза `tests-as-subject`

```csharp
Every Apply call in the stacking suite passes the same unit as target and source: EffectSystemTests.cs:29, 45, 65, 79, 97, 116, 173, 187, 189, 203, 205, 223, 229 are all `sys.Apply(unit, def, unit, ctx)`; the only two-actor tests (lines 140, 157) apply once and assert duration, never a second source. EffectComponentTests.cs:37/55-57/78/99/117/136 and EffectDispelTests.cs:22-74 are the same shape. No test in the suite constructs an EffectData with two StatModifierComponents (grep of `StatModifierComponent` across Tests: every site passes a single instance). Meanwhile EffectSystem.Apply keys by definition alone — `RuntimeEffect existing = FindEffect(target, def);` (EffectSystem.cs:105) — and never rewrites `effect.Source`, and Reapply (EffectSystem.cs:442-467) walks components applying OnExpire→OnApply.
```

**Чем стреляет.** These are exactly the holes R1-05 (second caster inherits the first caster's attribution and frozen potency) and R1-10 (Reapply drops modifiers when one effect carries more than one StatModifierComponent) fall through. Concretely: two Cryomancers slowing the same target — the second cast's ApplyDebuffEff and stat snapshot are discarded and the kill credit stays with caster one; a single effect authored with both a MaxHP and a MoveSpeed StatModifierComponent loses one of them on restack. Both produce a wrong number in a live battle and the 13 stacking assertions stay green.

**Куда править.** Add two cases to EffectSystemTests: (a) apply the same StackRule.Stack def from source A then source B and assert the documented attribution/potency rule (whatever it is, pin it — today the code silently keeps A); (b) build a def with two StatModifierComponents, restack it, and assert both modifier groups survive.

---

### TS-19 · P2 · gap — MapGenConfig.MaxEdgesPerNode is a shipped, played knob with zero assertions anywhere in the suite

`Assets/_Project/Tests/EditMode/Guild/MapGeneratorTests.cs:219` · линза `tests-as-subject`

```csharp
`grep -rn "MaxEdgesPerNode\|Edges.Length\|Edges.Count" Assets/_Project/Tests` returns nothing. MapGeneratorTests covers determinism, endpoints, forward/backward reachability, depth, the chest/camp waist, warm-up zone purity, anchor floors, row contiguity, the shop fallback and the width profile (line 219, Generate_ActNarrowsAtBothEnds) — never fan-out. ActConfigAssetTests likewise checks Columns, Anchors, MinColumnWidth and per-floor widths, never edges. The knob is authored in the played asset (ActConfig.asset:21 `MaxEdgesPerNode: 4`), documented as a hard ceiling (MapGenConfig.cs:37-42: «веер шире четырёх перестаёт читаться и превращает карту в кашу») and clamped in Validated() (MapGenConfig.cs:60).
```

**Чем стреляет.** This is precisely the invariant R1-51 says the generator breaks at width transitions — the default chest-waist node collecting 5-7 incoming edges. The map is generated from the asset on every run and rendered by WorldMapView; a node with a 7-way fan is the "каша" the config exists to prevent, and no test can see it. A knob nobody asserts is a knob that will be tuned once and then quietly stop being honoured.

**Куда править.** Add one test to MapGeneratorTests over 20 seeds: for every node assert `node.Edges.Length <= cfg.MaxEdgesPerNode` AND that the in-degree (count of nodes whose Edges contain it) is also <= cfg.MaxEdgesPerNode — the docstring claims the cap covers both directions. It should go red today on the floor-7 chest.

---

### TS-20 · P2 · architecture — LoadoutLayoutInvariantsTests hand-rebuilds the grid that LoadoutInventoryView builds, so the layout it measures is not the layout that ships

`Assets/_Project/Tests/EditMode/LoadoutLayoutInvariantsTests.cs:61` · линза `tests-as-subject`

```csharp
The fixture reconstructs the view's assembly step by step: `scroll.mode = ScrollViewMode.Vertical; scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible; var grid = new VisualElement(); grid.AddToClassList("gm-loadout__grid"); scroll.Add(grid);` (lines 61-65), and then clones four RelicArcanaCard.uxml instances into it. LoadoutInventoryView owns the same four steps: `var grid = root.Q<ScrollView>("relic-grid")` (LoadoutInventoryView.cs:124), `grid.mode = ScrollViewMode.Vertical` (:130), `grid.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible` (:134), `gridEl.AddToClassList("gm-loadout__grid")` (:136). The test's own comment concedes the coupling: «Отступишь от этой сборки — тест начнёт мерить другую раскладку» (lines 57-60).
```

**Чем стреляет.** The assertions are about the alignment of elements the TEST built, so a change in the view alone cannot fail them. Concretely: drop `AlwaysVisible` in LoadoutInventoryView (the scrollbar then reserves no width) and the shipped toolbar goes ~13px out of alignment with the card row — the exact class of defect this fixture was written for after it survived three QA rounds — while all four tests stay green because the fixture still forces AlwaysVisible on its own copy. Two owners of one assembly recipe.

**Куда править.** Extract the ScrollView-mode + grid-container assembly into one internal static helper on LoadoutInventoryView (e.g. `BuildGridInto(ScrollView)`) and have both the view and the fixture call it, so there is one owner and the test measures the real construction.

---

### TS-21 · P2 · gap — The save tier has no negative test: no corrupt file, no partial write, no schema-version case — and RunState_JsonRoundTrip mirrors the service's own serializer

`Assets/_Project/Tests/EditMode/Run/RunStateSaveTests.cs:54` · линза `tests-as-subject`

```csharp
JsonFileSaveService_DiskRoundTrip (line 54) writes and reads back a happy-path RunState; RunState_JsonRoundTrip (line 16) asserts `JsonUtility.FromJson<RunState>(JsonUtility.ToJson(rs))` — the same two calls JsonFileSaveService.Save/Load make (JsonFileSaveService.cs:22 and :36), so it proves JsonUtility round-trips itself, not that the DTO survives the service. Nothing in the suite ever writes a truncated or malformed file to persistentDataPath and calls Load; nothing reads or asserts RunState.SchemaVersion. JsonFileSaveService.Load swallows every exception and returns `default` (lines 38-42), which is the mechanism behind C-01 (corrupt save is indistinguishable from no save) and C-02 (File.WriteAllText truncates the only slot in place).
```

**Чем стреляет.** The two P1 save defects round 1 confirmed are both reachable only through the paths this tier declines to exercise. A half-written run.json (power loss mid-Autosave) makes Load return null, HasSave stays true, «Продолжить» becomes a silent no-op and the run is gone — and the suite reports full green on the save layer. Adding a schema migration hook later would also land untested, since SchemaVersion is written by RunState.cs and read by nothing.

**Куда править.** Add three cases to RunStateSaveTests using a temp key: write `{"Gold":` (truncated) then assert Load surfaces the corruption distinguishably from absence (today it cannot — that assertion is the bug report); write a payload with an older SchemaVersion and assert the migration decision; and assert Save is atomic by checking the previous file survives a failed write. Drop RunState_JsonRoundTrip's direct JsonUtility call and route it through ISaveService so the test covers the seam rather than the library.

---

### TS-8 · P2 · architecture — "Return the world to non-battle state" has three owners, one of which documents itself as a copy

`Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:51` · линза `tests-as-subject`

```csharp
Three sites perform the same pair of commands. RunBeatStage.cs:51-52: `_session.RequestReset(); _session.SetPhase(BattlePhase.Interlude);` plus RunBeatStage.cs:59 `EnterNode() => _session.SetPhase(BattlePhase.None)`. GameFlow.cs:106-108: "// Арена живёт всё время после боя … здесь (dev-разрез одного боя) петли нет, поэтому возвращаем сами." then `_session.RequestReset(); _session.SetPhase(BattlePhase.None);`. GameFlow.cs:194-197 (the RunActAsync `finally`): "// Забег кончился ЛЮБЫМ путём …" then the same two calls. `grep -rn RequestReset --include=*.cs Assets/_Project/Scripts` confirms these are all of them besides the declaration and implementation.
```

**Чем стреляет.** The three copies already differ in the phase they land on — Interlude in RunBeatStage, None in both GameFlow sites — and in whether the rest-beat buttons are shown. Adding a step to "returning the world" (the animated version RunBeatStage.cs:30-31 explicitly plans: "трупы тают, отряд возвращается на места, а «К построению» проигрывает её ×3") means finding and editing three places; miss one and the dev single-battle cut or the end-of-run teardown skips the animation and snaps. The GameFlow.cs:107 comment shows the author knew it was a duplicate at the time of writing.

**Куда править.** Make IRunBeatStage the single owner: give it an ExitToMenu()/ReturnWorld() member and have GameFlow.RunSingleBattleAsync and RunActAsync's finally call that instead of touching IBattleSession directly. GameFlow already takes ActRunner from the container, so injecting IRunBeatStage costs nothing.

---

### TS-9 · P2 · gap — The determinism suite cannot detect a sim or checksum regression: no golden hash, both sims built identically in-process, and the formula has three owners

`Assets/_Project/Tests/EditMode/Combat/CombatSimulationTests.cs:61` · линза `tests-as-subject`

```csharp
SameSeedAndCommands_ProduceSameChecksum builds simA and simB through the same BuildSim(Seed) in the same process, ticks both 120 times in lockstep, and asserts `simA.ComputeChecksum() == simB.ComputeChecksum()`. DifferentSeeds_ProduceDifferentChecksums_ViaRngState carries its own disclaimer at line 79-82: "в Фазе 1 симуляция НЕ потребляет RNG … Этот тест проверяет лишь, что checksum включает состояние RNG". No test anywhere asserts a literal checksum value: `grep -rn ComputeChecksum` returns only these two lines, DefenderSliceTests.cs:179 and ReactiveEffectTests.cs:97 (both returning it for a two-run comparison), plus the parked Net/_Parked/SimSyncProbe.cs. The formula itself exists three times — CombatSimulation.cs:520-543, BrainTests.cs:215-227, StaggeredBrainSpikeTests.cs:172-185.
```

**Чем стреляет.** Two identical objects in one process agree unless something is reading ambient state, which is the narrowest possible slice of determinism. Nothing here would catch: a movement/separation/attack-timing change that silently shifts every unit's trajectory (no golden hash to compare against), or a desync introduced by the checksum's own blind spots — it hashes positions, HP, AttackCooldownTicks, WindupRemaining, RecoveryRemaining and the RNG snapshot, and nothing else, so an effect-, cooldown-, shield- or resource-level divergence hashes identically in both sims. For a host-authoritative co-op game whose whole netcode plan rests on this hash, the guard is a formality.

**Куда править.** Pin a golden checksum: run the fixed-seed 120-tick battle once, hardcode the resulting ulong with a comment saying a change means the sim behaviour changed, and make the test compare against it (the SimTuningConfig_MatchesCodeDefaults precedent). Separately, widen ComputeChecksum to cover effect stacks/remaining ticks, ability cooldowns, shield and resource, and delete the two test-side copies of the formula in favour of the production one.

---

### UA-10 · P2 · legacy — UnityAudioService is a never-registered stub for a phase that already shipped, and IAudioService.Stop has no callers

`Assets/_Project/Scripts/Game/Services/UnityAudioService.cs:10` · линза `uncovered-assemblies`

```csharp
`public sealed class UnityAudioService : IAudioService` with a docstring reading "Заглушка... Фаза 1 — только Debug.Log. FMOD-реализация подключается в Фазе 9 без изменения зависимостей." Phase 9 shipped: `RootLifetimeScope.cs:74` registers `builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>();` and there is no other `As<IAudioService>()` in the project. The identifier `UnityAudioService` appears only inside its own file — no registration, no test, no scene. Separately, `IAudioService.Stop(string soundKey)` (Core/Audio/IAudioService.cs:17) has zero callers: grepping `.Stop(` outside Core finds only `PooledVfx.cs:171` (`ParticleSystem.Stop`). `StopAll` (line 20) is live via `AudioPresenter.cs:112`.
```

**Чем стреляет.** Two costs. First, the stub reads as the "no-FMOD fallback" — a reader debugging silent audio will check whether the Unity stub got registered instead of FMOD, and there is no such switch. Second, `Play` on FmodAudioService keeps a `_loops` dictionary of EventInstances (FmodAudioService.cs:25, 49-52) whose per-key teardown (`Stop`) nobody calls, so the only release path is the blanket `StopAll` on battle reset — the per-key half of the contract is untested and unexercised.

**Куда править.** Delete UnityAudioService.cs and its .meta, and remove `void Stop(string soundKey)` from IAudioService plus both implementations until a caller exists (music/ambient will need it, and can add it back with its first user).

---

### UA-11 · P2 · dead — Nine SO fields designers author in the inspector are read by nothing — ThreatPoints is already filled on four enemy assets

`Assets/_Project/Scripts/Data/Definitions/EnemyData.cs:29` · линза `uncovered-assemblies`

```csharp
Each of these identifiers occurs exactly once in the whole repo (the declaration): `EnemyData.ThreatPoints` (:29), `EnemyData.GoldBounty` (:30), `ItemData.ActiveAbility` (:34), `ItemData.Charges` (:35), `ItemData.ShopWeight` (:37), `RelicData.RunEffects` (:38), `EncounterData.ArenaId` (:73), `AIPresetData.ArchetypeTags` (:20), `UnitVisual.HasClips` (:58). The backing `[SerializeField]`s all carry Russian tooltips inviting authoring. And they ARE authored: `ScriptableObjects/Enemies/GoblinArcher.asset:70` `_threatPoints: 2`, `GoblinCutthroat.asset:64` `_threatPoints: 2`, `GoblinGrunt.asset:64` `_threatPoints: 1`, `GoblinWarrior.asset:64` `_threatPoints: 3`. Gold is in fact awarded as a flat constant: `RunStateService.cs:115` `AwardBattleReward() => AddGold(_config.BattleGoldReward)` with `GameConfig.cs:44` `_battleGoldReward = 20`.
```

**Чем стреляет.** A designer has already spent time balancing threat points across four goblins for a metric no code path reads, and `_goldBounty` invites the same for the gold economy while gold is a flat 20 per battle from GameConfig. `RelicData.RunEffects` is the worst of the set: an effect array on the player's core content type that silently never applies, so a relic authored with a run-scope effect just does nothing with no warning anywhere. `ItemData.ActiveAbility`/`Charges` likewise mean an authored active item is inert.

**Куда править.** Delete the fields whose feature is not planned for this milestone (ThreatPoints, GoldBounty, ArenaId, ArchetypeTags, HasClips) — the SO YAML entries go with them. For the ones that are real design intent (RelicData.RunEffects, ItemData.ActiveAbility/Charges/ShopWeight) either wire them this milestone or move them behind a validation test that fails when an asset sets a field no code consumes, so authoring effort cannot silently evaporate again.

---

### UA-12 · P2 · dead — Fifteen unreferenced public members across Core, Guild, Game, Presentation, UI and the ContentHub, including a documented dev-camera seam that does not exist

`Assets/_Project/Scripts/Presentation/Camera/CameraModeController.cs:198` · линза `uncovered-assemblies`

```csharp
Each identifier below occurs exactly once repo-wide. Core contract layer: `IRngService.NextUInt()` (Core/Random/IRngService.cs:15) and `IRngService.Chance(float)` (:27) — production code only ever calls NextInt/NextFloat/Snapshot/Reseed; `ScreenTransitionShape.Centered(...)` (Core/Flow/IScreenTransition.cs:46). Presentation: `CameraModeController.SetDevAccess(bool)` (:198), `.DevAccess` (:106), `.Mode` (:109) — and the access it is supposed to grant is instead hardcoded at line 130, `_devAccess = Application.isEditor;`, so the "доступ выдаётся отдельно, вики «16» §6" seam in the line-105 docstring has no implementation; `ClassBalanceConfig.BaseHp`/`BaseMoveSpeed` (Data/Definitions/ClassBalanceConfig.cs:35-36) — the live path is GetBaseModifiers; `CombatFeelConfig.EnableSchoolFlash` (:238) and `HitstopMin`/`HitstopMax`/`HitstopFullFrac`/`HitstopWeightCurve` (:271-274) — hitstop is consumed only through `EvaluateHitstopSeconds`. Game/Guild: `TimeScaleService.SetCinematic(float)` (:97), `WorldMapController.IsChoosing` (:46), `RunStateService.MaxVesselItems` (:246) and `MaxPartyBanners` (:279), `EncounterLoader.HasLast` (:59) and `LastEncounterId` (:62). UI/editor: `RelicCard.SetInfoTags` (:98), `SetRarity` (:131), `SetTooltip` (:139), `Slot.SetSelected` (:37), `NavHistory.HasCurrent` (:16), and the whole `VisualElementClassExtensions` helper class (EditorTools/ContentHub/ContentHubWindow.Browser.cs:429).
```

**Чем стреляет.** Public surface is a promise about what callers exist. `SetDevAccess` is the actively misleading one: a reader looking for how the third camera mode is unlocked finds a setter, a getter and a docstring pointing at the wiki, and only after reading line 130 learns that the real owner is `Application.isEditor` — meaning the dev camera cannot be granted in a build and cannot be revoked in the editor. The rest are read cost: every one is a member a maintainer must consider when changing the type, and four of them (`RelicCard.SetRarity`/`SetInfoTags`, `Slot.SetSelected`) look like the API a new screen should use.

**Куда править.** Delete all fifteen. For CameraModeController either implement the grant (route `SetDevAccess` from a DevTools command) or delete the setter, the two getters and the line-105 docstring and keep `Application.isEditor` as the acknowledged single owner. For CombatFeelConfig and ClassBalanceConfig, keep only the computing methods (`EvaluateHitstopSeconds`, `GetBaseModifiers`, `GetMultipliers`) public so the raw knobs cannot grow a second consumer that skips the formula.

---

### UA-18 · P2 · dead — The persist-world RNG reseed seam is dead: IRngService.Reseed has zero production callers and BattleSeed is registered but never resolved, so battle RNG drifts across every battle in a run

`Assets/_Project/Scripts/Core/Random/IRngService.cs:42` · линза `uncovered-assemblies`

```csharp
IRngService.cs:35-42 documents the exact requirement: "Нужно для persist-мира: боевой скоуп не умирает между боями, поэтому на каждый бой RNG пересевается суб-сидом (runSeed + battleIndex + attempt), иначе повторный бой пошёл бы с «уехавшего» состояния → потеря воспроизводимости и рассинхрон в коопе." `void Reseed(ulong seed);`
Repo-wide grep for `Reseed`: Core/Random/IRngService.cs:42 (declaration), XorShiftRng.cs:15,18 (ctor + impl), Tests/EditMode/Core/XorShiftRngTests.cs:139-169 (two dedicated tests), Tests/EditMode/Guild/RandomEventFlowTests.cs:76 (a no-op fake). ZERO callers in Scripts/.
CombatLifetimeScope.cs:110-118 creates the generator once per scope: `ulong seed = fixedSeed ? (ulong)_fixedSeed : GenerateBattleSeed(); builder.RegisterInstance(new BattleSeed(seed)); builder.RegisterInstance<IRngService>(new XorShiftRng(seed));` — and the class's own docstring (CombatLifetimeScope.cs:21-24) says "Вопреки имени, по одному бою НЕ пересоздаётся: сцена грузится один раз на буте и не выгружается."
BattleSeed: the only three hits in the whole repo are its own declaration (Core/Random/BattleSeed.cs:11,16) and the RegisterInstance above. Nothing ever resolves it.
```

**Чем стреляет.** Two seams that look wired and are pinned green by tests, but no production code path touches them. The `_fixedSeed` inspector knob advertises per-battle reproducibility and actually fixes the whole session's stream; a battle retry resumes from the drifted state. The test pair is the worst part — it makes the seam look covered while the game never calls it.

**Куда править.** Either call Reseed(runSeed ^ battleIndex ^ attempt) from the battle-start path (BattleSession/BattleBootstrap) and resolve BattleSeed where the sub-seed is composed, or delete Reseed, BattleSeed and the two XorShiftRngTests cases until the MP/replay work actually lands.

---

### UA-19 · P2 · dead — Guildmaster.Balance is a runtime assembly shipping one ScriptableObject type that has zero assets, behind a menu item that can never fire

`Assets/_Project/Scripts/Balance/Guildmaster.Balance.asmdef:8` · линза `uncovered-assemblies`

```csharp
Guildmaster.Balance.asmdef:8 `"includePlatforms": [],` — no Editor restriction, so the assembly is compiled into the player build. Its only referrers are Guildmaster.Balance.Editor and Guildmaster.Balance.Tests, both Editor-only.
Its entire content is BalanceScenarioData.cs, whose own docstring concedes the reason: "SO лежит в runtime-сборке только чтобы ассеты сериализовались" (BalanceScenarioData.cs:11).
There are no such assets: a grep for the script guid d896b3dbdd3ef594a952cfa527d0e065 across every .asset/.prefab/.unity in Assets/ returns nothing.
So BalanceMenu.cs:27-40 is unreachable: `var scenario = Selection.activeObject as BalanceScenarioData; if (scenario == null) { Debug.LogWarning("[SimBench] Выдели BalanceScenarioData-ассет в Project, затем запусти."); return; }` plus the validate handler `Selection.activeObject is BalanceScenarioData`, which permanently greys the item out. ScenarioBench.cs (91 lines) is reachable only through it.
```

**Чем стреляет.** Rule 1: an assembly, an SO type, a menu item, a validate handler and a whole bench exist for a workflow nobody can start, and they cost the retail build an extra assembly. The CreateAssetMenu entry is the only thing that makes it look alive.

**Куда править.** Either author one BalanceScenario asset and keep it, or delete BalanceScenarioData.cs, ScenarioBench.cs, the two BalanceMenu.RunScenario members and the Guildmaster.Balance asmdef, folding Guildmaster.Balance.Editor's remaining references accordingly.

---

### UA-20 · P2 · architecture — Three asmdef edges are wider than any usage, and one of them is the only mechanical guard the HARD localization rule had

`Assets/_Project/Scripts/UI/Guildmaster.UI.asmdef:12` · линза `uncovered-assemblies`

```csharp
Guildmaster.UI.asmdef:11-12 lists `"MessagePipe.VContainer"` and `"Unity.Localization"`. No file under Scripts/UI contains `UnityEngine.Localization`, `MessagePipe.VContainer`, `RegisterMessagePipe` or `GlobalMessagePipe` (verified by grep over Scripts/UI/**/*.cs) — UI reaches text only through Core's ILocalizationService.
Guildmaster.Presentation.asmdef:17-18 lists `"Unity.RenderPipelines.Core.Runtime"` and `"Unity.RenderPipelines.Universal.Runtime"`. The only `UnityEngine.Rendering.*` uses under Scripts/Presentation are ShadowCastingMode (DeathShatter.cs:69, MenuBackdropView.cs:129, WorldMapView.cs:452/493/539) and IndexFormat (ShatterMesh.cs:47-48), both of which live in UnityEngine.CoreModule, not in either URP assembly.
```

**Чем стреляет.** The lens question was what the graph mechanically prevents. It genuinely blocks Combat→Presentation (Guildmaster.Combat.asmdef references only Core+Data) — but UI→Unity.Localization is exactly the edge that would let a screen call LocalizationSettings.StringDatabase directly and bypass ILocalizationService, and given that 15 UXML files and 60 C# call sites already carry raw Russian, that guard is the one worth keeping shut. The URP pair is pure build-graph weight.

**Куда править.** Delete all four references. UI keeps Core/Data/Guild/VContainer/UniTask/MessagePipe; Presentation drops both URP entries. Removing Unity.Localization from Guildmaster.UI turns 'text goes through ILocalizationService' from discipline into a compile error.

---

### UA-21 · P2 · convention — The palette is duplicated by hand between tokens.primitives.uss and PaletteRemapWindow, and the file says so out loud

`Assets/_Project/Scripts/EditorTools/PaletteRemap/PaletteRemapWindow.cs:49` · линза `uncovered-assemblies`

```csharp
PaletteRemapWindow.cs:48-49: "// Дефолтная рампа Guildmaster: значения = наши примитив-токены (ink/brass/parchment).\n// Держим в синхроне с tokens.primitives.uss вручную (там — источник правды по палитре)."
Lines 54-62 restate seven colours: RGB(18,16,13) ink-900, RGB(36,26,18) ink-600, RGB(74,58,38) ink-300, RGB(138,95,40) brass-700, RGB(184,134,59) brass-500, RGB(217,178,106) brass-300, RGB(239,226,196) parchment-100.
UI/Theme/tokens.primitives.uss:10,13,16,33,35,37,41 declares the same seven: `--gm-ink-900: rgb(18, 16, 13);` … `--gm-parchment-100: rgb(239, 226, 196);`. Today they agree.
```

**Чем стреляет.** One fact, two owners, with the sync mechanism being 'remember to'. The HARD palette rule («тёплый свет») names the USS tokens as the source of truth; the day a token is retuned, every sprite remapped afterwards is baked into the stale ramp and the drift shows up as art that no longer matches the UI, far from the edit that caused it.

**Куда править.** Parse the seven `--gm-*` values out of UI/Theme/tokens.primitives.uss at window-open time (it is an Editor tool, AssetDatabase.LoadAssetAtPath + a regex is enough), or generate a small GuildmasterPalette.asset from the USS and have both the ramp and any future tool read that. Delete BuildGuildmasterRamp's literals either way.

---

### UA-22 · P2 · architecture — Base HP has two owners and the loser is a decoy: StatsConfig's MaxHP default is overridden by ClassBalanceConfig for every real unit and only surfaces when the class config is missing

`Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:18` · линза `uncovered-assemblies`

```csharp
StatsConfig.asset:17-22 authors global defaults `_defaults: - Stat: 0 (MaxHP) Value: 1200` and `- Stat: 20 (MoveSpeed) Value: 3`.
ClassBalanceConfig.asset:15-16 authors `_baseHp: 2000`, `_baseMoveSpeed: 3`, with per-class multipliers (Class 2 = 0.75).
ClassBalanceConfig.cs:47-48 emits them as Override modifiers: `new StatModifier(StatType.MaxHP, ModifierOp.Override, _baseHp * hpMult)`, and RuntimeUnitFactory.cs:69 applies them as the FIRST group, so Stats.cs's `baseTerm = Override (если задан) ИНАЧЕ дефолт StatsConfig` (Stats.cs:10-11) always picks 2000×mult.
Relic assets do not author MaxHP at all — Relics/Assassin.asset `_stats` lists only Stat 3/7/8/9 (PhysArmor, AutoAttackDamage, AttackSpeed, AttackRange), so its 1500 HP comes entirely from the class layer.
The two numbers disagree today: 1200 vs 2000×0.75 = 1500 for an Assassin.
```

**Чем стреляет.** A designer reading StatsConfig.asset sees a global base HP of 1200 that no unit in the game ever has. Worse, the decoy is what the code silently falls back to when the class config is absent — which is exactly the CoreScene wiring bug above, so the wrong number is not merely unused, it is the failure mode.

**Куда править.** Delete the MaxHP and MoveSpeed entries from StatsConfig.asset's _defaults (ClassBalanceConfig owns both), and make ClassBaseline.Apply log or throw when config is null instead of quietly returning — a missing class config should be loud, not a 1200-HP fallback.

---

### UA-6 · P2 · dead — Four content-definition SO types have zero assets and zero readers, but the domain and path tables make them look wired

`Assets/_Project/Scripts/Data/Definitions/GuildmasterData.cs:11` · линза `uncovered-assemblies`

```csharp
`GuildmasterData`, `TraitData`, `ConsequenceData` and `RunModifierData` each have (a) zero assets — no .asset file under Assets carries their `m_EditorClassIdentifier`, and `ScriptableObjects/Vessels/` is an empty tracked directory too — and (b) zero code references anywhere except the two lookup tables: `Data/Definitions/ContentDomains.cs:26/27/29/31` and `Data/Editor/ContentPaths.cs:26/27/29/31`. All four carry `[CreateAssetMenu]` under Guildmaster/Content. Every public getter on them is unread: `GuildmasterData.Spells/UniqueEffects/StartingRelicIds/StartingGold` (lines 28-32), `TraitData.SelectionWeight/ExclusiveGroup` (28-29), `RunModifierData.RewardMult` (28) — each name occurs exactly once repo-wide.
```

**Чем стреляет.** GuildmasterData models the OLD concept (a single Гильдмастер with spells, guild mods, a portrait and `_startingGold`); the current concept makes the players the Guildmasters and the run's starting relic/gold come from `GameConfig`. Because the type is registered in both the domain map and the path map and offers a CreateAssetMenu entry, a reader — human or agent — concludes the guildmaster/trait/consequence/run-modifier content pipelines exist and authors against them. They resolve to nothing.

**Куда править.** Delete the four .cs files (and their .meta), their rows in ContentDomains.cs and ContentPaths.cs, and the empty ScriptableObjects/Vessels directory. VesselData is the borderline case — it has zero assets but real readers (RosterSlot.VesselId, RuntimeUnitFactory, BalanceScenarioData), so keep it and note that the vessel layer is unauthored, not dead.

---

### UA-7 · P2 · dead — ModalPanel is a UxmlElement no UXML and no C# ever instantiates, while claiming to be the shared frame every overlay reuses

`Assets/_Project/Scripts/UI/Components/ModalPanel.cs:11` · линза `uncovered-assemblies`

```csharp
`[UxmlElement] public partial class ModalPanel : VisualElement`. Repo-wide the identifier `ModalPanel` appears exactly twice, both inside this file (the declaration at line 11 and its own constructor at line 40). Its script guid `7f066a34e56dcbb469b5776ab6f628ef` appears in no .unity/.prefab/.asset. Grepping every .uxml for `ModalPanel` (including namespaced forms) returns nothing. Its two `[UxmlAttribute]` properties `Title` (line 19) and `PanelModifier` (line 27) are therefore also unreachable.
```

**Чем стреляет.** The docstring states "Дедуплицирует раму, которую повторяет каждый оверлей (награда/ивент/хаб/настройки/пауза)" — a claim that the overlays go through it. They do not: MenuRouter builds each screen's frame from its own UXML. A future reader adding an overlay will either wire into a component nobody validated or spend time working out why the existing overlays ignore the deduplication they are documented to use.

**Куда править.** Delete ModalPanel.cs and its .meta. If the frame really should be deduplicated, do it as a change to the existing overlay UXMLs, not as an unused element sitting in Components/.

---

### UA-8 · P2 · dead — asmdef edges wider than usage keep a dead assembly and NGO in the player build, and the _Parked folder still compiles

`Assets/_Project/Scripts/Game/Guildmaster.Game.asmdef:10` · линза `uncovered-assemblies` · переоформляет R1-63

```csharp
`Guildmaster.Game.asmdef:10` references `Guildmaster.Net`, but no file under Scripts/Game names any type from `Guildmaster.Net` (grepped `FacepunchTransportBootstrap|NetworkCommandRelay|SimSyncProbe|Guildmaster.Net` across Scripts — the only hits are inside Net itself and two docstring mentions). Guildmaster.Net has `includePlatforms: []`, so it and its `Unity.Netcode.Runtime` reference ship in the player. Line 9 references `Guildmaster.MiniGames`, whose folder contains zero .cs files. `Net/_Parked/SimSyncProbe.cs` is also compiled: Unity only skips folders ending in `~` or starting with `.`, so the `_Parked` prefix does nothing — its guid `6a96136653c099243a3b67001aded853` is in no scene either. `Guildmaster.Presentation.asmdef:18` references `Unity.RenderPipelines.Universal.Runtime`, yet no Presentation file uses any `UnityEngine.Rendering.Universal` type (the only URP-package type is `Volume` in `Presentation/Effects/VolumeVisualToggle.cs:2`, which lives in Core.Runtime on line 17). `Guildmaster.Balance.asmdef:5` references `Guildmaster.Core`, but its single file `Balance/BalanceScenarioData.cs` uses only `Guildmaster.Data.Definitions` and UnityEngine.
```

**Чем стреляет.** Each edge is what stops the dead thing from being deletable, and reading the graph gives the wrong picture of the codebase: the reference list says Game depends on networking and minigames, so a reader budgets for a live network layer. Concretely it also costs build size and player-build compile time for a Netcode-dependent assembly with three unreachable MonoBehaviours.

**Куда править.** Remove the `Guildmaster.Net` and `Guildmaster.MiniGames` entries from Guildmaster.Game.asmdef, then delete the Net and MiniGames folders and their asmdefs outright (nothing else references them). Drop `Unity.RenderPipelines.Universal.Runtime` from Presentation and `Guildmaster.Core` from Balance. Rename `_Parked` to `_Parked~` if any parked code is to be kept out of the build.

---

### UA-9 · P2 · gap — Nothing mechanically enforces sim determinism or the layer graph — the two rules the codebase depends on most are pure discipline

`Assets/_Project/Scripts/Combat/Guildmaster.Combat.asmdef:4` · линза `uncovered-assemblies`

```csharp
The asmdef graph does enforce some layering: Guildmaster.Combat references only `Guildmaster.Core` + `Guildmaster.Data` (so Combat -> Presentation/UI is impossible), Guildmaster.Core has `"references": []` (Core -> Game impossible), and no runtime asmdef lists an Editor-only assembly. But the determinism contract has no enforcement at all: Combat has `noEngineReferences: false` and cannot set it (it uses Vector2/Mathf), so `UnityEngine.Random`, `Time.deltaTime` and `Time.time` are all reachable from every one of the ~40 Combat systems. The only statement of the rule is prose: `Combat/Systems/MovementSystem.cs:11` ("Ручная математика — без Rigidbody2D и без Time.deltaTime") and `Game/Services/CombatLoopService.cs:12` ("Time.deltaTime используется ТОЛЬКО здесь"). Searching Tests/ for `asmdef`, `GetAssemblies` or `deltaTime` returns nothing — no test inspects the assembly graph or scans Combat sources. Today the rule holds (I grepped Combat: zero `Time.`/`Random.` hits), which is exactly why the absence of a guard is invisible.
```

**Чем стреляет.** A single `Time.deltaTime` or `UnityEngine.Random.Range` added to a Combat system compiles, passes CI, and produces a battle that desyncs in coop and cannot be replayed — with a symptom (checksum mismatch at some tick) arbitrarily far from the cause. The same applies to the layer graph: it is currently correct, but nothing fails when someone adds `Guildmaster.Presentation` to Guildmaster.Combat.asmdef, and R1-68's string-reflection hack shows the team already reaches across a seam when the compiler blocks it.

**Куда править.** Add one EditMode test that (a) reads every Assets/_Project/Scripts/**/*.cs under Combat and fails on `Time.`, `UnityEngine.Random`, `DateTime`, `Environment.TickCount` and `Guildmaster.Presentation`/`Guildmaster.UI`, and (b) asserts the allowed reference set of each asmdef by parsing the .asmdef JSON. It is ~40 lines and it turns two prose rules into CI facts.

---

### AC-24 · P3 · gap — UnitTagResolver resolves tag.arcane, the one id in its table with no TagData asset, and the miss is swallowed

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/UnitTagResolver.cs:139` · линза `assets-vs-code`

```csharp
UnitTagResolver.cs:134-141 maps the magic elements — "MagicElement.Fire => \"tag.fire\", MagicElement.Ice => \"tag.ice\", MagicElement.Lightning => \"tag.lightning\", MagicElement.Arcane => \"tag.arcane\",". Enumerating the 55 assets in ScriptableObjects/Tags, every other id the resolver can produce (tag.tank/bruiser/assassin/ranged/support/summoner, tag.physical/magical/pure, tag.blunt/slash/pierce, tag.fire/ice/lightning, tag.poison/light/dark) exists; tag.arcane does not. The miss is deliberate but silent — resolver docstring line 20: "отсутствующий ассет тега молча пропускается (не роняем UI из-за тега)", implemented at line 33 "if (db.TryGet(id, out TagData tag) && tag != null) result.Add(tag);".
```

**Чем стреляет.** Latent today (no asset authors MagicElement.Arcane = 4, which CombatCategories.cs:56 calls "задел, механики пока нет"), but the first Arcane relic will render a tag row missing its damage-type chip with no log line and no failing test, and the author will look in the resolver rather than at the absent asset.

**Куда править.** Add Tags/Arcane.asset with id tag.arcane (plus its tag.arcane.name key) to close the taxonomy, or add a content-validation test asserting every id UnitTagResolver can emit resolves to an asset.

---

### BE-12 · P3 · legacy — Dead assets and stale serialized keys left behind by finished migrations: an orphaned UnitVisual pair and a removed GameBootstrap field still in CoreScene

`Assets/_Project/ScriptableObjects/Visuals/FantasyWarrior.asset:1` · линза `build-vs-editor`

```csharp
Guid sweep over every `guid: <32hex>` occurrence in Assets/_Project, Assets/AddressableAssetsData, Assets/Settings and ProjectSettings (768 distinct referenced guids) leaves exactly three orphaned ScriptableObjects: `Configs/ActConfig.asset` (finding 3 above), `Visuals/FantasyWarrior.asset`, and `Visuals/ForestMushroom/ForestMushroom.asset`.
FantasyWarrior is a full UnitVisual with clips wired — `_idleClip: {guid: b0458905c70f5a2449b94c4ff47a2ae9}`, `_runClip`, `_attackClip`, `_deathClip` — plus a sibling `Visuals/FantasyWarrior/` clip folder, and nothing points at it. Its live counterpart MedievalWarrior.asset by contrast is referenced by four content assets (Enemies/TrainingDummy.asset, Relics/BaseRelic.asset, Relics/Druid.asset, Relics/WhirlMonk.asset).
Both stragglers also carry the pre-move binding in their YAML — `m_EditorClassIdentifier: Guildmaster.Presentation::Guildmaster.Presentation.UnitVisual` on FantasyWarrior.asset and on MedievalWarrior.asset — while the class now lives at Data/Definitions/UnitVisual.cs:17 (`namespace Guildmaster.Data.Definitions`); the nine assets produced by the current pipeline all read `Guildmaster.Data::Guildmaster.Data.Definitions.UnitVisual`.
Separately, CoreScene.unity:242 serialises `_legacyBattleScene: 0` on the GameBootstrap component, and GameBootstrap.cs declares no such field — its serialized fields are `_runActOnBoot` (:20), `_runBattleFlowOnBoot` (:24), `_devStartPreset` (:27), `_runTextEventOnBoot` (:30), `_devStartEvent` (:33).
```

**Чем стреляет.** Low blast radius but real residue from two completed migrations — UnitVisual moving Presentation→Data, and the removal of the legacy load-BattleScene-per-battle path (ISceneLoader.cs:12: «старый путь „загрузить на бой → выгрузить после" снят вместе с legacy-входом»). The concrete cost is authoring confusion: `Visuals/` now contains two generations side by side, flat `FantasyWarrior.asset`/`MedievalWarrior.asset` next to nine per-pack folders produced by BuildUnitViewPrefabs.cs:25, one of the flat pair live and one dead, with no way to tell which is which except a guid sweep. A designer picking FantasyWarrior for a new enemy would be extending a file the pipeline no longer regenerates.

**Куда править.** Delete FantasyWarrior.asset, its .meta and the Visuals/FantasyWarrior/ clip folder, and either wire ForestMushroom/ForestMushroom.asset into the enemy that needs it or delete it too. Re-save MedievalWarrior.asset from the current pipeline so its m_EditorClassIdentifier matches the other nine, or fold it into a per-pack folder for consistency. Re-save CoreScene to drop the orphan `_legacyBattleScene` key.

---

### C-07 · P3 · gap — MapGenConfig.Validated() clamps six scalars but never checks that any ZoneRule or AnchorRule floor is inside the act it is validating

`Assets/_Project/Scripts/Guild/MapGenConfig.cs:54` · линза `critic`

```csharp
/// <summary>Валидирует и клампит поля к разумным границам (защита от кривого SO/ручного конфига).</summary>
        public MapGenConfig Validated()
        {
            if (Columns < 3) Columns = 3;
...
            Zones   ??= Array.Empty<ZoneRule>();
            Anchors ??= Array.Empty<AnchorRule>();
```

**Чем стреляет.** The docstring promises protection against a wrong hand-authored config, and the defaults it validates are hardwired to Columns=15: zones cover floors 1-4, 5-8, 9-13 and anchors sit at 7, 8, 13. MapGenerator only iterates `col` in 1..Columns-2 (MapGenerator.cs:33), so every zone and anchor outside that band is dropped with no warning. Shorten the act in ActConfig to Columns=10 — a legal edit that Validated() accepts unchanged — and the entire "Пик" zone (floors 9-13) plus the pre-boss Camp anchor at 13 vanish: no Chest, no elite-heavy endgame, every non-anchor floor rolling from the warm-up/development tables. The map still generates and still passes MapGeneratorTests, so the designer gets a quietly wrong act instead of an error. Same silence for a zone whose FromFloor > ToFloor, which Covers() makes permanently false.

**Куда править.** In Validated(), warn and drop (or clamp) any AnchorRule with Floor outside 1..Columns-2 and any ZoneRule that does not intersect that range, plus assert FromFloor <= ToFloor. A one-line Debug.LogWarning per dropped rule turns a silent content bug into a visible one.

---

### LT-12 · P3 · convention — Player strings are assembled by concatenation and positional args, against the interface's own written rule

`Assets/_Project/Scripts/UI/ShopScreenView.cs:124` · линза `localization-text`

```csharp
`goldLbl.text = L("ui.shop.gold", "Золото") + $": {shop.Gold}";` (cs:124), `reroll.text = L("ui.shop.reroll", "Обновить") + $" ({shop.RerollCost})";` (cs:127), `sell = new Button { text = L("ui.shop.sell", "Продать") + $" ({st.SellValue})" }` (cs:112); LoadoutHubView.cs:66 `SetText(root, "hub-gold", L("ui.hub.gold", "Золото") + ": " + gold)`; RunModeBarView.cs:98 `SetText(_act, "· " + L("ui.run.act", "Акт") + " " + actNumber)` and cs:105-106 `"· " + L("ui.run.floor", "Веха") + " " + floorNumber + (floorCount > 0 ? "/" + floorCount : "")`; LoadoutInventoryView.cs:82 `SetBtn(root, "sort", L("ui.loadout.sort.name", "Имя") + " ↓")`. And CampScreenView.cs:67 `string.Format(L("ui.camp.budget", "Действий осталось: {0} из {1}"), session.Remaining, session.Budget)` — positional slots, which ILocalizationService.cs:39-45 explicitly forbids: «Именно ИМЕНОВАННЫХ, не позиционных: … позиционные {0}/{1} в переводе неизбежно перепутают местами». SmartStringFlagTests cannot see it either: its regex `(?<!\{)\{[A-Za-z_][A-Za-z0-9_.:]*\}` (cs:21) requires a leading letter, so `{0}` is invisible to the guard.
```

**Чем стреляет.** Word order, separators and units are baked into C# where no translator can reach them: a language that puts the number first, uses a different separator than «: », or needs «Веха 1 из 15» cannot be expressed. Each of these also creates a second owner for a string whose first half already lives in a table row.

**Куда править.** Make each of these one Smart-String row with named slots (`ui.shop.gold = "Золото: {gold}"`, `ui.run.floor = "· Веха {floor}/{total}"`) and pass the dictionary overload that ILocalizationService already exposes; broaden the SmartStringFlagTests regex to catch `{0}`-style slots as well.

---

### R1-12 · P3 · architecture — The sim draws no random numbers at all, which makes the Monte-Carlo benches degenerate

`Assets/_Project/Scripts/Combat/ICombatContext.cs:76` · линза `combat-sim`

```csharp
/// <summary>Генератор случайных чисел боя (детерминированный).</summary>
IRngService Rng { get; }
```

**Чем стреляет.** A grep for `Rng.` across Assets/_Project/Scripts/Combat returns zero hits: IRngService is injected into CombatSimulation, re-exposed on ICombatContext and again on EffectContext.Rng (EffectContext.cs:48), and its only actual use is `hash ^= _rng.Snapshot();` in ComputeChecksum (CombatSimulation.cs:523) — which, with no draws, is a constant. So the seam is real but the sim is fully deterministic-by-absence, and the balance harness inherits that: DuelMatrixBench.RunDuel passes the same fixed `Seed` for every matchup, so repeating a duel can only ever reproduce one identical trajectory and any 'run N samples' averaging over it is measuring one sample N times.

**Куда править.** Either keep the seam and note in the bench docs that variance must come from perturbed initial conditions (positions/stat jitter), or introduce the first real draw (e.g. separation degenerate-direction, target tie-break) so the seed actually parameterises a trajectory.

---

### R1-22 · P3 · complexity — UiTrace log strings are built on every stack mutation even though tracing is disabled

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:136` · линза `ui-coordination`

```csharp
UiTrace.Log($"nav.Push {Desc(screen)} → [{StackDesc()}] suppress={_input?.GameplaySuppressed}");
```

**Чем стреляет.** UiTrace.Enabled is false by default (UiTrace.cs:16) but the check happens inside Log, so the interpolated string — including `StackDesc()`, which allocates a StringBuilder and walks the stack — is built on every Push/Pop/PopAll/Remove and in every bootstrap event handler. Dead allocation on a hot-ish path, and it makes the trace look free when it is not.

**Куда править.** Guard the call sites (`if (UiTrace.Enabled) UiTrace.Log(...)`) or take a `Func<string>`/`[Conditional]`-style API so the message is only composed when tracing is on.

---

### R1-23 · P3 · correctness — OnFocus is only raised by Pop, not by Push or Remove — the lifecycle hook is unreliable for future screens

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:153` · линза `ui-coordination`

```csharp
top.OnExit();

SyncVisibility();
SyncInput();
Top?.OnFocus();
FocusTop();
```

**Чем стреляет.** `Pop` calls `Top?.OnFocus()`, but `RemoveScreen` (line 258-261) only calls FocusTop() and `Push` calls `prevTop?.OnBlur()` without ever calling `OnFocus` on the newly pushed screen. Nothing overrides OnFocus today, so it is latent — but the first screen that uses it (e.g. refresh-on-refocus for the inventory) will silently miss the notification when it becomes top via Remove of the screen above it, which is exactly how HideInventory/HideTestZone/HideMapSpace unwind the stack.

**Куда править.** Raise OnFocus/OnBlur from one place: after SyncVisibility in Push/Pop/RemoveScreen/PopAll, compare the previous top with the new top and fire the pair once.

---

### R1-34 · P3 · architecture — Feel constants are duplicated as unreachable in-code fallbacks across four files, creating a second source of truth for the SO

`Assets/_Project/Scripts/Presentation/UnitView.cs:837` · линза `presentation`

```csharp
float hitAmt = (_feel != null ? _feel.SquashAmount : 0.4f) * _hitSquashWeight;
float flipAmt = (_feel != null ? _feel.FacingFlipSquashAmount : 0.35f) * _flipSquashWeight;
float acqAmt = (_feel != null ? _feel.TargetAcquireTwitch : 0.06f) * _acquireSquashWeight;
```

**Чем стреляет.** CombatLifetimeScope.cs:56-58 guarantees a non-null CombatFeelConfig (falls back to ScriptableObject.CreateInstance), and CombatPresenter.cs:245 calls ApplyFeelConfig on every spawned view, so every `_feel != null ? … : constant` branch is unreachable. There are ~25 such constants (UnitView 746-748, 775, 837-839, 979; DeathShatter 45-48, 90-102; ScreenShake 41-44), each restating a CombatFeelConfig default. They match the SO today; the first time a designer retunes SquashAmount the code keeps a stale twin that a reader will trust. CombatPresenter.cs:327 (`_feel.NumberMaxScale`) and :467 (`_feel.FinisherHoldSeconds`) already deref _feel unguarded, proving the guards are noise.

**Куда править.** Drop the null branches and take CombatFeelConfig as a required argument (throw/log once if the DI wiring is missing), so the asset is the only place these numbers exist.

---

### R1-35 · P3 · convention — WorldMapView reaches for Camera.main every frame despite having CameraModeController injected; plus dead presentation API

`Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs:699` · линза `presentation`

```csharp
private int HitTest()
{
    Camera cam = Camera.main;
    if (cam == null || _input == null || _style == null) return -1;
```

**Чем стреляет.** HitTest runs from Update (line 726) on every frame the map is shown, and ScreenUvOf (line 740) does the same — a tag-based scene lookup in gameplay code in a project whose rule is no scene lookups, while CameraModeController (which owns all four vcams) is already injected into this very class (line 130). If the CinemachineBrain camera is ever not tagged MainCamera, picking silently dies with no log. Alongside it the slice carries dead presentation API: FloatingText.Spawn (FloatingText.cs:45) has zero callers, which makes its three Play overloads (54, 57, 61) and the Destroy branch of Finish (137) unreachable; ProjectileView.Bind(projectile, tint) (ProjectileView.cs:25) has zero callers; IScreenTransition.Cancel (Core/Flow/IScreenTransition.cs:73) is never called by anyone, so ScreenTransitionRunner.Cancel (57) is dead too.

**Куда править.** Expose the brain camera from CameraModeController and use it in HitTest/ScreenUvOf instead of Camera.main; delete FloatingText.Spawn + its three unused overloads, the 2-arg ProjectileView.Bind, and IScreenTransition.Cancel.

---

### R1-45 · P3 · correctness — BattleSession.WaitOutcomeAsync leaks one cancellation registration per battle/retry onto the run-level token, and `??=` can hand back a stale completed outcome

`Assets/_Project/Scripts/Game/Flow/BattleSession.cs:126` · линза `di-lifecycle`

```csharp
UniTaskCompletionSource<BattleOutcome> tcs = _outcome ??= new UniTaskCompletionSource<BattleOutcome>();
            if (ct.CanBeCanceled)
                ct.Register(static state => ((UniTaskCompletionSource<BattleOutcome>)state).TrySetCanceled(), tcs);
            return tcs.Task;
```

**Чем стреляет.** BattleFlow calls this with `ctx.Cancellation` — the act-lifetime token from GameFlow._runCts — on every battle node and every retry (BattleFlow.cs:57 and :68), and the returned CancellationTokenRegistration is discarded, so registrations and their captured stale TCS objects accumulate on that token for the whole act and all fire on "В меню". Separately, `??=` means a caller that awaits without a preceding ArmOutcome gets the previous battle's already-resolved result instead of waiting: RequestReset and RestartInPlace deliberately do not arm (lines 159, 174), so any future await path that is not preceded by RequestLaunch/RequestRestart silently returns the last outcome.

**Куда править.** Dispose the registration (`using var reg = ct.Register(...)` inside an async method, or store and dispose it when the TCS completes) and make WaitOutcomeAsync fail fast / arm explicitly instead of `??=` reusing a completed source.

---

### R1-46 · P3 · correctness — A runtime BattlePresetData ScriptableObject is created per battle node and never destroyed

`Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:89` · линза `di-lifecycle`

```csharp
effective = BattlePresetData.CreateRuntime(
                            preset.Encounter, guildRoster, DeploymentMode.Free, party, preset.IsElite,
                            $"battle.run.{node.Id}");
```

**Чем стреляет.** CreateRuntime is `var preset = CreateInstance<BattlePresetData>();` (Data/Definitions/BattlePresetData.cs:89) with no matching Destroy anywhere. One instance per battle/elite/boss node (~a dozen per act, more with "?" nodes and act restarts) of a UnityEngine.Object that is not GC-collectable; BattleBootstrap also holds the last one alive in `_lastPreset` (BattleBootstrap.cs:33). With domain reload disabled these also survive Stop/Play in the editor.

**Куда править.** Destroy the runtime preset when the node's BattleFlow finishes (or reuse one scratch instance owned by NodeResolver and re-fill it per node), and drop `_lastPreset` to null in BattleBootstrap.ResetToWorld.

---

### R1-47 · P3 · correctness — SceneLoader guards WorldScene by name but BattleScene by a cached Scene struct, so a battle scene loaded by anything else is loaded twice

`Assets/_Project/Scripts/Game/Services/SceneLoader.cs:37` · линза `di-lifecycle`

```csharp
public async UniTask LoadBattleAsync()
        {
            if (_loadedBattleScene.isLoaded)
            {
                Debug.LogWarning("[SceneLoader] - BattleScene уже загружена");
                return;
            }
```

**Чем стреляет.** `_loadedBattleScene` is only ever set by this instance's own load (line 44), while LoadWorldAsync correctly asks SceneManager (`SceneManager.GetSceneByName(WorldSceneName).isLoaded`, line 24). If BattleScene is already in the build's start scenes, was left loaded in the editor, or gets loaded by the dev path (GameFlow.BootAsync / gm_restart's `SceneManager.LoadScene(active.name)` in DevTools/GuildmasterCommands.cs:468), this guard passes and a second BattleScene — with a second CombatLifetimeScope, a second CombatSimulation and a second set of scoped EntryPoints binding launch/reset/restart on the same BattleSession — is added additively. Only the last BindLaunch wins, so the surviving sim is not the one the presenters are showing.

**Куда править.** Use `SceneManager.GetSceneByName(BattleSceneName).isLoaded` for the guard, same as LoadWorldAsync, and keep the cached Scene only for unload.

---

### R1-57 · P3 · correctness — BattlePresetData.CreateRuntime leaks a ScriptableObject per battle node and mints an id outside its own domain

`Assets/_Project/Scripts/Data/Definitions/BattlePresetData.cs:89` · линза `data-guild-balance`

```csharp
var preset = CreateInstance<BattlePresetData>();
```

**Чем стреляет.** NodeResolver.cs:89 calls CreateRuntime for every Battle/Elite/Boss node (and again on each restart), and nothing ever Destroys the instance — each node permanently adds a live SO to the session. Both other CreateRuntime users cache (ArmorThornsComponent.cs:116 uses `??=`, CombatSimulation.cs:59 is a static). Separately, the id it stamps violates the id convention: ContentDomains.cs:33 maps BattlePresetData → "battle_preset", but NodeResolver passes `$"battle.run.{node.Id}"` and the parameter default is "battle.runtime", so transient presets carry a "battle.*" domain that exists nowhere in ContentDomains.

**Куда править.** Have the caller Object.Destroy the transient preset when the node flow ends (or reuse one cached instance and repopulate it), and build the id from ContentDomains.GetDomain(typeof(BattlePresetData)).

---

### R1-58 · P3 · correctness — SimBench.Drive rewrites Unit.Id after the factory derived BrainPhase from the old id, so bench AI staggering differs from real battles

`Assets/_Project/Scripts/Balance/Editor/SimBench.cs:42` · линза `data-guild-balance`

```csharp
for (int i = 0; i < tracked.Count; i++)
                tracked[i].Unit.Id = i;
```

**Чем стреляет.** RuntimeUnitFactory.Create couples the two: `Id = id` and `BrainPhase = id % SimConstants.AiTickInterval` (RuntimeUnitFactory.cs:92/103). Every bench builds its real units through env.Real (e.g. SurvivabilityBench.cs:66 places the victim after N synthetic attackers), then Drive overwrites Id with the tracking index while BrainPhase keeps the factory's value. In SurvivabilityBench with 3 attackers the victim gets Id=3 but BrainPhase=0, so its brain ticks on a different frame than the same relic would in a real battle — a small but systematic divergence in a tool whose whole premise is "таблица не врёт".

**Куда править.** Either let the factory own ids (offset the synthetic dummies' ids instead, e.g. start them above the factory counter) or recompute BrainPhase = Id % SimConstants.AiTickInterval after the reassignment.

---

### R1-59 · P3 · gap — Save DTO carries fields no code path honours: RunState.Difficulty is fully dead and RosterSlot.AiPresetId is silently dropped on the way into battle

`Assets/_Project/Scripts/Guild/RunState.cs:63` · линза `data-guild-balance`

```csharp
public string   AiPresetId = string.Empty;
```

**Чем стреляет.** AiPresetId is read nowhere in Scripts/ — GuildRoster.Resolve builds `new PlayerSlot(relic, vessel, rs.SavedPosition, ResolveItems(...))` (GuildRoster.cs:46) with no AI-preset argument, and PlayerSlot has no such field, so the moment the loadout UI starts writing a player's chosen AI preset it will round-trip through the save and be discarded at battle start with no warning. RunState.Difficulty (line 82) has zero readers and zero writers project-wide (`grep -rn "\.Difficulty"` returns only the RunStateSaveTests fixture). SlotOwner is the same shape but at least documented as a Phase-6 seam.

**Куда править.** Either wire AiPresetId through PlayerSlot/RuntimeUnitFactory (RelicData.AltAiPresets already exists for it) or drop the field until it is honoured; delete Difficulty or give it a reader.

---

### R1-70 · P3 · correctness — FmodAudioService swallows every exception from bus volume changes with a bare catch(Exception){}

`Assets/_Project/Scripts/Game/Services/FmodAudioService.cs:48` · линза `cross-cutting`

```csharp
var bus = RuntimeManager.GetBus(busPath);
                if (bus.isValid()) bus.setVolume(Mathf.Clamp01(volume));
            }
            catch (BankLoadException) { }
            catch (System.Exception) { }
```

**Чем стреляет.** The narrow `catch (BankLoadException)` is justified by the comment above it (bank not loaded yet). The blanket `catch (System.Exception) { }` underneath swallows everything else, including a genuinely broken FMOD initialization or a typo'd bus path: the settings slider then appears to work while volume never changes, and nothing is logged anywhere to tell you why. This is the only fully-silent catch in the runtime tree.

**Куда править.** Drop the blanket catch, or keep it but log once (`Debug.LogWarning` guarded by a bool) so a real FMOD failure is not indistinguishable from 'bank not loaded yet'.

---

### R1-71 · P3 · architecture — GlobalMessagePipe static provider is installed at boot but nothing in the project uses GlobalMessagePipe

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs:200` · линза `cross-cutting`

```csharp
var options = builder.RegisterMessagePipe();
            builder.RegisterBuildCallback(c => GlobalMessagePipe.SetProvider(c.AsServiceProvider()));
```

**Чем стреляет.** `GlobalMessagePipe` appears exactly once in the whole codebase — this line. It is MessagePipe's static service-locator escape hatch, which the project's own rule ('no singletons, no static service locators') forbids; keeping it wired means the next contributor who cannot be bothered to inject an IPublisher has a sanctioned static shortcut sitting ready, and every root scope build leaks a container reference into a static field (a stale provider after a scope rebuild in the editor).

**Куда править.** Delete the RegisterBuildCallback line; publishers/subscribers are already injected everywhere (CombatPresenter, UiRootBootstrap, DeploymentController…). Re-add it only if a non-DI context genuinely needs it.

---

### R1-82 · P3 · complexity — Dead public surface: unreferenced marker interface, unused command, superseded UnitView picking API, unread config getters

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Core/Simulation/ISimEvent.cs:11` · линза `dead-and-bloat`

```csharp
public interface ISimEvent
    {
    }
```

**Чем стреляет.** Each of these has exactly one whole-word hit in Assets/_Project (its own declaration): ISimEvent (empty marker, zero implementors — reactivity shipped as CombatEvent/IReactiveComponent instead); SpawnUnitCommand.cs:6 (ICombatCommand impl never constructed — NetworkCommandRelay.cs:59-64 maps only Pause/Resume, spawns go through EnqueueUnitSpawn); UnitView.cs:300 SpriteContainsWorldPoint, whose own docstring says it 'для захвата в расстановке НЕ годится' and points at FigureContainsWorldPoint, plus UnitView.cs:324 TryGetSpriteBounds; TimeScaleService.cs:97 SetCinematic (only CinematicPulse/PlayCinematicSequence are used); CameraModeController.cs:106 DevAccess and :198 SetDevAccess; Slot.SetSelected; RelicCard.SetRarity and SetInfoTags together with the RelicCardTag struct; ClassBalanceConfig.cs:35-36 BaseHp/BaseMoveSpeed; and five CombatFeelConfig getters (EnableSchoolFlash, HitstopMin, HitstopMax, HitstopFullFrac, HitstopWeightCurve) whose private fields are consumed only inside the config's own EvaluateHitstopSeconds/ResolveHitFlashColor. The cost is false signal, not bytes: a reader picking SpriteContainsWorldPoint or SetCinematic lands on the wrong, unmaintained path.

**Куда править.** Delete them. Where the intent should survive, keep only the note — fold ISimEvent's docstring into CombatEvent, and drop the public getters whose fields never leave the config.

---

### R1-83 · P3 · complexity — Guildmaster.MiniGames is an empty assembly definition that Guildmaster.Game still depends on

`C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/MiniGames/Guildmaster.MiniGames.asmdef:2` · линза `dead-and-bloat`

```csharp
"name": "Guildmaster.MiniGames",
  "rootNamespace": "Guildmaster.MiniGames",
  "references": [
    "Guildmaster.Core",
    "Guildmaster.Data"
  ],
```

**Чем стреляет.** Assets/_Project/Scripts/MiniGames contains only this .asmdef and its .meta — zero .cs files. Unity still compiles it as a separate assembly on every reload, and Assets/_Project/Scripts/Game/Guildmaster.Game.asmdef lists Guildmaster.MiniGames among its references, so the dependency graph (and any asmdef map in the tech wiki) shows Game depending on an empty unit and claims a subsystem that has no code.

**Куда править.** Delete the MiniGames folder and remove Guildmaster.MiniGames from Guildmaster.Game.asmdef's references; re-add the asmdef when the first mini-game script exists.

---

### RL-12 · P3 · correctness — The run timer is stored only in the UI and zeroed on every main-menu visit, so «Продолжить» restarts it at 00:00

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:394` · линза `run-loop-integrity`

```csharp
`private float _runElapsed;   // «рабочий» таймер забега (аккумулятор, RunState его не хранит)` (UiRootBootstrap.cs:120), accumulated in Update as `_runElapsed += UnityEngine.Time.unscaledDeltaTime;` (line 394), reset by `if (!runActive) { _runElapsed = 0f; return; }` (line 392) where `bool runActive = run != null && !_mainMenuOpen;` (line 366), and displayed via `_topBar.SetRunTime(FormatTime(_runElapsed));` (line 398). RunState (RunState.cs:68+) has no elapsed-time field, so RunStateService.Autosave (RunStateService.cs:131-134) cannot persist it.
```

**Чем стреляет.** The topbar presents run duration as run progress, but the value is owned by a MonoBehaviour field that dies on any main-menu visit: ESC → «В главное меню» → «Продолжить» resumes the same RunState with the timer back at 00:00, and so does relaunching the game. Every other figure in the same Update block (Gold, act, floor, restarts) is read from RunState and survives; this one silently does not.

**Куда править.** Move the accumulator into RunState (a `float RunSeconds` beside `RestartsRemaining`), advance it from the run-owning layer (GameFlow/ActRunner already autosave at every node boundary), and have the topbar read it like it reads Gold. If the timer is not meant to be durable, stop displaying it as run progress.

---

### UA-23 · P3 · dead — Two orphaned UnitVisual assets, one carrying a stale assembly-qualified type name

`Assets/_Project/ScriptableObjects/Visuals/FantasyWarrior.asset:13` · линза `uncovered-assemblies`

```csharp
Exhaustive guid scan over all 169 .asset files under _Project/ScriptableObjects against every .unity/.prefab/.asset in the project yields exactly three orphans: Configs/ActConfig.asset (reported above), Visuals/FantasyWarrior.asset and Visuals/ForestMushroom/ForestMushroom.asset. Every other UnitVisual is referenced from a relic or enemy `_visual:` field (e.g. Relics/Assassin.asset:24, Enemies/GoblinGrunt.asset:23).
FantasyWarrior.asset:14 also carries a stale identifier from before the type moved assemblies: `m_EditorClassIdentifier: Guildmaster.Presentation::Guildmaster.Presentation.UnitVisual`, while the sibling ForestMushroom.asset:14 (same script guid 551ba710b5e1fcc4f9e3f6c56bdfdf45) reads `Guildmaster.Data::Guildmaster.Data.Definitions.UnitVisual`.
Neither name is resolved by string anywhere: the only C# hits for "FantasyWarrior"/"ForestMushroom" are pack-name literals in the editor generators AuditUnitAnimations.cs:18 and BuildUnitViewPrefabs.cs:53, which key off sprite-sheet folders, not these assets.
```

**Чем стреляет.** Rule 1. Both hold live clip references, so they keep animation clips reachable in the build graph and look like authored content in the Visuals folder; a future author will pick one of them for a new enemy and inherit whichever half-migrated state it is in.

**Куда править.** Delete both .asset files and their .meta, or wire them to the enemies they were made for. If FantasyWarrior is kept, re-save it so the EditorClassIdentifier matches the current Guildmaster.Data type.

---

