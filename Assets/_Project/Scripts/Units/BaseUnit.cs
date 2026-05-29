using UnityEngine;
using Guildmaster.Core;

namespace Guildmaster.Units
{
    /// <summary>
    /// Базовая реализация боевой единицы. Все игровые юниты наследуются от этого класса.
    /// </summary>
    /// <remarks>
    /// Обрабатывает здоровье, смерть и позицию на сетке.
    /// Специфическая логика (атака, способности) реализуется в подклассах.
    /// </remarks>
    public abstract class BaseUnit : MonoBehaviour, IUnit
    {
        [Tooltip("Данные юнита: имя, базовые характеристики, спрайт.")]
        [SerializeField] private UnitData _data;

        [Tooltip("Текущее здоровье. Не редактировать вручную — используется только для отладки.")]
        [SerializeField] private int _currentHealth;

        private Vector2Int _gridPosition;

        /// <inheritdoc/>
        public int Id { get; private set; }

        /// <inheritdoc/>
        public string DisplayName => _data != null ? _data.DisplayName : name;

        /// <inheritdoc/>
        public int CurrentHealth => _currentHealth;

        /// <inheritdoc/>
        public int MaxHealth => _data != null ? _data.BaseHealth : 0;

        /// <inheritdoc/>
        public bool IsAlive => _currentHealth > 0;

        /// <inheritdoc/>
        public Vector2Int GridPosition => _gridPosition;

        /// <summary>
        /// Вызывается когда здоровье юнита опускается до нуля или ниже.
        /// </summary>
        public event System.Action<BaseUnit> OnDeath;

        /// <summary>
        /// Инициализирует юнита перед боем.
        /// </summary>
        /// <param name="id">Уникальный идентификатор в текущем бою.</param>
        /// <param name="startPosition">Стартовая позиция на боевой сетке.</param>
        public virtual void Initialize(int id, Vector2Int startPosition)
        {
            Id = id;
            _gridPosition = startPosition;
            _currentHealth = MaxHealth;
        }

        /// <inheritdoc/>
        public virtual int TakeDamage(int damage, IUnit source)
        {
            int actual = Mathf.Max(0, damage);
            _currentHealth = Mathf.Max(0, _currentHealth - actual);

            if (_currentHealth == 0)
                Die();

            return actual;
        }

        /// <inheritdoc/>
        public virtual void Heal(int amount)
        {
            _currentHealth = Mathf.Min(MaxHealth, _currentHealth + Mathf.Max(0, amount));
        }

        /// <inheritdoc/>
        public virtual void Die()
        {
            _currentHealth = 0;
            OnDeath?.Invoke(this);
        }

        /// <summary>
        /// Перемещает юнита на новую клетку сетки.
        /// </summary>
        /// <param name="newPosition">Целевая позиция на боевой сетке.</param>
        public void MoveTo(Vector2Int newPosition)
        {
            _gridPosition = newPosition;
        }

        protected virtual void Awake()
        {
            _currentHealth = MaxHealth;
        }
    }
}
