using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Abilities
{
    /// <summary>
    /// Рантайм-состояние одной активной способности на юните (POCO, один бой): таймер кулдауна.
    /// Создаётся <see cref="RuntimeUnitFactory"/> из <see cref="AbilityData"/> реликвии (вики «12» §2.4).
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

        public AbilityRuntime(AbilityData data) => Data = data;

        public bool IsReady => CooldownRemaining <= 0f;
    }
}
