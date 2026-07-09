using System;
using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Детерминированная тиковая симуляция боя. Реализует <see cref="ICombatContext"/>
    /// — единственная точка мутации состояния боя из систем и (Фаза 2) компонентов эффектов.
    /// <para>
    /// Порядок систем за тик: ApplyCommands → Brain (AI) → Ability → Movement → SpatialHashRebuild
    /// → AutoAttack → Projectiles → Regen → Effects → DrainEvents → Death → CheckOutcome → currentTick++.
    /// </para>
    /// (вики «10» §5.1).
    /// </summary>
    public sealed class CombatSimulation : ICombatContext, IBattleView
    {
        private readonly IRngService         _rng;
        private readonly float               _armorK;
        private readonly SpatialHash         _spatialHash;
        private readonly BrainSystem         _brainSystem;
        private readonly AbilitySystem       _abilitySystem;
        private readonly MovementSystem      _movementSystem;
        private readonly AutoAttackSystem    _autoAttackSystem;
        private readonly ProjectileSystem    _projectileSystem;
        private readonly DeathSystem         _deathSystem;
        private readonly EffectSystem        _effectSystem;
        private readonly RegenSystem         _regenSystem;

        private readonly List<RuntimeUnit>  _units       = new List<RuntimeUnit>();
        private readonly List<RuntimeUnit>  _pendingAdd  = new List<RuntimeUnit>();
        private readonly List<Projectile>   _projectiles = new List<Projectile>();
        private readonly List<ICombatCommand> _commandQueue = new List<ICombatCommand>();
        private readonly Queue<CombatEventData> _eventQueue = new Queue<CombatEventData>();

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

        /// <summary>Юнит вошёл в замах авто-атаки (вики «14»): запускает анимацию свинга во View.</summary>
        public event Action<RuntimeUnit, RuntimeUnit> OnAttackStarted;

        /// <summary>Замах авто-атаки прерван (стан/смерть себя): View рвёт свинг в idle (вики «14»).</summary>
        public event Action<RuntimeUnit> OnAttackInterrupted;

        /// <summary>Бой завершён с итогом.</summary>
        public event Action<BattleOutcome> OnBattleEnded;

        /// <summary>Зона удара сработала (линия авто-атаки / круг активки) — для dev-оверлея зон.</summary>
        public event Action<AreaHit> OnAreaHit;

        // --- ICombatContext ---

        public IRngService Rng         => _rng;
        public int         CurrentTick => _currentTick;
        public float       ArmorK      => _armorK;

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
            RegenSystem       regenSystem)
        {
            _rng              = rng;
            _armorK           = armorK;
            _spatialHash      = spatialHash;
            _brainSystem      = brainSystem;
            _abilitySystem    = abilitySystem;
            _movementSystem   = movementSystem;
            _autoAttackSystem = autoAttackSystem;
            _projectileSystem = projectileSystem;
            _deathSystem      = deathSystem;
            _effectSystem     = effectSystem;
            _regenSystem      = regenSystem;

            _deathSystem.OnUnitDied += unit => OnUnitDied?.Invoke(unit);
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
            _movementSystem.Tick(_units, dt);
            _spatialHash.Rebuild(_units);
            _autoAttackSystem.Tick(_units, this, dt);
            _projectileSystem.Tick(_projectiles, _units, this, dt);
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
            var result = DamagePipeline.Execute(req);
            OnDamageDealt?.Invoke(req.Source, req.Target, result);

            // Внутренние события для реактивных компонентов (vampiric/thorns). Два события на удар:
            // DamageDealt доставляется источнику, DamageTaken — цели (вики «12» §3.4).
            if (req.Source != null)
                _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageDealt, req.Source, req.Target, result.TotalDamage));
            _eventQueue.Enqueue(new CombatEventData(CombatEvent.DamageTaken, req.Source, req.Target, result.TotalDamage));

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

            _projectiles.Add(new Projectile
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
            });
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
            // Broad-phase: круг радиусом = длина линии (живых отбирает QueryRadius), затем
            // narrow-phase по геометрии полосы: проекция на направление в [0..length], перпендикуляр ≤ width/2.
            _spatialHash.QueryRadius(origin, length, results);

            Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            float halfWidth = width * 0.5f;

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

        // --- Управление симуляцией (вызывается командами) ---

        public void SetPaused(bool paused) => _isPaused = paused;

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
                        ? ev.Source
                        : ev.Target;

                if (carrier != null && !carrier.IsDead)
                {
                    _effectSystem.Dispatch(carrier, in ev, this);
                }

                processed++;
            }

            if (_eventQueue.Count > 0) _eventQueue.Clear();
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
