using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Arena;
using Guildmaster.Game.Flow;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Руки игрока на арене в фазе расстановки: круги-опоры под ногами, выбор бойца под курсором,
    /// перетаскивание фигурки и приём реликвии из инвентаря.
    /// </summary>
    /// <remarks>
    /// <b>Существует отдельно от <see cref="DeploymentController"/>, потому что ролей две, а не одна.</b>
    /// Контроллер владеет МЕСТОМ и СОСТАВОМ — кто вышел на арену, когда начинается бой, — и это право
    /// хозяина сеанса. Руки есть у каждого участника, включая гостя, у которого ни симуляции, ни
    /// пресета боя нет вовсе. Пока обе роли жили в одном классе, гость не получал ни кругов, ни драга,
    /// ни приёма реликвии — и это читалось как три отдельных бага вместо одной несделанной работы.
    ///
    /// <para><b>Ничего не применяет сам</b> — публикует намерения (<see cref="UnitMoveIntent"/>,
    /// <see cref="OpenLoadoutIntent"/>, <see cref="EquipRelicRequest"/>). Исполняет их владелец арены,
    /// один на сеанс. Отсюда следует и главное: у хозяина боец встаёт на место в тот же кадр, у гостя
    /// — кадром ленты, в котором хост его уже переставил, и второго пути к тому же результату не
    /// существует.</para>
    ///
    /// <para><b>Активность выводится из фазы боя</b>, а не из собственного флага «мы в расстановке».
    /// Фаза — единственный факт, одинаково известный обоим: у хозяина её ставит контроллер, гостю она
    /// приезжает в <c>ActivityState</c>. Свой флаг у рук означал бы второго владельца одного факта, и
    /// разошлись бы они ровно там, где это труднее всего заметить, — у гостя.</para>
    /// </remarks>
    public sealed class DeploymentInteraction : IStartable, ITickable, IDisposable
    {
        private const float DoubleClickWindow  = 0.30f;
        private const float DragMinDelta       = 0.05f; // мир-единицы: меньше = «клик», больше = «drag»
        private const float PickRadiusScale    = 1.3f;  // круг-опора × это = «ближняя» зона хватания (у ног)
        private const float FigurePickPadding  = 0.08f; // мировой запас вокруг фигуры: чуть-чуть, не «гигантский»

        private readonly IArenaUnits        _arena;
        private readonly IInputService      _input;
        private readonly IPointerWorld      _pointer;
        private readonly CombatPresenter    _presenter;
        private readonly ArenaLayoutData    _layout;
        private readonly DeploymentService  _deploy;
        private readonly IBattleSession     _session;
        private readonly BattleUnitRegistry _directory;
        private readonly Core.Simulation.SimTuning _tuning;
        private readonly Core.Players.ILocalPlayer _localPlayer;
        // Свой номер в сеансе — им подписывается намерение. Вне сеанса (соло) участника нет, и подпись
        // вырождается в единственного игрока.
        private readonly Core.Players.ISessionRoster _roster;
        private readonly ActivitySetup      _activity;
        private readonly Core.Audio.IAudioService  _audio;
        // Чужие курсоры и цвет их владельцев: наведение публично, и видно его прямо на бойце.
        private readonly Core.Players.IPresenceView _presence;
        private readonly GuildmasterPalette         _palette;

        private readonly ISubscriber<RelicDragEvent>     _relicDragSub;
        private readonly IPublisher<UnitMoveIntent>      _movePub;
        private readonly IPublisher<OpenLoadoutIntent>   _loadoutPub;
        private readonly IPublisher<EquipRelicRequest>   _equipPub;

        private DeploymentView _view;
        private IDisposable    _relicDragSubscription;

        // Кого тащим. Держим ИД, а не сам снимок: за время протяжки состав арены может пересобраться
        // (напарник надел кит — превью встало заново), и ссылка на исчезнувшего бойца пережила бы его.
        private int     _draggedId = -1;
        private Vector2 _dragStartWorld;
        // Схваченная точка фигурки: позиция бойца минус курсор в момент захвата. Фигурка НЕ прыгает
        // центром под курсор — держим её за то место, за которое взяли.
        private Vector2 _grabOffset;
        // Ноги минус позиция на момент захвата: круг-опору рисуем у ног ПРИЗРАКА, а не под курсором.
        private Vector2 _feetOffset;
        private bool    _dragMoved;

        private float _lastClickTime;
        private int   _lastClickUnitId = -1;

        private RelicData _relicDrag; // тащим реликвию из инвентаря (null = нет)

        private readonly List<(Vector2 center, float radius, DeploymentView.RingState state, Color? tint)> _ringBuffer = new();

        public DeploymentInteraction(
            IArenaUnits arena,
            IInputService input,
            IPointerWorld pointer,
            CombatPresenter presenter,
            ArenaLayoutData layout,
            DeploymentService deploy,
            IBattleSession session,
            BattleUnitRegistry directory,
            Core.Simulation.SimTuning tuning,
            Core.Players.ILocalPlayer localPlayer,
            Core.Players.ISessionRoster roster,
            ActivitySetup activity,
            Core.Audio.IAudioService audio,
            Core.Players.IPresenceView presence,
            GuildmasterPalette palette,
            ISubscriber<RelicDragEvent> relicDragSub,
            IPublisher<UnitMoveIntent> movePub,
            IPublisher<OpenLoadoutIntent> loadoutPub,
            IPublisher<EquipRelicRequest> equipPub)
        {
            _arena       = arena;
            _input       = input;
            _pointer     = pointer;
            _presenter   = presenter;
            _layout      = layout;
            _deploy      = deploy;
            _session     = session;
            _directory   = directory;
            _tuning      = tuning;
            _localPlayer = localPlayer;
            _roster      = roster;
            _activity    = activity;
            _audio       = audio;
            _presence    = presence;
            _palette     = palette;
            _relicDragSub = relicDragSub;
            _movePub      = movePub;
            _loadoutPub   = loadoutPub;
            _equipPub     = equipPub;
        }

        public void Start()
        {
            _input.PointerPressed  += OnPointerPressed;
            _input.PointerReleased += OnPointerReleased;
            _relicDragSubscription  = _relicDragSub?.Subscribe(OnRelicDrag);
        }

        public void Dispose()
        {
            _input.PointerPressed  -= OnPointerPressed;
            _input.PointerReleased -= OnPointerReleased;
            _relicDragSubscription?.Dispose();
            if (_view != null) UnityEngine.Object.Destroy(_view.gameObject);
        }

        /// <summary>
        /// Идёт ли расстановка. Единственный критерий — фаза боя: см. замечание про владельца факта
        /// в докстринге класса.
        /// </summary>
        private bool Deploying => _session.Phase == BattlePhase.Deployment;

        /// <summary>Распоряжаюсь ли я этой стороной: можно ли брать её бойцов и двигать.</summary>
        /// <remarks>
        /// Правило живёт в мероприятии (<see cref="ActivitySetup.MayCommandSide"/>) и здесь только
        /// спрашивается — вторая его копия разошлась бы с той, по которой хозяин исполняет намерения,
        /// и бойцы отскакивали бы назад после каждой протяжки. Сторона своего игрока берётся у состава
        /// сеанса и НЕ считается нулём: в PvP оба клиента считали бы своей одну и ту же сторону.
        /// </remarks>
        private bool CanCommand(int team) => _activity.MayCommandSide(team, _localPlayer.Team);

        /// <summary>
        /// Половина арены, на которой стоит эта команда. Ноль здесь — НЕ «моя команда», а первая
        /// сторона арены: зоны привязаны к геометрии площадки, а не к тому, кто на неё смотрит.
        /// </summary>
        private static DeploymentSide SideOf(int team) =>
            team == 0 ? DeploymentSide.Player : DeploymentSide.Enemy;

        // ── Покадровая интеракция ────────────────────────────────────────────
        public void Tick()
        {
            if (!Deploying)
            {
                if (_view != null && _view.gameObject.activeSelf) _view.SetActive(false);
                _draggedId = -1;
                _relicDrag = null;
                return;
            }

            EnsureView();
            if (!_view.gameObject.activeSelf) _view.SetActive(true);

            // Реликвия-drag из инвентаря: призрак силуэта виден ВЕЗДЕ, пока тащим (в том числе над
            // панелью грида), цель эквипа под курсором подсвечиваем кругом. Юнит-drag и ховер в это
            // время не трогаем — это отдельный жест поверх UI.
            if (_relicDrag != null) { DrawRelicDragGhost(); return; }

            // Меню снаряжения открыто (ввод заглушён) или курсор над непрозрачной панелью вне активного
            // драга — не интеракчим, но круги-опоры оставляем видимыми: читаемость поля не зависит от
            // того, где сейчас курсор.
            if (_input.GameplaySuppressed || (_input.PointerOverUI && _draggedId < 0))
            {
                HideGhostSprite();
                UpdateUnitRings(-1, default, false, false);
                return;
            }

            Vector2 world = _pointer.Position;
            int  hoverId  = -1;
            bool dragValid = false;
            Vector2 dragTarget = default;

            if (TryGetDragged(out ArenaUnit dragged))
            {
                if ((world - _dragStartWorld).sqrMagnitude > DragMinDelta * DragMinDelta) _dragMoved = true;
                dragTarget = DragTarget(world);          // куда встанет боец, если отпустить здесь
                dragValid  = CanDrop(dragged, dragTarget);
                ShowDragGhost(dragged, dragTarget, dragValid);
            }
            else
            {
                // Бойца, которого тащили, на арене больше нет (превью пересобрали) — отпускаем сами.
                if (_draggedId >= 0) CancelDrag();

                hoverId = TryPick(world, out ArenaUnit hover) ? hover.Id : -1;
                HideGhostSprite();
            }

            UpdateUnitRings(hoverId, dragTarget + _feetOffset, dragValid, _draggedId >= 0);
        }

        // Круги-опоры под ногами бойцов, которыми мы распоряжаемся: всегда видны (читаемость),
        // наведённый — ярче. У перетаскиваемого кругов ДВА: на его месте — ярко горящий («тащишь
        // именно меня»), и у ног призрака — по валидности drop. Так видно и кого взял, и куда он встанет.
        private void UpdateUnitRings(int hoverId, Vector2 dragFeet, bool dragValid, bool dragging)
        {
            _ringBuffer.Clear();
            IReadOnlyList<ArenaUnit> units = _arena.Units;
            for (int i = 0; i < units.Count; i++)
            {
                ArenaUnit u = units[i];
                if (!CanCommand(u.Team) || u.IsDead) continue;

                bool isDragged = dragging && u.Id == _draggedId;
                DeploymentView.RingState st = isDragged || u.Id == hoverId
                    ? DeploymentView.RingState.Hover
                    : DeploymentView.RingState.Normal;

                // Чужое наведение публично: пока напарник держит курсор на бойце, у нас этот боец горит
                // ЕГО мейн-цветом. Своё наведение цветом не красим — оно и так ярче остальных.
                Color? tint = TintOfForeignHover(u.Id);
                if (tint != null) st = DeploymentView.RingState.Hover;

                _ringBuffer.Add((FeetOf(u), BodyRadius(u), st, tint)); // у ног визуальных, не в центре фигуры

                if (isDragged) // + целевой круг у ног призрака (следует за курсором)
                    _ringBuffer.Add((dragFeet, BodyRadius(u),
                                     dragValid ? DeploymentView.RingState.DragValid : DeploymentView.RingState.DragInvalid,
                                     null));
            }
            _view.SetUnitRings(_ringBuffer);
        }

        /// <summary>
        /// Мейн-цвет того, кто держит курсор на этом бойце, или <c>null</c> — никто не держит.
        /// </summary>
        /// <remarks>
        /// «Пинг без клика»: половина голосового трафика в коопе — это «да вон тот, лучник… нет, другой
        /// лучник» (принято 30.07.2026, кооп-канон §Правила слоя). Данные ехали по проводу с самого
        /// начала — не хватало только показа.
        /// <para>Чей курсор нам вообще видно, решено ДО нас: пакет режется по сторонам у хозяина.
        /// Второй проверки «а не противник ли он» здесь нет и быть не должно.</para>
        /// </remarks>
        private Color? TintOfForeignHover(int unitId)
        {
            if (_presence == null || _roster == null) return null;

            for (int i = 0; i < _presence.Count; i++)
            {
                Core.Players.RemoteCursor cursor = _presence[i];
                if (cursor.HoveredId != unitId) continue;
                if (!_roster.TryGet(cursor.PlayerId, out Core.Players.SessionPlayer player)) continue;

                return PlayerColor(player.ColorIndex);
            }

            return null;
        }

        /// <summary>
        /// Мейн-цвет игрока из палитры. Токен тот же, что у курсора и кружков голосов в интерфейсе:
        /// палитра — единственный владелец цвета, и мир читает её снимок по имени токена.
        /// </summary>
        private Color? PlayerColor(int colorIndex) =>
            _palette != null && _palette.TryGet($"--gm-color-player-{(colorIndex % 4) + 1}", out Color c)
                ? c
                : (Color?)null;

        // Куда встанет перетаскиваемый боец, если отпустить курсор в точке world. Не «центром под
        // курсор», а со смещением, снятым в момент захвата: взял за левый край — ведёшь за левый край.
        private Vector2 DragTarget(Vector2 world) => world + _grabOffset;

        /// <summary>Можно ли отпустить перетаскиваемого бойца в этой точке.</summary>
        /// <remarks>
        /// Одна точка правды для превью и для самой постановки: разъехавшись, они дают призрака,
        /// который горит зелёным там, откуда на отпускании боец откатится назад.
        /// <para>Сторона зоны берётся по КОМАНДЕ бойца, а не «всегда своя»: на Ристалище противника
        /// двигают в его половину, и жёсткая <c>Player</c> запрещала бы любой его сдвиг.</para>
        /// </remarks>
        private bool CanDrop(in ArenaUnit dragged, Vector2 target) =>
            _deploy.CanPlace(target, SideOf(dragged.Team), CanUseExtended(dragged))
            && !Overlaps(target, dragged);

        // Призрак-силуэт перетаскиваемого бойца в целевой позиции — через единый источник
        // UnitSilhouette (тот же вид «в руке», что и при drag реликвии из инвентаря). Нет вида
        // (headless, спрайт не готов) → без призрака: круг всё равно ведёт цель.
        private void ShowDragGhost(in ArenaUnit dragged, Vector2 target, bool valid)
        {
            UnitSilhouette sil = UnitSilhouette.None;
            if (_presenter != null && _presenter.TryGetView(dragged.Id, out UnitView dv))
                sil = UnitSilhouette.FromView(dv, FeetOf(dragged)); // офсет арта — от ТЕКУЩИХ ног живого вида

            if (sil.Valid) _view.SetGhost(true, target + _feetOffset, sil, valid);
            else HideGhostSprite();
        }

        // Мировая точка ног бойца (визуальный FeetPoint из вида, а не центр фигуры) — круг и захват
        // садятся под ноги спрайта. Фолбэк — позиция на арене (headless, вид не готов).
        private Vector2 FeetOf(in ArenaUnit u)
        {
            if (_presenter != null && _presenter.TryGetView(u.Id, out UnitView view) && view != null)
            {
                Vector3 f = view.FeetPoint;
                return new Vector2(f.x, f.y);
            }
            return u.Position;
        }

        private void HideGhostSprite() => _view.SetGhost(false, default, UnitSilhouette.None, false);

        // ── Drag реликвии из инвентаря на бойца ──────────────────────────────
        private void OnRelicDrag(RelicDragEvent e)
        {
            if (!Deploying) return;
            switch (e.Phase)
            {
                case RelicDragPhase.Start:
                case RelicDragPhase.Move:
                    _relicDrag = e.Relic; // позицию берём из указателя в Tick/Drop — тот же источник, что пик
                    break;
                case RelicDragPhase.Drop:
                    if (e.Relic != null)
                    {
                        if (TryPick(_pointer.Position, out ArenaUnit target))
                        {
                            _equipPub?.Publish(new EquipRelicRequest(target.Id, e.Relic));
                            _audio?.Play("ui.relic_equip.ui");
                        }
                        else
                        {
                            _audio?.Play("ui.drag_reject.ui"); // карточку отпустили мимо бойца
                        }
                    }
                    _relicDrag = null;
                    HideGhostSprite();
                    break;
            }
        }

        // Призрак силуэта реликвии у курсора (единый вид «в руке», из ViewPrefab: бойца на поле ещё
        // нет) + подсветка бойца под курсором. Круги-опоры остаются видимыми.
        private void DrawRelicDragGhost()
        {
            Vector2 world = _pointer.Position;
            bool onUnit = TryPick(world, out ArenaUnit target);

            UnitSilhouette sil = UnitSilhouette.FromPrefab(_relicDrag != null ? _relicDrag.ViewPrefab : null);
            if (sil.Valid) _view.SetGhost(true, world, sil, onUnit);
            else HideGhostSprite();

            UpdateUnitRings(onUnit ? target.Id : -1, default, false, false);
        }

        // ── Указатель ────────────────────────────────────────────────────────
        private void OnPointerPressed()
        {
            if (!Deploying || _input.GameplaySuppressed) return;

            Vector2 world = _pointer.Position;
            if (!TryPick(world, out ArenaUnit unit)) return;

            float now = Time.unscaledTime;
            bool doubleClick = unit.Id == _lastClickUnitId && (now - _lastClickTime) < DoubleClickWindow;
            _lastClickTime   = now;
            _lastClickUnitId = unit.Id;

            if (doubleClick) { _loadoutPub?.Publish(new OpenLoadoutIntent(unit.Id)); return; }

            // Начинаем протяжку (различаем клик и drag по пройденной дистанции на отпускании).
            _draggedId      = unit.Id;
            _dragStartWorld = world;
            _grabOffset     = unit.Position - world;          // держим фигурку за схваченное место
            _feetOffset     = FeetOf(unit) - unit.Position;   // куда относительно центра садится круг-опора
            _dragMoved      = false;
            _view.SetExtendedHighlight(CanUseExtended(unit));
            _audio?.PlayAt("ui.deploy_grab.ui", unit.Position);
        }

        private void OnPointerReleased()
        {
            if (!TryGetDragged(out ArenaUnit dragged)) { CancelDrag(); return; }

            // Ввод заглушили посреди протяжки (консоль, модальный экран) — отпускание считаем ОТМЕНОЙ:
            // ставить бойца по курсору, уведённому в интерфейс, значит удивить игрока. Само событие при
            // этом обязано дойти, иначе боец остался бы «в руке» до постороннего клика.
            if (_dragMoved && !_input.GameplaySuppressed)
            {
                Vector2 target = DragTarget(_pointer.Position); // та же точка, что вела призрака
                if (CanDrop(dragged, target))
                {
                    _movePub?.Publish(new UnitMoveIntent(dragged.Id, target, _roster?.LocalId ?? 0));
                    _audio?.PlayAt("ui.deploy_place.ui", target);
                }
                else
                {
                    // Невалидно → боец остаётся на месте. Молчаливый откат читается как «не нажалось».
                    _audio?.Play("ui.deploy_reject.ui");
                }
            }

            CancelDrag();
        }

        private void CancelDrag()
        {
            _draggedId = -1;
            _dragMoved = false;
            if (_view == null) return;
            HideGhostSprite();
            _view.SetExtendedHighlight(false);
        }

        // ── Хелперы ──────────────────────────────────────────────────────────
        private bool TryGetDragged(out ArenaUnit unit)
        {
            unit = default;
            return _draggedId >= 0 && _arena.TryGet(_draggedId, out unit) && !unit.IsDead;
        }

        /// <summary>
        /// Кого игрок имеет в виду, указывая сюда. Захват двухслойный: круг-опора у ног ИЛИ сама
        /// фигура. Круг главнее — он нарисован и читается как «место бойца», поэтому попадание в чей-то
        /// круг всегда бьёт попадание в чужую фигуру (иначе высокий сосед перехватывал бы клик по ногам
        /// соседа). Внутри слоя выигрывает ближайший по ногам.
        /// </summary>
        private bool TryPick(Vector2 world, out ArenaUnit picked)
        {
            picked = default;
            bool hasRing = false, hasBody = false;
            ArenaUnit bestRing = default, bestBody = default;
            float bestRingSq = float.MaxValue, bestBodySq = float.MaxValue;

            IReadOnlyList<ArenaUnit> units = _arena.Units;
            for (int i = 0; i < units.Count; i++)
            {
                ArenaUnit u = units[i];
                if (!CanCommand(u.Team) || u.IsDead) continue;

                float r  = BodyRadius(u) * PickRadiusScale;
                float sq = (world - FeetOf(u)).sqrMagnitude;
                if (sq <= r * r)
                {
                    if (sq < bestRingSq) { bestRing = u; bestRingSq = sq; hasRing = true; }
                    continue; // в круг попали — по фигуре этого же бойца проверять нечего
                }

                if (FigureHit(u, world) && sq < bestBodySq) { bestBody = u; bestBodySq = sq; hasBody = true; }
            }

            if (hasRing) { picked = bestRing; return true; }
            if (hasBody) { picked = bestBody; return true; }
            return false;
        }

        // Попал ли курсор в фигуру — по ЭТАЛОННОМУ габариту вида, а не по AABB кадра: AABB скелетной
        // анимации шире фигуры (замах, плащ, прозрачные поля), и зона хватания выходила гигантской.
        // Нет вида (headless) → false: работает только круг-опора.
        private bool FigureHit(in ArenaUnit u, Vector2 world) =>
            _presenter != null
            && _presenter.TryGetView(u.Id, out UnitView view)
            && view != null
            && view.FigureContainsWorldPoint(world, FigurePickPadding);

        private bool Overlaps(Vector2 pos, in ArenaUnit exclude)
        {
            float r = BodyRadius(exclude);
            IReadOnlyList<ArenaUnit> units = _arena.Units;
            for (int i = 0; i < units.Count; i++)
            {
                ArenaUnit u = units[i];
                if (u.Id == exclude.Id || u.IsDead) continue;
                float min = r + BodyRadius(u);
                if ((pos - u.Position).sqrMagnitude < min * min) return true;
            }
            return false;
        }

        private float BodyRadius(in ArenaUnit u) =>
            Mathf.Max(0.01f, u.Size) * _tuning.BodyRadiusPerSize;

        /// <summary>
        /// Пускает ли кит этого бойца в расширенную зону. Определение спрашиваем у реестра боя, а не у
        /// боевой сущности: у гостя её нет, а реестр наполняется паспортами и есть у обоих.
        /// </summary>
        private bool CanUseExtended(in ArenaUnit u) =>
            (_directory?.DefinitionOf(u.Id) as RelicData)?.CanUseExtendedDeployment ?? false;

        private void EnsureView()
        {
            if (_view != null) return;
            var go = new GameObject("DeploymentView");
            _view = go.AddComponent<DeploymentView>();
            _view.Init(_layout);
        }
    }
}
