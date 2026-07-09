using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Формула сборки стата, dirty-кэш и управление модификаторами по источнику
    /// (вики «10» §5.2, «11» §1). Тесты headless — без StatsConfig SO.
    /// </summary>
    public sealed class StatsTests
    {
        private const StatType ZeroStat = StatType.MaxHP;          // NaturalDefault = 0
        private const StatType OneStat  = StatType.DamageDealtEff; // NaturalDefault = 1

        private static Stats NewStats() => new Stats(null);

        // --- Дефолты ---

        [Test]
        public void Get_NoModifiers_ZeroStatReturnsZero()
        {
            Assert.AreEqual(0f, NewStats().Get(ZeroStat));
        }

        [Test]
        public void Get_NoModifiers_OneStatReturnsOne()
        {
            Assert.AreEqual(1f, NewStats().Get(OneStat));
        }

        // --- Flat ---

        [Test]
        public void Flat_AddsToBase()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });
            Assert.AreEqual(100f, stats.Get(ZeroStat), 0.0001f);
        }

        [Test]
        public void MultipleFlats_Accumulate()
        {
            var stats = NewStats();
            var a = new object(); var b = new object();
            stats.AddModifiersFrom(a, new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });
            stats.AddModifiersFrom(b, new[] { Mod(ZeroStat, ModifierOp.Flat,  50f) });
            Assert.AreEqual(150f, stats.Get(ZeroStat), 0.0001f);
        }

        // --- PercentAdd ---

        [Test]
        public void PercentAdd_ScalesAfterFlat()
        {
            var stats = NewStats();
            var src = new object();
            stats.AddModifiersFrom(src, new[]
            {
                Mod(ZeroStat, ModifierOp.Flat,       200f),
                Mod(ZeroStat, ModifierOp.PercentAdd,  0.5f),
            });
            // (0 + 200) * (1 + 0.5) = 300
            Assert.AreEqual(300f, stats.Get(ZeroStat), 0.0001f);
        }

        // --- PercentMult ---

        [Test]
        public void PercentMult_MultiplicativePairs()
        {
            var stats = NewStats();
            var a = new object(); var b = new object();
            stats.AddModifiersFrom(a, new[] { Mod(OneStat, ModifierOp.PercentMult, 0.1f) });
            stats.AddModifiersFrom(b, new[] { Mod(OneStat, ModifierOp.PercentMult, 0.1f) });
            // base=1; (1+0)*(1+0)*(1+0.1)*(1+0.1) = 1.21
            Assert.AreEqual(1.21f, stats.Get(OneStat), 0.0001f);
        }

        // --- Полная формула ---

        [Test]
        public void FullFormula_AllThreeOps()
        {
            var stats = NewStats();
            var src = new object();
            stats.AddModifiersFrom(src, new[]
            {
                Mod(ZeroStat, ModifierOp.Flat,       200f),
                Mod(ZeroStat, ModifierOp.PercentAdd,  0.5f),
                Mod(ZeroStat, ModifierOp.PercentMult, 0.1f),
            });
            // (0 + 200) * (1 + 0.5) * (1 + 0.1) = 330
            Assert.AreEqual(330f, stats.Get(ZeroStat), 0.0001f);
        }

        // --- Remove ---

        [Test]
        public void RemoveSource_OnlyRemovesItsModifiers()
        {
            var stats = NewStats();
            var a = new object(); var b = new object();
            stats.AddModifiersFrom(a, new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });
            stats.AddModifiersFrom(b, new[] { Mod(ZeroStat, ModifierOp.Flat,  50f) });

            stats.RemoveModifiersFrom(a);
            Assert.AreEqual(50f, stats.Get(ZeroStat), 0.0001f);
        }

        [Test]
        public void RemoveUnknownSource_DoesNothing()
        {
            var stats = NewStats();
            var src = new object();
            stats.AddModifiersFrom(src, new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });

            stats.RemoveModifiersFrom(new object());
            Assert.AreEqual(100f, stats.Get(ZeroStat), 0.0001f);
        }

        // --- Dirty-кэш ---

        [Test]
        public void DirtyCache_RebuildsAfterAdd()
        {
            var stats = NewStats();
            var src = new object();
            float before = stats.Get(ZeroStat);

            stats.AddModifiersFrom(src, new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });
            float after = stats.Get(ZeroStat);

            Assert.AreEqual(0f,   before, 0.0001f);
            Assert.AreEqual(100f, after,  0.0001f);
        }

        [Test]
        public void DirtyCache_RebuildsAfterRemove()
        {
            var stats = NewStats();
            var src = new object();
            stats.AddModifiersFrom(src, new[] { Mod(ZeroStat, ModifierOp.Flat, 100f) });
            float before = stats.Get(ZeroStat);

            stats.RemoveModifiersFrom(src);
            float after = stats.Get(ZeroStat);

            Assert.AreEqual(100f, before, 0.0001f);
            Assert.AreEqual(0f,   after,  0.0001f);
        }

        // --- Хелпер ---

        private static StatModifier Mod(StatType stat, ModifierOp op, float value)
            => new StatModifier(stat, op, value);
    }
}
