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

        public AbilityRuntime(AbilityData data) => Data = data;

        public bool IsReady => CooldownRemaining <= 0f;
    }
}
