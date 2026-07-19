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
        private const float PickRadiusScale    = 1.3f;  // зона хватания = круг-опора × это (чуть больше круга, QA #3)

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
        private readonly ISubscriber<ToggleTestZoneRequest> _testZoneSub; // QA #2: «Бой» вне забега = тест-зона
        private readonly IBattleSession   _session;
        private readonly CameraModeController _cameraModes; // свободная камера расстановки (QA #4); null в headless

        // Редактируемый ростер игрока в этой фазе (позиции/релики меняются перетаскиванием и loadout'ом).
        private sealed class Slot { public RelicData Relic; public VesselData Vessel; public Vector2 Pos; public int LiveUnitId = -1; }
        private readonly List<Slot> _slots = new List<Slot>();
        private EncounterData _encounter;

        private DeploymentView _view;
        private Camera _camera;
        private IDisposable _equipSubscription;
        private IDisposable _equipAtCursorSubscription;
        private IDisposable _relicDragSubscription;
        private IDisposable _testZoneSubscription;

        private bool _deploying;
        private bool _testZone; // QA #2: текущая расстановка — тест-зона вне забега (не боевой узел)
        private RuntimeUnit _dragged;
        private Vector2 _dragStartWorld;
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
            ISubscriber<ToggleTestZoneRequest> testZoneSub,
            IBattleSession session,
            CameraModeController cameraModes)
        {
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
            _testZoneSubscription = _testZoneSub?.Subscribe(OnToggleTestZone);

            // Верхняя панель забега (план 12): часы боя + кнопка «Начать».
            // Persist-мир: скоуп живёт всю сессию, поэтому фазу НЕ выставляем на Start (иначе вне боя
            // Phase залипал бы на Fighting и ломал guard'ы топбара — вылет при клике «Бой» на ивенте).
            // Фаза выставляется по факту: Deployment на входе в бой (OnFreeDeployment), Fighting на «Начать»,
            // None — вне боя (сброс через BattleBootstrap.ResetToWorld).
            _session.BindClock(() => _sim.ElapsedSeconds);
            _session.BindStart(() => { if (_deploying) StartCombat(); });
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
                foreach (PlayerSlot s in preset.Roster)
                    if (s.Relic != null)
                        _slots.Add(new Slot { Relic = s.Relic, Vessel = s.Vessel, Pos = s.Position });
            RemapLiveUnits();

            EnsureView();
            _view.SetActive(true);
            _input.SetContext(InputContext.Deployment);
            _deploying = true;
            _testZone  = false; // боевая расстановка (узел боя), не тест-зона
            _session.SetPhase(BattlePhase.Deployment); // центр панели = «Начать»
            FrameCameraForDeployment(); // QA #4: свободная камера со стартовым боевым кадром (не отзум на всю зону)
        }

        // ── Тест-зона (QA #2): «Бой» вне забега → расстановка стоящего отряда БЕЗ врагов ────────
        // Отряд уже стоит на арене вне боя (WorldStageController по RunPartyReadyEvent). Тумблер: вошли в
        // тест-расстановку → «Бой» ещё раз выходит. Боевую расстановку (узел боя) тумблер не трогает.
        private void OnToggleTestZone(ToggleTestZoneRequest _)
        {
            if (_deploying)
            {
                if (_testZone) ExitTestZone(); // из тест-расстановки — выйти; боевую (!_testZone) не трогаем
                return;
            }
            EnterTestZone();
        }

        private void EnterTestZone()
        {
            // Строим редактируемые слоты из УЖЕ стоящих team-0 юнитов (не пере-спавниваем). Нет отряда
            // (забег не начат) → нечего расставлять; демо-отряд для теста из главного меню — отдельная итерация.
            _slots.Clear();
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;
                _slots.Add(new Slot { Relic = u.Unit as RelicData, Pos = u.Position, LiveUnitId = u.Id });
            }
            if (_slots.Count == 0)
            {
                Debug.LogWarning("[DeploymentController] - тест-зона: отряд не стоит (нет активного забега) → пропуск");
                return;
            }

            _encounter = null;     // без врагов — полигон
            _sim.SetPaused(true);
            EnsureView();
            _view.SetActive(true);
            _input.SetContext(InputContext.Deployment);
            _deploying = true;
            _testZone  = true;
            _session.SetPhase(BattlePhase.Deployment);
            FrameCameraForDeployment();
        }

        private void ExitTestZone()
        {
            _deploying = false;
            _testZone  = false;
            _dragged   = null;
            _relicDrag = null;
            _view?.SetActive(false);
            _input.SetContext(InputContext.None);
            _cameraModes?.ExitToActionView();
            _session.SetPhase(BattlePhase.None); // вне боя — панель без «Начать»/таймера
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

            if (_dragged != null)
            {
                if ((world - _dragStartWorld).sqrMagnitude > DragMinDelta * DragMinDelta) _dragMoved = true;
                dragValid = _deploy.CanPlace(world, DeploymentSide.Player, CanUseExtended(_dragged)) && !Overlaps(world, _dragged);
                ShowDragGhost(world, dragValid); // призрак-силуэт у целевых ног (QA #9)
            }
            else
            {
                RuntimeUnit hover = PickUnit(world);
                hoverId = hover != null ? hover.Id : -1;
                _hoverUnitId = hoverId;
                HideGhostSprite();
            }

            UpdateUnitRings(hoverId, world, dragValid, _dragged != null);
        }

        // Круги-опоры под ногами живых team-0 юнитов (QA #20/#3): всегда видны (читаемость), наведённый — ярче.
        // Перетаскиваемый (QA #4) НЕ пропускается — его круг рисуется у ног ПРИЗРАКА (целевая точка dragFeet),
        // ярко + по валидности drop, чтобы было видно кого тащишь и можно ли поставить.
        private readonly List<(Vector2 center, float radius, DeploymentView.RingState state)> _ringBuffer = new();
        private void UpdateUnitRings(int hoverId, Vector2 dragFeet, bool dragValid, bool dragging)
        {
            _ringBuffer.Clear();
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;
                if (dragging && _dragged != null && u.Id == _dragged.Id)
                {
                    DeploymentView.RingState st = dragValid ? DeploymentView.RingState.DragValid : DeploymentView.RingState.DragInvalid;
                    _ringBuffer.Add((dragFeet, BodyRadius(u), st)); // круг у ног призрака (следует за курсором)
                }
                else
                {
                    DeploymentView.RingState st = u.Id == hoverId ? DeploymentView.RingState.Hover : DeploymentView.RingState.Normal;
                    _ringBuffer.Add((FeetOf(u), BodyRadius(u), st)); // у ног (визуальных, не центр — QA #3)
                }
            }
            _view.SetUnitRings(_ringBuffer);
        }

        // Призрак-силуэт перетаскиваемого юнита в целевой точке ног — через ЕДИНЫЙ источник UnitSilhouette
        // (QA #5: тот же вид «в руке», что и при drag реликвии из инвентаря). Нет вида (headless / спрайт не
        // готов) → без призрака (круг DragValid/Invalid всё равно ведёт цель).
        private void ShowDragGhost(Vector2 targetFeet, bool valid)
        {
            UnitSilhouette sil = UnitSilhouette.None;
            if (_presenter != null && _presenter.TryGetView(_dragged.Id, out UnitView dv))
                sil = UnitSilhouette.FromView(dv, _dragged.Position);

            if (sil.Valid) _view.SetGhost(true, targetFeet, sil.Offset, sil.Sprite, sil.FlipX, sil.Scale, valid);
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
            _dragMoved = false;
            _view.SetExtendedHighlight(CanUseExtended(unit));
        }

        private void OnPointerReleased()
        {
            if (_dragged == null) return;

            if (_dragMoved) // именно перетаскивание (не клик) → пробуем поставить
            {
                Vector2 world = ScreenToWorld(_input.PointerScreenPosition);
                if (_deploy.CanPlace(world, DeploymentSide.Player, CanUseExtended(_dragged)) && !Overlaps(world, _dragged))
                {
                    _dragged.Position = world;
                    _dragged.PreviousPosition = world; // снап вида (без слайда интерполяции)
                    UpdateSlotPos(_dragged.Id, world);
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
            _deploying = false;
            _testZone = false;
            _dragged = null;
            _view?.SetActive(false);
            _sim.SetPaused(false);
            _input.SetContext(InputContext.Combat);
            _cameraModes?.ExitToActionView(); // QA #4: вернуть боевой вид (слежение) на старте боя
            _session.SetPhase(BattlePhase.Fighting); // центр панели = таймер боя
        }

        // ── Хелперы ──────────────────────────────────────────────────────────
        private static bool ReadyPressed()
        {
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        }

        // Захват по кругу-опоре у НОГ (QA #3): раньше пикали по всему AABB спрайта → зона хватания была
        // значительно больше видимого круга и «в центре». Теперь зона = круг у ног радиусом BodyRadius,
        // чуть увеличенным (PickRadiusScale), — совпадает с рисуемым кругом. Ближайший под курсором.
        private RuntimeUnit PickUnit(Vector2 world)
        {
            RuntimeUnit best = null;
            float       bestSq = float.MaxValue;

            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;

                float r  = BodyRadius(u) * PickRadiusScale;
                float sq = (world - FeetOf(u)).sqrMagnitude;
                if (sq <= r * r && sq < bestSq) { best = u; bestSq = sq; }
            }
            return best;
        }

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
            if (s != null) s.Pos = pos;
        }
    }
}
