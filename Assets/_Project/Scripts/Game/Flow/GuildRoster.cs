using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Мост durable-гильдии игрока (<see cref="RunState"/>, строковые id) → player-ростер боя (Data-ссылки).
    /// Каждый слот = сосуд + надетый релик (весь кит) + предметы + стартовая позиция. Пустой/непроставленный
    /// релик → <c>relic.base</c> (дамми-кит), чтобы сосуд всегда был боеспособен. Чистая функция (без DI и
    /// мутаций) — берётся <see cref="IContentDatabase"/> для резолва id; легко тестируется на фейке БД.
    /// </summary>
    public static class GuildRoster
    {
        private const string BaseRelicId = "relic.base";

        /// <summary>Гильдия забега → слоты player-ростера. Пустая/отсутствующая гильдия → пустой массив.</summary>
        public static PlayerSlot[] Resolve(RunState run, IContentDatabase content)
        {
            if (run?.Guild == null || run.Guild.Length == 0 || content == null)
                return System.Array.Empty<PlayerSlot>();

            // ПОРЯДОК И ДЛИНА совпадают с run.Guild слот-в-слот: по индексу этого массива фаза расстановки
            // пишет назад позиции и надетые релики. Поэтому «плохой» слот не выпадает, а откатывается на
            // базовый кит — иначе одна опечатка в id сдвигала бы всю запись на соседний сосуд.
            var slots = new List<PlayerSlot>(run.Guild.Length);
            foreach (RosterSlot rs in run.Guild)
            {
                if (rs == null) continue;

                string relicId = string.IsNullOrEmpty(rs.RelicId) ? BaseRelicId : rs.RelicId;
                if (!content.TryGet(relicId, out RelicData relic)
                    && !content.TryGet(BaseRelicId, out relic))
                {
                    Debug.LogWarning($"[GuildRoster] - релик '{relicId}' не найден в контент-БД, и базового кита " +
                                     $"'{BaseRelicId}' тоже нет → слот пропущен (индексы гильдии разъедутся)");
                    continue;
                }
                if (relic.Id != relicId)
                    Debug.LogWarning($"[GuildRoster] - релик '{relicId}' не найден в контент-БД → слот встаёт с базовым китом");

                VesselData vessel = null;
                if (!string.IsNullOrEmpty(rs.VesselId)) content.TryGet(rs.VesselId, out vessel);

                slots.Add(new PlayerSlot(relic, vessel, rs.SavedPosition, ResolveItems(rs.VesselItemIds, content)));
            }
            return slots.ToArray();
        }

        /// <summary>Строковые id предметов → массив <see cref="ItemData"/> (null, если ничего не разрешилось).</summary>
        public static ItemData[] ResolveItems(IReadOnlyList<string> ids, IContentDatabase content)
        {
            if (ids == null || ids.Count == 0 || content == null) return null;

            var items = new List<ItemData>(ids.Count);
            foreach (string id in ids)
                if (!string.IsNullOrEmpty(id) && content.TryGet(id, out ItemData item)) items.Add(item);
            return items.Count == 0 ? null : items.ToArray();
        }
    }
}
