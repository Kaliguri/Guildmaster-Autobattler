using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Валидация конфиг-SO (вики «13» §4, §8 правило 6): страховка неизменности баланса (снапшот-дефолты
    /// = исторические значения фиксированных констант) и диапазоны/клампы SimTuningConfig и GameConfig.
    /// </summary>
    public sealed class ConfigValidationTests
    {
        // --- Страховка баланса: SimTuning.Default == прежние захардкоженные значения (пакет 2 не сдвинул баланс) ---

        [Test]
        public void SimTuningDefault_MatchesHistoricalConstants()
        {
            SimTuning d = SimTuning.Default;
            Assert.AreEqual(0.575f, d.BodyRadiusPerSize,         1e-6f, nameof(d.BodyRadiusPerSize));
            Assert.AreEqual(0.5f,   d.SeparationStrength,        1e-6f, nameof(d.SeparationStrength));
            Assert.AreEqual(1,      d.SeparationIterations,             nameof(d.SeparationIterations));
            Assert.AreEqual(0.35f,  d.SeparationSameTeamScale,   1e-6f, nameof(d.SeparationSameTeamScale));
            Assert.AreEqual(0.25f,  d.ProjectileHitRadiusFactor, 1e-6f, nameof(d.ProjectileHitRadiusFactor));
            Assert.AreEqual(5f,     d.ProjectileDespawnMargin,   1e-6f, nameof(d.ProjectileDespawnMargin));
            Assert.AreEqual(0.6f,   d.KiteFleeFactor,            1e-6f, nameof(d.KiteFleeFactor));
            Assert.AreEqual(500f,   d.GlobalSearchRadius,        1e-6f, nameof(d.GlobalSearchRadius));
        }

        // --- §8 правило 6: закоммиченный SimTuningConfig == код-дефолты (ловит утёкшие play-mode правки) ---

        [Test]
        public void SimTuningConfig_MatchesCodeDefaults()
        {
            SimTuningConfig cfg = LoadSingle<SimTuningConfig>();
            SimTuning s = cfg.ToSnapshot();
            SimTuning d = SimTuning.Default;

            // Единый источник правды: правка баланса меняет и SimTuning.Default, и ассет вместе (иначе — дрейф).
            Assert.AreEqual(d.BodyRadiusPerSize,         s.BodyRadiusPerSize,         1e-6f);
            Assert.AreEqual(d.SeparationStrength,        s.SeparationStrength,        1e-6f);
            Assert.AreEqual(d.SeparationIterations,      s.SeparationIterations);
            Assert.AreEqual(d.SeparationSameTeamScale,   s.SeparationSameTeamScale,   1e-6f);
            Assert.AreEqual(d.ProjectileHitRadiusFactor, s.ProjectileHitRadiusFactor, 1e-6f);
            Assert.AreEqual(d.ProjectileDespawnMargin,   s.ProjectileDespawnMargin,   1e-6f);
            Assert.AreEqual(d.KiteFleeFactor,            s.KiteFleeFactor,            1e-6f);
            Assert.AreEqual(d.GlobalSearchRadius,        s.GlobalSearchRadius,        1e-6f);
        }

        // --- §8 правило 5: диапазоны ---

        [Test]
        public void SimTuningConfig_ValuesInSaneRanges()
        {
            SimTuning s = LoadSingle<SimTuningConfig>().ToSnapshot();
            Assert.Greater(s.BodyRadiusPerSize, 0f);
            Assert.That(s.SeparationStrength, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.GreaterOrEqual(s.SeparationIterations, 1);
            Assert.That(s.SeparationSameTeamScale, Is.InRange(0f, 1f));
            Assert.Greater(s.ProjectileHitRadiusFactor, 0f);
            Assert.GreaterOrEqual(s.ProjectileDespawnMargin, 0f);
            Assert.That(s.KiteFleeFactor, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.Greater(s.GlobalSearchRadius, 0f);
        }

        [Test]
        public void GameConfig_ValuesInSaneRanges()
        {
            GameConfig g = LoadSingle<GameConfig>();
            Assert.That(g.DefaultMasterVolume, Is.InRange(0f, 1f));
            Assert.That(g.DefaultMusicVolume,  Is.InRange(0f, 1f));
            Assert.That(g.DefaultSfxVolume,    Is.InRange(0f, 1f));
            Assert.GreaterOrEqual(g.VesselItemSlots, 1);
            Assert.IsFalse(string.IsNullOrEmpty(g.DefaultLocale), "GameConfig.DefaultLocale пуст.");
        }

        private static T LoadSingle<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            Assert.AreEqual(1, guids.Length, $"Ожидается ровно один ассет {typeof(T).Name}.");
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
