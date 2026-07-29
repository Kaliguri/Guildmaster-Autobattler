# Актуализация технической документации — план и реестр находок

**Статус:** ЗАКРЫТ 2026-07-30 — основная часть отменена, два хвоста переехали.

> [!important] Почему отменён, а не доделан
> Заход существовал, чтобы догнать справочник до кода. С 30.07.2026 справочник заморожен
> (`status: archive`), а правда о коде живёт в коде и тестах — сверять больше нечего, и Фазы 1–3
> (правда, архив планов, новые доки на 49 непокрытых систем) отменены целиком. Решение и причины —
> [`journal/2026-07-30-code-owns-truth-journal-owns-why`](wiki/tech/00-meta/journal/2026-07-30-code-owns-truth-journal-owns-why.md).
>
> **Живыми остались два хвоста, и они не про справочник:**
> 1. **Дыра в журнале 12–16.07 и 20–25.07** (Фаза 4) — реальная работа для нового `00-meta/journal/`:
>    пройти `git log` за диапазон и написать по файлу на развилку. Заход — через скилл `tech-scribe`.
> 2. **Пять реализационных скиллов разошлись с кодом** (Фаза 7, `gamefeel-vfx` критично) — скиллы
>    предписывают, а не описывают, поэтому заморозка их не касается: это настоящий дефект.
>
> Реестр ниже читать только ради этих двух хвостов. Всё, что про точность страниц справочника, —
> история.

**Прежний статус:** ОТЛОЖЕНО до завершения рефактора кода (решение Макса, 2026-07-26).
**Снято на:** ветка `feature/unit-tag-icons`, коммит `1a55d09d` (2026-07-26).
**Метод:** фан-аут 13 агентов, каждое утверждение сверялось с живым кодом (Read/Grep по `Assets/_Project/Scripts`).

> **Важно для следующей итерации.** Реестр ниже — снимок на 26.07. Прямо сейчас идёт анализ
> текущего состояния кода и последующий рефактор. Часть находок после него станет неверной
> (особенно всё, что про имена классов и структуру слоёв). Порядок работы: **сначала сверить
> реестр с новым кодом, потом править доки.** Заново гонять полный аудит не нужно — нужна
> дельта от этого снимка.

### Правки, сделанные ПОСЛЕ снимка (проверить при заходе)

Реестр отложен, но точечные синхронизации происходят по ходу другой работы. Что уже тронуто —
здесь, чтобы заход не переделывал сделанное и, главное, **не считал страницу целиком свежей**.

| Дата | Док | Что сделано | Что ОСТАЛОСЬ |
|---|---|---|---|
| 2026-07-26 | `20-explanation/di-events.md` | **Только два места, касавшиеся фолбэков:** пример `Configure` в §1.3 (регистрировал `UnityAudioService` — класс удалён в тот же день и не был зарегистрирован никогда) + новый подраздел про `ScopeWiring.Require`/`Optional` как контракт ссылок скоупа на ассеты сцены | **Вся остальная страница по-прежнему отражает код на 2026-06-19** — вердикт реестра `update`, точность 60%, статус `needs_review`. Свежесть подраздела про DI ≠ свежесть страницы |
| 2026-07-26 | `00-meta/tech-changelog.md` | Добавлена ADR-запись «Фолбэки: три полосы» (правило + причина + две оговорки). Разбор и реестр фолбэков — `docs/fallback-audit.md`, правило — `.cursor/rules/project-context.mdc` | Дыра журнала 11.07→26.07 (фаза 4) этим **не закрыта** — запись одна, а развилок за период ~10–12 |

---

## Контекст захода

С последней сверки вики с кодом (2026-07-16) прошло **239 коммитов**. 24 дока из 43 не
трогались ровно с той даты. Проверено: 41 док `docs/wiki/tech`, 49 систем без покрытия,
33 периферийных файла (журналы `docs/*.md`, агентские гайды, скиллы).

Границы захода, согласованные с Максом: `docs/wiki/tech` + `docs/*.md` + `CLAUDE.md`
и `.cursor/rules` + `.claude/skills`. Глубина: сверка **и** новые доки на непокрытые системы.
Системы в полёте (тултипы, VFX, анимация) — фиксировать как есть со статусом `needs_review`.

---

## План изменений

Работаем в текущей ветке, коммит после каждой фазы.

| Фаза | Что | Объём |
|---|---|---|
| **0. Каркас** | MOC: вернуть выпавшие `simbench` и `act-map-run-loop`; убрать второй источник статусов (ярлыки в MOC против `status` во frontmatter — сейчас расходятся); журнал сессии; прогон `scripts/check-wiki-links.ps1` | S |
| **1. Правда** | 9 доков с критическими расхождениями: `di-events`, `run-flow`, `combat-model`, `data-stats-damage`, `data-layer`, `scene-sorting`, `input-camera`, `ui-navigation`, `effects-abilities` | L |
| **2. Архив планов** | 11 planning-доков — статус-шапки под факт; роадмап получает отметку «где мы стоим». **Инвариант: в planning правим только шапки, имена классов внутри не трогаем — это архив замысла** | M |
| **3. Новое** | 49 пробелов, сжатых в ~10 доков (список ниже) | L |
| **4. Журнал** | Дыра `tech-changelog` 11.07→26.07: ~10–12 ADR-записей по развилкам, где был выбор. Мелкие фиксы не переносим — они есть в git | M |
| **5. Периферия** | 4 отработанных журнала в архив, план UI-архитектуры — в вики, живые трекеры оставить | M |
| **6. Гайды** | `CLAUDE.md` + 5 файлов `.cursor/rules` (`unity-csharp.mdc` — под снос) | M |
| **7. Скиллы** | 5 контурных скиллов разошлись с кодом; `gamefeel-vfx` — критично | M |

### Целевые новые доки (Фаза 3)

Сжатие 49 находок в связные документы, а не документ на каждую находку.

| Док | Кластер | Что собирает |
|---|---|---|
| `unit-taxonomy` | 10-reference | боевые классы, теги (4 оси), `SpeciesData`/подвиды, `CreatureType` |
| `stat-cascade-and-damage` | 20-explanation | 4-уровневый каскад статов, `ModifierOp.Override`, поисточниковая модель урона, сродство, `Stats.Explain` |
| `vfx-and-feel` | 20-explanation | префаб-шов `VfxData`, `CombatFeelConfig`, `IVisualTempo`, `VisualToggles` |
| `ui-design-system` | 10-reference | токены «тёплого света», единый источник контуров, контролы `gm-*`, `gm-chip`, `AspectBox`, витрина |
| `run-beats` | 20-explanation | `RunBeatStage`, фаза Interlude, `IScreenTransition`, `MenuVisibilityMessages` |
| `act-map` | 10-reference | `WorldMapView`, `MapStyle`, `ActConfig`, префабы узла и дорожки, world-space карта |
| `skeletal-animation` | 30-how-to | Aseprite → PSB → Unity, bone-export, риг `BoneUnit` |
| `docs-tooling` | 30-how-to | `check-wiki-links.ps1`, `docs-lint.yml`, `statdb.ps1`, paths-filter в CI |
| `tooltips-and-descriptors` | 10-reference | `TooltipSystem`, `ITooltipContentFactory`, `IDescriptionService` — статус `needs_review` (в полёте) |
| `build-identity` | 10-reference | Build Profile Windows, defines, брендинг/splash, профили Addressables |

---

## Реестр 1: дрейф вики (41 док)

Точность — доля документа, совпавшая с кодом. C/M/m — критические / значимые / мелкие расхождения.

| Док | Вердикт | Точность | Труд | C | M | m | status: сейчас → должен |
|---|---|---|---|---|---|---|---|
| `20-explanation/run-flow.md` | rewrite_section | 45% | L | 3 | 9 | 2 | ready → needs_review |
| `30-how-to/project-setup.md` | rewrite_section | 45% | L | 1 | 3 | 1 | needs_review → needs_review |
| `10-reference/scene-sorting.md` | update | 50% | L | 1 | 3 | 1 | needs_review → needs_review |
| `40-planning/vertical-slice.md` | update | 55% | M | 1 | 2 | 1 | living → archive |
| `40-planning/roadmap.md` | update | 55% | M | 1 | 2 | 0 | living → living |
| `10-reference/input-camera.md` | update | 55% | L | 2 | 5 | 1 | ready → needs_review |
| `20-explanation/data-stats-damage.md` | rewrite_section | 58% | L | 4 | 2 | 2 | ready → needs_review |
| `20-explanation/di-events.md` | update | 60% | M | 2 | 3 | 3 | needs_review → needs_review |
| `40-planning/stabilization.md` | update | 60% | M | 0 | 3 | 2 | living → living |
| `40-planning/seed.md` | update | 60% | M | 0 | 2 | 0 | planned → needs_review |
| `10-reference/data-layer.md` | update | 60% | L | 3 | 7 | 1 | ready → needs_review |
| `20-explanation/effects-abilities.md` | update | 63% | M | 1 | 5 | 2 | ready → needs_review |
| `40-planning/sfx.md` | update | 65% | M | 3 | 1 | 0 | needs_review → archive |
| `20-explanation/index.md` | update | 66% | M | 0 | 4 | 3 | needs_review → needs_review |
| `10-reference/combat-model.md` | rewrite_section | 68% | L | 2 | 4 | 5 | ready → needs_review |
| `20-explanation/presentation.md` | update | 70% | M | 0 | 4 | 4 | ready → needs_review |
| `30-how-to/adding-assets.md` | update | 70% | M | 0 | 1 | 4 | needs_review → needs_review |
| `10-reference/arena.md` | update | 70% | M | 0 | 4 | 1 | needs_review → needs_review |
| `10-reference/ui-navigation.md` | update | 75% | M | 1 | 3 | 1 | ready → needs_review |
| `10-reference/editor-tools.md` | update | 75% | S | 0 | 2 | 1 | ready → needs_review |
| `10-reference/saves.md` | update | 75% | S | 0 | 2 | 3 | needs_review → needs_review |
| `10-reference/tech-stack.md` | update | 75% | M | 0 | 3 | 4 | needs_review → needs_review |
| `20-explanation/simulation.md` | update | 78% | M | 0 | 5 | 4 | needs_review → needs_review |
| `10-reference/assemblies.md` | update | 80% | S | 0 | 3 | 4 | ready → needs_review |
| `00-meta/index.md` | update | 80% | S | 0 | 2 | 0 | living → living |
| `40-planning/stat-system.md` | rewrite_section | 80% | M | 0 | 2 | 1 | ready → needs_review |
| `10-reference/asset-inventory.md` | update | 85% | S | 0 | 3 | 3 | ready → needs_review |
| `40-planning/act-map-run-loop.md` | update | 85% | S | 1 | 2 | 0 | draft → archive |
| `40-planning/deployment-encounters.md` | update | 88% | S | 0 | 1 | 1 | archive → archive |
| `40-planning/phase-3-ai-relics.md` | update | 88% | S | 0 | 1 | 1 | ready → archive |
| `40-planning/simbench.md` | update | 90% | S | 0 | 1 | 1 | ready → archive |
| `40-planning/visual-harness.md` | update | 90% | S | 0 | 0 | 1 | archive → archive |
| `40-planning/attack-timing.md` | update | 90% | S | 1 | 1 | 1 | ready → archive |
| `40-planning/phase-4-content.md` | keep | 90% | S | 0 | 0 | 2 | ready → archive |
| `40-planning/phase-2-effects.md` | keep | 92% | S | 0 | 0 | 1 | ready → archive |
| `40-planning/content-hub.md` | keep | 92% | S | 0 | 0 | 2 | ready → archive |
| `40-planning/steam-workshop.md` | keep | 93% | S | 0 | 0 | 2 | planned → planned |
| `40-planning/phase-1-combat-core.md` | keep | 95% | S | 0 | 0 | 0 | archive → archive |
| `40-planning/lighting-2d.md` | keep | 95% | S | 0 | 0 | 1 | planned → planned |
| `20-explanation/netcode.md` | keep | 95% | S | 0 | 0 | 4 | ready → ready |
| `30-how-to/docs-site.md` | keep | 95% | S | 0 | 0 | 1 | ready → ready |

### Расхождения по докам (критические и значимые)

#### `20-explanation/run-flow.md` — rewrite_section, 45%

*Самый дрейфующий документ. Он написан как замысел (лобби, RunSetup, мини-игры, MapScene, ретраи «до 2 раз»), а стоит в кластере «как есть сейчас» со статусом ready — и потому активно дезинформирует. Реализованная петля выглядит иначе: TitleCard → MainMenu → BeginAct → ActRunner по узлам, весь мир persist (WorldScene + BattleScene грузятся один раз на буте), карта — world-space слой, а не UI-оверлей, ретраи — пул перезапусков на акт. Плюс целиком отсутствуют BattlePhase, RunBeatStage и IScreenTransition — самое новое за последние дни.*

- **КРИТ** — **док:** §9 «Структура сцен»: аддитивные геймплейные сцены MapScene и BattleScene грузятся/выгружаются по флоу через NGO scene management
  **код:** MapScene не существует как ассет вообще (в Assets/_Project/Scenes есть BootScene, CoreScene, WorldScene, BattleScene, UiGallery, UiPreview). Persist-мир — это WorldScene, и она вместе с BattleScene грузится один раз в GameBootstrap.StartBootAsync и НИКОГДА не выгружается. NGO scene management не задействован: SceneLoader работает через обычный SceneManager.LoadSceneAsync(Additive), а его XML-док честно говорит «NGO Scene Management подключится в Фазе 6».
  **где:** Assets/_Project/Scripts/Game/GameBootstrap.cs (StartBootAsync); Assets/_Project/Scripts/Game/Services/SceneLoader.cs (LoadWorldAsync/LoadBattleAsync/UnloadBattleAsync); Assets/_Project/Scenes/
  **ЗАКРЫТО 2026-07-26:** §9 переписан под persist-модель; заведён `10-reference/scenes.md` (карта сцен как есть). Состав сцен с тех пор изменился и сам: `BootScene` и `UiGallery` снесены, `BattleScene` переименована в `CombatSystemsScene`, выгрузка убрана из `ISceneLoader` вовсе.
- **КРИТ** — **док:** §9: «Глянуть карту/меню в бою» = read-only UI-оверлей, читающий RunState, а не переключение сцен
  **код:** Карта — не UI-оверлей, а world-space слой в persist-сцене: WorldMapView живёт в WorldScene своей зоной, разнесённой от арены, привязывается к WorldMapViewLink из корневого скоупа, а показом (в т.ч. посреди боя) владеет WorldMapController через SetWorldMapRequest/WorldMapSpaceChangedEvent. UITK-вариант карты был и снесён — комментарий в RootLifetimeScope: «UITK-карта снесена после приёмки: держать второй путь к той же карте значило чинить каждый баг дважды».
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs (регистрация WorldMapViewLink, WorldMapController, WorldMapNodeChooser + комментарий про снос UITK-карты); Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs; Assets/_Project/Scripts/Game/Flow/WorldMapController.cs
  **ЗАКРЫТО 2026-07-26:** §9 говорит про world-space карту и режим камеры `Map`; UI-оверлеями там названы только меню и инвентарь.
- **КРИТ** — **док:** §2 «Иерархия флоу»: GameFlow → Boot → MainMenu → Lobby (мультиплеер-сетап) → RunSetup (выбор ГМ [мини-игра], раздача Сосудов, перки [мини-игра], сложность) → Run
  **код:** Ни Lobby, ни RunSetup в коде нет. Реальный верхний цикл GameFlow.RunGameAsync: TitleCardPresenter.ShowAsync() один раз за сессию → цикл while с MainMenuPresenter.ShowAsync(hasSave) → выбор Continue (RunStateService.Load) либо новый забег (NewDefaultRun) → RunActAsync → назад в меню; Quit закрывает игру. RunActAsync = BeginAct(ActConfig.ToGenConfig()) + Autosave + публикация RunPartyReadyEvent + делегирование в ActRunner + экран исхода + DeleteSave.
  **где:** Assets/_Project/Scripts/Game/Services/GameFlow.cs (RunGameAsync, RunActAsync)
- знач. — **док:** §6 «Бой и ретраи (до 2 раз)»: поражение (попытка 1-2) → возврат в Prep → перезапуск; 3-е поражение финальное; саб-сид боя = runSeed + индекс_боя без номера попытки
  **код:** Механика другая: пул перезапусков НА АКТ. RunStateService.BeginAct ставит Current.RestartsRemaining = _config.RestartsPerAct; NodeResolver отдаёт в BattleFlow делегат () => _runStates.TrySpendRestart(); BattleFlow крутит while (!Won && TrySpendRestart()) → _session.RequestRestart() → снова ждёт исход. Фиксированного «до 2 раз на бой» нет — в XML-доке BattleFlow это прямо названо заменой прежнего фикс-счётчика («Заменяет прежний фикс-счётчик на бой (техдолг)»). Возврата в отдельную фазу Prep между попытками в коде тоже нет — перезапуск идёт командой в живую симуляцию.
  **где:** Assets/_Project/Scripts/Game/Flow/BattleFlow.cs (Run, параметр tryConsumeRestart); Assets/_Project/Scripts/Guild/RunStateService.cs (BeginAct строка 90, TrySpendRestart строка 123); Assets/_Project/Scripts/Game/Flow/NodeResolver.cs (строка 95)
- знач. — **док:** Документ нигде не описывает фазы пребывания игрока (Deployment/Fighting/Interlude) и стыки узлов
  **код:** Это ядро текущего потока, и его в тексте нет совсем. Есть enum BattlePhase {None, Deployment, Fighting, Interlude} — он определяет и центр верхней панели забега, и право UI закрыть мир непрозрачным задником; читается через IBattleClock (Phase, PhaseChanged, ElapsedSeconds, RequestStart), который реализует BattleSession, зарегистрированный в Root сразу как IBattleSession + IBattleClock. Стыки узлов вынесены в шов IRunBeatStage: EnterRestBeat(ct) возвращает мир (RequestReset), ставит фазу Interlude и показывает кнопки передышки «Продолжить»/«К построению»; EnterNode() ставит None. Петля ActRunner НЕ ждёт этих кнопок — узел засчитан раньше.
  **где:** Assets/_Project/Scripts/Data/Definitions/BattleClock.cs (enum BattlePhase, interface IBattleClock); Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs (IRunBeatStage, EnterRestBeat/EnterNode); Assets/_Project/Scripts/Game/Services/ActRunner.cs (RunActAsync, вызовы _beat)
- знач. — **док:** §8 «Карта акта»: генерится целиком из сида на старте акта, хранится в RunState (граф + позиция); про параметры генерации сказано лишь «через IRngService»
  **код:** Верхнеуровнево верно (BeginAct генерит карту, MapState живёт в RunState, ActRunner ходит по MapTraversal), но целый слой конфигурации не описан: ActConfig — SO в RootLifetimeScope, отдаёт MapGenConfig.ToGenConfig(); MapGenConfig задаёт Columns (по умолчанию 15), EdgeColumnWidth/EdgeColumns, Min/MaxColumnWidth, MaxEdgesPerNode, а главное — ZoneRule[] Zones (веса типов узлов по диапазонам этажей) и AnchorRule[] Anchors (принудительные узлы на этаже). Ассет не назначен → рантайм-инстанс с дефолтами, игра не падает.
  **где:** Assets/_Project/Scripts/Guild/ActConfig.cs; Assets/_Project/Scripts/Guild/MapGenConfig.cs (ZoneRule, AnchorRule, NodeTypeWeight); Assets/_Project/Scripts/Game/RootLifetimeScope.cs (регистрация ActConfig); Assets/_Project/Scripts/Game/Services/GameFlow.cs (RunActAsync → BeginAct)
- знач. — **док:** §3: узел резолвится в IEventFlow, реализации — BattleFlow / ShopFlow / TrainingFlow / HealFlow / TournamentFlow / RiskFlow / MiniGameFlow
  **код:** Интерфейс совпадает буквально (UniTask<EventResult> Run(RunContext ctx)), и таблица типов действительно в NodeResolver без центрального switch в петле — это keep. Но набор реализаций другой: BattleFlow (обёрнутый в BattleNodeFlow), TextEventFlow, ShopFlow, ChestFlow, CampFlow, RandomEventFlow (для узла «?»), CompletedStubFlow. TrainingFlow, HealFlow, TournamentFlow, RiskFlow, MiniGameFlow не существуют. Соответственно и enum MapNodeType: Start, Battle, Elite, TextEvent, Shop, Boss, Chest, Unknown, Camp.
  **где:** Assets/_Project/Scripts/Game/Flow/RunFlow.cs (IEventFlow, RunContext); Assets/_Project/Scripts/Game/Flow/NodeResolver.cs (Resolve); Assets/_Project/Scripts/Guild/RunState.cs (enum MapNodeType)
- знач. — **док:** §7 «Мини-игры — модульные, изолированные»: interface IMiniGame { UniTask<MiniGameResult> Run(MiniGameContext ctx); }, отдельная сборка Guildmaster.MiniGames
  **код:** Интерфейса IMiniGame в коде нет ни одного вхождения; MiniGameResult/MiniGameContext тоже. Сборка Guildmaster.MiniGames заведена, но пуста — в папке лежит только Guildmaster.MiniGames.asmdef и ни одного .cs. Раздел целиком — замысел, не «как есть».
  **где:** Assets/_Project/Scripts/MiniGames/ (только Guildmaster.MiniGames.asmdef); grep IMiniGame по Assets/_Project/Scripts — пусто
- знач. — **док:** §5 «Автосейв — на переходах флоу»: три точки (старт забега / перед стартом ивента / перед экраном наград), вызов `await _save.Autosave(runState)`
  **код:** Точек больше трёх и они в других местах, а вызов синхронный и без аргумента — RunStateService.Autosave(). Фактические вызовы: GameFlow.RunActAsync (после BeginAct и после завершения акта), GameFlow.RunSingleBattleAsync (после узла), ActRunner (при поражении и после каждого MapTraversal.Advance), RewardPresenter, ShopController (три места: покупка/продажа/прочее), EventEffectApplier. Ключевое решение, которого в документе нет: продвижение по карте и автосейв делаются СРАЗУ после узла, не дожидаясь кнопки передышки.
  **где:** Assets/_Project/Scripts/Guild/RunStateService.cs (Autosave, строка 131); Assets/_Project/Scripts/Game/Services/ActRunner.cs (строки 113, 122 + комментарий про реш. Макса 2026-07-26); Assets/_Project/Scripts/Game/Services/GameFlow.cs (180, 193, 111)
- знач. — **док:** Документ не упоминает переход между кадрами («моргание») — единственные упоминания переходов это push/pop флоу
  **код:** Появился отдельный шов Core.Flow.IScreenTransition с ScreenTransitionShape (InSeconds/HoldSeconds/OutSeconds/FocusUv) и реализацией Presentation.Transition.ScreenTransitionRunner, зарегистрированной entry-point'ом в КОРНЕВОМ скоупе. Причина прописана прямо в XML-доке интерфейса (QA #53): раньше три фазы вёл сам заказчик — карта акта, — но выбор узла уводил игрока с карты, и переход обрывался на пике; владелец шторки обязан пережить смену того, что под ней. Это архитектурное решение уровня run-flow и должно быть в этом документе.
  **где:** Assets/_Project/Scripts/Core/Flow/IScreenTransition.cs (ScreenTransitionShape, IScreenTransition.Play/Cancel); Assets/_Project/Scripts/Presentation/Transition/ScreenTransitionRunner.cs; Assets/_Project/Scripts/Game/RootLifetimeScope.cs (RegisterEntryPoint<ScreenTransitionRunner>().As<IScreenTransition>())
- знач. — **док:** §4 «RunState (durable)»: сид, текущий акт, граф карты + позиция, ростер гильдии (Сосуды + инвентарь реликвий/предметов), золото/ресурсы, травмы, сложность, привязка игрок→Сосуды, ГМ, разблокированные AI-профили
  **код:** Часть полей есть (SchemaVersion, Seed, CurrentActIndex, Difficulty, Gold, RelicInventory, RelicCapacity, PartyItemIds, Guild как RosterSlot[], Map, SlotOwner), но травм, ГМ и разблокированных AI-профилей в RunState НЕТ. Зато есть неописанное RestartsRemaining — то самое поле, на котором держится реальная механика ретраев, и SavedPosition внутри RosterSlot (расстановка durable).
  **где:** Assets/_Project/Scripts/Guild/RunState.cs (class RunState, строки 76-105; class RosterSlot, строки 59-65)
- знач. — **док:** §10 «Мультиплеер: сессия и реконнект» — ready-gate `await _net.WhenAllPlayersReady()`, фиксированный состав по SteamID, автопауза при дисконнекте, отсчёт 3…2…1
  **код:** Реализации нет — есть только шов с соло-телом: interface IReadyGate { UniTask WhenAllReady(); } и SoloReadyGate, возвращающий UniTask.CompletedTask; interface IPlayerIntentSource с SoloPlayerIntentSource (IsLocalAuthority => true). В XML-доке шва так и написано: «Шов есть — реализации нет». Метода WhenAllPlayersReady не существует. Раздел стоит либо явно пометить как план, либо увести в 40-planning.
  **где:** Assets/_Project/Scripts/Game/Flow/RunFlowSeams.cs (IReadyGate/SoloReadyGate, IPlayerIntentSource/SoloPlayerIntentSource); Assets/_Project/Scripts/Game/RootLifetimeScope.cs (регистрация соло-тел)

#### `30-how-to/project-setup.md` — rewrite_section, 45%

*Документ помечен «Выполнено (2026-05-28)» и с тех пор не пересобирался. Половина чеклиста описывает мир, которого больше нет: не та папка тестов, не тот граф сборок, не тот MCP-пакет и полностью устаревшая структура документации. Шаги 2, 3, 4, 6, 9 требуют переписывания.*

