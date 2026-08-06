using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Persist-мир: единая точка «поставить отряд забега на чистую арену». Резолвит гильдию
    /// (<c>RunState.Guild</c>, строковые id) в боевой ростер (<see cref="GuildRoster.Resolve"/>) и ставит
    /// team 0 через <see cref="EncounterLoader.PlaceParty"/> (сброс сцены + спавн только игрока). Общий для
    /// старта забега (<c>WorldStageController</c>) и сборки боя (<see cref="BattleStartup"/>) — чтобы отряд
    /// всегда материализовался ОДНИМ путём из durable-состояния (полный HP, позиции из <c>SavedPosition</c>).
    /// </summary>
    public static class RosterDeployer
    {
        /// <summary>Поставить отряд забега на арену. No-op-безопасно: пустая гильдия → пустая арена (только сброс).</summary>
        public static void Deploy(EncounterLoader loader, RunState run, IContentDatabase content)
        {
            if (loader == null || run == null) return;
            PlayerSlot[] roster = GuildRoster.Resolve(run, content);
            ItemData[]   party  = GuildRoster.ResolveItems(run.PartyItemIds, content);
            loader.PlaceParty(roster, party);
        }
    }
}
