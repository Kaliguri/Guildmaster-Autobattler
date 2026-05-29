// ЗАГЛУШКА: класс создан для проверки отображения в Doxygen.
// Не является финальной реализацией — логика намеренно минимальна.

using Guildmaster.Core;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Статический калькулятор урона. Инкапсулирует всю логику
    /// модификации урона: броня, критические удары, уязвимости.
    /// </summary>
    /// <remarks>
    /// Все методы статические — класс не хранит состояния.
    /// Для расширения системы урона создавайте новые методы здесь,
    /// а не разносите формулы по юнитам.
    /// </remarks>
    public static class DamageCalculator
    {
        /// <summary>Минимальный урон после всех модификаторов. Гарантирует что атака всегда наносит хоть что-то.</summary>
        private const int MinimumDamage = 1;

        /// <summary>
        /// Вычисляет итоговый физический урон с учётом брони цели.
        /// </summary>
        /// <param name="rawDamage">Базовый урон до модификаторов.</param>
        /// <param name="targetArmor">Броня цели.</param>
        /// <returns>Итоговый урон, не ниже <see cref="MinimumDamage"/>.</returns>
        public static int CalculatePhysical(int rawDamage, int targetArmor)
        {
            int result = rawDamage - targetArmor;
            return System.Math.Max(result, MinimumDamage);
        }

        /// <summary>
        /// Вычисляет магический урон. Броня не учитывается,
        /// но может применяться магическое сопротивление.
        /// </summary>
        /// <param name="rawDamage">Базовый магический урон.</param>
        /// <param name="magicResistance">Процент снижения магического урона (0–100).</param>
        /// <returns>Итоговый урон, не ниже <see cref="MinimumDamage"/>.</returns>
        public static int CalculateMagical(int rawDamage, int magicResistance)
        {
            float multiplier = 1f - (magicResistance / 100f);
            int result = (int)(rawDamage * multiplier);
            return System.Math.Max(result, MinimumDamage);
        }

        /// <summary>
        /// Определяет, является ли атака критическим ударом.
        /// </summary>
        /// <param name="critChance">Шанс крита в процентах (0–100).</param>
        /// <returns><c>true</c> если атака критическая.</returns>
        public static bool RollCrit(float critChance)
        {
            return UnityEngine.Random.value * 100f < critChance;
        }

        /// <summary>
        /// Применяет множитель критического удара к базовому урону.
        /// </summary>
        /// <param name="baseDamage">Базовый урон до крита.</param>
        /// <param name="critMultiplier">Множитель крита (обычно 1.5–2.0).</param>
        /// <returns>Урон после применения крита, округлённый вниз.</returns>
        public static int ApplyCrit(int baseDamage, float critMultiplier)
        {
            return (int)(baseDamage * critMultiplier);
        }
    }
}