- **КРИТ** — **док:** Шаг 6 приводит структуру docs/wiki с нумерацией «0.0. README.md», «0.3. Подготовка проекта (Unity).md», «1. Сборки.md» и «Соглашение по нумерации» с префиксами 0.0/0.1/1.–N.
  **код:** Вика давно перевезена на Diátaxis-кластеры с латинскими слагами: docs/wiki/tech/{00-meta,10-reference,20-explanation,30-how-to,40-planning}, файлы вида assemblies.md, tech-changelog.md. Нумерованных русских имён файлов в tech/ не осталось ни одного. Раздел активно уводит в несуществующую раскладку.
  **где:** ls docs/wiki/tech/* (00-meta/index.md, 10-reference/assemblies.md, …); сам этот файл лежит по пути 30-how-to/project-setup.md, а доке называет себя «0.3. Подготовка проекта (Unity).md»
- знач. — **док:** Шаг 2: «Создать Assets/Tests/EditMode/ и Assets/Tests/PlayMode/», в дереве — Assets/Tests/ рядом с _Project
  **код:** Папки Assets/Tests не существует. Тесты лежат внутри проекта: Assets/_Project/Tests/EditMode и Assets/_Project/Tests/PlayMode (+ вложенный Balance).
  **где:** ls Assets/Tests → No such file or directory; Assets/_Project/Tests/EditMode/Guildmaster.Tests.EditMode.asmdef
- знач. — **док:** Шаг 2/3: Scripts/ = Core, Units, Combat, Guild, UI; граф зависимостей «Core ← Units ← Combat / Guild ← UI»
  **код:** Сборки Guildmaster.Units не существует вовсе. Реально 12 папок скриптов и 18 asmdef: Core, Data, Combat, Guild, Game, Presentation, Net, MiniGames, DevTools, Balance, UI, EditorTools (+ *.Editor). Guildmaster.Core имеет пустой references — граф в доке не описывает ни одну реальную связь.
  **где:** find Assets/_Project -name *.asmdef; Assets/_Project/Scripts/Core/Guildmaster.Core.asmdef ("references": [])
- знач. — **док:** Шаг 4: «mcp-unity — для интеграции с Cursor AI», «После установки mcp-unity: Window → MCP Unity → Server Window → запустить сервер»
  **код:** Установлен другой пакет — com.coplaydev.unity-mcp по git-URL v10.0.0 (CoplayDev/unity-mcp), окно называется Window → MCP for Unity, мост на порту 6400. Инструкция ведёт в несуществующее меню.
  **где:** Packages/manifest.json:4 ("com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.0.0")

#### `10-reference/scene-sorting.md` — update, 50%

*Документ описывает одну боевую сцену образца июля-10, а раскладка стала мультисценовой (persist WorldScene держит камеру, арену и карту). Стек слоёв не полон (нет DevOverlay), правило «Default не для контента» нарушено картой акта, а «авто-Y без ручного Order» — юнитами.*

- **КРИТ** — **док:** §1: «Актуально для Assets/_Project/Scenes/BattleScene.unity», иерархия === CAMERA === (Main Camera, CM Action/Overview/Dev, CombatFocusTarget) и === ARENA === (Arena Layout, Arena Ground (Temp)) — в BattleScene
  **код:** Камера и арена переехали в персистентную WorldScene: там === CAMERA === (Main Camera, CM Action/CM Overview/CM Dev/CM Map, [Camera]), CombatFocusTarget, === ARENA === (Arena Layout, Arena Ground (Temp), Test Zone Arena Skin), а также новые группы === MAP === (тайлмапы + Map Post FX) и === MENU BACKDROP ===, и скоуп [World]. В BattleScene остались только === SYSTEMS ===/[Combat]/[Presenter] и === DEV ===. Четвёртой vcam (CM Map) в доке нет вовсе.
  **где:** Assets/_Project/Scenes/WorldScene.unity (объекты [World], === CAMERA ===, CM Map, === ARENA ===, === MAP ===); Assets/_Project/Scenes/BattleScene.unity; Assets/_Project/Scripts/Game/WorldLifetimeScope.cs — WorldLifetimeScope.Configure()
- знач. — **док:** §2 «Стек слоёв»: шесть слоёв Default / Background / GroundFX / Units / OverheadFX / WorldUI
  **код:** В проекте семь sorting-слоёв: последним добавлен DevOverlay (uniqueID 1122334455). Он реально используется дев- и расстановочной презентацией — таблица о нём молчит.
  **где:** ProjectSettings/TagManager.asset — m_SortingLayers; Assets/_Project/Scripts/Presentation/CombatStatusOverlay.cs — SortingLayer.NameToID("DevOverlay"); Assets/_Project/Scripts/Presentation/CombatAreaFlash.cs; Assets/_Project/Scripts/Presentation/DeploymentView.cs — резолв слоя DevOverlay
- знач. — **док:** §2: слой Default — «системный, не использовать для контента»
  **код:** Вся карта акта — контент — рисуется на Default: WorldMapView по умолчанию берёт слой "Default", префабы MapNode/PathDot лежат на слое с индексом 0. Порядок внутри карты задаётся не слоями и не Order, а Z-глубиной (TableZ/BackdropZ/EdgeZ/NodeZ/FogZ/PawnZ) — сознательно, потому что Shapes и SpriteRenderer слоями между собой надёжно не сортируются. Этой третьей модели порядка в §2 нет.
  **где:** Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs — поле _sortingLayerName = "Default", константы TableZ/BackdropZ/EdgeZ/NodeZ/FogZ/PawnZ, SortingLayerId(); Assets/_Project/Prefabs/Map/MapNode.prefab и PathDot.prefab — m_SortingLayer: 0
- знач. — **док:** §3: «всё на одном слое (Units) авто-сортируется по Y без ручного Z/Order»
  **код:** Настройка Transparency Sort Mode = Custom Axis (0,1,0) на месте, но юниты Y-сортируются вручную: UnitView каждый кадр пишет sortingOrder = -round(y * YSortPrecision), то же делает отдельный компонент YSortSprite. Правило «ручной Order не нужен» больше не описывает код.
  **где:** ProjectSettings/GraphicsSettings.asset — m_TransparencySortMode: 3, m_TransparencySortAxis {0,1,0}; Assets/_Project/Scripts/Presentation/UnitView.cs — присвоение _sprite.sortingOrder; Assets/_Project/Scripts/Presentation/YSortSprite.cs

#### `40-planning/vertical-slice.md` — update, 55%

*Статус-шапка остановилась на «часть A реализована, следующее — ресёрч B1», хотя B1, B3, C1 и D1 давно доставлены (частью через план act-map-run-loop). Реально не сделаны только два шага: пролог-шаблон и вариативные AI-пресеты.*

- **КРИТ** — **док:** Статус-шапка: «Часть A реализована (2026-07-14)… Следующее — развилка §7.1 + ресёрч B1 (генерация карты vs пролог-template): решение Макса»
  **код:** B1 закрыт (карта генерится из сида, есть экран выбора узла), B3 закрыт (оба типа узлов — текст-ивент и магазин), C1 закрыт (главное меню). Шапка держит доставленную работу как «следующий вопрос к решению».
  **где:** Assets/_Project/Scripts/Guild/MapGenerator.cs; Assets/_Project/Scripts/Game/Flow/MapNodeChooser.cs; Game/Flow/ShopFlow.cs; Game/Flow/MainMenuPresenter.cs
- знач. — **док:** Шаг B2: «RunTemplateData : ContentDefinition (домен run_template) — заранее выложенная обучающая карта пролога; флаг «пролог пройден» в сейв-профиле»
  **код:** Не реализовано и не начато: ни типа шаблона забега, ни домена run_template, ни флага пролога в коде нет — поиск по RunTemplateData / run_template / prologue даёт ноль совпадений.
  **где:** grep по Assets/_Project/Scripts (RunTemplateData/run_template/Prologue) → 0 файлов
- знач. — **док:** Шаг D2: «AI-пресеты: 2–3 варианта на релик… Loadout-таб «AI» перестаёт быть заглушкой»
  **код:** Не сделано: пресетов ровно по одному на юнита — 10 по числу реликвий плюс три гоблинских варианта. Выбора между вариантами у игрока нет.
  **где:** ls Assets/_Project/ScriptableObjects/AiPresets/ (13 ассетов: по одному на Assassin, Cryomancer, Defender, Druid, FlameSwordsman, IronSpearman, LightShepherd, Ranger, Treant, WhirlMonk + GoblinArcher/Flanker/Melee)

#### `40-planning/roadmap.md` — update, 55%

*Заявлен как «главное окно в прогресс» и living-документ, но прогресса в нём нет вообще: таблица девяти фаз не имеет ни одной отметки о выполнении, хотя фазы 0–5 и 7 доставлены. Плюс внутри застрял «факт» полуторамесячной давности, опровергнутый другим планом того же кластера.*

- **КРИТ** — **док:** Шапка: «Living (обновляется по ходу фаз; главное окно прогресса реализации)», далее таблица «Фазы (основной код-спайн)» 0–9
  **код:** В таблице нет колонки статуса и ни одной пометки. По коду закрыты фазы 0–5 и 7: боевое ядро, эффекты, AI+реликвии, контент-каркас, флоу забега (карта из сида, RunState, автосейв, награды), UI Toolkit-экраны. Не начата фаза 6 (сеть/кооп) — в Guildmaster.Net единственный файл — бутстрап транспорта.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs; Assets/_Project/Scripts/Guild/MapGenerator.cs; Assets/_Project/Scripts/Game/Services/ActRunner.cs; Assets/_Project/Scripts/UI/*ScreenView.cs; Assets/_Project/Scripts/Net/FacepunchTransportBootstrap.cs (единственный в сборке Net)
- знач. — **док:** Фаза 7: «…дизайн-система; IAudioService + Unity Audio (Suno) + SFX»
  **код:** Звук уехал на FMOD задолго до формальной Фазы 7: в DI зарегистрирован FmodAudioService, банки собраны и лежат в StreamingAssets. Строка роадмапа описывает отменённое решение.
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs:74; Assets/StreamingAssets/SFX.bank
- знач. — **док:** «Факт на 2026-05-30 (конец Фазы 2): визуальный editor-harness по факту ещё не собран — боёвка Фаз 1–2 целиком headless… сцены пусты, контент-ассетов нет»
  **код:** Харнесс собран и закрыт 2026-07-09, о чём прямо говорит соседний план в том же кластере; контент-ассетов теперь сотни. Врезка вводит в заблуждение о текущем состоянии.
  **где:** docs/wiki/tech/40-planning/visual-harness.md:8-11 («Выполнен и закрыт (2026-07-09)»); Assets/_Project/ScriptableObjects/ (Relics, Effects, Encounters, BattlePresets, Tags…)

#### `10-reference/input-camera.md` — update, 55%

*Документ описывает срез 2026-07-10/19 и не догнал ни разросшийся IInputService, ни четвёртый режим камеры (карта акта с нырком в узел). Статус ready держать нельзя: §2, §4, §5, §6, §7 расходятся с кодом.*

- **КРИТ** — **док:** §5: «три режима на Cinemachine 3» — таблица Action / Overview / Dev, «CameraModeController ... потребляя IInputService»
  **код:** Режимов четыре: добавлен CameraMode.Map (карта акта) со своей vcam _mapCam, своей зоной клампа _mapZone (приходит извне через EnterMap, боевая CameraZone к ней отношения не имеет), мягким клампом _mapFreedom, кадрированием при первом входе и нырком в узел (DiveMapTo/SurfaceMap/ExitMap). Tab на карте намеренно не циклит.
  **где:** Assets/_Project/Scripts/Presentation/Camera/CameraModeController.cs — enum CameraMode.Map, поля _mapCam/_mapZone/_mapFreedom/_mapDiveZoom, методы EnterMap/DiveMapTo/SurfaceMap/ExitMap, OnCycleView()
- знач. — **док:** §2: эскиз интерфейса IInputService — Context/SetContext, CameraPan, CameraZoomDelta, CycleViewRequested, PauseToggleRequested
  **код:** В интерфейсе сейчас также GameplaySuppressed {get;set;}, CameraPanDrag (MMB-пан), PointerOverUI (panel.Pick), PointerScreenPosition, PointerHeld, события PointerPressed/PointerReleased, GameSpeedCycleRequested («.»), MenuToggleRequested (Escape).
  **где:** Assets/_Project/Scripts/Core/Input/IInputService.cs — интерфейс IInputService целиком; Assets/_Project/Scripts/Game/Input/InputService.cs — InputService ctor (карты Camera/Combat/Deployment/Pointer/UI + always-on _menuToggle)
- знач. — **док:** §2 таблица контекстов: None / Menu / Deployment / Combat
  **код:** У InputContext пять значений: добавлен Map = 4 (карта акта — world-камера + указатель, без боевых действий); SetContext включает для него карты Camera + Pointer. Также появилась отдельная карта «Pointer», общая для Deployment и Map.
  **где:** Assets/_Project/Scripts/Core/Input/InputContext.cs — enum InputContext.Map; Assets/_Project/Scripts/Game/Input/InputService.cs — SetContext() case InputContext.Map
- **КРИТ** — **док:** §6: «Выдать/забрать: команда QFSW gm_cam_dev true/false → CameraModeController.SetDevAccess»; «Обычный игрок циклит только Action ↔ Overview, Dev входит в цикл, только когда доступ выдан»
  **код:** Команды gm_cam_dev в проекте нет ни одной (grep по Assets: строка встречается только в комментариях самого CameraModeController), у SetDevAccess нет вызывающих. Доступ выдаётся автоматически: в Start() стоит _devAccess = Application.isEditor, то есть в редакторе Dev в цикле всегда.
  **где:** Assets/_Project/Scripts/Presentation/Camera/CameraModeController.cs — Start() (_devAccess = Application.isEditor), SetDevAccess(); Assets/_Project/Scripts/DevTools/*.cs — список [Command(...)] без gm_cam_dev
- знач. — **док:** §5: «кламп ... из ArenaLayoutData.Bounds — ... зона = Bounds + авторимый _boundsPadding»
  **код:** Кламп идёт по отдельной зоне ArenaLayoutData.CameraZone (авторится своей жёлтой рамкой в ArenaLayoutAuthoring), а в режиме Map — по _mapZone. Поля _boundsPadding в контроллере нет. Плюс исключение: для Map максимальный зум считается по БОЛЬШЕЙ стороне зоны, для боевых — по меньшей.
  **где:** Assets/_Project/Scripts/Presentation/Camera/CameraModeController.cs — ActiveZone(), MaxZoomForZone(), ClampVisibleCenter(); Assets/_Project/Scripts/Core/Arena/ArenaLayoutData.cs — CameraZone
- знач. — **док:** §4 «Клавиши (игрок)»: пан WASD+стрелки, зум колесо, Tab, Space
  **код:** Не хватает трёх штатных биндов: пан драгом средней кнопки мыши (MMB), «.» (period) — цикл скорости боя 1x→2x→3x, Escape — MenuToggle (оверлей системного меню, всегда активен, намеренно не глушится GameplaySuppressed).
  **где:** Assets/_Project/Scripts/Game/Input/InputService.cs — _middlePan/_pointerDelta, _gameSpeedCycle (<Keyboard>/period), _menuToggle (<Keyboard>/escape, Enable() в ctor)
- знач. — **док:** Статус-шапка и §7: «сборка рига камеры в сцене (Cinemachine Brain + 3 виртуальные камеры + focus-target) ... — за Максом», чек-лист сборки в BattleScene
  **код:** Риг собран и живёт в persist-сцене WorldScene: Main Camera, CM Action, CM Overview, CM Dev, CM Map, CombatFocusTarget, [Camera]; поднимает его WorldLifetimeScope (RegisterComponentInHierarchy<CameraModeController>().As<IScreenShake>()), боевой скоуп рига не дублирует.
  **где:** Assets/_Project/Scenes/WorldScene.unity — объекты === CAMERA ===/Main Camera/CM Action/CM Overview/CM Dev/CM Map/CombatFocusTarget; Assets/_Project/Scripts/Game/WorldLifetimeScope.cs — Configure()

#### `20-explanation/data-stats-damage.md` — rewrite_section, 58%

*Два ключевых раздела описывают устаревшую механику: §3.2 даёт формулу стата из трёх операций, хотя основной способ авторинга сейчас — четвёртая операция Override (заменяет базовый терм), а §4 приводит пайплайн урона без шага сродства. Плюс полностью отсутствуют изменения последних десяти дней: четырёхуровневый стат-каскад (класс → вид → подвид → персона), базовый тип UnitData вместо RelicData, поисточниковая модель урона (School/PhysicalSubtype/MagicElement/Affinity) и переименование Elemental → Magical. §3.1 (StatType, 30 статов), формула брони и §5 (SpatialHash) верны.*

- **КРИТ** — **док:** §3.2: «Три типа операций (ModifierOp): Flat, PercentAdd, PercentMult», формула final = (base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult), где base — дефолт конфига
  **код:** Операций четыре. ModifierOp.Override (=3) ЗАМЕНЯЕТ базовый терм стата (дефолт StatsConfig игнорируется, побеждает последний Override), и именно это — основной способ авторинга базовых статов юнита на SO. Формула в коде: baseTerm = Override (если задан) ИНАЧЕ дефолт, далее (baseTerm + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult). Без Override не понять, как вообще работает каскад класс→персона.
  **где:** Assets/_Project/Scripts/Data/Stats/ModifierOp.cs — ModifierOp.Override; Assets/_Project/Scripts/Combat/Stats/Stats.cs — RebuildCache()/Compose(), строки 143-187
- **КРИТ** — **док:** Документ описывает статы юнита как «дефолты StatsConfig + моды реликвии + таланты» — плоскую двухслойную схему
  **код:** Действует четырёхуровневый стат-каскад: (1) дефолты StatsConfig → (2) классовая база по UnitData.CombatClass через ClassBalanceConfig (ClassBaseline.Apply, первая группа) → (3-4) видовые и подвидовые скейлы врага (EnemyScalers.Apply, обычно PercentMult) → (5) стат-блок персоны → перки Vessel → моды предметов. Каскад работает именно за счёт правила «последний Override побеждает». Ни ClassBalanceConfig, ни SpeciesData, ни UnitClass в документе не упомянуты.
  **где:** Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs — ClassBaseline.Apply(); Assets/_Project/Scripts/Combat/Stats/EnemyScalers.cs — EnemyScalers.Apply(); Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs — Create(); Assets/_Project/Scripts/Data/Definitions/UnitData.cs — поле _combatClass
- **КРИТ** — **док:** §4, «Порядок расчёта»: raw × DamageDealtEff → броня (кроме True) × DamageTakenEff → max(0) → щит → HP
  **код:** Между бронёй и DamageTakenEff стоит шаг сродства: если req.Affinity != None, урон умножается на AffinityTable.Multiplier(Affinity, Target.CreatureType) — бронёй этот множитель не гасится и действует ДАЖЕ на True-урон. Также в DamageRequest есть DamageSourceKind (Ability/AutoAttack/Periodic/Reactive) с признаками IsDirectHit/IsAutoAttack — гейт для реактивов «на удар», и он же попадает в DamageResult.
  **где:** Assets/_Project/Scripts/Combat/Damage/DamagePipeline.cs — Execute(), шаг 2.5 (строки 50-54); Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs — Affinity, SourceKind, IsDirectHit
- знач. — **док:** §2, таблица SO: «RelicData — Чемпион: тип атаки, тип урона, стат-блок, пассивки, активки», VesselData, EffectData, AbilityData, StatsConfig
  **код:** Общий боевой кит вынесен в абстрактный UnitData : ContentDefinition, от которого наследуются RelicData (мета игрока) и EnemyData (мета врага) — сим, RuntimeUnitFactory и EncounterLoader работают именно с базовым типом. «Тип урона» распался на четыре оси: DamageSchool (Physical/Magical/True — гасится бронёй), PhysicalSubtype (Blunt/Slash/Pierce), MagicElement (Fire/Ice/Lightning/Arcane) и DamageAffinity (Poison/Light/Dark), плюс CreatureType самого юнита. Таблица SO не знает также SpeciesData, ItemData, ClassBalanceConfig, SimTuningConfig, TagData.
  **где:** Assets/_Project/Scripts/Data/Definitions/UnitData.cs — abstract class UnitData; EnemyData.cs, RelicData.cs; Assets/_Project/Scripts/Data/Definitions/CombatCategories.cs — enum DamageSchool/PhysicalSubtype/MagicElement/DamageAffinity/CreatureType
- **КРИТ** — **док:** Документ нигде не упоминает поисточниковый тип урона и говорит просто «тип урона юнита»
  **код:** Тип урона задаётся НА КАЖДЫЙ источник: у способности есть SchoolOverride/AffinityOverride (и PhysicalSubtypeOverride/MagicElementOverride) со значением Inherit = взять у кастера, а разрешение идёт через DamageCategories.Resolve при каждом касте. Есть отдельная структура DamageType (School + подтип + элемент + сродство) с инвариантом нормализации. Школа Elemental переименована в Magical (int-значение 1 сохранено ради ассетов), и магическая броня одна на все стихии.
  **где:** Assets/_Project/Scripts/Data/Definitions/DamageType.cs — struct DamageType; CombatCategories.cs — DamageCategories.Resolve(...); Assets/_Project/Scripts/Combat/Abilities/AbilitySystem.cs — ApplyToTarget()/ApplyCircle() вызывают DamageCategories.Resolve; коммиты bf6aa93d, b2d730f9
- знач. — **док:** §3.2: у Stats описаны только Get / AddModifiersFrom / RemoveModifiersFrom и dirty-кэш
  **код:** Добавлен слой инспекции: Stats реализует IStatExplainer и метод Explain(StatType) отдаёт StatValue с разложением на базу и вклады источников (вклад считается как «насколько итог просядет без этого модификатора»). Формула вынесена в единственный метод Compose, который обязаны звать и горячий путь, и разбор — иначе тултипы разойдутся с симом. Это опора тултипов/панели юнита.
  **где:** Assets/_Project/Scripts/Combat/Stats/Stats.cs — Explain(), ComposeSubset(), Compose(); Assets/_Project/Scripts/Data/Stats/StatValue.cs, IStatExplainer.cs

#### `20-explanation/di-events.md` — update, 60%

*Учебная часть про DI (зачем, три формы регистрации, IAsyncStartable, method injection) верна и держится. Конкретика устарела целиком: скоупов три, а не два; боевой скоуп больше НЕ умирает с боем; пример регистрации Root отстаёт на порядок; MessagePipe давно вышел за рамки «мост из боя в Audio/VFX/UI».*

- **КРИТ** — **док:** «Наши два скоупа»: RootLifetimeScope (сессия) + CombatLifetimeScope (бой), дочерний от Root
  **код:** Скоупов ТРИ. Между ними стоит WorldLifetimeScope — персистентный скоуп мира (WorldScene грузится аддитивно на буте и не выгружается), который держит камеру-риг, ArenaLayoutData, CombatFeelConfig, WorldMapView, MenuBackdropView, TestZoneArenaSkin. В его же XML-доке прямо сказано, что CombatLifetimeScope становится дочерним К НЕМУ и резолвит камеру/арену из предка. При этом собственный XML-комментарий CombatLifetimeScope всё ещё утверждает «дочерний от RootLifetimeScope» — код сам себе противоречит, но факт наличия третьего скоупа неоспорим.
  **где:** Assets/_Project/Scripts/Game/WorldLifetimeScope.cs (класс WorldLifetimeScope.Configure); Assets/_Project/Scripts/Game/CombatLifetimeScope.cs (XML-док класса)
- **КРИТ** — **док:** «CombatLifetimeScope — живёт один бой. Создаётся при входе в BattleScene, умирает при выходе. Поэтому всё боевое автоматически уничтожается в конце боя — не надо вручную чистить состояние… новый бой = новый скоуп = чистые сервисы»
  **код:** Ровно наоборот. BattleScene грузится ОДИН РАЗ на буте и не выгружается — боевой скоуп живёт всю сессию. Запуск боя = команда в живой sim (IBattleSession.RequestLaunch), а не загрузка сцены; сброс между боями делается ВРУЧНУЮ через IBattleSession.RequestReset → CombatSimulation.OnBattleReset, на который CombatPresenter вручную сносит виды, трупы, снаряды, цифры и VFX. То есть главный аргумент документа («структурно не надо чистить») в коде опровергнут, и вся ручная чистка существует именно потому, что скоуп переживает бой.
  **где:** Assets/_Project/Scripts/Game/GameBootstrap.cs (StartBootAsync: LoadWorldAsync + LoadBattleAsync с комментарием «BattleScene тоже persist… боевой скоуп живёт всю сессию»); Assets/_Project/Scripts/Game/Flow/BattleFlow.cs (Run); Assets/_Project/Scripts/Presentation/CombatPresenter.cs (HandleBattleReset)
- знач. — **док:** Пример Configure у RootLifetimeScope: четыре регистрации (IRngService, UnityAudioService as IAudioService, SceneLoader, GameFlow) + MessagePipe; и отдельно «.As<IAudioService>(): а завтра — FmodAudioService»
  **код:** В RootLifetimeScope.Configure сейчас порядка сорока регистраций: ContentDatabase/ContentRegistry, GameConfig, ActConfig, AudioCatalog, SettingsService, IUnitStatPreview, DescriptionService, TooltipSystem/TooltipContentFactory, три ViewModel, MenuRouter, UiNavigator, UiRootBootstrap, GameBootstrap, LocalizationService, JsonFileSaveService, RunStateService, SoloLocalPlayer, SceneLoader, BattleSession (как IBattleSession + IBattleClock), SoloReadyGate, SoloPlayerIntentSource, RewardService, RelicPricer, RewardPresenter, ContinuePresenter, OutcomePresenter, TitleCardPresenter, MainMenuPresenter, EventEffectApplier, ShopController, NodeResolver, WorldMapViewLink, WorldMapController, ScreenTransitionRunner, VisualTempo, VisualToggles, WorldMapNodeChooser, RunBeatStage, ActRunner, GameFlow, InputService. «Завтра» уже наступило: аудио регистрируется как FmodAudioService, а UnityAudioService лежит незарегистрированной заглушкой.
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs (Configure, строки 55-201; аудио — builder.Register<FmodAudioService>().As<IAudioService>())
- знач. — **док:** Схема каналов «CombatSimulation → CombatPresenter → MessagePipe → Audio/VFX/UI» и вывод, что MessagePipe нужен для развязки боя от подписчиков
  **код:** Это верно только для боевой ветки (четыре события ретранслирует именно CombatPresenter). Но сегодня MessagePipe — основная шина ВСЕГО макро-слоя, а не только моста из боя: ~25 типов сообщений вне Presentation. Флоу-запросы экранов (OpenLoadoutRequest, OpenRewardRequest, OpenTextEventRequest, OpenShopRequest, OpenChestRequest, OpenCampRequest, OpenContinueRequest, OpenOutcomeRequest, OpenMainMenuRequest, OpenTitleCardRequest, OpenNodeFarewellRequest), инвентарь/драг (EquipRelicRequest, EquipRelicAtCursorRequest, RelicDragEvent), мир и карта (SetWorldMapRequest, WorldMapSpaceChangedEvent, SetFormationRequest, SetTestZoneRequest, TestZoneChangedEvent, RunPartyReadyEvent), видимость меню и шторка (MainMenuVisibilityChangedEvent, ScreenBackdropChangedEvent, ScreenFadeChangedEvent). Публикуют их не презентеры боя, а NodeResolver, GameFlow, RunBeatStage, ContinuePresenter, WorldMapController, ScreenTransitionRunner, UiRootBootstrap.
  **где:** Assets/_Project/Scripts/Guild/*Messages.cs, Assets/_Project/Scripts/Data/Definitions/{LoadoutMessages,RewardMessages,TextEventMessages,TestZoneMessages}.cs, Assets/_Project/Scripts/Core/Flow/MenuVisibilityMessages.cs, Assets/_Project/Scripts/Game/Flow/WorldStageMessages.cs; потребители — UI/UiRootBootstrap.cs (Construct), Game/Flow/NodeResolver.cs
- знач. — **док:** Код-снипет моста: `view.OnDamageReceived(result.TotalDamage); _damageNumbers?.Spawn(target.Position, result.TotalDamage);`
  **код:** Обоих API больше нет. Поля `_damageNumbers` в CombatPresenter не существует — цифры спавнятся через ObjectPool<FloatingText> методом SpawnNumber(anchor, text, color, sizeScale), раздельно по щиту и HP, с задержкой сплита и масштабом от доли урона. UnitView.OnDamageReceived принимает (Color flash, Vector2 nudgeDir), а не число.
  **где:** Assets/_Project/Scripts/Presentation/CombatPresenter.cs (HandleDamageDealt, SpawnNumber, EnsureTextPool)

#### `40-planning/stabilization.md` — update, 60%

*Living-план без единой отметки о ходе работ, а в MOC он числится «текущим». По коду фазы 0–1 закрыты, фаза 2 закрыта частично (боевой таймер не сделан), фазы 4–5 не начаты вовсе. Нужны пофазные пометки, иначе непонятно, где мы в нём стоим.*

- знач. — **док:** Фаза 2: «Таймер боя: 90 секунд → бой ускоряется ×2; ещё через 60 секунд → ничья (для игроков это поражение)»
  **код:** Не реализовано. Лимита времени боя в коде нет вовсе: поиск по TimeLimit/BattleTimeout/SpeedUpAfter даёт ноль совпадений, а CheckOutcome завершает бой только по вайпу команды — Draw наступает лишь когда не осталось живых с обеих сторон. В UI есть только отображение таймера, без логики ускорения и ничьей.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs:619-644 (CheckOutcome, _outcome = anyAlive ? Win(aliveTeam) : Draw); Assets/_Project/Scripts/UI/RunModeBarView.cs:23,125-128 (только Label battle-timer)
- знач. — **док:** Фаза 4: «Делаем 5 реликвий: Штормовой, Некромант, Шаман, Бард, Двойник» + движковые швы (цепь, канал с прерыванием, призыв, луч по линии, копирование реликвии)
  **код:** Не начата: ни одного из пяти ассетов в контенте нет. В Relics/ лежат десять других реликвий. Заготовлены только теги (Summon, Copycat), самих героев и механик нет.
  **где:** ls Assets/_Project/ScriptableObjects/Relics/ (Assassin, BaseRelic, Cryomancer, Defender, Druid, FlameSwordsman, IronSpearman, LightShepherd, Ranger, Treant, WhirlMonk)
- знач. — **док:** Фаза 5: «9 недостающих по ГДД (элитные гоблины: командир, маг, волчий наездник, волк стаи; бандиты: щитоносец, молотобоец, арбалетчик, маг; земляной голем)»
  **код:** Не начата: в Enemies/ по-прежнему четыре обычных гоблина и тренировочный манекен, ни одного элитного гоблина, бандита или голема.
  **где:** ls Assets/_Project/ScriptableObjects/Enemies/ (GoblinArcher, GoblinCutthroat, GoblinGrunt, GoblinWarrior, TrainingDummy)

#### `40-planning/seed.md` — update, 60%

*Статус «План» занижает реальность: четыре из шести перечисленных тем уже работают на IRngService — карта, награды, магазин, случайные события. Планом остались только ввод/шаринг сида и синхронизация в мультиплеере.*

- знач. — **док:** Шапка: «План. Контракт IRngService существует; наполнение генерации (карта/награды/магазин/ивенты) — по мере готовности флоу забега»
  **код:** Наполнение сделано: карта акта генерится из сида забега под-сидом Seed + CurrentActIndex, награды, пул магазина и «?»-события тоже крутятся через IRngService.
  **где:** Assets/_Project/Scripts/Guild/RunStateService.cs:92 (new XorShiftRng(Seed + CurrentActIndex)); Assets/_Project/Scripts/Guild/MapGenerator.cs; Assets/_Project/Scripts/Game/Flow/RewardService.cs; ShopController.cs; RandomEventFlow.cs
- знач. — **док:** «Темы для описания»: генерация карты акта · генерация наград · генерация пула магазина · рандомизация ивентов · система сидов (ввод/отображение/шаринг) · воспроизводимость в мультиплеере
  **код:** Первые четыре пункта — уже не темы для описания, а работающий код, и их надо описывать как «есть». Не реализованы только два последних: ручного ввода/отображения/шаринга сида в коде нет, MP-синхронизации тоже.
  **где:** Assets/_Project/Scripts/Guild/RunState.cs:80 (public long Seed) — сид хранится и передаётся в WorldMapController.cs:135, но UI ввода сида отсутствует; в Assets/_Project/Scripts/Net нет ничего про RNG

#### `10-reference/data-layer.md` — update, 60%

*Каркас слоя данных (три слоя, ContentDefinition/id-конвенции, плоский ContentDatabase + IContentDatabase, Addressables-политика, лок/аудио-ключи, валидация) совпадает с кодом. Но каталог SO (§3) отстал минимум на три крупных изменения последних 10 дней: боевые классы как база HP/скорости (ClassBalanceConfig + UnitClass), слой Species у врагов и поисточниковая модель урона; плюс §9 (контент-менеджер) описывает Odin-окно, которого нет — живёт UI-Toolkit ContentHubWindow. Документ в 10-reference обязан совпадать с кодом, поэтому это не «план опережает», а дрейф.*

- **КРИТ** — **док:** §3.4 / §12 п.3: «StatsConfig — базовые значения юнита… Реликвии задают отличия от базы своим стат-блоком», база «MaxHP 120, AAD 12, AS 1, AR 1.5, MS 3, PhysArmor 4»
  **код:** Между StatsConfig и стат-блоком юнита вставлен обязательный классовый уровень: RuntimeUnitFactory.Create сначала зовёт ClassBaseline.Apply(stats, data, _classBalance), который добавляет группу ModifierOp.Override из ClassBalanceConfig.GetBaseModifiers(data.CombatClass) — MaxHP = _baseHp × hpMult, MoveSpeed = _baseMoveSpeed × moveMult. В ассете ClassBalanceConfig.asset _baseHp: 2000, _baseMoveSpeed: 3, профили Bruiser 1/1, Tank 1.5/0.85, Assassin 0.75/1.1, Ranged/Support/Summoner 0.65/0.75. Дефолты StatsConfig.asset тоже другие: MaxHP 1200, AutoAttackDamage 120, AttackRange 1 (не 1.5). Ни UnitClass, ни ClassBalanceConfig в документе не упомянуты ни разу.
  **где:** Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs (Create, строки 65–78) + Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs (Apply) + Assets/_Project/Scripts/Data/Definitions/ClassBalanceConfig.cs (GetBaseModifiers) + Assets/_Project/Scripts/Data/Stats/UnitClass.cs + ассеты Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset, StatsConfig.asset
- **КРИТ** — **док:** §3.1 «EnemyData : UnitData — вражеский юнит (мета врага)»: поля только ThreatPoints и GoldBounty
  **код:** У EnemyData есть ещё два поля стат-каскада: _species и _subspecies (тип SpeciesData), и они реально применяются в сборке юнита — EnemyScalers.Apply добавляет enemy.Species.Scalers после классовой базы и до стат-блока персоны. Тип SpeciesData (домен species, поле Scalers: StatModifier[]) в документе отсутствует целиком, как и сам уровень каскада «Вид/Подвид».
  **где:** Assets/_Project/Scripts/Data/Definitions/EnemyData.cs (поля _species/_subspecies) + Assets/_Project/Scripts/Data/Definitions/SpeciesData.cs + Assets/_Project/Scripts/Combat/Stats/EnemyScalers.cs (Apply) + RuntimeUnitFactory.Create
- **КРИТ** — **док:** §3.1 «Поля — текущий боевой состав RelicData (сводно): DamageType/AttackType/ResourceType…» — то есть у юнита одно поле «тип урона»
  **код:** Модель урона стала поисточниковой и многоосевой. UnitData несёт _damageSchool (DamageSchool, FormerlySerializedAs "_damageType"), _physicalSubtype (PhysicalSubtype), _magicElement (MagicElement), _affinity (DamageAffinity), _creatureType (CreatureType) и метод ResolveAutoAttackDamageType(). DamageType теперь — readonly struct-дескриптор одного источника (School + PhysicalSubtype + MagicElement + Affinity). У AbilityData свои четыре Inherit-override (_schoolOverride, _physicalSubtypeOverride, _magicElementOverride, _affinityOverride) и ResolveDamageType(caster). Ничего из этого в документе нет.
  **где:** Assets/_Project/Scripts/Data/Definitions/UnitData.cs (строки 21–35, ResolveAutoAttackDamageType) + Assets/_Project/Scripts/Data/Definitions/DamageType.cs (struct DamageType) + Assets/_Project/Scripts/Data/Definitions/CombatCategories.cs (DamageSchool, PhysicalSubtype, MagicElement, DamageAffinity, CreatureType, DamageCategories.Resolve) + Assets/_Project/Scripts/Data/Definitions/AbilityData.cs (ResolveDamageType)
- знач. — **док:** §3.0 TagData: «Авто-теги выводятся из данных маппингом (например RelicData.DamageType == Magic → tag.dmg_magic)», примеры id — tag.role_tank, tag.dmg_magic, tag.style_aggressive
  **код:** Появился явный резолвер UnitTagResolver.Resolve(UnitData, IContentDatabase): четыре оси Role→DamageType→Playstyle→Mechanic, авто-оси Role (из UnitClass) и DamageType (из автоатаки + всех способностей с DamageMultiplier > 0), устойчивая сортировка по TagCategory. Id-схема плоская, без префикса оси: tag.tank / tag.bruiser / tag.assassin / tag.ranged / tag.support / tag.summoner, tag.physical / tag.magical / tag.pure, tag.blunt / tag.slash / tag.pierce, tag.fire / tag.ice / tag.lightning / tag.arcane, tag.poison / tag.light / tag.dark. Примеры id в документе (tag.role_tank, tag.dmg_magic, tag.style_aggressive) не соответствуют ни одному реальному id.
  **где:** Assets/_Project/Scripts/Data/Definitions/UnitTagResolver.cs (RoleTagId / UmbrellaTagId / SpecificTagId / AffinityTagId) + Assets/_Project/Scripts/Data/Definitions/TagData.cs
- знач. — **док:** §3.1 «RelicData : UnitData»: поле Rarity типа enum RelicRarity (Common / Cursed / Divine)
  **код:** Поле разделено на две ортогональные оси: _kitPower (enum KitPower: Common/Cursed/Divine, боевая сила кита, FormerlySerializedAs "_rarity") и _dropRarity (enum DropRarity: Trash/Common/Unique, экономика выпадения). Типа RelicRarity в коде нет. На KitPower завязаны цены магазина в GameConfig (PriceCommon/PriceCursed/PriceDivine).
  **где:** Assets/_Project/Scripts/Data/Definitions/RelicData.cs (поля _kitPower/_dropRarity) + Assets/_Project/Scripts/Data/Definitions/ContentEnums.cs (KitPower, DropRarity)
- знач. — **док:** §9 «Контент-менеджер (редакторное окно). Решение: OdinMenuEditorWindow… Чистый UI Toolkit отклонён»
  **код:** Реализовано ровно наоборот: ContentHubWindow : EditorWindow на UI Toolkit, partial-класс из 9 файлов со страницами Browser / Balance / Audio / Doctor / Configs, собственный asmdef Guildmaster.ContentHub.Editor, вспомогательные Core/Preview/Widgets/Styles. Sirenix в шелле окна не используется.
  **где:** Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.cs (class ContentHubWindow : EditorWindow, enum Page) + файлы ContentHubWindow.{Browser,Balance,Audio,Doctor,Configs,Navigation,Visual,Coverage}.cs
- знач. — **док:** §3.1 «VesselData — StartingTraits: TraitData[] НОВОЕ, InfoTags: TagData[] НОВОЕ»
  **код:** В VesselData этих полей нет. Реально: _tags (string[], легаси-идентичность) и _perkModifiers (StatModifier[], помечен как плейсхолдер «Фаза 2/4»). Фабрика применяет только vessel.PerkModifiers.
  **где:** Assets/_Project/Scripts/Data/Definitions/VesselData.cs + RuntimeUnitFactory.Create (строки 77–78)
- знач. — **док:** §2.2 список доменов id: relic · enemy · vessel · effect · ability · trait · item · guildmaster · consequence · ai_preset · tag · run_mod · encounter · event · reward · act · fate
  **код:** В ContentDomains зарегистрированы ещё три домена, которых нет в списке: species (SpeciesData), vfx (VfxData) и battle_preset (BattlePresetData — реальная нагрузка боевых узлов карты, MapNode.PayloadId = battle_preset.*). Доменов reward / act / fate в карте нет (зарезервированы только на бумаге).
  **где:** Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs (словарь Domains) + Assets/_Project/Scripts/Guild/RunState.cs (enum MapNodeType, комментарий payload = battle_preset.*)
- знач. — **док:** §3.3 «Новые — структура забега»: ActMapConfig (она же MapData), ShopConfig, RewardTableData, DifficultyConfig, DossierPoolsConfig
  **код:** Ни одного из этих типов в коде нет. Карта акта реализована иначе и вне слоя Data: ActConfig (ScriptableObject в Guildmaster.Guild, не ContentDefinition) оборачивает plain-класс MapGenConfig, генерация — MapGenerator.Generate по под-сиду Seed + CurrentActIndex; экономика магазина (цены, реролл, процент продажи) живёт в GameConfig, а не в ShopConfig/DifficultyConfig.
  **где:** Assets/_Project/Scripts/Guild/ActConfig.cs, Assets/_Project/Scripts/Guild/MapGenConfig.cs, Assets/_Project/Scripts/Guild/MapGenerator.cs, Assets/_Project/Scripts/Guild/RunStateService.cs (BeginAct) + Assets/_Project/Scripts/Data/Definitions/GameConfig.cs
- знач. — **док:** §3.4 GameConfig: «DefaultMaster/Music/SfxVolume, DefaultLocale, VesselItemSlots = 3, AutosaveEnabled и др. UX-дефолты»
  **код:** GameConfig разросся далеко за «дефолты настроек»: LocalPlayerTeam, PartyBannerSlots, RelicCapacityBase/Max, экономика забега (StartGold, BattleGoldReward, PriceCommon/Cursed/Divine, PriceSpread, SellPercent, ShopRerollCost, RestartsPerAct), стартовая гильдия (GuildSize, StartingRelicId). Поля AutosaveEnabled нет. Разграничение «GameConfig = только дефолты пользовательских настроек» в §3.4 больше не описывает реальный тип.
  **где:** Assets/_Project/Scripts/Data/Definitions/GameConfig.cs (все поля) + Assets/_Project/Scripts/Guild/RunStateService.cs (NewRun/NewDefaultRun читают StartGold, RelicCapacityBase, GuildSize, StartingRelicId)

#### `20-explanation/effects-abilities.md` — update, 63%

*Ядро модели (композиция stateless-компонентов, шов Data↔Combat, снимок потенции, целочисленная периодика, FIFO-очередь с капом 512) верно. Но семейство интерфейсов выросло на два (IStackableComponent, IPreDamageComponent), правило рестака больше не «всегда OnExpire→OnApply», а §6 про AbilitySystem описывает систему, которой уже нет: там условия каста, паника по HP, круговое AOE, масс-каст по тегу, аура по союзникам и рывок-смещение.*

- знач. — **док:** §2, таблица «Семейство интерфейсов компонентов» перечисляет ровно четыре: IRuntimeEffectComponent, IPeriodicComponent, IReactiveComponent, IScalablePotency
  **код:** В файле шесть контрактов: добавились IStackableComponent (OnStacksChanged — компонент с накопленным внешним состоянием сам правит вклад дельтой) и IPreDamageComponent (OnPreDamage — синхронный перехват до вычета HP, может негейтить удар через PreDamageResult).
  **где:** Assets/_Project/Scripts/Combat/Effects/IRuntimeEffectComponent.cs — IStackableComponent, IPreDamageComponent, PreDamageResult
- знач. — **док:** §4.3: «При изменении числа стаков зовётся Reapply (OnExpire→OnApply), чтобы stateful-компоненты пересчитали вклад под новый Stacks»
  **код:** Reapply теперь ветвится: если компонент реализует IStackableComponent — зовётся OnStacksChanged(previousStacks, ctx) и слепого OnExpire→OnApply НЕ происходит; дефолт остался только для прочих (keyed-снятие, StatModifierComponent). Причина в коде: слепой рестак пере-вычитал щит и бесплатно перезаряжал заряды.
  **где:** Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs — EffectSystem.Reapply(), строки 423-449
- **КРИТ** — **док:** §6: «AbilitySystem — плейсхолдер: кастуй первую готовую активку, если можешь; накладывает эффекты на цель (Self/NearestEnemy/NearestAlly)»
  **код:** Плейсхолдерным остался только триггер «первая готовая за тик». Всё остальное разрослось: режимы цели Self/NearestEnemy/NearestAlly/LowestHpAlly/AllEnemiesWithTag/AlliesInRadius; гейт CastCondition (EnemiesInRadius / AllyTargetHpBelowPct / EnemiesWithTagCount / Immediately); паника CastOverrideSelfHpPct (разворот хила на себя); ветки исполнения ApplyDisplace (рывок монаха), ApplyAllyAura, ApplyAllWithTag (масс-каст по тегу + ConsumesTriggerTag), ApplyCircle (AOE), ApplyToTarget; расчёт урона/хила (DamageMultiplier × AutoAttackDamage, HealFlat + HealPctTargetMissingHp); событие OnAbilityCast; гейт «в полёте» DisplacedTicksRemaining.
  **где:** Assets/_Project/Scripts/Combat/Abilities/AbilitySystem.cs — Tick(), TryCast(), CastConditionMet(), ApplyDisplace(), ApplyAllWithTag(), ApplyAllyAura(), ApplyCircle(), ResolveTarget()
- знач. — **док:** §3, снимок RuntimeEffect: Def, Source, RemainingTicks, Stacks, ScaledPotency, PeriodicTicks
  **код:** К ним добавились FullDurationTicks (для StackRule.Refresh), ReactiveReadyTick (внутренний КД реактива/pre-damage, абсолютный тик), PendingShield (фактически поднятая величина щита для корректного снятия), ChargeReadyTicks (заряды реактива с независимой перезарядкой). Плюс RuntimeEffect реализует IModifierSource (ModifierSourceLocKey) — он же источник в разборе стата для тултипа.
  **где:** Assets/_Project/Scripts/Combat/Effects/RuntimeEffect.cs — поля FullDurationTicks, ReactiveReadyTick, PendingShield, ChargeReadyTicks; свойство ModifierSourceLocKey
- знач. — **док:** §5: реактивность = DamageDealt/DamageTaken (+ Healed после правки ⑥); больше событий не названо
  **код:** Очередь несёт также UnitKilled (доставляется убийце), UnitDied (доставляется даже мёртвому носителю — перенос «Метки охотника») и EffectExpired (единый шов «эффект закончился», носитель-получатель = источник эффекта; на нём держатся реактивы монаха и завершение полёта смещения). Кроме того, у каждого события урона есть DamageSourceKind — гейт «прямой удар vs тик DoT vs ответка реактива», чтобы шипы не порождали шипы.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs — DealDamage()/DrainEventQueue() и подписка на EffectSystem.OnEffectExpired в конструкторе; Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs — enum DamageSourceKind, IsDirectHit
- знач. — **док:** Документ не упоминает механику смещения как эффект
  **код:** Смещение (отбрасывание/рывок) реализовано ЧЕРЕЗ систему эффектов: CombatSimulation.Displace вешает на цель системный runtime-эффект sys.airborne (Neutral, теги KnockUp/Control, unremovable, ControlComponent с полным запретом), а конец полёта снимает его через EffectSystem.RemoveByTag → Expire → OnEffectExpired. Метода RemoveByTag (принудительное снятие по тегу в обход Unremovable) в документе тоже нет.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs — поле _airborneEffect, метод Displace(), подписка OnDisplacementEnded; Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs — RemoveByTag()

#### `40-planning/sfx.md` — update, 65%

*Числится как «ТЗ к исполнению», а исполнено уже почти всё: FMOD-сервис включён, презентер зарегистрирован, банки собраны, enum действий расширен до полного набора, каталог наполнен. Раздел «что уже есть в проекте» описывает мёртвое прошлое и активно дезинформирует.*

- **КРИТ** — **док:** §1.1: «AudioPresenter — НЕ зарегистрирован ни в одном scope — инертен»; «UnityAudioService — стаб (Debug.Log), заменяется на FMOD-импл»; «Регистрация в DI — RootLifetimeScope.cs:38, тут свапается импл»
  **код:** Свап выполнен и презентер включён: в корневом скоупе зарегистрирован FmodAudioService как IAudioService, в боевом — AudioPresenter как entry point.
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs:74; Assets/_Project/Scripts/Game/CombatLifetimeScope.cs:69 (builder.RegisterEntryPoint<AudioPresenter>(Lifetime.Scoped))
- **КРИТ** — **док:** §1.2: «FMOD Studio проект… пустой (только каркас, Master-банки без событий). Assets/StreamingAssets — пустой, банки в Unity не попадают»
  **код:** Банки собраны и лежат в проекте: Master.bank, Master.strings.bank и SFX.bank — то есть пакеты П1–П2 закрыты.
  **где:** ls Assets/StreamingAssets/ → Master.bank, Master.strings.bank, SFX.bank
- знач. — **док:** §2.2/§1.1: «AudioAction enum: Attack, Hit, Death, Cast, Ui — расширяется в П4»; «AudioCatalog… пустой, заполняется в П5»
  **код:** Пакеты П4–П5 исполнены: enum содержит все 13 запланированных действий (добавлены Fire, Evade, Shield, Heal, Apply, Expire, Tick, Stinger), каталог наполнен записями.
  **где:** Assets/_Project/Scripts/Presentation/Audio/AudioAction.cs (13 членов, Attack…Stinger); Assets/_Project/ScriptableObjects/Audio/AudioCatalog.asset (256 строк, 17 ключей)
- **КРИТ** — **док:** Шапка: «ТЗ к исполнению (объём утверждён Максом 2026-07-13)», frontmatter status: needs_review
  **код:** ТЗ по существу исполнено (П1–П5 закрыты). Документ стоит переводить в архив исполненного ТЗ, оставив открытым только хвост П6/П7 (микс, добивка ◇-ключей, приёмочные тесты каталога).
  **где:** совокупно: FmodAudioService.cs, CombatLifetimeScope.cs:69, StreamingAssets/*.bank, AudioAction.cs

#### `20-explanation/index.md` — update, 66%

*Все вики-ссылки резолвятся в существующие файлы, тик-ордер в схеме потока данных совпадает с кодом. Но оглавление кластера потеряло run-flow.md, диаграмма сборок отстала на шесть asmdef, а блок «Сцены» описывает снесённую модель «грузим BattleScene на бой» вместо persist-мира.*

- знач. — **док:** Таблица «Документы раздела» перечисляет 6 документов кластера (00 index, 01 di-events, 02 simulation, 03 data-stats-damage, 04 effects-abilities, 05 presentation, 06 netcode) + ссылку на changelog
  **код:** В каталоге 20-explanation лежит ещё run-flow.md, не упомянутый в MOC вообще. Для оглавления кластера это дыра: документ существует, но с лендинга недостижим.
  **где:** docs/wiki/tech/20-explanation/run-flow.md (файл присутствует в кластере; в таблице index.md строки нет)
- знач. — **док:** Блок «Слои и сборки»: Core → Data → Combat → Net → Presentation → Game, «зависимости идут строго вниз»
  **код:** В проекте 18 asmdef. Помимо перечисленных шести есть Guildmaster.UI, Guildmaster.Guild, Guildmaster.MiniGames, Guildmaster.Balance (+ Balance.Editor), Guildmaster.DevTools и четыре редакторных (Data.Editor, Game.Editor, ContentHub.Editor, PaletteRemap.Editor, UI.Editor, Audio.Editor). Схема в MOC не отражает даже UI-слой, который в проекте один из крупнейших.
  **где:** Assets/_Project/Scripts/**/*.asmdef — Guildmaster.UI.asmdef, Guildmaster.Guild.asmdef, Guildmaster.MiniGames.asmdef, Guildmaster.Balance.asmdef, Guildmaster.DevTools.asmdef и др.
