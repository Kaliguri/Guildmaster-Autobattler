using System;
using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Детерминированная тиковая симуляция боя. Реализует <see cref="ICombatContext"/>
    /// — единственная точка мутации состояния боя из систем и (Фаза 2) компонентов эффектов.
    /// <para>
    /// Порядок систем за тик: ApplyCommands → Brain (AI) → Ability → Movement → Displacement → Separation
    /// → SpatialHashRebuild → AutoAttack → Projectiles → Regen → Effects → DrainEvents → Death → CheckOutcome → currentTick++.
    /// </para>
    /// (вики «10» §5.1).
    /// </summary>
    public sealed class CombatSimulation : ICombatContext, IBattleView
    {
        private readonly IRngService         _rng;
        private readonly float               _armorK;
        private readonly ArenaBounds         _arena;
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

        /// <summary>Границы боевого поля этого боя (для презентации/оверлея; клампинг — внутри систем).</summary>
        public ArenaBounds Arena       => _arena;

        /// <summary>Система разделения тел — доступ для dev-тюнинга параметров в рантайме (gm_sep_*).</summary>
        public SeparationSystem Separation => _separationSystem;

        public IReadOnlyList<RuntimeUnit> Units    => _units;
        public BattleOutcome              Outcome  => _outcome;
        public bool                       IsPaused => _isPaused;

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
            ArenaBounds?      arena              = null)
        {
            _rng              = rng;
            _armorK           = armorK;
            // null → бесконечное поле (headless-тесты, бой без заданной арены). НЕ default(ArenaBounds).
            _arena            = arena ?? ArenaBounds.Unbounded;
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

            _brainSystem.Tick(_units, this);
            _abilitySystem.Tick(_units, this, dt);
            _movementSystem.Tick(_units, dt, in _arena);
            _displacementSystem.Tick(this, dt, in _arena);
            _separationSystem.Tick(_units, _spatialHash, in _arena);
            _spatialHash.Rebuild(_units);
            _autoAttackSystem.Tick(_units, this, dt);
            _projectileSystem.Tick(_projectiles, _units, this, dt, in _arena);
            _regenSystem.Tick(_units, dt);
            _effectSystem.Tick(_units, this, dt);
            DrainEventQueue();
            _deathSystem.Tick(_units, _spatialHash);

            CheckOutcome();
            _currentTick++;
        }

        // --- ICombatContext ---

        public void DealDamage(in DamageRequest req)
        {
            if (req.Target.IsDead) return;

            // Синхронный pre-damage перехват (§9.3): «Оплот» поднимает щит (поглотит этот же удар),
            // «Изворотливость» может полностью отменить удар. Порядок детерминирован.
            if (_effectSystem.RunPreDamage(req.Target, in req, this))
            {
                OnAttackEvaded?.Invoke(req.Target); // presentation-сигнал «evade», симуляцию не трогает
                return; // удар негейтнут — ни урона, ни урон-событий
            }

            var result = DamagePipeline.Execute(req);
            OnDamageDealt?.Invoke(req.Source, req.Target, result);

            // Внутренние события для реактивных компонентов (vampiric/thorns). Два события на удар:
            // DamageDealt доставляется источнику, DamageTaken — цели (вики «12» §3.4).
            if (req.Source != null)
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageDealt, req.Source, req.Target, result.TotalDamage));
            _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageTaken, req.Source, req.Target, result.TotalDamage));

            // Убийство атрибутируется нанёсшему смертельный удар → доставляется УБИЙЦЕ (§10.5, «Скрытность»).
            if (result.KilledTarget && req.Source != null)
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.UnitKilled, req.Source, req.Target, 0f));

            // Вампиризм: источник лечится на долю нанесённого по HP урона. Фаза 1 — применяется
            // ко всему урону (без тегов источника); сузить до авто-атак/способностей — Фаза 2.
            if (req.Source != null && !req.Source.IsDead && result.HpDamage > 0f)
            {
                float lifesteal = req.Source.Stats.Get(Data.Stats.StatType.Lifesteal);
                if (lifesteal > 0f) Heal(req.Source, result.HpDamage * lifesteal, req.Source);
            }
        }

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source)
        {
            if (target.IsDead) return;

            // Потенция исцеления масштабируется парой HealShield-эффективностей (вики «11» §5).
            float mult = (source != null ? source.Stats.Get(Data.Stats.StatType.HealShieldDealtEff) : 1f)
                       * target.Stats.Get(Data.Stats.StatType.HealShieldTakenEff);

            float maxHp = target.Stats.Get(Data.Stats.StatType.MaxHP);
            float before = target.CurrentHP;
            target.CurrentHP = Mathf.Min(before + amount * mult, maxHp);

            // Событие для on-heal реактивных компонентов (носитель — source, как и DamageDealt).
            // Кладём по ФАКТИЧЕСКИ вылеченному (overheal не считается), дренаж — в этом же тике.
            float applied = target.CurrentHP - before;
            if (applied > 0f)
            {
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.Healed, source, target, applied));
                OnHealed?.Invoke(source, target, applied);
            }
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
                DamageType       = spawn.DamageType,
                ArmorK           = spawn.ArmorK,
                PiercesRemaining = spawn.MaxPierces,
                IsHeal           = spawn.IsHeal,
                OnHitEffects     = spawn.OnHitEffects,
                IsAlive          = true,
            };
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

        public void ReportAreaHit(in AreaHit hit) => OnAreaHit?.Invoke(hit);

        public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) => OnAttackStarted?.Invoke(unit, target);

        public void NotifyAttackInterrupted(RuntimeUnit unit) => OnAttackInterrupted?.Invoke(unit);

        public void Dispel(in Effects.DispelRequest req)
        {
            _effectSystem.Dispel(in req, this);
        }

        public void Displace(in DisplaceRequest req)
        {
            // Смещение — это ЭФФЕКТ: вешаем маркер «в полёте» (жёсткий контроль + тег KnockUp, длительность
            // не скейлится — Neutral) на цель, затем отдаём траекторию DisplacementSystem. Маркер снимается
            // в конце полёта (OnDisplacementEnded → RemoveByTag) и поднимает единый EffectExpired.
            if (req.Target != null && !req.Target.IsDead)
                _effectSystem.Apply(req.Target, _airborneEffect, req.Source, this);
            _displacementSystem.Add(in req);
        }

        // --- Управление симуляцией (вызывается командами) ---

        public void SetPaused(bool paused) => _isPaused = paused;

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
        /// Детерминированный слепок состояния симуляции: хэш позиций, HP и текущего тика.
        /// Используется <see cref="Net.SimSyncProbe"/> для проверки рассинхрона.
        /// </summary>
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
                // Состояние авто-атаки — детерминированное, входит в чек-сумму (вики «14»).
                hash ^= (ulong)(uint)u.AttackCooldownTicks * 374761393UL;
                hash ^= (ulong)(uint)u.WindupRemaining     * 3266489917UL;
                hash  = (hash << 13) | (hash >> 51);
            }

            return hash;
        }

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

        // FIFO-дренаж внутренних событий: реактивные компоненты могут породить новый урон → новые
        // события → дренятся в той же очереди БЕЗ рекурсии. Кап ловит бесконечный пинг-понг.
        private void DrainEventQueue()
        {
            int processed = 0;
            while (_eventQueue.Count > 0 && processed < MaxEventsPerDrain)
            {
                CombatEventData ev = _eventQueue.Dequeue();
                RuntimeUnit carrier =
                    ev.Type == CombatEvent.DamageDealt || ev.Type == CombatEvent.Healed
                    || ev.Type == CombatEvent.UnitKilled || ev.Type == CombatEvent.EffectExpired
                        ? ev.Source
                        : ev.Target;

                // UnitDied доставляем даже мёртвому носителю: «Метка охотника» переносится с трупа
                // на ближайшего живого врага (§9.5). Остальные события — только живым.
                bool allowDeadCarrier = ev.Type == CombatEvent.UnitDied;

                if (carrier != null && (allowDeadCarrier || !carrier.IsDead))
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

        private void CheckOutcome()
        {
            // До первого спавна бой не оценивается. После — _units непуст (мёртвые остаются
            // помеченными, не удаляются), поэтому отдельная проверка на пустоту не нужна.
            if (!_hasSpawned) return;

            bool teamAAlive = false;
            bool teamBAlive = false;

            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].IsDead) continue;
                if (_units[i].Team == 0) teamAAlive = true;
                else                     teamBAlive = true;
            }

            BattleOutcome newOutcome;
            if (!teamAAlive && !teamBAlive) newOutcome = BattleOutcome.Draw;
            else if (!teamBAlive)           newOutcome = BattleOutcome.TeamAWins;
            else if (!teamAAlive)           newOutcome = BattleOutcome.TeamBWins;
            else                            return;

            _outcome = newOutcome;
            OnBattleEnded?.Invoke(_outcome);
        }
    }
}
