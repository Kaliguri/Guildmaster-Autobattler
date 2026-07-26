using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Валидация конфиг-SO (вики «13» §4, §8 правило 6): страховка неизменности баланса (снапшот-дефолты
    /// = исторические значения фиксированных констант) и диапазоны/клампы SimTuningConfig и GameConfig.
    /// </summary>
    public sealed class ConfigValidationTests
    {
        // Снапшот-тест конкретных значений баланса убран намеренно: тюнинг (BodyRadiusPerSize и пр.)
        // крутится осознанно и не раз — хардкод-снапшот только мешал бы. Реальные баги ловят проверки
        // ниже: консистентность (ассет == код-дефолт) и разумные диапазоны (>0 и т.п.).

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
            Assert.AreEqual(d.FleeThreatWeight,          s.FleeThreatWeight,          1e-6f);
            Assert.AreEqual(d.FleeHomeWeight,            s.FleeHomeWeight,            1e-6f);
            Assert.AreEqual(d.FleeWallWeight,            s.FleeWallWeight,            1e-6f);
            Assert.AreEqual(d.FleeWallMargin,            s.FleeWallMargin,            1e-6f);
            Assert.AreEqual(d.FleeThreatRadius,          s.FleeThreatRadius,          1e-6f);
            Assert.AreEqual(d.KiteStrafeWeight,          s.KiteStrafeWeight,          1e-6f);
        }

        /// <summary>
        /// Дефолты полей SO — тоже <see cref="SimTuning.Default"/>, а не своя копия чисел.
        /// <para>Прежняя страховка сравнивала ассет с кодом, и они совпадали; расходился ТРЕТИЙ владелец —
        /// C#-инициализаторы <c>SimTuningConfig</c> (радиус тела 0.575 против играемых 0.3). Он невидим,
        /// пока ассет уже существует, и выстреливает у того, кто создаст новый через Create Asset Menu
        /// (аудит 2026-07-26, UA-3/AC-18/T-2).</para>
        /// </summary>
        [Test]
        public void FreshSimTuningConfig_StartsFromCodeDefaults()
        {
            var fresh = ScriptableObject.CreateInstance<SimTuningConfig>();
            try
            {
                SimTuning s = fresh.ToSnapshot();
                SimTuning d = SimTuning.Default;

                Assert.AreEqual(d.BodyRadiusPerSize,         s.BodyRadiusPerSize,         1e-6f);
                Assert.AreEqual(d.SeparationStrength,        s.SeparationStrength,        1e-6f);
                Assert.AreEqual(d.SeparationIterations,      s.SeparationIterations);
                Assert.AreEqual(d.SeparationSameTeamScale,   s.SeparationSameTeamScale,   1e-6f);
                Assert.AreEqual(d.ProjectileHitRadiusFactor, s.ProjectileHitRadiusFactor, 1e-6f);
                Assert.AreEqual(d.ProjectileDespawnMargin,   s.ProjectileDespawnMargin,   1e-6f);
                Assert.AreEqual(d.KiteFleeFactor,            s.KiteFleeFactor,            1e-6f);
                Assert.AreEqual(d.GlobalSearchRadius,        s.GlobalSearchRadius,        1e-6f);
                Assert.AreEqual(d.FleeThreatWeight,          s.FleeThreatWeight,          1e-6f);
                Assert.AreEqual(d.FleeHomeWeight,            s.FleeHomeWeight,            1e-6f);
                Assert.AreEqual(d.FleeWallWeight,            s.FleeWallWeight,            1e-6f);
                Assert.AreEqual(d.FleeWallMargin,            s.FleeWallMargin,            1e-6f);
                Assert.AreEqual(d.FleeThreatRadius,          s.FleeThreatRadius,          1e-6f);
                Assert.AreEqual(d.KiteStrafeWeight,          s.KiteStrafeWeight,          1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
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
            Assert.GreaterOrEqual(s.FleeThreatWeight, 0f);
            Assert.GreaterOrEqual(s.FleeHomeWeight, 0f);
            Assert.GreaterOrEqual(s.FleeWallWeight, 0f);
            Assert.GreaterOrEqual(s.FleeWallMargin, 0f);
            Assert.Greater(s.FleeThreatRadius, 0f);
            Assert.GreaterOrEqual(s.KiteStrafeWeight, 0f);
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