- знач. — **док:** «Поток данных»: [Сцены] SceneLoader грузит BattleScene аддитивно к persistent CoreScene
  **код:** Persist-мир: на буте один раз грузятся ОБЕ сцены — сначала персистентная WorldScene (камера-риг + арена), затем BattleScene, и обе живут всю сессию; бой запускается командой в живой sim, а не загрузкой сцены на узел. Дефолтный вход — RunGameAsync (главное меню → забег); старое поведение осталось под флагом _legacyBattleScene.
  **где:** Assets/_Project/Scripts/Game/GameBootstrap.cs — StartBootAsync() (LoadWorldAsync → LoadBattleAsync → RunGameAsync); Assets/_Project/Scripts/Game/Flow/BattleFlow.cs — комментарий «боевой скоуп уже жив, BattleScene загружена на буте и не выгружается»
- знач. — **док:** Карта классов, слой Game: «GameFlow — Макро-флоу: Boot → BattleScene → результат»
  **код:** GameFlow сейчас — вход в несколько флоу: RunGameAsync (меню→забег→меню), RunActAsync (весь акт по узлам), RunTextEventAsync, RunSingleBattleAsync, BootAsync (legacy). Плюс появился целый каталог Game/Flow (~20 классов: RunFlow, BattleNodeFlow, ShopFlow, CampFlow, ChestFlow, RandomEventFlow, WorldMapController, RewardService, RunBeatStage и др.), которого в карте классов нет.
  **где:** Assets/_Project/Scripts/Game/Services/GameFlow.cs — RunGameAsync/RunActAsync/RunTextEventAsync/RunSingleBattleAsync/BootAsync; каталог Assets/_Project/Scripts/Game/Flow/

#### `10-reference/combat-model.md` — rewrite_section, 68%

*§6 и §6.1 (пайплайн урона, школа vs сродство, MagicElement/PhysicalSubtype, DamageCategories.Resolve, AffinityTable, CreatureType) сверены с кодом и совпадают ПОЛНОСТЬЮ — недавняя переделка на поисточниковую модель в док доехала. Зато остальное отстало: §4 (визуал) описывает несуществующий пайплайн на Addressables и рантайм-сборке AnimatorOverrideController; §5.6 описывает класс ImmunityComponent, которого в коде нет; §5.1 показывает хуки на IEffectComponent, хотя в Data это пустой маркер; §3 не знает про четвёртую операцию ModifierOp.Override и про классовый/видовой слои каскада; §10 «бэклог» на 7 строк из 8 уже реализован.*

- **КРИТ** — **док:** §4: «Визуальные ссылки — на RelicData через Addressables AssetReference (спрайт-шит, override-контроллер, портрет)… При спавне CombatPresenter читает RuntimeUnit.Relic → грузит override + спрайт через Addressables → вешает на Animator»
  **код:** Addressables в игровом коде не используется вовсе (ни одного AssetReference/Addressables. в Assets/_Project/Scripts). Рантайм-подмены контроллера нет: UnitView.InitVisual прямо документирует «Animator уже несёт контроллер с клипами персонажа — рантайм-подмены больше нет», из UnitVisual берутся только маркер контакта и темп бега. Префаб — не общий: CombatPresenter.CreateView берёт unit.Unit.ViewPrefab и лишь фолбэком — общий _unitViewPrefab
  **где:** Assets/_Project/Scripts/Presentation/UnitView.cs:212-243 (UnitView.InitVisual); Assets/_Project/Scripts/Presentation/CombatPresenter.cs:235-237; Assets/_Project/Scripts/Data/Definitions/UnitData.cs:46 (_viewPrefab)
- **КРИТ** — **док:** §5.6: «ImmunityComponent : IEffectComponent … BlockPolarity/BlockTags/BlockDamage; пока активен, ICombatContext.ApplyEffect отвергает входящие эффекты; BlockDamage проверяется в пайплайне урона до митигации»
  **код:** Класса ImmunityComponent в коде нет (grep по ImmunityComponent/BlockPolarity/BlockDamage — ноль совпадений). В Combat/Effects/Components 20 компонентов, иммунитета среди них нет; DamagePipeline.Execute никакого BlockDamage не проверяет
  **где:** Assets/_Project/Scripts/Combat/Effects/Components/ (список файлов); Assets/_Project/Scripts/Combat/Damage/DamagePipeline.cs (Execute)
- знач. — **док:** §5.1: «public interface IEffectComponent { void OnApply(in EffectContext ctx); void OnExpire(in EffectContext ctx); }» + IPeriodicComponent/IReactiveComponent как единственные производные
  **код:** IEffectComponent в Guildmaster.Data — ПУСТОЙ маркер сериализации; хуки живут в Combat в IRuntimeEffectComponent : IEffectComponent. Производных больше: IStackableComponent, IPreDamageComponent (+PreDamageResult), IScalablePotency, помимо IPeriodicComponent/IReactiveComponent
  **где:** Assets/_Project/Scripts/Data/Definitions/IEffectComponent.cs (интерфейс без членов); Assets/_Project/Scripts/Combat/Effects/IRuntimeEffectComponent.cs
- знач. — **док:** §3: «public enum ModifierOp { Flat, PercentAdd, PercentMult }», формула final = (base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult); StatModifier несёт поле object Source
  **код:** Операций четыре: добавлена Override = 3, которая ЗАМЕНЯЕТ базовый терм (основной способ авторинга базовых статов на SO), формула — baseTerm = Override(если задан) иначе дефолт StatsConfig. Поля Source в StatModifier нет: источник — ключ группировки в Stats.AddModifiersFrom/RemoveModifiersFrom
  **где:** Assets/_Project/Scripts/Data/Stats/ModifierOp.cs (Override = 3); Assets/_Project/Scripts/Data/Stats/StatModifier.cs; Assets/_Project/Scripts/Combat/Stats/Stats.cs:41,131
- знач. — **док:** §3 «Сборка юнита»: base ← StatsConfig → + модификаторы Реликвии → + Перков → Предметов → Командных артефактов → (опц.) гильдие-широкие
  **код:** В каскаде есть два не описанных слоя между StatsConfig и реликвией: классовая база (ClassBalanceConfig, уровень 2) и видовые/подвидовые скейлы врага (SpeciesData, уровни 3–4). Реальный порядок: StatsConfig → класс → вид → моды кита → перки сосуда → предметы/баннеры → пассивки → активки → ресурс → CurrentHP
  **где:** Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs:10-23 (докблок) и Create() строки 62-112
- знач. — **док:** §10 «Движковые расширения под контент реликвий (бэклог)» — таблица из 8 строк, всё помечено как нужное к Ф3 (атака в движении, хил-автоатака, форма авто-атаки, смещения, подкидывание, перенос метки, empower next attack, заряды способности)
  **код:** 7 из 8 уже в коде: UnitData.CanAttackWhileMoving; AutoAttackMode.Heal; UnitData.AutoAttackShape (AreaShape); ICombatContext.Displace + DisplaceRequest; EffectTag.KnockUp; MarkTransferComponent; RuntimeUnit.EmpowerDamageMult. Не реализованы только заряды способности (в AbilityData/AbilitySystem нет ни одного Charge)
  **где:** Assets/_Project/Scripts/Data/Definitions/UnitData.cs:55,65; Assets/_Project/Scripts/Data/Definitions/AIProfile.cs (AutoAttackMode); Assets/_Project/Scripts/Combat/ICombatContext.cs:62; Assets/_Project/Scripts/Data/Definitions/EffectTag.cs:31; Assets/_Project/Scripts/Combat/Effects/Components/MarkTransferComponent.cs; Assets/_Project/Scripts/Combat/Units/RuntimeUnit.cs:61

#### `20-explanation/presentation.md` — update, 70%

*Документ не столько врёт, сколько показывает слой образца июня: мост, интерполяция и техдолг alpha — всё верно и живо. Но за месяц Presentation оброс VFX-префабным швом, feel-конфигом, пулом цифр, скелетной анимацией через Animator и целыми подпространствами Map/Transition/Tempo/Effects — из них в тексте нет ни одного. Статус ready на таком покрытии не держится.*

- знач. — **док:** §2, список «Что делает CombatPresenter»: подписывается на OnUnitSpawned, OnUnitDied, OnDamageDealt, OnBattleEnded и ретранслирует КАЖДОЕ событие в MessagePipe
  **код:** Подписок десять: плюс OnHealed, OnAttackEvaded, OnAttackStarted, OnAttackInterrupted, OnProjectileSpawned, OnBattleReset. В MessagePipe уходят по-прежнему только четыре (UnitSpawnedEvent, UnitDiedEvent, DamageDealtEvent, BattleEndedEvent) — «каждое событие» неверно, и именно эта асимметрия объясняет, почему свинг/хил/снаряды обрабатываются прямым вызовом во вью, а не через шину.
  **где:** Assets/_Project/Scripts/Presentation/CombatPresenter.cs (OnEnable/OnDisable, Handle*); Assets/_Project/Scripts/Presentation/Events/CombatEvents.cs (ровно 4 struct-сообщения)
- знач. — **док:** Документ описывает превращение состояния в картинку и нигде не упоминает VFX-слой
  **код:** VFX — теперь отдельный шов и главный неописанный кусок: CombatVfx держит пул ObjectPool<PooledVfx> по префабам, спавнит по VfxData (Prefab + SortingLayerName + SortingOrder + Scale + DefaultDirDeg), умеет DespawnAll на сброс боя. Точки спавна заданы в CombatFeelConfig (VfxHitSpark, VfxMuzzle, VfxImpactDust, VfxContactDust, VfxHeal) и дёргаются из CombatPresenter на попадание, выстрел, мили-удар, лечение и contact-dust. Прежнего процедурного PixelBurst в коде НЕТ ни одного вхождения — переезд на префабы состоялся.
  **где:** Assets/_Project/Scripts/Presentation/CombatVfx.cs (Spawn/GetOrCreatePool/DespawnAll); Assets/_Project/Scripts/Presentation/PooledVfx.cs; Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs (Vfx* свойства, строки 313-317); Assets/_Project/Scripts/Presentation/CombatPresenter.cs (HandleProjectileSpawned, HandleDamageDealt, HandleHealed, OnUnitContactDust)
- знач. — **док:** «Тело можно сделать толстым и юнитёвым (Feel-хуки, партиклы, твины LitMotion)» — единственное упоминание джуса, без единого имени класса
  **код:** Джус давно оформлен в два слоя с разной пропиской, и документ не даёт читателю их различить. Локальный per-hit фидбэк (hitstop пары источник/цель, вспышка по школе урона, nudge, lunge, размер цифры, HoldHitFrame финишера) считает CombatPresenter по CombatFeelConfig — SO с ~60 публичными параметрами и кривыми (EvaluateHitstopSeconds, EvaluateHitVfxIntensity, ResolveHitFlashColor). Глобальная политика значимости (slowmo на килл и на конец боя, тряска) вынесена в CombatFeelDirector, который живёт в Guildmaster.Game и слушает MessagePipe-события DamageDealtEvent/BattleEndedEvent, а не симуляцию.
  **где:** Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs; Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs (ISubscriber<DamageDealtEvent>, ISubscriber<BattleEndedEvent>); Assets/_Project/Scripts/Game/CombatLifetimeScope.cs (RegisterEntryPoint<CombatFeelDirector>)
- знач. — **док:** Про анимацию юнита в документе нет ничего — раздел 3 обсуждает только интерполяцию позиции
  **код:** Скелетная/клиповая анимация — рабочий слой. UnitView гоняет Animator по стейтам Idle/Run/Attack/Death (StringToHash), клипы подставляются AnimatorOverrideController'ом из UnitVisual (Clip(state), AttackClip, HitClip, SkillClip, AttackFrameCount/AttackHitFrame через ClipMarkers). Выбор состояния вынесен в чистую функцию UnitAnimationSelector.Select(isDead, attackPlaying, isMoving) + AttackClipPlaying(...) — без Unity-типов, специально чтобы проверяться EditMode-тестом и не вносить рассинхрон. Это ровно тот «read-only» инвариант, который документ провозглашает, но не показывает на самом интересном месте.
  **где:** Assets/_Project/Scripts/Presentation/UnitAnimation.cs (UnitAnimationSelector); Assets/_Project/Scripts/Presentation/UnitView.cs (поле _animator, IdleHash/RunHash/AttackHash/DeathHash, ApplyVisual); Assets/_Project/Scripts/Data/Definitions/UnitVisual.cs

#### `30-how-to/adding-assets.md` — update, 70%

*Порядок заводки ассетов и правило «ассет = данные в SO» по-прежнему верны, Git LFS действительно не настроен. Но фазовые оговорки про аудио/локализацию/Addressables отстали от кода на несколько фаз: FMOD уже работает, локализация уже подключена.*

- знач. — **док:** «Сейчас в Фазе 1 — Unity Audio за интерфейсом IAudioService (FMOD позже, Фаза 3)», далее весь Шаг 4 — про import-настройки Unity Audio (Load Type / Compression / Force To Mono)
  **код:** Аудио идёт через FMOD: в DI зарегистрирован FmodAudioService, банки Master.bank / Master.strings.bank / SFX.bank лежат в Assets/StreamingAssets. Импорт-настройки Unity Audio к боевому звуку больше не применяются — звук авторится в FMOD Studio.
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs:74 (builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>()); Assets/_Project/Scripts/Game/Services/FmodAudioService.cs; Assets/StreamingAssets/SFX.bank

#### `10-reference/arena.md` — update, 70%

*Ядро (модель геометрии, правило CanPlace, кламп симуляции) сходится с кодом. Разъехались: слой хранения/загрузки арены (Addressables-boot-flow не существует, арена живёт в persist WorldScene) и статус-шапка (фаза расстановки как UI давно реализована в DeploymentController).*

- знач. — **док:** §5 Boot-flow боя: «EncounterData -> ключ арены (arena_forest, Addressables) -> Addressables.LoadAssetAsync(key) -> Instantiate -> ArenaLayoutAuthoring», после боя «Addressables.Release + Destroy инстанса арены»
  **код:** Ничего из этого в коде нет: grep по Assets/_Project/Scripts не находит ни LoadAssetAsync, ни arena_forest, ни поля ключа арены. Снапшот печётся из ArenaLayoutAuthoring, найденного в уже загруженных сценах (persist WorldScene), и живёт всю сессию — ни загрузки по ключу, ни выгрузки после боя.
  **где:** Assets/_Project/Scripts/Game/WorldLifetimeScope.cs — WorldLifetimeScope.BuildArenaLayout() (FindFirstObjectByType<ArenaLayoutAuthoring>() + authoring.BuildLayout()); Assets/_Project/Scripts/Game/CombatLifetimeScope.cs — CombatLifetimeScope.BuildArenaLayout()
- знач. — **док:** §3 «Модель данных»: ArenaLayoutData = { ArenaBounds Bounds; IReadOnlyList<DeploymentZone> Zones; }
  **код:** У ArenaLayoutData три члена: третий — Rect2D CameraZone (зона клампа видимой области камеры, необязательный ctor-параметр, дефолт = bounds.Rect). Он же авторится отдельной жёлтой рамкой и является тем, чем реально клампится камера.
  **где:** Assets/_Project/Scripts/Core/Arena/ArenaLayoutData.cs — ArenaLayoutData.CameraZone + ctor(bounds, zones, cameraZone); Assets/_Project/Scripts/Game/ArenaLayoutAuthoring.cs — BuildLayout()
- знач. — **док:** Статус-шапка: «Шаг 4 ... drag-drop UI, сетевые команды расстановки и визуальная проверка — за Максом»; §10 «Осознанно НЕ сделано: фаза расстановки как UI (drag-drop) ... — отдельная задача; здесь только правило CanPlace + данные»
  **код:** Фаза расстановки как интерактивный UI реализована: пикинг по радиусу тела, drag с валидацией DeploymentService.CanPlace, анти-оверлап при drop, дабл-клик → OpenLoadoutRequest, drag реликвии из инвентаря на юнита, тест-зона, свободная камера расстановки. Не сделаны только сетевые (host-authoritative) команды — это прямо помечено в самом коде.
  **где:** Assets/_Project/Scripts/Game/DeploymentController.cs — класс DeploymentController (IStartable/ITickable), докстринг + ctor; Assets/_Project/Scripts/Presentation/DeploymentView.cs — DeploymentView.Init(ArenaLayoutData)
- знач. — **док:** §10 шаг 4: «CombatLifetimeScope ищет ArenaLayoutAuthoring через FindFirstObjectByType (сцена-авторинг работает сегодня)»
  **код:** Владелец снапшота арены переехал в персистентный WorldLifetimeScope (WorldScene грузится аддитивно и не выгружается); боевой скоуп стал дочерним и резолвит арену/камеру из предка. Метод BuildArenaLayout в CombatLifetimeScope при этом остался — в коде два места, строящих layout.
  **где:** Assets/_Project/Scripts/Game/WorldLifetimeScope.cs — WorldLifetimeScope.Configure() (builder.RegisterInstance(layout)); Assets/_Project/Scripts/Game/CombatLifetimeScope.cs — RegisterArena()/BuildArenaLayout()

#### `10-reference/ui-navigation.md` — update, 75%

*Каркас реформы (стек, ScreenKind, ввод как функция, единственный писатель контекста) держится и подтверждается кодом. Устарели три вещи: карта стала прозрачным Sheet в мире, слоёв девять (добавлена шторка перехода), а тултипы из «задела» стали работающей системой.*

