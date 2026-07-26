using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using Guildmaster.Data.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Контент ключевых слов (Трек Т, план §II.10.3/§II.10.7): каждый упомянутый в текстах термин
    /// существует, и у каждого термина заведены строки. Без этой проверки <c>[kw:poison]</c> тихо
    /// станет мусором в одной строке из двухсот, и найдёт это игрок, а не мы.
    /// </summary>
    public sealed class KeywordContentTests
    {
        private const string Ru = "ru";

        private static IEnumerable<KeywordDefinition> AllKeywords() =>
            ContentIdUtility.FindAll().OfType<KeywordDefinition>();

        [Test]
        public void EveryMentionedKeyword_Exists()
        {
            var known = new HashSet<string>(AllKeywords().Select(k => k.Id));
            var broken = new List<string>();

            foreach (KeyValuePair<string, string> entry in ContentLocalization.AllValues(Ru))
            {
                if (string.IsNullOrEmpty(entry.Value)) continue;
                foreach (string id in KeywordMarkup.Mentioned(entry.Value))
                {
                    if (!known.Contains(id)) broken.Add($"{entry.Key}: нет термина '{id}'");
                }
            }

            CollectionAssert.IsEmpty(broken,
                "В текстах есть ссылки на незаведённые ключевые слова:\n" + string.Join("\n", broken));
        }

        [Test]
        public void EveryKeyword_HasNameAndDescription()
        {
            var missing = new List<string>();
            foreach (KeywordDefinition kw in AllKeywords())
            {
                string path = AssetDatabase.GetAssetPath(kw);
                if (string.IsNullOrEmpty(ContentLocalization.GetValue(Ru, kw.Id + "." + ContentKeys.NameSuffix)))
                    missing.Add($"{kw.Id}.name ({path})");
                if (string.IsNullOrEmpty(ContentLocalization.GetValue(Ru, kw.Id + "." + ContentKeys.DescSuffix)))
                    missing.Add($"{kw.Id}.desc ({path})");
            }

            CollectionAssert.IsEmpty(missing,
                "У ключевых слов не заполнены RU-строки:\n" + string.Join("\n", missing));
        }

        [Test]
        public void KeywordIds_UseKeywordDomain()
        {
            foreach (KeywordDefinition kw in AllKeywords())
            {
                Assert.IsTrue(kw.Id.StartsWith(KeywordMarkup.Domain + "."),
                    $"Id термина '{kw.Id}' должен начинаться с '{KeywordMarkup.Domain}.' " +
                    $"({AssetDatabase.GetAssetPath(kw)}) — короткая запись [kw:burn] разворачивается именно так.");
            }
        }
    }
}
