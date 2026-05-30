using Guildmaster.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// World-space визуальное представление <see cref="RuntimeUnit"/>.
    /// Интерполирует позицию между тиками (сим 30 Hz, рендер 60+ fps).
    /// Feel-хуки — <see cref="UnityEvent"/> поля: Phase 1 пусты (no-op),
    /// в Inspector подключается MMF_Player.PlayFeedbacks без compile-time зависимости.
    /// (вики «10» §7).
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private SpriteRenderer _sprite;
        [SerializeField] private HealthBarView  _healthBar;

        [Header("Feel Hooks (подключить MMF_Player в Inspector)")]
        [SerializeField] private UnityEvent _onHitFeedback;
        [SerializeField] private UnityEvent _onDeathFeedback;

        private RuntimeUnit _unit;
        private Vector2     _renderPosition;

        /// <summary>Связать вид с рантайм-юнитом.</summary>
        public void Bind(RuntimeUnit unit)
        {
            _unit           = unit;
            _renderPosition = unit.Position;
            transform.position = (Vector3)_renderPosition;

            if (_healthBar != null)
                _healthBar.Bind(unit);
        }

        /// <summary>
        /// Обновить интерполированную позицию. Вызывается из <see cref="CombatPresenter.Update"/>.
        /// </summary>
        /// <param name="alpha">Степень интерполяции [0, 1] между PreviousPosition и Position.</param>
        public void UpdateInterpolation(float alpha)
        {
            if (_unit == null) return;

            _renderPosition = Vector2.Lerp(_unit.PreviousPosition, _unit.Position, alpha);
            transform.position = new Vector3(_renderPosition.x, _renderPosition.y, 0f);

            if (_healthBar != null)
                _healthBar.UpdateBar(_unit.CurrentHP, _unit.Stats.Get(Data.Stats.StatType.MaxHP));
        }

        /// <summary>Вызывается при получении урона.</summary>
        public void OnDamageReceived(float damage)
        {
            _onHitFeedback?.Invoke();
        }

        /// <summary>Вызывается при гибели юнита.</summary>
        public void OnDeath()
        {
            _onDeathFeedback?.Invoke();
            gameObject.SetActive(false);
        }
    }
}