- **КРИТ** — **док:** §6 «Модель пространств»: «Карта — единственный непрозрачный оверлей (Page), закрывает мир»
  **код:** Карта акта переехала в мир: MenuRouter.ShowMapSpace пушит ПРОЗРАЧНЫЙ RouterScreen(ScreenKind.Sheet, BuildMapSpace, modeTag: UiScreen.MapModeTag) — окно в мир, которое не рисует контента и не ловит клики, а сама карта рисуется WorldMapView в persist-сцене; фон забега при этом гасится (IsMapSpaceOpen). Непрозрачные Page — это ивент/магазин/награда/исход/главное меню.
  **где:** Assets/_Project/Scripts/UI/MenuRouter.cs — ShowMapSpace()/HideMapSpace()/IsMapSpaceOpen, BuildMapSpace(); Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs
- знач. — **док:** §3: «UiRootBootstrap создаёт ... восемь слоёв-контейнеров», список [0]–[7] с layer-system последним
  **код:** Слоёв девять: поверх layer-system добавлен layer-transition — самый верхний, в него кладётся элемент screen-fade (шторка перехода между узлами, обязана накрывать и топбар, и модалки).
  **где:** Assets/_Project/Scripts/UI/UiRootBootstrap.cs — BuildLayers() (AddLayer(root, "layer-transition") + _screenFade); Assets/_Project/Scripts/Presentation/Transition/ScreenTransitionRunner.cs
- знач. — **док:** §3: «Слои [5]–[7] создаются как заделы (ссылки на них пока не хранятся — наполнятся при реализации курсоров/тултипов/тостов)»; §8: «Тултипы (Трек Т) — слой layer-tooltip готов; система несущая для жанра»
  **код:** Тултип-слой уже не задел: ссылка хранится в поле _layerTooltip, а в Start() к нему привязывается работающая система TooltipSystem.Attach(root, _layerTooltip) с задержкой показа, grace-окном и живым рефрешем. Заделами остались только layer-cursors и layer-system.
  **где:** Assets/_Project/Scripts/UI/UiRootBootstrap.cs — поле _layerTooltip, _tooltips?.Attach(...); Assets/_Project/Scripts/UI/Tooltips/TooltipSystem.cs — class TooltipSystem (DelayMs/GraceSeconds/RefreshMs)
- знач. — **док:** §4: формула ввода — «modal → Menu, иначе WorldContextOf(clock.Phase); WorldContextOf: Deployment → Deployment; Fighting → Combat; None → None»
  **код:** В SyncInput появилась третья ветка между ними: если верх стека несёт ModeTag == UiScreen.MapModeTag, ставится InputContext.Map (world-камера карты жива, боевых действий нет). А WorldContextOf отображает в Combat не только Fighting, но и Interlude (передышка между узлами).
  **где:** Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs — SyncInput(), WorldContextOf()

#### `10-reference/editor-tools.md` — update, 75%

*Главное правило дока верно: все 21 [MenuItem] в Assets/_Project действительно под корнем Alebardium/. Но раскладка меню отстала на две целые группы (Test, Visuals), и заявленная схема приоритетов сотнями уже нарушена коллизией 500-502.*

- знач. — **док:** Таблица «Раскладка меню» перечисляет 14 пунктов и заканчивается группой Data (400–422)
  **код:** В коде есть ещё пять пунктов, которых в таблице нет: Alebardium/Test/Build & Run (Windows, fullscreen) [500], Alebardium/Test/Toggle Maximized Game View %#g [501], Alebardium/Visuals/Build Per-Unit View Prefabs [500], Alebardium/Visuals/Audit Unit Animations [501], Alebardium/Visuals/Export Unit Visual Catalog [502].
  **где:** Assets/_Project/Scripts/EditorTools/UI/TestPlayMenu.cs — [MenuItem] x2; Assets/_Project/Scripts/EditorTools/ContentHub/BuildUnitViewPrefabs.cs, AuditUnitAnimations.cs, ExportUnitVisualCatalog.cs
- знач. — **док:** «Как раздавать приоритеты новым пунктам»: диапазоны 0–99/100–199/200–299/300–399/400–499, «Новая группа — следующая свободная сотня»
  **код:** Правило уже нарушено в коде: группы Test и Visuals обе заняли 500–502 (Build & Run 500 и Build Per-Unit View Prefabs 500; Toggle Maximized 501 и Audit Unit Animations 501). Таблица диапазонов обрывается на 400–499 и про 500+ молчит, так что дока не описывает и не защищает фактическую раскладку.
  **где:** Assets/_Project/Scripts/EditorTools/UI/TestPlayMenu.cs — priority = 500/501; Assets/_Project/Scripts/EditorTools/ContentHub/BuildUnitViewPrefabs.cs — priority = 500; AuditUnitAnimations.cs — priority = 501; ExportUnitVisualCatalog.cs — priority = 502

#### `10-reference/saves.md` — update, 75%

*Главное утверждение — живой бэкенд JsonFileSaveService (JsonUtility, persistentDataPath) за интерфейсом ISaveService, ES3 не подключён — подтверждается кодом дословно. Дрейф в описании DTO-слоя: названных типов RunSaveData/UnitSaveData не существует, роль save-DTO играет RunState из Guildmaster.Guild. Список точек автосейва беднее реального.*

- знач. — **док:** «Сохраняем рантайм-состояние через слой DTO (RunSaveData, UnitSaveData), а не объекты сцены»
  **код:** Типов RunSaveData и UnitSaveData в проекте нет. Save-DTO — сам durable-класс забега RunState ([Serializable], поле SchemaVersion = 1, всё по строковым content id) вместе с вложенными RosterSlot, MapState, MapNode; в его докстринге прямо сказано «плоский [Serializable] = сам себе save-DTO… сплит на отдельный DTO не нужен». Сохраняется одним ключом "run" через RunStateService.
  **где:** Assets/_Project/Scripts/Guild/RunState.cs (classes RunState / RosterSlot / MapState / MapNode) + Assets/_Project/Scripts/Guild/RunStateService.cs (const SaveKey = "run", Load/Autosave)
- знач. — **док:** «Точки автосохранения: 1) в самом начале забега 2) в начале ивента 3) перед получением наград»
  **код:** Реальных вызовов RunStateService.Autosave() десять и они расставлены иначе: после генерации карты акта (GameFlow), после узла (GameFlow), после продвижения по карте и при поражении (ActRunner), после награды (RewardPresenter), после исхода текстового ивента (EventEffectApplier), после покупки/продажи/реролла в магазине (ShopController ×3), после расстановки (DeploymentController). Сейва строго «перед получением наград» нет — RewardPresenter сохраняет после выдачи.
  **где:** Assets/_Project/Scripts/Game/Services/GameFlow.cs:111,180 · Assets/_Project/Scripts/Game/Services/ActRunner.cs:113,122 · Assets/_Project/Scripts/Game/Flow/RewardPresenter.cs:69 · Assets/_Project/Scripts/Game/Flow/EventEffectApplier.cs:28 · Assets/_Project/Scripts/Game/Flow/ShopController.cs:82,92,104 · Assets/_Project/Scripts/Game/DeploymentController.cs:687

#### `10-reference/tech-stack.md` — update, 75%

*Архитектурные принципы, чек-лист детерминизма и особенно раздел про AI-пресеты (AIProfile/TargetingMode/ProfileBrain/BrainSystem, 30 Гц + 10 Гц со стаггером) совпадают с кодом почти дословно. Устарела фазировка: FMOD и Unity Localization описаны как «подключить на стадии 3–4», хотя оба уже живут в коде. Таблицы стека неполны — в них нет половины реально установленных пакетов (Cinemachine, Input System, Shapes, Quantum Console, Feel, ProBuilder, VFX Graph, 2D/Aseprite).*

- знач. — **док:** «Фаза 3 — Полировка: FMOD … До этого — Unity Audio за интерфейсом IAudioService» и в дорожной карте «FMOD ✅ Установлен / Подключать на стадии 4»
  **код:** FMOD уже подключён и является боевой реализацией: builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>() в композиционном корне; есть Presentation/Audio/AudioCatalog и редакторный AudioCatalogPopulator
  **где:** Assets/_Project/Scripts/Game/RootLifetimeScope.cs:74 (Configure); Assets/_Project/Scripts/Game/Services/FmodAudioService.cs
- знач. — **док:** Локализация: «Пакет интегрировать на стадии 2–3»; в дорожной карте «таблицы — стадия 2–3»
  **код:** Пакет интегрирован: есть Game/Services/LocalizationService.cs (UnityEngine.Localization.Settings), StatValueFormatter на SmartFormat, редакторный Data/Editor/ContentLocalization.cs; Unity.Localization в references у Guildmaster.UI и Guildmaster.Game
  **где:** Assets/_Project/Scripts/Game/Services/LocalizationService.cs; Assets/_Project/Scripts/Data/Editor/ContentLocalization.cs; Assets/_Project/Scripts/UI/Guildmaster.UI.asmdef
- знач. — **док:** Таблицы «Технологический стек (фазированно)» и «Дорожная карта внедрения» перечисляют стек проекта
  **код:** В таблицах нет реально установленных и используемых Cinemachine 3.1.7, Input System 1.19.0, Shapes, Quantum Console (QFSW.QC), Feel/MMTools, а также установленных ProBuilder 6.1.2, VFX Graph 17.4.0 и 2D-пакетов (Aseprite 4.0.2, PSD Importer, 2D Animation). Cinemachine/Input System/Shapes/QC — прямые references рантайм-сборок
  **где:** Packages/manifest.json; Assets/_Project/Scripts/Presentation/Guildmaster.Presentation.asmdef (ShapesRuntime, Unity.Cinemachine); Assets/_Project/Scripts/DevTools/Guildmaster.DevTools.asmdef (QFSW.QC, Unity.InputSystem)

#### `20-explanation/simulation.md` — update, 78%

*Главное — тик-ордер — СОВПАДАЕТ с кодом один-в-один (проверено по CombatSimulation.Tick). Дрейф в деталях: контракт ICombatContext в §3.2 устарел на треть методов, порядок сборки юнита в §4 не знает про стат-каскад класса/вида и предметы, а §3.1 не упоминает windup/recovery авто-атаки, каденс мозга 10 Гц и pre-damage перехват. Персист-мировой API (SetArena/ResetBattle/FlushSpawns) и снапшот SimTuning в документе отсутствуют.*

- знач. — **док:** §3.2 приводит контракт ICombatContext: DealDamage, Heal, SpawnProjectile, QueryUnitsInRadius, ApplyEffect, Dispel, Rng/CurrentTick/ArmorK
  **код:** В интерфейсе сейчас 11 членов + 4 свойства: добавились QueryUnitsInLine (линейные авто-атаки), Displace (смещение), ReportAreaHit (dev-оверлей зон), NotifyAttackStarted/NotifyAttackInterrupted (замах, вики «14») и свойство SimTuning Tuning (снапшот балансного тюнинга). Читатель, опирающийся на документ, не увидит половину шва.
  **где:** Assets/_Project/Scripts/Combat/ICombatContext.cs — interface ICombatContext (QueryUnitsInLine, Displace, ReportAreaHit, NotifyAttackStarted, NotifyAttackInterrupted, Tuning)
- знач. — **док:** §4: «Создаётся RuntimeUnit только через RuntimeUnitFactory (единая точка сборки из SO: дефолты статов → моды реликвии → таланты → пассивки → способности → CurrentHP = MaxHP)»
  **код:** Порядок сборки теперь: дефолты StatsConfig → классовая база (ClassBaseline.Apply, ClassBalanceConfig) → видовые/подвидовые скейлы врага (EnemyScalers.Apply) → стат-блок кита (UnitData.Stats) → перки сосуда (VesselData.PerkModifiers) → моды предметов/баннеров (ItemData.Mods) → пассивки кита → пассивки предметов → активки → CurrentResource=StartResource → CurrentHP. Двух уровней каскада (класс, вид) и слоя предметов в документе нет вовсе.
  **где:** Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs — RuntimeUnitFactory.Create(); Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs — ClassBaseline.Apply(); Assets/_Project/Scripts/Combat/Stats/EnemyScalers.cs — EnemyScalers.Apply()
- знач. — **док:** §3.1: «AutoAttack — атаковать/спавнить снаряды по кулдауну»
  **код:** Авто-атака — конечный автомат из трёх фаз (Idle/Windup/Recovery) с кадром контакта, прерыванием замаха и прощающим буфером досягаемости; сам документ ниже (§5) уже ссылается на WindupRemaining/RecoveryRemaining в checksum, но §3.1 и §4 об этой механике молчат.
  **где:** Assets/_Project/Scripts/Combat/Systems/AutoAttackSystem.cs — AutoAttackSystem.Tick/Resolve/Interrupt (unit.Phase, WindupRemaining, RecoveryRemaining); Assets/_Project/Scripts/Combat/Units/AttackPhase.cs; Core/Simulation/SimConstants.cs — MinWindupTicks, MaxAttackAnimTicks, AttackReachTolerance
- знач. — **док:** §3.2: мутации боя идут «только через методы контекста», список методов исчерпывающий; про перехват до урона не сказано
  **код:** DealDamage сначала прогоняет синхронный pre-damage проход EffectSystem.RunPreDamage — компонент может полностью негейтить удар («Изворотливость») либо поднять щит, поглощающий триггер-удар («Оплот»), и тогда ни урона, ни урон-событий не будет. Это отдельная развилка пайплайна, невидимая в документе.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs — DealDamage() строки 269-279; Assets/_Project/Scripts/Combat/Effects/EffectSystem.cs — RunPreDamage()
- знач. — **док:** Документ описывает сим как «бой = загруженная сцена», без упоминания persist-мира
  **код:** У симуляции появился persist-контур: SetArena(ArenaBounds, Rect2D?) меняет арену на месте (тест-зона ↔ боевая), ResetBattle() перезапускает бой без перезагрузки сцены (событие OnBattleReset), FlushSpawns() вливает юнитов в фазе расстановки без тика систем. Плюс RebakeTuning(SimTuning) применяет новый тюнинг к идущему бою (tainted).
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs — SetArena(), ResetBattle(), FlushSpawns(), RebakeTuning()

#### `10-reference/assemblies.md` — update, 80%

*Граф и правила в целом верны и совпадают с .asmdef, но карта неполна: с момента сверки 2026-07-16 появились три сборки (Balance, Balance.Editor, PaletteRemap.Editor) и отдельная тестовая сборка Balance.Tests, которых в доке нет вообще. Плюс мелкие расхождения по внутренним/внешним ссылкам у DevTools, Presentation и Tests.EditMode. Версии-дефайнов (versionDefines) нет ни в одной сборке — тут дока ничего лишнего не утверждает.*

- знач. — **док:** Список рантайм-сборок: Core, Data, Combat, Guild, MiniGames, Net, Presentation, UI, DevTools, Game (граф зависимостей + таблица «Текущие сборки»)
  **код:** Есть ещё рантайм-сборка Guildmaster.Balance (includePlatforms: [], references: Guildmaster.Core, Guildmaster.Data) — в доке отсутствует и в графе, и в таблице
  **где:** Assets/_Project/Scripts/Balance/Guildmaster.Balance.asmdef (name: Guildmaster.Balance; содержимое папки — BalanceScenarioData.cs)
- знач. — **док:** Editor-only сборки: Data.Editor, Game.Editor, Audio.Editor, ContentHub.Editor, UI.Editor
  **код:** Editor-сборок семь: дополнительно Guildmaster.Balance.Editor (refs: Balance, Core, Data, Combat) и Guildmaster.PaletteRemap.Editor (refs: пусто, rootNamespace Guildmaster.PaletteRemap)
  **где:** Assets/_Project/Scripts/Balance/Editor/Guildmaster.Balance.Editor.asmdef; Assets/_Project/Scripts/EditorTools/PaletteRemap/Guildmaster.PaletteRemap.Editor.asmdef
- знач. — **док:** Тестовых сборок две: Guildmaster.Tests.EditMode и Guildmaster.Tests.PlayMode
  **код:** Тестовых сборок три: есть ещё Guildmaster.Balance.Tests в Tests/EditMode/Balance/ (Editor-платформа, UNITY_INCLUDE_TESTS, refs: Balance, Balance.Editor, Combat, Core, Data)
  **где:** Assets/_Project/Tests/EditMode/Balance/Guildmaster.Balance.Tests.asmdef

#### `00-meta/index.md` — update, 80%

*MOC жив и структурно здоров: все перечисленные ссылки ведут на существующие файлы, битых внутренних ссылок во всей вике нет (проверено линтером), Dataview-запросы FROM "tech" корректны для vault-корня docs/wiki. Две болячки: два planning-дока не попали в список, а ярлыки статусов в списках живут своей жизнью мимо легенды.*

- знач. — **док:** Секция «Planning (40-planning/)» перечисляет полный набор планов — 16 позиций
  **код:** В папке 18 файлов. В MOC отсутствуют act-map-run-loop.md (карта акта и петля забега — крупный реализованный заход) и simbench.md (балансный стенд). Оба живые и на них ссылаются другие доки.
  **где:** ls docs/wiki/tech/40-planning/ → 18 .md против 16 пунктов списка в index.md:38-53
- знач. — **док:** «Легенда статусов (frontmatter status)»: draft / needs_review / ready / planned / living / archive — и списки-кластеры несут «ярлыки статуса»
  **код:** В planning-списке ярлыки взяты не из легенды и не из frontmatter: `реализовано`, `в работе`, `история`, `текущее`. Часть прямо противоречит файлам — phase-1 помечен `реализовано` при frontmatter archive, deployment-encounters `реализовано` при frontmatter archive, sfx помечен `в работе` при том, что FMOD-контур уже собран и звучит.
  **где:** index.md:39,47,50 против frontmatter status: archive в phase-1-combat-core.md и deployment-encounters.md; sfx помечен `в работе` против Assets/_Project/Scripts/Game/Services/FmodAudioService.cs + Assets/StreamingAssets/SFX.bank

#### `40-planning/stat-system.md` — rewrite_section, 80%

*Ядро дока безупречно: ровно 30 статов, порядок и ординалы совпадают с enum один в один, включая пометки фаз. Но §1 и §4 утверждают, что уклонения в игре нет, — а оно есть, и §2 описывает школу урона как единственное поле юнита, тогда как урон давно резолвится поисточниково.*

- знач. — **док:** §1: «Уклонение/промах — нет. Выживаемость покрыта HP / бронёй / DamageTakenEff / щитами»; §4: «Без RNG — крита и уклонения нет»
  **код:** Уклонение существует как полноценная механика: pre-damage реактив с зарядами полностью отменяет входящую автоатаку, и сим шлёт наружу отдельный сигнал уклонения. Тезис про отсутствие RNG при этом уцелел — механика на зарядах, не на броске, — но плоское «уклонения нет» неверно и противоречит контенту (есть ассет эффекта Dodge).
  **где:** Assets/_Project/Scripts/Combat/Effects/Components/DodgeComponent.cs:17,44-64 (IPreDamageComponent, result.Negated = true); Assets/_Project/Scripts/Combat/CombatSimulation.cs:96,277 (OnAttackEvaded); Assets/_Project/ScriptableObjects/Effects/Dodge.asset
- знач. — **док:** §2 «Категориальная конфигурация (НЕ статы, на RelicData)»: DamageSchool = Physical / Magic / True — как одно поле юнита
  **код:** Модель ушла на поисточниковую: школа урона резолвится на каждый источник через override данных способности поверх школы кастера, а не берётся только с юнита. Док описывает предыдущую редакцию модели урона.
  **где:** Assets/_Project/Scripts/Combat/Abilities/AbilitySystem.cs:220,326,351 (DamageCategories.Resolve(data.SchoolOverride, caster.DamageSchool)); Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs:40

#### `10-reference/asset-inventory.md` — update, 85%

*Каталог паков в целом жив: Kenney / Cainos / Honeti / Feel / Shapes / наша Art на месте, готчи про GUID-поиск и починенные находки (Always Included Shaders, Help.png) актуальны. Дрейф — в разделе «наша Art»: за последние дни появились три новые группы ассетов (бренд/сплеш, исходники скелетной анимации, VFX-материалы) и папка иконок тегов юнитов, ни одна не занесена в таблицу вопреки собственному «правилу ведения».*

- знач. — **док:** Таблица каталога: строка «Наша Art — 8 шейдеров, 4 материала, иконки карты, grayscale-арена, спрайт-пул»
  **код:** Появилась новая папка Assets/_Project/Art/Brand/ — 7 PNG бренда: AppIcon_HappyGuildmasters(+_512/_1024), AppIcon_Crest_Parts, AppIcon_Mascot_Alt, CompanyLogo_Alebardium, SplashLogo_HappyGuildmasters. Добавлены коммитами bdd96845 «art(brand): add Happy Guildmasters build icons and product name» и 7a44fe1f «feat(ui): add boot title card and 512/1024 brand splash». В инвентаре не отражены.
  **где:** Assets/_Project/Art/Brand/ (листинг) + git log --diff-filter=A -- Assets/_Project/Art/Brand
- знач. — **док:** Раздел «Чего нет и придётся делать самим» / строка про наш спрайт-пул — про исходники скелетной анимации ничего
  **код:** Добавлена папка Assets/_Project/Art/Sprites/Bone Animations/ с PSB-исходником «Bone Animation Sprites - Standart - Ready.psb» и нарезкой частей тела (Head, Body, Arm (Top/Down/Shoulder), Leg (Down/Boots)…) — сырьё для принятого вектора скелетной анимации. В каталоге отсутствует.
  **где:** Assets/_Project/Art/Sprites/Bone Animations/ (листинг) + git log --diff-filter=A --since=2026-07-20 -- Assets/_Project/Art
- знач. — **док:** Раздел про иконки: «Тема тянет две иконки мимо ремапа (HeartRed, LockGold)»; про иконки тегов ничего
  **код:** В Assets/_Project/Art/UI/Icons-gm/ появилась папка Tags/ — 55 PNG иконок тегов юнитов + ATTRIBUTION.txt (112 файлов с .meta): anti_heal, anti_summon, aoe, armor_break_*, assassin, blunt, bruiser и т.д. Это отдельный источник иконок с собственной атрибуцией, которого нет ни в каталоге, ни в разделе про палитру/ремап.
  **где:** Assets/_Project/Art/UI/Icons-gm/Tags/ (листинг, ATTRIBUTION.txt)

#### `40-planning/act-map-run-loop.md` — update, 85%

*План реализован практически дословно — от генератора карты до главного меню, включая экономику с точными числами из §3.3. Но статус-шапка всё ещё держит его как черновик, ожидающий разрешения на старт. Это самый громкий статусный разрыв в кластере.*

- **КРИТ** — **док:** Статус-шапка: «Черновик плана (2026-07-17)… Ждёт финального ревью Макса перед реализацией», frontmatter status: draft
  **код:** План исполнен целиком. В коде есть все узлы графа §6: MapGenerator, ActRunner, NodeResolver, ShopFlow, ChestFlow, RandomEventFlow, экран исхода, главное меню.
  **где:** Assets/_Project/Scripts/Guild/MapGenerator.cs; Assets/_Project/Scripts/Game/Services/ActRunner.cs; Assets/_Project/Scripts/Game/Flow/NodeResolver.cs; ShopFlow.cs; ChestFlow.cs; RandomEventFlow.cs; Assets/_Project/Scripts/UI/OutcomeScreenView.cs; Assets/_Project/Scripts/Game/Flow/MainMenuPresenter.cs
- знач. — **док:** §1 «Нет вообще: генератор карты, петля обхода узлов (главное), экран карты… ShopFlow + экран магазина (в коде ни строчки), ChestFlow… «?»-узел… экран исхода забега, главное меню»
  **код:** Раздел «отправная точка» описывает состояние, которого нет уже давно: перечисленное «нет вообще» существует всё, до последнего пункта, включая экраны магазина и сундука.
  **где:** Assets/_Project/Scripts/UI/ShopScreenView.cs; Assets/_Project/Scripts/UI/ChestScreenView.cs; Assets/_Project/Scripts/Game/Flow/MapNodeChooser.cs
- знач. — **док:** §3.4/C1: «в коде до сих пор старый счётчик BattleFlow.DefaultMaxRetries=2 на каждый бой — техдолг»
  **код:** Техдолг закрыт: DefaultMaxRetries в коде отсутствует, пул перезапусков живёт как RunState.RestartsRemaining и сбрасывается из GameConfig.RestartsPerAct на старте акта, как и предписывал план.
  **где:** Assets/_Project/Scripts/Guild/RunStateService.cs:90 (Current.RestartsRemaining = _config.RestartsPerAct); Assets/_Project/Scripts/Data/Definitions/GameConfig.cs:65,92

#### `40-planning/deployment-encounters.md` — update, 88%

*Frontmatter уже archive, но прозаическая статус-шапка застряла на середине пути и уверяет, что loadout-экран и фаза расстановки «в работе». Все пять шагов плана давно доставлены.*

- знач. — **док:** Статус-шапка: «Частично реализовано… Полноэкранный loadout-экран (§5) и полная фаза расстановки — в работе»
  **код:** Оба шага закрыты: есть оркестратор фазы расстановки с визуальным слоем и полноценный loadout-хаб с MVVM и вкладкой инвентаря.
  **где:** Assets/_Project/Scripts/Game/DeploymentController.cs; Assets/_Project/Scripts/Presentation/DeploymentView.cs; Assets/_Project/Scripts/UI/LoadoutHubView.cs + LoadoutHubViewModel.cs + LoadoutInventoryView.cs

#### `40-planning/phase-3-ai-relics.md` — update, 88%

*Шаги 1–6 отмечены готовыми и подтверждаются кодом. Висит только шаг 7 «закрытие фазы» с меткой ожидания — при том, что фаза давно перевыполнена: реликвий не 7, а 10.*

- знач. — **док:** Таблица «Состояние шагов (§8)», шаг 7 «Закрытие фазы: все 7 срезов в харнессе, отметка в Roadmap §3, ретро» — статус ⏳
  **код:** Фаза перевыполнена и де-факто закрыта: в контенте 10 боевых реликвий (7 плановых + Druid, Treant, FlameSwordsman), у каждой свой AI-пресет. Метка ⏳ держит закрытую фазу открытой.
  **где:** Assets/_Project/ScriptableObjects/Relics/ (Assassin, Cryomancer, Defender, Druid, FlameSwordsman, IronSpearman, LightShepherd, Ranger, Treant, WhirlMonk + BaseRelic); ScriptableObjects/AiPresets/ (пресет на каждую)

#### `40-planning/simbench.md` — update, 90%

*План исполнен и шапка это честно фиксирует, включая коррекции против эскиза. Подводит только пара деталей самой шапки: путь меню и имя ветки указаны неверные.*

