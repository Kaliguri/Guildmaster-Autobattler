# Вердикты скептиков — аудит 2026-07-26

Каждую заявку высокой важности проверял отдельный агент, которому было велено **опровергнуть** её: читать код и все вызывающие места, и по умолчанию считать находку ложной, если конкретный путь до сбоя не строится.

Итог по 33 проверкам:

| Заход | Проверено | Опровергнуто полностью | Механизм подтверждён, severity понижена | Осталось на заявленном уровне |
|---|---|---|---|---|
| 1 (8 заявок) | 8 | 3 | 4 | 1 |
| Догон заявок захода 1 (13) | 13 | 0 | 12 | 1 |
| 2 (12 заявок) | 12 | 0 | 12 | 0 |

**Как это читать.** Механизм («что код делает») подтверждался почти всегда — аудиторы не выдумывали. Ложным систематически оказывался **вывод** («чем это стреляет») и ярлык важности. Плюс поправка на мой же промпт: скептикам было велено уклоняться в опровержение, поэтому калибровка ненадёжна в обе стороны. Единственный надёжный слой — описание механизма с `file:line`.

---

## Заход 1 — восемь заявок

### ОПРОВЕРГНУТО → `P3` (уверенность high)

**Заявка:** AttackSpeed clamp from StatsConfig is never applied — shipped content already breaks the ceiling

`Assets/_Project/Scripts/Combat/Stats/Stats.cs:186` · заявлено P1

The mechanism half is true but the failure is not reachable, and the headline arithmetic is against the wrong constant.

TRUE: Stats.cs:143-187 (RebuildCache/Compose) never reads StatsConfig.AttackSpeedMin/Max. Repo-wide, the only readers are the editor-only Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:43-44 and the cross-invariant test Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:133. So the docstrings at Stats.cs:30 ("после всех модификаторов и клампов") and Assets/_Project/Scripts/Data/Stats/StatType.cs:26 ("клампится из StatsConfig") do lie.

FALSE #1 - the ceiling is not broken. The claim's cap of 2.5 is the C# field initializer (StatsConfig.cs:21); the SHIPPED asset Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:17 has _attackSpeedMax: 4. Worst reachable value: highest base AttackSpeed Override in the project is 1.3 (Relics/Assassin.asset:41-43, Enemies/GoblinCutthroat.asset), FlameSwordsman is 1.2 (Relics/FlameSwordsman.asset:39-41). BlazingBladesRamp (+0.05 PercentAdd, MaxStacks 20, linearly scaled by StatModifierComponent.ScaleByStacks -> +1.0) plus PyreRush (+1.0 PercentAdd) yields 1.2 x 3.0 = 3.6, at most 3.9 on the fastest base. Both are UNDER the shipped ceiling of 4.0, so there is no cap violation and no "IntervalTicks 8 instead of 12" defect.

FALSE #2 - the floor has no content path. The only AttackSpeed reduction anywhere in Assets/_Project/ScriptableObjects is Effects/SporeCloud.asset:47-49: Op 2 = PercentMult, Value -0.2, _maxStacks: 1. PercentMult composes as multAccum *= (1 + v), which is strictly positive; stacking more instances only approaches zero asymptotically. No PercentAdd <= -1 and no negative Flat AttackSpeed modifier exists in any shipped asset. Hence AttackTiming.IntervalTicks(attackSpeed <= 0) -> int.MaxValue (AttackTiming.cs:30) is unreachable from content, and the claim itself concedes it needs "any FUTURE >= -100% debuff".

FALSE #3 - language/state misread. "would disable the auto-attack permanently, long after the debuff expires" is wrong about Stats: the cache is rebuilt from live modifier groups (RemoveModifiersFrom sets _dirty), so the stat recovers on expiry. Only the already-written unit.AttackCooldownTicks would linger, and AutoAttackSystem.Interrupt (AutoAttackSystem.cs:242) zeroes it on any stun/displacement.

What actually remains is doc-vs-code drift plus two config fields that are dead outside the editor auditor - no gameplay consequence with current content. The repo already tracks exactly this at docs/audits/2026-07-09/opus-4.8.md:105 (classified "minor") and docs/audits/2026-07-19/cursor-grok-4.5-seams-data-flow-stats.md:198 (S-4). Fix would be either applying the clamp in RebuildCache or deleting the clamp promise from the two docstrings; it is a P3 consistency item, not a P1 correctness bug.

---

### ПОДТВЕРЖДЕНО → `P1` (уверенность high)

**Заявка:** Equipping a relic in the test-zone/interlude sandbox silently does nothing (Load bails on null encounter)

`Assets/_Project/Scripts/Combat/Units/EncounterLoader.cs:80` · заявлено P1

CONFIRMED — and the repro is the main intended flow, not a corner case.

Concrete path (all caller-verified):
1. Топбар «Инвентарь» -> UiRootBootstrap.GoToInventory (C:\My Projects\Guildmaster-Autobattler\Assets\_Project\Scripts\UI\UiRootBootstrap.cs:528-535) calls RequestTestZone(true) (line 567 -> SetTestZoneRequest(true)) and then _router.ShowInventory(gold, PublishRelicDrag).
2. DeploymentController.OnSetTestZone (Game\DeploymentController.cs:201-214, subscribed in Start line 136) -> EnterSandbox(grayZone: true) (line 240). With a live run (team-0 units standing) it builds _slots from the live units and, at line 262, sets `_encounter = null;` then `_deploying = true`.
3. The user drags a relic card: UiRootBootstrap.PublishRelicDrag -> RelicDragEvent -> OnRelicDrag (line 449-464). Drop with a unit under the cursor -> EquipOn(target.Id, e.Relic) (line 461).
4. EquipOn (552-561): sets slot.Relic, calls _runStates.SetSlotRelic(slot.GuildIndex, relic.Id) (RunStateService.cs:235 — writes RosterSlot.RelicId in the durable guild), then RebuildPreview().
5. RebuildPreview (564-577) is the only respawn path: `_loader.Load(_encounter, side)` with _encounter == null -> EncounterLoader.Load hits the guard at Units\EncounterLoader.cs:80-84, logs "[EncounterLoader] - Load: encounter == null" and returns before ResetBattle/ResetIds/EnqueueUnitSpawn. The subsequent SetPaused/FlushSpawns/RemapLiveUnits are no-ops (nothing enqueued).

Result: the live RuntimeUnit keeps its old RelicData — kit, stats, passives and UnitView all stale — while the guild save already holds the new relic; every equip attempt spams a LogWarning. Nothing else re-places the party: the only re-place is EncounterLoader.PlaceParty via Game\Flow\RosterDeployer.cs:22 (run/node deployment), so the change only becomes visible at the next node deployment. So it is not data loss, but in the sandbox the equip is visually/mechanically a no-op — exactly the flow the project treats as the headline feature (relic-from-inventory-onto-unit outside battle).

Same defect on the interlude «К построению» path: OnSetFormation (line 219-233) -> EnterSandbox(grayZone: false), also _encounter == null.

The battle path is unaffected: OnFreeDeployment (line 172) assigns `_encounter = preset.Encounter`, so RebuildPreview respawns correctly there — matching the claim's reasoning.

Extra observation worth folding into the fix: EnterSandbox builds Slot without Vessel (line 258: only Relic/Pos/LiveUnitId/GuildIndex), so simply removing the null guard is not enough — RebuildPreview would respawn sandbox units with Vessel == null. The sandbox needs its own rebuild (ResetBattle + SpawnPlayerSide, no SpawnEnemies, vessel carried over from the live unit), e.g. via the existing EncounterLoader.SpawnPlayerSide / PlaceParty seam.

---

### ОПРОВЕРГНУТО → `not-a-bug` (уверенность high)

**Заявка:** sys.airborne hard-CC leaks permanently when DisplacementSystem drops the request

`Assets/_Project/Scripts/Combat/CombatSimulation.cs:448` · заявлено P1

The guard asymmetry the claim describes is real in the source text, but neither branch produces the claimed failure in this codebase.

1) Ticks <= 0 branch. The premise (`sys.airborne` is permanent — `RuntimeEffect.IsPermanent => RemainingTicks < 0`, RuntimeEffect.cs:69 — and unremovable, and its only removal hook is `OnDisplacementEnded → RemoveByTag(KnockUp)`, CombatSimulation.cs:182-186) is correct. But there are exactly three producers of `DisplaceRequest`, and none can pass Ticks <= 0 today:
   - `AbilitySystem.ApplyDisplace` (Abilities/AbilitySystem.cs:147-149) passes `data.DisplaceTicks`; it is reached only when `data.Displaces` (line 95). `_displaces: 1` exists in exactly ONE authored asset in the whole project — `Assets/_Project/ScriptableObjects/Relics/WhirlMonk.asset` (line 73), with `_displaceTicks: 6`. Every other `_displaceTicks` value in the repo is 12 or 18, and the C# field initializer is `= 12` (AbilityData.cs:112), so a freshly created asset also lands on 12 — reaching 0 requires a designer to deliberately type 0 into the one ability that has displacement enabled.
   - `WhirlDashLandingComponent` (Effects/Components/WhirlDashLandingComponent.cs:69-73) passes its serialized `_displaceTicks` (initializer 12; the only authored instance, `ScriptableObjects/Effects/VortexDashLanding.asset:32`, is 18).
   - The chain-knockback recursion inside `DisplacementSystem.Cannonball` (line 132) is explicitly gated: `if (a.ChainDistance > 0f && a.ChainTicks > 0 …)` — it cannot emit Ticks <= 0.
   So the "AbilityData authored with DisplaceTicks 0" repro is hypothetical mis-authored data, not a state the shipped content can reach. Also note the request there displaces the CASTER (self-dash), so even that hypothetical would freeze the monk, not the victim.

