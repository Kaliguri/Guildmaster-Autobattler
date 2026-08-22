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
        /// <summary>
        /// Гильдия забега → слоты player-ростера. Берутся ТОЛЬКО помеченные
        /// <see cref="RosterSlot.InBattle"/>: в отряде до восьми мест, на арену выходят четверо
        /// (ГДД <c>preparation-screens</c> §2.1). Пустая/отсутствующая гильдия → пустой массив.
        /// <para>Из-за этого длина результата больше НЕ совпадает с гильдией, и место каждого слота
        /// едет в <see cref="PlayerSlot.GuildIndex"/> — по нему расстановка пишет позицию обратно.</para>
        /// </summary>
        public static PlayerSlot[] Resolve(RunState run, IContentDatabase content)
        {
            if (run?.Guild == null || run.Guild.Length == 0 || content == null)
                return System.Array.Empty<PlayerSlot>();

            // «Плохой» слот не выпадает, а откатывается на базовый кит: иначе одна опечатка в id
            // уводила бы бойца с арены молча. Выпасть слот может в двух аварийных случаях — пустая
            // запись в гильдии и отсутствие даже базового кита в БД; оба означают битые данные и оба
            // кричат в лог. Место в гильдии при этом не теряется: оно едет в PlayerSlot.GuildIndex.
            var slots = new List<PlayerSlot>(run.Guild.Length);
            for (int i = 0; i < run.Guild.Length; i++)
            {
                RosterSlot rs = run.Guild[i];
                if (rs == null)
                {
                    Debug.LogWarning("[GuildRoster] - пустая запись в run.Guild → слот пропущен");
                    continue;
                }

                if (!rs.InBattle) continue; // в запасе: на арену не выходит

                string relicId = string.IsNullOrEmpty(rs.RelicId) ? ContentIds.BaseRelic : rs.RelicId;
                if (!content.TryGet(relicId, out RelicData relic)
                    && !content.TryGet(ContentIds.BaseRelic, out relic))
                {
                    Debug.LogWarning($"[GuildRoster] - релик '{relicId}' не найден в контент-БД, и базового кита " +
                                     $"'{ContentIds.BaseRelic}' тоже нет → слот пропущен (индексы гильдии разъедутся)");
                    continue;
                }
                if (relic.Id != relicId)
                    Debug.LogWarning($"[GuildRoster] - релик '{relicId}' не найден в контент-БД → слот встаёт с базовым китом");

                VesselData vessel = null;
                if (!string.IsNullOrEmpty(rs.VesselId)) content.TryGet(rs.VesselId, out vessel);

                slots.Add(new PlayerSlot(relic, vessel, rs.SavedPosition,
                                         ResolveItems(rs.VesselItemIds, content),
                                         ResolveConsequences(rs.Injuries, content),
                                         i));
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

        /// <summary>
        /// Строковые id последствий → массив <see cref="ConsequenceData"/> (null, если ничего не
        /// разрешилось). Ненайденный id кричит в лог: молча потерянная травма — это боец, вошедший в
        /// бой здоровым после того, как игрок за него заплатил слотом.
        /// </summary>
        public static ConsequenceData[] ResolveConsequences(IReadOnlyList<Injury> injuries, IContentDatabase content)
        {
            if (injuries == null || injuries.Count == 0 || content == null) return null;

            var list = new List<ConsequenceData>(injuries.Count);
            foreach (Injury injury in injuries)
            {
                string id = injury?.Id;
                if (string.IsNullOrEmpty(id)) continue;
                if (content.TryGet(id, out ConsequenceData consequence)) list.Add(consequence);
                else Debug.LogWarning($"[GuildRoster] - последствие '{id}' не найдено в контент-БД → "
                                      + "боец выйдет в бой без него");
            }
            return list.Count == 0 ? null : list.ToArray();
        }
    }
}
