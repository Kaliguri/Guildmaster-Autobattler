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
    /// Владелец МЕСТА и СОСТАВА расстановки: кто вышел на арену, откуда он там взялся и когда
    /// начинается бой. На загрузку Free-пресета (<see cref="EncounterLoader.FreeDeploymentRequested"/>)
    /// ставит бой на паузу, флашит спавны и держит фазу до общего согласия «Начать».
    /// <list type="bullet">
    /// <item>Состав площадки: узел забега, построение между узлами или Ристалище — см. <c>Venue</c>.</item>
    /// <item>Исполнение намерений игроков: <see cref="UnitMoveIntent"/>, <see cref="OpenLoadoutIntent"/>,
    /// <see cref="EquipRelicRequest"/> — правит ростер и пересобирает превью респавном.</item>
    /// <item>Исход боя на площадке и возврат в расстановку по общему согласию.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>Живёт ТОЛЬКО у хозяина сеанса</b> (см. <c>CombatLifetimeScope</c>): состав арены поднимает
    /// пресет боя, а у гостя пресета не бывает — бой приезжает лентой. Руки игрока живут отдельно, в
    /// <see cref="DeploymentInteraction"/>, и есть у обоих участников: право трогать фигурки и право
    /// решать, кто на арене, — это разные права, и разведены они по разным классам намеренно.
    /// <para><b>Намерение здесь ПЕРЕПРОВЕРЯЕТСЯ.</b> Зону и перекрытие уже проверили руки, но руки
    /// бывают чужие и бывают устаревшие: у гостя арена отстаёт на задержку сети, и точка, законная в
    /// его кадре, к нашему может быть занята. Владелец — последнее слово, и это не перестраховка, а
    /// то самое «одна авторитетность на факт».</para>
    /// </remarks>
    public sealed class DeploymentController : IStartable, IDisposable
    {
        // Что подтверждают все игроки: прежде чем бой начнётся — и прежде чем вернуться с итогов боя
        // обратно в расстановку. Ключи общие с теми, кто рисует счёт, поэтому живут одним владельцем.
        private const string ReadyKeyStart    = Core.Net.DecisionKeys.BattleStart;
        private const string ReadyKeyContinue = Core.Net.DecisionKeys.BattleContinue;

        private readonly EncounterLoader  _loader;
        private readonly CombatSimulation _sim;
        private readonly DeploymentService _deploy;
        private readonly IPublisher<OpenLoadoutRequest> _openLoadoutPub;
        private readonly ISubscriber<EquipRelicRequest> _equipSub;
        private readonly ISubscriber<UnitMoveIntent>    _moveSub;      // «поставь бойца сюда» — от рук любого игрока
        private readonly ISubscriber<OpenLoadoutIntent> _loadoutSub;   // «покажи снаряжение этого бойца»
        private readonly ISubscriber<SetTestZoneRequest> _testZoneSub; // радио-табы: целевое состояние тест-зоны (интент)
        private readonly ISubscriber<SetFormationRequest> _formationSub; // кнопка передышки «К построению» (интент)
        private readonly ISubscriber<ProvingGroundsSetupRequest> _groundsSetupSub; // заказ состава площадки (дев-срезы)
        private readonly IPublisher<TestZoneChangedEvent> _testZoneChangedPub; // Ф5: вещаем СОСТОЯНИЕ (единый источник)
        private readonly IPublisher<ArenaRevealRequest>   _arenaRevealPub;    // «яви место боя» — подача за презентером
        // Титр «В бой!»: тот же приём появления, что у победы и поражения (вердикт Макса 22.08.2026).
        private readonly IPublisher<Core.Flow.TitleRevealRequest> _titlePub;
        private readonly IBattleSession   _session;
        // За какую сторону играем МЫ. Своего поля с командой у расстановки нет и не должно быть:
        // владелец этого факта один — состав сеанса, и спрашивается он в момент вопроса.
        private readonly Core.Players.ILocalPlayer _localPlayer;
        // Кто ещё в сеансе и за какую сторону: по нему проверяется право автора намерения. Вне сеанса
        // (соло) пуст, и право сводится к «участник ровно один».
        private readonly Core.Players.ISessionRoster _roster;
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
        private readonly Core.Net.ISharedDecision _ready;
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

        private IDisposable _equipSubscription;
        private IDisposable _moveSubscription;
        private IDisposable _loadoutSubscription;
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

        public DeploymentController(
            EncounterLoader loader,
            CombatSimulation sim,
            DeploymentService deploy,
            IPublisher<OpenLoadoutRequest> openLoadoutPub,
            IPublisher<Core.Flow.TitleRevealRequest> titlePub,
            ISubscriber<EquipRelicRequest> equipSub,
            ISubscriber<UnitMoveIntent> moveSub,
            ISubscriber<OpenLoadoutIntent> loadoutSub,
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
            Core.Net.ISharedDecision ready,
            Core.Players.ILocalPlayer localPlayer,
            Core.Players.ISessionRoster roster,
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
            _localPlayer   = localPlayer;
            _roster        = roster;
            _arenaRevealPub = arenaRevealPub;
            _audio         = audio;
            _runStates     = runStates;
            _commands      = commands;
            _loader        = loader;
            _sim           = sim;
            _deploy        = deploy;
            _openLoadoutPub = openLoadoutPub;
            _titlePub       = titlePub;
            _equipSub      = equipSub;
            _moveSub       = moveSub;
            _loadoutSub    = loadoutSub;
            _testZoneSub   = testZoneSub;
            _formationSub  = formationSub;
            _groundsSetupSub = groundsSetupSub;
            _testZoneChangedPub = testZoneChangedPub;
            _session       = session;
        }

        public void Start()
        {
            _loader.FreeDeploymentRequested += OnFreeDeployment;
            _equipSubscription = _equipSub.Subscribe(OnEquip);
            // Намерения игроков — единственный вход в правку арены. Свой клик приходит той же дорогой,
            // что чужой: разных путей к одному результату быть не должно, иначе они разъедутся.
            _moveSubscription    = _moveSub?.Subscribe(OnMoveIntent);
            _loadoutSubscription = _loadoutSub?.Subscribe(OnLoadoutIntent);
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
            // None — вне боя (сброс на закрытии боя, BattleHost.Close).
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
        /// Половина арены, на которой стоит эта команда.
        /// </summary>
        /// <remarks>
        /// <b>Ноль здесь — НЕ «моя команда», а первая сторона арены,</b> и менять его на сторону
        /// смотрящего нельзя: зоны расстановки привязаны к геометрии площадки, а не к тому, кто на неё
        /// смотрит. Игрок за команду 1 расставляется в правой зоне и у себя, и у противника — иначе бойцы
        /// у двух клиентов стояли бы в разных местах одной арены.
        /// </remarks>
        private static DeploymentSide SideOf(int team) =>
            team == 0 ? DeploymentSide.Player : DeploymentSide.Enemy;

        // Есть ли на арене живой противник. Дешевле и честнее флага «это боевой узел»: полигон построения
        // и площадка отличаются друг от друга ровно этим, а не тем, кто их открыл.
        private bool HasOpponents()
        {
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Team != _localPlayer.Team && !units[i].IsDead) return true;
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
            _equipSubscription?.Dispose();
            _moveSubscription?.Dispose();
            _loadoutSubscription?.Dispose();
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
            // Возвращаем ТУ фазу, в которой место застали: вне забега — None (панель без «Начать»),
            // в передышке — Interlude (мир на экране, задник UI по-прежнему запрещён).
            BattlePhase back = _returnPhase;
            SetVenue(Venue.None); // цветная арена + снятие Sheet — по ребру внутри
            _session.SetPhase(back);
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

        // ── Намерения игроков ────────────────────────────────────────────────

        /// <summary>
        /// «Поставь бойца сюда». Единственный вход в правку расстановки — и для своих рук, и для рук
        /// напарника.
        /// </summary>
        /// <remarks>
        /// <b>Проверяем ЗАНОВО всё, что проверили руки.</b> Право на сторону — потому что автор
        /// намерения может быть не тот, кто им распоряжается; зону и перекрытие — потому что арена
        /// автора отстаёт от нашей на задержку сети, и законная у него точка к нам может прийти уже
        /// занятой. Отказ молчаливый: у автора боец просто останется на месте, а звук отказа ему уже
        /// сыграли собственные руки.
        /// </remarks>
        private void OnMoveIntent(UnitMoveIntent e)
        {
            if (!_deploying) return;

            RuntimeUnit unit = FindLive(e.UnitId);
            if (unit == null || unit.IsDead) return;
            if (!MayCommand(e.PlayerId, unit.Team)) return;
            if (!_deploy.CanPlace(e.Position, SideOf(unit.Team), CanUseExtended(unit))) return;
            if (Overlaps(e.Position, unit)) return;

            unit.Position         = e.Position;
            unit.PreviousPosition = e.Position; // снап вида, без слайда интерполяции
            UpdateSlotPos(e.UnitId, e.Position);
        }

        /// <summary>«Покажи снаряжение этого бойца»: экран открывает владелец состава — кит и сосуд знает он.</summary>
        private void OnLoadoutIntent(OpenLoadoutIntent e)
        {
            if (!_deploying) return;
            Slot slot = FindSlot(e.UnitId);
            _openLoadoutPub.Publish(new OpenLoadoutRequest(e.UnitId, slot?.Relic, slot?.Vessel));
        }

        /// <summary>
        /// Распоряжается ли этот участник сеанса той стороной. Своя сторона у автора спрашивается в
        /// составе сеанса — единственном владельце факта «кто за кого играет»; само право — у
        /// мероприятия, и то же самое, по которому руки игрока решают, кого вообще можно взять.
        /// </summary>
        private bool MayCommand(int playerId, int team)
        {
            // Автора нет в составе — значит соло: участник ровно один, и это мы.
            int side = _roster != null && _roster.TryGet(playerId, out Core.Players.SessionPlayer p)
                ? p.Team
                : _localPlayer.Team;

            return _activity.MayCommandSide(team, side);
        }

        private RuntimeUnit FindLive(int unitId)
        {
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++) if (units[i].Id == unitId) return units[i];
            return null;
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
        }

        // ── Старт боя ────────────────────────────────────────────────────────
        private void StartCombat()
        {
            FlushRoster();          // расстановка, с которой идём в бой, должна пережить и бой, и вылет игры
            RememberProvingSquad(); // на площадке: с чем ушли в бой — с тем и вернёмся в расстановку
            _deploying = false;
            _sim.SetPaused(false);
            // Фаза Fighting — она же сигнал камере: сценарный вид включит она сама, если игрок его выбрал.
            _session.SetPhase(BattlePhase.Fighting); // центр панели = таймер боя; фаза → навигатор ставит контекст Combat (K8)

            // «В БОЙ!» — тем же приёмом появления, что победа и поражение (вердикт Макса 22.08.2026,
            // 7А: «единый стиль и источник и переюзание под всякие ситуации»). Титр ничего не ждёт и
            // ничего не решает: он уходит сам, пока идут первые удары.
            _titlePub?.Publish(Core.Flow.TitleRevealRequest.ToBattle());

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
