using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Детерминизм PRNG — фундамент воспроизводимости боя (реплеи, host-auth синхрон).
    /// Тесты проверяют: одинаковый сид → одинаковая последовательность; диапазоны; отпечаток.
    /// </summary>
    public sealed class XorShiftRngTests
    {
        private const ulong Seed = 123456789UL;
        private const int Samples = 1000;

        [Test]
        public void SameSeed_ProducesIdenticalUIntSequence()
        {
            var a = new XorShiftRng(Seed);
            var b = new XorShiftRng(Seed);

            for (int i = 0; i < Samples; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"Расхождение на шаге {i}");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new XorShiftRng(Seed);
            var b = new XorShiftRng(Seed + 1UL);

            bool anyDifference = false;
            for (int i = 0; i < Samples; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.IsTrue(anyDifference, "Разные сиды дали идентичную последовательность");
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new XorShiftRng(Seed);

            for (int i = 0; i < Samples; i++)
            {
                float value = rng.NextFloat();
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }
        }

        [Test]
        public void NextFloat_Range_StaysWithinBounds()
        {
            var rng = new XorShiftRng(Seed);
            const float min = -5f;
            const float max = 12.5f;

            for (int i = 0; i < Samples; i++)
            {
                float value = rng.NextFloat(min, max);
                Assert.GreaterOrEqual(value, min);
                Assert.Less(value, max);
            }
        }

        [Test]
        public void NextInt_StaysInHalfOpenRange()
        {
            var rng = new XorShiftRng(Seed);
            const int min = 3;
            const int max = 9;

            for (int i = 0; i < Samples; i++)
            {
                int value = rng.NextInt(min, max);
                Assert.GreaterOrEqual(value, min);
                Assert.Less(value, max);
            }
        }

        [Test]
        public void NextInt_EmptyOrInvertedRange_ReturnsMin()
        {
            var rng = new XorShiftRng(Seed);

            Assert.AreEqual(7, rng.NextInt(7, 7), "Пустой диапазон должен вернуть min");
            Assert.AreEqual(7, rng.NextInt(7, 2), "Перевёрнутый диапазон должен вернуть min");
        }

        [Test]
        public void Chance_BoundaryProbabilities_AreDeterministic()
        {
            var rng = new XorShiftRng(Seed);

            for (int i = 0; i < Samples; i++)
            {
                Assert.IsFalse(rng.Chance(0f), "Chance(0) должен быть всегда false");
                Assert.IsTrue(rng.Chance(1f), "Chance(1) должен быть всегда true");
            }
        }

        [Test]
        public void Snapshot_SameSeedAndDraws_MatchesFingerprint()
        {
            var a = new XorShiftRng(Seed);
            var b = new XorShiftRng(Seed);

            for (int i = 0; i < Samples; i++)
            {
                a.NextUInt();
                b.NextUInt();
            }

            Assert.AreEqual(a.Snapshot(), b.Snapshot(), "Отпечатки состояния разошлись при равных сиде и числе бросков");
        }

        [Test]
        public void Snapshot_ChangesAfterAdvancing()
        {
            var rng = new XorShiftRng(Seed);

            ulong before = rng.Snapshot();
            rng.NextUInt();
            ulong after = rng.Snapshot();

            Assert.AreNotEqual(before, after, "Отпечаток не изменился после продвижения генератора");
        }

        [Test]
        public void Reseed_MatchesFreshInstanceSequence()
        {
            // Persist-мир: сервис не пересоздаётся между боями — Reseed должен дать РОВНО ту же
            // последовательность, что свежий инстанс с тем же сидом (иначе теряется воспроизводимость).
            var reused = new XorShiftRng(Seed);
            for (int i = 0; i < Samples; i++) reused.NextUInt(); // «уводим» состояние прошлым боем

            const ulong battleSeed = 987654321UL;
            reused.Reseed(battleSeed);
            var fresh = new XorShiftRng(battleSeed);

            for (int i = 0; i < Samples; i++)
                Assert.AreEqual(fresh.NextUInt(), reused.NextUInt(), $"Reseed разошёлся со свежим инстансом на шаге {i}");
        }

        [Test]
        public void Reseed_NewSeed_DivergesFromPreReseedSequence()
        {
            var rng = new XorShiftRng(Seed);
            uint firstBefore = rng.NextUInt();

            rng.Reseed(Seed + 42UL); // другой сид → генератор не должен «застрять» на прежней последовательности
            bool anyDifference = false;
            var reference = new XorShiftRng(Seed);
            reference.NextUInt(); // тот же индекс, что уже сдвинули выше
            for (int i = 0; i < Samples; i++)
            {
                if (rng.NextUInt() != reference.NextUInt()) { anyDifference = true; break; }
            }

            Assert.IsTrue(anyDifference, "Reseed новым сидом дал прежнюю последовательность");
            Assert.AreNotEqual(0u, firstBefore); // защита от тривиального прохода на нулях
        }
    }

    /// <summary>Константы тиковой симуляции — защита от случайного рассогласования частот.</summary>
    public sealed class SimConstantsTests
    {
        [Test]
        public void TickDelta_IsReciprocalOfTickRate()
        {
            Assert.AreEqual(1f / SimConstants.TickRate, SimConstants.TickDelta);
        }

        [Test]
        public void AiTickInterval_DividesTickRate()
        {
            Assert.AreEqual(SimConstants.TickRate / SimConstants.AiTickRate, SimConstants.AiTickInterval);
            Assert.AreEqual(3, SimConstants.AiTickInterval);
        }
    }
}
