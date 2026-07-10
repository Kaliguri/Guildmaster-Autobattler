using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// CRUD контента без GUI-вызовов (вики «13» §9): создание в folder-per-type с автогенерацией id,
    /// дубликация с РЕГЕНЕРАЦИЕЙ id, удаление с проверкой использований, синхронизация БД после операций.
    /// Окно контент-менеджера — тонкая оболочка над этим сервисом (шов: сменить IMGUI→UITK без переписи логики).
    /// </summary>
    public static class ContentCrudService
    {
        /// <summary>Создать новый ассет типа <paramref name="type"/> в его папке с уникальным id. Синхронизирует БД.</summary>
        public static ContentDefinition Create(Type type)
        {
            if (!typeof(ContentDefinition).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException($"{type.Name} is not a concrete ContentDefinition.");

            var asset = (ContentDefinition)ScriptableObject.CreateInstance(type);

            string folder = ContentPaths.FolderFor(type);
            ContentPaths.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{type.Name.Replace("Data", "")}.asset");
            AssetDatabase.CreateAsset(asset, path);

            AssignUniqueId(asset);
            AssetDatabase.SaveAssets();
            ContentDatabaseSync.Sync();
            return asset;
        }

        /// <summary>Дублировать ассет с новым уникальным id (вики «13» §2.3 п.4). Синхронизирует БД.</summary>
        public static ContentDefinition Duplicate(ContentDefinition source)
        {
            if (source == null) return null;
            string srcPath = AssetDatabase.GetAssetPath(source);
            string dstPath = AssetDatabase.GenerateUniqueAssetPath(srcPath);
            if (!AssetDatabase.CopyAsset(srcPath, dstPath)) return null;

            var copy = AssetDatabase.LoadAssetAtPath<ContentDefinition>(dstPath);
            AssignUniqueId(copy); // РЕГЕНЕРАЦИЯ id обязательна — иначе дубль id (валидация §8 п.1 покраснеет)
            AssetDatabase.SaveAssets();
            ContentDatabaseSync.Sync();
            return copy;
        }

        /// <summary>
        /// Удалить ассет, если он ни на что не ссылается. Иначе не удаляет и возвращает список использований
        /// (окно покажет диалог). При успехе синхронизирует БД.
        /// </summary>
        public static bool TryDelete(ContentDefinition def, out List<string> usages)
        {
            usages = FindUsages(def);
            if (usages.Count > 0) return false;

            string path = AssetDatabase.GetAssetPath(def);
            bool ok = AssetDatabase.DeleteAsset(path);
            if (ok) ContentDatabaseSync.Sync();
            return ok;
        }

        /// <summary>Пути ассетов/сцен/префабов, ссылающихся на <paramref name="def"/> (обратная зависимость).</summary>
        public static List<string> FindUsages(ContentDefinition def)
        {
            var result = new List<string>();
            if (def == null) return result;

            string targetPath = AssetDatabase.GetAssetPath(def);
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject t:Scene t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == targetPath) continue;
                // ContentDatabase ссылается на ВЕСЬ контент по дизайну — это не блокирующее использование
                // (Sync уберёт запись при удалении). Иначе удалить нельзя было бы ничего.
                if (AssetDatabase.LoadAssetAtPath<ContentDatabase>(path) != null) continue;

                foreach (string dep in AssetDatabase.GetDependencies(path, recursive: false))
                {
                    if (dep == targetPath) { result.Add(path); break; }
                }
            }
            return result;
        }

        // Уникальный id из имени ассета (domain.name, суффикс _2 при коллизии) через SerializedProperty.
        private static void AssignUniqueId(ContentDefinition def)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentDefinition other in ContentIdUtility.FindAll())
                if (other != def && !string.IsNullOrEmpty(other.Id)) taken.Add(other.Id);

            string id = ContentIdUtility.GenerateUniqueId(def, taken);
            var so = new SerializedObject(def);
            so.FindProperty("_id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }
    }
}
