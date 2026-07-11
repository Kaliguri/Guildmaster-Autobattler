using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor.Migrations
{
    /// <summary>
    /// Одноразовая миграция Фазы 4, пакет 1 (вики «13» §10 шаг 1): проставить canonical id всем
    /// существующим контент-ассетам, разложить их по folder-per-type (§0) и синхронизировать
    /// <see cref="ContentDatabase"/>. Идемпотентна (повторный прогон ничего не портит).
    /// ВЫПОЛНЕНО: пакет 1. Оставлена в репо по правилу 0.8 (история миграций); не удалять.
    /// </summary>
    public static class Phase4Package1Migration
    {
        private const string Root = "Assets/_Project/ScriptableObjects";

        [MenuItem("Tools/Guildmaster/Migrations/Phase 4 - Package 1 (ids + layout)")]
        public static void Run()
        {
            // Без StartAssetEditing-батча: свежесозданные папки (CreateFolder) регистрируются в AssetDatabase
            // только вне батча, иначе MoveAsset не видит целевую папку («Parent directory is not in asset database»).
            // Порядок важен: сначала присвоить id и СБРОСИТЬ на диск, потом двигать — MoveAsset реимпортит
            // ассет и обнулил бы несохранённую правку id (баг первого прогона).
            AssignIds();
            AssetDatabase.SaveAssets();
            MoveToDomainFolders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ContentDatabaseSync.Sync(verbose: true);
            Debug.Log("[Phase4Package1Migration] Done: ids assigned, assets laid out, ContentDatabase synced.");
        }

        // --- ids ---

        private static void AssignIds()
        {
            List<ContentDefinition> all = ContentIdUtility.FindAll();
            all.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // детерминированный порядок

            var taken = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (ContentDefinition def in all)
            {
                var so = new SerializedObject(def);
                SerializedProperty idProp = so.FindProperty("_id");
                string desired = DesiredId(def, idProp.stringValue);

                // Защита от коллизий (рукотворные дубли имён): суффикс _2, _3…
                if (taken.Contains(desired))
                {
                    string baseId = desired;
                    for (int n = 2; taken.Contains(desired); n++) desired = $"{baseId}_{n}";
                }

                if (idProp.stringValue != desired)
                {
                    idProp.stringValue = desired;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(def);
                }
                taken.Add(desired);
            }
        }

        // Эффекты: сохраняем существующий семантический id, префиксуя до effect.* (или генерим из имени,
        // если пусто). Прочие типы: генерим из имени, только если id ещё пуст (не перетираем осмысленные).
        private static string DesiredId(ContentDefinition def, string current)
        {
            if (def is EffectData)
            {
                if (string.IsNullOrEmpty(current)) return ContentDomains.MakeId(def.GetType(), def.name);
                return current.Contains('.') ? current : $"effect.{current}";
            }

            return string.IsNullOrEmpty(current)
                ? ContentDomains.MakeId(def.GetType(), def.name)
                : current;
        }

        // --- layout ---

        private static void MoveToDomainFolders()
        {
            EnsureFolder($"{Root}/Relics");
            EnsureFolder($"{Root}/Vessels");
            EnsureFolder($"{Root}/Effects");
            EnsureFolder($"{Root}/Configs");

            foreach (ContentDefinition def in ContentIdUtility.FindAll())
            {
                string target = def switch
                {
                    RelicData  => $"{Root}/Relics",
                    VesselData => $"{Root}/Vessels",
                    EffectData => $"{Root}/Effects",
                    _          => null,
                };
                if (target != null) MoveAsset(def, target);
            }

            // StatsConfig — не ContentDefinition, но по раскладке §0 живёт в Configs/.
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(StatsConfig)}"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (cfg != null) MoveAsset(cfg, $"{Root}/Configs");
            }
        }

        private static void MoveAsset(Object asset, string targetFolder)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string fileName = System.IO.Path.GetFileName(path);
            string target = $"{targetFolder}/{fileName}";
            if (path == target) return; // уже на месте

            string error = AssetDatabase.MoveAsset(path, target);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"[Phase4Package1Migration] Move failed: {path} → {target}: {error}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
