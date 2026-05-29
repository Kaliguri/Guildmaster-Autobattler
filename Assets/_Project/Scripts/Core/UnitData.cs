using UnityEngine;

namespace Guildmaster.Core
{
    /// <summary>
    /// ScriptableObject с базовыми характеристиками юнита.
    /// Создаётся через <c>Assets → Create → Guildmaster → Unit Data</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewUnitData", menuName = "Guildmaster/Unit Data")]
    public class UnitData : ScriptableObject
    {
        [Tooltip("Отображаемое имя юнита в бою и UI.")]
        [SerializeField] private string _displayName;

        [Tooltip("Базовое здоровье без учёта осколков и бонусов.")]
        [SerializeField] private int _baseHealth = 100;

        [Tooltip("Базовый урон атаки без учёта осколков.")]
        [SerializeField] private int _baseAttack = 10;

        [Tooltip("Базовая броня: уменьшает входящий физический урон.")]
        [SerializeField] private int _baseArmor = 0;

        [Tooltip("Время между атаками в секундах.")]
        [SerializeField] private float _attackInterval = 1.5f;

        [Tooltip("Дальность атаки в клетках сетки.")]
        [SerializeField] private int _attackRange = 1;

        /// <summary>Отображаемое имя юнита.</summary>
        public string DisplayName => _displayName;

        /// <summary>Базовое здоровье без учёта бонусов.</summary>
        public int BaseHealth => _baseHealth;

        /// <summary>Базовый урон атаки.</summary>
        public int BaseAttack => _baseAttack;

        /// <summary>Базовая броня: уменьшает входящий физический урон.</summary>
        public int BaseArmor => _baseArmor;

        /// <summary>Интервал между атаками в секундах.</summary>
        public float AttackInterval => _attackInterval;

        /// <summary>Дальность атаки в клетках сетки.</summary>
        public int AttackRange => _attackRange;
    }
}