- знач. — **док:** Статус-шапка: «меню Tools/Balance/*»
  **код:** Меню живёт под корнем Alebardium (проектное правило единого корня редакторного тулинга): Alebardium/Balance/0. Audit Content, 1. DPS Bench, 2. Duel Matrix и т.д. По пути из доки пункт не найдётся.
  **где:** Assets/_Project/Scripts/Balance/Editor/BalanceMenu.cs:15-27 ([MenuItem("Alebardium/Balance/…")])

#### `40-planning/attack-timing.md` — update, 90%

*Дизайн-заметка реализована целиком, вплоть до целочисленной формулы, рута на замах, трёх событий сима и переноса UnitVisual в Data. Шапка при этом всё ещё утверждает, что код не написан.*

- **КРИТ** — **док:** Шапка: «Код ещё не написан — это согласованная идея, причины и решённые развилки для продолжения в новом чате»
  **код:** Двухфазный windup в симе реализован: есть выделенный модуль тайминга с формулой из §«Модель тайминга», фазовое состояние на юните и вычитание windup в системе автоатак.
  **где:** Assets/_Project/Scripts/Combat/Systems/AttackTiming.cs (WindupTicks/AttackDurationTicks/FollowThroughTicks); Assets/_Project/Scripts/Combat/Units/RuntimeUnit.cs:95 (IsWindingUp => Phase == AttackPhase.Windup); Assets/_Project/Scripts/Combat/Systems/AutoAttackSystem.cs:51-52
- знач. — **док:** §«Швы реализации» п.4: события OnAttackStarted / OnAttackInterrupted — «сигнатуры в следующем чате»
  **код:** Оба события существуют в симуляции и уже потребляются презентацией и аудио-слоем.
  **где:** Assets/_Project/Scripts/Combat/CombatSimulation.cs:99,102,434,436; Assets/_Project/Scripts/Presentation/CombatPresenter.cs:105; Assets/_Project/Scripts/Presentation/Audio/AudioPresenter.cs:47

---

## Реестр 2: системы без покрытия (49)

`in-flight` — система активно менялась на момент снимка, документировать со статусом `needs_review`.

| Система | Покрытие | Действие | Кластер | Труд | in-flight |
|---|---|---|---|---|---|
| Боевые классы юнита + 4-уровневый каскад стат-базы | none | section_in_existing | 20-explanation | M |  |
| Слой Species/Subspecies — стат-скейлы врага | none | extend_existing | 10-reference | S |  |
| Поисточниковая модель урона (DamageType-струка, MagicElement, PhysicalSubtype, Elemental→Magical) | partial | extend_existing | 10-reference | M |  |
| Резолвер тегов юнита (4 оси Role→DamageType→Playstyle→Mechanic) | stale | extend_existing | 10-reference | M | да |
| Разложение стата (Stats.Explain / StatValue) — read-model для тултипов | none | section_in_existing | 20-explanation | M |  |
| Редизайн Пастыря — AllyMendComponent («Целебный свет») | stale | extend_existing | 10-reference | S | да |
| Позиционика боя (угроза/блокирование вместо зоны контроля) | none | skip | 40-planning | M |  |
| SimBench + ContentEditService (петля баланса read→edit→read) | covered | skip | 40-planning | S |  |
| Боевые VFX через префаб-шов VfxData (миграция с PixelBurst) | none | new_doc | 20-explanation | M | да |
| CombatFeelConfig и слой микро-джуса | none | section_in_existing | 20-explanation | M | да |
| Скелетная анимация: Aseprite bone-export пайплайн и BoneUnit-риг | none | new_doc | 30-how-to | M | да |
| Переход между кадрами: IScreenTransition + чернильная шторка MapTransition | none | new_doc | 20-explanation | S | да |
| Карта акта в презентации (world-space): WorldMapView, MapStyle, префабы узла и дорожки | stale | new_doc | 10-reference | L | да |
| Единый визуальный такт (IVisualTempo) и реестр переключаемых эффектов (VisualToggles) | none | extend_existing | 20-explanation | S |  |
| Тултипы и слой описаний (TooltipSystem + ITooltipContentFactory + IDescriptionService) | stale | new_doc | 10-reference | M | да |
| Тест-зона: grayscale-скин арены | partial | section_in_existing | 10-reference | S |  |
| Бренд и boot title card (splash, иконки сборки) | none | new_doc | 10-reference | S |  |
| Пайплайн per-unit ViewPrefab, аудит анимаций и визуальный каталог (Alebardium/Visuals) | stale | extend_existing | 10-reference | M |  |
| Визуал расстановки: единый силуэт юнита и drag-призрак | none | section_in_existing | 10-reference | S |  |
| Дизайн-система токенов «тёплый свет» (3 яруса USS) | none | new_doc | 10-reference | M | да |
| Единый источник контуров --gm-outline-* | none | section_in_existing | 10-reference | S | да |
| Библиотека кастом-контролов gm-* (UITK composites) | none | new_doc | 10-reference | M | да |
| AspectBox — соотношение сторон как механика | none | section_in_existing | 10-reference | S |  |
| gm-chip — единый компонент «иконка + подпись» | none | section_in_existing | 10-reference | S |  |
| Экран лоадаута/инвентаря — трёхколоночник с таро-карточками | partial | new_doc | 10-reference | M | да |
| Boot title card — карточка бренда до главного меню | none | section_in_existing | 10-reference | S |  |
| MenuRouter + UiNavigator: стек экранов и слои | stale | extend_existing | 10-reference | S |  |
| Шов UI↔презентация: MenuVisibilityMessages и шторка перехода | none | section_in_existing | 10-reference | S | да |
| Ритм узлов забега: RunBeatStage и фаза Interlude | stale | extend_existing | 20-explanation | M | да |
| IScreenTransition — «моргание» между кадрами | none | new_doc | 20-explanation | S | да |
| UI-тесты инвариантов раскладки и политик | none | new_doc | 30-how-to | S |  |
| TMP Static-bake шрифты (EN+RU) и защита от git-churn | partial | section_in_existing | 30-how-to | S |  |
| Витрина компонентов (UI gallery) | partial | section_in_existing | 10-reference | S |  |
| Тултип-система (Трек Т) | none | skip | 20-explanation | M | да |
| План UI-архитектуры (docs/ui-architecture-rework-plan.md) — жив, но частично отработан | partial | new_doc | 40-planning | L |  |
| Единый корень Editor-меню Alebardium (реальная раскладка + приоритеты) | stale | extend_existing | 10-reference | S |  |
| Гейт битых вики-ссылок: check-wiki-links.ps1 + docs-lint.yml | none | new_doc | 30-how-to | S |  |
| Сайт документации: docs.yml (Quartz v4.5.2 + Doxygen) | covered | section_in_existing | 30-how-to | S |  |
| statdb.ps1 — правка статов в YAML-ассетах мимо Unity | none | new_doc | 30-how-to | S |  |
| Write-сторона авторинга контента: ContentEditService / ContentCrudService | partial | section_in_existing | 10-reference | M |  |
| Content Hub — реальное состояние окна | partial | new_doc | 10-reference | M |  |
| Арт-пайплайн Aseprite → Photoshop → Unity (костяная анимация) | none | new_doc | 30-how-to | M | да |
| Palette Remapper (gradient-map перекраска растрового арта) | partial | extend_existing | 10-reference | S |  |
| CI-пайплайн ci.yml: paths-filter + единый CI Gate | partial | new_doc | 30-how-to | M |  |
| Тестовый набор: состав, сборки, покрытие | stale | extend_existing | 10-reference | S |  |
| Сборки Balance и PaletteRemap.Editor отсутствуют в карте asmdef | stale | extend_existing | 10-reference | S |  |
| Идентичность билда: Build Profile Windows, defines, брендинг, Alebardium/Test | none | new_doc | 10-reference | M | да |
| Addressables: профиль-данные и папка Windows/ билд-состояния | partial | section_in_existing | 10-reference | S | да |
| .gitattributes: сеть под TMP SDF-атласы | stale | extend_existing | 30-how-to | S |  |

### Что за системы (по коду)

- **Боевые классы юнита + 4-уровневый каскад стат-базы** — Введён enum UnitClass (Bruiser=0 эталон, Tank, Assassin, Ranged, Support, Summoner) как механический вход баланса: поле UnitData.CombatClass. ClassBalanceConfig (единственный SO, ассет: _baseHp=2000, _baseMoveSpeed=3, множители Tank 1.5/0.85, Assassin 0.75/1.1, Ranged/Support/Summoner 0.65/0.75) отдаёт классовую базу как группу StatModifier с ModifierOp.Override через GetBaseModifiers. ClassBaseline.Apply — единственная точка внесения этой базы в Stats; вызывается ПЕРВОЙ группой, до стат-блока персоны, поэтому правило «последний Override побеждает» бесплатно даёт каскад StatsConfig → класс → вид/подвид → персона → Vessel. RuntimeUnitFactory.Create вызывает ClassBaseline.Apply(69) → EnemyScalers.Apply(72) → data.Stats(75) → vessel.PerkModifiers(78) → item.Mods(86). Тот же путь переиспользуют Content Hub (StatMath.BuildEffective) и SimBench (SimEnvironment передаёт конфиг в фабрику), чтобы «таблица не врала».
  Ключевое: `UnitClass (enum)`, `ClassBalanceConfig (ScriptableObject)`, `ClassBalanceConfig.ClassProfile`, `ClassBaseline (static)`, `RuntimeUnitFactory`, `StatMath.BuildEffective`
  Код: Assets/_Project/Scripts/Data/Stats/UnitClass.cs, Assets/_Project/Scripts/Data/Definitions/ClassBalanceConfig.cs, Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs, Assets/_Project/Scripts/Combat/Units/RuntimeUnitFactory.cs, Assets/_Project/Scripts/Game/CombatLifetimeScope.cs, Assets/_Project/Scripts/EditorTools/ContentHub/Core/StatMath.cs, Assets/_Project/Scripts/Balance/Editor/BalanceAssets.cs, Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset, Assets/_Project/Tests/EditMode/Combat/ClassBaselineTests.cs
- **Слой Species/Subspecies — стат-скейлы врага** — Новый контент-тип SpeciesData : ContentDefinition с полем StatModifier[] _scalers — множители, общие для всех юнитов вида. Тот же контейнер используется и для подвида. EnemyData получил поля _species и _subspecies. EnemyScalers.Apply — единственная точка внесения: no-op для не-EnemyData, иначе добавляет группы Species затем Subspecies, между ClassBaseline и стат-блоком персоны. Скейлы обычно PercentMult и перемножаются поверх классовой базы (ассет species.goblins: Stat 0 (MaxHP) Op 2 (PercentMult) -0.6, Stat 20 (MoveSpeed) PercentMult +0.1). В ContentDomains зарегистрирован новый домен id "species".
  Ключевое: `SpeciesData (ScriptableObject : ContentDefinition)`, `EnemyScalers (static)`, `EnemyData.Species / EnemyData.Subspecies`, `ContentDomains (домен species)`
  Код: Assets/_Project/Scripts/Data/Definitions/SpeciesData.cs, Assets/_Project/Scripts/Combat/Stats/EnemyScalers.cs, Assets/_Project/Scripts/Data/Definitions/EnemyData.cs, Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs, Assets/_Project/ScriptableObjects/Species/Goblins.asset, Assets/_Project/Tests/EditMode/Combat/ClassBaselineTests.cs
- **Поисточниковая модель урона (DamageType-струка, MagicElement, PhysicalSubtype, Elemental→Magical)** — Тип урона перестал быть одним полем юнита и стал дескриптором КАЖДОГО источника. Введена readonly struct DamageType {School, PhysicalSubtype, MagicElement, Affinity} с инвариантом нормализации (подтип живёт только при Physical, элемент — только при Magical). Добавлена ось MagicElement (None/Fire/Ice/Lightning/Arcane) рядом с существующим PhysicalSubtype (Blunt/Slash/Pierce). Каждая ось имеет *Override-enum с Inherit, DamageCategories.Resolve разворачивает override в конкретику. UnitData.ResolveAutoAttackDamageType() собирает тип автоатаки, AbilityData.ResolveDamageType(caster) — тип способности; DoT-компоненты (PeriodicDamage, Ignition, ArmorThorns) получили собственные поля типа. Параллельно школа Elemental переименована в Magical, статы ElementalArmor/ElementalPen/ElementalPenPct → MagicArmor/MagicPen/MagicPenPct (int-значения enum сохранены, ассеты не мигрировали). В пайплайне урона участвуют только School и Affinity; подтип/элемент — read-model для тегов и задел на будущее.
  Ключевое: `DamageType (readonly struct)`, `MagicElement / PhysicalSubtype / DamageSchool / DamageAffinity (enum)`, `DamageSchoolOverride / MagicElementOverride / PhysicalSubtypeOverride / DamageAffinityOverride`, `DamageCategories (static Resolve)`, `UnitData.ResolveAutoAttackDamageType()`, `AbilityData.ResolveDamageType(UnitData)`
  Код: Assets/_Project/Scripts/Data/Definitions/DamageType.cs, Assets/_Project/Scripts/Data/Definitions/CombatCategories.cs, Assets/_Project/Scripts/Data/Definitions/UnitData.cs, Assets/_Project/Scripts/Data/Definitions/AbilityData.cs, Assets/_Project/Scripts/Data/Stats/StatType.cs, Assets/_Project/Scripts/Combat/Damage/DamagePipeline.cs, Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs, Assets/_Project/Scripts/Combat/Effects/Components/PeriodicDamageComponent.cs, Assets/_Project/Scripts/Combat/Effects/Components/IgnitionComponent.cs, Assets/_Project/Scripts/Combat/Effects/Components/ArmorThornsComponent.cs, Assets/_Project/Tests/EditMode/Combat/DamageTypeResolverTests.cs
- **Резолвер тегов юнита (4 оси Role→DamageType→Playstyle→Mechanic)** — Статический UnitTagResolver.Resolve(UnitData, IContentDatabase) собирает упорядоченный список TagData для карточки юнита. Оси 1-2 авто-выводятся из данных: Role — из UnitData.CombatClass (Tank→tag.tank и т.д.), DamageType — из DamageType всех статических источников урона (автоатака + способности с DamageMultiplier>0), причём внутри оси порядок «зонтик школы (tag.physical/tag.magical/tag.pure) → конкретика (tag.blunt/tag.slash/tag.pierce, tag.fire/tag.ice/tag.lightning/tag.arcane) → сродство (tag.poison/tag.light/tag.dark)». Оси 3-4 (Playstyle, Mechanic) — ручные из UnitData.InfoTags. Дальше устойчивая сортировка вставками по TagData.Category. Отсутствующий в базе ассет тега молча пропускается. Стихии, живущие в эффектах (Burn→Fire, споры→Poison), сознательно НЕ попадают — это осознанный задел.
  Ключевое: `UnitTagResolver (static)`, `TagData (ScriptableObject : ContentDefinition)`, `TagCategory (enum)`, `UnitData.InfoTags`, `LoadoutViewModel.ResolveTags`
  Код: Assets/_Project/Scripts/Data/Definitions/UnitTagResolver.cs, Assets/_Project/Scripts/Data/Definitions/TagData.cs, Assets/_Project/Scripts/Data/Definitions/UnitData.cs, Assets/_Project/Scripts/UI/LoadoutViewModel.cs, Assets/_Project/Scripts/UI/LoadoutInventoryView.cs, Assets/_Project/Tests/EditMode/Combat/UnitTagResolverTests.cs, Assets/_Project/ScriptableObjects/Tags/
- **Разложение стата (Stats.Explain / StatValue) — read-model для тултипов** — Stats реализует IStatExplainer и получил метод Explain(StatType) → StatValue {Stat, Base, Final, Terms[], Kind}. Base = последний Override (способ авторинга базовых статов) либо дефолт StatsConfig; Override сознательно НЕ попадает в Terms. Каждый StatTerm несёт SourceLocKey, Op, сырое Value и Contribution — вклад считается как «итог с модом минус итог без него», единственная честная величина при смешанных Flat/PercentAdd/PercentMult. Final по построению равен Get(stat) (обе дороги через Compose). ValueKind + таблица StatKinds.KindOf задают размерность показа (Flat/Percent/Multiplier/Seconds/PerSecond/Distance/Count) — форматирование решается данными, а не ToString по месту. Интерфейс IModifierSource даёт источнику опциональное имя; RuntimeEffect его реализует через ContentKeys.NameKey(Def). Явно помечено как инспекция, а не горячий путь тика.
  Ключевое: `Stats.Explain`, `StatValue / StatTerm (readonly struct)`, `ValueKind (enum)`, `StatKinds (static)`, `IModifierSource`, `IStatExplainer`, `ContentKeys`
  Код: Assets/_Project/Scripts/Combat/Stats/Stats.cs, Assets/_Project/Scripts/Data/Stats/StatValue.cs, Assets/_Project/Scripts/Data/Stats/StatKinds.cs, Assets/_Project/Scripts/Data/Stats/IStatExplainer.cs, Assets/_Project/Scripts/Data/Definitions/ContentKeys.cs, Assets/_Project/Scripts/Combat/Effects/RuntimeEffect.cs, Assets/_Project/Tests/EditMode/Combat/StatsExplainTests.cs
- **Редизайн Пастыря — AllyMendComponent («Целебный свет»)** — Новый реактивный компонент эффекта: подписан на CombatEvent.DamageDealt носителя, по умолчанию только на автоатаку (_autoAttackOnly). При срабатывании берёт heal = e.Amount × _fraction × ctx.Stacks, запрашивает союзников в радиусе _radius вокруг СЕБЯ (QueryUnitsInRadius, TargetFilter.Allies) и лечит самого раненого по HP% через ctx.Combat.Heal; тай-брейк при равном HP% — меньший Id, ради детерминизма. Stateless, буфер списка переиспользуется. Это смена модели Пастыря: вместо хил-снаряда по союзнику он бьёт врага чистым/световым уроном, а лечение идёт побочным эффектом — аналог LifestealComponent, но исцеляющий не себя.
  Ключевое: `AllyMendComponent : IReactiveComponent`, `EffectData ассет LightMend`, `RuntimeEffect / EffectContext`
  Код: Assets/_Project/Scripts/Combat/Effects/Components/AllyMendComponent.cs, Assets/_Project/ScriptableObjects/Effects/LightMend.asset, Assets/_Project/ScriptableObjects/Relics/LightShepherd.asset, Assets/_Project/ScriptableObjects/AiPresets/LightShepherd.asset, Assets/_Project/Tests/EditMode/Combat/AllyMendComponentTests.cs
- **Позиционика боя (угроза/блокирование вместо зоны контроля)** — По КОДУ система отсутствует: за последние 10 дней в Assets/_Project/Scripts/Combat нет ни одного коммита по позиционике (последнее касание ProfileBrain.cs — 0c6d3351 от 2026-07-12, FleeSteering.cs — тот же коммит). Существующие в коде упоминания Threat — это давний режим таргетинга TargetingMode.HighestThreat (оценочный DPS), не новая механика угрозы. Всё изменённое за окно живёт только в дизайн-доке docs/wiki/gdd/20-combat/positioning.md (коммиты 0eab7120 → 65db1243 → d6380286, 25-26.07): круг вердиктов P1-P22 закрыт, принято «ИИ блокирует, зоны контроля нет» и «танк держит линию угрозой, а не стеной», числа (5 сек агро, +20%/+40% за тыл) черновые.
  Ключевое: `ProfileBrain (существующий, не менялся)`, `TargetingMode.HighestThreat (существующий)`
  Код: Assets/_Project/Scripts/Combat/AI/ProfileBrain.cs, Assets/_Project/Scripts/Combat/FleeSteering.cs, docs/wiki/gdd/20-combat/positioning.md
- **SimBench + ContentEditService (петля баланса read→edit→read)** — Headless-стенд поверх боевого ядра: SimEnvironment собирает CombatSimulation вручную (rng + armorK + SpatialHash + системы) и тикает голым циклом, бенчи (DPS/выживаемость/дуэли+рейтинг) и ContentAuditor выдают CSV/Markdown в BalanceReports. Write-сторона — ContentEditService в Guildmaster.Data.Editor: правка ЗНАЧЕНИЙ контент-SO через SerializedObject+Undo с change-log (сосед ContentCrudService, который отвечает за жизненный цикл ассета/id). Меню собрано под корень Alebardium/Balance/*.
  Ключевое: `SimBench`, `SimEnvironment`, `BalanceAssets`, `ContentAuditor`, `MetricCollector`, `ContentEditService`
  Код: Assets/_Project/Scripts/Balance/Editor/SimBench.cs, Assets/_Project/Scripts/Balance/Editor/SimEnvironment.cs, Assets/_Project/Scripts/Balance/Editor/BalanceAssets.cs, Assets/_Project/Scripts/Balance/Editor/ContentAuditor.cs, Assets/_Project/Scripts/Balance/Editor/MetricCollector.cs, Assets/_Project/Scripts/Balance/Editor/Benches, Assets/_Project/Scripts/Balance/Editor/Rating, Assets/_Project/Scripts/Data/Editor/ContentEditService.cs
- **Боевые VFX через префаб-шов VfxData (миграция с PixelBurst)** — Боевые эффекты теперь описываются контент-ассетом VfxData (id vfx.*, ссылка на самодостаточный префаб, Scale, SortingLayerName, SortingOrder, DefaultDirDeg), а форма частиц живёт в самом префабе. CombatVfx держит ObjectPool<PooledVfx> на каждый префаб, спавнит по мировой точке с переопределяемым углом и множителем интенсивности, и умеет гасить всё летящее на сбросе боя (DespawnAll). PooledVfx на корне префаба играет эффект, пересчитывает sorting order детей относительно базового из VfxData и сам возвращается в пул. CombatPresenter дёргает пять ссылок из CombatFeelConfig (VfxMuzzle в ShotPoint, VfxHitSpark/VfxHeal в HitPoint, VfxImpactDust/VfxContactDust в FeetPoint), сила искр — из EvaluateHitVfxIntensity по доле HP-урона. Прежний процедурный PixelBurst снесён целиком: ни файлов PixelBurst*, ни шейдера SH_Pixel_Burst в дереве нет.
  Ключевое: `VfxData (ContentDefinition, CreateAssetMenu Guildmaster/Content/Vfx)`, `CombatVfx.Spawn(VfxData, Vector3, float?, float)`, `CombatVfx.DespawnAll()`, `PooledVfx.Play/Cancel`, `ObjectPool<PooledVfx>`
  Код: Assets/_Project/Scripts/Data/Definitions/VfxData.cs, Assets/_Project/Scripts/Presentation/CombatVfx.cs, Assets/_Project/Scripts/Presentation/PooledVfx.cs, Assets/_Project/Scripts/Presentation/CombatPresenter.cs, Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs
- **CombatFeelConfig и слой микро-джуса** — Один SO собрал весь impact-слой боя: 12 булевых тумблеров микро-фила (contact dust, hit nudge, flip-squash, target-acquire tell, idle breath, school flash, attack anticipation, attacker lunge, impact frame, death anticipation, floating-text arc, HP-bar punch) плюс их числа, и рядом — hitstop с кривой веса, форма и интенсивности screen shake, масштаб боевых цифр, ~14 параметров death-shatter (включая псевдо-3D tumble, ember-цвет с HDR под bloom, up-bias) и ступенчатый таймлайн финишера (пауза → slowmo смерти → сильное slowmo разлёта → возврат по кривой). Цвет hit-flash считается ResolveHitFlashColor по школе и сродству урона — сродство перекрывает школу, при выключенном тумблере остаётся фолбэк-цвет. Потребители (UnitView, CombatPresenter, ScreenShake, CombatFeelDirector) тянут значения отсюда, а не хардкодят. Выключение тумблера здесь гасит эффект во всех местах без правок кода.
  Ключевое: `CombatFeelConfig (CreateAssetMenu Guildmaster/Design/Combat Feel Config)`, `CombatFeelConfig.EvaluateHitstopSeconds`, `CombatFeelConfig.EvaluateHitVfxIntensity`, `CombatFeelConfig.ResolveHitFlashColor(DamageSchool, DamageAffinity)`, `CombatFeelConfig.FinisherHoldSeconds`
  Код: Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs, Assets/_Project/Scripts/Presentation/UnitView.cs, Assets/_Project/Scripts/Presentation/CombatPresenter.cs, Assets/_Project/Scripts/Presentation/DeathShatter.cs, Assets/_Project/Scripts/Presentation/Camera/ScreenShake.cs, Assets/_Project/Scripts/Presentation/FloatingText.cs, Assets/_Project/Scripts/Presentation/HealthBarView.cs
- **Скелетная анимация: Aseprite bone-export пайплайн и BoneUnit-риг** — Две ветки экспорта из Aseprite по общей конвенции слоёв (обычный слой → как есть, группа @Sword → плоский слой Sword, #Guide/_ref → пропуск): export_bone_parts.lua режет спрайт на отдельные PNG-части (Head, Body, Arm (Top/Down/Shoulder), Leg (Top/Down/Boots), Sword, Shield) прямо в Assets/_Project/Art/Sprites/Bone Animations, export_bone_psd.lua пишет PSD через вендоренный Tsukina-скрипт для ручного пути Photoshop → .psb → Unity Character Rig. BonePartSpritePostprocessor автоматом накатывает BonePartSprite.preset на импорт текстур из этой папки, но только при importSettingsMissing — повторный экспорт поверх PNG не сносит правки PPU/pivot в .meta. В Assets/_Project/Prefabs/Bones собран cutout-риг BoneUnit_Standart (вложенные префабы Arm/Leg, контроллер с Idle/Attack, ~9k строк ключей в Attack.anim). Это отдельный от боевого UnitView стенд: BoneUnit-префаб ни на один UnitData/ViewPrefab пока не заведён.
  Ключевое: `BonePartSpritePostprocessor (AssetPostprocessor, OnPreprocessTexture)`, `BoneUnit_Standart.prefab (Transform+SpriteRenderer риг: Arm/Leg вложенные префабы)`, `BoneUnit_Standart.controller (Idle/Attack)`
  Код: Aseprite/scripts/README.md, Aseprite/scripts/export_bone_parts.lua, Aseprite/scripts/export_bone_psd.lua, Aseprite/scripts/vendor/export_as_psd.lua, Assets/_Project/Scripts/EditorTools/PaletteRemap/BonePartSpritePostprocessor.cs, Assets/_Project/Art/Sprites/Bone Animations/BonePartSprite.preset, Assets/_Project/Prefabs/Bones/BoneUnit_Standart.prefab, Assets/_Project/Prefabs/Bones/BoneUnit_Standart.controller, Assets/_Project/Prefabs/Bones/Idle.anim, Assets/_Project/Prefabs/Bones/Attack.anim
- **Переход между кадрами: IScreenTransition + чернильная шторка MapTransition** — Общий шов «моргания» между кадрами: заказчик приносит форму (сколько закрываемся, сколько держим чёрный кадр, сколько открываемся и в какую UV-точку схлопывается кадр), а ведёт переход ScreenTransitionRunner — не MonoBehaviour, тикает от корневого скоупа по unscaled-времени, чтобы пережить и смену сцены, и уход самого заказчика, и паузу боя. Плотность и точку схлопывания он вещает наружу ScreenFadeChangedEvent, рисует шторку UI-слой поверх всех камер (UiRootBootstrap держит материал). Сама шторка — шейдер Guildmaster/Map/Transition: закрытие идёт по шуму (край расползается пятнами, как чернила по бумаге), виньетка подмешана в порог, растворение дизерингом Байера, есть наезд узора к точке схлопывания (_Dive). Первый заказчик — WorldMapView: клик по узлу схлопывает кадр в него.
  Ключевое: `IScreenTransition (Busy, Play)`, `ScreenTransitionShape (InSeconds/HoldSeconds/OutSeconds/FocusUv, Centered)`, `ScreenTransitionRunner (ITickable, стадии None/In/Hold/Out)`, `ScreenFadeChangedEvent`, `Shader "Guildmaster/Map/Transition"`
  Код: Assets/_Project/Scripts/Core/Flow/IScreenTransition.cs, Assets/_Project/Scripts/Presentation/Transition/ScreenTransitionRunner.cs, Assets/_Project/Scripts/Core/Flow/MenuVisibilityMessages.cs, Assets/_Project/Scripts/UI/UiRootBootstrap.cs, Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs, Assets/_Project/Art/Shaders/SH_Map_Transition.shader, Assets/_Project/Art/Shaders/MapTransitionCommon.hlsl, Assets/_Project/Art/Shaders/MapTransition.mat, Assets/_Project/Tests/EditMode/Presentation/ScreenTransitionRunnerTests.cs
- **Карта акта в презентации (world-space): WorldMapView, MapStyle, префабы узла и дорожки** — Карта акта переехала из UXML-экрана в мир: WorldMapView живёт постоянно в persist-мире и включается/гасится по состоянию (шаблон DeploymentController, а не спавн-на-время), рисует узлы и дорожки на Shapes и отдаёт наружу клик мировым пикингом. Раскладку считает чистая MapLayout (топология этаж+ряд → координаты, разброс выводится хешем из id узла и сида забега, потом расталкивание до MinDistance) — в данных карты координат нет. Все числа и цвета вынесены в единый ассет MapStyle сознательно: поля на компоненте уходили в сериализацию сцены и переставали слушаться кода (дважды стоило раунда play-QA); в сцене остаются только ссылки. Узел — ОДИН префаб на все типы с выключенными вариантами иконок внутри (MapNodeView.IconVariant по строковому Kind), порядок между Shapes и SpriteRenderer задаётся не слоями сортировки, а Z-константами (TableZ/BackdropZ/EdgeZ/NodeZ/FogZ/PawnZ). Presentation намеренно не знает про MapNode/MapNodeType из Guildmaster.Guild — конвертацию делает слой Game, а петля забега достаёт слой через WorldMapViewLink, потому что RegisterComponentInHierarchy не видит объекты чужой сцены. MenuBackdropView рисует тот же «стол» под всеми экранами на своей камере и своём слое.
  Ключевое: `IWorldMapView (Show/Hide/Bounds/NodeClicked/PreviewTravel/ResetPawn)`, `WorldMapView (MonoBehaviour, 921 строка)`, `WorldMapViewLink (линк корневого скоупа к слою мира)`, `MapStyle (CreateAssetMenu Guildmaster/Map/Style, 305 строк)`, `MapLayout (struct, Default, MinDistance/RelaxIterations)`, `MapNodeVisual + MapNodeVisualState (Locked/Available/Current/Cleared)`, `MapNodeView.IconVariant`, `MenuBackdropView`
  Код: Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs, Assets/_Project/Scripts/Presentation/Map/MapStyle.cs, Assets/_Project/Scripts/Presentation/Map/MapLayout.cs, Assets/_Project/Scripts/Presentation/Map/MapNodeView.cs, Assets/_Project/Scripts/Presentation/Map/MapNodeVisual.cs, Assets/_Project/Scripts/Presentation/Map/IWorldMapView.cs, Assets/_Project/Scripts/Presentation/Map/WorldMapViewLink.cs, Assets/_Project/Scripts/Presentation/Map/MenuBackdropView.cs, Assets/_Project/Prefabs/Map/MapNode.prefab, Assets/_Project/Prefabs/Map/PathDot.prefab, Assets/_Project/Art/Shaders/SH_Map_Backdrop.shader, Assets/_Project/Art/Shaders/SH_Map_Table.shader, Assets/_Project/Art/Shaders/SH_Map_Fog.shader, Assets/_Project/Art/Shaders/SH_Map_Icon.shader
- **Единый визуальный такт (IVisualTempo) и реестр переключаемых эффектов (VisualToggles)** — Два маленьких сквозных механизма презентации. IVisualTempo — общий метроном, от которого пляшут все ритмичные анимации (биение доступных узлов карты, волна по дорожкам, пульсация подсветок): анимации считают время не в секундах, а в долях (Phase/Swell с делением такта), поэтому идут как один организм и переживут смену источника такта на музыку из FMOD. Текущая реализация VisualTempo считает от unscaled-времени, чтобы метроном шёл и на паузе боя; BPM намеренно НЕ параметр конструктора — VContainer пошёл бы искать регистрацию float и ронял всю ветку карты. VisualToggles — реестр, где эффекты регистрируются сами и гасятся по имени из дев-команд (gm_fx_off post.map); VolumeVisualToggle цепляет к нему целый Volume постобработки. Экран с галочками сознательно не строился: часть тумблеров позже уедет игроку в настройки доступности.
  Ключевое: `IVisualTempo (Bpm, BeatDuration, Phase(division), Swell(division))`, `VisualTempo (unscaled-время, DefaultBpm=84, SetBpm)`, `VisualToggles (+Entry, Register/Unregister)`, `VolumeVisualToggle (id по умолчанию post.map)`
  Код: Assets/_Project/Scripts/Presentation/Tempo/IVisualTempo.cs, Assets/_Project/Scripts/Presentation/Tempo/VisualTempo.cs, Assets/_Project/Scripts/Presentation/Effects/VisualToggles.cs, Assets/_Project/Scripts/Presentation/Effects/VolumeVisualToggle.cs
- **Тултипы и слой описаний (TooltipSystem + ITooltipContentFactory + IDescriptionService)** — Один-единственный показыватель подсказок на панель: TooltipSystem владеет слоем layer-tooltip, держит задержку наведения 400 мс, grace 0.35 с между соседними целями (иначе проводка курсора через грид читается как «подсказка отключилась»), клампит и флипает окно у краёв, живо рефрешит содержимое и глушится на драге. Что показывать, описывает TooltipRequest по РОДУ данных (реликвия, тег, сосуд, отдельный стат, ключевое слово из текста, готовая строка), а собирает содержимое ITooltipContentFactory — система про контент знает только размер. HARD-правило соблюдено в коде: числа в фабрике не считаются, имя и описание берутся у IDescriptionService, стат-сводка у IUnitStatPreview, то есть из тех же мест, откуда их берёт бой. Крепится к элементам манипулятором (TooltipManipulator), раскладка позиции — в TooltipPlacement.
  Ключевое: `TooltipSystem (DelayMs=400, GraceSeconds=0.35, слой layer-tooltip)`, `TooltipKind (None/Text/Relic/Tag/Vessel/Stat/Keyword)`, `ITooltipContentFactory / TooltipContentFactory`, `TooltipManipulator`, `TooltipPlacement`, `IDescriptionService`, `IUnitStatPreview`
  Код: Assets/_Project/Scripts/UI/Tooltips/TooltipSystem.cs, Assets/_Project/Scripts/UI/Tooltips/ITooltipContentFactory.cs, Assets/_Project/Scripts/UI/Tooltips/TooltipContentFactory.cs, Assets/_Project/Scripts/UI/Tooltips/TooltipManipulator.cs, Assets/_Project/Scripts/UI/Tooltips/TooltipPlacement.cs, Assets/_Project/Scripts/UI/Tooltips/TooltipRequest.cs, Assets/_Project/Scripts/UI/Tooltips/TooltipEvents.cs, Assets/_Project/Scripts/UI/Components/TooltipCard.cs, Assets/_Project/Scripts/Data/Descriptions/DescriptionService.cs, Assets/_Project/Scripts/Data/Descriptions/IDescriptionService.cs
- **Тест-зона: grayscale-скин арены** — Компонент persist-мира держит два корня пола арены — цветной боевой и grayscale-дубль тех же тайлов Cainos — и свапает их по СОСТОЯНИЮ тест-зоны из TestZoneChangedEvent. Серый пол работает визуальным маркером «полигон, не настоящий бой». Явно слушает состояние (e.Active), а не тумблер: прежний самотог SetGray(!_gray) расходился с владельцем, если тот игнорировал бродкаст (регрессии QA #28/#31). Подписку инъектит WorldLifetimeScope.
  Ключевое: `TestZoneArenaSkin (_colorRoot / _grayRoot)`, `TestZoneChangedEvent`
  Код: Assets/_Project/Scripts/Presentation/TestZoneArenaSkin.cs
- **Бренд и boot title card (splash, иконки сборки)** — Между стартом сессии и главным меню показывается boot title card: GameFlow зовёт ITitleCardPresenter.ShowAsync, тот публикует OpenTitleCardRequest с колбэком закрытия и ждёт dismiss через UniTaskCompletionSource — тот же publish/await-паттерн, что у экрана исхода. Без слушателя UI (headless, тесты) ожидание завершается сразу, так что карточка не вешает автоматику. В Assets/_Project/Art/Brand легли фирменные растры: иконки приложения 512/1024, SplashLogo_HappyGuildmasters, CompanyLogo_Alebardium, вариации маскота; ProjectSettings прописан productName «Happy Guildmasters» и логотипы в m_SplashScreenLogos при включённом Unity splash.
  Ключевое: `ITitleCardPresenter / TitleCardPresenter`, `OpenTitleCardRequest (readonly struct, OnDismiss)`, `TitleCardScreenView`, `ProjectSettings: productName = Happy Guildmasters, m_SplashScreenLogos`
  Код: Assets/_Project/Scripts/Game/Flow/TitleCardPresenter.cs, Assets/_Project/Scripts/Guild/TitleCardMessages.cs, Assets/_Project/Scripts/UI/TitleCardScreenView.cs, Assets/_Project/Scripts/Game/Services/GameFlow.cs, Assets/_Project/Art/Brand/, ProjectSettings/ProjectSettings.asset
- **Пайплайн per-unit ViewPrefab, аудит анимаций и визуальный каталог (Alebardium/Visuals)** — Повторяемый редакторный пайплайн визуала юнитов: BuildUnitViewPrefabs режет спрайт-листы → клипы → AnimatorOverrideController поверх UnitBase.controller → UnitVisual → Prefab Variant от UnitView.prefab → проводка ViewPrefab в Relic/Enemy SO. AuditUnitAnimations проходит по десятку спрайт-паков и сверяет наличие клипов, ExportUnitVisualCatalog собирает PNG-контактку idle-визуалов в масштабе рекомендованного гизмо (120 px на метр, ~1.7 м фигура) в Art/Dev. В Content Hub есть вкладка Visual: портрет, проигрывание клипов по спрайт-кадрам без рига, масштаб, lineup. Отдельно закрыт единый источник тинта тела юнита — поле в UnitData, которое читают и боевой CombatPresenter, и карточка инвентаря через RelicCardVisualRig.
  Ключевое: `BuildUnitViewPrefabs.BuildAll (MenuItem Alebardium/Visuals/Build Per-Unit View Prefabs, priority 500)`, `AuditUnitAnimations.Run (priority 501)`, `ExportUnitVisualCatalog.Export (priority 502)`, `ContentHubWindow.BuildVisualPreview`, `ClipSpriteFrames`, `UnitData.ViewPrefab`, `RelicCardVisualRig`
  Код: Assets/_Project/Scripts/EditorTools/ContentHub/BuildUnitViewPrefabs.cs, Assets/_Project/Scripts/EditorTools/ContentHub/AuditUnitAnimations.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ExportUnitVisualCatalog.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Visual.cs, Assets/_Project/Scripts/EditorTools/ContentHub/Preview/ClipSpriteFrames.cs, Assets/_Project/Scripts/Data/Definitions/UnitData.cs, Assets/_Project/Scripts/UI/Components/RelicCardVisualRig.cs
- **Визуал расстановки: единый силуэт юнита и drag-призрак** — UnitSilhouette — один источник того, «что сейчас в руке» при перетаскивании: из живого вида юнита на поле (FromView, спрайт/флип/масштаб/офсет от точки ног) и из боевого UnitData.ViewPrefab, когда юнита на поле ещё нет и тащат реликвию из инвентаря (FromPrefab читает спрайт и масштаб прямо из префаба, без инстанса). Отрисовка призрака при этом ровно одна — DeploymentView.SetGhost, так что вид призрака меняется в одном месте для всех сценариев. Рядом живут круги-подошвы под юнитами, зона хвата и подсветка вражеской зоны — визуал фазы расстановки, доведённый серией раундов play-QA (последняя правка 2026-07-26: кольцо-подошва меряется по фигуре, а не по коллайдеру).
  Ключевое: `UnitSilhouette (readonly struct; FromView, FromPrefab, None)`, `DeploymentView.SetGhost`
  Код: Assets/_Project/Scripts/Presentation/UnitSilhouette.cs, Assets/_Project/Scripts/Presentation/DeploymentView.cs
- **Дизайн-система токенов «тёплый свет» (3 яруса USS)** — Трёхъярусная система стилей: primitives (сырые рампы ink/ember/brass/parchment/danger + акцентные moss/storm/wine, шкала space-0..6) → semantic (роли: поверхности, рамки, текст, скримы, --gm-color-surface-accent на «углях») → components (единственный слой, задающий вид, 2330 строк .gm-*). Прыгать через ярус запрещено: компоненты консюмят только семантику. Внутри components.uss живут семейство кнопок (.gm-button / --primary / --fill / :focus / .unity-disabled, с явными перебивками темы Unity через .unity-button.gm-button), ярус шрифтов и посекционная разметка экранов. Палитра переведена в «тёплый свет» решениями 2026-07-25 (ember-ступень под primary; storm уведён в пыльную бирюзу, чтобы не звенеть рядом с латунью).
  Ключевое: `--gm-ink-*/--gm-ember-*/--gm-brass-*/--gm-parchment-*`, `--gm-moss-*/--gm-storm-*/--gm-wine-*`, `--gm-space-0..6`, `--gm-color-surface-accent`, `--gm-color-scrim / --gm-color-scrim-soft`, `.gm-button / .gm-button--primary / --fill`, `GuildmasterRuntimeTheme.tss (порядок импорта ярусов)`
  Код: Assets/_Project/UI/Theme/tokens.primitives.uss, Assets/_Project/UI/Theme/tokens.semantic.uss, Assets/_Project/UI/Theme/components.uss, Assets/_Project/UI/Theme/theme.uss, Assets/_Project/UI/Theme/GuildmasterRuntimeTheme.tss, Assets/_Project/UI/AGENTS.md
- **Единый источник контуров --gm-outline-*** — Решение Макса 2026-07-25: в проекте ровно три разновидности контура — strong (панели, видео-вставка, таро-карта, primary-кнопка, активный таб), subtle (поля ввода, вторичные кнопки, слоты, утопленные блоки), inner (вторая кайма внутри уже обведённого блока). Заводить четвёртую нельзя. Пиксель-рамки Honeti 9-slice (WindowStroke/GoldOutlined/Inactive) выведены из роли контура — при -unity-slice-scale они давали ~16px кайму; заливки Honeti (Gold2) остались как ФОН. Отдельно зафиксированы контрасты: --gm-color-border-subtle переведён с ink-300 (контраст 1.67, «тихий» читалось как «невидимый») на ink-100 (3.43).
  Ключевое: `--gm-outline-width`, `--gm-outline-color`, `--gm-outline-color-subtle`, `--gm-outline-color-inner`, `--gm-outline-color-hover`
  Код: Assets/_Project/UI/Theme/tokens.semantic.uss, Assets/_Project/UI/Theme/components.uss
- **Библиотека кастом-контролов gm-* (UITK composites)** — 14 [UxmlElement]-контролов, покрывающих повторяющуюся СТРУКТУРУ (по договору AGENTS.md: атомы = классы, композиты = кастом-контролы). Среди них нетривиальные: SlantedPanel и SlantedChip рисуют скошенную ленту через Painter2D (9-slice «плывёт» при смене ширины) и берут цвета из custom-свойств USS (--gm-slant-fill/stroke/width), а не из background-color; RelicCardVisualRig держит живой рендер юнита на карточке; UiDragDrop — драг реликвии из грида на юнита арены; Slot/SliderRow/ToggleRow/ModalPanel/RelicCard/VesselCard/Tooltip/TooltipCard — типовые.
  Ключевое: `Chip`, `SlantedChip`, `SlantedPanel`, `AspectBox`, `ModalPanel`, `Slot`, `SliderRow`, `ToggleRow`, `RelicCard`, `RelicCardVisualRig`, `VesselCard`, `Tooltip`, `TooltipCard`, `UiDragDrop`
  Код: Assets/_Project/Scripts/UI/Components/
- **AspectBox — соотношение сторон как механика** — Контрол, выводящий высоту из фактической ширины: в UI Toolkit нет aspect-ratio, и до него пропорции стояли в USS числами с комментарием «320 × 9/16 = 180». Атрибут Aspect записан словами («16:9»), нераспознанное значение откатывается к 16:9 с предупреждением. Внутри два неочевидных инварианта: style.flexShrink = 0 держится в КОДЕ, а не в USS (с дефолтным flex-shrink родитель ужимал блок и пропорция молча терялась — 220 вместо 236 на 420px), и порог Epsilon = 0.5px против бесконечного дрожания лэйаута, потому что присваивание высоты внутри GeometryChangedEvent само вызывает новый прогон.
  Ключевое: `AspectBox`, `AspectBox.Aspect`, `AspectBox.Epsilon`
  Код: Assets/_Project/Scripts/UI/Components/AspectBox.cs
- **gm-chip — единый компонент «иконка + подпись»** — Один компонент на три роли, сведённые в коммите c848ca7d: фильтры инвентаря, теги юнита и табы ленты режимов (модификатор .gm-chip--collapsible). Структура — иконка (VisualElement) + Label, обе pickingMode = Ignore, чтобы pick доставался самому чипу. Иконку даёт код (SetIcon(Sprite)) или класс-модификатор; состояние — SetActive → .gm-chip--active. Отдельная готча зафиксирована коммитом 8b253855: с чипов снят transition как известный триггер залипания состояний.
  Ключевое: `Chip`, `Chip.SetIcon`, `Chip.SetActive`, `.gm-chip / __icon / __label / --active / --collapsible`
  Код: Assets/_Project/Scripts/UI/Components/Chip.cs, Assets/_Project/Scripts/UI/Components/SlantedChip.cs, Assets/_Project/UI/Theme/components.uss
- **Экран лоадаута/инвентаря — трёхколоночник с таро-карточками** — Полноэкранный трёхколоночник (539 строк сборки из UXML): слева боевая зона-«дырка» СКВОЗЬ панель к реальной камере, в центре грид таро-пропорциональных карточек реликвий с поиском/сортировкой/фильтрами-чипами, справа панель деталей (видео 16:9 через AspectBox, способности, улучшения, нарратив, статблок, чипы-теги с разделителями «/»). Несёт неочевидный инвариант ввода: корень/тело/боевая дырка стоят pickingMode = Ignore, и им НЕЛЬЗЯ ставить Position — он перехватывает pick на весь экран и глушит дырку, после чего под инвентарём перестаёт стартовать деплой-драг.
  Ключевое: `LoadoutInventoryView.Build`, `LoadoutViewModel`, `LoadoutHubViewModel`, `RelicDragPhase`, `gm-loadout__* (components.uss)`
  Код: Assets/_Project/Scripts/UI/LoadoutInventoryView.cs, Assets/_Project/Scripts/UI/LoadoutViewModel.cs, Assets/_Project/Scripts/UI/LoadoutHubView.cs, Assets/_Project/Scripts/UI/LoadoutHubViewModel.cs, Assets/_Project/UI/Screens/LoadoutInventoryScreen.uxml, Assets/_Project/UI/Screens/RelicArcanaCard.uxml, Assets/_Project/UI/Screens/LoadoutHubScreen.uxml
- **Boot title card — карточка бренда до главного меню** — Полноэкранная ink-карточка с печатью и Cormorant-надписью «Happy Guildmasters», показывается на загрузке до главного меню; закрывается кликом или авто-таймером (~2.2 с). Добавлена коммитом 7a44fe1f вместе со splash-ассетами 512/1024.
  Ключевое: `TitleCardScreenView`, `.gm-titlecard (components.uss)`
  Код: Assets/_Project/Scripts/UI/TitleCardScreenView.cs, Assets/_Project/UI/Screens/TitleCardScreen.uxml, Assets/_Project/UI/Theme/components.uss
- **MenuRouter + UiNavigator: стек экранов и слои** — Стек типизированных экранов (Page/Modal/Sheet) + слои-контейнеры с фиксированным z; ввод и видимость вычисляются как f(верх стека, фаза боя). Система ОПИСАНА в вике, но дока отстала от кода на две вещи: (1) UiRootBootstrap.cs:272-287 создаёт ДЕВЯТЬ слоёв — после layer-system добавлен layer-transition под шторку перехода, а док перечисляет восемь [0]-[7]; (2) док утверждает «ссылки на слои [5]-[7] пока не хранятся», тогда как _layerTooltip уже сохраняется (UiRootBootstrap.cs:278) под Трек Т.
  Ключевое: `UiNavigator`, `ScreenKind`, `UiScreen`, `MenuRouter`, `RunModeBarView`, `UiRootBootstrap.AddLayer`
  Код: Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs, Assets/_Project/Scripts/UI/Navigation/ScreenKind.cs, Assets/_Project/Scripts/UI/Navigation/UiScreen.cs, Assets/_Project/Scripts/UI/Navigation/UiScreenContext.cs, Assets/_Project/Scripts/UI/MenuRouter.cs, Assets/_Project/Scripts/UI/UiRootBootstrap.cs, Assets/_Project/Scripts/UI/RunModeBarView.cs
- **Шов UI↔презентация: MenuVisibilityMessages и шторка перехода** — Три readonly-struct сообщения MessagePipe, намеренно живущие в Core (а не в Guild), потому что Presentation на сборку Guild не ссылается: MainMenuVisibilityChangedEvent (меню на экране → под него подкладывается тот же стол, что под картой акта), ScreenBackdropChangedEvent (единый владелец задника — материал стола из MapStyle; своей непрозрачной заливки у UI больше нет, QA #50) и ScreenFadeChangedEvent (плотность 0..1 + точка схлопывания в UV экрана). Шторку рисует UI-слой: UiRootBootstrap гоняет материал перехода в RenderTexture (высота 360, ширина по аспекту) и вешает её фоном на .gm-screen-fade — потому что затемнить ВЕСЬ кадр вместе с топбаром и панелями может только UITK, мировой квад гасил лишь карту (QA #47).
  Ключевое: `MainMenuVisibilityChangedEvent`, `ScreenBackdropChangedEvent`, `ScreenFadeChangedEvent`, `UiRootBootstrap.ApplyScreenFade`, `UiRootBootstrap.EnsureFadeTexture`, `MenuBackdropView`
  Код: Assets/_Project/Scripts/Core/Flow/MenuVisibilityMessages.cs, Assets/_Project/Scripts/UI/UiRootBootstrap.cs, Assets/_Project/Scripts/Presentation/Map/MenuBackdropView.cs, Assets/_Project/Art/Shaders/SH_Map_Transition.shader
- **Ритм узлов забега: RunBeatStage и фаза Interlude** — Шов между петлёй акта и МИРОМ на стыках узлов. ActRunner решает «узел кончился», а IRunBeatStage знает, как при этом выглядит арена: EnterRestBeat() возвращает мир (IBattleSession.RequestReset), ставит BattlePhase.Interlude и показывает кнопки передышки, EnterNode() уводит мир на второй план (BattlePhase.None). Ключевое: петля НЕ ждёт кнопок — узел уже засчитан, а ct снимает кнопки, когда игрок выбрал следующий узел. Кнопки лишь публикуют интенты, которые и так висят на табах (SetWorldMapRequest / SetFormationRequest). В BattleClock.cs появилась четвёртая фаза Interlude = 3 с прямым следствием для UI: мир на экране → непрозрачный задник ЗАПРЕЩЁН.
  Ключевое: `IRunBeatStage`, `RunBeatStage`, `BattlePhase.Interlude`, `ActRunner`, `IContinuePresenter`, `SetWorldMapRequest`, `SetFormationRequest`
  Код: Assets/_Project/Scripts/Game/Flow/RunBeatStage.cs, Assets/_Project/Scripts/Game/Services/ActRunner.cs, Assets/_Project/Scripts/Data/Definitions/BattleClock.cs, Assets/_Project/Scripts/Game/Flow/ContinuePresenter.cs, Assets/_Project/Scripts/Game/Flow/BattleNodeFlow.cs, Assets/_Project/UI/Screens/ContinueScreen.uxml
- **IScreenTransition — «моргание» между кадрами** — Владелец перехода, живущий ОТДЕЛЬНО от заказчика — в этом весь смысл (QA #53): раньше три фазы вёл сам WorldMapView, но выбор узла, засчитанный на закрытом кадре, уводил игрока с карты, карта скрывалась в середине собственного перехода, и игрок видел только закрытие. ScreenTransitionRunner — не MonoBehaviour и не житель сцены, тикает от корневого скоупа по НЕмасштабированному времени (пауза боя и хрономант на смену кадра влиять не должны). Форма перехода — ScreenTransitionShape (in/hold/out + FocusUv, точка схлопывания едет к центру вместе с закрытием). Закрытие идёт с ускорением (t*t), открытие тормозит (1-(1-t)²). Второй заказ поверх идущего перехода игнорируется — побеждает первый. Есть Tick(float) отдельно от Tick() именно ради тестов.
  Ключевое: `IScreenTransition`, `ScreenTransitionShape`, `ScreenTransitionRunner`, `ScreenTransitionRunner.Cancel`, `ScreenTransitionShape.Centered`
  Код: Assets/_Project/Scripts/Core/Flow/IScreenTransition.cs, Assets/_Project/Scripts/Presentation/Transition/ScreenTransitionRunner.cs, Assets/_Project/Scripts/Game/RootLifetimeScope.cs, Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs, Assets/_Project/Tests/EditMode/Presentation/ScreenTransitionRunnerTests.cs
- **UI-тесты инвариантов раскладки и политик** — Четыре фикстуры, ловящие то, что раньше ловилось только глазами. LoadoutLayoutInvariantsTests наследует UITestFixture (UnityEngine.UIElements.TestFramework), поднимает настоящий UXML + тему на панели 1920×1080 и сверяет края тулбара с краями карточек с допуском 0.5px — потому что USS посчитать это не может (в UI Toolkit нет calc() и математики над переменными), а находка «4px» возвращалась три раунда подряд. ScrimPolicyTests держит договор «класс скрима ставит ОДИН владелец» и «носитель .gm-screen ищется внутри контейнера, а не красится сам контейнер». UiNavigatorTests (20 тестов) — стек и вычисление ввода. Ключевая готча зафиксирована в коде: panelSize и themeStyleSheet присваиваются в КОНСТРУКТОРЕ фикстуры, в [SetUp] уже поздно — панель создана, и экран считался бы без единого стиля (карточка выходила 1907×30 вместо 132×227).
  Ключевое: `UITestFixture`, `LoadoutLayoutInvariantsTests`, `ScrimPolicyTests`, `UiNavigatorTests`, `ScreenTransitionRunnerTests`
  Код: Assets/_Project/Tests/EditMode/LoadoutLayoutInvariantsTests.cs, Assets/_Project/Tests/EditMode/ScrimPolicyTests.cs, Assets/_Project/Tests/EditMode/UI/UiNavigatorTests.cs, Assets/_Project/Tests/EditMode/Presentation/ScreenTransitionRunnerTests.cs
- **TMP Static-bake шрифты (EN+RU) и защита от git-churn** — Игровые шрифты запечены в Static-атлас (m_AtlasPopulationMode: 0 в FiraSans-Regular SDF.asset и соседях) вместо Dynamic — Dynamic дописывал глифы в ассет на каждом прогоне и давал сотни тысяч строк диффа. Подстраховка в .gitattributes: `*SDF*.asset -diff -merge` (одна строка диффа, без авто-мержа в мусор). Ярус шрифтов живёт в components.uss, а НЕ отдельным файлом, потому что тема .tss не подхватывала 4-й @import; тело — Fira Sans, заголовки — Cormorant Garamond Medium, и правило .unity-text-element бьёт дефолт-тему Unity порядком импорта. Отдельная готча: -unity-font-definition требует TextCore FontAsset, а не .ttf.
  Ключевое: `m_AtlasPopulationMode: 0`, `*SDF*.asset -diff -merge`, `-unity-font-definition`, `.unity-text-element`
  Код: Assets/_Project/UI/Fonts/, Assets/_Project/UI/Theme/components.uss, .gitattributes
- **Витрина компонентов (UI gallery)** — Dev-каталог всех .gm-*-компонентов и свотчей токенов в одном экране, открывается пунктом Alebardium/UI Preview/Component Gallery (цель "gallery" в UiPreviewCatalog.cs:37 → BuildGallery). Служит визуальной приёмкой дизайн-системы, и договор жёсткий: образцы палитры ссылаются на токены КЛАССАМИ, ни одного числа в разметке — если правка яруса токенов не видна на витрине, сломана витрина, а не палитра. Коммитом 46c99414 на витрину выведены и состояния (hover/active/disabled/focus).
  Ключевое: `UiPreviewCatalog`, `UiPreviewMenu`, `UiPreviewHost`, `BuildGallery`, `RelicCardVisualRig`
  Код: Assets/_Project/UI/Screens/UiGalleryScreen.uxml, Assets/_Project/UI/Screens/UiGalleryScreen.uss, Assets/_Project/Scripts/DevTools/UiPreviewCatalog.cs, Assets/_Project/Scripts/DevTools/UiPreviewMenu.cs
- **Тултип-система (Трек Т)** — Восемь файлов (ITooltipContentFactory, TooltipContentFactory, TooltipEvents, TooltipManipulator, TooltipPlacement, TooltipRequest, TooltipSystem) + TooltipCard. Слой layer-tooltip под неё уже создан и сохраняется в UiRootBootstrap.cs:278. ВАЖНО: вся папка Scripts/UI/Tooltips/ и TooltipCard.cs числятся untracked в git — код существует только в рабочем дереве, ни один коммит их не содержит (git ls-files по папке пуст). Содержание системы по факту кода я не читала — только состав файлов.
  Ключевое: `TooltipSystem`, `TooltipManipulator`, `TooltipPlacement`, `ITooltipContentFactory`, `TooltipRequest`, `TooltipEvents`, `TooltipCard`
  Код: Assets/_Project/Scripts/UI/Tooltips/, Assets/_Project/Scripts/UI/Components/Tooltip.cs, Assets/_Project/Scripts/UI/Components/TooltipCard.cs
- **План UI-архитектуры (docs/ui-architecture-rework-plan.md) — жив, но частично отработан** — План на ~1050 строк: диагноз (Часть I), целевая архитектура трёх слоёв (Часть II, 15 разделов), пофазный план Ф0-Ф7 + треки Д/К/Х/Т/П/С/Д-о (Часть III). Проверка по коду: Ф0-Ф7 РЕАЛИЗОВАНЫ (UiNavigator.cs + ScreenKind.cs существуют, слои-контейнеры в UiRootBootstrap.cs:272-287, RunModeBarView.cs жив, IMenuRouter — grep по Scripts даёт ноль). Трек Д (дизайн-система) РЕАЛИЗОВАН коммитами 24-26.07 (tokens.primitives/semantic, gm-chip, галерея, --gm-outline-*). Трек Т (тултипы) В РАБОТЕ, код untracked. Трек К (своя DevConsole) НЕ НАЧАТ — QFSW.QC жив в трёх файлах DevTools и упомянут как текущий в IInputService.cs:24, DevCommandRegistry не существует. Треки С и Д-о (Descriptor) НЕ НАЧАТЫ — grep по Descriptor в Scripts даёт ноль. Треки Х (хоткеи/rebinding) и П (панель юнита) я НЕ проверяла — статус unknown, проверять по IInputService/карте UI-действий и по наличию экрана панели юнита.
  Ключевое: `Ф0-Ф7 (реализованы)`, `Трек Д (реализован)`, `Трек Т (в работе, untracked)`, `Трек К (не начат, QFSW жив)`, `Трек С / Д-о (не начаты)`, `Трек Х / П (unknown, не проверяла)`
  Код: docs/ui-architecture-rework-plan.md, docs/ui-architecture-rework-progress.md, Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs, Assets/_Project/Scripts/UI/UiRootBootstrap.cs, Assets/_Project/Scripts/DevTools/GuildmasterCommands.cs
- **Единый корень Editor-меню Alebardium (реальная раскладка + приоритеты)** — Всё редакторное меню собрано под корень Alebardium/ (правило заведено коммитом e7f07d78, 2026-07-20). Реальный грепом набор — 21 [MenuItem], все начинаются с 'Alebardium/'. Правило соблюдается. Но с момента написания дока добавились ДВЕ новые группы, которых в доке нет: Alebardium/Visuals/* (3 пункта, priority 500/501/502 — BuildUnitViewPrefabs, AuditUnitAnimations, ExportUnitVisualCatalog) и Alebardium/Test/* (2 пункта, priority 500/501 — TestPlayMenu.BuildAndRun и ToggleMaximizedGameView с хоткеем %#g, коммит 52d9bec9 от 2026-07-24). Обе группы взяли одну и ту же сотню 500, т.е. правило «новая группа = следующая свободная сотня» из дока уже нарушено кодом.
  Ключевое: `TestPlayMenu`, `BuildUnitViewPrefabs`, `AuditUnitAnimations`, `ExportUnitVisualCatalog`, `ContentHubWindow`, `PaletteRemapWindow`, `BalanceMenu`
  Код: Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.cs, Assets/_Project/Scripts/EditorTools/PaletteRemap/PaletteRemapWindow.cs, Assets/_Project/Scripts/EditorTools/UI/TestPlayMenu.cs, Assets/_Project/Scripts/EditorTools/ContentHub/BuildUnitViewPrefabs.cs, Assets/_Project/Scripts/EditorTools/ContentHub/AuditUnitAnimations.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ExportUnitVisualCatalog.cs, Assets/_Project/Scripts/Balance/Editor/BalanceMenu.cs, Assets/_Project/Scripts/Data/Editor/ContentDatabaseSync.cs, Assets/_Project/Scripts/DevTools/UiPreviewMenu.cs
- **Гейт битых вики-ссылок: check-wiki-links.ps1 + docs-lint.yml** — PowerShell-линтер целостности внутренних ссылок Obsidian-vault docs/wiki: парсит [[wiki/alias]] и относительные [text](target), режет fenced/inline код, снимает экранирование \/ внутри таблиц, резолвит по правилам Obsidian (partial-path с '/', короткое имя без '/'), пропускает http/mailto/obsidian/якоря/абсолютные пути и выходы за корень vault. Exit 0 — чисто, exit 1 — печатает список битых. ПРОВЕРЕНО ЗАПУСКОМ: pwsh ./scripts/check-wiki-links.ps1 -VaultPath docs/wiki → exit 0, «184 .md проверено». Workflow docs-lint.yml гоняет его на ubuntu-latest/pwsh на pull_request и на push в dev/master, path-фильтр docs/wiki/**, сам скрипт, сам workflow. Гейт = падает job при любой битой ссылке (никаких continue-on-error).
  Ключевое: `Test-WikiLink`, `Test-MdLink`, `Resolve-RelPath`, `Remove-Code`
  Код: scripts/check-wiki-links.ps1, ' .github/workflows/docs-lint.yml'
- **Сайт документации: docs.yml (Quartz v4.5.2 + Doxygen)** — Workflow docs.yml: клон Quartz v4.5.2 → подмена quartz.config.ts/quartz.layout.ts из quartz-config/ → docs/wiki копируется в quartz-build/content → npx quartz build -o ../site; затем apt-get install doxygen graphviz и doxygen Doxyfile из папки doxygen (Doxyfile: OUTPUT_DIRECTORY=../site/api, HTML_OUTPUT=., INPUT=../Assets/_Project/Scripts) → деплой peaceiris/actions-gh-pages@v4 на gh-pages. Тема — git-сабмодуль doxygen/doxygen-awesome-css (checkout идёт с submodules: recursive). Триггеры: push в dev/master по путям docs/wiki, Assets/_Project/Scripts, quartz-config, doxygen, сам workflow + workflow_dispatch. Всё сходится с тем, что описано в доке.
  Код: ' .github/workflows/docs.yml', quartz-config/quartz.config.ts, quartz-config/quartz.layout.ts, doxygen/Doxyfile, doxygen/doxygen-awesome-css
- **statdb.ps1 — правка статов в YAML-ассетах мимо Unity** — PowerShell-тул (требует pwsh 7): читает и правит список _stats прямо в .asset YAML реликвий и врагов, без Unity. Команды list/get/set/scale/migrate; set пишет Override (Op 3), migrate конвертит Flat-дельты в абсолютные Override с множителем на «магнитудные» статы (MaxHP id 0, AutoAttackDamage id 7). Дефолты подтягивает из Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset. КРИТИЧНО: перечень имён StatType (30 штук) и список NaturalOneIds захардкожены в скрипте копией с Assets/_Project/Scripts/Data/Stats/StatType.cs — при добавлении стата в enum скрипт молча начнёт врать индексами. В шапке скрипта честно предупреждает про гонку с открытым в инспекторе ассетом.
  Ключевое: `Resolve-StatId`, `Get-BaseDefaults`, `Read-Effective`, `Write-StatValue`
  Код: scripts/statdb.ps1
- **Write-сторона авторинга контента: ContentEditService / ContentCrudService** — Editor-only слой записи в контент-SO, разделённый на две ответственности (прямо зафиксировано в XML-докстринге ContentEditService): ContentCrudService владеет жизненным циклом ассета (create/duplicate/delete + id), ContentEditService правит ЗНАЧЕНИЯ полей внутри ассета (статы, кулдауны, поля эффектов). Всё через SerializedObject + Undo; каждая правка возвращает readonly struct Change (Asset/Field/Before/After/Applied/Note) для аудит-лога. Id и схему ContentEditService не трогает. Это write-половина петли баланса read→edit→read (read = SimBench и страница Balance хаба). Рядом — ContentDatabaseSync (меню Alebardium/Data/Sync Content Database, priority 400).
  Ключевое: `ContentEditService`, `ContentEditService.Change`, `ContentCrudService`, `ContentIdUtility`, `ContentPaths`, `ContentDatabaseSync`
  Код: Assets/_Project/Scripts/Data/Editor/ContentEditService.cs, Assets/_Project/Scripts/Data/Editor/ContentCrudService.cs, Assets/_Project/Scripts/Data/Editor/ContentIdUtility.cs, Assets/_Project/Scripts/Data/Editor/ContentPaths.cs, Assets/_Project/Scripts/Data/Editor/ContentLocalization.cs, Assets/_Project/Scripts/Data/Editor/ContentDatabaseSync.cs
- **Content Hub — реальное состояние окна** — Одно UITK-окно (меню Alebardium/Content Hub, priority 0), partial-класс из 9 файлов + Core (ContentIndex, ContentValidationService, ConfigDiff, StatMath, StatCohort, NavHistory, MarkdownTable, ContentIndexPostprocessor). Фактический enum Page = { Browser, Balance, Audio, Doctor, Configs } — ПЯТЬ страниц, плюс динамические pill-табы по доменам SO (строятся из индекса, не из enum), плюс ConfigLikeDomains { Configs, Design, Audio } едут на страницу Configs. Coverage и Visual отдельными табами НЕ являются: ContentHubWindow.Coverage.cs даёт BuildCoverageSummary(VisualElement), ContentHubWindow.Visual.cs — BuildVisualPreview(inner, UnitVisual), то есть это встроенные секции, а не страницы. Состояние (_page, _selectedGuid) переживает domain reload; RebuildContent на OnFocus.
  Ключевое: `ContentHubWindow`, `ContentHubWindow.Page`, `ContentIndex`, `ContentValidationService`, `StatMath`, `StatCohort`, `NavHistory`, `ConfigDiff`, `MarkdownTable`, `HubToasts`
  Код: Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Browser.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Balance.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Audio.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Doctor.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Configs.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Coverage.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Visual.cs, Assets/_Project/Scripts/EditorTools/ContentHub/ContentHubWindow.Navigation.cs, Assets/_Project/Scripts/EditorTools/ContentHub/Core/ContentIndex.cs
- **Арт-пайплайн Aseprite → Photoshop → Unity (костяная анимация)** — Два Lua-скрипта для Aseprite (коммит b671454a, 2026-07-25) плюс вендоренный Tsukina export_as_psd. export_bone_psd.lua собирает Photoshop-ready .psd под Unity 2D Animation PSB-пайплайн: клон спрайта, дроп слоёв с префиксами #/_ , flatten групп '@Name', полный canvas без trim, nearest-скейл ×10 по умолчанию, дублирование групп Arm/Leg в 'Arm (left)'/'Arm (right)'. README описывает установку через directory junction в %APPDATA%\Aseprite\scripts и конвенцию имён слоёв. На стороне Unity парный шов — BonePartSpritePostprocessor (AssetPostprocessor в asmdef Guildmaster.PaletteRemap.Editor): на ПЕРВЫЙ импорт текстур под Assets/_Project/Art/Sprites/Bone Animations/ применяет BonePartSprite.preset (importSettingsMissing-гард, чтобы ре-экспорт PNG не сбивал уже настроенный .meta).
  Ключевое: `BonePartSpritePostprocessor`, `export_bone_psd.lua`, `export_bone_parts.lua`
  Код: Aseprite/scripts/export_bone_psd.lua, Aseprite/scripts/export_bone_parts.lua, Aseprite/scripts/README.md, Aseprite/scripts/vendor/export_as_psd.lua, Assets/_Project/Scripts/EditorTools/PaletteRemap/BonePartSpritePostprocessor.cs
- **Palette Remapper (gradient-map перекраска растрового арта)** — EditorWindow (меню Alebardium/Palette Remapper, priority 1): перекрашивает растровые пиксель-арт спрайты в нашу палитру методом gradient-map по яркости, рампа ink → brass → parchment строится по умолчанию из tokens.primitives.uss. Параметры: _ramp (Gradient), _normalize (растянуть яркость по фактическому min..max спрайта), _alphaThreshold=0.01, _applyPixelImport (настроить результат как pixel-perfect Sprite — Point, без компрессии и мипов), _outputFolder='Assets/_Project/Art/UI/Honeti-gm', _suffix='_gm'. Editor-only, свой asmdef Guildmaster.PaletteRemap.Editor (includePlatforms: Editor, references пуст).
  Ключевое: `PaletteRemapWindow`
  Код: Assets/_Project/Scripts/EditorTools/PaletteRemap/PaletteRemapWindow.cs, Assets/_Project/Scripts/EditorTools/PaletteRemap/Guildmaster.PaletteRemap.Editor.asmdef
- **CI-пайплайн ci.yml: paths-filter + единый CI Gate** — Три job'а. changes — dorny/paths-filter@v3, выставляет outputs.code=true, если тронуто Assets/**, Packages/**, ProjectSettings/** или сам ci.yml. test — запускается только при code==true: game-ci/unity-test-runner@v4 на Unity 6000.4.8f1 (матрица), сначала editmode, затем playmode, кэш Library по hashFiles тех же путей, checkout с lfs:true, env FORCE_JAVASCRIPT_ACTIONS_TO_NODE24, артефакт test-results/. ci-gate — единственный required-статус: if: always(), падает только если code==true И test.result != success; при docs-only изменениях test скипается, а гейт проходит мгновенно. Локальный аналог — scripts/run-tests.ps1: версию редактора берёт из ProjectSettings/ProjectVersion.txt (не хардкод), гоняет Unity.exe -runTests -batchmode -quit, кладёт XML в TestResults/.
  Код: ' .github/workflows/ci.yml', scripts/run-tests.ps1
- **Тестовый набор: состав, сборки, покрытие** — 78 .cs-файлов тестов, ~519 атрибутов [Test]/[TestCase*]/[UnityTest]. EditMode — 77 файлов, разложены по папкам Audio(1), Balance(1), Combat(36), Content(5), ContentHub(6), Core(3), Guild(13), Presentation(4), Run(5), UI(1) + два корневых (LoadoutLayoutInvariantsTests, ScrimPolicyTests). PlayMode — один файл, Battle/BattleIntegrationTest.cs. ТРИ тестовые сборки, а не две: Guildmaster.Tests.EditMode, Guildmaster.Tests.PlayMode и отдельная Guildmaster.Balance.Tests (includePlatforms Editor, defineConstraints UNITY_INCLUDE_TESTS, autoReferenced false, ссылается на Balance + Balance.Editor + Combat + Core + Data). Гоняются через Unity MCP run_tests, ./scripts/run-tests.ps1 или ci.yml.
  Ключевое: `Guildmaster.Tests.EditMode`, `Guildmaster.Tests.PlayMode`, `Guildmaster.Balance.Tests`
  Код: Assets/_Project/Tests/EditMode, Assets/_Project/Tests/PlayMode, Assets/_Project/Tests/EditMode/Guildmaster.Tests.EditMode.asmdef, Assets/_Project/Tests/EditMode/Balance/Guildmaster.Balance.Tests.asmdef, Assets/_Project/Tests/PlayMode/Guildmaster.Tests.PlayMode.asmdef
- **Сборки Balance и PaletteRemap.Editor отсутствуют в карте asmdef** — В проекте 21 asmdef. Guildmaster.Balance (runtime-слой, references Core+Data) и Guildmaster.Balance.Editor (includePlatforms Editor, references Balance+Core+Data+Combat; внутри SimBench, SimEnvironment, MetricCollector, ReportWriter, ContentAuditor, SyntheticUnits, бенчи DpsBench/SurvivabilityBench/DuelMatrixBench/ScenarioBench, Rating/BradleyTerry) появились коммитом 41b6ff7a 2026-07-18. Guildmaster.PaletteRemap.Editor — 2026-07-18 и дополнена 2026-07-25. Ни одной из трёх нет в таблицах assemblies.md (грепом 'Balance' и 'PaletteRemap' по файлу — ноль совпадений).
  Ключевое: `Guildmaster.Balance`, `Guildmaster.Balance.Editor`, `Guildmaster.PaletteRemap.Editor`, `SimBench`, `BradleyTerry`, `MetricCollector`, `ReportWriter`, `ContentAuditor`
  Код: Assets/_Project/Scripts/Balance/Guildmaster.Balance.asmdef, Assets/_Project/Scripts/Balance/Editor/Guildmaster.Balance.Editor.asmdef, Assets/_Project/Scripts/EditorTools/PaletteRemap/Guildmaster.PaletteRemap.Editor.asmdef, Assets/_Project/Scripts/Balance/Editor/SimBench.cs, Assets/_Project/Scripts/Balance/Editor/BalanceMenu.cs
- **Идентичность билда: Build Profile Windows, defines, брендинг, Alebardium/Test** — Появился Unity 6 Build Profile Assets/Settings/Build Profiles/Windows.asset (коммит 3ca05e0a, 2026-07-24; правка e3d553fc): m_BuildTarget 19, m_HasScriptingDefines: 1 с единственным дефайном MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED, m_Development 0, m_OverrideGlobalSceneList 0. Важная готча: профиль ЗАМЕЩАЕТ глобальные Standalone-дефайны, а глобальный набор Standalone в ProjectSettings шире (ODIN_INSPECTOR*, UNITY_VISUAL_SCRIPTING, ES3_TMPRO, ES3_UGUI, MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED). Там же зафиксирована идентичность продукта: companyName Alebardium, productName 'Happy Guildmasters', applicationIdentifier com.Alebardium.HappyGuildmasters, скрытый Unity-логотип на сплэше, resizable-окно. Рядом — Alebardium/Test/Build & Run: собирает Development-плеер из включённых сцен Build Settings в Builds/Test/Guildmaster-Test.exe (папка в .gitignore) с AutoRunPlayer, чтобы оценивать масштаб UI на живом мониторе.
  Ключевое: `TestPlayMenu`
  Код: Assets/Settings/Build Profiles/Windows.asset, ProjectSettings/ProjectSettings.asset, Assets/_Project/Scripts/EditorTools/UI/TestPlayMenu.cs
- **Addressables: профиль-данные и папка Windows/ билд-состояния** — В рабочем дереве появились НЕЗАКОММИЧЕННЫЕ Assets/AddressableAssetsData/ProfileDataSourceSettings.asset (+ .meta) и папка Windows/ с addressables_content_state.bin (файл от 2026-07-25, .meta от 2026-07-24) — это результат первого реального Addressables-билда контента под Windows. Группы существующие: Default Local Group + пять Localization-* (Locales, Assets-Shared, String-Tables-English, String-Tables-Russian). Гвоздь: .gitignore игнорирует под этой папкой ТОЛЬКО *.bundle и *.bundle.meta (строки «# Packed Addressables»), поэтому addressables_content_state.bin в игнор не попадает и уедет в репозиторий при ближайшем git add. Так задумано или нет — по коду и .gitignore определить нельзя.
  Код: Assets/AddressableAssetsData/ProfileDataSourceSettings.asset, Assets/AddressableAssetsData/Windows/addressables_content_state.bin, Assets/AddressableAssetsData/AddressableAssetSettings.asset, ' .gitignore'
- **.gitattributes: сеть под TMP SDF-атласы** — Коммит d075e02c (2026-07-24) добавил в .gitattributes правило `*SDF*.asset -diff -merge` с комментарием: перегенерированный glyph-атлас иначе даёт сотни тысяч строк диффа и склеивается автомержем в мусор; настоящее лекарство — Static-популяция атласа, это лишь страховочная сеть. Остальной файл — Linguist-оверрайды (scripts/*.ps1, *.yml, *.yaml, *.json, *.md как documentation; *.cs → C#; *.shader → GLSL). Git LFS в .gitattributes по-прежнему НЕ настроен.
  Код: ' .gitattributes'

---

## Реестр 3: периферия (33 файла)

| Файл | Вердикт | Труд | Суть |
|---|---|---|---|
| `docs/README.md` | update | S | Оглавление docs/ описывает только 3 из 9 сущностей дерева: 0 из 11 корневых .md-журналов и 4 из 7 подпапок не упомянуты. Отработано 0 пунктов, висит 1 (сама таблица устарела как минимум с 2026-07-10, последний коммит по файлу — 311eccba). |
| `docs/act-map-overhaul-progress.md` | update | M | Живой журнал (99 КБ, 11 фаз D1-D11 + привал + инвентарь-редизайн), но списки «осталось» не чистились. Из ~11 висящих пунктов 5 по коду ОТРАБОТАНЫ, 6 реально висят, ещё 3 — визуальная приёмка Макса (unknown). Держать как журнал, но выкосить закрытое и отделить инвентарную секцию. |
| `docs/damage-model-rework-progress.md` | archive | S | Рефактор закончен: 6 фаз из 6 отработаны и подтверждены кодом, висит 3 хвоста-техдолга (AutoAttackMode.Heal, переписать tag-reference, шов «стихии из эффектов»). Журнал как процесс мёртв — хвосты перенести в tech-changelog/бэклог, файл в архив. |
| `docs/ui-architecture-rework-plan.md` | merge_into_wiki | L | Гибрид: часть III (Ф0-Ф7) исполнена целиком — 8 фаз из 8 подтверждены кодом, это архив; но части II.10-II.15 и треки К/Х/П/Д-о/В — живая непрочитанная спека (5 треков из 8 не реализованы). Разнести: исполненное — в tech-changelog/ui-navigation, неисполненные треки и спеки — в docs/wiki/tech/40-planning; статус-шапка врёт про ветку. |
| `docs/ui-architecture-rework-progress.md` | archive | S | Журнал закрытого реворка: 8 фаз из 8 влиты («РЕВОРК UI ЗАВЕРШЁН, Ф0-Ф7»), 0 висящих фаз. Ценность осталась только в разделе «Готчи исполнителя» — их вынести (в скилл uitk / tech-changelog), сам файл в архив. |
| `docs/persist-battle-qa-findings.md` | keep | M | Самый живой трекер: последняя запись сегодня (2026-07-26, раунд 6, находки #47-#53). Из ~60 пунктов отработаны почти все; висят 2 осознанно отложенных (G — шейдер HitFlash, и хвост #29), плюс 3 ранних пункта (C/D/E) носят статус «не исправлено», хотя по коду давно закрыты. |
| `docs/inventory-ui-qa.md` | keep | M | Трекер жив, но наполовину отработан: раунды 1-4 (32 находки) ПРИНЯТЫ целиком 2026-07-26, висят 2 хвоста раунда 2 и трек «ревизия вёрстки UITK» (из 5 его пунктов сделаны 2 — AspectBox и выравнивание тулбара). Рекомендация: срезать принятые раунды в архив, оставить трек вёрстки как живой. |
| `docs/ui-qa-checklist.md` | archive | S | Одноразовый чеклист приёмки сессии 2026-07-25: пройден целиком 2026-07-26, находок 0, висящих пунктов 0. Ценное — только раздел «Что осталось в бэклоге» (4 пункта), его перенести в бэклог/вики, файл в архив. |
| `docs/character-art-prompts.md` | keep | S | Живой рабочий файл пайплайна «нейросеть → части → скелет»: 6 итераций из 6 отработаны и вкачены в промпты, висит 0 пунктов, но раздел «Что дальше» не отражает, что первый персонаж уже собран. Обновить хвост и держать. |
| `docs/third-party-credits.md` | update | S | Реестр атрибуции жив и нужен, но отстал от ассетов: покрыто 9 иконок (Modes + Filters), не покрыто ~58 (Tags 55 шт + Bar 3 шт) и не указан третий автор (sbed). Отработанных пунктов 2 из 2, висит 1 крупный пробел — это лицензионный риск к релизу. |
| `docs/obsidian-plugins-backup.md` | keep | S | Служебный бэкап-список на 27 плагинов от 2026-07-16; пунктов «к отработке» в нём нет вовсе — это справочник восстановления после чистого клона. Дешёвый, полезный, риск один: список не сверялся 10 дней. |
| `docs/audits/` | keep | S | Три раунда аудитов (2026-07-09 — 6 отчётов, 2026-07-15 — 1 синтез по GDD, 2026-07-19 — 4 отчёта Cursor Grok) + README с методологией. По собственной конвенции папки отчёты — исторический артефакт и не правятся; отработанность их находок трекается вне папки. Висит 1 пункт: README ссылается на несуществующий путь вики. |
| `docs/handoff/2026-07-15 Ночной автономный заход (ветка feat-encounter-data-loader).md` | archive | S | Отчёт-хендофф на 27 КБ по ночному заходу 2026-07-15: ветка feat/encounter-data-loader давно влита, все перечисленные «чего нет» пункты (карта акта, главное меню, магазин, AI-пресеты) с тех пор сделаны. Отработано практически всё, висит 0 адресуемых пунктов — чистый исторический артефакт. |
| `docs/art/ТЗ_пиксель-арт_юниты.md` | archive | S | ТЗ внешнему пиксель-художнику на покадровую анимацию (64×64, idle/walk/attack/hurt/death на ~16 героев). Курс сменён: принята скелетная анимация, покадровка явно отвергнута как неокупаемая. Из 5 «решений за заказчиком» не подтверждено ни одно, документ так и не был запущен в работу. |
| `CLAUDE.md` | update | L | Каркас верен (Unity 6000.4.8f1, VContainer/MessagePipe/UniTask, Input System, Cinemachine, FMOD за IAudioService — всё подтверждено кодом), но фактура протухла в трёх местах: MCP-раздел описывает несуществующий файл и четыре несуществующих сервера, ссылки на вики ведут на удалённую нумерованную схему, а таблица «установлено, но не используется» врёт про джус и про сейвы. Плюс структура проекта отстала на несколько контуров и на весь слой скиллов. |
| `.cursor/rules/project-context.mdc` | update | M | alwaysApply-правило с самой плотной концентрацией дрейфа: таблица MCP описывает четыре сервера, которых нет в конфигах, все шесть ссылок на вики битые, путь к тестам неверный, а правило про async прямо противоречит коду. Ядро (детерминизм, DI, запреты, Alebardium) — верно и проверено. |
| `.cursor/rules/agent-workflows.mdc` | update | S | Процедурная часть (refresh_unity → read_console → коммит .cs вместе с .meta, нарезка спрайтов Grid by Cell Count) актуальна и полезна. Протухли три числа версий и раздел про markdown-шапку доков, который теперь противоречит frontmatter-конвенции вики. |
| `.cursor/rules/obsidian-conventions.mdc` | update | S | Файл alwaysApply: true и активно учит устаревшему формату: числовые префиксы в именах и эмодзи-строка статуса. Реальная вика перешла на латинские слаги внутри кластеров и YAML-frontmatter (title/order/status). Следование этому правилу сегодня СОЗДАЁТ дрейф, а не предотвращает его. |
| `.cursor/rules/unity-csharp.mdc` | delete | S | Обобщённая «Unity best practices»-болванка без единого проектного факта, при этом четыре её пункта прямо противоречат законам проекта из project-context.mdc и кода. Всё ценное уже сказано точнее в project-context.mdc — файл стоит удалить, чтобы он не выдавал агенту противоположные инструкции по globs **/*.cs. |
| `.cursor/rules/git-conventions.mdc` | update | S | Основа верна и живёт: типы/скоупы коммитов, ветки master↔dev↔feature/*, CI Gate как required-check — всё сходится с историей и с ci.yml. Разошлись две вещи: заявленный маркер агента в теле коммита фактически не ставится, и раздел про неспушенные спрайт-паки описывает состояние, которого больше нет. |
| `.cursor/rules/phase-design-pipeline.mdc` | keep | S | Чисто процессный документ (design-first: данные → швы → классы → спайк → имена тестов → реализация). Фактических утверждений о версиях, путях и конфигах не содержит — сверять не с чем, дрейфа нет. Единственное имя из кода (ICombatContext) реально существует как шов боевого контура. Оставить как есть. |
| `.mcp.json` | keep | S | Единственный MCP-конфиг репозитория и единственный источник, с которым CLAUDE.md сходится по версии: unityMCP через uvx, mcpforunityserver==10.0.0, transport stdio. Дрейфа в самом файле нет — расхождение на стороне гайдов (agent-workflows.mdc называет 9.7.1). |
| `.cursor/mcp.json` | update | S | Файла не существует, хотя на него как на источник конфигурации MCP ссылаются и CLAUDE.md, и project-context.mdc. Решение за Максом: либо конфиг восстанавливается, либо все ссылки на него вычищаются из обоих гайдов и таблица серверов сводится к одному unityMCP из .mcp.json. |
| `.claude/skills/xgaida-x-nixi-gamefeel-vfx/` | update | L | Самый сильный дрейф из всех скиллов. Скилл описывает префаб-шов VFX как ЦЕЛЕВОЙ и «пока не построенный», а pixel-burst — как живой placeholder. В коде всё наоборот: шов VfxData→PooledVfx→ObjectPool построен и работает (коммит c7e8c021), а PixelBurst/PixelBurstMesh/PixelBurstPreset снесены (коммит 46c99414). Карта слоя в SKILL.md ссылается на три несуществующих файла. Требуется переписать раздел «Целевой шов» в «как есть», обновить карту, переписать references/vfx-and-pooling.md и подраздел про placeholder в feedback-seam.md. |
| `.claude/skills/xgaida-x-nixi-combat-sim/` | update | M | Ядро описано верно: тик-ордер в references/simulation-and-determinism.md совпадает с CombatSimulation.Tick посимвольно, список ICombatContext совпадает с интерфейсом, контракты эффектов на месте. Дрейф в двух местах: карта боя не знает о слое Damage/ (модель урона «школа × сродство × источник») и о ряде подпапок Combat, а presentation-seam.md ссылается на удалённый PixelBurstPreset.cs. |
| `.claude/skills/xgaida-x-nixi-data-authoring/` | update | M | Каркас описан верно (ContentDefinition, ContentDomains, IContentDatabase/ContentRegistry, Override-авторинг, 30 статов, ContentValidationService, миграции). Дрейф: перечень доменов id устарел на 2 позиции, каскад классовой базы (ClassBalanceConfig/ClassBaseline) вообще не описан, хотя он вклинивается ровно в ту формулу сборки статов, которую скилл объясняет; в карте нет новых контент-типов (TagData, SpeciesData, VfxData, ClassBalanceConfig). |
| `.claude/skills/xgaida-x-nixi-uitk/` | update | M | Карта UI-слоя и правила (токены, BEM, MVVM, канва 1920×1080) в целом совпадают с кодом. Три реальных дрейфа: пример custom control учит PascalCase-атрибуту в UXML (молча не применяется — это как раз проектная готча), роутер описан через несуществующий IMenuRouter и без UiNavigator/ScreenKind (реворк Ф2 уже в коде), и скилл ничего не знает про два HARD-инварианта темы — единый источник рамок --gm-outline-* и тёплую палитру. |
| `.claude/skills/xgaida-x-nixi-gdd-scribe/` | update | M | Правила (ADR append-only, разнос, глоссарий, frontmatter, система тайтлов) актуальны, но адресация файлов застряла в старой нумерации: скилл и его references зовут доки по номерам «0.4», «0.7», «Справочник тегов.md», «0.2. Шаблон карточки реликвии» — таких файлов нет, всё переехало на латинские слаги. Плюс блок «Структура (Фаза 1 выполнена)» противоречит собственной шапке-статусу и реальному дереву. |
| `.claude/skills/xgaida-x-nixi-tech-scribe/` | update | S | Самый здоровый из «писарских»: кластеры, frontmatter-схема, статусы, автоматизация — всё соответствует реальности (папки 00-meta/10-reference/20-explanation/30-how-to/40-planning на месте, check-wiki-links.ps1 и docs-lint.yml существуют, tech-changelog живой append-only). Правки косметические — перечни доков и счётчики отстали. |
| `.claude/skills/xgaida-x-nixi-audio/` | keep | S | Дрейфа не нашла. Все пути и имена из карты аудио-слоя существуют, ключевое утверждение о подписке AudioPresenter напрямую на CombatSimulation (а не через MessagePipe) подтверждается кодом — причём именно этот скилл описывает ситуацию правильно, а combat-sim и gamefeel-vfx её путают. |
| `.claude/skills/xgaida-x-nixi-content-design/` | keep | S | Процессный скилл, почти не делает утверждений о коде — только о методичках и соседних скиллах. Все внешние ссылки живые: методички формата на месте (появилась даже item.md, которую скилл числит как «TBD»), pillars и journal-adr существуют. |
| `.claude/skills/xgaida-x-nixi-balance/DRAFT.md` | update | S | Черновик по-прежнему валиден по сути (SimBench + ContentEditService, обе стороны петли существуют), но путь меню назван по-старому — Tools/Balance, тогда как код давно переехал под корень Alebardium (это наше HARD-правило по Editor-меню). Статус «финализируем в конце сессии 2026-07-17» висит девять дней. |
| `.claude/skills/BACKLOG.md` | update | S | Реестр отстал от собственной папки: в таблице «Готовы» 7 скиллов, а на диске 8 SKILL.md плюс один DRAFT. Плюс раздел «Связанные задачи» числит vision и дизайн-столпы как невыполненный план, хотя доки заведены. Описание gamefeel-vfx в реестре несёт тот же устаревший тезис про «целевой шов префаб-VFX», что и сам скилл. |

### Расхождения в агентских гайдах и скиллах (критические и значимые)

#### `docs/act-map-overhaul-progress.md` — update

- знач. — **док:** «Осталось из фазы D» п.5: снос UITK-карты (MapScreen.uxml, MapGraph.cs, MapScreenView.cs, MapScreenNodeChooser.cs)
  **код:** Уже снесено — ни одного из четырёх файлов в Assets нет; карта живёт в Presentation/Map/WorldMapView.cs. Раздел D8 это фиксирует, а старый список — нет
  **где:** find Assets/_Project -name MapScreen.uxml/MapGraph.cs/MapScreenView.cs/MapScreenNodeChooser.cs — пусто; Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs существует
- знач. — **док:** Трек п.0b — физрасстановка Grok-спрайтов: «осталось физически на ~15 юнитов… прописать _viewPrefab в SO»
  **код:** Сделано: 16 префабов UnitView_<Champion>.prefab в Assets/_Project/Prefabs/Units/, поле _viewPrefab заполнено во всех 11 RelicData разными guid
  **где:** ls Assets/_Project/Prefabs/Units/ (UnitView_Assassin…UnitView_WhirlMonk); grep _viewPrefab Assets/_Project/ScriptableObjects/Relics/*.asset — 11 из 11 непустые

#### `docs/damage-model-rework-progress.md` — archive

- знач. — **док:** ВИСИТ: tag-reference — полный переписанный документ
  **код:** Подтверждено — документ всё ещё описывает старую модель («elemental_damage — школа, гасится стихийной бронёй»), хотя в коде StatType.MagicArmor и DamageSchool.Magical
  **где:** docs/wiki/gdd/roster/tag-reference.md, таблица полей: `elemental_damage` / «Школа (гасится стихийной бронёй)»

#### `docs/ui-architecture-rework-plan.md` — merge_into_wiki

- знач. — **док:** Статус-шапка: «утверждённый план… Ветка feat/persist-battle-flow»
  **код:** Ветка влита и удалена (PR #20), работа давно идёт на других ветках; план как «к исполнению» неверен — Ф0-Ф7 закрыты 2026-07-19
  **где:** git branch --show-current = feature/unit-tag-icons; docs/ui-architecture-rework-progress.md, таблица «Статус фаз» — все 8 строк ✅ влито
- знач. — **док:** Трек С (шов разложения статов) и Трек Т (тултип-система) — «после…, не сделано»
  **код:** Оба реализованы; в плане числятся будущими
  **где:** Assets/_Project/Scripts/Data/Stats/StatValue.cs + Game/Services/StatValueFormatter.cs; папка Assets/_Project/Scripts/UI/Tooltips/ (TooltipSystem, TooltipContentFactory, TooltipManipulator, TooltipPlacement, TooltipRequest)

#### `docs/third-party-credits.md` — update

- знач. — **док:** Из game-icons.net в проекте иконки режимов (6) и фильтров (3)
  **код:** Есть ещё папка Tags с 55 PNG из game-icons.net (авторы Lorc, Delapouite, sbed) и Bar с 3 иконками того же происхождения — в реестре их нет, третий автор sbed нигде не упомянут
  **где:** Assets/_Project/Art/UI/Icons-gm/Tags/ATTRIBUTION.txt («Authors: Lorc, Delapouite, sbed … CC BY 3.0»), ls Tags/*.png = 55; Assets/_Project/Art/UI/Icons-gm/Bar/{health-normal,hearts,two-coins}.png
- знач. — **док:** Строка в титры покрывает UI-иконки проекта
  **код:** Строка называет только Lorc и Delapouite — при текущем наборе она неполна
  **где:** docs/third-party-credits.md:37-39 против Tags/ATTRIBUTION.txt

#### `docs/audits/` — keep

- знач. — **док:** README, шаг 5: actionable-пункты заводить в «../wiki/tech/code-guide/07. Техдолг, решения и changelog.md»
  **код:** Такого пути нет — вика перестроена в кластеры Diátaxis, журнал теперь docs/wiki/tech/00-meta/tech-changelog.md
  **где:** ls docs/wiki/tech/code-guide → No such file or directory; ls docs/wiki/tech/00-meta/ → index.md, tech-changelog.md

#### `docs/art/ТЗ_пиксель-арт_юниты.md` — archive

- **КРИТ** — **док:** Набор анимаций покадрово: idle 4-6, walk 6-8, attack_a/b 6-8, hurt, death — на весь ростер, канвас 64×64
  **код:** Отменено принятым решением: персонажи анимируются скелетно, покадровка признана неокупаемой; реализация идёт через PSB + Unity 2D Animation, а не листы 64×64
  **где:** docs/wiki/gdd/10-vision/character-animation.md (status draft, updated 2026-07-26): «Персонажи анимируются скелетно (кости), а не покадрово: покадровый пиксель на ~30 юнитов не окупается»; Assets/_Project/Prefabs/Bones/BoneUnit_Standart.prefab
- знач. — **док:** Стилевой эталон — собственные спрайты FewSeconds-ManyDeaths, classic pixel-perfect требования (чистая альфа, фикс-пивот)
  **код:** Проект сознательно ушёл в стилизованный пиксель-арт, не pixel-perfect; текущий арт-вход — AI-генерация по промптам
  **где:** docs/character-art-prompts.md (пайплайн Nano Banana Pro → части → скелет); docs/wiki/gdd/10-vision/character-animation.md, раздел «Намерение» («нейросети сильнее помогают с картинкой»)

#### `CLAUDE.md` — update

- **КРИТ** — **док:** «Все серверы настроены в `.cursor/mcp.json` и активны автоматически» + таблица из 5 серверов (unityMCP, github, git, context7, filesystem с ID вида `project-0-Guildmaster_-_Autobattler-*`)
  **код:** Файла `.cursor/mcp.json` в репозитории НЕТ (в `.cursor/` лежит только `rules/`). Единственный конфиг — корневой `.mcp.json`, и в нём ровно один сервер: `unityMCP` (uvx → mcpforunityserver==10.0.0, transport stdio). Ни github, ни git, ни context7, ни filesystem в репо-конфигах не описаны.
  **где:** `ls -la .cursor/` → только rules/; `ls .cursor/mcp.json` → No such file or directory; `.mcp.json` целиком = {"mcpServers":{"unityMCP":{...}}}
- **КРИТ** — **док:** «Полное обоснование — `docs/wiki/tech/5. Технологический стек и архитектура.md`» и «`docs/wiki/tech/0.3. Подготовка проекта (Unity).md`»
  **код:** Обоих файлов не существует. Вика перестроена в Diátaxis-кластеры: `docs/wiki/tech/{00-meta,10-reference,20-explanation,30-how-to,40-planning}`; актуальный стек — `docs/wiki/tech/10-reference/tech-stack.md`.
  **где:** `ls docs/wiki/tech/` → 00-meta, 10-reference, 20-explanation, 30-how-to, 40-planning; проверка обоих путей → MISSING; `docs/wiki/tech/10-reference/tech-stack.md` → EXISTS
- знач. — **док:** Раздел «Графика / прочее (установлено, пока не используется в коде)»: «ProBuilder, Visual Effect Graph … Джус/VFX — отложены (см. план 13, «Явно отложено»)»
  **код:** Джус давно реализован и живёт целым контуром. Верна только узкая часть утверждения: сам пакет VFX Graph не используется (0 файлов с `using UnityEngine.VFX`, 0 ассетов `*.vfx`), ProBuilder тоже не используется в коде (0 `using UnityEngine.ProBuilder`) — но он нужен как зависимость группы probuilder в Unity MCP.
  **где:** Реализованный джус: Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs, Presentation/CombatVfx.cs, Presentation/PooledVfx.cs, Presentation/DeathShatter.cs, Presentation/ShatterMesh.cs, Presentation/FloatingText.cs, Presentation/Camera/ScreenShake.cs (+IScreenShake/NullScreenShake), Presentation/Design/CombatFeelConfig.cs, Data/Definitions/VfxData.cs; grep `using UnityEngine.VFX` → 0, find -name '*.vfx' → пусто
- знач. — **док:** «Easy Save 3 / Плумбинг сейвов (диск + Steam Cloud). Сами пишем DTO-слой.»
  **код:** ES3 в сейвах не участвует вообще. Реализация ISaveService — Guildmaster.Game.Services.JsonFileSaveService: File.WriteAllText + JsonUtility.ToJson в Application.persistentDataPath. В самом файле написано, почему: «Без зависимости от Easy Save 3 (у ES3 нет asmdef → из Guildmaster.* не вызвать)». То же у SettingsService. Пакет лежит в Assets/Plugins/Easy Save 3, но это плановая замена бэкенда, а не текущий плумбинг.
  **где:** Assets/_Project/Scripts/Game/Services/JsonFileSaveService.cs:9-11, 22, 36; Assets/_Project/Scripts/Game/Services/SettingsService.cs:13-14
- знач. — **док:** «Newtonsoft.Json 3.2.2 / JSON-сериализация DTO»
  **код:** Версия пакета указана верно, но в игровом коде Newtonsoft не используется ни разу. DTO сериализуются UnityEngine.JsonUtility.
  **где:** Packages/manifest.json:26 (com.unity.nuget.newtonsoft-json 3.2.2); `grep -rln Newtonsoft Assets/_Project/Scripts` → 0 файлов; JsonFileSaveService.cs:22
- знач. — **док:** «CI/CD. Сборки и тесты через GameCI» и в структуре: «ci.yml # GameCI pipeline (тесты + сборка)»
  **код:** В .github/workflows/ci.yml нет ни одного build-джоба. Пайплайн = changes (paths-filter) → test (game-ci/unity-test-runner@v4, editmode + playmode) → ci-gate. Сборок GameCI не делает. Также не упомянуты соседние workflow: docs.yml и docs-lint.yml.
  **где:** .github/workflows/ci.yml — jobs changes / test / ci-gate (стр. 10, 27, 91); `ls .github/workflows/` → ci.yml, docs-lint.yml, docs.yml
- знач. — **док:** Блок «Структура проекта» (дерево с .cursor/{mcp.json,rules×2}, .github/workflows/ci.yml, Assets, Packages, ProjectSettings, scripts/run-tests.ps1)
  **код:** Дерево отстало почти по всем ветвям: (1) `.cursor/mcp.json` не существует; (2) в `.cursor/rules/` шесть файлов, а не два; (3) в `scripts/` также check-wiki-links.ps1 и statdb.ps1; (4) в workflows также docs.yml и docs-lint.yml; (5) в корне не упомянуты AGENTS.md, docs/, .claude/, .agents/, .codex/, Aseprite/, Art_Dev/, doxygen/, quartz-config/, skills-lock.json.
  **где:** `ls .cursor/rules` → agent-workflows.mdc, git-conventions.mdc, obsidian-conventions.mdc, phase-design-pipeline.mdc, project-context.mdc, unity-csharp.mdc; `ls scripts` → check-wiki-links.ps1, run-tests.ps1, statdb.ps1; `ls -a` корня
- знач. — **док:** Таблица «Правила и конвенции» перечисляет только git-conventions.mdc и project-context.mdc
  **код:** Агент не узнаёт из CLAUDE.md о четырёх остальных правилах, три из которых alwaysApply: true — agent-workflows.mdc (пайплайн refresh_unity/.meta, нарезка спрайтов, версии Unity MCP), obsidian-conventions.mdc, phase-design-pipeline.mdc (design-first), unity-csharp.mdc (globs **/*.cs).
  **где:** frontmatter файлов .cursor/rules/agent-workflows.mdc:4, obsidian-conventions.mdc:3, phase-design-pipeline.mdc:3 (alwaysApply: true), unity-csharp.mdc:3-4 (globs **/*.cs, alwaysApply: false)
- знач. — **док:** Стек молчит о QFSW Quantum Console и Roslyn
  **код:** Assets/Plugins/QFSW используется тремя dev-командными файлами, и на консоль завязаны боевые фичи. Assets/Plugins/Roslyn даёт USE_ROSLYN для validate_script (упомянут только в agent-workflows.mdc). Оба плагина есть в Assets/Plugins, но в стек-таблице CLAUDE.md их нет.
  **где:** `ls Assets/Plugins` → Easy Save 3, FMOD, Facepunch.Steamworks, QFSW, Roslyn, Sirenix; `using QFSW` → DevTools/GuildmasterCommands.cs, DevTools/MapDevCommands.cs, DevTools/VisualFxCommands.cs; Presentation/Camera/CameraModeController.cs:197 «QFSW: gm_cam_dev»
- знач. — **док:** ЧЕГО НЕ ХВАТАЕТ (1): слой скиллов
  **код:** В .claude/skills/ лежат 9 проектных скиллов-контуров плюс BACKLOG.md и .agents/skills. CLAUDE.md о них не говорит ни слова — агент не знает, что у каждой подсистемы есть свой контур и кто ведёт какую документацию (в частности что docs/wiki/tech ведёт tech-scribe, а docs/wiki/gdd — gdd-scribe).
  **где:** `ls .claude/skills/` → xgaida-x-nixi-{audio,balance,combat-sim,content-design,data-authoring,gamefeel-vfx,gdd-scribe,tech-scribe,uitk}, BACKLOG.md; `ls .agents/` → skills
- знач. — **док:** ЧЕГО НЕ ХВАТАЕТ (2): новые кодовые контуры и редакторный тулинг
  **код:** Появились сборки, которых нет ни в одном гайде: Guildmaster.Guild (слой карты акта/привалов — ActConfig, CampSession, CampMessages, ChestMessages, ContinueMessages), Guildmaster.Balance + .Editor (BalanceScenarioData, SimBench), Guildmaster.DevTools, Guildmaster.Net и четыре редакторных: ContentHub.Editor, PaletteRemap.Editor, Audio.Editor, UI.Editor. Guildmaster.MiniGames — пустой asmdef-заглушка.
  **где:** `find Assets/_Project/Scripts -name *.asmdef` → 18 сборок; `ls Assets/_Project/Scripts/MiniGames` → только Guildmaster.MiniGames.asmdef + .meta; `ls Assets/_Project/Scripts/{Guild,Balance,EditorTools}`
- знач. — **док:** ЧЕГО НЕ ХВАТАЕТ (3): правило меню Alebardium и AGENTS.md-близнец
  **код:** HARD-правило «все [MenuItem] под корнем Alebardium/» реально соблюдается кодом, но живёт только в project-context.mdc — в CLAUDE.md его нет. Также AGENTS.md в корне — посимвольный близнец CLAUDE.md для Codex, несущий ВСЕ те же протухшие утверждения; CLAUDE.md на него не ссылается, поэтому правки будут расходиться.
  **где:** `grep -o '\[MenuItem("[^"]*"' Assets/_Project/Scripts` → 21 пункт, все под Alebardium/ (Balance, Data/Migrations, Visuals, UI Preview, Audio, Test, Content Hub, Palette Remapper); AGENTS.md:1-19 дословно повторяет CLAUDE.md включая битую ссылку «5. Технологический стек»

#### `.cursor/rules/project-context.mdc` — update

- **КРИТ** — **док:** Таблица «MCP-серверы» с ID `project-0-Guildmaster_-_Autobattler-{github,git,context7,filesystem}` и рабочий процесс п.1-3 («Git-операции — через Git MCP», «перед ответами об Unity API — Context7»)
  **код:** Ни один из этих серверов не описан в репозитории: `.cursor/mcp.json` отсутствует, `.mcp.json` содержит только unityMCP.
  **где:** .cursor/rules/project-context.mdc:12-18; `ls .cursor/` → только rules/; cat .mcp.json
- **КРИТ** — **док:** Ссылки на документацию: `docs/wiki/tech/5. Технологический стек и архитектура.md`, `docs/wiki/tech/1. Сборки.md` («всегда проверяй её перед созданием нового скрипта»), `docs/wiki/gdd/0.0. README.md`, `docs/wiki/gdd/1. Концепция.md`, `docs/wiki/gdd/3. Боевая система.md`, `docs/wiki/gdd/5. Реликвии.md`
  **код:** ВСЕ шесть путей не существуют. Обе вики перестроены в кластеры: tech = {00-meta,10-reference,20-explanation,30-how-to,40-planning} (карта сборок — docs/wiki/tech/10-reference/assemblies.md), gdd = {00-meta,10-vision,20-combat,30-run-meta,40-content,50-modes-ux,enemies,relics,roster}.
  **где:** .cursor/rules/project-context.mdc:68, :110, :116-123; проверка каждого пути → MISSING; ls docs/wiki/tech, ls docs/wiki/gdd
- знач. — **док:** Структура: «Assets/Tests/ EditMode/ ← юнит-тесты, PlayMode/ ← интеграционные тесты»
  **код:** Каталога Assets/Tests не существует. Тесты живут в Assets/_Project/Tests/{EditMode,PlayMode} — там же asmdef'ы Guildmaster.Tests.EditMode, Guildmaster.Tests.PlayMode и вложенный Guildmaster.Balance.Tests (EditMode/Balance).
  **где:** .cursor/rules/project-context.mdc:103-105; `ls Assets/Tests` → нет; `find Assets -name *.asmdef / grep -i test` → Assets/_Project/Tests/EditMode/Balance/Guildmaster.Balance.Tests.asmdef, Assets/_Project/Tests/EditMode/Guildmaster.Tests.EditMode.asmdef, Assets/_Project/Tests/PlayMode/Guildmaster.Tests.PlayMode.asmdef
- знач. — **док:** «Async: Coroutines для time-based операций; async/await с UniTask если подключён» (стр. 41)
  **код:** Прямо противоречит и коду, и CLAUDE.md («UniTask … использовать вместо корутин для всего time-based»). UniTask подключён и доминирует; формулировка «если подключён» дезориентирует агента.
  **где:** `grep -rl 'using Cysharp.Threading.Tasks' Assets/_Project/Scripts` → 26 файлов; `grep -rn StartCoroutine Assets/_Project/Scripts` → 1 вхождение; Packages/manifest.json:7 (com.cysharp.unitask)

#### `.cursor/rules/agent-workflows.mdc` — update

- знач. — **док:** «сервер `mcpforunityserver==9.7.1` (uvx → mcp-for-unity, транспорт stdio)» (стр. 50)
  **код:** В .mcp.json прописан mcpforunityserver==10.0.0. CLAUDE.md называет 10.0.0 — два alwaysApply-документа расходятся между собой, правым оказывается CLAUDE.md.
  **где:** .mcp.json args: ["--from","mcpforunityserver==10.0.0","mcp-for-unity","--transport","stdio"]; Packages/manifest.json:4 (com.coplaydev.unity-mcp … #v10.0.0)
- знач. — **док:** Раздел «Новые Markdown-файлы в docs/wiki»: правильная структура = «**Статус:** Draft» (с эмодзи) + `---`, а начинать файл с `---` нельзя, потому что Quartz примет его за frontmatter
  **код:** Вика теперь ИМЕННО на YAML-frontmatter и открывается им же. Статус задаётся полем `status`, а не эмодзи-строкой — и графические эмодзи вдобавок запрещены общими правилами оформления.
  **где:** head -10 docs/wiki/tech/10-reference/tech-stack.md → `---` title: "Reference - Tech Stack" / order: 0 / status: needs_review / updated: 2026-07-16 / `---`; head -8 docs/wiki/gdd/00-meta/glossary.md → title/order/status: living

#### `.cursor/rules/obsidian-conventions.mdc` — update

- **КРИТ** — **док:** «Именование файлов: числовой префикс задаёт порядок: `0.0.`, `0.1.`, `1.`, `2.`; `0.x.` — служебные», пример `1. Сборки (Assembly Definitions).md`
  **код:** Ни один живой док вики так не назван. Имена — латинские слаги внутри пронумерованных КАТАЛОГОВ-кластеров, порядок задаётся полем `order` во frontmatter, отображаемое имя — `title`.
  **где:** `ls docs/wiki/tech/10-reference/` → arena.md, assemblies.md, asset-inventory.md, combat-model.md, data-layer.md, editor-tools.md, input-camera.md, saves.md, scene-sorting.md, tech-stack.md, ui-navigation.md; docs/wiki/gdd/00-meta/index.md: «Порядок глав задаётся полем order во frontmatter; имена файлов — латинские слаги»
- знач. — **док:** Шаблон контентного документа: «**Статус:** Draft» (с эмодзи) + `---` + `## Раздел 1`
  **код:** Актуальный шаблон — YAML-frontmatter в начале файла: title / order / status (draft/needs_review/ready/planned/living/archive) / опционально updated. Эмодзи-статус нигде не воспроизводится.
  **где:** head -10 docs/wiki/tech/10-reference/tech-stack.md; head -8 docs/wiki/gdd/00-meta/{glossary,index}.md

