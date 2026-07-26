using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Guildmaster.Presentation.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Audio
{
    /// <summary>
    /// Сцепка «код → каталог → FMOD-манифест». Ловит ровно те поломки, которые в рантайме дают тишину
    /// и никак себя не проявляют: опечатку в ключе, забытую запись в каталоге, рассинхрон каталога
    /// с пересобранным FMOD-проектом.
    /// </summary>
    public sealed class AudioCoverageTests
    {
        private const string CatalogPath = "Assets/_Project/ScriptableObjects/Audio/AudioCatalog.asset";
        private const string ManifestRelative = "../FMOD Project/Scripts/manifest.json";

        [Serializable] private sealed class ManifestDto { public EventDto[] events; }
        [Serializable] private sealed class EventDto { public string key; public string action; public bool isDefault; public string path; }

        private static AudioCatalog LoadCatalog() => AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);

        private static ManifestDto LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, ManifestRelative));
            return File.Exists(path) ? JsonUtility.FromJson<ManifestDto>(File.ReadAllText(path)) : null;
        }

        /// <summary>Ключи, которые код реально зовёт: литералы Play("…") и PlayUi("…") по всем скриптам.</summary>
        private static IEnumerable<(string Key, string File)> CalledKeys()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var direct = new Regex(@"Play\(""([a-z0-9_.]+)""\)");
            var ui = new Regex(@"PlayUi\(""([a-z0-9_]+)""\)");

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);
                foreach (Match m in direct.Matches(text)) yield return (m.Groups[1].Value, name);
                foreach (Match m in ui.Matches(text)) yield return ("ui." + m.Groups[1].Value + ".ui", name);
            }
        }

        [Test]
        public void EveryKeyCalledFromCode_ResolvesToAnEvent()
        {
            AudioCatalog catalog = LoadCatalog();
            Assert.IsNotNull(catalog, $"Каталог не найден: {CatalogPath}");

            var broken = new List<string>();
            foreach ((string key, string file) in CalledKeys())
            {
                if (!catalog.HasSound(key)) broken.Add($"{key} ({file})");
            }

            Assert.IsEmpty(broken,
                "Код зовёт ключи, которых нет в каталоге (в игре это тихий no-op):\n  " + string.Join("\n  ", broken));
        }

        [Test]
        public void Catalog_MatchesFmodManifest()
        {
            AudioCatalog catalog = LoadCatalog();
            ManifestDto manifest = LoadManifest();
            Assert.IsNotNull(catalog, $"Каталог не найден: {CatalogPath}");
            Assert.IsNotNull(manifest?.events, "Манифест FMOD не найден или пуст.");

            var manifestKeys = new HashSet<string>(manifest.events.Where(e => !e.isDefault).Select(e => e.key));
            var catalogKeys = new HashSet<string>(catalog.EntryKeys());

            var missingInCatalog = manifestKeys.Except(catalogKeys).OrderBy(k => k).ToArray();
            var missingInManifest = catalogKeys.Except(manifestKeys).OrderBy(k => k).ToArray();

            Assert.IsEmpty(missingInCatalog,
                "В манифесте есть событие, а в каталоге записи нет — перезапусти Alebardium/Audio/Populate Catalog:\n  "
                + string.Join("\n  ", missingInCatalog));
            Assert.IsEmpty(missingInManifest,
                "В каталоге запись есть, а в FMOD-манифесте события нет — каталог отстал от пересобранного проекта:\n  "
                + string.Join("\n  ", missingInManifest));
        }

        [Test]
        public void Catalog_HasNoEmptyEventReferences()
        {
            AudioCatalog catalog = LoadCatalog();
            Assert.IsNotNull(catalog, $"Каталог не найден: {CatalogPath}");

            var empty = catalog.KeysWithoutEvent().OrderBy(k => k).ToArray();
            Assert.IsEmpty(empty,
                "Записи каталога без FMOD-события (тишина в игре):\n  " + string.Join("\n  ", empty));
        }
    }
}
