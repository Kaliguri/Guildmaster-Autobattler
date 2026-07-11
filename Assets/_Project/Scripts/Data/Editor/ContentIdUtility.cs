using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEditor;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Editor-помощник id/реестра: поиск всех контент-ассетов и генерация уникального id
    /// (вики «13» §2.3). Общая точка для Sync БД, миграций и (Фаза 4, пакет 5) контент-менеджера.
    /// </summary>
    public static class ContentIdUtility
    {
        /// <summary>
        /// Все <see cref="ContentDefinition"/>-ассеты проекта. Ищем среди ScriptableObject и фильтруем
        /// по типу — надёжно для абстрактной базы (не зависим от поведения фильтра <c>t:</c> по наследникам).
        /// </summary>
        public static List<ContentDefinition> FindAll()
        {
            var result = new List<ContentDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ContentDefinition>(path);
                if (def != null) result.Add(def);
            }
            return result;
        }

        /// <summary>
        /// Уникальный id для <paramref name="def"/> из имени ассета: <c>domain.name</c>, при занятости —
        /// суффикс <c>_2</c>, <c>_3</c>… (плейсхолдер до осмысленного имени, вики «13» §2.3 п.4). Множество
        /// занятых id передаётся вызывающим (обычно ids всех прочих ассетов).
        /// </summary>
        public static string GenerateUniqueId(ContentDefinition def, ISet<string> taken)
        {
            string baseId = ContentDomains.MakeId(def.GetType(), def.name);
            if (!taken.Contains(baseId)) return baseId;

            for (int n = 2; ; n++)
            {
                string candidate = $"{baseId}_{n}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