#### `.cursor/rules/unity-csharp.mdc` — delete

- знач. — **док:** «Use Coroutines for time-based and async operations» (стр. 24)
  **код:** Проект на UniTask; CLAUDE.md формулирует обратное правило («использовать вместо корутин для всего time-based»).
  **где:** `grep -rl 'using Cysharp.Threading.Tasks' Assets/_Project/Scripts` → 26 файлов; `grep -rn StartCoroutine` → 1 вхождение
- знач. — **док:** «Leverage Unity's physics engine for game mechanics and interactions» (стр. 22) и «Simplify collision meshes; tune fixed timestep for physics» (стр. 40)
  **код:** Боевая симуляция детерминирована и физику запрещает: project-context.mdc:77 перечисляет Physics2D / Rigidbody2D / Time.deltaTime в разделе «Запрещено».
  **где:** .cursor/rules/project-context.mdc:65, :77; Assets/_Project/Scripts/Combat/Guildmaster.Combat.asmdef (бой вынесен в отдельную сборку)

#### `.cursor/rules/git-conventions.mdc` — update

- знач. — **док:** «Sprite packs под `Assets/_Project/Art/Sprites/Pixel Art Heroes/` — kept local and NOT committed for now … added later (likely via Git LFS)» + «never `git add .` blindly (it would sweep in the sprite packs)»
  **код:** Паки давно в индексе: git отслеживает 255 файлов по этому пути. В .gitignore из этого раздела реально исполняется только Assets/Screenshots/. Запрет `git add .` стоит сохранить, но с актуальной причиной (чужая незавершённая работа в дереве), а не с несуществующей угрозой.
  **где:** `git ls-files 'Assets/_Project/Art/Sprites/Pixel Art Heroes/' / wc -l` → 255 (среди них EVil Wizard 2/License.txt); `grep -n 'Pixel Art Heroes\/Screenshots' .gitignore` → только строки 120-121 про Assets/Screenshots/

