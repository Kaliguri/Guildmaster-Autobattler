using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Players
{
    /// <summary>
    /// Соло-тело <see cref="ILocalPlayer"/>: команда локального игрока берётся из <see cref="GameConfig"/>
    /// (по умолчанию первая). В коопе сюда придёт команда из лобби, в PvP — у клиентов она будет разной.
    /// </summary>
    public sealed class SoloLocalPlayer : ILocalPlayer
    {
        public SoloLocalPlayer(GameConfig config) => Team = config != null ? config.LocalPlayerTeam : 0;

        public int Team { get; }
    }
}
