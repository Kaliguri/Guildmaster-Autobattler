using System.Collections.Generic;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;

namespace Guildmaster.UI
{
    /// <summary>
    /// ViewModel лоадаут-хаба (кольцо реликвий, Фаза 2): читает durable-гильдию забега (<see cref="RunState"/>) и
    /// отдаёт вью обзор команды (сосуды + надетый релик), запас реликвий и баннеры; применяет надевание/снятие
    /// релика через <see cref="RunStateService"/> (durable + автосейв). Строковые id резолвит по
    /// <see cref="IContentDatabase"/>. POCO по образцу <c>SettingsViewModel</c>/<c>LoadoutViewModel</c>: сервисы
    /// Core/Data/Guild, без ссылки на боевой слой.
    /// </summary>
    public sealed class LoadoutHubViewModel
    {
        private readonly RunStateService      _runStates;
        private readonly IContentDatabase     _content;
        private readonly ILocalizationService _loc;

        public LoadoutHubViewModel(RunStateService runStates, IContentDatabase content, ILocalizationService loc)
        {
            _runStates = runStates;
            _content   = content;
            _loc       = loc;
        }

        public int Gold => _runStates.Gold;

        /// <summary>Команда: сосуды с надетым реликом (весь кит). Пустая/отсутствующая гильдия → пустой список.</summary>
        public IReadOnlyList<LoadoutHubView.RosterEntry> Roster()
        {
            RunState run = _runStates.Current;
            if (run?.Guild == null) return System.Array.Empty<LoadoutHubView.RosterEntry>();

            var list = new List<LoadoutHubView.RosterEntry>(run.Guild.Length);
            foreach (RosterSlot s in run.Guild)
            {
                if (s == null) continue;
                VesselData vessel = null;
                if (!string.IsNullOrEmpty(s.VesselId)) _content.TryGet(s.VesselId, out vessel);
                RelicData relic = null;
                if (!string.IsNullOrEmpty(s.RelicId)) _content.TryGet(s.RelicId, out relic);
                list.Add(new LoadoutHubView.RosterEntry(vessel, relic));
            }
            return list;
        }

        /// <summary>Запас собранных, но не надетых реликов (в порядке <see cref="RunState.RelicInventory"/>).</summary>
        public IReadOnlyList<RelicData> Stash()
        {
            RunState run = _runStates.Current;
            if (run?.RelicInventory == null) return System.Array.Empty<RelicData>();

            var list = new List<RelicData>(run.RelicInventory.Length);
            foreach (string id in run.RelicInventory)
                if (!string.IsNullOrEmpty(id) && _content.TryGet(id, out RelicData r)) list.Add(r);
            return list;
        }

        /// <summary>Активные баннеры отряда (party-скоуп).</summary>
        public IReadOnlyList<ItemData> Banners()
        {
            RunState run = _runStates.Current;
            if (run?.PartyItemIds == null) return System.Array.Empty<ItemData>();

            var list = new List<ItemData>(run.PartyItemIds.Length);
            foreach (string id in run.PartyItemIds)
                if (!string.IsNullOrEmpty(id) && _content.TryGet(id, out ItemData it)) list.Add(it);
            return list;
        }

        /// <summary>Локализованное имя контента (<c>{id}.name</c>); фолбэк — сам id.</summary>
        public string NameOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            string n = _loc?.GetString($"{id}.name");
            return string.IsNullOrEmpty(n) ? id : n;
        }

        /// <summary>Надеть релик из запаса (индекс в <see cref="Stash"/>) на сосуд слота. Автосейв при успехе.</summary>
        public bool Equip(int slotIndex, int stashIndex)
        {
            IReadOnlyList<RelicData> stash = Stash();
            if (stashIndex < 0 || stashIndex >= stash.Count) return false;

            bool ok = _runStates.EquipRelic(slotIndex, stash[stashIndex].Id);
            if (ok) _runStates.Autosave();
            return ok;
        }

        /// <summary>Снять релик со слота обратно в запас (слот → базовый кит). Автосейв при успехе.</summary>
        public bool Unequip(int slotIndex)
        {
            bool ok = _runStates.UnequipRelic(slotIndex);
            if (ok) _runStates.Autosave();
            return ok;
        }
    }
}