#### `.cursor/mcp.json` — update

- **КРИТ** — **док:** CLAUDE.md и project-context.mdc считают этот файл существующим и рабочим
  **код:** В каталоге .cursor/ лежит только подкаталог rules/ — mcp.json отсутствует. Серверы github / git / context7 / filesystem с ID `project-0-Guildmaster_-_Autobattler-*` не описаны нигде в репозитории; возможно, они настроены на уровне пользователя вне репо — по файлам репозитория это НЕ проверяется, поэтому судить об их доступности я не могу — только об отсутствии проектного конфига.
  **где:** `ls -la .cursor/` → только rules/; `ls .cursor/mcp.json` → No such file or directory; CLAUDE.md:93 и :103, .cursor/rules/project-context.mdc:12-18

#### `.claude/skills/xgaida-x-nixi-gamefeel-vfx/` — update

- **КРИТ** — **док:** «Целевой шов префаб-VFX (проектируем, пока не построен)»; HARD-правило 1: «Целевой шов (VfxData SO → префаб → пул-спавнер) … пока не построен, но проектируем под него» (SKILL.md стр. 87-88, 136-152)
  **код:** Шов ПОСТРОЕН и используется в бою. VfxData : ContentDefinition (Prefab/Scale/SortingLayerName/SortingOrder/DefaultDirDeg) существует; CombatVfx.Spawn(VfxData, worldPos, dirDegOverride, intensity) держит ObjectPool<PooledVfx> на префаб; PooledVfx.Play(...) — корневой компонент префаба с авто-возвратом в пул и запечённым относительным sorting-order детей; CombatPresenter спавнит через _vfx.Spawn(_feel.VfxMuzzle/VfxHitSpark/VfxImpactDust/VfxHeal/VfxContactDust, ...).
  **где:** Assets/_Project/Scripts/Data/Definitions/VfxData.cs; Assets/_Project/Scripts/Presentation/PooledVfx.cs; Assets/_Project/Scripts/Presentation/CombatVfx.cs (Spawn/GetOrCreatePool/DespawnAll); Assets/_Project/Scripts/Presentation/CombatPresenter.cs:216-220, 330-336, 360-361, 396-401; git log: c7e8c021 «feat(feel): migrate combat VFX to prefab seam via VfxData»
