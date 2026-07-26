using System;
using System.Collections.Generic;
using System.IO;
using Guildmaster.Data.Definitions;
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

        private static readonly Dictionary<Type, string> Folders = new Dictionary<Type, string>
        {
            { typeof(RelicData),       "Relics"       },
            { typeof(EnemyData),       "Enemies"      },
            { typeof(VesselData),      "Vessels"      },
            { typeof(EffectData),      "Effects"      },
            { typeof(VfxData),         "Vfx"          },
            { typeof(TagData),         "Tags"         },
            { typeof(KeywordDefinition), "Keywords"   },
            { typeof(TraitData),       "Traits"       },
            { typeof(ConsequenceData), "Consequences" },
            { typeof(AIPresetData),    "AiPresets"    },
            { typeof(GuildmasterData), "Guildmasters" },
            { typeof(ItemData),        "Items"        },
            { typeof(RunModifierData), "RunModifiers" },
        };

        /// <summary>Все типы контента, которые менеджер умеет создавать (в порядке объявления).</summary>
        public static IEnumerable<Type> CreatableTypes => Folders.Keys;

        /// <summary>Полный путь целевой папки для типа (создаётся при необходимости).</summary>
        public static string FolderFor(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                if (Folders.TryGetValue(t, out string folder)) return $"{Root}/{folder}";
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
