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
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Оркестратор интерактивной фазы расстановки (план шаг 4). На загрузку Free-пресета
    /// (<see cref="EncounterLoader.FreeDeploymentRequested"/>) ставит бой на паузу, флашит спавны и даёт
    /// игроку таскать своих юнитов (team 0) в пределах player-зон; бой начинает кнопка «Начать».
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
        /// <summary>Что подтверждают все игроки, прежде чем бой начнётся.</summary>
        private const string ReadyKeyStart = "battle.start";

        /// <summary>…и прежде чем вернуться с итогов боя обратно в расстановку.</summary>
        private const string ReadyKeyContinue = "battle.continue";

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
        private readonly ISubscriber<RelicDragEvent> _relicDragSub; // QA #5: drag реликвии из инвентаря на юнита
        private readonly ISubscriber<SetTestZoneRequest> _testZoneSub; // радио-табы: целевое состояние тест-зоны (интент)
        private readonly ISubscriber<SetFormationRequest> _formationSub; // кнопка передышки «К построению» (интент)
        private readonly ISubscriber<ProvingGroundsSetupRequest> _groundsSetupSub; // заказ состава площадки (дев-срезы)
        private readonly IPublisher<TestZoneChangedEvent> _testZoneChangedPub; // Ф5: вещаем СОСТОЯНИЕ (единый источник)
        private readonly IPublisher<ArenaRevealRequest>   _arenaRevealPub;    // «яви место боя» — подача за презентером
        private readonly IBattleSession   _session;
        // Камеры здесь НЕТ намеренно: какой вид показать, выводится из фазы боя (её слушает
        // CameraModeController). Прежде расстановка сама ставила свободную камеру и кадрировала арену, а
        // старт боя сам возвращал слежение — два владельца вида, и оба перебивали выбор игрока.
        // Durable-гильдия: только ЧИТАЕМ ростер, и только через шов чтения. Держателя состояния бой не
        // получает: расстановка идёт и на дев-арене, и на Ристалище, где забега нет вовсе.
        private readonly Guildmaster.Guild.IRunStateView _runStates;
        // Позиции и киты уезжают в забег ТОЛЬКО через шину команд: в коопе расстановку правят двое, и
        // «кто передвинул юнита» обязано остаться в логе (ТЗ кооп-вертикали §4.1).
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;
        // С чем открыто мероприятие: вид площадки и заказанный при входе состав. Приходит параметром, а
        // не событием, — см. ActivitySetup.Roster.
        private readonly ActivitySetup _activity;
        // Расклад площадки по умолчанию. Может быть null: без ассета площадка просто встаёт пустой, и это
        // не отказ, а «состав не собран».
        private readonly ProvingGroundsConfig _groundsConfig;
        // Общее согласие: «Начать» — это «я готов», а бой начинается, когда готовы все. В соло участник
        // один, и гейт пропускает в тот же кадр, поэтому ветки «а мы одни?» здесь нет.
        private readonly Core.Net.IReadyGate _ready;
        // Исход боя и выход с площадки: экран «Продолжить / В меню» показывает тот же, кто площадкой и
        // владеет. Отдельного презентера нет намеренно — состав, с которым продолжают, живёт здесь.
        private readonly ISubscriber<BattleEndedEvent> _battleEndedSub;
        private readonly IPublisher<Guildmaster.Guild.OpenOutcomeRequest> _outcomePub;
        private readonly Core.Flow.IRunControl _runControl;
        private readonly Core.Audio.IAudioService _audio;              // взял/поставил/отказ — звук расстановки

        // Редактируемый ростер игрока в этой фазе (позиции/релики меняются перетаскиванием и loadout'ом).
        // GuildIndex — тот же слот в durable-гильдии забега (RunState.Guild): по нему правки уезжают в сейв,
        // иначе расстановка и надетые релики жили бы только до конца боя (наход. Макса, п.5).
        private sealed class Slot
        {
            // Юнит слота — UnitData, а не RelicData: на площадке может стоять и не-реликвия (дев-срез
            // ставит своих бойцов). Сужение до реликвии молча теряло такого юнита на первой же пересборке
            // превью — слот оставался, а ставить было некого.
            public UnitData Unit; public VesselData Vessel; public Vector2 Pos;
            public int LiveUnitId = -1;
            public int GuildIndex = -1;

            /// <summary>
            /// Чья это сторона. На Ристалище слоты есть у ОБЕИХ команд: там противник — такие же киты, и
            /// расставляет их тот же игрок. В забеге сторона всегда своя — врагов приносит энкаунтер.
            /// </summary>
            public int Team;

            /// <summary>Кит слота, если это реликвия: лоадаут и гильдия забега работают только с ними.</summary>
            public RelicData Relic => Unit as RelicData;
        }
        private readonly List<Slot> _slots = new List<Slot>();
        private EncounterData _encounter;

        // Противник полигона: на Ристалище враги — такие же киты, заданные списком, а не энкаунтером.
        // Держим, чтобы перетаскивание своего бойца пересобирало обе стороны, а не сметало вражескую.
        private readonly List<PlayerSpawn> _opponents = new List<PlayerSpawn>();

        /// <summary>
        /// ГДЕ игрок расставляет бойцов. Единственный источник правды о месте: из него выводятся и состав
        /// сторон, и цвет арены, и то, куда вернуть фазу на выходе. Место переживает бой — «Начать» меняет
        /// не место, а фазу.
        /// </summary>
        private enum Venue
        {
            /// <summary>Нигде: расстановки не было и мир нам не принадлежит.</summary>
            None,
            /// <summary>Узел боя в забеге: отряд гильдии против врагов энкаунтера, арена боевая.</summary>
            BattleNode,
            /// <summary>Построение между узлами: свой отряд без врагов, арена боевая (мы всё ещё в забеге).</summary>
            Formation,
            /// <summary>Ристалище: серая арена вне забега, обе стороны — киты (ГДД [[proving-grounds]]).</summary>
            ProvingGrounds,
        }

        private Venue _venue = Venue.None;

        private DeploymentView _view;
        private Camera _camera;
        private IDisposable _equipSubscription;
        private IDisposable _relicDragSubscription;
        private IDisposable _testZoneSubscription;
        private IDisposable _formationSubscription;
        private IDisposable _groundsSetupSubscription;

        // Заказанный состав площадки (дев-срезы). Живёт до выхода с площадки: пока он есть, Ристалище
        // встаёт им, а не раскладом из ассета.
        private readonly List<PlayerSpawn> _groundsSquadOrder     = new List<PlayerSpawn>();
        private readonly List<PlayerSpawn> _groundsOpponentOrder  = new List<PlayerSpawn>();
        private bool _hasGroundsOrder;

        private bool _deploying;
        private bool _foldingUp; // сворачиваемся по внешнему сбросу фазы — защита от захода на второй круг
        private BattlePhase _returnPhase = BattlePhase.None; // фаза, в которой застали место — туда и вернём

        // Расклад площадки: с чем игрок ушёл в последний бой Ристалища. Держит расстановку внутри захода —
        // после боя бойцы встают туда же, куда их поставил игрок, но целыми.
        private readonly List<PlayerSpawn> _provingSquad = new List<PlayerSpawn>();
        // Противник этого захода — рядом со своими и по той же причине: после боя площадка обязана
        // встать тем же составом, иначе «Продолжить» подсунуло бы игроку чужой бой.
        private readonly List<PlayerSpawn> _provingOpponents = new List<PlayerSpawn>();
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
            ISubscriber<RelicDragEvent> relicDragSub,
            ISubscriber<SetTestZoneRequest> testZoneSub,
            ISubscriber<SetFormationRequest> formationSub,
            ISubscriber<ProvingGroundsSetupRequest> groundsSetupSub,
            IPublisher<TestZoneChangedEvent> testZoneChangedPub,
            IPublisher<ArenaRevealRequest> arenaRevealPub,
            IBattleSession session,
            Guildmaster.Guild.IRunStateView runStates,
            Guildmaster.Guild.Commands.IRunCommands commands,
            Core.Audio.IAudioService audio,
            ActivitySetup activity,
            ProvingGroundsConfig groundsConfig,
            Core.Net.IReadyGate ready,
            ISubscriber<BattleEndedEvent> battleEndedSub,
            IPublisher<Guildmaster.Guild.OpenOutcomeRequest> outcomePub,
            Core.Flow.IRunControl runControl)
        {
            _battleEndedSub = battleEndedSub;
            _outcomePub     = outcomePub;
            _runControl     = runControl;
            _activity = activity;
            _groundsConfig = groundsConfig;
            _ready         = ready;
            _arenaRevealPub = arenaRevealPub;
            _audio         = audio;
            _runStates     = runStates;
            _commands      = commands;
            _loader        = loader;
            _sim           = sim;
            _deploy        = deploy;
            _input         = input;
            _presenter     = presenter;
            _layout        = layout;
            _openLoadoutPub = openLoadoutPub;
            _equipSub      = equipSub;
            _relicDragSub  = relicDragSub;
            _testZoneSub   = testZoneSub;
            _formationSub  = formationSub;
            _groundsSetupSub = groundsSetupSub;
            _testZoneChangedPub = testZoneChangedPub;
            _session       = session;
        }

        public void Start()
        {
            _loader.FreeDeploymentRequested += OnFreeDeployment;
            _input.PointerPressed  += OnPointerPressed;
            _input.PointerReleased += OnPointerReleased;
            _equipSubscription = _equipSub.Subscribe(OnEquip);
            _relicDragSubscription = _relicDragSub?.Subscribe(OnRelicDrag);
            _testZoneSubscription = _testZoneSub?.Subscribe(OnSetTestZone);
            _formationSubscription = _formationSub?.Subscribe(OnSetFormation);
            _groundsSetupSubscription = _groundsSetupSub?.Subscribe(OnProvingGroundsSetup);

            // Состав, заказанный ПРИ ВХОДЕ на площадку, применяем тем же обработчиком, что и заказ на
            // живой площадке: дорога одна, разница только в моменте. Через событие такой заказ не
            // доходил вовсе — его публиковали раньше, чем нас создали.
            if (_activity.Roster.HasValue) OnProvingGroundsSetup(_activity.Roster.Value);

            // Площадка ВСТАЁТ САМА, по виду мероприятия, а не по интенту снаружи. Интент до нас не
            // доходил по той же причине, что и заказ состава: его публикуют в момент открытия площадки,
            // а нас к этому моменту ещё не создали (VContainer диспатчит IStartable отдельной фазой).
            // Выглядело это так, будто арены нет вовсе, пока игрок не ткнёт таб «Бой» — тот публикует
            // тот же интент повторно, и уже он доходил (наход. Макса 02.08.2026).
            // Состав применяем ДО входа: иначе площадка встанет раскладом из ассета, а заказ увидит уже
            // собранную арену.
            if (_activity.Kind == ActivityKind.ProvingGrounds)
                OnSetTestZone(new SetTestZoneRequest(true));

            // Верхняя панель забега (план 12): часы боя + кнопка «Начать».
            // Persist-мир: скоуп живёт всю сессию, поэтому фазу НЕ выставляем на Start (иначе вне боя
            // Phase залипал бы на Fighting и ломал guard'ы топбара — вылет при клике «Бой» на ивенте).
            // Фаза выставляется по факту: Deployment на входе в бой (OnFreeDeployment), Fighting на «Начать»,
            // None — вне боя (сброс через BattleBootstrap.ResetToWorld).
            _session.BindClock(() => _sim.ElapsedSeconds);
            // «Начать» стартует бой из ЛЮБОЙ расстановки, где есть с кем драться (требование 2026-07-27:
            // на Ристалище бой начинается той же кнопкой, что в узле забега — второй кнопки старта у нас
            // быть не должно). Критерий — не «где мы», а наличие противника на арене: построение между
            // узлами врагов не имеет, и там кнопка по-прежнему ни во что не ведёт.
            _session.BindStart(TryStartFromButton);

            // Что именно подтверждают. Ключ живёт здесь, а не в гейте: гейт не знает, чего ждут, — он
            // знает только, что ждут все.
            _ready?.Bind(ReadyKeyStart, StartCombat);

            // Мир могут сбросить и мимо нас: «В меню» из системного меню рвёт забег через IRunControl, и
            // до нас доходит только смена фазы. Без этой подписки расстановка (и тест-зона вместе с ней)
            // оставалась взведённой — в главном меню и в следующей сессии полигон продолжал висеть.
            _session.PhaseChanged += OnPhaseChanged;
        }

        /// <summary>
        /// Кнопка «Начать» — ЕДИНСТВЕННЫЙ способ пустить бой. Отказ громкий: молчаливое
        /// нажатие читается игроком как «не нажалось», а причина у отказа ровно одна и внятная.
        /// </summary>
        private void TryStartFromButton()
        {
            if (!_deploying)
            {
                Guildmaster.Diagnostics.UiTrace.Log("ctrl «Начать»: расстановки нет — стартовать нечего");
                return;
            }
            if (!HasOpponents())
            {
                Guildmaster.Diagnostics.UiTrace.Log("ctrl «Начать»: на арене нет противника (построение между узлами) — бой не начинается");
                return;
            }

            // Не «начать бой», а «я готов». В соло разницы нет — гейт пропускает сразу; вдвоём бой ждёт
            // второго, и кнопка сама говорит, скольких ждёт.
            _ready?.ToggleLocal();
            if (_ready == null) StartCombat();
        }

        /// <summary>
        /// Распоряжаюсь ли я этой стороной: можно ли брать её бойцов и двигать.
        /// </summary>
        /// <remarks>
        /// <b>Ристалище и PvP отличаются РОВНО этим</b>, и разница описана флагом мероприятия
        /// (<c>ActivitySetup.OwnUnitsOnly</c>), а не двумя ветками кода — решение Макса 02.08.2026, по
        /// которому PvP не заводится отдельным видом. До 03.08.2026 флаг не читал никто: расстановка
        /// жёстко знала «команда 0» в семи местах, и на площадке нельзя было тронуть противника, хотя
        /// он там — такой же кит игрока.
        /// <para>Сторона арены выводится отсюда же: своя половина у команды 0, вражеская у всех
        /// остальных. Без этого перетаскивание противника проверялось бы по чужой зоне и запрещалось
        /// всегда.</para>
        /// </remarks>
        private bool CanCommand(int team) => !_activity.OwnUnitsOnly || team == 0;

        private static DeploymentSide SideOf(int team) =>
            team == 0 ? DeploymentSide.Player : DeploymentSide.Enemy;

        // Есть ли на арене живой противник. Дешевле и честнее флага «это боевой узел»: полигон построения
        // и площадка отличаются друг от друга ровно этим, а не тем, кто их открыл.
        private bool HasOpponents()
        {
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Team != 0 && !units[i].IsDead) return true;
            return false;
        }

        /// <summary>
        /// Фаза сменилась мимо нас. Два случая, и оба про место, которым владеем мы:
        /// <list type="bullet">
        /// <item><b>None</b> — мир сброшен снаружи («В меню» рвёт забег через <c>IRunControl</c>): сворачиваем место,
        /// иначе оно осталось бы взведённым и в главном меню, и в следующей сессии.</item>
        /// <item><b>Interlude на Ристалище</b> — бой на площадке кончился. Возвращаем расстановку тем же входом,
        /// каким на площадку пришли: у площадки нет узла забега, который вернул бы мир за нас.</item>
        /// </list>
        /// </summary>
        private void OnPhaseChanged()
        {
            if (_foldingUp) return;

            if (_session.Phase == BattlePhase.None)
            {
                _foldingUp = true;             // LeaveVenue сам трогает фазу — не даём себя же перезвать
                try
                {
                    if (_deploying) LeaveVenue();
                    SetVenue(Venue.None);      // мир ничей — место кончается вместе с фазой (в том числе после узла боя)
                }
                finally { _foldingUp = false; }
                return;
            }

            // Бой на площадке кончился. Прежде расстановка возвращалась молча и сразу — игрок оказывался
            // над тем же строем, не увидев даже, чем кончилось. Теперь возврат — это ВЫБОР: продолжить тем
            // же составом или уйти в меню (наход. Макса 03.08.2026).
            if (_session.Phase == BattlePhase.Interlude && !_deploying && _venue == Venue.ProvingGrounds)
                ShowGroundsOutcome();
        }

        /// <summary>
        /// Предложить итог боя на площадке: продолжить тем же составом или вернуться в главное меню.
        /// </summary>
        /// <remarks>
        /// <b>«Продолжить» тоже проходит через общее согласие</b>: вдвоём переигрывать бой по нажатию
        /// одного значило бы, что второй ещё смотрит на поле, а его уже вернули в расстановку.
        /// <para>Победу определяем по тому, кто остался: у площадки нет ни забега, ни петли акта, которые
        /// в бою за нас считают исход, — есть только арена.</para>
        /// </remarks>
        private void ShowGroundsOutcome()
        {
            bool victory = !HasOpponents();

            _ready?.Bind(ReadyKeyContinue, ReturnToDeployment);

            if (_outcomePub == null) { ReturnToDeployment(); return; } // некому показать экран — не запираем игрока

            _outcomePub.Publish(new Guildmaster.Guild.OpenOutcomeRequest(
                victory,
                onToMenu:   () => _runControl?.RequestReturnToMainMenu(),
                onContinue: () => { if (_ready != null) _ready.ToggleLocal(); else ReturnToDeployment(); }));
        }

        private void ReturnToDeployment()
        {
            if (_venue != Venue.ProvingGrounds || _deploying) return;

            _foldingUp = true;             // EnterVenue ставит Deployment — тот же приём против рекурсии
            try { EnterVenue(Venue.ProvingGrounds); }
            finally { _foldingUp = false; }

            _ready?.Bind(ReadyKeyStart, StartCombat); // снова ждём согласия НА БОЙ, а не на возврат
        }

        public void Dispose()
        {
            _session.PhaseChanged -= OnPhaseChanged;
            _loader.FreeDeploymentRequested -= OnFreeDeployment;
            _input.PointerPressed  -= OnPointerPressed;
            _input.PointerReleased -= OnPointerReleased;
            _equipSubscription?.Dispose();
            _relicDragSubscription?.Dispose();
            _testZoneSubscription?.Dispose();
            _formationSubscription?.Dispose();
            _groundsSetupSubscription?.Dispose();
            _session.UnbindStart();
            // Снимаем ОБА своих ключа: гейт живёт в скоупе сессии и переживает и бой, и занятие, а
            // Unbind чужого ключа он игнорирует. Уйти с активным battle.continue значило бы оставить
            // на гейте делегат уничтоженного контроллера — и «готов» от напарника позвал бы его.
            _ready?.Unbind(ReadyKeyStart);
            _ready?.Unbind(ReadyKeyContinue);
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
                    _slots.Add(new Slot { Unit = s.Relic, Vessel = s.Vessel, Pos = s.Position, GuildIndex = i });
                }
            RemapLiveUnits();

            EnsureView();
            _view.SetActive(true);
            _deploying = true;
            SetVenue(Venue.BattleNode); // узел боя — не площадка: серая арена гаснет здесь же, по ребру места
            _session.SetPhase(BattlePhase.Deployment); // центр панели = «Начать»; фаза → навигатор ставит контекст Deployment (K8)

            // Вход в узел — момент, когда место боя должно ЯВИТЬСЯ. Что показать говорим здесь, как именно
            // (сколько актов, дожидаться ли шторки) решает презентер арены: подача не дело боевого потока.
            // Облик пока один на все узлы; когда у узлов появятся свои — сюда придёт id из пресета.
            _arenaRevealPub?.Publish(new ArenaRevealRequest(null));
        }

        // ── Расстановка без узла боя: Ристалище и построение ─────────────────
        // Оба входа ведут в ОДИН механизм и различаются ровно двумя вещами — откуда берётся состав и какого
        // цвета арена. Обе выводятся из места (<see cref="Venue"/>), поэтому отдельных флагов «мы в тест-зоне»,
        // «мы на площадке» больше нет: место одно, и оно единственный источник правды.
        //
        // Радио-режимы: топбар просит целевое СОСТОЯНИЕ (Active=бой, !Active=не-бой). Идемпотентно — повтор
        // того же = no-op (табы переключают режим, не тоглят).
        private void OnSetTestZone(SetTestZoneRequest req)
        {
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.OnSetTestZone(Active={req.Active}) (deploying={_deploying}, venue={_venue}, phase={_session.Phase})");
            if (req.Active)
            {
                // Куда именно ведёт «Бой» вне узла — решает наличие забега, а не отдельный интент: идти
                // в забеге на серую площадку с чужим составом было бы враньём, а вне забега своего
                // отряда попросту нет.
                EnterVenue(_runStates?.Current == null ? Venue.ProvingGrounds : Venue.Formation);
            }
            else
            {
                LeaveVenue(); // выйти можно только из расстановки без узла — узел боя себя не сворачивает
            }
        }

        /// <summary>
        /// Заказ состава площадки (дев-срезы). Состав арены остаётся за расстановкой: команда не ставит
        /// бойцов сама, а говорит, кого поставить, — иначе у арены два хозяина, и второй проигрывает всегда
        /// (слоты пересобирают превью и возвращают своих, а сброс боя снимает паузу расстановки).
        /// <para>
        /// Если площадка уже открыта — пересобираем её тут же: команду вводят и стоя на Ристалище, и ждать
        /// повторного входа значило бы «команда сработала, но ничего не изменилось».
        /// </para>
        /// </summary>
        private void OnProvingGroundsSetup(ProvingGroundsSetupRequest req)
        {
            _groundsSquadOrder.Clear();
            _groundsOpponentOrder.Clear();
            _hasGroundsOrder = req.HasContent;

            if (req.Squad != null)
                for (int i = 0; i < req.Squad.Count; i++)
                    if (req.Squad[i].Unit != null)
                        _groundsSquadOrder.Add(new PlayerSpawn(req.Squad[i].Unit, null, req.Squad[i].Position));

            if (req.Opponents != null)
                for (int i = 0; i < req.Opponents.Count; i++)
                    if (req.Opponents[i].Unit != null)
                        _groundsOpponentOrder.Add(new PlayerSpawn(req.Opponents[i].Unit, null, req.Opponents[i].Position));

            // Заказ отменяет расклад ПРОШЛОГО захода: иначе площадка встала бы теми, с кем игрок дрался в
            // прошлый раз, и заказ читался бы как проигнорированный.
            _provingSquad.Clear();
            _provingOpponents.Clear();

            Guildmaster.Diagnostics.UiTrace.Log($"ctrl: заказан состав площадки от «{req.Source}» " +
                $"(свои {_groundsSquadOrder.Count}, противник {_groundsOpponentOrder.Count})");

            if (_venue != Venue.ProvingGrounds) return;

            _slots.Clear();
            StageProvingGrounds();
        }

        // ── «К построению» (передышка между узлами) ──────────────────────────
        // Тот же механизм, что таб «Бой» в забеге, — другой интент к тому же месту (кнопка передышки).
        private void OnSetFormation(SetFormationRequest req)
        {
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.OnSetFormation(Active={req.Active}) (deploying={_deploying}, venue={_venue}, phase={_session.Phase})");
            if (req.Active) EnterVenue(Venue.Formation);
            else if (_venue == Venue.Formation) LeaveVenue();
        }

        // Вставать в расстановку без боя можно только когда у мира на экране нет чужого хозяина: вне забега
        // (None) или в передышке между узлами (Interlude). Во время боя/боевой расстановки — нельзя.
        private bool CanEnterVenue() =>
            _session.Phase == BattlePhase.None || _session.Phase == BattlePhase.Interlude;

        /// <summary>
        /// Войти в расстановку без узла боя. Состав ставит вариация места: <see cref="Venue.Formation"/>
        /// поднимает уже стоящий отряд, <see cref="Venue.ProvingGrounds"/> — обе стороны из ассета.
        /// </summary>
        private void EnterVenue(Venue venue)
        {
            if (_deploying) { Guildmaster.Diagnostics.UiTrace.Log("ctrl: уже в расстановке — no-op"); return; }
            if (!CanEnterVenue()) { Guildmaster.Diagnostics.UiTrace.Log($"ctrl: фаза {_session.Phase} — вход в «{venue}» запрещён"); return; }

            _slots.Clear();
            bool staged = venue == Venue.ProvingGrounds ? StageProvingGrounds() : StageStandingParty();
            if (!staged) return;

            _encounter = null;     // без узла боя врагов задаёт место, а не энкаунтер
            _sim.SetPaused(true);
            EnsureView();
            _view.SetActive(true);
            _deploying = true;
            SetVenue(venue);
            _session.SetPhase(BattlePhase.Deployment); // фаза → навигатор ставит контекст Deployment (K8)
        }

        /// <summary>
        /// Сменить МЕСТО. Единственная точка, где вещается серая арена: серой её делает ровно Ристалище,
        /// и знать об этом больше некому. Смена места запоминает и фазу, в которую место надо вернуть, —
        /// поэтому бой на площадке (место не меняет) не сбивает возврат.
        /// </summary>
        private void SetVenue(Venue venue)
        {
            if (_venue == venue) return;

            bool wasGray = _venue == Venue.ProvingGrounds;
            bool isGray  = venue == Venue.ProvingGrounds;
            if (_venue == Venue.None) _returnPhase = _session.Phase; // куда вернуть панель, когда место закончится
            if (wasGray)
            {
                _provingSquad.Clear(); // расклад принадлежит одному заходу на площадку, не следующему
                _provingOpponents.Clear();
                // Заказанный состав — тоже принадлежность захода: следующий вход обычной кнопкой обязан
                // встречать штатный расклад, а не дев-срез, который кто-то заказал полчаса назад.
                _hasGroundsOrder = false;
                _groundsSquadOrder.Clear();
                _groundsOpponentOrder.Clear();
            }
            _venue = venue;

            // Гашение серой зоны читается верхним циклом игры как «игрок ушёл с площадки» (GameFlow), поэтому
            // публикуем ТОЛЬКО по ребру и только из смены места. Бой на площадке места не меняет — и она не гаснет.
            if (wasGray != isGray) _testZoneChangedPub?.Publish(new TestZoneChangedEvent(isGray));
        }

        /// <summary>Свой отряд, уже стоящий на арене (построение между узлами). false — стоять некому.</summary>
        private bool StageStandingParty()
        {
            _opponents.Clear(); // построение врагов не знает
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.Team != 0 || u.IsDead) continue;
                // Отряд спавнится в порядке гильдии, поэтому порядковый номер живого team-0 = индекс сосуда:
                // правки построения уезжают в тот же сейв, что и правки в боевой расстановке.
                _slots.Add(new Slot { Unit = u.Unit, Pos = u.Position, LiveUnitId = u.Id, GuildIndex = _slots.Count });
            }

            if (_slots.Count == 0)
            {
                Debug.LogWarning("[DeploymentController] - построение: отряд на арене не стоит → пропуск");
                return false;
            }
            Guildmaster.Diagnostics.UiTrace.Log($"ctrl: построение из стоящих team-0 ({_slots.Count})");
            return true;
        }

        /// <summary>
        /// Обе стороны Ристалища заново: свои — последним раскладом захода (а при первом входе из ассета),
        /// противник — из ассета. Ставим ВСЕГДА заново, а не поднимаем стоящих: площадка обязана встречать
        /// бойцов целыми, иначе второй бой подряд играется не тем составом, что первый, и мерить на ней нечего.
        /// </summary>
        /// <remarks>
        /// Спавн идёт штатным путём (<see cref="EncounterLoader.LoadSides"/>), тем же, которым пересобирается
        /// превью при перетаскивании: иначе у площадки появился бы второй способ ставить юнитов, и виды с
        /// сейвом разошлись бы. GuildIndex = −1: этот отряд ничей, в гильдию забега его правки уезжать не должны.
        /// </remarks>
        private bool StageProvingGrounds()
        {
            // Свои: заказанный состав (дев-срез) важнее всего — им площадку и просили поставить. Иначе
            // расклад ЭТОГО захода, если игрок уже переставлял бойцов и дрался. Иначе площадка ПУСТА.
            // Между заходами расклад не сохраняется — ГДД [[proving-grounds]], «Отложено».
            var side = new List<PlayerSpawn>();
            _opponents.Clear();

            if (_hasGroundsOrder)
            {
                side.AddRange(_groundsSquadOrder);
                _opponents.AddRange(_groundsOpponentOrder);
            }
            else if (_provingSquad.Count > 0)
            {
                side.AddRange(_provingSquad);
                _opponents.AddRange(_provingOpponents);
            }
            else
            {
                // Просто зашли, ничего не заказав, — площадка встаёт раскладом из ассета (сегодня это
                // базовые киты 4×4). Ассет стоит ПОСЛЕДНИМ, и это принципиально: 02.08.2026 его убрали
                // не за сам расклад, а за то, что он срабатывал ПЕРВЫМ и перебивал заказанный состав —
                // игрок получал бой, которого не просил. Порядок «заказ → расклад этого захода → ассет»
                // сохраняет оба требования: дев-срез по-прежнему главнее, а голый вход больше не пуст
                // (наход. Макса 03.08.2026).
                StageFromConfig(side, _opponents);
            }

            for (int i = 0; i < side.Count; i++)
                _slots.Add(new Slot { Unit = side[i].Unit, Vessel = side[i].Vessel, Pos = side[i].Position,
                                      LiveUnitId = -1, GuildIndex = -1, Team = 0 });

            // Противник — такие же киты, поэтому обе стороны задаются списком, а не энкаунтером. И слоты
            // ему заводятся такие же: на площадке игрок расставляет обе стороны, а в PvP команду 1 держит
            // второй игрок — но держит она ровно тот же слот.
            for (int i = 0; i < _opponents.Count; i++)
                _slots.Add(new Slot { Unit = _opponents[i].Unit, Vessel = _opponents[i].Vessel, Pos = _opponents[i].Position,
                                      LiveUnitId = -1, GuildIndex = -1, Team = 1 });

            _loader.LoadSides(side, _opponents);
            _sim.FlushSpawns();

            // Слоты знают о живых юнитах по Id — раздаём их после материализации, иначе перетаскивание
            // на площадке не найдёт, кого двигать. Раздаём по КОМАНДАМ: спавн идёт в порядке ростера
            // внутри стороны, но стороны в общем списке чередоваться не обязаны.
            BindLiveUnits();

            Guildmaster.Diagnostics.UiTrace.Log($"ctrl: Ристалище — поставлены обе стороны (своих {side.Count}, противник {_opponents.Count})");
            return true;
        }

        /// <summary>
        /// Расклад площадки по умолчанию — из ассета. Пустой ассет оставляет площадку пустой: это
        /// честный ответ «состав не собран», а не отказ.
        /// </summary>
        private void StageFromConfig(List<PlayerSpawn> squad, List<PlayerSpawn> opponents)
        {
            if (_groundsConfig == null) return;

            for (int i = 0; i < _groundsConfig.SquadCount; i++)
            {
                RelicData relic = _groundsConfig.SquadAt(i);
                if (relic != null) squad.Add(new PlayerSpawn(relic, null, _groundsConfig.SquadPositionAt(i)));
            }
            for (int i = 0; i < _groundsConfig.OpponentCount; i++)
            {
                RelicData relic = _groundsConfig.OpponentAt(i);
                if (relic != null) opponents.Add(new PlayerSpawn(relic, null, _groundsConfig.OpponentPositionAt(i)));
            }
        }

        /// <summary>
        /// Раздать слотам id живых юнитов — по каждой команде отдельно, в порядке спавна.
        /// </summary>
        /// <remarks>
        /// Раньше сопоставление шло одним счётчиком по команде 0 и молча ломалось бы, как только у
        /// противника появились свои слоты: слот второй стороны так и остался бы без живого юнита, а
        /// перетаскивание не нашло бы, кого двигать.
        /// </remarks>
        private void BindLiveUnits()
        {
            IReadOnlyList<RuntimeUnit> spawned = _sim.Units;
            for (int team = 0; team <= 1; team++)
            {
                int slotIndex = -1;
                for (int i = 0; i < spawned.Count; i++)
                {
                    if (spawned[i].Team != team) continue;

                    slotIndex = NextSlotOfTeam(team, slotIndex);
                    if (slotIndex < 0) break;
                    _slots[slotIndex].LiveUnitId = spawned[i].Id;
                }
            }
        }

        private int NextSlotOfTeam(int team, int after)
        {
            for (int i = after + 1; i < _slots.Count; i++)
                if (_slots[i].Team == team) return i;
            return -1;
        }

        /// <summary>
        /// Покинуть место (таб «Карта», кнопка передышки, сброс мира снаружи). Узел боя не сворачивает
        /// себя сам — у него свой владелец, петля акта.
        /// </summary>
        private void LeaveVenue()
        {
            if (_venue != Venue.Formation && _venue != Venue.ProvingGrounds)
            {
                Guildmaster.Diagnostics.UiTrace.Log($"ctrl.LeaveVenue: место «{_venue}» не наше — выходить нечего (no-op)");
                return;
            }

            Guildmaster.Diagnostics.UiTrace.Log($"ctrl.LeaveVenue({_venue}) → фаза {_returnPhase}");
            FlushRoster(); // что переставили — то и останется в гильдии
            _deploying = false;
            _dragged   = null;
            _relicDrag = null;
            _view?.SetActive(false);
            // Возвращаем ТУ фазу, в которой место застали: вне забега — None (панель без «Начать»),
            // в передышке — Interlude (мир на экране, задник UI по-прежнему запрещён).
            BattlePhase back = _returnPhase;
            SetVenue(Venue.None); // цветная арена + снятие Sheet — по ребру внутри
            _session.SetPhase(back);
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

            // Клавиши старта здесь НЕТ (реш. Макса 2026-07-27): бой начинает только кнопка «Начать».
            // Прежний Enter читался с клавиатуры напрямую, мимо IInputService, и потому не подчинялся
            // ни контексту ввода, ни чужому захвату клавиатуры: Enter, которым отправляли команду в
            // dev-консоли, приходил сюда и начинал бой. Хоткей вернём — но через карту действий.

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
                dragValid = CanDrop(dragTarget);
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
                if (!CanCommand(u.Team) || u.IsDead) continue;

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
            if (sil.Valid) _view.SetGhost(true, target + _feetOffset, sil, valid);
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

        private void HideGhostSprite() => _view.SetGhost(false, default, UnitSilhouette.None, false);

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
                    if (target != null && e.Relic != null)
                    {
                        EquipOn(target.Id, e.Relic);
                        _audio?.Play("ui.relic_equip.ui");
                    }
                    else if (e.Relic != null)
                    {
                        _audio?.Play("ui.drag_reject.ui"); // карточку отпустили мимо юнита
                    }
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
            if (sil.Valid) _view.SetGhost(true, world, sil, target != null);
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
            _audio?.PlayAt("ui.deploy_grab.ui", unit.Position);
        }

        /// <summary>
        /// Можно ли отпустить перетаскиваемого бойца в этой точке.
        /// </summary>
        /// <remarks>
        /// Одна точка правды для превью и для самой постановки: разъехавшись, они дают призрака,
        /// который горит зелёным там, откуда на отпускании боец откатится назад.
        /// <para>Сторона зоны берётся по КОМАНДЕ бойца, а не «всегда своя»: на Ристалище противника
        /// двигают в его половину, и жёсткая <c>Player</c> запрещала бы любой его сдвиг — зоны сторон
        /// не пересекаются.</para>
        /// </remarks>
        private bool CanDrop(Vector2 target) =>
            _dragged != null
            && _deploy.CanPlace(target, SideOf(_dragged.Team), CanUseExtended(_dragged))
            && !Overlaps(target, _dragged);

        private void OnPointerReleased()
        {
            if (_dragged == null) return;

            // Ввод заглушили посреди протяжки (консоль по F1, модальный экран) — отпускание считаем
            // ОТМЕНОЙ: ставить бойца по курсору, уведённому в интерфейс, значит удивить игрока. Само
            // событие при этом обязано дойти, иначе боец остался бы «в руке» до постороннего клика.
            if (_dragMoved && !_input.GameplaySuppressed) // именно перетаскивание (не клик) → пробуем поставить
            {
                // Та же целевая точка, что вела призрака: иначе юнит на отпускании прыгал бы к курсору.
                Vector2 target = DragTarget(ScreenToWorld(_input.PointerScreenPosition));
                if (CanDrop(target))
                {
                    _dragged.Position = target;
                    _dragged.PreviousPosition = target; // снап вида (без слайда интерполяции)
                    UpdateSlotPos(_dragged.Id, target);
                    _audio?.PlayAt("ui.deploy_place.ui", target);
                }
                else
                {
                    // невалидно → юнит остаётся на месте (reject). Молчаливый откат читается как «не нажалось».
                    _audio?.Play("ui.deploy_reject.ui");
                }
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

        private void EquipOn(int unitId, RelicData relic)
        {
            if (!_deploying) return;
            Slot slot = FindSlot(unitId);
            if (slot == null) return;
            slot.Unit = relic;
            // Надетый прямо на поле кит — изменение ГИЛЬДИИ, а не превью боя (реш. Макса): переживает бой и сейв.
            if (_commands?.SetSlotRelic(slot.GuildIndex, relic.Id) == true) _rosterDirty = true;
            RebuildPreview();
        }

        // Пересобрать превью боя из редактируемого ростера (респавн через штатный путь — виды перестраиваются).
        private void RebuildPreview()
        {
            // Слоты обеих сторон разбираются ПО КОМАНДАМ. Слить их в один список нельзя: на площадке у
            // противника теперь тоже есть слоты, и общий список отправил бы его бойцов в команду 0 —
            // перетаскивание врага пересобирало бы бой в восемь своих.
            var side = new List<PlayerSpawn>(_slots.Count);
            var foes = new List<PlayerSpawn>();
            foreach (Slot s in _slots)
            {
                var spawn = new PlayerSpawn(s.Unit, s.Vessel, s.Pos);
                if (s.Team == 0) side.Add(spawn);
                else             foes.Add(spawn);
            }

            // На площадке слоты второй стороны и есть источник правды о ней — список _opponents только
            // отражает их. В забеге слотов у врага нет, и там правду по-прежнему держит энкаунтер.
            if (_encounter == null && foes.Count > 0)
            {
                _opponents.Clear();
                _opponents.AddRange(foes);
            }

            // Состав или расстановка изменились — прежние «готов» относились к тому, чего больше нет.
            // Без этого второй игрок подтвердил бы один строй, а в бой ушёл бы другой.
            _ready?.Reset("расстановка изменилась");

            // ResetBattle + enqueue (сбрасывает паузу). На полигоне энкаунтера нет — противник задан
            // списком, и пересобирать надо ОБЕ стороны: иначе перетаскивание своего бойца стирало бы врага.
            if (_encounter != null) _loader.Load(_encounter, side);
            else _loader.LoadSides(side, _opponents);
            _sim.SetPaused(true);
            _sim.FlushSpawns();
            BindLiveUnits();

            _dragged = null;
            _dragMoved = false;
            _hoverUnitId = -1;
        }

        // ── Старт боя ────────────────────────────────────────────────────────
        private void StartCombat()
        {
            FlushRoster();          // расстановка, с которой идём в бой, должна пережить и бой, и вылет игры
            RememberProvingSquad(); // на площадке: с чем ушли в бой — с тем и вернёмся в расстановку
            _deploying = false;
            _dragged = null;
            _view?.SetActive(false);
            _sim.SetPaused(false);
            // Фаза Fighting — она же сигнал камере: сценарный вид включит она сама, если игрок его выбрал.
            _session.SetPhase(BattlePhase.Fighting); // центр панели = таймер боя; фаза → навигатор ставит контекст Combat (K8)

            // Места бой не меняет — поэтому серую арену тут никто не трогает. Гашение серой зоны читается
            // верхним циклом как «игрок ушёл с площадки»: сделай это здесь, и Ристалище закрывалось бы
            // главным меню на первом же ударе.

            // Переход арены здесь НЕ играем. Он говорит «место сменилось», а на «Начать» место то же самое:
            // оно уже явилось при входе в узел, и повтор читался как сбой, а не как язык. Смена облика
            // остаётся за входом в узел и за возвратом на полигон после боя.
        }

        /// <summary>
        /// Запомнить расклад Ристалища перед боем: после боя площадка встаёт заново ровно им. Вне площадки
        /// не нужен — там расстановку хранит гильдия забега (<c>RunState.Guild</c>), а не мы.
        /// </summary>
        private void RememberProvingSquad()
        {
            if (_venue != Venue.ProvingGrounds) return;

            _provingSquad.Clear();
            _provingOpponents.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.Unit == null) continue;

                // Обе стороны, каждая в свой список: после боя площадка встаёт ровно тем, с чем в него
                // ушли. Запоминать одних своих значило бы, что «Продолжить» возвращает игрока к его
                // расстановке и к раскладу противника ИЗ АССЕТА — то есть к чужому бою.
                var spawn = new PlayerSpawn(s.Unit, s.Vessel, s.Pos);
                if (s.Team == 0) _provingSquad.Add(spawn);
                else             _provingOpponents.Add(spawn);
            }
        }

        // ── Хелперы ──────────────────────────────────────────────────────────
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
                if (!CanCommand(u.Team) || u.IsDead) continue;

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
            if (_commands?.SetSlotPosition(s.GuildIndex, pos) == true) _rosterDirty = true;
        }

        // Правки расстановки уезжают в durable-гильдию сразу, а на диск — на выходе из фазы (старт боя, выход
        // из тест-зоны). Писать сейв на каждый drop незачем: за одну расстановку их десятки, а состояние в
        // RunState уже актуально — автосейв узла подхватит его и без нас.
        private bool _rosterDirty;

        private void FlushRoster()
        {
            if (!_rosterDirty) return;
            _rosterDirty = false;
            // Просим ЗАПИСЬ зафиксировать, а не держателя состояния сохраниться: кто владелец сейва и
            // есть ли он вообще — не дело расстановки. Вне забега ответ «нечего фиксировать», и это
            // ровно то, что мы про дев-арену и знаем.
            _commands?.RequestSave();
        }
    }
}