- **КРИТ** — **док:** Карта слоя: «Один брызг (кодовый меш, placeholder) — Presentation/PixelBurst.cs, PixelBurstMesh.cs»; «Пресет одного пиксель-брызга — Presentation/Design/PixelBurstPreset.cs» (SKILL.md стр. 53, 60)
  **код:** Все три файла удалены. В Presentation/ нет PixelBurst.cs и PixelBurstMesh.cs; в Presentation/Design/ лежат только CombatColorPalette.cs и CombatFeelConfig.cs. В CombatFeelConfig нет типа PixelBurstPreset — вместо него секция «VFX — prefab refs (VfxData)» с полями _vfxHitSpark/_vfxMuzzle/_vfxImpactDust/_vfxContactDust/_vfxHeal.
  **где:** ls Assets/_Project/Scripts/Presentation, ls Assets/_Project/Scripts/Presentation/Design; Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs:213-224, 313-317; git log: 46c99414 «chore(ui): снести мёртвый PixelBurst…»
- знач. — **док:** references/vfx-and-pooling.md: «Текущий placeholder-путь (не расширять): PixelBurst/PixelBurstMesh — пиксельные брызги кодовым мешем, параметры из PixelBurstPreset (Serializable-класс внутри CombatFeelConfig: Color/Count/Speed/Size/Life/Gravity/SpreadDeg). Типы: hit-spark / muzzle / impact-dust / heal» (стр. 58-67)
  **код:** Этого пути в коде больше нет; hit-spark/muzzle/impact-dust/heal реализованы как VfxData-ассеты с префабами. Совет «правь пресет в CombatFeelConfig, не числа в PixelBurst» неисполним.
  **где:** Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs (grep PixelBurstPreset — нет совпадений); Assets/_Project/Scripts/Presentation/CombatVfx.cs
- знач. — **док:** HARD-правило 4 и references/feedback-seam.md стр. 19-21: «CombatFeelDirector, как AudioPresenter» слушают MessagePipe, «а не sim напрямую»
  **код:** CombatFeelDirector действительно на MessagePipe (ISubscriber<DamageDealtEvent>/<BattleEndedEvent>), а вот AudioPresenter подписан на C#-события CombatSimulation НАПРЯМУЮ (_sim.OnDamageDealt/OnUnitDied/OnHealed/OnAttackEvaded/OnAttackStarted/OnProjectileSpawned/OnBattleEnded). Скилл audio описывает это верно — здесь противоречие между скиллами.
  **где:** Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs:21-33; Assets/_Project/Scripts/Presentation/Audio/AudioPresenter.cs:23, 43-49
- знач. — **док:** references/feedback-seam.md стр. 37-38: «Пиксельные VFX (пока placeholder-путь): искры в HitPoint … Целевая форма — префаб-VFX (см. vfx-and-pooling.md)»
  **код:** Уже префаб-форма: презентер зовёт _vfx.Spawn(_feel.VfxHitSpark, anchor, intensity: …) и _vfx.Spawn(_feel.VfxImpactDust, view.FeetPoint) — это VfxData-префабы через пул.
  **где:** Assets/_Project/Scripts/Presentation/CombatPresenter.cs:330-336

#### `.claude/skills/xgaida-x-nixi-combat-sim/` — update

- знач. — **док:** «Карта боя» (SKILL.md стр. 37-58) перечисляет файлы Combat, но в ней нет ни DamagePipeline, ни DamageRequest/DamageResult, ни School/Affinity — при том что модель урона в проекте поисточниковая (HARD-решение).
  **код:** В Combat есть отдельная папка Damage/ с DamagePipeline.Execute(in DamageRequest): порядок raw → DamageDealtEff → броня по DamageSchool (Physical/Magical/True, пробивание % затем flat) → AffinityTable.Multiplier(Affinity, CreatureType) → DamageTakenEff → щит → HP. DamageRequest несёт School, Affinity и DamageSourceKind (AutoAttack/Ability/Periodic/Reactive) с гейтами IsAutoAttack/IsDirectHit. Также в карте нет Spatial/SpatialHash.cs, Projectiles/, Commands/, AI/ (BrainSystem/ProfileBrain), CombatPositioning.cs / PositioningIntent.cs / FleeSteering.cs.
  **где:** Assets/_Project/Scripts/Combat/Damage/DamagePipeline.cs:20-71; Assets/_Project/Scripts/Combat/Damage/DamageRequest.cs:9,46-74; ls Assets/_Project/Scripts/Combat (AI/, Commands/, Damage/, Projectiles/, Spatial/, CombatPositioning.cs, PositioningIntent.cs, FleeSteering.cs)

#### `.claude/skills/xgaida-x-nixi-data-authoring/` — update

- знач. — **док:** references/three-layers-and-ids.md стр. 39-41: «На сейчас 14 доменов: relic, enemy, vessel, effect, tag, trait, consequence, ai_preset, guildmaster, item, run_mod, encounter, battle_preset, event»
  **код:** В словаре ContentDomains.Domains 16 записей: к перечисленным добавлены species (SpeciesData) и vfx (VfxData).
  **где:** Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs:15-33
- знач. — **док:** references/stats-and-configs.md стр. 34-68: базовый терм = «Override (если задан) ИНАЧЕ дефолт StatsConfig», база авторится в UnitData._stats, «StatsConfig остаётся фолбэком для НЕзаданных статов»; описание сборки в Stats.cs без промежуточных слоёв
  **код:** Между StatsConfig и стат-блоком персоны появился слой классовой базы: ClassBaseline.Apply(stats, data, ClassBalanceConfig) добавляет модификаторы класса ПЕРВОЙ группой (до персоны), и правило «последний Override побеждает» даёт каскад Класс → Персона → Vessel. Зовут это и RuntimeUnitFactory (бой), и StatMath (Content Hub). Плюс есть EnemyScalers и UnitClass — про них скилл молчит.
  **где:** Assets/_Project/Scripts/Combat/Stats/ClassBaseline.cs:18-29 (+ remarks про каскад); Assets/_Project/Scripts/Data/Definitions/ClassBalanceConfig.cs; Assets/_Project/Scripts/Combat/Stats/EnemyScalers.cs; Assets/_Project/Scripts/Data/Stats/UnitClass.cs
- знач. — **док:** «Карта дата-слоя» (SKILL.md стр. 39-55) перечисляет контент-типы как UnitData/RelicData/EnemyData «+ прочие Definitions/*.cs»
  **код:** Формально не ложь, но новые типы, которые скилл обязан вести, нигде не названы: TagData + UnitTagResolver (система тегов юнитов), SpeciesData (слой вида врага), VfxData (VFX как контент — это теперь реальный стык с gamefeel-vfx), ClassBalanceConfig, GuildmasterData, RunModifierData, ConsequenceData, TraitData.
  **где:** ls Assets/_Project/Scripts/Data/Definitions (TagData.cs, UnitTagResolver.cs, SpeciesData.cs, VfxData.cs, ClassBalanceConfig.cs, GuildmasterData.cs, RunModifierData.cs, ConsequenceData.cs, TraitData.cs)

#### `.claude/skills/xgaida-x-nixi-uitk/` — update

- знач. — **док:** references/component-model.md стр. 57, 78: `[UxmlAttribute] public string LabelText` и использование `<gm:SliderRow name="row-master" LabelText="Общая громкость" />`
  **код:** Многословный UxmlAttribute в разметке пишется kebab-case; PascalCase игнорируется молча. Реальная разметка в проекте так и делает: `<gm:ToggleRow label-text="Анимация карточек" Value="true" />`, `<gm:SlantedChip Slant="12" slant-side="Left" />` (однословный Slant — PascalCase, многословный slant-side — kebab). Пример в скилле воспроизводит ровно тот баг, на котором мы уже горели.
  **где:** Assets/_Project/UI/Screens/UiGalleryScreen.uxml:279-280; Assets/_Project/UI/Screens/RunModeBar.uxml:23; Assets/_Project/Scripts/UI/Components/SliderRow.cs:17-40
- знач. — **док:** references/screens-and-mvvm.md стр. 4-5, 77-81: «Эталоны в коде: … MenuRouter/IMenuRouter»; «IMenuRouter/MenuRouter переключают экраны/страницы (табы). Логика показа/скрытия и активной страницы живёт в роутере»
  **код:** Типа IMenuRouter в проекте нет вообще (grep по Assets — 0 файлов). Владелец видимости и глушения ввода — UiNavigator (стек типизированных UiScreen, видимость и suppress ВЫЧИСЛЯЮТСЯ из (верх стека, фаза боя)); MenuRouter только строит экраны и делегирует стек навигатору. Есть также ScreenKind/UiScreen/UiScreenContext и тест UiNavigatorTests.
  **где:** grep IMenuRouter по Assets — нет совпадений; Assets/_Project/Scripts/UI/MenuRouter.cs:17-27 (docstring «стек, видимость и глушение геймплейного ввода делегирует UiNavigator»); Assets/_Project/Scripts/UI/Navigation/UiNavigator.cs:12-30; Assets/_Project/Tests/EditMode/UI/UiNavigatorTests.cs
- знач. — **док:** HARD-правило 2 «Значения — только через токены»: перечислены только цвета/отступы/шрифты/рамки в общем виде; про рамки нет единого источника
  **код:** В теме заведён отдельный контур рамок: --gm-outline-width / --gm-outline-color / -subtle / -inner / -hover в tokens.semantic.uss, и он используется в components.uss 42 раза. Скилл про него не упоминает, значит не защищает инвариант «рамки только из --gm-outline-*». Аналогично не упомянут инвариант тёплой палитры (ink/brass/parchment).
  **где:** Assets/_Project/UI/Theme/tokens.semantic.uss:30-34; grep gm-outline в Assets/_Project/UI/Theme/components.uss → 42 совпадения

#### `.claude/skills/xgaida-x-nixi-gdd-scribe/` — update

- знач. — **док:** Карта ГДД, SKILL.md стр. 44: «Нормативный справочник тегов — docs/wiki/gdd/roster/Справочник тегов.md»; то же в references/terminology-and-canon.md:11,47
  **код:** Файла с таким именем нет. В roster/ лежат tag-reference.md, unit-tag-glossary.md, relic-tag-assignments.md, character-registry.md/.base, roster-balance.md, index.md.
  **где:** ls docs/wiki/gdd/roster
- знач. — **док:** Процедура записи и чеклист оперируют номерами: «запись в 0.7 (журнал)», «сверить термины с 0.4 Локализация», references/decision-journal-adr.md:3 «Наш 0.7. Журнал ГД-решений», references/terminology-and-canon.md:8 «docs/wiki/gdd/0.4. Локализация.md»
  **код:** Нумерованных доков в vault нет: журнал — docs/wiki/gdd/00-meta/journal-adr.md, глоссарий — docs/wiki/gdd/00-meta/glossary.md (сама карта в SKILL.md их так и называет — внутреннее противоречие скилла).
  **где:** ls docs/wiki/gdd/00-meta (glossary.md, index.md, journal-adr.md, legacy.md, open.md, roadmap.md)
- знач. — **док:** Блок «Структура (Фаза 1 выполнена)», стр. 112-126: схема `10-vision/ (⊕vision·⊕pillars)·concept·lore·guildmaster·difficulty-skill`, где ⊕ = «ещё не заведено»; «Осталось: карточки relics/roster/enemies → слаги (Фаза 2). Не запускать Фазу 2 без «да»»
  **код:** vision.md и pillars.md существуют; карточки давно на слагах (the-bloom.md, bandit-bruiser.md, goblins.md) — что признаёт шапка-статус в этом же файле (стр. 51-55). Плюс дерево выросло: 10-vision содержит ещё pitch.md, visual-direction.md, character-animation.md, audio-subbuses.md и 5 backlog-*.md; 20-combat — positioning.md и папку effects/; 40-content — authoring/ и items/; 50-modes-ux — ui-feedback.md.
  **где:** ls docs/wiki/gdd/10-vision, /20-combat, /40-content, /50-modes-ux, /relics, /enemies

#### `.claude/skills/xgaida-x-nixi-balance/DRAFT.md` — update

- знач. — **док:** «меню Tools/Balance/*» и «Меню Tools/Balance/* (или публичные *.Run() через execute_code)» (стр. 18, 28)
  **код:** Все пункты зарегистрированы под Alebardium: [MenuItem("Alebardium/Balance/0. Audit Content")], "Alebardium/Balance/1. DPS Bench (all relics)", "…/1. Survivability Bench", "…/2. Duel Matrix + Rating", "…/Run Selected Scenario".
  **где:** Assets/_Project/Scripts/Balance/Editor/BalanceMenu.cs:15-39

#### `.claude/skills/BACKLOG.md` — update

- знач. — **док:** Таблица «Готовы» перечисляет uitk, combat-sim, gdd-scribe, data-authoring, gamefeel-vfx, tech-scribe, audio — семь скиллов
  **код:** В .claude/skills лежат восемь SKILL.md: перечисленные семь + xgaida-x-nixi-content-design (оркестратор контента, зарегистрирован и триггерится). Ещё есть xgaida-x-nixi-balance/DRAFT.md, о котором реестр тоже молчит, хотя сам DRAFT ссылается на «черновик скилла» как на живую сущность.
  **где:** ls .claude/skills; .claude/skills/xgaida-x-nixi-content-design/SKILL.md; .claude/skills/xgaida-x-nixi-balance/DRAFT.md
- знач. — **док:** «Связанные задачи»: «Завести vision + дизайн-столпы [план] — новые артефакты 10-vision/; пишет Макс, оформляю я»
  **код:** Оба артефакта существуют: docs/wiki/gdd/10-vision/vision.md и pillars.md (плюс pitch.md, visual-direction.md и др.).
  **где:** ls docs/wiki/gdd/10-vision
- знач. — **док:** Строка gamefeel-vfx в таблице «Готовы»: «…целевой шов префаб-VFX … ОПРЕДЕЛЕНИЕ VfxData SO — data-authoring (целевой шов, не построен)»
  **код:** Шов построен: VfxData + PooledVfx + CombatVfx-пул работают, VfxData зарегистрирован доменом vfx в ContentDomains.
  **где:** Assets/_Project/Scripts/Data/Definitions/VfxData.cs; Assets/_Project/Scripts/Presentation/PooledVfx.cs; Assets/_Project/Scripts/Presentation/CombatVfx.cs; Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs:22

---

## Граница с ГДД (дополнено 2026-07-26 заходом по ГДД)

> Раздел дописан **снаружи**, из аудита геймдизайн-вики, чтобы при исполнении фаз 1-2 не
> переписывать заново то, у чего канон-дом в другой вике. Сам реестр находок выше не трогался.

Три факта имели **второй дом** в тех-вике. В доки проставлены врезки-указатели (только шапки и
преамбулы разделов, содержание не менялось):

| Факт | Канон-дом (ГДД) | Что в тех-вике | Что делать при rewrite |
|---|---|---|---|
| **Модель урона** (школа/сродство/тип существа) | [[gdd/20-combat/stats\|Combat - Stats]] §Школа vs сродство | `10-reference/combat-model.md` §6.1 | Оставить **реализацию** (enum, override, сборка `DamageType`), не пересказывать дизайн-модель. **Сперва вердикт Макса по `AffinityTable`** — см. ниже |
| **Модель перезапуска** | [[gdd/20-combat/combat-system\|Combat - System]] §Перезапуск | `20-explanation/run-flow.md` §6 | Дизайн-часть («до 2 на бой», «3-е финальное», «цена TBD») **устарела** — заменить ссылкой на ГДД. Инженерная часть (саб-сид без номера попытки) остаётся |
| **Структура акта и талия** | [[gdd/30-run-meta/events-minigames\|Run - Events & Minigames]] §Место привалов | `40-planning/act-map-run-loop.md` | Это **архив замысла**; числа оставить как след, врезка уже поясняет, что источник правды — ГДД + `ActConfig` |

### Решено 2026-07-26 (ночь-3): `AffinityTable` снимается

**Вердикт Макса: сродство — идейная часть, а не сетка резистов.** Матрица подлежит снятию из кода
(`AffinityTable.cs`, вызов в `DamagePipeline`, четыре теста). `combat-model` §6.1 переписывать
**после** правки кода — сейчас он верно описывает фактическое поведение. Ниже — исходный разбор.

**`AffinityTable` — работающая механика, которой нет в дизайне.** `DamagePipeline.cs:53` вызывает
`AffinityTable.Multiplier(affinity, CreatureType)`: яд ×0 по `Undead`/`Construct`, свет ×1.3 по
`Undead`/`Demon`, тьма ×1.3 по `Living`. Это универсальная матрица «тип → уязвимость», которую
ГДД **отклонил** решением 2026-07-15/35 (тип существа — «редкая специя» на отдельных врагах, а не
главный гейт сродства).

Вариантов два, и оба — дизайн-решение, не редактура: **легализовать** матрицу в ГДД (тогда
`combat-model` §6.1 верен и правки не требует) либо **снять** её из пайплайна (тогда правится код,
а не док). До вердикта раздел `combat-model` §6.1 **не переписывать** — он честно описывает
фактическое поведение кода.
