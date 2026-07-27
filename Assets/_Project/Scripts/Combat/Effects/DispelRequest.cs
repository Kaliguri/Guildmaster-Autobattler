using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Запрос на снятие эффектов с цели. Разрешение: совпадение полярности И тегов И
    /// <c>CleanseTier ≤ DispelPower</c> И <c>!Unremovable</c> (вики «6» §5.4).
    /// </summary>
    public readonly struct DispelRequest
    {
        /// <summary>С кого снимаем.</summary>
        public readonly RuntimeUnit Target;

        /// <summary>Какую полярность снимать (Any / Buff / Debuff).</summary>
        public readonly DispelTargetPolarity Polarity;

        /// <summary>Категории-теги (<see cref="EffectTag.None"/> = любая категория).</summary>
        public readonly EffectTag Tags;

        /// <summary>Снимает эффекты с <c>CleanseTier ≤ DispelPower</c>.</summary>
        public readonly int DispelPower;

        /// <summary>Сколько максимум снять (0 = все подходящие).</summary>
        public readonly int MaxCount;

        /// <summary>
        /// Кто снимает. Нужен, чтобы отличить помощь команде от собственной механики: снятая с союзника
        /// чужая порча — это утилита снявшего, а съеденный своей же ульткой собственный дебафф (крио
        /// конвертирует «Заморозку» в стан) — не очистка вовсе. null = системное снятие без автора.
        /// </summary>
        public readonly RuntimeUnit Source;

        public DispelRequest(
            RuntimeUnit target, DispelTargetPolarity polarity, EffectTag tags, int dispelPower, int maxCount,
            RuntimeUnit source = null)
        {
            Target      = target;
            Polarity    = polarity;
            Tags        = tags;
            DispelPower = dispelPower;
            MaxCount    = maxCount;
            Source      = source;
        }
    }
}
