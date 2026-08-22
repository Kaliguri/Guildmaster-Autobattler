using System.Collections.Generic;
using System.Reflection;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Рождение процедурного «Сосуда»: один сид — один и тот же человек, перков ровно два и разной
    /// полярности, пустой пул виден как ошибка, а не как «так и было».
    /// <para>Детерминизм здесь не украшение: в сейве живёт только сид, и человек собирается из него
    /// заново при каждой загрузке. Разъедься это — игрок при загрузке увидит другого человека.</para>
    /// </summary>
    public sealed class VesselFactoryTests
    {
        [Test]
        public void SameSeed_GivesSamePerson()
        {
            VesselNamePool pool = MakePool(new[] { "Ирма", "Кай", "Дан" }, new[] { "Тихий", "из каменоломни" });
            List<TraitData> traits = MakeTraits();

            VesselState first  = VesselFactory.Create(12345L, pool, traits);
            VesselState second = VesselFactory.Create(12345L, pool, traits);

            Assert.AreEqual(first.Name, second.Name, "Имя разворачивается из сида, значит повторяемо.");
            Assert.AreEqual(first.PositiveTraitId, second.PositiveTraitId);
            Assert.AreEqual(first.NegativeTraitId, second.NegativeTraitId);
            Assert.AreEqual(12345L, first.BirthSeed, "Сид рождения сохраняется: по нему человек и собирается.");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentPeople()
        {
            VesselNamePool pool = MakePool(new[] { "Ирма", "Кай", "Дан", "Сув", "Лех" }, new[] { "Тихий" });
            List<TraitData> traits = MakeTraits();

            var seen = new HashSet<string>();
            for (long seed = 1; seed <= 12; seed++) seen.Add(VesselFactory.Create(seed, pool, traits).Id);

            Assert.AreEqual(12, seen.Count, "У каждого сида свой человек: id строится от сида.");
        }

        [Test]
        public void Traits_ComeInPair_PlusAndMinus()
        {
            VesselState vessel = VesselFactory.Create(7L, MakePool(new[] { "Кай" }, null), MakeTraits());

            Assert.AreEqual("trait.plus", vessel.PositiveTraitId, "Положительный берётся из своей половины пула.");
            Assert.AreEqual("trait.minus", vessel.NegativeTraitId, "Отрицательный — из своей; человек без минуса нарушает модель.");
        }

        [Test]
        public void EmptyPool_ShoutsInsteadOfInventingNames()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("пул имён"));
            VesselState vessel = VesselFactory.Create(1L, MakePool(null, null), MakeTraits());

            Assert.IsNotEmpty(vessel.Name, "Человек всё равно рождается — но с техническим ярлыком, который видно.");
        }

        [Test]
        public void NoTraits_WarnsAndLeavesThemEmpty()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("пул перков"));
            VesselState vessel = VesselFactory.Create(1L, MakePool(new[] { "Кай" }, null), null);

            Assert.IsEmpty(vessel.PositiveTraitId);
            Assert.IsEmpty(vessel.NegativeTraitId);
        }

        // ── helpers ──────────────────────────────────────────────

        private static VesselNamePool MakePool(string[] names, string[] epithets)
        {
            var pool = ScriptableObject.CreateInstance<VesselNamePool>();
            SetPrivate(pool, "_names", names);
            SetPrivate(pool, "_epithets", epithets);
            return pool;
        }

        private static List<TraitData> MakeTraits()
        {
            var plus = ScriptableObject.CreateInstance<TraitData>();
            SetPrivate(plus, "_polarity", TraitPolarity.Positive);
            SetId(plus, "trait.plus");

            var minus = ScriptableObject.CreateInstance<TraitData>();
            SetPrivate(minus, "_polarity", TraitPolarity.Negative);
            SetId(minus, "trait.minus");

            return new List<TraitData> { plus, minus };
        }

        private static void SetId(ContentDefinition def, string id) =>
            typeof(ContentDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(target, value);
    }
}
