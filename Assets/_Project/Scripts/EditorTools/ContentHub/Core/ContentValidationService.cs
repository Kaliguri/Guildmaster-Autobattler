using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Guildmaster.Data.Definitions;
using UnityEditor;

namespace Guildmaster.ContentHub.Editor
{
    public enum IssueSeverity { Warning, Error }

    /// <summary>Одна находка валидации: где, насколько серьёзно, что.</summary>
    public sealed class ValidationIssue
    {
        public readonly ContentEntry Entry;
        public readonly IssueSeverity Severity;
        public readonly string Message;

        public ValidationIssue(ContentEntry entry, IssueSeverity severity, string message)
        {
            Entry = entry;
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// Правила валидации контента (перенос логики из v0 <c>ContentManagerWindow.ValidateSelected</c> в
    /// тестируемый сервис). Browser зовёт <see cref="Validate"/>, Doctor (P4) — <see cref="ValidateAll"/>.
    /// </summary>
    public static class ContentValidationService
    {
        private static readonly Regex IdFormat = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>Чистые проверки id-строки (без обращения к индексу): пустой / формат / домен под тип.</summary>
        public static List<string> ValidateIdString(string id, Type type)
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(id)) { issues.Add("id пуст"); return issues; }
            if (!IdFormat.IsMatch(id)) { issues.Add($"id '{id}' не формата domain.name"); return issues; }
            if (ContentDomains.TryGetDomain(type, out string domain) && !id.StartsWith(domain + "."))
                issues.Add($"домен id не соответствует типу (ожидался '{domain}.')");
            return issues;
        }

        /// <summary>Валидация одной записи (id, дубликаты по индексу, null-компоненты эффектов).</summary>
        public static List<ValidationIssue> Validate(ContentEntry entry)
        {
            var result = new List<ValidationIssue>();
            if (entry?.Asset == null) return result;

            // Битые object-ссылки (был назначен объект, тип пропал/удалён) — для любого ассета.
            using (var so = new SerializedObject(entry.Asset))
            {
                var p = so.GetIterator();
                while (p.NextVisible(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference
                        && p.objectReferenceValue == null
                        && p.objectReferenceEntityIdValue != default)
                        result.Add(new ValidationIssue(entry, IssueSeverity.Error,
                            $"битая ссылка (missing) в поле '{p.displayName}'"));
            }

            if (entry.Asset is ContentDefinition cd)
            {
                foreach (string m in ValidateIdString(cd.Id, cd.GetType()))
                    result.Add(new ValidationIssue(entry, IssueSeverity.Error, m));

                if (!string.IsNullOrEmpty(cd.Id))
                    foreach (var other in ContentIndex.Entries)
                        if (other != entry && other.Asset is ContentDefinition ocd && ocd.Id == cd.Id)
                        {
                            result.Add(new ValidationIssue(entry, IssueSeverity.Error, $"дубликат id с {other.Path}"));
                            break;
                        }

                if (cd is EffectData effect && effect.Components != null)
                    for (int i = 0; i < effect.Components.Length; i++)
                        if (effect.Components[i] == null)
                            result.Add(new ValidationIssue(entry, IssueSeverity.Error, $"null-компонент [{i}] (missing type)"));
            }

            return result;
        }

        /// <summary>Полный проход по индексу — основа Doctor (P4).</summary>
        public static List<ValidationIssue> ValidateAll()
        {
            var all = new List<ValidationIssue>();
            foreach (var e in ContentIndex.Entries)
                all.AddRange(Validate(e));
            return all;
        }
    }
}
