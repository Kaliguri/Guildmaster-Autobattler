using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// С чем родился этот бой: какой пресет играем и с каким сидом. Регистрируется в боевом скоупе
    /// хостом (<see cref="BattleHost"/>) в момент рождения.
    /// </summary>
    /// <remarks>
    /// <b>Почему параметры, а не команда «начни бой такой-то».</b> Пока скоуп жил вечно, бой приходил
    /// в него вызовом, и всё, что должно было родиться вместе с боем, приходилось доводить руками:
    /// генератор — пересевать, ленту — сбрасывать, состав — чистить. Теперь это состав рождения:
    /// скоуп, у которого нет параметров, — это скоуп без боя, а такого не бывает.
    /// </remarks>
    public sealed class BattleScopeParams
    {
        /// <summary>Какой бой играем. У боя вне забега (dev, Ристалище) пресет транзиентный.</summary>
        public readonly BattlePresetData Preset;

        /// <summary>Сид боевого генератора. Считает хост — он единственный, кто знает узел забега.</summary>
        public readonly ulong Seed;

        public BattleScopeParams(BattlePresetData preset, ulong seed)
        {
            Preset = preset;
            Seed   = seed;
        }
    }
}
