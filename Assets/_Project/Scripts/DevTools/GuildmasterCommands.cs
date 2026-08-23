using Guildmaster.Combat;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using MessagePipe;
using Guildmaster.Core.DevConsole;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VContainer;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Набор отладочных команд Quantum Console для Фазы 1 (вики «10» §9).
    /// Инъектируется через VContainer — зависимости появляются только при наличии активной симуляции.
    /// </summary>
    public sealed class GuildmasterCommands : MonoBehaviour
    {
        // Ссылок на семь конкретных мементо в инспекторе больше нет: Мементо дев-среза резолвится по id
        // из контент-БД — тем же способом, что и болванчик строкой ниже. Семь serialized-полей означали,
        // что переименование или замена ассета ломает команду молча, а сцена помнит контент (2026-07-26).

        [Tooltip("Тот же SimTuningConfig, что и на CombatLifetimeScope — для gm_tuning_rebake (QC).")]
        [SerializeField] private SimTuningConfig _simTuningConfig;

        private CombatDebugDraw    _debugDraw;
        private IInputService      _input;

        // Владелец жизненного цикла МЕРОПРИЯТИЯ (корень). Ни боя, ни занятия консоль не запоминает:
        // и то и другое рождается и умирает, а этот объект живёт всю игру — запомненная ссылка
        // протухла бы на первом же переходе. Спрашиваем текущее в момент команды.
        private Guildmaster.Game.Activity.ActivityHost _activities;

        /// <summary>Рукопожатие боя идущего мероприятия или null. Живёт в занятии, поэтому не запоминается.</summary>
        private Guildmaster.Game.Flow.IBattleSession Session => _activities != null ? _activities.Battles : null;

        /// <summary>Владелец боя ИДУЩЕГО мероприятия или null (мероприятия нет).</summary>
        private Guildmaster.Game.Flow.BattleHost HostOrNull => _activities != null ? _activities.Battle : null;

        /// <summary>
        /// Он же, но с открытием РИСТАЛИЩА при нужде: команда боя вне мероприятия и означает «заведи мне
        /// площадку». Своей дев-арены у нас нет (решение Макса 02.08.2026) — тест-бои это прегены состава
        /// для Ристалища, и открывать им отдельный вид мероприятия значило бы держать второй путь.
        /// </summary>
        private Guildmaster.Game.Flow.BattleHost HostEnsured
            => _activities != null ? _activities.EnsureBattleHost() : null;

        /// <summary>
        /// Стоим ли мы сейчас на Ристалище. СПРАШИВАЕМ у мероприятия, а не помним по событиям.
        /// </summary>
        /// <remarks>
        /// Прежде здесь жил флаг, поднимавшийся по <c>TestZoneChangedEvent</c>. Он залипал: площадка
        /// уходит вместе со скоупом боя, а сказать об этом уже некому — событие о выходе приходит только
        /// когда игрок покидает место сам. После пары переходов флаг оставался поднятым, и `bones`
        /// молча слала заказ в пустоту вместо того, чтобы открыть площадку (наход. Макса 02.08.2026).
        /// </remarks>
        private bool OnProvingGrounds
            => _activities != null && _activities.Current.Kind == ActivityKind.ProvingGrounds;

        [Tooltip("UXML витрины боёв (F3): список того, что можно запустить прямо сейчас.")]
        [SerializeField] private UnityEngine.UIElements.VisualTreeAsset _battleBrowserUxml;

        private Guildmaster.UI.MenuRouter _menuRouter; // владелец показа консоли: у него и спрашиваем видимость
        private Guildmaster.UI.UiNavigator _navigator; // витрину боёв показываем сами: UI-слою о боях знать незачем
        private DevBattleBrowserScreen _battleBrowser;
        private DevCommandRegistry _registry;          // куда кладём команды
        private DevCommandSet _commands;               // свои команды + статические наборы; снимаются вместе с модулем

        // Ристалище: интент входа и состояние площадки. Живут ВЫШЕ боевого скоупа (Root), поэтому
        // резолвятся опционально — в standalone-арене без Root их нет, и команда честно об этом скажет.
        private Core.Flow.IRunControl _runControl;
        private MessagePipe.IPublisher<Core.Flow.OpenProvingGroundsRequest> _provingGroundsPub;

        // Заказ состава площадки: на Ристалище бойцов ставит расстановка, а команда только говорит, каких.
        // Спавнить их самим нельзя — см. StageOnProvingGrounds.
        private MessagePipe.IPublisher<Data.Definitions.ProvingGroundsSetupRequest> _groundsSetupPub;

        // Дамми-болванчики оформлены как полноценный юнит (EnemyData «enemy.training_dummy»): свой SO,
        // визуал MedievalWarrior (→ анимации). Резолвится из контент-БД, поэтому не нужен serialized-ref в сцене.
        private UnitData _dummyEnemy;

        // Дев-дуэлянт на скелетном визуале (EnemyData «enemy.bone_storybook»): отдельный ассет ради того,
        // чтобы смотр анимации не требовал подмены вида у игрового мементо. Бьёт и умирает — иначе не
        // увидеть ни удар, ни разлёт на осколки.
        // Второй такой же (`enemy.bone_dev` на виде Standart) удалён 06.08.2026: он существовал, чтобы
        // сравнивать новый арт со старым рядом, а старого больше нет — Storybook единственный живой вид.
        private UnitData _boneStorybookDuelist;

        // Контент-БД для дев-срезов: Мементо берётся по id (relic.*) в момент вызова команды.
        private IContentDatabase _content;

        // Снапшот арены: по зонам расстановки команды считают КРАЯ поля, а не хардкодят координаты —
        // «максимально далеко друг от друга» на разных аренах означает разные числа.
        private Core.Arena.ArenaLayoutData _arena;

        // Открыта ли консоль сейчас: пока да — глушим наш игровой ввод (кроме F5), чтобы набор
        // команд в консоли не протекал в геймплей (пауза/смена вида/пан-зум/перезапуск боя).
        private bool _consoleOpen;

        // Последний сетап боя для быстрого перезапуска по R. static — переживает релоад сцены (F5),
        // чтобы R после F5 всё ещё знал последний бой.
        private static System.Action<GuildmasterCommands> _lastBattleSetup;

        /// <summary>
        /// Задать «последний бой» для R извне (dev-панель энкаунтеров) — единый владелец R остаётся здесь,
        /// а внешний источник просто регистрирует свой рестарт. Делегат должен резолвить живой скоуп сам
        /// (переживает F5). Перекрывается следующим gm_spawn_* (last-write-wins).
        /// </summary>
        public static void SetLastBattle(System.Action<GuildmasterCommands> setup) => _lastBattleSetup = setup;

        /// <summary>Симуляция ИДУЩЕГО боя или null. Для того, что боя не открывает: пауза, статус, отчёты.</summary>
        private CombatSimulation SimOrNull => HostOrNull != null ? HostOrNull.Resolve<CombatSimulation>() : null;

        /// <summary>Режим dev-оверлеев идущего боя или null (вне боя оверлеям нечего показывать).</summary>
        private DevOverlayMode OverlayModeOrNull => HostOrNull != null ? HostOrNull.Resolve<DevOverlayMode>() : null;

        /// <summary>
        /// Арена, на которой работает дев-срез. Боя нет — открываем пустой дев-бой: команда «поставь мне
        /// болванчика» и означает «мне нужна арена», а требовать сперва зайти в узел значило бы сделать
        /// инструмент менее полезным, чем он был при вечной симуляции.
        /// </summary>
        private CombatSimulation Sim
        {
            get
            {
                CombatSimulation live = SimOrNull;
                if (live != null) return live;
                Guildmaster.Game.Flow.BattleHost host = HostEnsured;
                if (host == null)
                {
                    Debug.LogWarning("[GuildmasterCommands] - мира нет → арену открывать некому");
                    return null;
                }

                host.Open(DevArenaPreset());
                return SimOrNull;
            }
        }

        /// <summary>Фабрика юнитов идущего боя. Как <see cref="Sim"/>, открывает дев-арену при нужде.</summary>
        private RuntimeUnitFactory Factory
        {
            get
            {
                if (Sim == null) return null;
                return HostOrNull?.Resolve<RuntimeUnitFactory>();
            }
        }

        /// <summary>
        /// Пустой бой для дев-срезов: ни врагов, ни ростера — состав ставят сами команды. Транзиентный
        /// пресет, а не ассет: этот бой не принадлежит контенту и не должен в нём заводиться.
        /// </summary>
        private static Data.Definitions.BattlePresetData DevArenaPreset()
            => Data.Definitions.BattlePresetData.CreateRuntime(
                encounter: null, roster: System.Array.Empty<Data.Definitions.PlayerSlot>(),
                mode: Data.Definitions.DeploymentMode.Fixed, partyItems: null, id: "battle.dev.arena");

        [Inject]
        public void Construct(CombatDebugDraw debugDraw,
            IInputService input, IContentDatabase contentDatabase, Core.Arena.ArenaLayoutData arena,
            IObjectResolver resolver)
        {
            _debugDraw   = debugDraw;
            _input      = input;
            _content = contentDatabase;
            _arena   = arena;
            contentDatabase.TryGet("enemy.training_dummy", out _dummyEnemy);
            contentDatabase.TryGet("enemy.bone_storybook", out _boneStorybookDuelist);
            // Сессия боя живёт в RootScope: в реальном забеге резолвится, в standalone dev-арене (без Root) — null.
            resolver.TryResolve(out _activities);
            resolver.TryResolve(out _runControl);
            resolver.TryResolve(out _provingGroundsPub);
            resolver.TryResolve(out _groundsSetupPub);
            // Реестр и роутер живут в корне; в standalone dev-арене без Root их нет — команды тогда просто
            // некуда класть, и это не ошибка (тот же случай, что и с сессией боя выше).
            resolver.TryResolve(out _registry);
            resolver.TryResolve(out _menuRouter);
            resolver.TryResolve(out _navigator);
        }

        /// <summary>
        /// Консоль подключается к миру САМА. Прежде её инжектил боевой скоуп — он лежал в одной сцене с
        /// ней и держал её в списке автоинъекции; теперь бой рождается из префаба и на объекты сцены
        /// ссылаться не может. Мировой скоуп её тоже не зарегистрирует: игра не ссылается на dev-слой, и
        /// заводить эту ссылку ради консоли — значит развернуть зависимость не в ту сторону.
        /// </summary>
        /// <remarks>
        /// Мир поднимается раньше сцены с консолью (<c>GameBootstrap</c> грузит WorldScene первой),
        /// поэтому к <c>Awake</c> он уже построен. Нет мира — значит запущена одиночная сцена без него, и
        /// консоль честно останется без боевых команд.
        /// </remarks>
        private void Awake()
        {
            var world = VContainer.Unity.LifetimeScope.Find<Guildmaster.Game.WorldLifetimeScope>();
            if (world != null && world.Container != null) world.Container.Inject(this);
            else Debug.LogWarning("[GuildmasterCommands] - мир не найден: боевые команды консоли работать не будут");
        }

        // Пауза сима, пока консоль открыта: настраиваешь бой за консолью, закрываешь — он идёт с начала
        // на виду (без этого бой проигрывается за полноэкранной консолью и заканчивается невидимым).
        private void Start()
        {
            if (_menuRouter != null) _menuRouter.DevConsoleVisibilityChanged += OnConsoleVisibilityChanged;
            if (_input != null) _input.DevBattlesToggleRequested += ToggleBattleBrowser;

            // Команды кладём ЗДЕСЬ, а не в Construct: реестр приходит инъекцией, но статические наборы
            // (арена/карта/эффекты) тоже надо куда-то регистрировать, а своего объекта в сцене у них нет.
            // Один набор на модуль — и снимается он одним Dispose, что важно после domain reload:
            // повторная регистрация того же имени в реестре — исключение, а не тихая замена.
            if (_registry != null)
            {
                _commands = new DevCommandSet(_registry);
                RegisterCommands(_commands);
                ArenaDevCommands.Register(_commands);
                MapDevCommands.Register(_commands);
                VisualFxCommands.Register(_commands);
                DiagCommands.Register(_commands);
                SessionDevCommands.Register(_commands);
                UiDevCommands.Register(_commands);
            }
        }

        // Состояние площадки: открыта ли она прямо сейчас. Ставить по этому событию бой больше нечего —
        // состав площадки заказывается ДО входа и применяется её собственным владельцем (см. StageOnProvingGrounds).

        private void OnDestroy()
        {
            if (_menuRouter != null) _menuRouter.DevConsoleVisibilityChanged -= OnConsoleVisibilityChanged;
            if (_input != null) _input.DevBattlesToggleRequested -= ToggleBattleBrowser;
            _commands?.Dispose();
        }

        /// <summary>
        /// Показать/снять витрину боёв (F3). Экран живёт одним инстансом: в нём набранный запрос и выбор,
        /// и пересоздание сбрасывало бы их при каждом закрытии.
        /// </summary>
        private void ToggleBattleBrowser()
        {
            if (_navigator == null || _registry == null) return;

            if (_battleBrowserUxml == null)
            {
                Debug.LogError("[GuildmasterCommands] - витрина боёв: не разведён UXML " +
                               "(поле _battleBrowserUxml на объекте dev-команд)", this);
                return;
            }

            if (_battleBrowser != null && _navigator.AnyScreen(s => ReferenceEquals(s, _battleBrowser)))
            {
                _navigator.Remove(_battleBrowser);
                return;
            }

            // Полки взаимоисключающи: открытая консоль уходит, иначе две простыни лягут внахлёст.
            _menuRouter?.CloseDevOverlays();

            if (_battleBrowser == null)
                _battleBrowser = new DevBattleBrowserScreen(_battleBrowserUxml, _registry, _content);
            else
                _battleBrowser.Refresh();   // список мог измениться, пока витрина была закрыта

            _navigator.Push(_battleBrowser);
        }

        // Консоль показана/снята — тот же смысл, что раньше несли OnActivate/OnDeactivate у QFSW.
        private void OnConsoleVisibilityChanged(bool visible)
        {
            if (visible) PauseForConsole();
            else ResumeAfterConsole();
        }

        // Пауза сима, которая была ДО открытия консоли. Консоль паузой не владеет — она её одалживает:
        // владелец (расстановка, тумблер Space) мог поставить паузу задолго до нас, и снимать её за него
        // нельзя. Ровно это и стреляло: команда вводила игрока в расстановку Ристалища (пауза), а
        // закрытие консоли её снимало — бой начинался сам, без нажатия «Начать».
        private bool _pausedBeforeConsole;

        // Консоль открыта: пауза сима (настраиваешь бой за консолью, закрываешь — он идёт с начала на
        // виду) + глушим игровой ввод, чтобы буквы команд не текли в геймплей.
        private void PauseForConsole()
        {
            _consoleOpen = true;
            _pausedBeforeConsole = SimOrNull != null && Sim.IsPaused;
            SimOrNull?.SetPaused(true);
            if (_input != null) _input.SetSuppressed(Core.Input.InputSuppressSource.DevConsole, true);
        }

        private void ResumeAfterConsole()
        {
            _consoleOpen = false;
            // Паузу возвращаем ВЛАДЕЛЬЦУ, а не «снимаем». В расстановке владелец — она сама: мир там стоит
            // по определению, и бой начинает кнопка «Начать», а не закрытая консоль. Фазу спрашиваем, а не
            // помним: команда могла увести игрока в расстановку уже ПОСЛЕ открытия консоли — ровно так и
            // делает gm_proving_grounds.
            bool deploying = Session != null && Session.Phase == Data.Definitions.BattlePhase.Deployment;
            SimOrNull?.SetPaused(deploying || _pausedBeforeConsole);
            if (_input != null) _input.SetSuppressed(Core.Input.InputSuppressSource.DevConsole, false);
        }

        // Dev-хоткеи (new Input System): F5 — полный релоад сцены (пустая арена), R — рестарт боя НА МЕСТЕ.
        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // F5 работает всегда (жёсткий сброс сцены) — даже с открытой консолью.
            if (kb.f5Key.wasPressedThisFrame) Restart();

            // R (перезапуск боя) глушим, пока консоль открыта: иначе буква «r» в команде дёргает рестарт.
            // Dev-спавн (gm_spawn_*) задал последний бой → перезапускаем его; иначе это бой ЗАБЕГА (грузится
            // BattleFlow→BattleHost→BattleStartup, dev-сетап пуст) → перезапуск на месте через сессию.
            if (!_consoleOpen && kb.rKey.wasPressedThisFrame)
            {
                if (_lastBattleSetup != null) RestartLastBattle();
                else if (Session == null || !Session.RestartInPlace()) RestartLastBattle(); // варнинг «нет боя»
            }
        }

        /// <summary>
        /// Объявить команды модуля в наборе. Тела остаются обычными методами: реестр хранит делегат, а не
        /// рефлексию, поэтому команда — это одна строка объявления рядом с методом, который она зовёт.
        /// </summary>
        /// <remarks>
        /// Команды ничего не возвращают строкой: их отчёты идут через <c>Debug.Log</c>, а консоль слушает
        /// лог — то есть вывод виден и в ней, и в Console редактора, без дублирования текста в двух местах.
        /// </remarks>
        private void RegisterCommands(DevCommandSet set)
        {
            // --- Бой: запустить, закончить, повторить ---

            // Команд запуска боя осталась ОДНА (решение Макса 02.08.2026). Снесены spawn, crowd, kit,
            // mirror, battle, preset, battles: все они ставили бой в обход мероприятия — спавнили состав
            // мимо расстановки, которая площадкой владеет. Работали они, пока боевой скоуп был вечным;
            // с рождением боя по требованию каждая стала гонкой «заказ раньше владельца». Балансные
            // прогоны (mirror, crowd) живут в SimBench, где им и место — там не нужен ни показ, ни арена.
            set.Add("storybook", "1×1 дуэль скелетных дев-бойцов (смоук вида юнита)",
                _ => { SpawnBoneStorybookDuel(); return null; });

            set.Add("win", "Мгновенно завершить бой победой команды A",
                _ => { SkipBattle(); return null; });

            set.Add("restart", "Перезапустить последний бой на месте",
                _ => { RestartLastBattle(); return null; });

            set.Add("reload", "Перезагрузить сцену целиком (жёсткий сброс)",
                _ => { Restart(); return null; });

            set.Add("grounds", "Уйти на Ристалище: свернуть забег и открыть площадку",
                _ => { ProvingGrounds(); return null; });

            // --- Правка состояния ---

            set.Add("hp", "Выставить HP юниту по id",
                a => { SetHp(a.GetInt(0), a.GetFloat(1)); return null; },
                new DevParam("unitId", DevParamType.Int), new DevParam("value", DevParamType.Float));

            set.Add("seed", "Зафиксировать сид боя (действует до старта симуляции)",
                a => { SetRngSeed(a.GetULong(0)); return null; }, new DevParam("seed", DevParamType.Int));

            set.Add("tuning", "Перечитать SimTuning из ассета и применить к бою (бой станет TAINTED)",
                _ => { TuningRebake(); return null; });

            // --- Расталкивание (живая правка) ---

            set.Add("sep", "Показать параметры расталкивания",
                _ => { SepInfo(); return null; });

            set.Add("sep_radius", "Радиус тела на единицу Size",
                a => { SepRadius(a.GetFloat(0)); return null; }, new DevParam("value", DevParamType.Float));

            set.Add("sep_strength", "Сила расталкивания за тик",
                a => { SepStrength(a.GetFloat(0)); return null; }, new DevParam("value", DevParamType.Float));

            set.Add("sep_iters", "Проходов расталкивания за тик",
                a => { SepIters(a.GetInt(0)); return null; }, new DevParam("count", DevParamType.Int));

            set.Add("sep_ally", "Мягкость расталкивания своих (0..1)",
                a => { SepAlly(a.GetFloat(0)); return null; }, new DevParam("scale", DevParamType.Float));

            // --- Что видно на экране ---

            set.Add("draw", "Вкл/выкл отладочную отрисовку боя",
                _ => { ToggleDebugDraw(); return null; });

            set.Add("rings", "Вкл/выкл кольца статусов над юнитами",
                _ => { ToggleStatusOverlay(); return null; });

            set.Add("overlay", "Источник оверлеев: показанный кадр или живой сим",
                _ => { ToggleOverlaySource(); return null; });
        }

        /// <summary>Зафиксировать сид боя для детерминизм-отладки (только до старта).</summary>
        public void SetRngSeed(ulong seed)
        {
            Debug.Log($"[GuildmasterCommands] - gm_rng_seed {seed}: изменение сида поддерживается только через CombatLifetimeScope до запуска");
        }

        /// <summary>Показать текущие параметры расталкивания (SeparationSystem).</summary>
        public void SepInfo()
        {
            if (!SimReady()) return;
            var s = Sim.Separation;
            Debug.Log($"[GuildmasterCommands] - gm_sep: BodyRadiusPerSize={s.BodyRadiusPerSize} (⌀ при Size1 = {s.BodyRadiusPerSize * 2f}), Strength={s.Strength}, Iterations={s.Iterations}, SameTeamScale={s.SameTeamScale}");
        }

        /// <summary>Радиус тела на единицу Size (0.25 = ⌀0.5 при Size1). Крути под ширину спрайта.</summary>
        public void SepRadius(float radiusPerSize)
        {
            if (!SimReady()) return;
            Sim.Separation.BodyRadiusPerSize = Mathf.Max(0.01f, radiusPerSize);
            SepInfo();
        }

        /// <summary>Сила расталкивания за тик (0..1; 1 = жёстко, мягче = плавнее). Live.</summary>
        public void SepStrength(float strength)
        {
            if (!SimReady()) return;
            Sim.Separation.Strength = Mathf.Clamp(strength, 0f, 1f);
            SepInfo();
        }

        /// <summary>Проходов расталкивания за тик (больше = жёстче/дороже). Live.</summary>
        public void SepIters(int iterations)
        {
            if (!SimReady()) return;
            Sim.Separation.Iterations = Mathf.Max(1, iterations);
            SepInfo();
        }

        /// <summary>Множитель расталкивания СВОИХ (0..1): меньше = свои расступаются мягче, задние просачиваются к фронту. Live.</summary>
        public void SepAlly(float scale)
        {
            if (!SimReady()) return;
            Sim.Separation.SameTeamScale = Mathf.Clamp01(scale);
            SepInfo();
        }


        /// <summary>Выставить HP юниту по ID.</summary>
        public void SetHp(int unitId, float hp)
        {
            if (SimOrNull == null) return;

            for (int i = 0; i < Sim.Units.Count; i++)
            {
                var unit = Sim.Units[i];
                if (unit.Id == unitId)
                {
                    unit.CurrentHP = Mathf.Max(0f, hp);
                    Debug.Log($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} HP = {unit.CurrentHP}");
                    return;
                }
            }

            Debug.LogWarning($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} не найден");
        }

        /// <summary>Мгновенно завершить бой (убить всех из команды 1).</summary>
        public void SkipBattle()
        {
            if (SimOrNull == null) return;

            for (int i = 0; i < Sim.Units.Count; i++)
            {
                var unit = Sim.Units[i];
                if (unit.Team == 1) unit.CurrentHP = -1f;
            }

            Debug.Log("[GuildmasterCommands] - gm_skip_battle: все юниты команды B убиты");
        }

        /// <summary>Перезагрузить боевую сцену для нового прогона (бой одноразовый: после конца loop останавливается).</summary>
        public void Restart()
        {
            Scene active = SceneManager.GetActiveScene();
            Debug.Log($"[GuildmasterCommands] - gm_restart: перезагружаю {active.name}");
            SceneManager.LoadScene(active.name);
        }

        /// <summary>
        /// R: перезапустить ПОСЛЕДНИЙ бой НА МЕСТЕ — сброс сима (юниты/снаряды/исход) + повтор последнего сетапа.
        /// Сцену и камеру НЕ перезагружаем: заново начинается только бой (dev-итерация).
        /// </summary>
        public void RestartLastBattle()
        {
            if (_lastBattleSetup == null)
            {
                Debug.LogWarning("[GuildmasterCommands] - gm_restart_battle: последний бой не задан (сначала запусти любой gm_spawn_*)");
                return;
            }
            ResetForNewBattle();
            _lastBattleSetup.Invoke(this);
        }

        /// <summary>Включить/выключить Shapes debug-слой.</summary>
        public void ToggleDebugDraw()
        {
            if (_debugDraw == null) { Debug.LogWarning("[GuildmasterCommands] - CombatDebugDraw не найден"); return; }
            _debugDraw.IsEnabled = !_debugDraw.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_debug_draw: {(_debugDraw.IsEnabled ? "ON" : "OFF")}");
        }

        /// <summary>Включить/выключить dev-слой статус-колец (метка/стан/щит/заморозка/усиление).</summary>
        public void ToggleStatusOverlay()
        {
            var overlay = FindAnyObjectByType<CombatStatusOverlay>(FindObjectsInactive.Include);
            if (overlay == null) { Debug.LogWarning("[GuildmasterCommands] - CombatStatusOverlay не найден (создаётся в бою)"); return; }
            overlay.IsEnabled = !overlay.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_status: {(overlay.IsEnabled ? "ON" : "OFF")}");
        }

        /// <summary>
        /// Переключить источник dev-оверлеев: показанный кадр (по умолчанию) или живой сим. Сим впереди
        /// картинки на окно опережения, поэтому в его режиме кольца и радиусы разъезжаются с боем —
        /// это правда модели, а не баг, и подпись на экране про это говорит.
        /// </summary>
        public void ToggleOverlaySource()
        {
            if (OverlayModeOrNull == null) { Debug.LogWarning("[GuildmasterCommands] - DevOverlayMode не найден (нет активного боевого скоупа)"); return; }
            DevOverlaySource source = OverlayModeOrNull.Toggle();
            Debug.Log($"[GuildmasterCommands] - gm_overlay_source: {source} — {OverlayModeOrNull.Describe()}");
        }

        /// <summary>Пересобрать SimTuning из SO и применить к идущему бою (QC-тюнинг без рекомпиляции).</summary>
        public void TuningRebake()
        {
            if (SimOrNull == null) { Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: нет активного боя"); return; }
            if (_simTuningConfig == null) { Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: SimTuningConfig не назначен"); return; }

            Sim.RebakeTuning(_simTuningConfig.ToSnapshot());
            Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: тюнинг применён к бою → battle TAINTED (реплей невалиден, вики «13» §4.1)");
        }

        // Начать НОВЫЙ бой: сбросить текущий (юниты/снаряды/исход/очереди) + счётчик Id фабрики + снять
        // заморозку времени. Вызывается всеми gm_spawn_* — новая команда старта ПРЕРЫВАЕТ предыдущий бой,
        // а не копит юнитов поверх (иначе Id-коллизии и каша из нескольких боёв).
        private void ResetForNewBattle()
        {
            // ResetBattle() шлёт OnBattleReset → презентация снимает виды/цифры и сбрасывает slowmo/тряску
            // (CombatPresenter.HandleBattleReset → TimeScaleService.Reset). Ручной Time.timeScale тут больше не
            // нужен и вреден: перетёр бы выбранную игроком скорость (единый писатель — TimeScaleService).
            SimOrNull?.ResetBattle();
            Factory?.ResetIds();
        }

        // Единый dev-болванчик: собирается фабрикой из SO «enemy.training_dummy» (реальный путь — статы,
        // Brain из _ai, стартовый HP=MaxHP). Статы дамми правятся ТОЛЬКО в самом SO (1000 HP / 100 урона),
        // без хардкода в харнессе — один дамми на все сценарии gm_spawn_*.
        // Мементо дев-среза по id. Нет в БД — говорим вслух и не спавним: молчаливый пропуск читался бы
        // как «команда не сработала», а причина (контент переименован/не в базе) осталась бы невидимой.
        /// <summary>
        /// Уйти на Ристалище из любого состояния игры (ГДД «Modes - Proving Grounds»): свернуть забег
        /// штатным возвратом в меню, затем послать тот же интент, что кнопка площадки.
        /// </summary>
        /// <remarks>
        /// Команда НЕ делает ничего своими руками: и выход, и вход идут теми же швами, что живой UI, — иначе
        /// у площадки появился бы второй способ открыться, и он бы разошёлся с первым. Решение по интенту
        /// принимает <c>DeploymentController</c>: если отряда нет (мы в меню), он ставит состав из
        /// <c>ProvingGroundsConfig</c>.
        /// </remarks>
        public void ProvingGrounds() => RequestProvingGrounds();

        /// <summary>
        /// Поставить дев-бой на Ристалище: заказать площадке состав и уйти на неё. Возвращает false, если
        /// площадки в этой сборке нет (standalone dev-арена без Root) — тогда зовущий ставит бой сам.
        /// </summary>
        /// <remarks>
        /// Почему команда НЕ спавнит бойцов сама. На площадке составом распоряжается расстановка: она держит
        /// его в слотах, пересобирает превью при каждом перетаскивании и владеет паузой, которую снимает
        /// кнопка «Начать». Прямой спавн в симуляцию проигрывал ей трижды — юниты стирались первой же
        /// пересборкой, сброс боя снимал чужую паузу (бой стартовал сам), а слоты оставались от прежнего
        /// состава. Плюс порядок: событие «площадка открылась» приходит СИНХРОННО внутри запроса входа,
        /// поэтому отложенный спавн, записанный после запроса, не исполнялся никогда — команда молча не
        /// делала ничего, и игрок видел штатный расклад площадки. Заказ снимает все четыре разом: он уходит
        /// ДО входа, а ставит бойцов тот, кто ими и так распоряжается.
        /// </remarks>
        private bool StageOnProvingGrounds(System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn> mine,
            System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn> theirs, string what)
        {
            if (_groundsSetupPub == null || _provingGroundsPub == null) return false;

            var order = new Data.Definitions.ProvingGroundsSetupRequest(mine, theirs, what);

            // Заказали бой — консоли на экране больше делать нечего: её открывали ради этой команды, а
            // смотреть на бой сквозь простыню логов нельзя. Закрываем ЗДЕСЬ, в единственной точке заказа,
            // поэтому одинаково уходят и набранная команда, и выбор из витрины F3.
            _menuRouter?.CloseDevOverlays();

            // Уже на площадке — слушатель заказа существует, шлём событием.
            if (OnProvingGrounds)
            {
                _groundsSetupPub.Publish(order);
                return true;
            }

            // Площадки нет — событие слать НЕКОМУ: расстановка родится вместе с ней. Поэтому заказ
            // кладём мероприятию как параметр входа, а сам вход просим у верхнего цикла: ему ещё меню
            // закрывать. Прежде заказ публиковался до входа и терялся молча — команда «работала», а
            // игрок получал пустую площадку (наход. Макса 02.08.2026).
            _activities?.OrderGroundsRoster(order);
            RequestProvingGrounds();
            return true;
        }

        /// <summary>
        /// Запросить Ристалище. Возвращает false, если запрашивать некому (dev-арена без Root-скоупа).
        /// </summary>
        private bool RequestProvingGrounds()
        {
            if (_provingGroundsPub == null)
            {
                Debug.LogWarning("[GuildmasterCommands] - Ристалище недоступно: нет Root-скоупа " +
                                 "(запущена standalone dev-арена, а не игра)");
                return false;
            }

            // Сначала выход: пока идёт забег, площадка вне забега открыться не имеет права. Отмена
            // всплывает до верхнего цикла, тот возвращается в меню — и там запрос его и встречает.
            _runControl?.RequestReturnToMainMenu();
            _provingGroundsPub.Publish(new Core.Flow.OpenProvingGroundsRequest());
            Debug.Log("[GuildmasterCommands] - gm_proving_grounds: запрошено Ристалище");
            return true;
        }

        /// <summary>
        /// 1×1 зеркально на дев-дуэлянте (<c>enemy.bone_storybook</c>) — смотреть скелетный визуал в живом
        /// бою. Кит у него ЗАЩИТНИКА (Оплот + «Решительный удар»): смотреть скелетную анимацию на бойце без
        /// активок было нечего — ни каста, ни телеграфа щита, ради которого поза блока и делается. Отличие
        /// от Защитника ровно одно: HP 800 вместо 3000, иначе дуэль двух танков идёт полторы минуты и до
        /// смерти с осколками дело не доходит.
        /// У него СВОЙ контент-ассет со ссылкой на костяной вид, поэтому команда работает всегда и ничего не
        /// подменяет в игровом контенте: раньше костяной вид приходилось руками вписывать в <c>relic.base</c>,
        /// и пока подмена стояла, костяным становился каждый бой базового мементо.
        /// Тот же вход на Ристалище, что у <see cref="SpawnMirror"/>.
        /// </summary>
        public void SpawnBoneStorybookDuel()
            => SpawnSkeletalDuel(_boneStorybookDuelist, "enemy.bone_storybook", "storybook",
                self => self.SpawnBoneStorybookDuel());

        /// <summary>
        /// Общий вход зеркальной скелетной дуэли: один юнит слева и справа, либо заказ на Ристалище,
        /// либо прямой спавн на standalone dev-арене.
        /// </summary>
        private void SpawnSkeletalDuel(
            UnitData duelist, string contentId, string commandName, System.Action<GuildmasterCommands> restart)
        {
            if (duelist == null)
            {
                Debug.LogError($"[GuildmasterCommands] - юнита '{contentId}' нет в контент-БД → дуэль не запущена");
                return;
            }

            ResolveDuelEdges(out Vector2 left, out Vector2 right);
            var mine   = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>
                { new Data.Definitions.ProvingGroundsSpawn(duelist, left) };
            var theirs = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>
                { new Data.Definitions.ProvingGroundsSpawn(duelist, right) };

            _lastBattleSetup = restart;
            string view = duelist.ViewPrefab != null ? duelist.ViewPrefab.name : "null";
            string tag = $"gm_spawn_{commandName}_duel";

            if (StageOnProvingGrounds(mine, theirs, tag))
            {
                Debug.Log($"[GuildmasterCommands] - {tag}: дуэль заказана площадке " +
                          $"({left.x:0.##} vs {right.x:0.##}, дистанция {(right.x - left.x):0.##}; ViewPrefab={view}). " +
                          "Бой начинает кнопка «Начать».");
                return;
            }

            // Площадки нет (standalone dev-арена) — владельца состава тоже, ставим бой сами.
            if (!SimReady()) return;
            if (!FactoryReady()) return;

            ResetForNewBattle();
            Sim.EnqueueUnitSpawn(Factory.Create(duelist, null, 0, left));
            Sim.EnqueueUnitSpawn(Factory.Create(duelist, null, 1, right));
            Debug.Log($"[GuildmasterCommands] - {tag}: дуэль поставлена на dev-арене " +
                      $"(дистанция {(right.x - left.x):0.##}; ViewPrefab={view})");
        }

        /// <summary>
        /// Крайние точки своих зон расстановки — дуэлянты встают максимально далеко друг от друга. Дистанция
        /// здесь не украшение: подход, спринт и атака с разбега видны только тогда, когда бойцам есть куда
        /// разбегаться. Зоны читаются из снапшота арены, а не задаются числом: на другой арене «край» другой.
        /// </summary>
        private void ResolveDuelEdges(out Vector2 left, out Vector2 right)
        {
            const float margin = 0.6f;   // запас от кромки: юнит не должен влипать в границу зоны
            const float fallbackX = 6f;  // бесконечное поле (dev-арена без авторинга) — разводим фиксированно

            float y = 0f;
            float xLeft = float.NaN, xRight = float.NaN;

            if (_arena != null)
            {
                y = _arena.Bounds.Center.y;
                for (int i = 0; i < _arena.Zones.Count; i++)
                {
                    Core.Arena.DeploymentZone zone = _arena.Zones[i];
                    float min = zone.Area.Center.x - zone.Area.HalfSize.x + margin;
                    float max = zone.Area.Center.x + zone.Area.HalfSize.x - margin;

                    // Команда 0 живёт в зонах игрока (слева), команда 1 — в зонах врага (справа).
                    if (zone.Side == Core.Arena.DeploymentSide.Player)
                        xLeft = float.IsNaN(xLeft) ? min : Mathf.Min(xLeft, min);
                    else
                        xRight = float.IsNaN(xRight) ? max : Mathf.Max(xRight, max);
                }
            }

            // Зон нет — берём кромки поля; поле бесконечное (Unbounded) — фиксированный разнос.
            if (float.IsNaN(xLeft) || float.IsNaN(xRight))
            {
                float halfWidth = _arena != null ? _arena.Bounds.Rect.HalfSize.x : float.PositiveInfinity;
                float edge = float.IsFinite(halfWidth) ? Mathf.Max(1f, halfWidth - margin) : fallbackX;
                if (float.IsNaN(xLeft))  xLeft  = -edge;
                if (float.IsNaN(xRight)) xRight =  edge;
            }

            left  = new Vector2(xLeft,  y);
            right = new Vector2(xRight, y);
        }

        // Гейты боевых команд. Боя может не быть — это законно (хаб, карта, меню), поэтому команда сама
        // открывает дев-арену через Sim. Не сложиться это может только если мира нет вовсе: тогда бой
        // открывать некому, и об этом надо сказать прямо.
        private bool SimReady()
        {
            if (Sim != null) return true;
            Debug.LogWarning("[GuildmasterCommands] - арену открыть не удалось: мира нет (standalone-сцена " +
                             "без WorldLifetimeScope) или у мира не задан префаб боевого скоупа.");
            return false;
        }

        private bool FactoryReady()
        {
            if (Factory != null) return true;
            Debug.LogWarning("[GuildmasterCommands] - фабрика юнитов недоступна (см. сообщение об арене выше).");
            return false;
        }

        private RelicData DevRelic(string id)
        {
            if (_content != null && _content.TryGet(id, out RelicData relic) && relic != null) return relic;
            Debug.LogError($"[GuildmasterCommands] - мементо '{id}' нет в контент-БД → срез не запущен");
            return null;
        }

        private RuntimeUnit MakeDummy(int team, Vector2 pos) => Factory.Create(_dummyEnemy, null, team, pos);
    }
}
