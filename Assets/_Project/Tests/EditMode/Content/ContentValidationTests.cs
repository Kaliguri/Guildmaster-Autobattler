using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Валидация контент-каркаса (вики «13» §8, правила 1–3, 5): формат/уникальность id, полнота
    /// реестра, отсутствие null в <c>[SerializeReference]</c>-списках, кросс-инварианты диапазонов.
    /// Красный тест = ошибка данных в коммите, а не флейк — правится контент/Sync, не тест.
    /// </summary>
    public sealed class ContentValidationTests
    {
        private static readonly Regex IdFormat = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.Compiled);

        private static List<ContentDefinition> AllContent() => ContentIdUtility.FindAll();

        // --- §8 правило 1: id непустой, формат domain.name, домен по типу, без дубликатов ---

        [Test]
        public void AllContent_HasValidId()
        {
            foreach (ContentDefinition def in AllContent())
            {
                string assetPath = AssetDatabase.GetAssetPath(def);
                Assert.IsFalse(string.IsNullOrEmpty(def.Id), $"Empty id: {assetPath}");
                Assert.IsTrue(IdFormat.IsMatch(def.Id), $"Id '{def.Id}' not in domain.name format: {assetPath}");

                string expectedDomain = ContentDomains.GetDomain(def.GetType());
                Assert.IsTrue(def.Id.StartsWith(expectedDomain + "."),
                    $"Id '{def.Id}' domain mismatch: type {def.GetType().Name} expects '{expectedDomain}.' ({assetPath})");
            }
        }

        [Test]
        public void AllContent_IdsUnique()
        {
            var byId = new Dictionary<string, string>();
            foreach (ContentDefinition def in AllContent())
            {
                string path = AssetDatabase.GetAssetPath(def);
                if (byId.TryGetValue(def.Id, out string other))
                    Assert.Fail($"Duplicate content id '{def.Id}': {path} and {other}");
                byId[def.Id] = path;
            }
        }

        // --- §8 правило 2: ContentDatabase полон, без null, актуален после Sync ---

        [Test]
        public void ContentDatabase_ExistsAndComplete()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ContentDatabase)}");
            Assert.AreEqual(1, guids.Length,
                "Ровно один ContentDatabase.asset ожидается (Tools/Guildmaster/Sync Content Database).");

            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(db);

            Assert.IsFalse(db.Entries.Any(e => e == null), "ContentDatabase содержит null-ссылку.");

            var dbIds = db.Entries.Select(e => e.Id).OrderBy(x => x).ToArray();
            var projectIds = AllContent().Select(d => d.Id).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(projectIds, dbIds,
                "ContentDatabase не актуален: запусти Tools/Guildmaster/Sync Content Database.");
        }

        // --- §8 правило 3: [SerializeReference]-списки без null (следы missing types) ---

        [Test]
        public void EffectComponents_NoNullElements()
        {
            foreach (ContentDefinition def in AllContent())
            {
                if (def is not EffectData effect || effect.Components == null) continue;

                for (int i = 0; i < effect.Components.Length; i++)
                {
                    Assert.IsNotNull(effect.Components[i],
                        $"Effect '{effect.Id}' has a null [SerializeReference] component at [{i}] " +
                        $"(вероятно missing type): {AssetDatabase.GetAssetPath(def)}");
                }
            }
        }

        // --- §8 правило 4: обязательные ключи локализации по доменам ---

        [Test]
        public void RequiredLocalizationKeys_Exist()
        {
            foreach (ContentDefinition def in AllContent())
            {
                IReadOnlyList<string> missing = ContentLocalization.MissingKeys(def);
                Assert.IsEmpty(missing,
                    $"Content '{def.Id}' ({AssetDatabase.GetAssetPath(def)}) missing required localization key(s): " +
                    $"[{string.Join(", ", missing)}] — open it in the inspector and press 'Create missing keys'.");
            }
        }

        // --- §8 правило 5: диапазоны и кросс-инварианты ---

        [Test]
        public void Relics_AiProfileRanges()
        {
            foreach (RelicData relic in AllContent().OfType<RelicData>())
            {
                AIProfile ai = relic.Ai;
                if (ai == null) continue;

                // Гистерезис осмыслен только когда механика включена; у выключенных retreat/kite
                // значения нулевые (default) и порядок не важен.
                if (ai.Retreat.Enabled)
                    Assert.Less(ai.Retreat.FleeAtHpPct, ai.Retreat.ReturnAtHpPct,
                        $"Relic '{relic.Id}': при включённом Retreat FleeAtHpPct должен быть < ReturnAtHpPct (гистерезис).");
                if (ai.Kite.Enabled)
                    Assert.Less(ai.Kite.FleeDist, ai.Kite.FallbackDist,
                        $"Relic '{relic.Id}': при включённом Kite FleeDist должен быть < FallbackDist.");
                Assert.That(ai.PassiveThresholdPct, Is.InRange(0f, 1f),
                    $"Relic '{relic.Id}': PassiveThresholdPct вне [0..1].");
            }
        }

        [Test]
        public void StatsConfig_AttackSpeedClampOrdered()
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(StatsConfig)}"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.Less(cfg.AttackSpeedMin, cfg.AttackSpeedMax,
                    $"StatsConfig: AttackSpeedMin должен быть < AttackSpeedMax ({AssetDatabase.GetAssetPath(cfg)}).");
            }
        }

        // --- §8 правило 7: ссылочная целостность по id — враги энкаунтера существуют ---

        [Test]
        public void Encounters_EnemyIdsExist()
        {
            var enemyIds = new HashSet<string>(AllContent().OfType<EnemyData>().Select(e => e.Id));

            foreach (EncounterData enc in AllContent().OfType<EncounterData>())
            {
                if (enc.Units == null) continue;
                foreach (EncounterUnit u in enc.Units)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(u.EnemyId),
                        $"Encounter '{enc.Id}' has an empty EnemyId ({AssetDatabase.GetAssetPath(enc)}).");
                    Assert.IsTrue(enemyIds.Contains(u.EnemyId),
                        $"Encounter '{enc.Id}' references unknown enemy id '{u.EnemyId}' " +
                        $"({AssetDatabase.GetAssetPath(enc)}).");
                }
            }
        }
    }
}
