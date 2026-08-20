using System;
using System.Collections.Generic;
using System.IO;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using UnityEditor;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Folder-per-type раскладка контент-ассетов (вики «13» §0). Единый источник целевых путей для
    /// контент-менеджера и миграций.
    /// </summary>
    public static class ContentPaths
    {
        public const string Root = "Assets/_Project/ScriptableObjects";

        // ДОМЕН → папка, а не тип → папка. Список самих типов живёт в ContentDomains и только там: пока
        // здесь был свой реестр, он отстал на четыре типа — Species, Encounter, BattlePreset и TextEvent
        // уезжали в Misc, а меню создания предлагало ровно те типы, что помечены к удалению, и прятало
        // живые (аудит 2026-07-26, T-24).
        private static readonly Dictionary<string, string> FoldersByDomain = new Dictionary<string, string>
        {
            { "relic",         "Relics"        },
            { "enemy",         "Enemies"       },
            { "species",       "Species"       },
            { "vessel",        "Vessels"       },
            { "effect",        "Effects"       },
            { "vfx",           "Vfx"           },
            { "tag",           "Tags"          },
            { KeywordMarkup.Domain, "Keywords" },
            { "trait",         "Traits"        },
            { "consequence",   "Consequences"  },
            { "ai_preset",     "AiPresets"     },
            { "guildmaster",   "Guildmasters"  },
            { "item",          "Items"         },
            { "run_mod",       "RunModifiers"  },
            { "encounter",     "Encounters"    },
            { "battle_preset", "BattlePresets" },
            { "event",         "Events"        },
            { "cursor",        "Cursors"       },
            { "outfit",        "Outfits"       },
        };

        /// <summary>Все типы контента, которые менеджер умеет создавать — из реестра доменов.</summary>
        public static IEnumerable<Type> CreatableTypes => ContentDomains.RegisteredTypes;

        /// <summary>
        /// Полный путь целевой папки для типа (создаётся при необходимости). Тип без домена или домен без
        /// папки — это незаполненный реестр, а не повод молча создать ассет в стороне, поэтому Misc остаётся
        /// видимым исходом, но домен для него уже обязан существовать.
        /// </summary>
        public static string FolderFor(Type type)
        {
            if (ContentDomains.TryGetDomain(type, out string domain)
                && FoldersByDomain.TryGetValue(domain, out string folder))
                return $"{Root}/{folder}";

            return $"{Root}/Misc";
        }

        /// <summary>Гарантировать существование папки-ассета (рекурсивно).</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
