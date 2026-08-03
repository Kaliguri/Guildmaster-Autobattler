using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Какой параметр способности меняет конвертация стата.</summary>
    public enum AbilityParameter
    {
        /// <summary>Базовый кулдаун, сек. Обратная форма делает его короче с ростом стата.</summary>
        Cooldown = 0,

        /// <summary>Длительность подготовки, сек («Стальной вихрь»: 0.5 с, короче при высокой скорости атаки).</summary>
        CastSeconds = 1,

        /// <summary>Множитель прямого урона («Абсолютная сила» Мага молний растёт от статов).</summary>
        DamageMultiplier = 2,
    }

    /// <summary>
    /// Правило «стат носителя → параметр ЕГО способности» (M4): та же формула, что везде
    /// (<see cref="StatConversion"/>), плюс указание, какой именно параметр она правит. Заводится на
    /// <see cref="AbilityData"/> списком — у кита может быть и ускорение каста, и прибавка к множителю.
    /// </summary>
    [Serializable]
    public struct AbilityStatScaling
    {
        [Tooltip("Какой параметр способности меняем.")]
        public AbilityParameter Target;

        [Tooltip("Правило конвертации: от какого стата, в какую сторону и насколько.")]
        public StatConversion Conversion;

        /// <summary>Применить правило к базовому значению параметра.</summary>
        public float Apply(float baseValue, IStatReader stats) => Conversion.Apply(baseValue, stats);
    }
}
