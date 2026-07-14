using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Редакторный мост «контент ↔ String Table <c>Content</c>» (вики «13» §5–6). Ключи —
    /// <c>{id}.name</c> / <c>{id}.desc</c>; здесь чтение/запись по локалям, создание недостающих ключей
    /// (по ассету и по домену) и ЕДИНАЯ политика обязательных суффиксов — её же берёт валидация (§8 п.4),
    /// чтобы «что создаём» и «что проверяем» не разъезжались.
    /// </summary>
    public static class ContentLocalization
    {
        public const string TableName = "Content";
        public const string NameSuffix = "name";
        public const string DescSuffix = "desc";

        private static readonly string[] NameOnly = { NameSuffix };
        private static readonly string[] NameAndDesc = { NameSuffix, DescSuffix };

        public static string KeyFor(ContentDefinition def, string suffix) => def.Id + "." + suffix;

        public static StringTableCollection Collection =>
            LocalizationEditorSettings.GetStringTableCollection(TableName);

        /// <summary>
        /// Обязательные суффиксы ключей для типа (пусто = контент не локализуется). Политика:
        /// tag / ai_preset — техническая таксономия и ИИ, игроку не видны; encounter — пока dev-конструкт
        /// без player-facing текста (пикер показывает id; получит name/desc, когда станет узлом карты в
        /// Фазе 5); event — player-facing текст живёт в собственных полях ассета (title/body/choices), а не
        /// в <c>.name</c>/<c>.desc</c>; полная локализация ивентов (перевод этих полей) — отдельным заходом
        /// (TODO loc в TextEventData); effect — <c>name</c> всегда, <c>desc</c> лишь у эффекта с иконкой
        /// (видим в бафф-баре); прочий контент — <c>name</c>+<c>desc</c>.
        /// </summary>
        public static IReadOnlyList<string> RequiredSuffixes(ContentDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return Array.Empty<string>();
            switch (ContentDomains.GetDomain(def.GetType()))
            {
                case "tag":
                case "ai_preset":
                case "encounter":
                case "battle_preset":
                case "event":
                    return Array.Empty<string>();
                case "effect":
                    return def is EffectData e && e.Icon != null ? NameAndDesc : NameOnly;
                default:
                    return NameAndDesc;
            }
        }

        /// <summary>Значение строки для локали (<c>null</c>, если ключа/таблицы нет).</summary>
        public static string GetValue(string localeCode, string key)
        {
            var table = Collection?.GetTable(new LocaleIdentifier(localeCode)) as StringTable;
            return table?.GetEntry(key)?.Value;
        }

        /// <summary>Записать значение строки для локали (заводит общий ключ и запись при отсутствии).</summary>
        public static void SetValue(string localeCode, string key, string value)
        {
            StringTableCollection col = Collection;
            if (col == null) return;
            EnsureKey(col, key);
            if (col.GetTable(new LocaleIdentifier(localeCode)) is not StringTable table) return;
            StringTableEntry entry = table.GetEntry(key) ?? table.AddEntry(key, string.Empty);
            entry.Value = value ?? string.Empty;
            EditorUtility.SetDirty(table);
        }

        /// <summary>Обязательные ключи ассета, которых ещё нет в таблице.</summary>
        public static IReadOnlyList<string> MissingKeys(ContentDefinition def)
        {
            var missing = new List<string>();
            StringTableCollection col = Collection;
            if (col == null) return missing;
            foreach (string suffix in RequiredSuffixes(def))
            {
                string key = KeyFor(def, suffix);
                if (!col.SharedData.Contains(key)) missing.Add(key);
            }
            return missing;
        }

        /// <summary>Создать недостающие ключи одного ассета. Возвращает число созданных.</summary>
        public static int CreateMissingKeys(ContentDefinition def)
        {
            StringTableCollection col = Collection;
            if (col == null) return 0;
            int created = 0;
            foreach (string suffix in RequiredSuffixes(def))
            {
                string key = KeyFor(def, suffix);
                if (!col.SharedData.Contains(key)) { col.SharedData.AddKey(key); created++; }
            }
            if (created > 0) { EditorUtility.SetDirty(col.SharedData); AssetDatabase.SaveAssets(); }
            return created;
        }

        /// <summary>Создать недостающие ключи для всех ассетов домена данного типа. Возвращает число созданных.</summary>
        public static int CreateMissingKeysForDomain(Type type)
        {
            int created = 0;
            foreach (ContentDefinition def in ContentIdUtility.FindAll())
                if (type.IsInstanceOfType(def)) created += CreateMissingKeys(def);
            return created;
        }

        private static void EnsureKey(StringTableCollection col, string key)
        {
            if (col.SharedData.Contains(key)) return;
            col.SharedData.AddKey(key);
            EditorUtility.SetDirty(col.SharedData);
        }
    }
}
