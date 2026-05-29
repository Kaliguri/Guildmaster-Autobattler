using UnityEngine;

namespace Guildmaster.Core
{
    /// <summary>
    /// Общий контракт для всех боевых единиц на поле боя.
    /// Реализуется как героями гильдии, так и юнитами противника.
    /// </summary>
    public interface IUnit
    {
        /// <summary>Уникальный идентификатор юнита в текущем бою.</summary>
        int Id { get; }

        /// <summary>Отображаемое имя юнита.</summary>
        string DisplayName { get; }

        /// <summary>Текущее количество очков здоровья.</summary>
        int CurrentHealth { get; }

        /// <summary>Максимальное количество очков здоровья.</summary>
        int MaxHealth { get; }

        /// <summary>Возвращает <c>true</c>, если юнит жив и может участвовать в бою.</summary>
        bool IsAlive { get; }

        /// <summary>Позиция юнита на боевой сетке.</summary>
        Vector2Int GridPosition { get; }

        /// <summary>
        /// Применяет урон к юниту с учётом брони и активных эффектов.
        /// </summary>
        /// <param name="damage">Базовый урон до применения модификаторов.</param>
        /// <param name="source">Источник урона — используется для триггеров реликвий.</param>
        /// <returns>Фактически нанесённый урон после всех модификаторов.</returns>
        int TakeDamage(int damage, IUnit source);

        /// <summary>
        /// Восстанавливает здоровье юнита. Не превышает <see cref="MaxHealth"/>.
        /// </summary>
        /// <param name="amount">Количество восстанавливаемого здоровья.</param>
        void Heal(int amount);

        /// <summary>
        /// Принудительно убивает юнита, независимо от текущего здоровья.
        /// Используется для эффектов мгновенной смерти.
        /// </summary>
        void Die();
    }
}
