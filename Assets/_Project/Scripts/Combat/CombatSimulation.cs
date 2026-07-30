using System;
using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Детерминированная тиковая симуляция боя. Реализует <see cref="ICombatContext"/>
    /// — единственная точка мутации состояния боя из систем и (Фаза 2) компонентов эффектов.
    /// <para>
    /// Порядок систем за тик: ApplyCommands → Brain (AI) → Ability → Movement → Displacement → Separation
    /// → SpatialHashRebuild → AutoAttack → Projectiles → Regen → Effects → <b>ResolveCombatRounds</b>
    /// (применение урона/лечения + доставка событий, раундами) → <b>CommitEffects</b> → Death →
    /// CheckOutcome → currentTick++.
    /// </para>
    /// <para>
    /// <b>Урон и лечение применяются разом.</b> Системы за тик только ЗАЯВЛЯЮТ их (расчёт чист: статы
    /// заморожены законом видимости), а <see cref="TickLedger"/> складывает заявки по каждой цели и
    /// применяет одним коммитом: щит поглощает сумму, дельты HP складываются, доли делятся между
    /// источниками пропорционально. Пока удары ложились в HP по одному, лечение упиралось в потолок
    /// раньше, чем приходил предназначенный цели урон, и одинаковые стороны расходились на пустом месте.
    /// </para>
    /// <para>
    /// <b>Закон видимости эффектов:</b> наложенный эффект меняет статы и маску тегов носителя не раньше
    /// шага CommitEffects, то есть со следующего тика — как это давно сделано для флагов контроля.
    /// Исключение — pre-damage реактивы («Оплот» ловит тот же удар, что его разбудил). Без этого закона
    /// эффект, наложенный ранним по списку юнитом, менял расчёты тех, кто идёт позже, и порядок обхода
    /// становился игровым преимуществом (см. <c>EffectSystem.CommitTickChanges</c>).
    /// </para>
    /// (вики «10» §5.1).
    /// </summary>
    public sealed class CombatSimulation : ICombatContext, IBattleView, ITickLedgerSink
    {
        private readonly IRngService         _rng;
        private readonly float               _armorK;
        // Не readonly: persist-мир сменяет арену на месте (тест-зона ↔ боевая) через SetArena, без
        // пересоздания сима (единая живущая сцена, вики «16» §5 / план persist-геймплея).
        private ArenaBounds                  _arena;
        // Зона деспавна снарядов = видимая область камеры (CameraZone) + margin: снаряд гаснет ЗА
        // пределами видимого игроку, а не на краю арены (решение Макса). Фолбэк — границы арены.
        private ArenaBounds                  _projectileDespawnBounds;
        // Не readonly: dev re-bake (gm_tuning_rebake) применяет новый тюнинг к идущему бою (tainted).
        private SimTuning                    _tuning;
        private readonly SpatialHash         _spatialHash;
        private readonly BrainSystem         _brainSystem;
        private readonly AbilitySystem       _abilitySystem;
        private readonly MovementSystem      _movementSystem;
        private readonly AutoAttackSystem    _autoAttackSystem;
        private readonly ProjectileSystem    _projectileSystem;
        private readonly DeathSystem         _deathSystem;
        private readonly EffectSystem        _effectSystem;
        private readonly RegenSystem         _regenSystem;
        private readonly DisplacementSystem  _displacementSystem;

        // Разделение тел — локальное избегание (вики «10» §5.1). Не инъектится: без конфигурации,
        // читает тюнеры из SimConstants; внутренний инстанс, чтобы не ломать позиционные тест-конструкторы.
        private readonly SeparationSystem    _separationSystem = new SeparationSystem();

        private readonly List<RuntimeUnit>  _units       = new List<RuntimeUnit>();
        private readonly List<RuntimeUnit>  _pendingAdd  = new List<RuntimeUnit>();

        // Жизнь призванных тел (M10). Создаётся здесь, а не приходит извне: система без зависимостей и без
        // состояния, а обязательный параметр конструктора заставил бы каждый существующий вызов (десятки
        // тестов и бенчей) таскать её ради механики, которой в них нет. Осознанный компромисс.
        private readonly SummonSystem _summonSystem = new SummonSystem();

        // Фабрика призывов: подаётся снаружи (BindSummonFactory). null = в этом бою призывать нечем.
        private ISummonFactory _summonFactory;
        private readonly List<Projectile>   _projectiles = new List<Projectile>();
        private readonly List<ICombatCommand> _commandQueue = new List<ICombatCommand>();
        private readonly Queue<CombatEventData> _eventQueue = new Queue<CombatEventData>();

        // Системный эффект-маркер «в полёте» (смещение — это ЭФФЕКТ, вики §4.4): вешается на цель смещения,
        // несёт жёсткий контроль + тег KnockUp, снимается в конце полёта → единый EffectExpired. Neutral →
        // длительность не скейлится ReceiveDebuffEff (единственное исключение смещения). Строится в коде,
        // чтобы не тащить системный .asset в скоуп/тесты.
        private readonly Data.Definitions.EffectData _airborneEffect =
            Data.Definitions.EffectData.CreateRuntime(
                "sys.airborne",
                Data.Definitions.EffectPolarity.Neutral,
                Data.Definitions.EffectTag.KnockUp | Data.Definitions.EffectTag.Control,
                baseDuration: -1f,          // постоянный: снимает DisplacementSystem в конце полёта
                unremovable: true,          // диспелом не снять — полёт всегда доигрывается
                new Effects.Components.ControlComponent(preventAct: true, preventMove: true, preventCast: true));

        // Защита от бесконечной реентрантности реактивных компонентов (шипы↔шипы): дренаж
        // капается детерминированно — остаток отбрасывается (вики «12» §3.4, спайк S2).
        private const int MaxEventsPerDrain = 512;

        // Сколько раз за тик урон может «отскочить» реактивом. Шипы бьют в ответ шипам, вампиризм
        // лечит от ответки — каждый круг это новая заявка в реестр. Хвост таких цепочек обрезает порог
        // значимости (TickLedger.MinSignificantAmount): при 25% отражения ряд гаснет за семь кругов,
        // при 50% — за одиннадцать. Кап — страховка от контента с отражением под 90%, где ряд затухает
        // слишком медленно, чтобы досчитывать его в одном тике.
        private const int MaxCombatRounds = 16;

        // Реестр урона и лечения за раунд: заявки копятся и применяются одним коммитом (tick-resolution).
        private readonly TickLedger _ledger = new TickLedger();

        // Заявленные переходы «за спину» и посчитанные для них точки — применяются в конце раунда.
        private readonly List<(RuntimeUnit unit, RuntimeUnit target)> _pendingTeleports = new();
        private readonly List<Vector2> _teleportDestinations = new();

        private int           _currentTick;
        private bool          _isPaused;
        private BattleOutcome _outcome = BattleOutcome.Ongoing;
        private int           _nextProjectileId;

        // Хотя бы один юнит был заспавнен. До этого CheckOutcome не завершает бой (иначе
        // пустой стартовый кадр сразу дал бы Draw); юниты из _units не удаляются (только IsDead),
        // поэтому после первого спавна список непуст до конца боя.
        private bool          _hasSpawned;

        // --- События для Presentation и Game-слоя ---

        /// <summary>Юнит появился в симуляции.</summary>
        public event Action<RuntimeUnit> OnUnitSpawned;

        /// <summary>Юнит погиб.</summary>
        public event Action<RuntimeUnit> OnUnitDied;

        /// <summary>Нанесён урон: источник, цель, результат. Совпадает с кадром контакта (конец замаха, вики «14»).</summary>
        public event Action<RuntimeUnit, RuntimeUnit, DamageResult> OnDamageDealt;

        /// <summary>Юнит исцелён: источник, цель, фактически вылеченное HP (overheal не входит). Для presentation (хил-цифры).</summary>
        public event Action<RuntimeUnit, RuntimeUnit, float> OnHealed;

        /// <summary>Входящий удар полностью отменён pre-damage реактивом («Изворотливость»). Для presentation («evade»). Урона нет.</summary>
        public event Action<RuntimeUnit> OnAttackEvaded;

        /// <summary>Юнит вошёл в замах авто-атаки (вики «14»): запускает анимацию свинга во View.</summary>
        public event Action<RuntimeUnit, RuntimeUnit> OnAttackStarted;

        /// <summary>Замах авто-атаки прерван (стан/смерть себя): View рвёт свинг в idle (вики «14»).</summary>
        public event Action<RuntimeUnit> OnAttackInterrupted;

        /// <summary>Бой завершён с итогом.</summary>
        public event Action<BattleOutcome> OnBattleEnded;

        /// <summary>Бой сброшен для перезапуска на месте (R): презентация чистит виды/оверлеи. Сцена НЕ перезагружается.</summary>
        public event Action OnBattleReset;

        /// <summary>Зона удара сработала (линия авто-атаки / круг активки) — для dev-оверлея зон.</summary>
        public event Action<AreaHit> OnAreaHit;

        /// <summary>
        /// Снаряд создан — презентация заводит вид (Bullet), держит ссылку на этот <see cref="Projectile"/>
        /// и следует за ним интерполяцией. При попадании сим ставит <c>Position</c> = точка удара и
        /// <c>IsAlive = false</c> в тот же тик, что и <see cref="OnDamageDealt"/> — вид снапается туда и гаснет,
        /// поэтому визуальный импакт и реальный эффект совпадают (без рассинхрона).
        /// </summary>
        public event Action<Projectile> OnProjectileSpawned;

        // --- ICombatContext ---

        public IRngService Rng         => _rng;
        public int         CurrentTick => _currentTick;
        public float       ArmorK      => _armorK;
        public SimTuning   Tuning      => _tuning;

        /// <summary>Границы боевого поля этого боя (для презентации/оверлея; клампинг — внутри систем).</summary>
        public ArenaBounds Arena       => _arena;

        /// <summary>Система разделения тел — доступ для dev-тюнинга параметров в рантайме (gm_sep_*).</summary>
        public SeparationSystem Separation => _separationSystem;

        public IReadOnlyList<RuntimeUnit> Units    => _units;

        /// <summary>Живые снаряды — их снимает лента боя, чтобы показ не летел по будущему.</summary>
        public IReadOnlyList<Projectile>  Projectiles => _projectiles;
        public BattleOutcome              Outcome  => _outcome;
        public bool                       IsPaused => _isPaused;

        /// <summary>Сколько боевого времени прошло, сек. Идёт по симуляционным тикам, а не по стенным часам,
        /// поэтому пауза и slowmo его не искажают. Основа для боевого таймера в HUD.</summary>
        public float ElapsedSeconds => _currentTick * SimConstants.TickDelta;

        public CombatSimulation(
            IRngService       rng,
            float             armorK,
            SpatialHash       spatialHash,
            BrainSystem       brainSystem,
            AbilitySystem     abilitySystem,
            MovementSystem    movementSystem,
            AutoAttackSystem  autoAttackSystem,
            ProjectileSystem  projectileSystem,
            DeathSystem       deathSystem,
            EffectSystem      effectSystem,
            RegenSystem       regenSystem,
            DisplacementSystem displacementSystem = null,
            ArenaBounds?      arena              = null,
            SimTuning?        tuning             = null,
            Rect2D?           cameraZone         = null)
        {
            _rng              = rng;
            _armorK           = armorK;
            // null → бесконечное поле (headless-тесты, бой без заданной арены). НЕ default(ArenaBounds).
            _arena            = arena ?? ArenaBounds.Unbounded;
            // Деспавн снарядов — по видимой зоне камеры; не задана → границы арены (в т.ч. Unbounded).
            _projectileDespawnBounds = new ArenaBounds(cameraZone ?? _arena.Rect);
            // null → код-дефолты (headless-тесты). Боевой скоуп печёт снапшот из SimTuningConfig.
            _tuning           = tuning ?? SimTuning.Default;
            PushSeparationTuning();
            _spatialHash      = spatialHash;
            _brainSystem      = brainSystem;
            _abilitySystem    = abilitySystem;
            _movementSystem   = movementSystem;
            _autoAttackSystem = autoAttackSystem;
            _projectileSystem = projectileSystem;
            _deathSystem      = deathSystem;
            _effectSystem     = effectSystem;
            _regenSystem      = regenSystem;
            // Опциональный (дефолт — свежий): headless-тесты конструируют симуляцию позиционно без него.
            _displacementSystem = displacementSystem ?? new DisplacementSystem();

            // Полёт завершился → снимаем маркер «в полёте» с цели. Снятие эффекта само поднимет единый
            // EffectExpired (через OnEffectExpired ниже) — отдельного UnitDisplaced больше нет.
            _displacementSystem.OnDisplacementEnded += (source, target) =>
            {
                if (target != null)
                    _effectSystem.RemoveByTag(target, Data.Definitions.EffectTag.KnockUp, this);
            };

            // Единый шов «эффект закончился»: ретранслируем в EffectExpired, носитель-получатель = источник
            // эффекта (напр. монах — источник и рывка, и отбрасывания). Реактивы фильтруют по тегам + команде.
            _effectSystem.OnEffectExpired += (unit, source, tags) =>
            {
                if (source != null)
                    _eventQueue.Enqueue(new CombatEventData(CombatEvent.EffectExpired, source, unit, 0f, tags));
            };

            _deathSystem.OnUnitDied += unit =>
            {
                // Внутреннее событие для реактивов (перенос «Метки охотника», §9.5). Дренится следующим
                // тиком; носитель-труп ещё в _units с эффектами, DrainEventQueue допускает его для UnitDied.
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.UnitDied, null, unit, 0f));
                OnUnitDied?.Invoke(unit);
            };
        }

        // Сепарация тюнится вживую (gm_sep_*), её публичные поля не readonly — засеваем снапшотом здесь.
        private void PushSeparationTuning()
        {
            _separationSystem.BodyRadiusPerSize = _tuning.BodyRadiusPerSize;
            _separationSystem.Strength          = _tuning.SeparationStrength;
            _separationSystem.Iterations        = _tuning.SeparationIterations;
            _separationSystem.SameTeamScale     = _tuning.SeparationSameTeamScale;
        }

        /// <summary>
        /// Dev re-bake (QC, gm_tuning_rebake): применить новый снапшот тюнинга к идущему бою. Бой становится
        /// TAINTED — реплей невалиден (вики «13» §4.1). В обычном потоке снапшот неизменен с старта боя.
        /// </summary>
        public void RebakeTuning(SimTuning tuning)
        {
            _tuning = tuning;
            PushSeparationTuning();
        }

        // --- Основной тик ---

        /// <summary>
        /// Выполнить один детерминированный шаг симуляции.
        /// <paramref name="dt"/> всегда равен <see cref="SimConstants.TickDelta"/>.
        /// </summary>
        public void Tick(float dt)
        {
            if (_outcome != BattleOutcome.Ongoing) return;

            FlushPendingSpawns();

            // Пауза, применённая ЭТИМ тиком, вступает в силу со следующего:
            // текущий тик ещё досимулировывается. Поэтому фиксируем состояние ДО команд.
            bool pausedBeforeCommands = _isPaused;
            ApplyDueCommands();

            if (_isPaused && pausedBeforeCommands)
            {
                // Системы стоят, но счётчик тиков продолжает идти, ПОКА в очереди есть
                // команды — иначе ResumeCommand с будущим TargetTick никогда не наступит
                // и бой залипнет в паузе навсегда.
                if (_commandQueue.Count > 0) _currentTick++;
                return;
            }

            // Снимок дееспособности на начало тика: по нему гейтятся реактивы, которым нужно ДЕЙСТВИЕ
            // носителя. Живой CanAct для этого не годится — он пересчитывается синхронно при наложении
            // контроля, то есть меняется посреди тика, и реакция начинала зависеть от порядка юнитов в
            // списке (зеркальные команды расходились). См. RuntimeUnit.CanActAtTickStart.
            for (int i = 0; i < _units.Count; i++) _units[i].CanActAtTickStart = _units[i].CanAct;

            _brainSystem.Tick(_units, this);
            _abilitySystem.Tick(_units, this, dt);
            _movementSystem.Tick(_units, dt, in _arena, in _tuning);
            _displacementSystem.Tick(this, dt, in _arena);
            _separationSystem.Tick(_units, _spatialHash, in _arena);
            _spatialHash.Rebuild(_units);
            // Блинк убийцы двигает тело уже ПОСЛЕ перестройки хэша — тогда сетка врёт до конца тика,
            // и соседи находят блинкнувшего по-разному с двух сторон. Перестраиваем повторно, но только
            // если сдвиг реально был: в обычном тике это лишняя работа.
            if (_autoAttackSystem.Tick(_units, this, dt)) _spatialHash.Rebuild(_units);
            _projectileSystem.Tick(_projectiles, _units, this, dt, in _projectileDespawnBounds);
            _regenSystem.Tick(_units, dt);
            _effectSystem.Tick(_units, this, dt);
            ResolveCombatRounds();
            // Закон видимости эффектов: всё, что наложено и снято за этот тик, проявляется здесь — одним
            // проходом на всех. До этой точки статы и маска тегов отдают состояние, с которым тик начался,
            // поэтому исход не зависит от того, чей ход в обходе списка раньше. Место — после дренажа
            // (реактивы успевают наложить своё) и до смерти (пересчёт на трупах не нужен).
            _effectSystem.CommitTickChanges(_units);
            // Срок жизни призывов и уход вместе с хозяином — ДО смерти: развеянный призыв обязан умереть
            // тем же проходом, что все остальные, иначе он исчез бы без события смерти.
            _summonSystem.Tick(_units);
            _deathSystem.Tick(_units, _spatialHash);

            CheckOutcome();
            _currentTick++;
        }

        // --- ICombatContext ---

        public void DealDamage(in DamageRequest request)
        {
            if (request.Target.IsDead) return;

            // Усиление ИСТОЧНИКА за состояние цели (Криомант больнее бьёт замороженных). Считается до
            // расщепления, поэтому прибавка достаётся обеим половинам удара: это свойство удара, а не
            // школы. Уязвимость цели («Угли») живёт отдельно, в pre-damage — путать их нельзя, иначе
            // один множитель начнёт отвечать за два разных факта.
            DamageRequest req = ApplyOutgoingBonus(in request);

            // Расщепление авто-атаки по школам (The Pyre: по горящей цели половина клинка бьёт Огнём).
            // Живёт здесь, а не в AutoAttackSystem, чтобы одинаково работать для мили, линии и снаряда.
            // Половинки уходят в Core напрямую — иначе огненная половина расщепилась бы снова.
            if (req.SourceKind == DamageSourceKind.AutoAttack && req.Source != null
                && _effectSystem.TryResolveAttackSplit(req.Source, req.Target, this, out AttackSplit split)
                && split.Share > 0f)
            {
                // Отщеплённая часть либо забирает долю удара (суммарный урон сохраняется), либо приходит
                // со своей величиной — процентом от макс. HP цели у Мечника. Во втором случае клинок всё
                // равно теряет свою долю: половина стали + процентный огонь, а не полный удар плюс огонь.
                float removed = req.RawDamage * split.Share;
                float splitDamage = split.HasOwnDamage ? split.OwnDamage : removed;

                DealDamageCore(new DamageRequest(req.Source, req.Target, req.RawDamage - removed,
                    req.School, req.ArmorK, req.SourceKind, req.Affinity, req.Element,
                    subtype: req.Subtype));
                if (!req.Target.IsDead)
                    DealDamageCore(new DamageRequest(req.Source, req.Target, splitDamage,
                        split.School, req.ArmorK, req.SourceKind, req.Affinity, split.Element,
                        subtype: req.Subtype));
                return;
            }

            DealDamageCore(in req);
        }

        /// <summary>
        /// Домножить удар на прибавки, которые даёт носителю его собственные эффекты за состояние цели.
        /// Возвращает исходный запрос без копии, когда прибавок нет — это горячий путь каждого удара.
        /// </summary>
        private DamageRequest ApplyOutgoingBonus(in DamageRequest req)
        {
            float bonus = _effectSystem.ResolveOutgoingDamageBonus(
                req.Source, req.Target, req.SourceKind == DamageSourceKind.AutoAttack, this);
            if (bonus <= 0f) return req;

            return new DamageRequest(
                req.Source, req.Target, req.RawDamage * (1f + bonus), req.School, req.ArmorK,
                req.SourceKind, req.Affinity, req.Element, req.Vulnerability, req.BonusFlatPen,
                req.Subtype);
        }

        private void DealDamageCore(in DamageRequest req)
        {
            if (req.Target.IsDead) return;

            // Синхронный pre-damage перехват (§9.3): «Оплот» поднимает щит (поглотит этот же удар),
            // «Изворотливость» может полностью отменить удар. Порядок детерминирован.
            if (_effectSystem.RunPreDamage(req.Target, in req, this))
            {
                OnAttackEvaded?.Invoke(req.Target); // presentation-сигнал «evade», симуляцию не трогает
                return; // удар негейтнут — ни урона, ни урон-событий
            }

            // Уязвимости цели, накопленные тем же проходом («Угли» усиливают огонь по подожжённому).
            // Домножаем сырой урон ДО пайплайна: это свойство ЦЕЛИ, а не пробивание источника.
            float vulnerability = _effectSystem.PreDamageMultiplier;

            // Овертайм: правило анти-затягивания. Урон растёт со временем боя, лечение и щиты — нет,
            // поэтому клинч «никто никого не пробивает» разваливается сам. Множитель общий для обеих
            // сторон: иначе овертайм наказывал бы защищающегося вместо того, чтобы форсировать развязку.
            // В уязвимость его не складываем — Vuln% в отчётах должен остаться про «Угли», а не про таймер.
            float overtime = _tuning.OvertimeDamageMultiplier(ElapsedSeconds);

            float scale = vulnerability * overtime;
            DamageRequest effective = scale == 1f
                ? req
                : new DamageRequest(req.Source, req.Target, req.RawDamage * scale, req.School,
                                    req.ArmorK, req.SourceKind, req.Affinity, req.Element, vulnerability,
                                    subtype: req.Subtype);

            // Урон считается сразу (расчёт чист и от порядка не зависит — статы заморожены на тик),
            // а применяется реестром, когда сложатся все удары раунда. См. TickLedger.
            float dealt = DamagePipeline.Resolve(effective, out float mitigated);
            _ledger.AddDamage(req.Target, dealt, mitigated, in effective);
        }

        /// <summary>
        /// Урон дошёл до цели — доля этого источника в общем ударе раунда. Здесь и только здесь урон
        /// превращается в события: наружу для презентации и метрик, внутрь для реактивов.
        /// </summary>
        void ITickLedgerSink.OnDamageResolved(RuntimeUnit source, RuntimeUnit target, in DamageResult result)
        {
            OnDamageDealt?.Invoke(source, target, result);

            // Внутренние события для реактивных компонентов (vampiric/thorns). Два события на удар:
            // DamageDealt доставляется источнику, DamageTaken — цели (вики «12» §3.4).
            if (source != null)
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageDealt, source, target, result.TotalDamage, Data.Definitions.EffectTag.None, result.SourceKind, result.School, result.Element));
            _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageTaken, source, target, result.TotalDamage, Data.Definitions.EffectTag.None, result.SourceKind, result.School, result.Element));

            // Убийство атрибутируется наибольшей доле урона (решение 2026-07-27) → доставляется УБИЙЦЕ
            // (§10.5, «Скрытность»). Реестр уже выбрал владельца — здесь только разносим.
            if (result.KilledTarget && source != null)
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.UnitKilled, source, target, 0f));

            // Вампиризм: источник лечится на долю нанесённого по HP урона. Заявка ложится в реестр и
            // применится следующим раундом этого же тика — как и ответка шипов.
            if (source != null && !source.IsDead && result.HpDamage > 0f)
            {
                float lifesteal = source.Stats.Get(Data.Stats.StatType.Lifesteal);
                if (lifesteal > 0f) Heal(source, result.HpDamage * lifesteal, source);
            }
        }

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source)
        {
            if (target.IsDead) return;

            // Потенция исцеления масштабируется парой HealShield-эффективностей (вики «11» §5).
            // Кламп по MaxHP делает реестр — он один знает, сколько урона придёт по цели этим раундом.
            float mult = (source != null ? source.Stats.Get(Data.Stats.StatType.HealShieldDealtEff) : 1f)
                       * target.Stats.Get(Data.Stats.StatType.HealShieldTakenEff);

            _ledger.AddHeal(target, amount * mult, source);
        }

        /// <summary>
        /// Лечение дошло до цели — доля этого источника в фактически вылеченном (overheal не входит).
        /// </summary>
        void ITickLedgerSink.OnHealResolved(RuntimeUnit source, RuntimeUnit target, float applied)
        {
            if (applied <= 0f) return;

            // Событие для on-heal реактивных компонентов (носитель — source, как и DamageDealt).
            _eventQueue.Enqueue(new CombatEventData(CombatEvent.Healed, source, target, applied));
            OnHealed?.Invoke(source, target, applied);
        }

        public void SpawnProjectile(in ProjectileSpawn spawn)
        {
            Vector2 velocity = spawn.TargetUnit != null
                ? (spawn.TargetUnit.Position - spawn.StartPosition).normalized * spawn.Speed
                : Vector2.right * spawn.Speed;

            var projectile = new Projectile
            {
                Id               = _nextProjectileId++,
                Source           = spawn.Source,
                Position         = spawn.StartPosition,
                PreviousPosition = spawn.StartPosition,
                Velocity         = velocity,
                CollisionRadius  = spawn.CollisionRadius,
                TargetUnit       = spawn.TargetUnit,
                RawDamage        = spawn.RawDamage,
                School           = spawn.School,
                Affinity         = spawn.Affinity,
                ArmorK           = spawn.ArmorK,
                PiercesRemaining = spawn.MaxPierces,
                IsHeal           = spawn.IsHeal,
                OnHitEffects     = spawn.OnHitEffects,
                IsAutoAttack     = spawn.IsAutoAttack,
                IsAlive          = true,
            };

            // Бронь входящих тегов на трекинг-цель: пока снаряд с on-hit эффектами летит, таргетинг
            // (PreferTagged/PreferUntagged) считает цель уже «тегнутой» → крио не шлёт вторую «Заморозку»
            // в того же врага. Снимается в ProjectileSystem при разрешении снаряда (попал/деспавн).
            if (spawn.TargetUnit != null && spawn.OnHitEffects != null)
            {
                Data.Definitions.EffectTag reserved = 0;
                for (int e = 0; e < spawn.OnHitEffects.Length; e++)
                    if (spawn.OnHitEffects[e] != null) reserved |= spawn.OnHitEffects[e].Tags;

                projectile.ReservedTags = reserved;
                spawn.TargetUnit.AddIncomingEffect(reserved);
            }

            _projectiles.Add(projectile);
            OnProjectileSpawned?.Invoke(projectile); // презентация заведёт Bullet и будет следовать за ссылкой
        }

        public int QueryUnitsInRadius(
            Vector2 center,
            float radius,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam)
        {
            _spatialHash.QueryRadius(center, radius, results);

            if (filter != TargetFilter.All)
            {
                for (int i = results.Count - 1; i >= 0; i--)
                {
                    bool isAlly = results[i].Team == requestingTeam;
                    if (filter == TargetFilter.Enemies &&  isAlly) results.RemoveAt(i);
                    if (filter == TargetFilter.Allies  && !isAlly) results.RemoveAt(i);
                }
            }

            return results.Count;
        }

        public int QueryUnitsInLine(
            Vector2 origin,
            Vector2 direction,
            float length,
            float width,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam)
        {
            // Broad-phase: круг радиусом = length + halfWidth (живых отбирает QueryRadius), затем
            // narrow-phase по геометрии полосы: проекция на направление в [0..length], перпендикуляр ≤ width/2.
            // Радиус ДОЛЖЕН включать halfWidth: юнит у дальнего угла полосы лежит на расстоянии
            // √(length² + halfWidth²) > length от origin — с радиусом length он отсекался бы до
            // narrow-phase (07 §3.8 B5). length + halfWidth — консервативный супермножество-радиус.
            float halfWidth = width * 0.5f;
            _spatialHash.QueryRadius(origin, length + halfWidth, results);

            Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;

            for (int i = results.Count - 1; i >= 0; i--)
            {
                RuntimeUnit u = results[i];
                Vector2 v = u.Position - origin;
                float along = Vector2.Dot(v, dir);
                bool inLine = along >= 0f && along <= length && (v - along * dir).magnitude <= halfWidth;

                bool isAlly = u.Team == requestingTeam;
                bool teamOk = filter == TargetFilter.All
                           || (filter == TargetFilter.Enemies && !isAlly)
                           || (filter == TargetFilter.Allies && isAlly);

                if (!inLine || !teamOk) results.RemoveAt(i);
            }

            return results.Count;
        }

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source)
        {
            _effectSystem.Apply(target, def, source, this);
        }

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds)
        {
            _effectSystem.Apply(target, def, source, this, durationSeconds);
        }

        public void ReportAreaHit(in AreaHit hit) => OnAreaHit?.Invoke(hit);

        public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) => OnAttackStarted?.Invoke(unit, target);

        public void NotifyAttackInterrupted(RuntimeUnit unit) => OnAttackInterrupted?.Invoke(unit);

        public void Dispel(in Effects.DispelRequest req)
        {
            _effectSystem.Dispel(in req, this);
        }

        /// <summary>
        /// Каст объявлен: событие уходит в общую очередь, а разослать его врагам — работа дренажа
        /// (см. <see cref="DrainEventQueue"/>). Здесь оно ставится ОДНОЙ записью, потому что состав живых
        /// врагов должен читаться в момент доставки, а не в момент заявки.
        /// </summary>
        public void ReportAbilityCast(RuntimeUnit caster)
        {
            if (caster == null || caster.IsDead) return;

            _eventQueue.Enqueue(new CombatEventData(CombatEvent.AbilityCast, caster, caster, 0f));
        }

        /// <summary>Заявка на переход за спину — применяется в конце раунда, см. <see cref="ApplyPendingTeleports"/>.</summary>
        public void TeleportBehind(RuntimeUnit unit, RuntimeUnit target)
        {
            if (unit == null || target == null || unit.IsDead || target.IsDead) return;
            _pendingTeleports.Add((unit, target));
        }

        /// <summary>
        /// Применить заявленные переходы: сначала считаем ВСЕ новые позиции от снимка мира, затем
        /// записываем. Иначе второй телепорт целился бы за спину тела, которое только что уехало.
        /// </summary>
        /// <returns>true, если хоть кто-то переместился (вызывающий обязан перестроить хэш).</returns>
        private bool ApplyPendingTeleports()
        {
            if (_pendingTeleports.Count == 0) return false;

            _teleportDestinations.Clear();
            for (int i = 0; i < _pendingTeleports.Count; i++)
            {
                (RuntimeUnit unit, RuntimeUnit target) = _pendingTeleports[i];
                _teleportDestinations.Add(CombatPositioning.BehindPosition(unit, target));
            }

            for (int i = 0; i < _pendingTeleports.Count; i++)
            {
                RuntimeUnit unit = _pendingTeleports[i].unit;
                if (unit.IsDead) continue;

                // Снап без интерполяции: вид не должен «ехать» через экран за один тик.
                unit.Position = _teleportDestinations[i];
                unit.PreviousPosition = unit.Position;
            }

            _pendingTeleports.Clear();
            return true;
        }

        public void Displace(in DisplaceRequest req)
        {
            // Смещение — это ЭФФЕКТ: вешаем маркер «в полёте» (жёсткий контроль + тег KnockUp, длительность
            // не скейлится — Neutral) на цель, затем отдаём траекторию DisplacementSystem. Маркер снимается
            // в конце полёта (OnDisplacementEnded → RemoveByTag) и поднимает единый EffectExpired.
            if (req.Target != null && !req.Target.IsDead)
                _effectSystem.Apply(req.Target, _airborneEffect, req.Source, this);

            // Урон толчка по САМОЙ отброшенной цели (решение 2026-07-28): раньше заданный урон уходил
            // только тем, кого задело «ядром» на линии, — то есть толчок бил мимо того, кого толкнули.
            // Самосмещение (рывок кастующего) не бьёт себя, цепь идёт с нулевым уроном и тоже молчит.
            if (req.Damage > 0f && req.Source != null && req.Target != null && !req.Target.IsDead
                && !ReferenceEquals(req.Source, req.Target))
            {
                DealDamage(new DamageRequest(req.Source, req.Target, req.Damage, req.School, ArmorK,
                    affinity: req.Affinity));
            }

            _displacementSystem.Add(in req, in _tuning);
        }

        // --- Управление симуляцией (вызывается командами) ---

        /// <summary>
        /// Заморозить симуляцию СЦЕНАРИЕМ — расстановка, передышка, тест-полигон. Детерминированно: пауза
        /// вступает со следующего тика, а счётчик тиков продолжает идти, пока в очереди есть команды.
        /// <para>Это НЕ пауза игрока. Ту держит <c>TimeScaleService</c> (Time.timeScale), и она
        /// останавливает сим косвенно — через обнулённый <c>Time.deltaTime</c>, из которого
        /// <c>CombatLoopService</c> копит тики. Два разных факта с похожими именами: сведение их в один
        /// ломает и расстановку, и тумблер Space (аудит 2026-07-26, T-4).</para>
        /// </summary>
        public void SetPaused(bool paused) => _isPaused = paused;

        /// <summary>
        /// Сменить арену НА МЕСТЕ, без пересоздания сима (persist-мир: тест-зона ↔ боевая арена).
        /// Обновляет границы поля (движение/отбрасывание) и зону деспавна снарядов. Звать на пустом
        /// или сброшенном симе (между боями / на входе в бой), не посреди активного тика.
        /// </summary>
        public void SetArena(ArenaBounds arena, Rect2D? cameraZone = null)
        {
            _arena = arena;
            // Как в конструкторе: деспавн снарядов — по видимой зоне камеры; не задана → границы арены.
            _projectileDespawnBounds = new ArenaBounds(cameraZone ?? _arena.Rect);
        }

        /// <summary>
        /// Сбросить бой для перезапуска НА МЕСТЕ (dev-R): чистим всех юнитов/снаряды/очереди и возвращаем
        /// исход в <see cref="BattleOutcome.Ongoing"/>. Сцена/камера НЕ трогаются — их не перезагружаем.
        /// Тик-цикл (<see cref="CombatLoopService"/>) простаивает, пока не-Ongoing, и сам возобновится после сброса.
        /// Дальше вызывающий заново расставляет бой (тот же сетап) через <see cref="EnqueueUnitSpawn"/>.
        /// </summary>
        public void ResetBattle()
        {
            OnBattleReset?.Invoke(); // презентация снимает виды/оверлеи до очистки сим-состояния

            _units.Clear();
            _pendingAdd.Clear();
            _projectiles.Clear();
            _eventQueue.Clear();
            _commandQueue.Clear();
            _ledger.Clear();              // незакрытые заявки урона/лечения не должны пережить бой
            _pendingTeleports.Clear();    // как и незакрытые заявки на переход
            _displacementSystem.Clear();  // незавершённые полёты не должны держать ссылки на удалённых юнитов
            _spatialHash.Rebuild(_units); // пустой список → хэш очищен

            _outcome          = BattleOutcome.Ongoing;
            _hasSpawned       = false;
            _isPaused         = false;
            _currentTick      = 0;
            _nextProjectileId = 0;
        }

        /// <summary>Поставить юнита в очередь добавления (не в _units напрямую, чтобы не нарушить итерацию).</summary>
        public void EnqueueUnitSpawn(RuntimeUnit unit) => _pendingAdd.Add(unit);

        /// <summary>
        /// Подать фабрику призывов (M10). Разводится снаружи — сборка юнитов из SO живёт вне боевого ядра,
        /// и бою нужен из неё ровно один метод. Отдельный вызов, а не параметр конструктора: бой без
        /// призывов полностью рабочий (балансные бенчи задают состав заранее), и обязательная зависимость
        /// заставила бы каждый прогон таскать фабрику ради механики, которой в нём нет.
        /// </summary>
        public void BindSummonFactory(ISummonFactory factory) => _summonFactory = factory;

        /// <summary>Призвать тело в бой: собрать по киту и поставить в очередь спавна (см. ICombatContext).</summary>
        public RuntimeUnit Summon(
            Data.Definitions.UnitData data, int team, Vector2 position, RuntimeUnit summoner)
        {
            if (data == null || _summonFactory == null) return null;

            RuntimeUnit summon = _summonFactory.CreateSummon(data, team, position, summoner);
            if (summon == null) return null;

            EnqueueUnitSpawn(summon);
            return summon;
        }

        // --- Очередь команд ---

        /// <summary>Добавить команду в отсортированную очередь.</summary>
        public void EnqueueCommand(ICombatCommand command)
        {
            int insertIdx = _commandQueue.Count;
            for (int i = 0; i < _commandQueue.Count; i++)
            {
                if (_commandQueue[i].TargetTick > command.TargetTick)
                {
                    insertIdx = i;
                    break;
                }
            }
            _commandQueue.Insert(insertIdx, command);
        }

        // --- Расчёт checksum для SimSyncProbe ---

        /// <summary>
        /// Детерминированный слепок состояния симуляции: тик, состояние RNG, юниты (позиция, HP, щит,
        /// ресурс, фаза атаки), их активные эффекты и снаряды в полёте.
        /// Используется <see cref="Net.SimSyncProbe"/> для проверки рассинхрона.
        /// </summary>
        /// <remarks>
        /// Эффекты, щит, ресурс и снаряды добавлены по аудиту 2026-07-26 (RC-8). Прежний слепок брал
        /// позицию, HP и тайминги атаки — то есть расхождение, начавшееся в эффектах (яд тикнул у хоста
        /// и не тикнул у клиента, стак обновился по-разному, снаряд разошёлся траекторией), становилось
        /// видно только когда оно доедет до HP, а к тому моменту причина уже далеко позади. Дёшево:
        /// перебор без аллокаций, зовётся по требованию пробы, а не каждый тик.
        /// </remarks>
        public ulong ComputeChecksum()
        {
            ulong hash = (ulong)_currentTick * 2654435761UL;
            hash ^= _rng.Snapshot();

            for (int i = 0; i < _units.Count; i++)
            {
                RuntimeUnit u = _units[i];
                // Каст float→long ДО ulong: прямой каст отрицательного float в ulong
                // даёт 0/неопределённость, и разные отрицательные координаты схлопываются.
                hash ^= (ulong)(u.Id * 1000003);
                hash ^= (ulong)(long)(u.Position.x * 1000f) * 2246822519UL;
                hash ^= (ulong)(long)(u.Position.y * 1000f) * 3266489917UL;
                hash ^= (ulong)(long)(u.CurrentHP  * 100f)  * 668265263UL;
                hash ^= (ulong)(long)(u.CurrentShield * 100f) * 2166136261UL;
                hash ^= (ulong)(long)(u.CurrentResource * 100f) * 1099511628211UL;
                hash ^= u.IsDead ? 0x9E3779B97F4A7C15UL : 0UL;
                // Состояние авто-атаки — детерминированное, входит в чек-сумму (вики «14»).
                hash ^= (ulong)(uint)u.AttackCooldownTicks * 374761393UL;
                hash ^= (ulong)(uint)u.WindupRemaining     * 3266489917UL;
                hash ^= (ulong)(uint)u.RecoveryRemaining   * 2654435761UL;

                // Эффекты — главный источник расхождений: длительность, стаки и периодика тикают
                // каждый тик у каждого носителя. Порядок в списке сам детерминирован (наложение идёт
                // из детерминированных систем), поэтому индекс входит в хэш как есть.
                List<Effects.RuntimeEffect> effects = u.ActiveEffects;
                for (int e = 0; e < effects.Count; e++)
                {
                    Effects.RuntimeEffect eff = effects[e];
                    hash ^= (ulong)(uint)(e + 1) * 2654435761UL;
                    hash ^= Core.Random.DeterministicHash.Of(eff.Def != null ? eff.Def.Id : null);
                    hash ^= (ulong)(uint)eff.RemainingTicks * 2246822519UL;
                    hash ^= (ulong)(uint)eff.Stacks         * 668265263UL;
                    hash ^= (ulong)(uint)(eff.Source != null ? eff.Source.Id : 0) * 374761393UL;

                    int[] periodic = eff.PeriodicTicks;
                    if (periodic != null)
                        for (int p = 0; p < periodic.Length; p++)
                            hash ^= (ulong)(uint)periodic[p] * (ulong)(uint)(p + 1) * 3266489917UL;

                    hash = (hash << 7) | (hash >> 57);
                }

                hash = (hash << 13) | (hash >> 51);
            }

            // Снаряды: до попадания они не влияют ни на чьё HP, поэтому разошедшийся полёт был невидим
            // ровно до момента, когда исправить рассинхрон уже поздно.
            for (int i = 0; i < _projectiles.Count; i++)
            {
                Projectile p = _projectiles[i];
                hash ^= (ulong)(uint)p.Id * 1000003UL;
                hash ^= (ulong)(long)(p.Position.x * 1000f) * 2246822519UL;
                hash ^= (ulong)(long)(p.Position.y * 1000f) * 3266489917UL;
                hash ^= (ulong)(uint)p.PiercesRemaining * 668265263UL;
                hash  = (hash << 11) | (hash >> 53);
            }

            return hash;
        }

        /// <summary>
        /// Влить отложенные спавны в живой список БЕЗ тика систем (фаза расстановки, шаг 4): юниты должны
        /// присутствовать и быть видимыми/двигаемыми, пока бой на паузе. Фактически арм <c>_hasSpawned</c>
        /// и <c>OnUnitSpawned</c> (презентация строит виды) — безвредно до первого реального <see cref="Tick"/>.
        /// </summary>
        public void FlushSpawns() => FlushPendingSpawns();

        // --- Приватные ---

        private void FlushPendingSpawns()
        {
            if (_pendingAdd.Count > 0) _hasSpawned = true;

            for (int i = 0; i < _pendingAdd.Count; i++)
            {
                _units.Add(_pendingAdd[i]);
                _spatialHash.Add(_pendingAdd[i]);
                OnUnitSpawned?.Invoke(_pendingAdd[i]);
            }
            _pendingAdd.Clear();
        }

        /// <summary>
        /// Разрешить весь урон и лечение тика — раундами, пока реактивы порождают новые заявки.
        /// </summary>
        /// <remarks>
        /// Один раунд = «применить всё накопленное разом → доставить события». Ответка шипов и лечение
        /// вампиризмом попадают в реестр во время доставки событий, поэтому уезжают в СЛЕДУЮЩИЙ раунд —
        /// и там снова складываются со всем, что пришло вместе с ними. Так ответка остаётся мгновенной
        /// (тот же тик), но ни один круг не зависит от порядка обхода: внутри круга всё одновременно.
        /// <para>
        /// Смерть при этом наступает только в конце тика (<c>DeathSystem</c>), поэтому древень, погибший
        /// в первом раунде, успевает уколоть в ответ во втором — взаимный размен возможен (решение
        /// 2026-07-27).
        /// </para>
        /// </remarks>
        private void ResolveCombatRounds()
        {
            for (int round = 0; round < MaxCombatRounds; round++)
            {
                if (_ledger.HasPending) _ledger.Commit(this);
                else if (_eventQueue.Count == 0) return;

                DrainEventQueue();

                // Переходы «за спину», заявленные реактивами этого круга, применяются здесь — все разом,
                // до того как следующий круг начнёт мерить дистанции.
                if (ApplyPendingTeleports()) _spatialHash.Rebuild(_units);

                if (!_ledger.HasPending) return;
            }

            // Упёрлись в кап: заявки остались. Не обязательно баг — так выглядит контент с очень высокой
            // долей отражения, у которого ряд затухает медленнее, чем мы готовы считать в одном тике.
            // Предупреждаем (число видно в логе) и обрываем хвост: он заведомо мельче порога значимости.
            if (_ledger.HasPending)
            {
                Debug.LogWarning(
                    $"[CombatSimulation] Combat-round cap hit at tick {_currentTick}: заявки урона/лечения " +
                    $"продолжают порождаться после {MaxCombatRounds} кругов. Хвост отражения обрезан.");
                _ledger.Clear();
            }
        }

        // FIFO-дренаж внутренних событий: реактивные компоненты могут породить новый урон → новые
        // события → дренятся в той же очереди БЕЗ рекурсии. Кап ловит бесконечный пинг-понг.
        private void DrainEventQueue()
        {
            int processed = 0;
            while (_eventQueue.Count > 0 && processed < MaxEventsPerDrain)
            {
                CombatEventData ev = _eventQueue.Dequeue();

                // Широковещательные события: реагирует не участник, а НАБЛЮДАТЕЛЬ, поэтому носителей
                // столько, сколько подходящих юнитов. Обход идёт по _units в порядке списка — он же
                // порядок сборки боя, то есть детерминированный и одинаковый у обеих сторон.
                if (ev.Type == CombatEvent.AbilityCast)
                {
                    // На чужой каст реагирует ПРОТИВНИК («Отражающий налёт» Антимага).
                    RuntimeUnit caster = ev.Source;
                    for (int i = 0; i < _units.Count; i++)
                    {
                        RuntimeUnit u = _units[i];
                        if (u.IsDead || caster == null || u.Team == caster.Team) continue;
                        _effectSystem.Dispatch(u, in ev, this);
                    }

                    processed++;
                    continue;
                }

                // Смерть слышит вся арена: на неё реагируют не только эффекты на трупе (перенос метки),
                // но и наблюдатели — «Собиратель костей» Некроманта смотрит, не его ли скелет упал.
                // Труп в рассылку входит: реактивы на нём обязаны сработать так же, как раньше.
                if (ev.Type == CombatEvent.UnitDied)
                {
                    for (int i = 0; i < _units.Count; i++)
                    {
                        RuntimeUnit u = _units[i];
                        if (u.IsDead && !ReferenceEquals(u, ev.Target)) continue;   // мёртвым зрителям смерть не нужна
                        _effectSystem.Dispatch(u, in ev, this);
                    }

                    processed++;
                    continue;
                }

                RuntimeUnit carrier =
                    ev.Type == CombatEvent.DamageDealt || ev.Type == CombatEvent.Healed
                    || ev.Type == CombatEvent.UnitKilled || ev.Type == CombatEvent.EffectExpired
                        ? ev.Source
                        : ev.Target;

                // Смерть ушла выше своей ветвью (труп получает событие там), значит здесь остались
                // только события живым носителям.
                if (carrier != null && !carrier.IsDead)
                {
                    _effectSystem.Dispatch(carrier, in ev, this);
                }

                processed++;
            }

            // Упёрлись в кап: осталось > 0 непродренированных событий. Раньше их молча сбрасывали —
            // исход боя тихо зависел от «уложились ли в MaxEventsPerDrain» (07 §3.8 B6). На текущем
            // контенте 512/тик недостижимо; если сработало — это пинг-понг реактивов или всплеск,
            // требующий внимания. Логируем (dev-сигнал) и сбрасываем, чтобы не зациклиться.
            if (_eventQueue.Count > 0)
            {
                Debug.LogError(
                    $"[CombatSimulation] Event-queue cap hit: dropped {_eventQueue.Count} events at tick {_currentTick} " +
                    $"(processed {MaxEventsPerDrain}). Возможен пинг-понг реактивных компонентов.");
                _eventQueue.Clear();
            }
        }

        private void ApplyDueCommands()
        {
            int i = 0;
            while (i < _commandQueue.Count && _commandQueue[i].TargetTick <= _currentTick)
            {
                _commandQueue[i].Apply(this);
                _commandQueue.RemoveAt(i);
            }
        }

        /// <summary>
        /// Бой кончается, когда живой остаётся не больше одной команды. Считаем по фактическим номерам
        /// команд, а не по «своей/чужой»: сторон может быть больше двух (PvP, будущие режимы).
        /// </summary>
        private void CheckOutcome()
        {
            // До первого спавна бой не оценивается. После — _units непуст (мёртвые остаются
            // помеченными, не удаляются), поэтому отдельная проверка на пустоту не нужна.
            if (!_hasSpawned) return;

            int  aliveTeam = BattleOutcome.NoTeam;
            bool anyAlive  = false;

            for (int i = 0; i < _units.Count; i++)
            {
                RuntimeUnit u = _units[i];
                if (u.IsDead) continue;

                if (!anyAlive)
                {
                    aliveTeam = u.Team;
                    anyAlive  = true;
                }
                else if (u.Team != aliveTeam)
                {
                    return; // живы минимум две команды — бой продолжается
                }
            }

            _outcome = anyAlive ? BattleOutcome.Win(aliveTeam) : BattleOutcome.Draw;
            OnBattleEnded?.Invoke(_outcome);
        }
    }
}