2) Dead-target branch (DisplacementSystem.cs:85-90). The marker does survive on a unit that dies mid-flight, but it is inert: `IsDead` gates every consumer — `EffectSystem.Tick` skips dead units entirely (EffectSystem.cs:44) so it is never even iterated, `AbilitySystem.Tick` (line 29), `AutoAttackSystem.Tick` (line 31), and targeting queries all skip dead units, and there is NO revive/resurrect path anywhere in the combat scripts (grep for Revive/Resurrect/IsDead=false returns only RegenSystem's comment that it must not revive; `DeathSystem` sets `IsDead = true` one-way). Nor can it leak across battles: `RuntimeUnit` instances are freshly constructed per spawn by `RuntimeUnitFactory.Create` (Units/RuntimeUnitFactory.cs:90) via `EncounterLoader`, and `ResetBattle`/`DisplacementSystem.Clear` drop the old objects. Hard CC on a corpse changes no observable behaviour.

Net: this is a defense-in-depth gap (the two guard sets should be identical, ideally by having `Displace` apply the marker only after `Add` accepts the request), not a defect with a reachable failure path. Worth a one-line hardening if touched, but nothing in the current game misbehaves.

---

### ПОДТВЕРЖДЕНО → `P1` (уверенность high)

**Заявка:** Topbar tabs stay clickable over an awaited Page and permanently bury it — run loop soft-locks

`Assets/_Project/Scripts/UI/UiRootBootstrap.cs:277` · заявлено P0

CONFIRMED. Path: ActRunner.RunActAsync (Assets/_Project/Scripts/Game/Services/ActRunner.cs:101-102) calls _beat.EnterNode() -> RunBeatStage.EnterNode() = SetPhase(BattlePhase.None) (Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:59), then awaits flow.Run, which for a shop parks on MenuRouter.ShowShopAsync -> _nav.ShowAsync(RouterResultScreen<bool>(ScreenKind.Page), req.Cancellation) (Assets/_Project/Scripts/UI/MenuRouter.cs:761-770). The topbar remains drawn AND pickable over it: UiRootBootstrap.Update sets _topBar.Root display purely from (run != null && !_mainMenuOpen) (lines 366-367), and _layerTopbar is added after _layerScreens (lines 277-278), so it wins picking; RefreshShell only calls SetActiveMode/SetMenuActive and RunModeBarView never disables the chips. (Tabs-over-shop is in fact intended: docs/persist-battle-qa-findings.md:535 records Max's decision that tabs work over map/inventory/shop.) One click on «Бой» -> GoToBattle -> RequestTestZone(true) -> DeploymentController.OnSetTestZone: _deploying==false and CanEnterSandbox() == (Phase==None \|\| Interlude) (DeploymentController.cs:237-238) passes at a shop node; the party is standing (WorldStageController.OnPartyReady deploys team-0 for the whole run), so _slots.Count>0 and EnterSandbox publishes TestZoneChangedEvent(true) -> bootstrap -> _router.ShowTestZone() -> Push Sheet. UiNavigator.SyncVisibility (line 298: hidden = pageAbove \|\| (sheetAbove && Kind==Page)) sets the shop Page to display:None while it stays in the stack with its UniTaskCompletionSource unresolved, so its «Уйти» button is unpickable and the act loop stays parked. No tab sequence reaches zero Sheets: GoToMap drops the test-zone Sheet but pushes the map Sheet (WorldMapController.SetVisible(true) succeeds whenever a map exists), GoToBattle drops the map Sheet but pushes the test-zone Sheet, GoToInventory leaves two. The map is not an escape either: inside a node _choosable == null, so WorldMapController.OnNodeClicked returns early and no node can be chosen. ONE CORRECTION to the claim: "only exit is abandoning the run" is overstated — battle-center («Начать») lives in _layerBattleCenter BELOW the screens layer and becomes clickable precisely because the Page went hidden; pressing it runs DeploymentController.StartCombat, which publishes TestZoneChangedEvent(false) (line 590) -> HideTestZone -> the shop Page reappears. That is an accidental un-bury into a second broken state (enemy-less fight, Phase=Fighting, sim unpaused), not a legitimate recovery, so it does not refute the finding. Severity downgraded to P1 rather than P0: no crash, autosave intact, and ESC -> «В главное меню» exits cleanly through the run CTS — but a single design-sanctioned click during shop/chest/camp/reward/event permanently hides the awaited node Page and strands the act loop.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Push ignores ScreenKind, so a Sheet can land above the pause Modal: input un-suppresses and ESC pops the wrong screen

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:226` · заявлено P1

Confirmed by code, but via a different caller than the one claimed. Verified mechanics: Push (UiNavigator.cs:126) appends without any Kind ordering check; SyncInput (line 226) un-suppresses whenever Top.Kind == Sheet; SyncVisibility (line 298) never hides a Modal; ToggleSystemMenu (MenuRouter.cs:385-387) detects pause via AnyScreen but then calls _nav.Pop(), which removes the TOP screen; WorldMapController.SetVisible is idempotent on _visible (line 65), and the map Sheet's OnExit nulls _mapSpaceScreen (MenuRouter.cs:334) -> desync.

The claim's stated trigger is weak: BeginChoose(show:true) only fires at act entry (ActRunner.cs:72, openMap: actEntry), where the pause menu cannot realistically be open. But a reachable path exists through the rest beat: (1) player presses ESC mid-battle -> pause Modal pushed; ESC is explicitly an overlay, NOT a pause (InputService.cs:98), the sim and ActRunner keep running, and nothing closes the pause at node boundaries (only usages of IsSystemMenuOpen/ToggleSystemMenu are the topbar highlight and the ESC/menu button). (2) Battle wins -> BattleNodeFlow's bridge Modal, then the reward Page, each added to its layer AFTER the pause, so they render on top and are clickable; the reward resolves normally. (3) ActRunner loops -> _beat.EnterRestBeat -> ContinuePresenter.ShowRestBeat -> MenuRouter.ShowContinue pushes a Modal above the still-present pause; pause becomes visible again. (4) Player clicks the rest-beat "Продолжить" -> RunBeatStage.cs:54 publishes SetWorldMapRequest(true) -> WorldMapController.SetVisible(true) -> WorldMapSpaceChangedEvent(true) -> UiRootBootstrap.cs:253 -> MenuRouter.ShowMapSpace() pushes the map Sheet ABOVE the pause Modal.

Wrong behaviour at that point: GameplaySuppressed becomes false and context becomes InputContext.Map while the pause panel is still on screen, so keyboard/wheel camera pan is live under the visible menu (CameraPan/CameraZoomDelta gate only on GameplaySuppressed, not PointerOverUI). Pressing ESC then pops the map Sheet instead of the pause, nulling _mapSpaceScreen while WorldMapController._visible stays true: the in-world map stays drawn with no mode tag / no Map input context, and the "Карта" tab is a no-op.

Two corrections to the claim. (a) It is recoverable: clicking "Бой"/"Инвентарь" publishes SetWorldMapRequest(false), resetting _visible, after which "Карта" works again — so "can never be restored for the rest of the run" is overstated. (b) The same root cause has a worse sibling at step 2: with the reward Page above the pause, ESC hits AnyScreen(pause) -> Pop() -> removes the reward and ResolveDefaultIfPending returns RewardChoiceResult.Skip, silently losing a run reward.

Note the topbar tab route IS blocked (the pause screen's FillRoot TemplateContainer is fullscreen with default PickingMode.Position and sits in layer-modal above layer-topbar), so the flow-driven push above is the real trigger. Test coverage claim checks out: UiNavigatorTests has Modal_OverSheet_SuppressesToMenu / Modal_OverSheetOverPage_... / PhaseChanged_UnderModal_StaysMenu, and no Sheet-over-Modal case.

---

### ПОДТВЕРЖДЕНО → `P1` (уверенность high)

**Заявка:** Rest-beat «Продолжить» screen is a fullscreen pickable Modal — it blocks the topbar and kills camera input for the whole Interlude

`Assets/_Project/Scripts/UI/MenuRouter.cs:708` · заявлено P1

Находка подтверждается, причём главная её половина — свежая регрессия последнего коммита.

Путь воспроизведения (конкретно): узел пройден → `RunBeatStage.EnterRestBeat` (Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:49) делает `SetPhase(BattlePhase.Interlude)` и зовёт `IContinuePresenter.ShowRestBeat` → `ContinuePresenter` публикует `OpenContinueRequest` с `OnFormation != null` → `UiRootBootstrap` → `MenuRouter.ShowContinueAsync` (MenuRouter.cs:700) пушит `RouterResultScreen<bool>(ScreenKind.Modal, …)` с корнем `FillRoot(_continueUxml.CloneTree())`.

1) Перехват пиков поверх топбара — РЕАЛЬНО и это регрессия `4a087f32` (`git log -L` показывает: до него было `ScreenKind.Page`). Kind решает СЛОЙ: `UiNavigator.LayerFor` (UiNavigator.cs:60) кладёт Modal в `_layerModal`, а `BuildLayers` (UiRootBootstrap.cs:272-280) добавляет слои в порядке `layer-screens`(2) → `layer-topbar`(3) → `layer-modal`(4), и `_layerTopbar.Add(_topBar.Root)` (строка 333). То есть Page лежал ПОД топбаром, а Modal — НАД ним. Корень экрана — `TemplateContainer` от `CloneTree()` с дефолтным `PickingMode.Position`, растянутый `FillRoot` в absolute 0/0/0/0 (MenuRouter.cs:903-911); ни `ShowContinueAsync`, ни ContinueScreen.uxml, ни `.gm-continue-screen` (components.uss:1115) picking-mode не гасят (в USS его и нет — только из C#). Что именно так это и ломает, зафиксировано в самом проекте: LoadoutInventoryView.cs:50-56 — «НЕ ставить Position: он перехватывает pick на ВЕСЬ экран (root покрывает всё)», поэтому там корень явно `PickingMode.Ignore`; тест-зона и `BuildNodeFarewellScreen` тоже picking-mode задают руками. Итог: всю передышку прозрачный fullscreen-элемент над топбаром съедает клики — табы Карта/Бой/Инвентарь и чип ☰ визуально видны и включены (`Update`: топбар отображается при `run != null && !_mainMenuOpen`, UiRootBootstrap.cs:367), но не нажимаются. Это прямо противоречит контракту `IContinuePresenter.ShowRestBeat` («Оба места достижимы и табами — кнопки лишь короткий путь»); инвентарь в передышке становится недостижим вообще (в ESC-меню его нет). Незаблокированной остаётся только клавиша Escape (`OnMenuToggle` не гейтится).

2) Мёртвая камера — тоже реально, но НЕ ново: `UiNavigator.SyncInput` (строка 226) считает `modal = top.Kind != ScreenKind.Sheet`, то есть Page глушил ровно так же (об этом и говорит комментарий в MenuRouter.cs:707), `GameplaySuppressed = true` → `InputService.CameraPan/CameraZoomDelta` = 0 (InputService.cs:155-156), контекст `Menu`. Уточнение к формулировке находки: «весь Interlude» — преувеличение. Экран снимается ДО колбэка (`ShowAsync` → `RemoveScreen` перед резолвом), поэтому после «Продолжить» верхом стека становится карта-Sheet (`ModeTag == map`) → suppress снимается, контекст `Map`, камера жива; «К построению» уводит в Deployment. Камера мертва именно пока висят кнопки бита — то есть ровно в тот момент, который коммит `0410520c` («let the world breathe between nodes») и задумывал как «посмотреть на живую арену», и что подтверждает намерение в `WorldContextOf` (Interlude → Combat, «камера должна жить»). Ссылки находки на WorldMapView.cs:593/726 к этому пути отношения не имеют: карта в момент показа кнопок не активна.

Правильная форма экрана здесь — Sheet (не глушит ввод) плюс `pickingMode = Ignore` на корне и на `.gm-continue-screen`, чтобы пикались только сами кнопки; либо оставить Modal-слой, но обязательно погасить picking у корня.

---

### ОПРОВЕРГНУТО → `not-a-bug` (уверенность high)

**Заявка:** Main-menu panel is hidden with an inline display that SyncVisibility overwrites on the very next Push

`Assets/_Project/Scripts/UI/MenuRouter.cs:861` · заявлено P1

The MECHANISM half of the claim is factually right, but the claimed FAILURE cannot happen.

Mechanism (true): `menuPanel` is literally `screen.Root` — the same element `SyncVisibility` writes to. During the Modal `Push`, `UiNavigator.SyncVisibility` (UiNavigator.cs:295-304) walks top-down: the settings Modal sets neither `pageAbove` nor `sheetAbove`, so for the main-menu Page below it `hidden == false` and `s.Root.style.display = DisplayStyle.Flex` — the inline `None` set one line earlier is overwritten in the same call. So the hide (and the `onExit` restore) is dead code.

Why the failure does not manifest: the main menu is NOT left peeking out from under the settings dialog. Both screens are `.gm-screen` (components.uss:26-34 — `position:absolute`, all insets 0, `align-items:center; justify-content:center`) living in two layers that are geometrically identical (`UiRootBootstrap.AddLayer` builds every layer as `position:absolute; left/top/right/bottom = 0`, no classes, no USS selector matches `layer-screens`/`layer-modal`, and no padding-top is ever applied in code or USS — I grepped). Both dialogs carry `gm-panel gm-panel--dialog` → `width: var(--gm-dialog-width)=640px; height: var(--gm-dialog-height)=680px` (components.uss:270-273, tokens.semantic.uss:103-104), with a fully opaque body `--gm-color-surface-panel = --gm-ink-700 = rgb(28,20,13)` and identical border width. There is no entry transition on dialogs. layer-modal is added after layer-screens, so the settings panel occludes the menu panel pixel-for-pixel, both visually and for picking (the fullscreen `.gm-screen` of the settings tree eats the clicks; the menu buttons are unreachable).

The only residue is the menu screen's own full-screen wash `gm-screen--mainmenu { background-color: --gm-color-scrim-soft }` = rgba(0,0,0,0.42), which was already on screen the frame before the click — so nothing pops. Note it cuts the other way too: had the hide actually worked, `SuppressScrim` would leave the settings dialog over an UNDIMMED backdrop (a sudden brightening), so the overwrite yields the look the comment asks for. `SuppressScrim` itself is a screen property (UiScreen.cs:35) and is honoured correctly by SyncVisibility (UiNavigator.cs:320) — the `gm-screen--scrimless` bug class the claim cites is exactly what that refactor already fixed, and it is not repeated here in any observable way. `HasVisiblePage`/backdrop is also unaffected (UiRootBootstrap.cs:440 ORs in `_mainMenuOpen`, true throughout).

So: no state → wrong behaviour path exists; the user sees the intended screen. What remains is a dead hide/restore pair that lies about who owns `style.display` — worth deleting as a P3 cleanup (and it would become a real visual bug only if the two dialogs' sizes ever diverge), but it is not a P1 correctness defect today.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Reward drop-list resolves relic names from the offered choices, so the player sees raw content ids

`Assets/_Project/Scripts/UI/RewardScreenView.cs:127` · заявлено P1

Confirmed by reading the code and both call sites. Repro path: RewardPresenter.PresentAsync (Assets/_Project/Scripts/Game/Flow/RewardPresenter.cs:41-53) rolls `choices` via RewardService.RollChoices — which picks from the WHOLE RelicData pool and knows nothing about the player's stash — and publishes OpenRewardRequest(choices, full = _runStates.RelicInventoryFull, run.RelicInventory, ...). MenuRouter.ShowRewardAsync (MenuRouter.cs:613-623) forwards req.CurrentInventory and `nameOf: relic => _loadoutVm.Name(relic)` into RewardScreenView.Build. When RunState.RelicInventory.Length >= RelicCapacity (GameConfig.asset `_relicCapacityBase: 12`, RunStateService.cs:142), Build enters the `inventoryFull` branch (line 118); RewardScreen.uxml really does contain #drop-section/#drop-list, so the rows render. For each owned id it calls FindById(choices, id) — searching the 3 OFFERED relics, not the content DB. The stash ids are near-always absent from the 3 offered ones (RollChoices only guarantees no dupes *within* a showcase; overlap with owned relics is incidental), FindById returns null, LoadoutViewModel.Name(null) returns string.Empty (LoadoutViewModel.cs:81), Coalesce falls through to the raw id, and the row label becomes e.g. `relic.flame_swordsman`. No guard anywhere rescues it: `nameOf` only accepts RelicData, so Build has no way to resolve an id — the lookup pool is simply the wrong collection (the correct source is the content DB / a by-id resolver). The other call site (DevTools/UiPreviewCatalog.cs:96) passes inventoryFull:false + currentInventory:null, so it never exercises the branch — i.e. the editor stand hides the bug rather than refuting it. Two things pull severity below P1: the failure is purely cosmetic text (clicking the row still sets `drop` to the correct id, and RemoveRelic/TryAddRelic behave properly), and it only surfaces deep in a run once the stash actually holds 12 relics (with ~10-11 droppable RelicData assets, that needs duplicates). Real bug, but P2.

---

## Догон заявок захода 1 — тринадцать

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Inter-tick interpolation alpha is a frame-rate ratio, not the tick phase — units render at a fixed offset and never interpolate smoothly (two round-1 auditors independently reported this)

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:181` · заявлено P1

Mechanism CONFIRMED, consequence/severity overstated (the "P1" half is wrong; the code half is right).

Concrete path:
- `CombatPresenter.Update` (Assets/_Project/Scripts/Presentation/CombatPresenter.cs:181-187) computes `alpha = Clamp01(Time.deltaTime / SimConstants.TickDelta)` and hands the same value to every `UnitView.UpdateInterpolation(alpha)` and `ProjectileView.Tick(alpha)`.
- `UnitView.UpdateInterpolation` (Assets/_Project/Scripts/Presentation/UnitView.cs:251-259) does `Vector2.Lerp(_unit.PreviousPosition, _unit.Position, alpha)` and writes it straight to `transform.position` — no additional smoothing layer.
- The real tick phase lives in `CombatLoopService._accumulator` (Assets/_Project/Scripts/Game/Services/CombatLoopService.cs:51-64) and is never exposed. `SimConstants.TickDelta = 1/30` (Core/Simulation/SimConstants.cs:13).
- `MovementSystem.Tick` sets `unit.PreviousPosition = unit.Position` once per tick (Combat/Systems/MovementSystem.cs:44).

So at a stable 60 fps alpha is a constant 0.5, at 144 fps a constant ~0.21: the rendered position only changes when the sim ticks, i.e. locomotion is stepped at 30 Hz with a constant sub-tick lag instead of being smoothed to display rate — exactly the thing the interpolation exists to prevent (documented intent: docs/wiki/tech/20-explanation/presentation.md §3). Secondary artifacts that do follow: frame-time jitter makes alpha jitter, wobbling moving units by a fraction of a tick step; and because `Time.deltaTime` is timeScale-scaled while `TimeScaleService` (Game/Services/TimeScaleService.cs:201) drives pause/hitstop/finisher-slowmo, alpha collapses toward 0 on those transitions and units snap back up to one full tick step (~moveSpeed × 0.033 ≈ 0.1 world units).

Why P1 is too high:
- Purely presentational. The presenter only reads sim state; nothing here feeds the simulation, and worst case the render lags/leads by less than one tick step. Picking is unaffected in practice: deployment-phase units aren't ticking, so `PreviousPosition == Position` and offset is zero for `DeploymentController`'s pick path.
- At <=30 fps alpha clamps to 1 and the view sits exactly on the tick position, so "fixed offset" isn't universal — it's fps-dependent.
- Not a regression: the line is unchanged since f97d99b1 (2026-05-30), and the project already tracks it as known tech debt in docs/wiki/tech/00-meta/tech-changelog.md §3.3 ("правильная — accumulator / TickDelta ... Косметика, не срочно") with the fix noted in docs/wiki/tech/40-planning/visual-harness.md step 6.

Fix is one seam: pass `_accumulator / SimConstants.TickDelta` from `CombatLoopService` to the presenter. Real, worth doing, but P2 (visual quality), not P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** Every LitMotion feel tween runs on scaled time while the config and comments promise unscaled — hit feel freezes during the finisher pause and stretches 10x in slowmo

`Assets/_Project/Scripts/Presentation/UnitView.cs:794` · заявлено P1

Half right. The MECHANISM is confirmed: none of the six LMotion.Create calls in Assets/_Project/Scripts/Presentation/UnitView.cs (655 flip, 751 flash, 776 squash, 794 nudge, 821 anticipation/lunge, 906 acquire) pass .WithScheduler, LitMotion's MotionScheduler.cs:12 sets DefaultScheduler = Update (PlayerLoopMotionScheduler with MotionTimeKind.Time), MotionUpdateJob.cs:40-42 maps MotionTimeKind.Time to the SCALED DeltaTime, and nothing under Assets/ ever assigns MotionScheduler.DefaultScheduler or uses an IgnoreTimeScale scheduler. Time.timeScale really does reach 0 and 0.1 (TimeScaleService.cs:201 is the sole writer; CombatFeelDirector.cs:101-108 builds segments 0f / 0.5 / 0.1 / ramp-to-1 from CombatFeelConfig FinisherPause/DeathFactor/ShatterFactor).

The CONSEQUENCE half is wrong: freezing during the finisher pause and stretching in slowmo is the documented design, not a defect. TimeScaleService's class doc (lines 39-43) states the intent outright - the sim accumulates already-scaled Time.deltaTime "как и анимации/партиклы на scaled-времени", with only camera pan kept unscaled (CameraModeController.cs:391). CombatFeelDirector.cs:97-98 says the finisher ladder is built to coincide with the death sequence "на scaled-времени", and stage 3 (FinisherShatterFactor 0.1, tooltip "сильное slowmo во время разлёта осколков", CombatFeelConfig.cs:204-207) is meaningless unless presentation tweens are scaled. Stage 1 is tooltipped "полная пауза (timeScale 0) на финальном ударе, пока держится хит-эффект" (CombatFeelConfig.cs:198) - the held flash IS the point of the pause, and UnitView.DriveDeath lines 947-950 deliberately gate the death clip on that same non-fading flash. BattleInputController.cs:49 likewise documents that pause halts presentation.

Also, the claim's "comments promise unscaled" does not hold at the cited line. Every "unscaled" comment in UnitView (362-367, 550-567, 731-736, 873, 975) describes the hand-rolled hitstop / HoldHitFrame / death-anticipation timers, which are correctly driven by Time.unscaledDeltaTime. Nothing at or near line 794 promises unscaled. The only false text is three tooltips in Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs - _hitNudgeDuration (51-52), _anticipationDuration (82-83), _lungeDuration (86-87) each say "сек (unscaled)" while feeding scaled tweens. That is tooltip drift in a different file with no runtime failure: the tweens are AddTo(gameObject), zero their offsets on completion (UnitView 801, 827), TimeScaleService.Reset()/Dispose() clear stuck sequences and restore timeScale = 1, so no stuck-offset or deadlock path exists. Worst real symptom: at GameSpeed 2x/3x the fixed unscaled hitstop window (0.02-0.09s) desyncs slightly from the now-faster 0.12s nudge. P3 comment/tooltip fix, not P1.

---

### ПОДТВЕРЖДЕНО → `P1` (уверенность high)

**Заявка:** "Начать" in between-nodes formation mode starts a phantom enemy-less battle (un-pauses sim, flips phase to Fighting) while the act loop is still waiting for a node choice

`Assets/_Project/Scripts/Game/DeploymentController.cs:147` · заявлено P1

CONFIRMED mechanism, but half the described consequence is wrong and the other half understates the damage.

Reproduction: node completes -> ActRunner.cs:69 calls _beat.EnterRestBeat -> RunBeatStage.cs:51-56 does RequestReset (BattleBootstrap.ResetToWorld :103-111 -> PlaceParty -> CombatSimulation.ResetBattle sets _outcome=Ongoing, then SetPaused(true)), SetPhase(Interlude), shows rest-beat buttons; ActRunner.cs:72 is awaiting _chooser.ChooseAsync. Player clicks "К построению" -> SetFormationRequest(true) -> DeploymentController.OnSetFormation (:220) -> CanEnterSandbox passes on Interlude (:238) -> EnterSandbox(grayZone:false) (:240) sets _deploying=true, _testZone=FALSE, _encounter=null, SetPhase(Deployment). Deployment phase makes "Начать" visible: UiRootBootstrap.cs:376-377 -> RunModeBarView.SetFighting(false,..) :124 display=Flex; handler is unguarded (UiRootBootstrap.cs:330 onStart -> BattleSession.cs:210 RequestStart -> the lambda at DeploymentController.cs:147). The guard `_deploying && !_testZone` passes because it only excludes the GRAY test zone, not formation mode (OnSetFormation's own exit check at :229 uses `_encounter == null` as the sandbox marker; StartCombat has no such check). StartCombat (:580) runs: SetPaused(false), SetPhase(Fighting), view closed, camera to action view.

WHERE THE CLAIM IS WRONG: the phantom battle does not persist, and the act loop is never confused — ActRunner awaits node choice and is fully decoupled from BattlePhase. On the first unpaused tick CombatSimulation.CheckOutcome (:619-645) sees only team 0 alive -> _outcome=Win(0) + OnBattleEnded -> CombatPresenter.HandleBattleEnded (:463-475) publishes BattleEndedEvent -> BattleBootstrap.OnBattleEnded (:137) puts phase straight back to Interlude. Visible symptom is a spurious VICTORY: finisher cinematic full-stop + slowmo + shake (CombatFeelDirector.cs:99-110), victory audio, player ejected from formation.

WHERE IT IS WORSE THAN CLAIMED: the sim is left with a TERMINAL _outcome=Win(0) and nothing resets it before the next real battle. BattleBootstrap.LaunchBattle (:86) calls DeployParty() — the only ResetBattle path (EncounterLoader.cs:172) — solely when !HasLivingParty(), and the party survived the phantom victory, so it is skipped; SpawnEnemies (EncounterLoader.cs:206) and FlushSpawns never touch _outcome. At the next battle node CombatSimulation.Tick early-returns at :232 and CombatLoopService idles at :44 -> enemies and party stand frozen, clock frozen, and BattleFlow.cs:54 `await WaitOutcomeAsync` never resolves. The run soft-locks; only escape is ESC -> "В меню".

Severity kept at P1: one click on a legitimately visible button in the normal between-nodes flow bricks the next battle, but autosave already ran at ActRunner.cs:122, so there is no save corruption and reloading the run recovers — that keeps it below P0.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Formation mode has no exit: SetFormationRequest(false) is never published by anyone, so _deploying stays true across node selection and non-battle nodes

`Assets/_Project/Scripts/Game/DeploymentController.cs:229` · заявлено P1

MECHANISM CONFIRMED. `SetFormationRequest` has exactly one publisher in the whole solution: `Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:55`, `onFormation: () => _formationPub?.Publish(new SetFormationRequest(true))` — always `true`. Nothing anywhere publishes `false` (grep over all of Assets returns only the struct definition in TestZoneMessages.cs:33, the DeploymentController subscription, and that one publish). So the `else if` at DeploymentController.cs:229-232 is dead code and `ExitTestZone()` is unreachable from formation state.

Concrete path: rest beat between nodes -> `RunBeatStage.EnterRestBeat` sets phase Interlude and shows «К построению» -> `OnSetFormation(true)` -> `CanEnterSandbox()` passes on Interlude (line 237) -> `EnterSandbox(grayZone: false)` sets `_deploying=true`, `_testZone=false`, `_encounter=null`, `_sandboxReturnPhase=Interlude`, phase Deployment, free deployment camera. To pick the next node the player presses the «Карта» tab -> `UiRootBootstrap.GoToMap` (line 554-562) publishes `SetTestZoneRequest(false)`, and `OnSetTestZone` line 212 requires `_deploying && _testZone` — `_testZone` is false in formation, so it logs "не в тест-зоне — выходить нечего (no-op)". Node chosen -> `ActRunner.cs:101` `_beat.EnterNode()` -> `RunBeatStage.cs:59` only does `SetPhase(None)`; nothing clears `_deploying`. Confirmed leak: `_deploying` stays true through node selection. Downstream while stuck: `Tick()` keeps running (gated on `_deploying`, not phase), DeploymentView + zones + rings stay alive, the deployment camera is never returned via `ExitToActionView()`, and any later `SetTestZoneRequest(true)` from the «Бой»/«Инвентарь» tabs no-ops at line 206 (`if (_deploying) return`).

WHERE THE CLAIM IS OVERSTATED (two halves, one wrong): "has no exit" is false. Two exits work: (1) `_session.BindStart` at line 147 fires `StartCombat()` when `_deploying && !_testZone`, which is exactly formation state, and the topbar center button IS shown because phase is Deployment (`UiRootBootstrap.cs:376`) — plus the Enter key at line 346 does the same; both clear `_deploying` (line 584). They exit badly (unpause sim + phase Fighting with no enemies during the interlude), but they exit. (2) The state self-heals at the next battle node: `LaunchBattle` -> `RequestDeployment` -> `OnFreeDeployment` (lines 188-192) fully re-initialises `_deploying`/`_testZone`/`_encounter`/phase/camera, so the leak cannot persist past the next combat node. Blast radius is therefore bounded to the stretch between formation and the next battle node (non-battle nodes: shop/event/rest), where interaction is additionally largely muted by `GameplaySuppressed` on the modal event screens and by the phase-None opaque backdrop. No crash, no save corruption, no cascade into the next fight's outcome (a phantom `ReportOutcome` is swallowed — `BattleSession.cs:134` `TrySetResult` on a TCS armed only by `RequestLaunch`). Real dead-code/missing-wiring defect with visible jank and a missing UI affordance, not broken core functionality: P2.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** Root IRngService is seeded from wall-clock time and never reseeded from RunState.Seed, so node payloads, reward showcases and shop stock are not reproducible from a save

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs:57` · заявлено P1

MECHANISM: CONFIRMED, exactly as described. `RootLifetimeScope.cs:57` registers `new XorShiftRng(GenerateRootSeed())`, and `GenerateRootSeed()` (lines 203-206) returns `(ulong)DateTime.UtcNow.Ticks`. `IRngService.Reseed(ulong)` exists as a seam (`IRngService.cs:42`, doc explicitly citing `runSeed + battleIndex + attempt` for persist-world), but a grep over all of `Assets/_Project` shows Reseed is called only from `XorShiftRng`'s own ctor (`XorShiftRng.cs:15`) and from tests — never from production code. The dev console hook is a stub that logs and does nothing (`GuildmasterCommands.cs:142-145`).

That root singleton is the RNG behind all three named consumers: `GameFlow` injects it (`GameFlow.cs:42,59,75`) and hands it to every `RunContext` (`GameFlow.cs:95,179,221`), so `ctx.Rng` in `NodeResolver.PickBattlePreset` (`NodeResolver.cs:164,166`) and `PickContent<T>` (`NodeResolver.cs:142`) and `RandomEventFlow.Roll` (`RandomEventFlow.cs:20,29`) is the wall-clock instance. `MapGenerator` writes `PayloadId = string.Empty` for every node (`MapGenerator.cs:77`), so the payload branch is never short-circuited by an id — every payload is rolled at node entry. `RewardService` (`RewardService.cs:32,73,81`) and `ShopController` (`ShopController.cs:66,90` → `_shopSeed` → `RelicPricer`) take the same singleton.

REPRO: run with Seed=X, quit at an unfinished node (`ActRunner.cs:118-120` autosaves only *after* the node completes) → relaunch → Continue (`GameFlow.cs:127-130`) → `RunStateService.Load()` restores Seed=X, `BeginAct` no-ops because the map is already in the save (`RunStateService.cs:88`) → the pending node resolves against a freshly wall-clock-seeded RNG → different `BattlePresetData`, different `?`-roll, different 1-of-3 relic showcase, different shop shelf and prices. So the literal claim holds.

WHY THE SEVERITY IS WRONG (the consequence half is overstated): nothing in the codebase today consumes run-seed reproducibility, and nothing observable breaks.
1. The run seed is itself wall-clock — `NewDefaultRun(DateTime.UtcNow.Ticks)` at `GameFlow.cs:133,93,165` — and there is no UI, console command or config to enter a seed. "Reproducible from a save" is not a feature that exists and regressed; it is a feature that was never wired.
2. The part a player identifies as "the run" — the map graph — *is* reproducible: `RunStateService.cs:92` uses its own `new XorShiftRng(Seed + CurrentActIndex)`, and `RunState.Map` is serialized in the save, so a reload never reshuffles the map.
3. Coop/replay, the stated reasons for determinism in the doc comments, are unimplemented (`SoloReadyGate`/`SoloLocalPlayer`/`SoloPlayerIntentSource` in `RootLifetimeScope.cs:121,130,131`) and the parallel gap is an acknowledged TODO: the *battle* seed is also wall-clock (`CombatLifetimeScope.cs:112,179-183`) with `// TODO Фаза MP: сид боя должен прийти от хоста` at line 176. Nothing desyncs today — the model is host-authoritative and only the host ticks.
4. Distributions are unaffected; no crash, no wrong number, no stuck state.

Actual live harm reduces to mild save-scumming (quit-and-Continue to reroll a pending node's enemies/reward/shelf, at the cost of refighting the node) plus a seam that must be wired before replay/coop/seed-sharing. That is an architecture gap, not a P1 defect — P3. If you weight the save-scum exploit as a balance bug in its own right, P2 is the ceiling; P1 is not defensible.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** ContentAuditor bakes stats WITHOUT the class/species cascade — MaxHP, EHP and MoveSpeed columns are wrong for every unit, so balance decisions rest on wrong numbers

`Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:82` · заявлено P0

MECHANISM CONFIRMED, CONSEQUENCE/SEVERITY OVERSTATED.

Confirmed omission. `ContentAuditor.BuildRow` (C:\My Projects\Guildmaster-Autobattler\Assets\_Project\Scripts\Balance\Editor\ContentAuditor.cs:82-84) builds `new Stats(config)` and adds only `data.Stats`. Every other consumer of the cascade calls the two shared appliers first:
- `RuntimeUnitFactory.Create` — RuntimeUnitFactory.cs:65-75 (`ClassBaseline.Apply` → `EnemyScalers.Apply` → persona)
- `StatMath.BuildEffective` — EditorTools/ContentHub/Core/StatMath.cs:21-26
- `UnitStatPreview.Build` — Combat/Stats/UnitStatPreview.cs:63-67

`ContentAuditor.Run` (line 41) does not even load `ClassBalanceConfig`, although `BalanceAssets.LoadClassBalanceConfig()` exists (BalanceAssets.cs:21) and `SimEnvironment.cs:48` uses it. Git confirms the retrofit miss: commit 5335cb3a ("unit classes as HP/move-speed baseline (4-level stat cascade)") added `ClassBaseline` and wired `SimEnvironment`, but left `ContentAuditor` untouched.

CONCRETE NUMBERS (assets, not theory). ClassBalanceConfig.asset: baseHp 2000, baseMoveSpeed 3, mults Tank 1.5/0.85, Assassin 0.75/1.1, backline 0.65/0.75. Goblins.asset scalers: MaxHP PercentMult -0.6, MoveSpeed +0.1. StatsConfig defaults: MaxHP 1200, MoveSpeed 3. Only BaseRelic/TrainingDummy (Override 1200) and Treant (Flat +600) author MaxHP at all — all other relics and all goblins carry no MaxHP/MoveSpeed in `_stats`, so they fall to the `StatsConfig` default in the auditor:
- Defender (Tank): real 3000 HP, report 1200 (2.5x low)
- Treant (Tank, Flat +600): real (3000+600)=3600, report 1800
- Assassin: real 1500, report 1200; MoveSpeed real 3.3, report 3
- GoblinGrunt (Bruiser+Goblins): real 2000x0.4=800, report 1200 (50% high)
- GoblinWarrior (Tank+Goblins): real 3000x0.4=1200 — report 1200, coincidentally correct

Because EHP = MaxHP x (K+armor)/K / takenEff (lines 111-112), both EHP columns inherit the error. A second-order effect that the claim misses: with MaxHP frozen near 1200 for nearly everyone, `FlagOutliers`' EHP z-score (line 139) has almost no HP variance to work with, so the audit is blind to exactly the HP spread (800..3600) the cascade introduces.

WHAT IS OVERSTATED — two parts:
1. "wrong for every unit" — the cascade touches ONLY MaxHP and MoveSpeed (ClassBalanceConfig.cs:47-48, Goblins.asset). AutoAtk, AtkSpeed, AtkRange, PhysArmor, MagicArmor, DmgTaken/DealtEff, Lifesteal and the whole RawDPS column are correct, and GoblinWarrior/BaseRelic/TrainingDummy MaxHP happens to land right. Roughly half the report is sound.
2. "P0" — this is an editor-only report tool reachable solely from a menu item (`BalanceMenu.cs:15` `Alebardium/Balance/0. Audit Content`), writing CSV/MD into BalanceReports/. Zero runtime, gameplay, player-facing or save impact; no crash, no data corruption. The simulation benches (DpsBench/SurvivabilityBench/DuelMatrixBench via `SimEnvironment` → `RuntimeUnitFactory`) are correct, as are the Content Hub table and the in-game unit panel — so a human comparing them would see the discrepancy. Impact is "dev decision-support tool silently reports wrong HP/EHP", which is P2 (elevated by the explicit "таблица не врёт" contract in the sibling docstrings, but not P0/P1). Fix is a two-line retrofit: load `BalanceAssets.LoadClassBalanceConfig()` in `Run` and call `ClassBaseline.Apply` + `EnemyScalers.Apply` before line 84.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** ActConfig.ToGenConfig() hands out the SO own MapGenConfig and Validated() mutates it in place — clamped values get written back into the asset on disk

`Assets/_Project/Scripts/Guild/ActConfig.cs:18` · заявлено P1

The first half of the claim is correct, the second half (the part that made it P1) is wrong.

CONFIRMED — the aliasing and in-place mutation are real. `Assets/_Project/Scripts/Guild/ActConfig.cs:18` is `public MapGenConfig ToGenConfig() => (_map ?? new MapGenConfig()).Validated();`. `MapGenConfig` is a reference type (`[Serializable] public sealed class`, `Assets/_Project/Scripts/Guild/MapGenConfig.cs:13`), and `Validated()` (lines 54-68) assigns to its own fields (`Columns`, `MinColumnWidth`, `MaxColumnWidth`, `EdgeColumnWidth`, `MaxEdgesPerNode`, `EdgeColumns`, `Zones`, `Anchors`) and ends with `return this;`. So the object handed to callers IS the SO's serialized `_map` instance, and clamps land on the SO's in-memory managed fields. Callers: `GameFlow.cs:167` (`_actConfig.ToGenConfig()`) and the EditMode test `Assets/_Project/Tests/EditMode/Guild/ActConfigAssetTests.cs:24`, which loads the real asset via `AssetDatabase.LoadAssetAtPath<ActConfig>` and then mutates it.

REFUTED — "written back into the asset on disk". A managed-field write on a loaded ScriptableObject does not touch Unity's native dirty flag, so Unity has no reason to serialize it. There is no `EditorUtility.SetDirty`, `AssetDatabase.SaveAssets`, or `SaveAssetIfDirty` anywhere under `Assets/_Project/Scripts/Guild` (grep clean), and none of the call sites dirty or save `ActConfig`. In a player build nothing is writable at all. So `ActConfig.asset` on disk is never modified by this code; the mutation lives only in the in-editor instance and dies at the next domain reload.

Blast radius is further limited by three things:
1. No clamp fires on the shipping asset. `Assets/_Project/ScriptableObjects/Configs/ActConfig.asset` lines 16-21 hold Columns:15, EdgeColumnWidth:3, EdgeColumns:1, Min:5, Max:7, MaxEdges:4 — every guard in `Validated()` is a no-op today, so `Validated()` currently writes nothing different.
2. `Validated()` is idempotent (after one pass Columns>=3 and EdgeColumns*2<=Columns-2), so repeated `ToGenConfig()` calls across many acts and 20-seed test loops cannot drift or accumulate.
3. `MapGenerator.Generate` only reads `cfg` (no writes anywhere in `MapGenerator.cs`), and `RunStateService.BeginAct` (`RunStateService.cs:85-94`) does not retain the reference — so the aliased object cannot be corrupted by downstream code either.

The only real-but-narrow harm, editor-only: line 64 `if (EdgeColumns * 2 > middle) EdgeColumns = middle / 2;` writes a field the designer did not touch, derived from another. If someone temporarily authors `Columns = 3` and enters play, `EdgeColumns` silently becomes 0 in the live instance and in the Inspector; raising `Columns` back to 15 does not restore it, and a later unrelated inspector edit + save would then commit `EdgeColumns: 0`. That is a designer-workflow annoyance in a single-config asset with valid values today, not a data-corruption bug — P3. The correct fix is still trivial and worth doing: return a clone (`Validated()` on a copy, or make `Validated()` non-mutating).

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** "relic.base" is hardcoded in five non-test files while only RunStateService honours GameConfig.StartingRelicId

`Assets/_Project/Scripts/Game/Flow/RewardService.cs:27` · заявлено P1

The factual half of the claim checks out; the severity half does not.

CONFIRMED FACTS. `"relic.base"` is a behavioural constant in five non-test files: `Assets/_Project/Scripts/Game/Flow/RewardService.cs:27` (used at :59 to exclude the base kit from the reward showcase), `Assets/_Project/Scripts/Game/Flow/GuildRoster.cs:16` (fallback kit at :32/:34), `Assets/_Project/Scripts/UI/LoadoutHubView.cs:36` (at :99 decides whether a vessel card is a drag source), `Assets/_Project/Scripts/Guild/RunState.cs:62` (`RosterSlot.RelicId` field default), and `Assets/_Project/Scripts/DevTools/UiPreviewCatalog.cs:92,151,154,178,261,338`. `GameConfig.StartingRelicId` (`Assets/_Project/Scripts/Data/Definitions/GameConfig.cs:72,95`) is read only in `RunStateService.cs:57` (NewDefaultRun) and `:173` (BaseRelicId, consumed by EquipRelic:190 / UnequipRelic:207,212). No other consumer exists — grep over `Assets/_Project/Scripts` returns exactly those two sites. Ironically `RunStateService` itself hardcodes the same literal twice as its fallback.

WHY P1 IS WRONG. The divergence is unreachable in the project as it stands — there is no state, save file, or code path that makes the two values differ:
1. `Assets/_Project/ScriptableObjects/Configs/GameConfig.asset` does not contain a `_startingRelicId` key at all (the asset predates the field and was never re-serialized; it stops at `_relicCapacityMax: 16`). So at load the property yields either `""` or the initializer `"relic.base"`, and both branches of `string.IsNullOrEmpty(...) ? "relic.base" : ...` at RunStateService:57/173 collapse to the same `"relic.base"`.
2. There is exactly one relic asset with that id (`ScriptableObjects/Relics/BaseRelic.asset:15 _id: relic.base`); the other ten are `relic.assassin`, `relic.defender`, etc. Nothing else could be a base kit.
3. Triggering it requires a human editing an inspector field that has never been touched — not a runtime path, not a save-migration, not a coop/host case.

WHAT WOULD BREAK IF SOMEONE SET IT (the latent, not-current, consequence): with `_startingRelicId = "relic.dummy"`, `NewDefaultRun` (RunStateService:57) equips `relic.dummy` while `RewardService.RollChoices` (:59) keeps filtering `relic.base` — so the dummy kit becomes a legal reward drop and the real base relic disappears from the pool; `LoadoutHubView:99` would render the dummy kit as a drag source whose `UnequipRelic` (RunStateService:207) then silently returns false, and `GuildRoster:32` would still fall back to `relic.base`. That is a cosmetic/config-hygiene defect discovered only by an author who opts into it, i.e. a "config knob is a lie, extract the constant behind one owner" cleanup — P3. It is not a P1: no player-visible wrong behaviour, no data loss, no crash exists today, and the 395-test suite (RewardServiceTests.cs:34, RunStateEquipTests.cs, GuildRosterTests.cs) asserts the current single-value behaviour.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** UiNavigator discards every CancellationTokenRegistration — the run-long CTS accumulates callbacks that retain each closed screen for the whole act

`Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:211` · заявлено P1

Mechanism confirmed, consequence overstated. Push (UiNavigator.cs:138-139) and ShowAsync (UiNavigator.cs:211-212) discard the CancellationTokenRegistration, and UiScreen never releases Root on exit (UiScreen.cs:38, OnExit is a no-op), so each closure pins a closed screen plus its detached VisualElement tree in the CTS callback list. Real callers that pass the run-long token (RunContext.Cancellation = GameFlow._runCts.Token, GameFlow.cs:176/179): reward (MenuRouter.cs:625 via RewardPresenter.cs:52-53), shop (MenuRouter.cs:769 via ShopFlow.cs:31), chest (MenuRouter.cs:786 via ChestFlow.cs:29), camp (MenuRouter.cs:804 via CampFlow.cs:31), continue-gate (MenuRouter.cs:733 via ContinuePresenter.cs:39-44 / BattleNodeFlow.cs:59). That is roughly 1-2 stranded registrations per node, ~15-25 per act. WHERE THE CLAIM IS OVERSTATED: (1) retention is bounded to one act, not growing over the session - GameFlow disposes _runCts in finally (GameFlow.cs:198) and before creating a new one (GameFlow.cs:175), and CTS Cancel/Dispose clears the registration list; screens pushed with ctx.NodeCancellation (text event MenuRouter.cs:635, farewell :669) are freed every node because ActRunner.cs:97-99 cancels+disposes the node CTS each iteration, and beatCts is a using (ActRunner.cs:68). (2) No behavioural fault when the token finally fires (GameFlow.cs:207 RequestReturnToMainMenu): stale RemoveScreen calls are idempotent (UiNavigator.cs:252-253) and ResolveDefaultIfPending is guarded by _resolved (UiScreen.cs:92-95); detached VisualElements neither tick nor render. Net effect is a bounded per-act memory retention (order of a megabyte of dead UXML trees) plus noise in UiTrace on cancel - a hygiene fix (keep the registration, dispose it on exit/resolve), not a P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** The whole Guildmaster.Net assembly is unreferenced dead code, and its Steam bootstrap does not do what its name and summary claim

`Assets/_Project/Scripts/Net/FacepunchTransportBootstrap.cs:9` · заявлено P1

Half right, and the wrong half is the one that carries the severity.

WHAT IS TRUE (the naming/summary half). `FacepunchTransportBootstrap.cs:7-11` claims it "Устанавливает Facepunch Transport как транспорт NGO", and `docs/wiki/tech/40-planning/phase-1-combat-core.md` §6.1 repeats that responsibility ("Init Steam + Facepunch transport"). The body (lines 17-41) only calls `SteamClient.Init(_appId, false)`, `SteamClient.RunCallbacks()`, `SteamClient.Shutdown()`. It never touches `NetworkManager` or any `NetworkTransport`. This is not merely un-implemented — it is un-implementABLE as written: the Facepunch transport package is not installed (`Packages/manifest.json` has only `com.unity.netcode.gameobjects": "2.11.2"`; `packages-lock.json` has zero facepunch entries; `grep "class FacepunchTransport"` across Assets/ and Packages/ matches only this file's own name). So the type the summary names does not exist in the project. Class name + XML summary + wiki row all overstate. Verified fact.

WHAT IS FALSE (the "unreferenced dead code" half, i.e. the P1 justification).
1. The assembly IS referenced: `Assets/_Project/Scripts/Game/Guildmaster.Game.asmdef:10` lists `"Guildmaster.Net"`. `docs/wiki/tech/10-reference/assemblies.md:67,71` documents this deliberately — Game pulls NGO transitively through Net. So "unreferenced" is literally wrong.
2. It is not accidental dead code, it is documented parked spike scaffolding. `assemblies.md:67` marks it "(спайк в Фазе 1)"; phase-1 doc §6 says "Архитектура закладывается сейчас; полная сеть — Фаза 6"; §6.2 lists "Facepunch transport поднят" as a not-yet-met DoD item; `NetworkCommandRelay.cs:11-19` carries an explicit remarks block stating its lockstep broadcast will be reworked at the MP phase; and the third file lives in a folder literally named `_Parked/`.
3. There is no reachable failure. `grep -rl c459789c7b4121b4aacb49051575b0ed Assets/ ProjectSettings/` matches only the .meta — the MonoBehaviour is on no scene and no prefab. No `using Guildmaster.Net` anywhere; the only mention outside the folder is a comment at `Game/Input/BattleInputController.cs:51`. `Awake` therefore never runs, so no wrong Steam state is ever produced. Nothing is P1-broken; the cost is 1.4 KB of compiled-but-inert code plus three inaccurate doc surfaces.

Latent-only (do not raise severity): if this were ever attached, `OnDestroy` (line 40) calls `SteamClient.Shutdown()` guarded only by `IsValid`, without tracking whether *this* component did the init — it would tear Steam down for whoever else owned it. Today that cannot fire: this is the project's only `SteamClient.Init` call site (grep over `Assets/_Project` returns lines 23/35/40 of this file only), and the component is unattached.

Correct framing: a doc/name-vs-code accuracy defect in an unwired spike, worth a rename to `SteamClientBootstrap` and a summary fix (plus correcting the wiki row), not a P1. Note that `docs/tech-docs-sync-plan.md` (lines 207, 966) already tracks doc-vs-code drift in this area, and per project memory tech-doc edits were deliberately deferred until after the code refactor.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** Stat cascade assembly is copy-pasted into three live sites; StatMath.AttacksPerSecond is a verbatim duplicate of UnitStatPreview implementation, so editor previews and the sim can drift apart

`Assets/_Project/Scripts/EditorTools/ContentHub/Core/StatMath.cs:19` · заявлено P1

The duplication half of the claim checks out literally; the "drift" consequence is a maintainability risk, not a defect that exists today, so P1 is too high.

What is actually true:
1. Cascade assembly is repeated in three live sites, same order (StatsConfig defaults -> ClassBaseline.Apply -> EnemyScalers.Apply -> persona mods):
- Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs:65-75 (the sim path; then additionally vessel perks 77-78, items 81-87, passives 106-108)
- Assets/_Project/Scripts/Combat/Stats/UnitStatPreview.cs:63-68 (runtime inventory/tooltip preview, injected as IUnitStatPreview from RootLifetimeScope)
- Assets/_Project/Scripts/EditorTools/ContentHub/Core/StatMath.cs:21-27 (editor Content Hub; called from ContentIndex.cs:126 with both statsConfig and classBalanceConfig)
2. AttacksPerSecond is a verbatim duplicate: StatMath.cs:31-36 vs UnitStatPreview.cs:71-76 — the same four lines (AttackTiming.IntervalTicks, guard on <=0 / int.MaxValue, SimConstants.TickRate / interval). Not merely similar, identical.
3. The copy-paste pattern has already produced one real divergence elsewhere: Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs:81-84 bakes only `new Stats(config)` + persona mods and omits ClassBaseline.Apply and EnemyScalers.Apply entirely, so the SimBench audit report's MaxHP/MoveSpeed columns are missing the class baseline and enemy species scalers the sim applies. That is a fourth site, and it is stale — which is why I do not dismiss the mechanism as theoretical.

Why P1 is wrong (no failure path exists at the cited line today):
- The formulas themselves are NOT duplicated. All three sites delegate to the same Stats, ClassBaseline, EnemyScalers, Stats.AddModifiersFrom and AttackTiming.IntervalTicks. What is copy-pasted is a 3-line call order plus a 4-line wrapper. StatMath and UnitStatPreview currently produce bit-identical numbers for the SO layer; I could not construct any input where they differ.
- The known gaps (GrantedEffects passives, vessel perks, items) are deliberate and documented in both preview headers (StatMath.cs:13-14, UnitStatPreview.cs:17-22) as the accepted scope of a preview, not accidental drift.
- The editor copy is the tested one: Assets/_Project/Tests/EditMode/ContentHub/StatMathTests.cs pins StatMath.AttacksPerSecond to AttackTiming.IntervalTicks quantization (AttacksPerSecond_MatchesTickQuantization) and BuildEffective to a direct Stats bake (BuildEffective_MatchesDirectStatsBake). So StatMath.cs:19/31 — the exact lines flagged — is the best-covered of the three.
- The genuinely unguarded copy is the other one: grep over Assets/_Project/Tests finds zero references to UnitStatPreview or IUnitStatPreview. Sharpened, the finding should point at UnitStatPreview.cs:63-76, not StatMath.cs:19.
- Worst case if drift did occur: wrong numbers in an editor balance table or an inventory panel. Display only — never the sim's own values, which come from RuntimeUnitFactory. Nothing gameplay-authoritative reads either preview.

So: real duplication, correctly identified, but a DRY/quality item at P3. Structural fix: hoist AttacksPerSecond into AttackTiming (Guildmaster.Combat) so both callers reuse it — UnitStatPreview cannot call StatMath because Guildmaster.ContentHub.Editor is Editor-only and Combat does not reference it — and expose one shared cascade helper in Combat that RuntimeUnitFactory, UnitStatPreview, StatMath and ContentAuditor all call. ContentAuditor.cs:81-84 is the only part with a wrong output right now.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** The entire game runs inside one un-guarded UniTaskVoid: a single non-cancellation exception kills the run loop permanently with no recovery path

`Assets/_Project/Scripts/Game/GameBootstrap.cs:40` · заявлено P1

MECHANISM CONFIRMED, CONSEQUENCE HALF-WRONG — this is a hardening gap, not an active defect.

What I verified (all read-only):
- `C:\My Projects\Guildmaster-Autobattler\Assets\_Project\Scripts\Game\GameBootstrap.cs:40` — `StartBootAsync().Forget()`, and `StartBootAsync` (lines 43-80) has no try/catch at all. It is the only entry point: grep over all of `Assets` finds exactly one `.Forget()` on this method and exactly one call site of `RunGameAsync` (GameBootstrap.cs:79). Nothing restarts it.
- `Assets\_Project\Scripts\Game\Services\GameFlow.cs:117-145` — the `while (true)` macro loop catches ONLY `OperationCanceledException` (line 140) around `RunActAsync()`. Any other exception exits the loop.
- `Assets\_Project\Scripts\Game\Services\ActRunner.cs:38-134` and the node flows have `finally` blocks but no `catch` for non-OCE. A grep for `catch (` across `Assets\_Project\Scripts` shows no catch-all anywhere in the flow chain (only OCE in GameFlow/CombatLoopService, plus IO/FMOD/settings-local catches). So a throw in any presenter or MessagePipe subscriber invoked from the flow does propagate to the top.
- Recovery really is absent: `GameFlow.RunActAsync`'s `finally` (lines 192-200) nulls `_runCts`, so the ESC-menu hook `RequestReturnToMainMenu()` (line 204) becomes a no-op afterwards. Only `RequestQuit()` still works. So the run/menu progression needs an app restart.

Where the claim overstates:
1. "The entire game runs inside one un-guarded UniTaskVoid" is false. The persist world, UI panels, input service and the combat tick are independent — `CombatLoopService` runs its own loop with its own token (`Assets\_Project\Scripts\Game\Services\CombatLoopService.cs:42-85`). Losing the macro-flow kills menu→act→node progression, not the running game; the player is left standing in the world (phase reset to `None` by the `finally`) and can still quit. Also not silent: `.Forget()` routes the exception to UniTaskScheduler's unobserved handler, which logs it as an exception in the console.
2. No concrete trigger exists in the current code. Every foreseeable data failure is explicitly guarded to return a result instead of throwing: empty/ungenerated map → `Aborted` (ActRunner.cs:41-45), dead end → `Aborted` (55-62), null/unreachable node → `Aborted` (87-92), missing `BattlePresetData`/`TextEventData` → warning + `CompletedStubFlow` (NodeResolver.cs:74-77, 101-105), no elite presets → fallback (161-166). Grep finds zero `throw new` in `Assets\_Project\Scripts\Game`. Reaching the failure therefore requires an unforeseen exception (e.g. an NRE inside a UI presenter or a MessagePipe subscriber, which MessagePipe rethrows to the publisher) — i.e. this finding amplifies other bugs rather than being one.

So: real as a robustness gap (one missing `catch (Exception)` + re-enter-menu path at GameBootstrap.cs:40 / GameFlow.cs:139), P3 rather than P1 — no repro path from current code, and the blast radius is the macro-flow, not the whole game.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Save writes are non-atomic: File.WriteAllText truncates the only run slot in place, so an interrupted write destroys the run

`Assets/_Project/Scripts/Game/Services/JsonFileSaveService.cs:22` · заявлено P1

MECHANISM CONFIRMED, CONSEQUENCE/SEVERITY OVERSTATED.

The mechanism half is correct. Assets/_Project/Scripts/Game/Services/JsonFileSaveService.cs:22 uses File.WriteAllText, which opens with FileMode.Create (truncate-in-place). There is no temp-file-then-File.Replace, no backup, no slot rotation. Assets/_Project/Scripts/Guild/RunStateService.cs:16 pins a single slot (const string SaveKey = "run"), so PathFor (JsonFileSaveService.cs:15-16) always resolves to one run.json in persistentDataPath. The only other writer is DeleteSave (RunStateService.cs:137). So yes: the live run save is truncated before the replacement bytes land, with no second copy anywhere.

Stronger than the claim: the failure does not even require a crash. Save() catches IOException at line 24 AFTER line 22 has already truncated the file, so a disk-full / antivirus-lock / transient I/O error mid-write turns "this save failed" into "the previous save is destroyed" and merely logs Debug.LogError. That is the classic write-temp-then-rename argument, and it holds here.

Where the claim is overstated — the consequence half, and hence the P1 rating:

1. Graceful degradation, not a crash or a wedged install. Load (JsonFileSaveService.cs:30-43) catches System.Exception broadly (line 38), so a truncated/partial run.json makes JsonUtility.FromJson throw and Load return default (null). The only consumer, GameFlow.cs:129, handles exactly that: `if (_runStates.Load() == null) { Debug.LogWarning(...); continue; }` — the loop returns to the main menu. Worst visible symptom is that Exists() (line 45) still sees the corrupt file so HasSave (RunStateService.cs:30) stays true and the "Continue" button bounces the player back to the menu once. Choosing "Start" (GameFlow.cs:133 NewDefaultRun) then overwrites it, so the state is self-clearing, not sticky-broken.

2. Only the in-progress roguelike run is at risk. Nothing else routes through ISaveService — the sole production registration is RootLifetimeScope.cs:115 and the sole consumer is RunStateService; every other ISaveService implementation in the repo is an in-memory test double. No account, meta-progression, or unlock data exists in this slot. And the run is deliberately discarded at act end anyway (GameFlow.cs:188 DeleteSave after the outcome screen).

3. The exposure window is narrow and deliberately throttled. Writes are a few KB of JSON at discrete flow transitions: GameFlow.cs:99/168/181, ActRunner.cs:113/122, RewardPresenter.cs:69, ShopController.cs:82/92/104, EventEffectApplier.cs:28, LoadoutHubViewModel.cs:88/96. DeploymentController.cs:670-688 explicitly avoids per-drop writes via the _rosterDirty flag and only flushes on phase exit (comment at lines 678-680), so drag-heavy deployment is not hammering the file. This is a handful of sub-millisecond writes per node, not a continuous window.

So: a genuine durability gap worth the ~3-line temp+File.Replace fix (and the same non-atomic pattern exists at SettingsService.cs:102 for settings.json, though that file is trivially regenerable), but "destroys the run" leading to P1 overstates it — it costs one single-player run, degrades to a logged warning and a menu bounce, and is recoverable by starting a new run. P2.

---

## Заход 2 — двенадцать заявок

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** The whole sim command pipeline is dead: queue, three commands and the paused-tick hack are reachable only from a NetworkBehaviour that is in no scene

`Assets/_Project/Scripts/Combat/ICombatCommand.cs:10` · заявлено P1

MECHANISM CONFIRMED, CONSEQUENCE OVERSTATED — real as a P3 dead-code/doc-accuracy note, not P1.

Verified true: ICombatCommand (ICombatCommand.cs:10) has exactly 3 implementors (PauseCommand.cs:4, ResumeCommand.cs:4, SpawnUnitCommand.cs:6). CombatSimulation.EnqueueCommand (CombatSimulation.cs:500) has exactly one production caller, NetworkCommandRelay.cs:68. The relay's guid d5c295c98501a84409171559b523d8ab appears in ZERO .unity/.prefab/.asset files repo-wide; there is no AddComponent and no VContainer registration — its only non-doc mention outside its own file is a comment at BattleInputController.cs:54. So the NetworkBehaviour never instantiates and the queue never fills at runtime. SpawnUnitCommand has zero callers anywhere, including the relay (switch at NetworkCommandRelay.cs:59-64 builds only Pause/Resume). Live paths bypass the queue entirely: SetPaused is called directly from BattleBootstrap.cs:90/110/120, DeploymentController.cs:172/266/587/604, WorldStageController.cs:53, BattleInputController.cs:58; spawning goes straight to EnqueueUnitSpawn from EncounterLoader.cs:190/228, SimBench.cs:47, GuildmasterCommands.cs. ISimCommand.cs:5-6 does state the invariant, and it is factually false today.

WHICH HALF IS WRONG — two concrete errors in the claimed failure:

(1) "untestable live logic" is FALSE. Assets/_Project/Tests/EditMode/Combat/CombatSimulationTests.cs:102-129 contains PauseCommand_StopsTick and PauseAndResume_WorkCorrectly, which call EnqueueCommand with PauseCommand/ResumeCommand and assert precisely the tick-counter semantics that line 246 implements. PauseAndResume_WorkCorrectly's Assert.Greater(sim.CurrentTick, 4) would fail without that line. The path is covered by EditMode tests.

(2) "the special paused branch at lines 241-247 ... exists solely to serve ResumeCommand" is HALF FALSE. Lines 241-248 as a whole are the live early-return that makes SetPaused function at all — the deployment phase and the Space-pause both run through this branch every paused tick. Only the single inner line 246 (if (_commandQueue.Count > 0) _currentTick++;) is command-specific, and with a permanently empty queue it costs one List.Count compare per paused tick — not meaningful hot-path weight.

Also refuting the "decoy that will be trusted when MP is built" framing: this is documented parked scaffolding, not a trap. docs/wiki/tech/20-explanation/netcode.md:70-72 explicitly classifies both the relay and the ICombatCommand queue as "Keeper, переработать обвязку" and spells out the required rework; netcode.md:89 and :109 repeat it; the class's own <remarks> (NetworkCommandRelay.cs:14-20) states the ClientRpc-broadcast half will be reworked at MP phase; Scripts/Net/_Parked/ (SimSyncProbe) is an established convention for exactly this; and four prior audits under docs/audits/2026-07-09/ already flagged the same thing.

Nothing malfunctions, no user-visible defect, no crash, no wrong value — so P1 is unjustified. What genuinely survives is small and actionable: ISimCommand.cs:5-6's docstring asserts an invariant that no longer holds and should be reworded to say the seam is reserved for the MP phase, and SpawnUnitCommand.cs is dead even by the relay's own standard (zero callers, zero tests) and is the one file worth deleting outright.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** "Is the battle paused" is stored in two places; StartCombat un-pauses only the sim and leaves Time.timeScale at 0

`Assets/_Project/Scripts/Game/DeploymentController.cs:587` · заявлено P1

Mechanism CONFIRMED, repro partly wrong. The split is genuine: TimeScaleService._paused (Game/Services/TimeScaleService.cs:54, sole writer SetPaused at :192 → Time.timeScale = Effective at :201) is not derived from CombatSimulation._isPaused (Combat/CombatSimulation.cs:455). Repo-wide grep shows `_time.SetPaused` has exactly ONE call site — BattleInputController.OnPauseToggle (Game/Input/BattleInputController.cs:59); DeploymentController (172/266/587 and the unpause at 604), BattleBootstrap (90/110/120), WorldStageController:53, GuildmasterCommands (110/117), Pause/ResumeCommand all touch the sim alone. Nothing else writes Time.timeScale except TimeScaleService.Apply/Dispose. Reset() (:218) intentionally preserves _paused, and the service is Lifetime.Scoped in the persist combat scope (CombatLifetimeScope.cs:67) which GameFlow never tears down ("Сцен этот класс не грузит вовсе… живут всю сессию"), so Dispose()'s timeScale=1 never runs between battles. The frozen-battle consequence is real: CombatLoopService:51 accumulates Time.deltaTime (0 at timeScale 0) so Tick never fires, while deployment stays interactive (ITickable on Update, unscaledTime for clicks/camera) and hides the stale timeScale until "Начать".

WHERE THE CLAIM IS WRONG — reachability, which is why I lowered P1→P2:
1. The rest-beat "К построению" path does NOT work. MenuRouter.ShowContinueAsync:719 pushes the beat as ScreenKind.Modal, UiNavigator:235 forces InputContext.Menu for any modal, and InputService.SetContext:129 leaves _combatMap disabled there — Space cannot pause during the rest beat. Reaching Interlude already-paused from Fighting is impossible (a paused sim never ends the battle), and the only unmodal Interlude window (the 2 s post-win delay, BattleNodeFlow.cs:55) is itself a scaled UniTask.Delay that freezes on pause instead of carrying it forward.
2. dev-R (GuildmasterCommands.Update:133) is a DevTools-only hotkey, not a player path.
3. The one genuine player path the claim missed: Space during Fighting → ESC → "В главное меню" (MenuRouter:434 → GameFlow.RequestReturnToMainMenu cancels _runCts; RunActAsync's finally only does RequestReset + SetPhase(None), never touching timeScale) → new run → battle node → LaunchBattle → deployment → "Начать" → DeploymentController.cs:604 clears _isPaused with Time.timeScale still 0. OnPauseToggle then reads IsPaused==false, so the first Space re-pauses and the second resumes — the double-press symptom is accurate. The stale timeScale=0 also spans the whole main-menu/map stretch in between, so the blast radius is if anything understated, not overstated.

Also note the cited anchor: line 587 is `_sim.SetPaused(true)` inside RebuildPreview; StartCombat's `_sim.SetPaused(false)` is line 604 of Assets/_Project/Scripts/Game/DeploymentController.cs.

P2 rather than P1: the defect requires a specific two-step accident (pause, then quit to main menu mid-battle) rather than ordinary play, the player can recover with two Space presses, and there is no data loss or crash — but the game does read as hard-locked to a player who does not know that.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** BodyRadiusPerSize has three owners and they already disagree — the SO's own C# default is 0.575 while code, asset and guard test all say 0.3

`Assets/_Project/Scripts/Data/Definitions/SimTuningConfig.cs:17` · заявлено P1

The FACTS check out; the CONSEQUENCES are inflated. Verified verbatim:

- `Assets/_Project/Scripts/Core/Simulation/SimTuning.cs:79` — `bodyRadiusPerSize: 0.3f`.
- `Assets/_Project/Scripts/Data/Definitions/SimTuningConfig.cs:16-17` — tooltip "Size 1.0 → 0.575 (диаметр 1.15)" and `private float _bodyRadiusPerSize = 0.575f;`.
- `Assets/_Project/ScriptableObjects/Configs/SimTuningConfig.asset:15` — `_bodyRadiusPerSize: 0.3`.
- `Assets/_Project/Tests/EditMode/Content/ConfigValidationTests.cs:23-28` — loads the ASSET, calls `ToSnapshot()`, compares against `SimTuning.Default`. It never observes the field initialiser, so the 0.575 is indeed invisible to it. Every other field's initialiser matches Default; `_bodyRadiusPerSize` is the lone straggler, so this is stale-value drift, not a deliberate design.
- `Assets/_Project/Scripts/Presentation/UnitView.cs:1084` — `float cr = size * SimTuning.Default.BodyRadiusPerSize;` — the gizmo really does read the code seed, not `_sim.Tuning` (contrast `DeploymentController.cs:668`, which correctly does `CombatPositioning.BodyRadius(u, _sim.Tuning)`).

So the three declarations exist and genuinely disagree; none is derived from another. That half stands.

What is wrong is the claimed failure. The runtime does NOT resolve the config by `AssetDatabase.FindAssets` — that is test-only code (`ConfigValidationTests.LoadSingle`). The game binds an explicit serialized reference: `Game/CombatLifetimeScope.cs:35` `[SerializeField] private SimTuningConfig _simTuningConfig;` baked at `:146` via `.WithParameter("tuning", (SimTuning?)_simTuningConfig.ToSnapshot())`. Creating a second asset from `Guildmaster/Config/Sim Tuning Config` therefore changes nothing in play — the new asset is inert until a human drags it onto CombatLifetimeScope (and onto `GuildmasterCommands.cs:43`). There is no path where body radius silently doubles, no change to separation, `CombatPositioning.BodyRadius`, or melee reach. And if someone did re-create and wire the single config, `SimTuningConfig_MatchesCodeDefaults` fails immediately with a precise `Expected 0.3f, was 0.575f` — the "Ожидается ровно один ассет" message only appears in the two-assets case, which is itself harmless to gameplay. So the guard is weaker than the docstring at `SimTuningConfig.cs:10` claims, but it is not absent for the case that actually matters.

Residual real cost, all documentation/tooling grade: (a) a designer reading the inspector tooltip sees a diameter of 1.15 while the sim plays 0.6 — a 92% lie in the label, correct as stated; (b) the class docstring's "при рассинхроне падает тест-страховка" over-promises; (c) UnitView's debug disc is pinned to `SimTuning.Default` and will misreport the moment the asset is tuned away from 0.3 — note it is already wrong whenever `gm_sep_radius` live-tweaks `_simulation.Separation.BodyRadiusPerSize` (`DevTools/GuildmasterCommands.cs:211`). Three cheap one-line fixes, zero current gameplay impact, drift on the played value is test-covered. P3, not P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** A completed one-shot migration still sits on an Alebardium menu item and would destroy current balance if clicked

`Assets/_Project/Scripts/Data/Editor/Migrations/Phase4Package3StatsBaseMigration.cs:33` · заявлено P1

Mechanism confirmed, consequence badly overstated on both quantitative claims. HOLDS: the [MenuItem] at Phase4Package3StatsBaseMigration.cs:33 is live, Run() is public, there is no confirmation dialog and no undo (ApplyModifiedPropertiesWithoutUndo, line 60), and neither reflected field was renamed (StatsConfig._defaults at StatsConfig.cs:25, UnitData._stats at UnitData.cs:81), so it would execute rather than throw.

BREAKS #1 - the relic-rewrite half is a near-no-op, not "every relic". RewriteRelicsAsDiffs only rewrites modifiers where m.Op == ModifierOp.Flat (line 80); all others hit the else at line 89 and are copied verbatim. I dumped every _stats block in all 16 UnitData assets (ScriptableObjects/Relics/ + Enemies/): every modifier is Op: 3 (Override) except exactly one project-wide - Relics/Treant.asset "Stat: 0, Op: 0, Value: 600". So the migration would change one number, 600 -> 480. Treant is _combatClass: 1 (Tank, HpMult 1.5 -> 3000 base), so 3600 -> 3480 HP, ~3%. The claimed failure "a relic whose Flat MaxHP is already a small diff becomes a large negative one" cannot happen - there are no small Flat diffs. This is structural: ModifierOp.Override's docstring declares itself the primary way to author unit base stats on SO, so authoring moved off Flat deltas.

BREAKS #2 - the headline MaxHP write is inert, and the claim missed the write that isn't. Current StatsConfig.asset is MaxHP 1200 / AutoAttackDamage 120 / AttackSpeed 1 / AttackRange 1 / MoveSpeed 3 / PhysArmor 4; the migration writes 120 / 12 / 1 / 1.5 / 3 / 4, so AttackSpeed, MoveSpeed and PhysArmor are unchanged. MaxHP 1200->120 never reaches a unit: ClassBaseline.Apply unconditionally adds an Override MaxHP for every UnitData and Override replaces the baseTerm, so the StatsConfig MaxHP default is a dead fallback. The actually-live damage is AutoAttackDamage 120->12 (the class layer sets only HP and MoveSpeed), affecting the only two units that don't author stat 7 themselves - Cryomancer and WhirlMonk - a silent 10x damage nerf; plus AttackRange 1->1.5 on the goblins/BaseRelic/TrainingDummy.

Framing note: the file AND its menu entry are mandated convention, not oversight - docs/wiki/tech/40-planning/phase-4-content.md:26 item 8 requires one-shot migrations to stay in repo under .../Migrations/ with a migrations menu path, marked executed+date, which is what the line 15 docstring does.

Net: real footgun (deliberate 3-level navigation, confusable with the adjacent "Phase 4 - Package 3 (AI presets)" entry, no dry run), but blast radius is 3 numbers in one config asset (one inert) plus 1 number on Treant, all git-tracked YAML recoverable via git checkout. Worth adding a confirmation dialog; not a P1 "would destroy current balance".

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** HP/shield bar colour has four owners and the enemy colour already disagrees — the fallback paints the exact red the palette documents as rejected

`Assets/_Project/Scripts/Presentation/CombatPresenter.cs:431` · заявлено P1

MECHANISM: REAL, and I confirmed the drift from git history. CONSEQUENCE: OVERSTATED — the drifted branch is unreachable in the project as it stands, so nothing paints plain red today.

What checks out (the "truth" half):
- `Assets/_Project/Scripts/Presentation/CombatPresenter.cs:435-437` `DefaultHealthColor(bool)` returns enemy `new Color(0.90f, 0.25f, 0.25f)`, while `Assets/_Project/Scripts/Presentation/Design/CombatColorPalette.cs:26` and `Assets/_Project/ScriptableObjects/Configs/CombatColorPalette.asset` both carry vermilion `(1, 0.4, 0.13)`. The values genuinely disagree.
- The disagreement is provable drift, not a design choice. At `git show 0c6d3351` the SO default enemy colour WAS `(0.90f, 0.25f, 0.25f)` — identical to the fallback. Commit `8cd89300` ("Enemy HP color -> vermilion for readability") moved the SO and the asset and left `DefaultHealthColor` behind.
- Consequently the fallback's own docstring at `CombatPresenter.cs:434` — "совпадает с дефолтами SO" ("matches the SO defaults") — is now factually false for the enemy colour. That is the concrete defect: a stale constant plus a comment asserting a равенство that no longer holds.

What refutes the claimed severity:
- The fallback is unreachable. `CombatLifetimeScope.cs:166` uses `builder.RegisterComponentInHierarchy<CombatPresenter>()`, so the presenter must be a scene object. Grepping the presenter's script guid `5b57b96080af5d2429d001e99d7f6e7a` across all of `Assets` yields exactly ONE component instance, on `[Presenter]` in `Assets/_Project/Scenes/CombatSystemsScene.unity:1148-1159`, and `_colorPalette` there IS wired to guid `422d364d4b20d314e91116d7591f294b` (the palette asset). No `AddComponent<CombatPresenter>`, no presenter prefab, no test constructs one. So `_colorPalette != null` always holds at `CombatPresenter.cs:258` and `:263`, and the plain-red branch never executes. The claim's "on any scene where the presenter's palette reference is unset" describes a scene that does not exist. The readability bug is therefore NOT reintroduced in the shipped build — the claim's central consequence is wrong.
- The "four owners" count is inflated. Owner 2 (the `[SerializeField]` initialisers in `CombatColorPalette.cs:22/26/32`) is not an independent runtime owner: for an existing asset Unity reads the serialized YAML, never the initialiser. It is consulted only when a designer creates a NEW palette asset, and its three values match the asset exactly today. Owner 4 (`HealthBarView.cs:43-44` / `HealthBar.prefab:264-265`) is a different component's last-resort default, guarded by `_hasHpColor`/`_hasShieldColor` at `HealthBarView.cs:96-97`; `SetMainColor`/`SetShieldColor` are called from nowhere but `UnitView.cs:201/207`, which is called from nowhere but the presenter. So it too only surfaces on the same dead null-palette path, and its shield value `(0.62, 0.86, 1)` is currently identical to the palette's. Realistically there are two runtime-relevant owners of the HP colour, and only one of them ever runs.
- The shield sub-claim ("the prefab value wins") is technically a correct reading of `CombatPresenter.cs:263-264` but produces no disagreement: same dead branch, and the two values are byte-identical.

Net: a stale duplicated constant on a currently-dead branch, with a comment that now lies about it. Worth a one-line fix (delete the fallback and require the palette, or update the constant), but it cannot change a pixel in the game as the project stands — that is P3 (code truth / maintenance trap), not P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** StaggeredBrainSpikeTests is 259 lines of self-declared throwaway spike, verbatim superseded by BrainTests

`Assets/_Project/Tests/EditMode/Combat/StaggeredBrainSpikeTests.cs:16` · заявлено P1

The EXISTENCE half is real; the CONSEQUENCE half is wrong on all three counts.

Real: StaggeredBrainSpikeTests.cs is 259 lines, self-declares throwaway (line 16), and grep confirms ISpikeBrain/NearestEnemySpikeBrain/SpikeBrainSystem have no consumer outside that file. BrainTests.cs mirrors 3 of its 4 tests on production types and says so itself (BrainTests.cs:15). Minor overstatement: it is not "verbatim" superseded — StaggeredBrain_UnitDecidesOnItsPhaseOnly_Every3rdTick (spike:41) has no BrainTests counterpart because prod BrainSystem has no DecisionLog instrumentation; but that test asserts about the spike's own copy, so deleting it loses no production coverage.

REFUTED #1 — "already drifted off the real rule (Id vs BrainPhase, no explicit dirty flag)": there is no drift. BrainPhase IS `id % SimConstants.AiTickInterval` by construction — RuntimeUnitFactory.cs:103, the field docstring RuntimeUnit.cs:48, and BrainTests.SetBrains (BrainTests.cs:179) all use that identical expression. And `grep "BrainDirty = true"` over Assets/ returns zero matches: the flag has no writer anywhere in the codebase (BrainSystem.cs:31 calls it a "задел"/placeholder, line 36 only clears it). So prod's gate `u.BrainDirty \|\| (target != null && target.IsDead)` reduces today to exactly the spike's `dirty` expression. The two cadence rules are behaviorally identical, not divergent.

REFUTED #2 — "false pass": SpikeBrainSystem is a file-private test class touching zero production code. Editing it produces no game change and cannot mask a regression, because BrainTests still exercises the real BrainSystem. The hazard is a maintainer's wasted time, not a green suite covering a broken cadence.

REFUTED #3 — "the desync probe's hash has three owners": the probe has exactly one owner — SimSyncProbe.cs:42,52 call CombatSimulation.ComputeChecksum (CombatSimulation.cs:520). Neither test copy feeds it, and both deliberately omit `_rng.Snapshot()` and the AttackCooldownTicks/WindupRemaining/RecoveryRemaining terms — they are reduced local determinism helpers, structurally incapable of disagreeing with the wire hash.

Additionally, the retention is a recorded decision, not an oversight: docs/wiki/tech/40-planning/phase-3-ai-relics.md:157 lists "StaggeredBrainSpikeTests — спайк S1 (throwaway-прото), оставлен как регрессия staggered-детерминизма."

Net: a genuine 259-line dead-test cleanup with one duplicated helper, no runtime effect, no CI risk, no correctness exposure. P3, not P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** IBattleSession.SetPending has zero producers, so BattleBootstrap's "legacy-compat" launch branch can never run

`Assets/_Project/Scripts/Game/Flow/BattleBootstrap.cs:58` · заявлено P1

Deadness confirmed, severity overstated (P1 -> P3). CONFIRMED HALF: repo-wide grep for SetPending (no glob filter, whole project) returns exactly six hits — the interface decl (BattleSession.cs:20), the impl (:110), two comment mentions (:26, BattleBootstrap.cs:58), and the test-fake stub (BattleFlowTests.cs:160). Zero producers. _hasPending (BattleSession.cs:101) is written only by SetPending (set) and TryConsumePending (clear), so it is permanently false and TryConsumePending always returns had=false, making BattleBootstrap.cs:59-60 unreachable. IBattleSession has exactly two implementers — BattleSession (registered RootLifetimeScope.cs:135) and the EditMode fake — so no external implementer can revive it. Live path is BattleFlow.cs:48 _session.RequestLaunch(_preset), which arms the outcome TCS itself; BattleSession.cs:26 documents that the Bind/RequestLaunch trio replaced SetPending+LoadBattleAsync. OVERSTATED HALF: (1) No failure exists — nothing produces a wrong result, crashes, hangs, or misbehaves; this is dead code / maintenance tax, not a correctness or safety defect, so it does not meet a P1 bar. (2) The "looks like safety, reader wastes debugging time" argument is undercut by the code itself: line 58 explicitly labels the branch "Legacy-совместимость ... старый путь до persist" and BattleSession.cs:26 states the path was replaced — the branch announces it is retired. (3) The ArmOutcome claim is factually wrong: because SetPending is never invoked, its ArmOutcome() never runs, so it has no side effect (apparent or real) on outcome-waiting; the live flow arms via RequestLaunch (:147) and RequestRestart (:166). (4) The "load-bearing tax" is two one-line stubs in a single test double — real but trivial. Net: two dead interface members plus a 2-line unreachable, correctly-labeled branch, worth deleting in the next flow cleanup. P3.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** Only 2 of 9 config assets have an asset-vs-code guard, and two of the unguarded ones already disagree with the defaults every test uses

`Assets/_Project/Tests/EditMode/Content/ConfigValidationTests.cs:67` · заявлено P1

Mechanism half is partly real, consequence half is mostly false. VERIFIED: GameConfig.asset:22 `_relicCapacityBase: 12` vs GameConfig.cs:34 `= 8` (git 41b6ff7a deliberately changed the asset 8->12 in PR #17), and StatsConfig.asset:17 `_attackSpeedMax: 4` vs StatsConfig.cs:21 `= 2.5f`. REFUTED, four ways: (1) MapStyle is NOT drifted -- MapStyle.asset._layout is 6.5/4.2/0/0/2.2/10, byte-identical to MapLayout.Default (MapLayout.cs:45-53), so all nine MapLayoutTests test the played values. (2) ClassBalanceConfig is NOT drifted -- asset has _baseHp: 2000, _baseMoveSpeed: 3 and the exact multiplier grid 1.0/1.5-0.85/0.75-1.1/0.65-0.75x3, identical to code initializers and to ClassBaselineTests.MakeConfig(); the fields MakeConfig() omits are the ones that already agree. (3) The "economy block lives only in C# with no inspector row" claim misreads Unity serialization: keys absent from YAML are not zeroed -- Unity runs field initializers when constructing the managed object and only overlays keys present in the stream, so _startGold=100, _priceCommon/Cursed/Divine=50/100/150, _priceSpread=0.2, _sellPercent=0.25 at runtime, exactly what RelicPricerTests asserts; they are also fully visible in the inspector, just not yet re-saved to disk. (4) The attackSpeedMax divergence is inert -- AttackSpeedMax has zero runtime consumers; the only reads are ContentValidationTests.cs:133 (asset-side min<max) and Balance/Editor/ContentAuditor.cs:44, an editor-only audit tool with its own 2.5f fallback. Combat reads StatType.AttackSpeed raw (AttackTiming.cs:88, AutoAttackSystem.cs:94) with no config clamp. Also, RunStateSaveTests.cs:79-100 falsifies nothing: it asserts against config.RelicCapacityBase and loops `while (run.RelicCapacity < config.RelicCapacityMax)`, so it is value-agnostic and passes identically at 12 -- it proves the fill/overflow/upgrade mechanism, not an "8->16 ladder"; "a play-reported capacity bug would not reproduce in EditMode" is speculation with no concrete path. Finally, "only 2 of 9 guarded" mistakes a pattern for a gap: SimTuningConfig and ActConfig have match-code-defaults guards because a code-side POCO is the sim canon (SimTuning.Default, new MapGenConfig().Validated()); GameConfig/StatsConfig have no canonical POCO -- the asset is the sole runtime source via DI (RootLifetimeScope.cs:50, RunStateService.cs:43,172), so a GameConfig_MatchesCodeDefaults test would forbid the inspector tuning the SO exists for. Residual genuine gap, and the only reason this is not not-a-bug: no EditMode test pins the PLAYED GameConfig numbers (relic capacity 12->16), so that one live value is uncovered. That is a P3 test-fidelity nit, not a P1 split-source-of-truth defect.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** StatsConfig.asset's MaxHP/MoveSpeed defaults are dead and contradict ClassBalanceConfig, and no test ever reads the shipped asset

`Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset:18` · заявлено P1

Mechanism CONFIRMED, consequence and the test half OVERSTATED.

Confirmed: StatsConfig.asset carries MaxHP 1200 / MoveSpeed 3; ClassBaseline.Apply (Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs:25-29) unconditionally adds Override MaxHP+MoveSpeed for any data!=null && config!=null; Stats.RebuildCache (Stats.cs:172-174) makes Override the baseTerm irrespective of group order; both configs are wired in the live scene (CombatSystemsScene.unity:453-454); every runtime Create passes non-null data (EncounterLoader.cs:190,228; GuildmasterCommands.cs). Asset proof: the four goblins in ScriptableObjects/Enemies carry no MaxHP modifier at all and get 2000/3000/1500/1300 from the class layer, never 1200. So editing MaxHP in StatsConfig truly has no gameplay effect.

Wrong half 1 - the "two owners that disagree" framing. This is an explicitly documented cascade with StatsConfig as the BOTTOM fallback layer, not a split owner. ModifierOp.Override's docstring says verbatim that StatsConfig defaults remain a fallback for unset stats plus a GD reminder, and ClassBalanceConfig.cs:13-16 spells out StatsConfig (global fallback) -> ClassBalanceConfig -> persona -> Vessel. By the claim's logic ClassBalanceConfig's 2000 is equally "contradicted" by BaseRelic.asset and TrainingDummy.asset, which both re-author Stat 0 / Op 3 / Value 1200 - that shadowing is the intended back-compat path. A shadowed lower cascade layer is the design, not a defect.

Wrong half 2 - "no test ever reads the shipped asset" is factually false. ContentValidationTests.cs:128-136 loads every shipped StatsConfig via AssetDatabase.FindAssets("t:StatsConfig") and asserts on it. Only the narrower "no test pins the shipped defaults" holds, and ConfigValidationTests.cs:14-16 documents that value-snapshot tests were removed deliberately because tuning moves intentionally.

Also, StatsConfig.asset is far from dead: only 2 of its 6 default rows are shadowed. AutoAttackDamage 120, AttackSpeed 1, AttackRange 1, PhysArmor 4, ArmorConstantK and the attack-speed clamps are all live.

What survives is a data-hygiene nit with zero runtime consequence: two stale rows left over from Phase4Package3StatsBaseMigration (which wrote MaxHP 120, later hand-tuned to 1200) that the 2026-07-24 class cascade made unreachable, plus Content Hub's editor-only "база" column (ContentHubWindow.Browser.cs:308) printing 1200 next to an effective 2000. Worth a cleanup commit, not P1.

Unrelated but stronger finding spotted while verifying: CoreScene.unity:294-296 has _statsConfig: {fileID: 0} AND _classBalanceConfig: {fileID: 0} on RootLifetimeScope, so IUnitStatPreview (loadout cards, tooltips - RootLifetimeScope.cs:84-85) is constructed with both configs null and falls back to natural defaults (MaxHP 0) - a genuine UI-vs-sim divergence.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** StatsConfig attack-speed clamp has three owners, zero sim readers, and a test that makes the dead knob look alive

`Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:128` · заявлено P1

The DEAD-KNOB half is real and verified; the SPLIT-SOURCE-OF-TRUTH half is wrong, and the consequences are inert on today's content, so P1 is far too high.

CONFIRMED (mechanism):
1. The complete reader set of `StatType.AttackSpeed` is: `AutoAttackSystem.cs:94`, `AttackTiming.cs:88`, `ProfileBrain.cs:128`, `StatMath.cs:43`, `UnitStatPreview.cs:55`, `ContentHubWindow.Balance.cs:40`, `ContentAuditor.cs:88`. Only `ContentAuditor.cs:89` (`Mathf.Clamp(atkSpeedRaw, asMin, asMax)`) clamps, and that assembly is `Guildmaster.Balance.Editor` — editor-only. The sim path is raw: `AutoAttackSystem.EnterWindup` reads `unit.Stats.Get(StatType.AttackSpeed)` and hands it straight to `AttackTiming.IntervalTicks(float)`.
2. `Stats.cs` (`Assets/_Project/Scripts/Combat/Stats/Stats.cs`) genuinely has no clamp stage: `RebuildCache` (l.169-176) and `Compose` (l.186-187) are `(base+flat)*(1+percentAdd)*multAccum` and nothing else. So the docstring at `Stats.cs:30` ("после всех модификаторов и клампов") and `StatType.cs:26` ("AttackSpeed = 8, // [Ф1] атак/сек, клампится из StatsConfig") both describe a stage that does not exist — and `StatType.cs`'s own remark says only NON-`[Ф1]` stats have clamps deferred, so `[Ф1] AttackSpeed` is not covered by the "later phase" escape hatch. That doc/code drift is real.
3. `ContentValidationTests.cs:128-136` really only asserts ordering (`0.1 < 4`), so it does read as validation of a clamp that nothing enforces.

REFUTED (the split-owner half):
4. "Three owners ... can disagree" is not true — there is exactly ONE live owner. `Glob **/StatsConfig*.asset` returns a single asset (`Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset`). `StatsConfig.cs:20-21` (`= 0.1f`/`= 2.5f`) are C# field initializers: for the existing asset they are dead (serialized values win), and they can only surface on a newly created `StatsConfig` — not a concurrent value.
5. "An audit run without the config silently switches ceiling" is not reachable in any meaningful way. `BalanceAssets.LoadStatsConfig` (l.13-19) returns null only when `FindAssets("t:StatsConfig")` is empty. In that case `ContentAuditor.BuildRow` also does `new Stats(null)` → every stat collapses to `StatsConfig.NaturalDefault`, so MaxHP=0, AutoAtk=0, RawDPS=0 for every row and `FlagOutliers` stamps `MaxHP<=0, RawDPS<=0` on all of them. Nobody would read a subtly-wrong ceiling off that report; it is visibly broken for much louder reasons. The clamp fallback is not "silent".
6. The 2.5-vs-4 gap changes no number today. Every authored AttackSpeed base is 0.6–1.3 (`GoblinWarrior` 0.6 … `Assassin`/`GoblinCutthroat` 1.3), inside both `[0.1, 2.5]` and `[0.1, 4]`. The auditor's clamp never binds; the two ceilings are numerically indistinguishable on current content.
7. "Applied nowhere" overstates the runtime risk: `AttackTiming.IntervalTicks` (l.28-33) explicitly handles both edges — `attackSpeed <= 0f` → `int.MaxValue`, and `max(1, ...)` gives an implicit hard ceiling of `TickRate` aps. So the missing StatsConfig clamp cannot produce a divide-by-zero, a zero interval, or a desync; it only means the DESIGN ceiling is unenforced. Reaching even 2.5 aps requires a buff (`PyreRush` is `Op: 1` PercentAdd +1.0 → 1.3 base becomes 2.6; `BlazingBladesRamp` +5%/stack), and exceeding 4 needs ~+208%, which no current content reaches.

Net: real cleanup item — either wire the clamp where the wiki says it lives, or delete `_attackSpeedMin/_attackSpeedMax` + the ordering test and fix the two lying docstrings. No failure scenario exists in the code as it stands, so this is P3 hygiene/doc-truth, not a P1.

---

### ПОДТВЕРЖДЕНО → `P3` (уверенность high)

**Заявка:** Damage-affinity tests mirror AffinityTable.VulnerableMult, so the +30% vulnerability magnitude is pinned nowhere

`Assets/_Project/Tests/EditMode/Combat/DamagePipelineTests.cs:284` · заявлено P1

Mechanism confirmed, consequence overstated — real but P3, not P1.

CONFIRMED: DamagePipelineTests.cs:284/298/314 all express the expectation as `100f * AffinityTable.VulnerableMult`, mirroring AffinityTable.cs:15 (`public const float VulnerableMult = 1.3f;`). A repo-wide grep for `VulnerableMult\|1\.3f` shows the literal 1.3 exists nowhere in tests (other hits are DeploymentController.cs:34 PickRadiusScale and FMOD vendor code), and no other test pins the magnitude — the only other affinity-touching tests (DamageTypeResolverTests.cs, PoisonBurnThornsSliceTests.cs) assert enum plumbing only. Tracing the degenerate edit: with VulnerableMult = 1.0f, Light_VulnerableAgainstUndeadAndDemon computes expected = 100 while its neutral control at line 287 also asserts 100f, so it stays green; Dark_VulnerableAgainstLiving likewise. The vulnerable-vs-neutral distinction and the magnitude are genuinely unpinned. The complaint is a tautological assertion, and the one-line fix is `float expected = 130f;`.

OVERSTATED — three parts:
1. "The entire school-vs-creature-type mechanic silently evaporates" holds only for the exact Mult == 1.0 value. The pairing table itself stays pinned for any other value: change CreatureType.Undead to Beast in AffinityTable.Multiplier and line 285 or 287 fails. Half the table is already pinned with hard literals — Poison_ImmuneAgainstUndeadAndConstruct asserts 0f (lines 259-260) and Poison_NeutralAgainstLivingAndBeast asserts 100f (lines 271-272).
2. "The third test's whole point degenerates to 100 == 100" — its affinity half does, but the residual assertion still pins that DamageSchool.True bypasses armor: DamagePipeline.cs:32-48 (the armor block) is inside the non-True branch and affinity applies after at line 53. The test weakens, it does not become vacuous.
3. Severity. No defect exists in shipped behavior: the const is 1.3f, matching its own docstring ("+30%") and the GDD, and DamagePipeline.cs:53 applies it correctly. Nothing fails for any input today — the failure requires a future erroneous edit this suite would fail to catch. That is a regression-coverage gap in test-only code with zero runtime impact, which is P3, not P1.

Also note the claim's "split source of truth" framing is the wrong lens and would refute itself: the test is derived from the production constant by direct reference, so the two owners can never disagree. The actual (smaller) issue is the missing independent pin.

---

### ПОДТВЕРЖДЕНО → `P2` (уверенность high)

**Заявка:** Commit 0410520c deleted both tests guarding "the arena returns to the world when a node ends" and its new owner has no tests at all

`Assets/_Project/Tests/EditMode/Guild/BattleNodeFlowTests.cs:76` · заявлено P1

Verified in the working tree. Commit 0410520c does delete both Win_ResetsArena_OnlyAfterReward and Defeat_ResetsArena_Too (plus the ResetSpyReward/CountingSession doubles) from Assets/_Project/Tests/EditMode/Guild/BattleNodeFlowTests.cs, and the mechanic moved to Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs:49-57, which is registered for real play (RootLifetimeScope.cs:196) and has no test of its own — the only IRunBeatStage in the test tree is SpyBeat (ActRunnerTests.cs:221-227), two bare counters. Both halves of the claim hold on inspection. (1) Ordering: the replacement test RunAct_RestBeat_BetweenNodes_NotOnActEntry asserts only RestBeats == NodeEntries - 1 and RestBeats > 0; I traced the specific regression edit — moving _beat?.EnterRestBeat from ActRunner.cs:69 to just before `await flow.Run(...)` at line 102, still under `if (!actEntry)`, leaves the counts at N-1 vs N so the test still passes, while the reward would again be chosen over a cleared arena, the exact regression BattleNodeFlow.cs:18-20 records and 7d0b2f6e fixed. (2) Defeat: ActRunner.cs:110-116 returns Defeated with no _beat call, so the reset rests solely on GameFlow.RunActAsync's finally (GameFlow.cs:193-199), and `grep -rln GameFlow Assets/_Project/Tests` matches only a comment in ActRunnerTests.cs:101 — there are zero GameFlow tests. RequestReset has exactly three call sites (RunBeatStage.cs:51, GameFlow.cs:107, GameFlow.cs:196), confirming no other guard exists. One evidence item is wrong but immaterial: FakeSession.ResetCount at BattleFlowTests.cs:176-179 is NOT a fossil of coverage removed here — `git log -S"ResetCount"` on that file returns only 137065d2, so the occurrence count never changed and the field was never asserted from the day it was added; it is unrelated debris. Severity is overstated and I lower it to P2: there is no live defect. Current behaviour is correct by construction, and the defeat-path reset actually moved into a `finally` that fires on every exit path (boss, defeat, cancellation) — structurally stronger than the inline call the deleted test observed, merely untested. P1 implies something shipping-blocking; this is a latent-future coverage hole on a named invariant that did regress once before, which is P2-shaped.

---

## Непроверенное — читать со скидкой

Кэп верификации сработал: заявок P0/P1 оказалось больше, чем я разрешила проверить. Ниже — то, что **никто не оспаривал**. Учитывая, что из 25 проверенных ни одна не удержала заявленный уровень выше P1, ожидать здесь пожаров не стоит.

- P0 CoreScene leaves StatsConfig and ClassBalanceConfig unassigned, so the inventory panel shows Здоровье 0 / Скорость 0 for every hero kit (Assets/_Project/Scenes/CoreScene.unity:295)
- P1 The whole Camp-node feature is switched off by one {fileID: 0} in CoreScene, and every act map has two guaranteed Camp floors (Assets/_Project/Scenes/CoreScene.unity:160)
- P1 ActConfig.asset is referenced by nothing, so the act map is generated from C# defaults while a guard test pins the orphaned file (Assets/_Project/ScriptableObjects/Configs/ActConfig.asset:13)
- P1 Localization has two String Tables but only one is reachable: 23 authored keys in the "UI" table are resolved against "Content" and always miss (Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48)
- P1 51 ui.* keys referenced by code exist in neither String Table, so whole screens ship as hardcoded Russian and ignore the locale (Assets/_Project/Scripts/UI/ShopScreenView.cs:42)
- P1 relic.base still hard-Overrides MaxHP to 1200, so every run's four starting vessels sit 40% under the Bruiser balance anchor (Assets/_Project/ScriptableObjects/Relics/BaseRelic.asset:34)
- P1 The reward rarity ramp and the shop price tiers are inert: every relic asset is Common on both axes (Assets/_Project/Scripts/Game/Flow/RewardService.cs:61)
- P1 No BattlePreset asset is marked elite, so Elite nodes fall back to the whole preset pool — including the training-dummy dev preset (Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159)
- P1 The game boots into the EN locale by default, where 132 of 212 content keys are blank and 19 more are literally «—» (Assets/_Project/Localization/LocalizationSettings.asset:44)
- P1 ui.* strings are split across two String Tables while every screen resolves against Content — 21 of 41 UI-table rows are unreachable, and 5 filled Content rows are read by nobody (Assets/_Project/Scripts/Game/Services/LocalizationService.cs:18)
- P1 51 of the 100 ui.* keys the code asks for exist in no String Table — the Russian text of half the interface lives only as C# fallback literals (Assets/_Project/Scripts/UI/MenuRouter.cs:441)
- P0 Pause has two owners; the run loop resets only one, so a Space-pause freezes every later battle in the session (Assets/_Project/Scripts/Game/Services/TimeScaleService.cs:192)
- P1 DeploymentController._deploying is a second owner of BattlePhase; three other classes write the phase, so quitting mid-deployment strands the controller (Assets/_Project/Scripts/Game/DeploymentController.cs:188)
- P1 Four flow presenters document a "no UI listener → resolve immediately" fallback that does not exist; three have no CancellationToken, so a missing subscriber hangs the game at boot forever (Assets/_Project/Scripts/Game/Flow/MainMenuPresenter.cs:27)
- P1 The SetPending/TryConsumePending battle-queue seam is dead, and BattleBootstrap's "legacy" launch branch it feeds is unreachable (Assets/_Project/Scripts/Game/Flow/BattleSession.cs:110)
- P1 ActRunner's `IRunBeatStage beat = null` default and its two null-guards are a dead fallback on a DI-registered type (Assets/_Project/Scripts/Game/Services/ActRunner.cs:30)
- P0 Root scope ships with StatsConfig and ClassBalanceConfig unwired — the loadout stat panel prints Здоровье 0 / Скорость 0, and the editor preview stand hides it (Assets/_Project/Scenes/CoreScene.unity:295)
- P1 CampScreen.uxml is referenced by nothing — every camp node on the act map silently completes without showing a screen (Assets/_Project/Scenes/CoreScene.unity:160)
- P1 ActConfig.asset is an orphan: the act layout has two owners and the code copy is the one that plays, so editing the designer-facing SO changes nothing (Assets/_Project/Scenes/CoreScene.unity:294)
- P1 A shipped build picks its language from the OS with no fallback and no in-game switch; the UI table's English column is nine literal dashes (Assets/_Project/Localization/LocalizationSettings.asset:41)
- P1 ModalPanel is a dead UITK control that claims to own the frame every overlay repeats (Assets/_Project/Scripts/UI/Components/ModalPanel.cs:11)
- P1 Elite and Boss nodes have no content at all — the whole difficulty axis of the act is authored but empty (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159)
- P1 The act's live battle pool contains dev slices — a Battle/Elite/Boss node can roll three training dummies (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/ScriptableObjects/BattlePresets/PresetBaseKit.asset:16)
- P1 The whole item/banner content axis is dead: three authored items, full combat plumbing, and no code path that can ever grant one (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunStateService.cs:263)
- P1 The Vessel ("pilot") layer is dead end to end — empty folder, VesselId hardcoded to empty, perk path unreachable (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Guild/RunStateService.cs:68)
- P1 Two registries own "content type → where it lives", and the Content Hub create menu offers exactly the dead types while hiding the live ones (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Editor/ContentPaths.cs:17)
- P0 The loadout stat panel is wired to a null stat cascade: CoreScene leaves StatsConfig and ClassBalanceConfig unassigned, so every relic shows HP 0 / Speed 0 (Assets/_Project/Scenes/CoreScene.unity:295)
- P1 Every Camp node in an act is a silent no-op: CoreScene never assigns CampScreen.uxml, so the flow's null-guard immediately calls OnLeave (Assets/_Project/Scenes/CoreScene.unity:160)
- P1 UI localization is a facade: screens query the Content table while 41 ui.* keys live in the UI table, and 52 of 60 keys do not exist at all — the real owner of every string is a hardcoded Russian literal (Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48)
- P1 The node-farewell card renders completely blank: its two keys exist only in the UI table and this call site has no fallback at all (Assets/_Project/Scripts/UI/MenuRouter.cs:691)
- P1 ActConfig.asset is orphaned — the act map the game generates comes from C# defaults, so designer edits to the act config are silently ignored (Assets/_Project/Scenes/CoreScene.unity:294)
- P1 StatMathTests pins the cascade-aware bake to the cascade-FREE formula — the test blesses exactly the bug R1-48/R1-72 reports (Assets/_Project/Tests/EditMode/ContentHub/StatMathTests.cs:77)
- P1 RequiredLocalizationKeys_Exist checks only that the key exists, not that RU has text — 7 required keys ship blank right now (Assets/_Project/Tests/EditMode/Content/ContentValidationTests.cs:97)
- P1 GameConfig.asset carries only 7 of its 20 serialized fields, disagrees with the code default the whole test tier uses, and nothing pins it (Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20)
- P1 Every unit's AI is stored twice — an assigned preset asset and an invisible legacy inline block — and 8 of 15 already disagree (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/UnitData.cs:97)
- P1 The Vessel cascade level is a fully wired seam with zero assets: VesselId is only ever written as empty, and a test locks that in (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs:77)
- P1 Four whole content types have zero assets and zero readers — they exist only as two registry rows and a CreateAssetMenu each (C:/My Projects/Guildmaster-Autobattler/Assets/_Project/Scripts/Data/Definitions/GuildmasterData.cs:11)
- P1 The .gitignore rule added to hide the personal scratch scene does not match its filename, and the 976 KB near-duplicate of WorldScene is still exposed (C:/My Projects/Guildmaster-Autobattler/.gitignore:137)
- P1 The player can never choose a language: the whole locale-switching half of ILocalizationService has zero production callers (Assets/_Project/Scripts/Game/Services/LocalizationService.cs:81)
- P1 No fallback locale is configured, so every blank EN entry renders as an empty string instead of falling back to RU (Assets/_Project/Localization/LocalizationSettings.asset:61)
- P1 Every fighter's name label in battle prints the ScriptableObject file name, bypassing localization entirely (Assets/_Project/Scripts/Presentation/CombatPresenter.cs:442)
- P1 The node-farewell card renders with a blank title and blank body after every shop, chest and camp node (Assets/_Project/Scripts/UI/MenuRouter.cs:691)
- P0 Camp node is a mandatory stop on every act map and its screen asset is unassigned — the node resolves to nothing, silently (Assets/_Project/Scenes/CoreScene.unity:160)
- P1 The save has two commit points: node loot autosaves immediately, node progress only after the flow returns — quit-to-menu mid-node farms it (Assets/_Project/Scripts/Game/Services/ActRunner.cs:122)
- P1 "A run is active" has no owner — RunStateService.Current is never cleared, so the run shell outlives the run and buries the one screen with no cancellation token (Assets/_Project/Scripts/UI/UiRootBootstrap.cs:372)
- P1 GameConfig.asset stores 6 of 21 serialized fields; the one it does store contradicts the C# default that five tests pin (Assets/_Project/ScriptableObjects/Configs/GameConfig.asset:20)
- P1 Every Shop/Chest/Camp node ends on a completely blank farewell card — its keys live in the UI table, the renderer reads the Content table, and there is no fallback (Assets/_Project/Scripts/UI/MenuRouter.cs:691)
- P1 The ui.* namespace has two String Table owners; 16 authored, RU-filled UI keys are read from the wrong table and silently lose to hardcoded Russian literals in C# and UXML (Assets/_Project/Scripts/Game/Services/LocalizationService.cs:48)
- P1 The whole reward-rarity ramp is inert: not one shipped relic is DropRarity.Unique, so a boss showcase is identical to a trash-fight showcase (Assets/_Project/Scripts/Game/Flow/RewardService.cs:61)
- P1 "Is this an elite fight" has two owners: EncounterData._tier is authored but read only by a dev label, while BattlePresetData._isElite — the field selection actually reads — is unauthored on all 11 presets (Assets/_Project/Scripts/Game/Flow/NodeResolver.cs:159)
- P0 Split source of truth: Which StatsConfig / ClassBalanceConfig feeds the stat cascade (the base HP and MoveSpeed of every unit) (C:0)
- P1 Split source of truth: Body radius per unit Size (drives separation rest distance and, through CombatPositioning.AttackReachCenter, every attack reach) (C:0)
- P1 Split source of truth: Which localization table owns the `ui.*` screen keys (C:0)
- P1 Split source of truth: Whether the battle is paused (C:0)
- P1 Split source of truth: Act map generation parameters (depth, column widths, zone weights, anchors) (C:0)
- P1 Split source of truth: Which relics the player actually owns (the run's stash) (Assets/_Project/Scripts/Guild/RunState.cs:92)
- P1 Split source of truth: Which icon a unit tag (tag.tank, tag.fire, …) shows on its chip (Assets/_Project/Scripts/Data/Definitions/TagData.cs:17)
- P1 Split source of truth: What kind of node a «?» (MapNodeType.Unknown) turns out to be (Assets/_Project/Scripts/Game/Flow/RandomEventFlow.cs:29)
