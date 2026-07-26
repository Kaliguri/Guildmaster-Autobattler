using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Game.Flow;
using Guildmaster.Presentation;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Оркестратор интерактивной фазы расстановки (план шаг 4). На загрузку Free-пресета
    /// (<see cref="EncounterLoader.FreeDeploymentRequested"/>) ставит бой на паузу, флашит спавны и даёт
    /// игроку таскать своих юнитов (team 0) в пределах player-зон; «Готово» (Enter) стартует бой.
    /// <list type="bullet">
    /// <item>Пикинг — математикой по радиусу тела (без коллайдеров), ближайший team-0 юнит под курсором.</item>
    /// <item>Drag с валидацией <see cref="DeploymentService.CanPlace"/> + анти-оверлап при drop (reject).</item>
    /// <item>Дабл-клик по юниту → публикация <see cref="OpenLoadoutRequest"/> (открывает loadout, шаг 5).</item>
    /// <item>Экип релика (<see cref="EquipRelicRequest"/>) правит ростер и пересобирает превью респавном.</item>
    /// </list>
    /// Живёт в боевом скоупе как EntryPoint. Хост-авторитативные сетевые команды расстановки — будущий шов
    /// (сейчас всё локально; drop = прямая правка позиции, а не команда хосту).
    /// </summary>
    public sealed class DeploymentController : IStartable, ITickable, IDisposable
    {
        private const float DoubleClickWindow = 0.30f;
        private const float DragMinDelta       = 0.05f; // мир-единицы: меньше = «клик», больше = «drag»
        private const float PickRadiusScale    = 1.3f;  // круг-опора × это = «ближняя» зона хватания (у ног)
        private const float FigurePickPadding  = 0.08f; // мировой запас вокруг фигуры: чуть-чуть, не «гигантский»

        private readonly EncounterLoader  _loader;
        private readonly CombatSimulation _sim;
        private readonly DeploymentService _deploy;
        private readonly IInputService    _input;
        private readonly CombatPresenter  _presenter;
        private readonly ArenaLayoutData  _layout;
        private readonly IPublisher<OpenLoadoutRequest> _openLoadoutPub;
        private readonly ISubscriber<EquipRelicRequest> _equipSub;
        private readonly ISubscriber<EquipRelicAtCursorRequest> _equipAtCursorSub;
        private readonly ISubscriber<RelicDragEvent> _relicDragSub; // QA #5: drag реликвии из инвентаря на юнита
        private readonly ISubscriber<SetTestZoneRequest> _testZoneSub; // радио-табы: целевое состояние тест-зоны (интент)
        private readonly ISubscriber<SetFormationRequest> _formationSub; // кнопка передышки «К построению» (интент)
        private readonly IPublisher<TestZoneChangedEvent> _testZoneChangedPub; // Ф5: вещаем СОСТОЯНИЕ (единый источник)
        private readonly IBattleSession   _session;
        private readonly CameraModeController _cameraModes; // свободная камера расстановки (QA #4); null в headless
        private readonly Guildmaster.Guild.RunStateService _runStates; // durable-гильдия: сюда уезжают позиции и киты

        // Редактируемый ростер игрока в этой фазе (позиции/релики меняются перетаскиванием и loadout'ом).
        // GuildIndex — тот же слот в durable-гильдии забега (RunState.Guild): по нему правки уезжают в сейв,
        // иначе расстановка и надетые релики жили бы только до конца боя (наход. Макса, п.5).
        private sealed class Slot
        {
            public RelicData Relic; public VesselData Vessel; public Vector2 Pos;
            public int LiveUnitId = -1;
            public int GuildIndex = -1;
        }
        private readonly List<Slot> _slots = new List<Slot>();
        private EncounterData _encounter;

        private DeploymentView _view;
        private Camera _camera;
        private IDisposable _equipSubscription;
        private IDisposable _equipAtCursorSubscription;
        private IDisposable _relicDragSubscription;
        private IDisposable _testZoneSubscription;
        private IDisposable _formationSubscription;

        private bool _deploying;
        private bool _testZone; // QA #2: текущая расстановка — СЕРЫЙ полигон вне забега (не боевой узел, не построение)
        private BattlePhase _sandboxReturnPhase = BattlePhase.None; // куда вернуть фазу, выйдя из расстановки-без-боя
        private RuntimeUnit _dragged;
        private Vector2 _dragStartWorld;
        // Схваченная точка фигурки: сим-позиция юнита минус курсор в момент захвата. Юнит НЕ прыгает центром
        // под курсор («магнит») — держим его за то место, за которое взяли, как настоящую фигурку на столе.
        private Vector2 _grabOffset;
        // Ноги минус сим-позиция на момент захвата: круг-опору рисуем у ног ПРИЗРАКА, а не под курсором.
        // Замеряем один раз при захвате — иначе дрожание кадра анимации ёрзало бы кругом.
        private Vector2 _feetOffset;
        private bool _dragMoved;
        private int _hoverUnitId = -1;
        private float _lastClickTime;
        private int _lastClickUnitId = -1;

        private RelicData _relicDrag;        // QA #5: тащим реликвию из инвентаря (null = нет); ghost её силуэта

        public DeploymentController(
            EncounterLoader loader,
            CombatSimulation sim,
            DeploymentService deploy,
            IInputService input,
            CombatPresenter presenter,
            ArenaLayoutData layout,
            IPublisher<OpenLoadoutRequest> openLoadoutPub,
            ISubscriber<EquipRelicRequest> equipSub,
            ISubscriber<EquipRelicAtCursorRequest> equipAtCursorSub,
            ISubscriber<RelicDragEvent> relicDragSub,
            ISubscriber<SetTestZoneRequest> testZoneSub,
            ISubscriber<SetFormationRequest> formationSub,
            IPublisher<TestZoneChangedEvent> testZoneChangedPub,
            IBattleSession session,
            CameraModeController cameraModes,
            Guildmaster.Guild.RunStateService runStates)
        {
            _runStates     = runStates;
            _loader        = loader;
            _sim           = sim;
            _deploy        = deploy;
            _input         = input;
            _presenter     = presenter;
            _layout        = layout;
            _openLoadoutPub = openLoadoutPub;
            _equipSub      = equipSub;
            _equipAtCursorSub = equipAtCursorSub;
            _relicDragSub  = relicDragSub;
            _testZoneSub   = testZoneSub;
            _formationSub  = formationSub;
            _testZoneChangedPub = testZoneChangedPub;
            _session       = session;
            _cameraModes   = cameraModes;
        }

        public void Start()
        {
            _loader.FreeDeploymentRequested += OnFreeDeployment;
            _input.PointerPressed  += OnPointerPressed;
            _input.PointerReleased += OnPointerReleased;
            _equipSubscription = _equipSub.Subscribe(OnEquip);
            _equipAtCursorSubscription = _equipAtCursorSub.Subscribe(OnEquipAtCursor);
            _relicDragSubscription = _relicDragSub?.Subscribe(OnRelicDrag);
            _testZoneSubscription = _testZoneSub?.Subscribe(OnSetTestZone);
            _formationSubscription = _formationSub?.Subscribe(OnSetFormation);

            // Верхняя панель забега (план 12): часы боя + кнопка «Начать».
            // Persist-мир: скоуп живёт всю сессию, поэтому фазу НЕ выставляем на Start (иначе вне боя
            // Phase залипал бы на Fighting и ломал guard'ы топбара — вылет при клике «Бой» на ивенте).
            // Фаза выставляется по факту: Deployment на входе в бой (OnFreeDeployment), Fighting на «Начать»,
            // None — вне боя (сброс через BattleBootstrap.ResetToWorld).
            _session.BindClock(() => _sim.ElapsedSeconds);
            // «Начать» стартует бой только из БОЕВОЙ расстановки. В тест-зоне бой пока не запускается (полигон
            // только для расстановки/реликвий — решение Макса); кнопка там — no-op до появления боя в тест-зоне.
            _session.BindStart(() => { if (_deploying && !_testZone) StartCombat(); });
        }

        public void Dispose()
        {
            _loader.FreeDeploymentRequested -= OnFreeDeployment;
            _input.PointerPressed  -= OnPointerPressed;
            _input.PointerReleased -= OnPointerReleased;
            _equipSubscription?.Dispose();
            _equipAtCursorSubscription?.Dispose();
            _relicDragSubscription?.Dispose();
            _testZoneSubscription?.Dispose();
            _formationSubscription?.Dispose();
            _session.UnbindStart();
            _session.UnbindClock(); // сбрасывает фазу в None → панель скрывается между боями
            if (_view != null) UnityEngine.Object.Destroy(_view.gameObject);
        }

        // ── Вход в фазу ──────────────────────────────────────────────────────
        private void OnFreeDeployment(BattlePresetData preset)
        {
            // Юниты уже поставлены в очередь (Load внутри LoadPreset). Пауза + флаш → присутствуют, но заморожены.
            _sim.SetPaused(true);
            _sim.FlushSpawns();

            _encounter = preset.Encounter;
            _slots.Clear();
            if (preset.Roster != null)
                for (int i = 0; i < preset.Roster.Count; i++)
                {
                    PlayerSlot s = preset.Roster[i];
                    if (s.Relic == null) continue;
                    // Ростер боя собран из гильдии слот-в-слот (GuildRoster.Resolve), поэтому индекс здесь =
                    // индекс сосуда в RunState.Guild. Инспекторный dev-пресет гильдии не касается — там запись
                    // в сейв просто не сработает (нет забега / длина не сойдётся, проверяем в PersistSlot).
                    _slots.Add(new Slot { Relic = s.Relic, Vessel = s.Vessel, Pos = s.Position, GuildIndex = i });
                }
            RemapLiveUnits();

            EnsureView();
            _view.SetActive(true);
            _deploying = true;
            _testZone  = false; // боевая расстановка (узел боя), не тест-зона
            _session.SetPhase(BattlePhase.Deployment); // центр панели = «Начать»; фаза → навигатор ставит контекст Deployment (K8)
            _testZoneChangedPub?.Publish(new TestZoneChangedEvent(false)); // Ф5: боевая расстановка ≠ тест-зона (гарантия сброса)
            FrameCameraForDeployment(); // QA #4: свободная камера со стартовым боевым кадром (не отзум на всю зону)
        }

        // ── Тест-зона (QA #2): «Бой» вне забега → расстановка стоящего отряда БЕЗ врагов ────────
        // Отряд уже стоит на арене вне боя (WorldStageController по RunPartyReadyEvent). Тумблер: вошли в
        // тест-расстановку → «Бой» ещё раз выходит. Боевую расстановку (узел боя) тумблер не трогает.
        // Радио-режимы: топбар просит целевое СОСТОЯНИЕ (Active=бой, !Active=не-бой). Идемпотентно — повтор
        // того же = no-op (табы переключают режим, не тоглят). Вход только вне боя из стоящего отряда;
        // выход — только из ТЕСТ-расстановки (боевую, !testZone, «Карта» не трогает).
        private void OnSetTestZone(SetTestZoneRequest req)
        {
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.OnSetTestZone(Active={req.Active}) (deploying={_deploying}, testZone={_testZone}, phase={_session.Phase})");
            if (req.Active)
            {
                if (_deploying) { Guildmaster.Diagnostics.UiTrace.Log("ctrl: уже в расстановке — no-op"); return; } // тест или боевая — уже в бою
                if (!CanEnterSandbox()) { Guildmaster.Diagnostics.UiTrace.Log($"ctrl: фаза {_session.Phase} — вход в тест-зону запрещён"); return; }
                EnterSandbox(grayZone: true);
            }
            else
            {
                if (_deploying && _testZone) ExitTestZone(); // выйти из ТЕСТ-расстановки; боевую не трогаем
                else Guildmaster.Diagnostics.UiTrace.Log("ctrl: не в тест-зоне — выходить нечего (no-op)");
            }
        }

        // ── «К построению» (передышка между узлами) ──────────────────────────
        // Та же расстановка без врагов, что и полигон, но арена остаётся БОЕВОЙ (цветной): игрок правит строй
        // между узлами забега, а не тестирует вне его. Выход — та же кнопка/таб (см. ExitTestZone).
        private void OnSetFormation(SetFormationRequest req)
        {
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.OnSetFormation(Active={req.Active}) (deploying={_deploying}, phase={_session.Phase})");
            if (req.Active)
            {
                if (_deploying) return;                 // уже расставляем — просить нечего
                if (!CanEnterSandbox()) { Guildmaster.Diagnostics.UiTrace.Log($"ctrl: фаза {_session.Phase} — построение запрещено"); return; }
                EnterSandbox(grayZone: false);
            }
            else if (_deploying && _testZone == false && _encounter == null)
            {
                ExitTestZone(); // вышли из построения тем же путём (арена и так боевая)
            }
        }

        // Вставать в расстановку без врагов можно только когда мира на экране нет чужого хозяина: вне забега
        // (None) или в передышке между узлами (Interlude). Во время боя/боевой расстановки — нельзя.
        private bool CanEnterSandbox() =>
            _session.Phase == BattlePhase.None || _session.Phase == BattlePhase.Interlude;

        private void EnterSandbox(bool grayZone)
        {
            // Строим редактируемые слоты из УЖЕ стоящих team-0 юнитов (не пере-спавниваем). Нет отряда
            // (забег не начат) → нечего расставлять; демо-отряд для теста из главного меню — отдельная итерация.
            _slots.Clear();
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;
                // Отряд спавнится в порядке гильдии, поэтому порядковый номер живого team-0 = индекс сосуда:
                // правки в тест-зоне уезжают в тот же сейв, что и правки в боевой расстановке.
                _slots.Add(new Slot { Relic = u.Unit as RelicData, Pos = u.Position, LiveUnitId = u.Id, GuildIndex = _slots.Count });
            }
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.EnterSandbox(gray={grayZone}) (слотов из стоящих team-0: {_slots.Count})");
            if (_slots.Count == 0)
            {
                Debug.LogWarning("[DeploymentController] - расстановка без боя: отряд не стоит (нет активного забега) → пропуск");
                return;
            }

            _sandboxReturnPhase = _session.Phase; // куда вернуть панель на выходе (None вне забега / Interlude в передышке)
            _encounter = null;     // без врагов — полигон
            _sim.SetPaused(true);
            EnsureView();
            _view.SetActive(true);
            _deploying = true;
            _testZone  = grayZone;
            _session.SetPhase(BattlePhase.Deployment); // фаза → навигатор ставит контекст Deployment (K8)
            // Серой арена становится ТОЛЬКО на полигоне вне забега. Построение между узлами идёт по боевой.
            if (grayZone) _testZoneChangedPub?.Publish(new TestZoneChangedEvent(true));
            FrameCameraForDeployment();
        }

        private void ExitTestZone()
        {
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.ExitTestZone → phase {_sandboxReturnPhase}, TestZoneChanged(false)");
            FlushRoster(); // что переставили — то и останется в гильдии
            bool wasGray = _testZone;
            _deploying = false;
            _testZone  = false;
            _dragged   = null;
            _relicDrag = null;
            _view?.SetActive(false);
            _cameraModes?.ExitToActionView();
            // Возвращаем ТУ фазу, из которой вставали: вне забега — None (панель без «Начать»), в передышке —
            // Interlude (мир на экране, задник UI по-прежнему запрещён).
            _session.SetPhase(_sandboxReturnPhase);
            if (wasGray) _testZoneChangedPub?.Publish(new TestZoneChangedEvent(false)); // цветная арена + снять Sheet
        }

        // Стартовый кадр расстановки: центр и разброс ВСЕХ живых юнитов (свои + враги — видно противника).
        // Считаем сами (не через focus-таймер) — детерминированно на входе, без гонки с LateUpdate камеры.
        private void FrameCameraForDeployment()
        {
            if (_cameraModes == null) return;

            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsDead) continue;
                sum += units[i].Position;
                n++;
            }
            if (n == 0) return; // нет юнитов — камера остаётся как есть

            Vector2 center = sum / n;
            float maxSq = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsDead) continue;
                float d = (units[i].Position - center).sqrMagnitude;
                if (d > maxSq) maxSq = d;
            }
            _cameraModes.EnterDeployment(center, Mathf.Sqrt(maxSq));
        }

        private void EnsureView()
        {
            if (_view != null) return;
            var go = new GameObject("DeploymentView");
            _view = go.AddComponent<DeploymentView>();
            _view.Init(_layout);
        }

        // Сопоставить слоты ростера живым team-0 юнитам (спавнятся в порядке ростера).
        private void RemapLiveUnits()
        {
            int idx = 0;
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count && idx < _slots.Count; i++)
            {
                if (units[i].Team != 0 || units[i].IsDead) continue;
                _slots[idx].LiveUnitId = units[i].Id;
                idx++;
            }
        }

        // ── Покадровая интеракция ────────────────────────────────────────────
        public void Tick()
        {
            if (!_deploying) return;

            // «Готово» — стартуем бой (Enter). Работает даже при открытом меню? нет — только в чистой фазе.
            if (!_input.GameplaySuppressed && ReadyPressed()) { StartCombat(); return; }

            // Реликвия-drag из инвентаря (QA #5): призрак силуэта реликвии виден ВЕЗДЕ, пока тащим (в т.ч. над
            // панелью грида — ghost рисуется поверх мира), цель эквипа под курсором подсвечиваем кругом. Юнит-
            // drag/ховер в это время не трогаем — это отдельный жест поверх UI.
            if (_relicDrag != null) { DrawRelicDragGhost(); return; }

            // Меню loadout открыто (ввод заглушён) или курсор над непрозрачной UITK-панелью (инвентарь) вне
            // активного драга — не интеракчим (ховер/ghost гасим), но круги-опоры оставляем видимыми (QA #20:
            // читаемость поля не зависит от того, где курсор).
            if (_input.GameplaySuppressed || (_input.PointerOverUI && _dragged == null))
            {
                HideGhostSprite();
                UpdateUnitRings(-1, default, false, false);
                return;
            }

            Vector2 world = ScreenToWorld(_input.PointerScreenPosition);
            int hoverId = -1;
            bool dragValid = false;

            Vector2 dragTarget = default;
            if (_dragged != null)
            {
                if ((world - _dragStartWorld).sqrMagnitude > DragMinDelta * DragMinDelta) _dragMoved = true;
                dragTarget = DragTarget(world); // куда встанет юнит, если отпустить здесь (с учётом точки захвата)
                dragValid = _deploy.CanPlace(dragTarget, DeploymentSide.Player, CanUseExtended(_dragged))
                            && !Overlaps(dragTarget, _dragged);
                ShowDragGhost(dragTarget, dragValid); // призрак-силуэт у целевых ног (QA #9)
            }
            else
            {
                RuntimeUnit hover = PickUnit(world);
                hoverId = hover != null ? hover.Id : -1;
                _hoverUnitId = hoverId;
                HideGhostSprite();
            }

            UpdateUnitRings(hoverId, dragTarget + _feetOffset, dragValid, _dragged != null);
        }

        // Круги-опоры под ногами живых team-0 юнитов (QA #20/#3): всегда видны (читаемость), наведённый — ярче.
        // У перетаскиваемого кругов ДВА (реш. Макса): на его месте — ярко горящий («тащишь именно меня»), и у ног
        // призрака — по валидности drop. Так видно и кого взял, и куда он встанет.
        private readonly List<(Vector2 center, float radius, DeploymentView.RingState state)> _ringBuffer = new();
        private void UpdateUnitRings(int hoverId, Vector2 dragFeet, bool dragValid, bool dragging)
        {
            _ringBuffer.Clear();
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;

                bool isDragged = dragging && _dragged != null && u.Id == _dragged.Id;
                DeploymentView.RingState st = isDragged || u.Id == hoverId
                    ? DeploymentView.RingState.Hover
                    : DeploymentView.RingState.Normal;
                _ringBuffer.Add((FeetOf(u), BodyRadius(u), st)); // у ног (визуальных, не центр — QA #3)

                if (isDragged) // + целевой круг у ног призрака (следует за курсором)
                    _ringBuffer.Add((dragFeet, BodyRadius(u),
                                     dragValid ? DeploymentView.RingState.DragValid : DeploymentView.RingState.DragInvalid));
            }
            _view.SetUnitRings(_ringBuffer);
        }

        // Куда встанет перетаскиваемый юнит, если отпустить курсор в точке world. Не «центром под курсор», а
        // со смещением, снятым в момент захвата: взял за левый край — ведёшь за левый край (наход. Макса, п.4).
        private Vector2 DragTarget(Vector2 world) => world + _grabOffset;

        // Призрак-силуэт перетаскиваемого юнита в целевой сим-позиции — через ЕДИНЫЙ источник UnitSilhouette
        // (QA #5: тот же вид «в руке», что и при drag реликвии из инвентаря). Нет вида (headless / спрайт не
        // готов) → без призрака (круг DragValid/Invalid всё равно ведёт цель).
        private void ShowDragGhost(Vector2 target, bool valid)
        {
            UnitSilhouette sil = UnitSilhouette.None;
            if (_presenter != null && _presenter.TryGetView(_dragged.Id, out UnitView dv))
                sil = UnitSilhouette.FromView(dv, FeetOf(_dragged)); // офсет арта — от ТЕКУЩИХ ног живого вида

            // Рисуем тот же силуэт у ЦЕЛЕВЫХ ног: ноги призрака = целевая сим-позиция + замер «ноги-минус-центр».
            if (sil.Valid) _view.SetGhost(true, target + _feetOffset, sil.Offset, sil.Sprite, sil.FlipX, sil.Scale, valid);
            else HideGhostSprite();
        }

        // Мировая точка ног юнита (визуальный FeetPoint из вида, а не сим-центр) — круг/pick садятся под ноги
        // спрайта, а не в центр фигуры (QA #3). Фолбэк — сим-позиция (headless / вид не готов).
        private Vector2 FeetOf(RuntimeUnit u)
        {
            if (_presenter != null && _presenter.TryGetView(u.Id, out UnitView view) && view != null)
            {
                Vector3 f = view.FeetPoint;
                return new Vector2(f.x, f.y);
            }
            return u.Position;
        }

        private void HideGhostSprite() => _view.SetGhost(false, default, default, null, false, Vector3.one, false);

        private void HideDragVisuals() => HideGhostSprite();

        // ── Drag реликвии из инвентаря на юнита (QA #5) ───────────────────────
        // UITK-грид публикует RelicDragEvent (Start/Move/Drop). Вне расстановки пока не поддержано — эквип
        // на тест-арене придёт с #26. Ghost/подсветку рисует DrawRelicDragGhost из Tick, Drop надевает реликвию.
        private void OnRelicDrag(RelicDragEvent e)
        {
            if (!_deploying) return;
            switch (e.Phase)
            {
                case RelicDragPhase.Start:
                case RelicDragPhase.Move:
                    _relicDrag = e.Relic; // позицию берём из _input в Tick/Drop (тот же источник, что deployment-pick)
                    break;
                case RelicDragPhase.Drop:
                    RuntimeUnit target = e.Relic != null ? PickUnit(ScreenToWorld(_input.PointerScreenPosition)) : null;
                    if (target != null && e.Relic != null) EquipOn(target.Id, e.Relic);
                    _relicDrag = null;
                    HideGhostSprite();
                    break;
            }
        }

        // Призрак силуэта реликвии у курсора (единый вид «в руке», как drag юнита — из ViewPrefab, т.к. юнита
        // на поле ещё нет) + подсветка юнита под курсором (цель эквипа). Круги-опоры остаются видимыми.
        private void DrawRelicDragGhost()
        {
            Vector2 world = ScreenToWorld(_input.PointerScreenPosition);
            RuntimeUnit target = PickUnit(world);

            UnitSilhouette sil = UnitSilhouette.FromPrefab(_relicDrag != null ? _relicDrag.ViewPrefab : null);
            if (sil.Valid) _view.SetGhost(true, world, sil.Offset, sil.Sprite, sil.FlipX, sil.Scale, target != null);
            else HideGhostSprite();

            UpdateUnitRings(target != null ? target.Id : -1, default, false, false);
        }

        private void OnPointerPressed()
        {
            if (!_deploying || _input.GameplaySuppressed) return;

            Vector2 world = ScreenToWorld(_input.PointerScreenPosition);
            RuntimeUnit unit = PickUnit(world);
            if (unit == null) return;

            float now = Time.unscaledTime;
            bool doubleClick = unit.Id == _lastClickUnitId && (now - _lastClickTime) < DoubleClickWindow;
            _lastClickTime = now;
            _lastClickUnitId = unit.Id;

            if (doubleClick) { OpenLoadout(unit); return; }

            // Начинаем протяжку (различаем клик/drag по пройденной дистанции на release).
            _dragged = unit;
            _dragStartWorld = world;
            _grabOffset = unit.Position - world;      // держим фигурку за схваченное место, а не за центр
            _feetOffset = FeetOf(unit) - unit.Position; // куда относительно центра садится круг-опора
            _dragMoved = false;
            _view.SetExtendedHighlight(CanUseExtended(unit));
        }

        private void OnPointerReleased()
        {
            if (_dragged == null) return;

            if (_dragMoved) // именно перетаскивание (не клик) → пробуем поставить
            {
                // Та же целевая точка, что вела призрака: иначе юнит на отпускании прыгал бы к курсору.
                Vector2 target = DragTarget(ScreenToWorld(_input.PointerScreenPosition));
                if (_deploy.CanPlace(target, DeploymentSide.Player, CanUseExtended(_dragged)) && !Overlaps(target, _dragged))
                {
                    _dragged.Position = target;
                    _dragged.PreviousPosition = target; // снап вида (без слайда интерполяции)
                    UpdateSlotPos(_dragged.Id, target);
                }
                // невалидно → юнит остаётся на месте (reject)
            }

            _dragged = null;
            _dragMoved = false;
            HideDragVisuals();
            _view.SetExtendedHighlight(false);
        }

        // ── Loadout ──────────────────────────────────────────────────────────
        private void OpenLoadout(RuntimeUnit unit)
        {
            Slot slot = FindSlot(unit.Id);
            _openLoadoutPub.Publish(new OpenLoadoutRequest(unit.Id, slot?.Relic, slot?.Vessel));
        }

        private void OnEquip(EquipRelicRequest req)
        {
            if (req.Relic == null) return;
            EquipOn(req.UnitId, req.Relic);
        }

        // Дроп карточки релика в поле: сосуд под курсором резолвим сами — тем же экраном→миром и пикингом, что
        // и деплой-драг, поэтому попадание совпадает с ховер-кольцом. Мимо сосуда (пустое поле) → no-op.
        private void OnEquipAtCursor(EquipRelicAtCursorRequest req)
        {
            if (!_deploying || req.Relic == null) return;
            RuntimeUnit unit = PickUnit(ScreenToWorld(req.ScreenPosition));
            if (unit == null) return;
            EquipOn(unit.Id, req.Relic);
        }

        private void EquipOn(int unitId, RelicData relic)
        {
            if (!_deploying) return;
            Slot slot = FindSlot(unitId);
            if (slot == null) return;
            slot.Relic = relic;
            // Надетый прямо на поле кит — изменение ГИЛЬДИИ, а не превью боя (реш. Макса): переживает бой и сейв.
            if (_runStates?.SetSlotRelic(slot.GuildIndex, relic.Id) == true) _rosterDirty = true;
            RebuildPreview();
        }

        // Пересобрать превью боя из редактируемого ростера (респавн через штатный путь — виды перестраиваются).
        private void RebuildPreview()
        {
            var side = new List<PlayerSpawn>(_slots.Count);
            foreach (Slot s in _slots) side.Add(new PlayerSpawn(s.Relic, s.Vessel, s.Pos));

            _loader.Load(_encounter, side); // ResetBattle + enqueue (сбрасывает паузу)
            _sim.SetPaused(true);
            _sim.FlushSpawns();
            RemapLiveUnits();

            _dragged = null;
            _dragMoved = false;
            _hoverUnitId = -1;
        }

        // ── Старт боя ────────────────────────────────────────────────────────
        private void StartCombat()
        {
            FlushRoster(); // расстановка, с которой идём в бой, должна пережить и бой, и вылет игры
            _deploying = false;
            _testZone = false;
            _dragged = null;
            _view?.SetActive(false);
            _sim.SetPaused(false);
            _cameraModes?.ExitToActionView(); // QA #4: вернуть боевой вид (слежение) на старте боя
            _session.SetPhase(BattlePhase.Fighting); // центр панели = таймер боя; фаза → навигатор ставит контекст Combat (K8)
            _testZoneChangedPub?.Publish(new TestZoneChangedEvent(false)); // Ф5: бой начался → не тест-зона (гарантия сброса)
        }

        // ── Хелперы ──────────────────────────────────────────────────────────
        private static bool ReadyPressed()
        {
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        }

        // Захват — двухслойный (реш. Макса): круг-опора у ног ИЛИ сама фигура юнита.
        // Круг главнее: он нарисован и читается как «место юнита», поэтому попадание в чей-то круг всегда
        // бьёт попадание в чужую фигуру (иначе высокий сосед перехватывал бы клик по ногам соседа).
        // Внутри слоя выигрывает ближайший по ногам — «хватаем круг ближайшего».
        private RuntimeUnit PickUnit(Vector2 world)
        {
            RuntimeUnit bestRing = null; float bestRingSq = float.MaxValue;
            RuntimeUnit bestBody = null; float bestBodySq = float.MaxValue;

            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;

                float r  = BodyRadius(u) * PickRadiusScale;
                float sq = (world - FeetOf(u)).sqrMagnitude;
                if (sq <= r * r)
                {
                    if (sq < bestRingSq) { bestRing = u; bestRingSq = sq; }
                    continue; // в круг попали — по фигуре этого же юнита проверять нечего
                }

                if (FigureHit(u, world) && sq < bestBodySq) { bestBody = u; bestBodySq = sq; }
            }
            return bestRing ?? bestBody;
        }

        // Попал ли курсор в фигуру юнита — по ЭТАЛОННОМУ габариту (зелёная рамка гизмо UnitView), а не по AABB
        // кадра: AABB скелетной анимации шире фигуры (замах, плащ, прозрачные поля), и зона хватания выходила
        // гигантской (наход. Макса). Нет вида (headless) → false: работает только круг-опора.
        private bool FigureHit(RuntimeUnit u, Vector2 world) =>
            _presenter != null
            && _presenter.TryGetView(u.Id, out UnitView view)
            && view != null
            && view.FigureContainsWorldPoint(world, FigurePickPadding);

        private bool Overlaps(Vector2 pos, RuntimeUnit exclude)
        {
            float r = BodyRadius(exclude);
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u == exclude || u.IsDead) continue;
                float min = r + BodyRadius(u);
                if ((pos - u.Position).sqrMagnitude < min * min) return true;
            }
            return false;
        }

        private float BodyRadius(RuntimeUnit u) => CombatPositioning.BodyRadius(u, _sim.Tuning);

        private static bool CanUseExtended(RuntimeUnit u) =>
            (u.Unit as RelicData)?.CanUseExtendedDeployment ?? false;

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return screen; // нет камеры → возвращаем как есть (пикинг просто не совпадёт)
            Vector3 w = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        private Slot FindSlot(int unitId)
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].LiveUnitId == unitId) return _slots[i];
            return null;
        }

        private void UpdateSlotPos(int unitId, Vector2 pos)
        {
            Slot s = FindSlot(unitId);
            if (s == null) return;
            s.Pos = pos;
            if (_runStates?.SetSlotPosition(s.GuildIndex, pos) == true) _rosterDirty = true;
        }

        // Правки расстановки уезжают в durable-гильдию сразу, а на диск — на выходе из фазы (старт боя, выход
        // из тест-зоны). Писать сейв на каждый drop незачем: за одну расстановку их десятки, а состояние в
        // RunState уже актуально — автосейв узла подхватит его и без нас.
        private bool _rosterDirty;

        private void FlushRoster()
        {
            if (!_rosterDirty) return;
            _rosterDirty = false;
            _runStates?.Autosave();
        }
    }
}
