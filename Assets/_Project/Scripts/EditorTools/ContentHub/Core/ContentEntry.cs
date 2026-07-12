using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Одна запись индекса контента: ассет + метаданные + рёбра графа ссылок + (для юнитов) эффективные статы.
    /// Заполняется <see cref="ContentIndex"/>; страницы хаба — вьюхи над этим.
    /// </summary>
    public sealed class ContentEntry
    {
        public UnityEngine.Object Asset { get; }
        public string Id { get; }
        public Type Type { get; }
        public string Domain { get; }
        public string DisplayName { get; }
        public string Path { get; }

        /// <summary>Юнит (реликвия/враг) — участвует в стат-когортах и Balance-таблице.</summary>
        public UnitData Unit { get; }
        public bool IsUnit => Unit != null;

        // Граф ссылок в обе стороны (заполняется индексом после сбора всех записей).
        public readonly List<ContentEntry> Uses = new List<ContentEntry>();
        public readonly List<ContentEntry> UsedBy = new List<ContentEntry>();

        /// <summary>Эффективный стат-блок (только юниты) через <see cref="StatMath"/>; null для не-юнитов.</summary>
        public Stats EffectiveStats { get; internal set; }

        public ContentEntry(UnityEngine.Object asset, string id, string domain, string displayName, string path)
        {
            Asset = asset;
            Id = id;
            Type = asset != null ? asset.GetType() : null;
            Domain = domain;
            DisplayName = displayName;
            Path = path;
            Unit = asset as UnitData;
        }

        /// <summary>Эффективное значение стата (0, если запись не юнит).</summary>
        public float Effective(StatType stat) => EffectiveStats != null ? EffectiveStats.Get(stat) : 0f;
    }
}
