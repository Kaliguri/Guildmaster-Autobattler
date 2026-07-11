using System.Collections.Generic;
using System.IO;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Наполнение <see cref="ContentDatabase"/> сканом проекта (вики «13» §3.6): никакого ручного
    /// перетаскивания. Идемпотентно; сортирует по id для стабильного git-диффа.
    /// </summary>
    public static class ContentDatabaseSync
    {
        private const string DatabaseDir  = "Assets/_Project/ScriptableObjects/Database";
        private const string DatabasePath = DatabaseDir + "/ContentDatabase.asset";

        [MenuItem("Tools/Guildmaster/Sync Content Database")]
        public static void SyncMenu() => Sync(verbose: true);

        /// <summary>Найти/создать БД, заполнить всеми контент-ассетами (сортировка по id), сохранить.</summary>
        public static ContentDatabase Sync(bool verbose = false)
        {
            ContentDatabase db = FindOrCreate();

            List<ContentDefinition> all = ContentIdUtility.FindAll();
            all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            db.EditorSetEntries(all);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            if (verbose)
                Debug.Log($"[ContentDatabase] Synced {all.Count} entries → {DatabasePath}");
            return db;
        }

        /// <summary>Существующая БД (первая найденная) или новая в <see cref="DatabasePath"/>.</summary>
        public static ContentDatabase FindOrCreate()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ContentDatabase)}");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));

            Directory.CreateDirectory(DatabaseDir);
            var db = ScriptableObject.CreateInstance<ContentDatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ContentDatabase] Created {DatabasePath}");
            return db;
        }
    }
}
