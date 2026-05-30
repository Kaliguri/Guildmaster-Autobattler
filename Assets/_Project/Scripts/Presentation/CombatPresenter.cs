using System.Collections.Generic;
using Guildmaster.Combat;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Мост «симуляция → вид боя». Подписывается на C#-события <see cref="CombatSimulation"/>,
    /// спавнит/деспавнит <see cref="UnitView"/> и распространяет события в Presentation-слой.
    /// Только читает состояние симуляции — никогда не мутирует (вики «10» §7).
    /// </summary>
    public sealed class CombatPresenter : MonoBehaviour
    {
        [Tooltip("Префаб вида юнита.")]
        [SerializeField] private UnitView _unitViewPrefab;

        [Tooltip("Спаунер чисел урона (необязательно).")]
        [SerializeField] private DamageNumberSpawner _damageNumbers;

        private CombatSimulation            _simulation;
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();

        [Inject]
        public void Construct(CombatSimulation simulation)
        {
            _simulation = simulation;
        }

        private void OnEnable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned  += HandleUnitSpawned;
            _simulation.OnUnitDied     += HandleUnitDied;
            _simulation.OnDamageDealt  += HandleDamageDealt;
            _simulation.OnBattleEnded  += HandleBattleEnded;
        }

        private void OnDisable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned  -= HandleUnitSpawned;
            _simulation.OnUnitDied     -= HandleUnitDied;
            _simulation.OnDamageDealt  -= HandleDamageDealt;
            _simulation.OnBattleEnded  -= HandleBattleEnded;
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
            if (_unitViewPrefab == null) return;

            var view = Instantiate(_unitViewPrefab, (Vector3)(Vector2)unit.Position, Quaternion.identity, transform);
            view.Bind(unit);
            _views[unit.Id] = view;
        }

        private void HandleUnitDied(RuntimeUnit unit)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.OnDeath();
                _views.Remove(unit.Id);
            }
        }

        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            if (_views.TryGetValue(target.Id, out var view))
                view.OnDamageReceived(result.TotalDamage);

            _damageNumbers?.Spawn(target.Position, result.TotalDamage);
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            Debug.Log($"[CombatPresenter] - Бой завершён: {outcome}");
        }
    }
}
