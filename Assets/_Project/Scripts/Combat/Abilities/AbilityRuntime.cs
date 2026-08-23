using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Abilities
{
    /// <summary>
    /// Рантайм-состояние одной активной способности на юните (POCO, один бой): таймер кулдауна.
    /// Создаётся <see cref="RuntimeUnitFactory"/> из <see cref="AbilityData"/> мементо (вики «12» §2.4).
    /// </summary>
    public sealed class AbilityRuntime
    {
        public AbilityData Data;

        /// <summary>Остаток кулдауна, сек. ≤ 0 — готова.</summary>
        public float CooldownRemaining;

        /// <summary>
        /// Сколько раз способность кастовала В ЭТОМ БОЮ. Питает разгон числа применений нагрузки
        /// (<see cref="AbilityData.ResolvePayloadRepeats"/>): залп Арканиста растёт на стрелу за каст.
        /// </summary>
        /// <remarks>
        /// Живёт здесь, а не на юните и не на эффекте: разгон — свойство КОНКРЕТНОЙ способности, и у
        /// кита с двумя растущими активками счётчики обязаны быть раздельными. Обнуляется вместе с
        /// рантаймом, то есть с началом нового боя — межбоевого накопления нет по замыслу.
        /// </remarks>
        public int CastsThisBattle;

        /// <summary>
        /// Дальность каста в мировых единицах. <b>Отрицательная = «как у авто-атаки»</b>: тогда дистанция
        /// читается из стата юнита прямо в момент проверки.
        /// </summary>
        /// <remarks>
        /// Разрешается фабрикой, потому что ступень живёт в <c>StatsConfig</c>, а он есть у сборки юнита
        /// и не должен протаскиваться в боевые системы. Наследование намеренно НЕ разворачивается в число
        /// при сборке: авто-атака у кита может меняться по ходу боя — стойка Кровоманта переставляет
        /// дальность с 8 на 1, — и умение обязано ехать вместе с ней, а не с той, что была на старте.
        /// </remarks>
        public float CastRange = -1f;

        public AbilityRuntime(AbilityData data) => Data = data;

        public bool IsReady => CooldownRemaining <= 0f;
    }
}
