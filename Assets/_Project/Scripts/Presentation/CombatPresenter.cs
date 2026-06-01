using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Мост «симуляция → презентация». Подписывается на C#-события <see cref="CombatSimulation"/>,
    /// спавнит/деспавнит <see cref="UnitView"/> и ретранслирует события в MessagePipe, чтобы
    /// Audio/VFX/UI подписывались независимо от симуляции (вики «10» §7, стек — CLAUDE.md).
    /// Только читает состояние симуляции — никогда не мутирует.
    /// </summary>
    public sealed class CombatPresenter : MonoBehaviour
    {
        [Tooltip("Префаб вида юнита.")]
        [SerializeField] private UnitView _unitViewPrefab;

        [Tooltip("Спаунер чисел урона (необязательно).")]
        [SerializeField] private DamageNumberSpawner _damageNumbers;

        [Tooltip("Пер-юнит визуалы по реликвии (вики «13» шаг 4): если у юнита эта реликвия — её набор кадров вместо дефолтного на префабе.")]
        [SerializeField] private VisualOverride[] _visualOverrides = System.Array.Empty<VisualOverride>();

        [System.Serializable]
        private struct VisualOverride
        {
            public RelicData Relic;
            public UnitVisual Visual;
        }

        private CombatSimulation            _simulation;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();

        private IPublisher<UnitSpawnedEvent> _unitSpawnedPublisher;
        private IPublisher<UnitDiedEvent>    _unitDiedPublisher;
        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            IPublisher<UnitSpawnedEvent> unitSpawnedPublisher,
            IPublisher<UnitDiedEvent>    unitDiedPublisher,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher)
        {
            _simulation           = simulation;
            _unitSpawnedPublisher = unitSpawnedPublisher;
            _unitDiedPublisher    = unitDiedPublisher;
            _damageDealtPublisher = damageDealtPublisher;
            _battleEndedPublisher = battleEndedPublisher;
        }

        private void OnEnable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned       += HandleUnitSpawned;
            _simulation.OnUnitDied          += HandleUnitDied;
            _simulation.OnDamageDealt       += HandleDamageDealt;
            _simulation.OnBattleEnded       += HandleBattleEnded;
            _simulation.OnAttackStarted     += HandleAttackStarted;
            _simulation.OnAttackInterrupted += HandleAttackInterrupted;
        }

        private void OnDisable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned       -= HandleUnitSpawned;
            _simulation.OnUnitDied          -= HandleUnitDied;
            _simulation.OnDamageDealt       -= HandleDamageDealt;
            _simulation.OnBattleEnded       -= HandleBattleEnded;
            _simulation.OnAttackStarted     -= HandleAttackStarted;
            _simulation.OnAttackInterrupted -= HandleAttackInterrupted;
        }

        private void Update()
        {
            float alpha = Time.deltaTime / Guildmaster.Core.Simulation.SimConstants.TickDelta;
            alpha = UnityEngine.Mathf.Clamp01(alpha);

            foreach (var kvp in _views)
            {
                kvp.Value.UpdateInterpolation(alpha);
            }
        }

        private void HandleUnitSpawned(RuntimeUnit unit)
        {
            if (_unitViewPrefab != null)
            {
                var view = Instantiate(_unitViewPrefab, (Vector3)(Vector2)unit.Position, Quaternion.identity, transform);
                view.Bind(unit);

                UnitVisual ov = ResolveVisual(unit.Relic);
                if (ov != null) view.SetVisual(ov);

                _views[unit.Id] = view;
            }

            _unitSpawnedPublisher.Publish(new UnitSpawnedEvent(unit));
        }

        private UnitVisual ResolveVisual(RelicData relic)
        {
            if (relic == null) return null;
            for (int i = 0; i < _visualOverrides.Length; i++)
                if (_visualOverrides[i].Relic == relic) return _visualOverrides[i].Visual;
            return null;
        }

        private void HandleUnitDied(RuntimeUnit unit)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.OnDeath();
                _views.Remove(unit.Id);
            }

            _unitDiedPublisher.Publish(new UnitDiedEvent(unit));
        }

        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            // Урон совпадает с кадром контакта (конец замаха): здесь — импакт-фидбэк цели.
            // Свинг источника запускается раньше, на OnAttackStarted (вики «14»).
            if (_views.TryGetValue(target.Id, out var view))
                view.OnDamageReceived(result.TotalDamage);

            _damageNumbers?.Spawn(target.Position, result.TotalDamage);

            _damageDealtPublisher.Publish(new DamageDealtEvent(source, target, result));
        }

        private void HandleAttackStarted(RuntimeUnit source, RuntimeUnit target)
        {
            // Вход в замах: запускаем анимацию свинга у источника (вики «14»).
            if (source != null && _views.TryGetValue(source.Id, out var sourceView))
                sourceView.OnAttackStarted();
        }

        private void HandleAttackInterrupted(RuntimeUnit unit)
        {
            if (unit != null && _views.TryGetValue(unit.Id, out var view))
                view.OnAttackInterrupted();
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            Debug.Log($"[CombatPresenter] - Бой завершён: {outcome}");

            _battleEndedPublisher.Publish(new BattleEndedEvent(outcome));
        }
    }
}
