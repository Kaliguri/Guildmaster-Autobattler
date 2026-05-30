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
    /// Порядок систем за тик: ApplyCommands → Targeting → Movement → SpatialHashRebuild
    /// → AutoAttack → Projectiles → Effects → Death → CheckOutcome → currentTick++.
    /// </para>
    /// (вики «10» §5.1).
    /// </summary>
    public sealed class CombatSimulation : ICombatContext
    {
        private readonly IRngService         _rng;
        private readonly float               _armorK;
        private readonly SpatialHash         _spatialHash;
        private readonly TargetingSystem     _targetingSystem;
        private readonly AbilitySystem       _abilitySystem;
        private readonly MovementSystem      _movementSystem;
        private readonly AutoAttackSystem    _autoAttackSystem;
        private readonly ProjectileSystem    _projectileSystem;
        private readonly DeathSystem         _deathSystem;
        private readonly EffectSystem        _effectSystem;

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

        // --- События для Presentation и Game-слоя ---

        /// <summary>Юнит появился в симуляции.</summary>
        public event Action<RuntimeUnit> OnUnitSpawned;

        /// <summary>Юнит погиб.</summary>
        public event Action<RuntimeUnit> OnUnitDied;

        /// <summary>Нанесён урон: источник, цель, результат.</summary>
        public event Action<RuntimeUnit, RuntimeUnit, DamageResult> OnDamageDealt;

        /// <summary>Бой завершён с итогом.</summary>
        public event Action<BattleOutcome> OnBattleEnded;

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
            TargetingSystem   targetingSystem,
            AbilitySystem     abilitySystem,
            MovementSystem    movementSystem,
            AutoAttackSystem  autoAttackSystem,
            ProjectileSystem  projectileSystem,
            DeathSystem       deathSystem,
            EffectSystem      effectSystem)
        {
            _rng              = rng;
            _armorK           = armorK;
            _spatialHash      = spatialHash;
            _targetingSystem  = targetingSystem;
            _abilitySystem    = abilitySystem;
            _movementSystem   = movementSystem;
            _autoAttackSystem = autoAttackSystem;
            _projectileSystem = projectileSystem;
            _deathSystem      = deathSystem;
            _effectSystem     = effectSystem;

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

            _targetingSystem.Tick(_units);
            _abilitySystem.Tick(_units, this, dt);
            _movementSystem.Tick(_units, dt);
            _spatialHash.Rebuild(_units);
            _autoAttackSystem.Tick(_units, this, dt);
            _projectileSystem.Tick(_projectiles, _units, this, dt);
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
        }

        public void Heal(RuntimeUnit target, float amount, RuntimeUnit source)
        {
            if (target.IsDead) return;

            // Потенция исцеления масштабируется парой HealShield-эффективностей (вики «11» §5).
            float mult = (source != null ? source.Stats.Get(Data.Stats.StatType.HealShieldDealtEff) : 1f)
                       * target.Stats.Get(Data.Stats.StatType.HealShieldTakenEff);

            float maxHp = target.Stats.Get(Data.Stats.StatType.MaxHP);
            target.CurrentHP = Mathf.Min(target.CurrentHP + amount * mult, maxHp);
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

        public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source)
        {
            _effectSystem.Apply(target, def, source, this);
        }

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
                hash  = (hash << 13) | (hash >> 51);
            }

            return hash;
        }

        // --- Приватные ---

        private void FlushPendingSpawns()
        {
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
            if (_units.Count == 0) return;

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
