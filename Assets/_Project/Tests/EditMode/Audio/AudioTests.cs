using System.Collections.Generic;
using System.Linq;
using Guildmaster.Presentation.Audio;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Audio
{
    /// <summary>
    /// Резолвер аудио-ключей (вики «13» §7) на фейк-каталоге: точная запись → дефолт действия → тишина.
    /// Юнит-тест без ассетов и FMOD — только логика приоритетов.
    /// </summary>
    public sealed class AudioResolverTests
    {
        private sealed class FakeCatalog : IAudioCatalog
        {
            private readonly HashSet<string> _keys;
            private readonly HashSet<AudioAction> _defaults;

            public FakeCatalog(IEnumerable<string> keys, IEnumerable<AudioAction> defaults)
            {
                _keys = new HashSet<string>(keys ?? System.Array.Empty<string>());
                _defaults = new HashSet<AudioAction>(defaults ?? System.Array.Empty<AudioAction>());
            }

            public bool Contains(string key) => _keys.Contains(key);
            public bool HasDefault(AudioAction action) => _defaults.Contains(action);
        }

        [Test]
        public void ExactEntry_WinsOverDefault()
        {
            var catalog = new FakeCatalog(
                new[] { "relic.defender.attack" },
                new[] { AudioAction.Attack });
            var resolver = new AudioResolver(catalog);

            Assert.AreEqual("relic.defender.attack", resolver.Resolve("relic.defender", AudioAction.Attack));
        }

        [Test]
        public void FallsBackToActionDefault_WhenNoExactEntry()
        {
            var catalog = new FakeCatalog(System.Array.Empty<string>(), new[] { AudioAction.Hit });
            var resolver = new AudioResolver(catalog);

            Assert.AreEqual("hit", resolver.Resolve("relic.whatever", AudioAction.Hit));
        }

        [Test]
        public void Silence_WhenNoEntryAndNoDefault()
        {
            var catalog = new FakeCatalog(System.Array.Empty<string>(), System.Array.Empty<AudioAction>());
            var resolver = new AudioResolver(catalog);

            Assert.IsNull(resolver.Resolve("relic.whatever", AudioAction.Death));
        }

        [Test]
        public void NullContentId_UsesActionDefault()
        {
            var catalog = new FakeCatalog(System.Array.Empty<string>(), new[] { AudioAction.Ui });
            var resolver = new AudioResolver(catalog);

            Assert.AreEqual("ui", resolver.Resolve(null, AudioAction.Ui));
        }

        [Test]
        public void NullCatalog_IsSilent()
        {
            var resolver = new AudioResolver(null);
            Assert.IsNull(resolver.Resolve("relic.defender", AudioAction.Attack));
        }

        [Test]
        public void ActionKey_IsCanonicalLowercase()
        {
            Assert.AreEqual("attack", AudioResolver.ActionKey(AudioAction.Attack));
            Assert.AreEqual("hit", AudioResolver.ActionKey(AudioAction.Hit));
            Assert.AreEqual("death", AudioResolver.ActionKey(AudioAction.Death));
            Assert.AreEqual("cast", AudioResolver.ActionKey(AudioAction.Cast));
            Assert.AreEqual("ui", AudioResolver.ActionKey(AudioAction.Ui));
        }
    }

    /// <summary>
    /// Валидация ассетов <see cref="AudioCatalog"/> (вики «13» §7 шаг 3): без дублей ключей и дефолты
    /// покрывают весь канонический список действий. Красный тест = битый каталог в коммите.
    /// </summary>
    public sealed class AudioCatalogValidationTests
    {
        private static IEnumerable<AudioCatalog> AllCatalogs() =>
            AssetDatabase.FindAssets($"t:{nameof(AudioCatalog)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AudioCatalog>(AssetDatabase.GUIDToAssetPath(guid)));

        [Test]
        public void Catalogs_HaveNoDuplicateOrEmptyKeys()
        {
            foreach (AudioCatalog catalog in AllCatalogs())
            {
                string path = AssetDatabase.GetAssetPath(catalog);
                var seen = new HashSet<string>();
                foreach (AudioCatalog.Entry entry in catalog.Entries)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(entry.Key), $"Empty entry key in {path}.");
                    Assert.IsTrue(seen.Add(entry.Key), $"Duplicate key '{entry.Key}' in {path}.");
                }
            }
        }

        [Test]
        public void Catalogs_DefaultsCoverCanonicalActions()
        {
            foreach (AudioCatalog catalog in AllCatalogs())
            {
                string path = AssetDatabase.GetAssetPath(catalog);
                foreach (AudioAction action in System.Enum.GetValues(typeof(AudioAction)))
                    Assert.IsTrue(catalog.HasDefault(action),
                        $"Catalog {path} is missing a default for action '{action}'.");
            }
        }
    }
}
