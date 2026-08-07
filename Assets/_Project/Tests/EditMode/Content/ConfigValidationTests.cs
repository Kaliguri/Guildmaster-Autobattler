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
            AssertMatchesCodeDefaults(cfg.ToSnapshot(), "ассет");
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
                AssertMatchesCodeDefaults(fresh.ToSnapshot(), "свежий SO");
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        /// <summary>
        /// Сверяет снапшот с <see cref="SimTuning.Default"/> ПО ВСЕМ полям структуры, перечисляя их
        /// рефлексией.
        /// </summary>
        /// <remarks>
        /// Рукописный список полей — сам по себе второй владелец: он отстаёт от структуры молча, и ровно
        /// это здесь и случилось. Проверялось 14 полей из 32 — восемнадцать позднейших (смещение,
        /// овертайм, спринт, рекаст, маскировка, комбо, отступление) могли разъехаться между ассетом и
        /// кодом, никого не потревожив, при том что стенд и зеркальные тесты гоняют бой на код-дефолтах.
        /// Найдено панелью аудита 2026-08-07.
        /// </remarks>
        private static void AssertMatchesCodeDefaults(SimTuning actual, string what)
        {
            SimTuning expected = SimTuning.Default;
            var drift = new System.Collections.Generic.List<string>();

            System.Reflection.FieldInfo[] fields = typeof(SimTuning).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.IsNotEmpty(fields, "Рефлексия не нашла полей SimTuning — страховка молчала бы всегда.");

            foreach (System.Reflection.FieldInfo f in fields)
            {
                object a = f.GetValue(actual);
                object e = f.GetValue(expected);
                bool same = a is float af && e is float ef
                    ? Mathf.Abs(af - ef) <= 1e-6f
                    : Equals(a, e);

                if (!same) drift.Add($"{f.Name}: {what} {a} против кода {e}");
            }

            Assert.IsEmpty(drift,
                "Единый источник правды: правка баланса меняет и SimTuning.Default, и ассет вместе.\n  "
                + string.Join("\n  ", drift));
        }

        /// <summary>
        /// Конфиг-ассет несёт ВСЕ поля своего класса, а не часть.
        /// <para>Поле, добавленное в C# после того, как ассет был сохранён, в файл не попадает — Unity молча
        /// подставляет код-дефолт при загрузке. Снаружи это выглядит как работающий конфиг, но владельцев у
        /// значения становится двое: часть полей играет из ассета, часть из кода, и дизайнер, который правит
        /// ассет, вторую часть не видит вовсе. У <c>GameConfig</c> так разъехалось 13 полей из 20, причём
        /// единственное, что ассет всё-таки держал против кода (вместимость реликвий 12 против 8), кодовые
        /// тесты продолжали проверять по коду (аудит 2026-07-26, T-8/CD-10/AC-17).</para>
        /// </summary>
        [TestCase(typeof(GameConfig))]
        [TestCase(typeof(SimTuningConfig))]
        [TestCase(typeof(StatsConfig))]
        [TestCase(typeof(ClassBalanceConfig))]
        public void ConfigAsset_CarriesEveryFieldOfItsClass(System.Type type)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
            Assert.AreEqual(1, guids.Length, $"Ожидается ровно один ассет {type.Name}.");

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            string yaml = System.IO.File.ReadAllText(path);

            var missing = new System.Collections.Generic.List<string>();
            SerializedProperty p = new SerializedObject(asset).GetIterator();
            bool enterChildren = true;
            while (p.NextVisible(enterChildren))
            {
                enterChildren = false;                       // только верхний уровень
                if (p.name == "m_Script" || p.name.StartsWith("m_")) continue;
                if (!yaml.Contains($"\n  {p.name}:")) missing.Add(p.name);
            }

            Assert.IsEmpty(missing,
                $"{type.Name}: этих полей нет в ассете, значит они приезжают из кода — " +
                $"пересохрани ассет: {string.Join(", ", missing)}");
        }

        // --- Лестницы дальности: две штуки, связанные сдвигом ---

        /// <summary>
        /// <c>CastRangeBand</c> — та же лестница, что <c>AttackRangeBand</c>, плюс нулевой дефолт
        /// «как у авто-атаки». Проверяем, что она и правда та же: имена, порядок и сдвиг ровно на единицу.
        /// </summary>
        /// <remarks>
        /// Связь между двумя enum держит одна строка приведения в <c>RuntimeUnitFactory</c>
        /// (<c>(AttackRangeBand)(ability.CastRange - 1)</c>). Добавь ступень в один список и забудь про
        /// второй — и умения молча съедут на ступень: компилятор такого приведения не замечает, а в бою
        /// это выглядит как «лучник почему-то кастует с дистанции метателя». Инвариант живёт между тремя
        /// файлами, значит держать его может только тест (панель аудита 2026-08-07).
        /// </remarks>
        [Test]
        public void RangeBands_CastLadderMirrorsAttackLadder()
        {
            string[] attack = System.Enum.GetNames(typeof(AttackRangeBand));
            string[] cast   = System.Enum.GetNames(typeof(CastRangeBand));

            Assert.AreEqual(nameof(CastRangeBand.LikeAutoAttack), cast[0],
                "Нулевая ступень каста — дефолт «как у авто-атаки»; на нём стоит весь сдвиг.");
            Assert.AreEqual(attack.Length + 1, cast.Length,
                "У лестницы каста ровно на одну ступень больше — дефолт. Ступень добавили в один список, а не в оба.");

            for (int i = 0; i < attack.Length; i++)
            {
                Assert.AreEqual(attack[i], cast[i + 1], $"Ступень {i}: имена лестниц разошлись.");

                var castBand = (CastRangeBand)System.Enum.Parse(typeof(CastRangeBand), cast[i + 1]);
                var attackBand = (AttackRangeBand)System.Enum.Parse(typeof(AttackRangeBand), attack[i]);
                Assert.AreEqual((int)attackBand, (int)castBand - 1,
                    $"Ступень {attack[i]}: приведение (AttackRangeBand)(CastRange - 1) даёт не её.");
            }
        }

        /// <summary>Число за ступенью есть у КАЖДОЙ ступени: иначе <c>RangeOf</c> тихо отдаёт ближний бой.</summary>
        [Test]
        public void StatsConfig_CarriesADistanceForEveryRangeBand()
        {
            StatsConfig cfg = LoadSingle<StatsConfig>();
            foreach (AttackRangeBand band in System.Enum.GetValues(typeof(AttackRangeBand)))
                Assert.Greater(cfg.RangeOf(band), 0f, $"Ступень {band} осталась без дистанции в StatsConfig.");
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
            Assert.GreaterOrEqual(g.PartyBannerSlots, 1, "Знамён на отряд — минимум одно (ГДД: два).");
            Assert.GreaterOrEqual(g.GuildSize, 1, "Гильдия не может быть пустой (ГДД: четверо).");
            Assert.IsFalse(string.IsNullOrEmpty(g.StartingRelicId), "Стартовая реликвия не задана.");

            // Экономика: инициализаторов у полей больше нет, значения живут только в ассете. Незаполненный
            // ассет иначе прошёл бы молча — и забег стартовал бы с нулём золота и бесплатной лавкой.
            Assert.Greater(g.StartGold, 0, "Стартовое золото забега — ноль.");
            Assert.Greater(g.BattleGoldReward, 0, "Награда за бой — ноль.");
            Assert.Greater(g.PriceCommon, 0, "Цена Common — ноль.");
            Assert.Greater(g.PriceCursed, 0, "Цена Cursed — ноль.");
            Assert.Greater(g.PriceDivine, 0, "Цена Divine — ноль.");
            Assert.Greater(g.ShopRerollCost, 0, "Реролл витрины бесплатен — это не задумано.");
            Assert.Greater(g.SellPercent, 0f, "Продажа реликвии не приносит ничего.");
            Assert.GreaterOrEqual(g.RelicCapacityBase, 1, "Вместимость коллекции — ноль.");
            Assert.GreaterOrEqual(g.RelicCapacityMax, g.RelicCapacityBase,
                "Потолок вместимости ниже стартовой — апгрейд невозможен.");
        }

        /// <summary>
        /// `GameConfig` — единственное место, где выбран играющий экземпляр стат-конфигов, поэтому его
        /// ссылки обязаны быть заполнены и указывать на те самые ассеты, что лежат в проекте.
        /// </summary>
        /// <remarks>
        /// До 2026-07-30 эту роль играли поля скоупов, и их пустоту ловил <c>SceneWiringTests</c>, ходя по
        /// сценам билда. После переноса ссылок внутрь ассета сцены о них больше ничего не знают — без этой
        /// проверки миграция забрала бы охрану и ничего не поставила взамен: пустой `Stats` уронил бы бой
        /// уже в `Configure`, но узналось бы это только запуском.
        /// </remarks>
        [Test]
        public void GameConfig_PointsAtTheProjectBalanceConfigs()
        {
            GameConfig g = LoadSingle<GameConfig>();

            Assert.IsNotNull(g.Stats,
                "GameConfig._statsConfig пуст — боевому скоупу негде взять armorK и реген ресурса.");
            Assert.IsNotNull(g.ClassBalance,
                "GameConfig._classBalanceConfig пуст — классовый каскад не применится, юниты уедут на MaxHP 0.");

            Assert.AreSame(LoadSingle<StatsConfig>(), g.Stats,
                "GameConfig ссылается не на тот StatsConfig, что лежит в проекте: бенчи и игра разойдутся.");
            Assert.AreSame(LoadSingle<ClassBalanceConfig>(), g.ClassBalance,
                "GameConfig ссылается не на тот ClassBalanceConfig, что лежит в проекте.");
        }

        private static T LoadSingle<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            Assert.AreEqual(1, guids.Length, $"Ожидается ровно один ассет {typeof(T).Name}.");
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
