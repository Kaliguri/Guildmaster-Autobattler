using System;
using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.ContentHub.Editor;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    /// <summary>
    /// Контракт «таблица не врёт»: StatMath считает эффективные статы ТЕМ ЖЕ путём, что и сим
    /// (переиспользует <see cref="Stats"/>), и производные величины совпадают с сим-таймингом.
    /// </summary>
    public sealed class StatMathTests
    {
        private static readonly FieldInfo StatsField =
            typeof(UnitData).GetField("_stats", BindingFlags.NonPublic | BindingFlags.Instance);

        [Test]
        public void AttacksPerSecond_MatchesTickQuantization()
        {
            foreach (float speed in new[] { 0.5f, 0.9f, 1f, 1.7f, 2.5f })
            {
                int interval = AttackTiming.IntervalTicks(speed);
                float expected = (float)SimConstants.TickRate / interval;
                Assert.AreEqual(expected, AttackTiming.AttacksPerSecond(speed), 1e-4f, $"speed={speed}");
            }
        }

        [Test]
        public void AttacksPerSecond_ZeroSpeed_IsZero()
        {
            Assert.AreEqual(0f, AttackTiming.AttacksPerSecond(0f), 1e-6f);
        }

        [Test]
        public void AutoAttackDps_IsDamageTimesRateTimesDealtEff()
        {
            var config = ScriptableObject.CreateInstance<StatsConfig>();
            try
            {
                var stats = new Stats(config);
                stats.AddModifiersFrom(this, new[]
                {
                    new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 50f),
                    new StatModifier(StatType.AttackSpeed, ModifierOp.Flat, 1f),
                    // DamageDealtEff остаётся натуральным дефолтом 1.0
                });

                Assert.AreEqual(1f, AttackTiming.AttacksPerSecond(1f), 1e-4f);
                Assert.AreEqual(50f, StatMath.AutoAttackDps(stats), 1e-3f);
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void BuildEffective_MatchesDirectStatsBake()
        {
            var config = ScriptableObject.CreateInstance<StatsConfig>();
            var relic = ScriptableObject.CreateInstance<RelicData>();
            try
            {
                var mods = new[]
                {
                    new StatModifier(StatType.MaxHP, ModifierOp.Flat, 320f),
                    new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 40f),
                    new StatModifier(StatType.AutoAttackDamage, ModifierOp.PercentAdd, 0.25f),
                    new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 3.5f),
                };
                StatsField.SetValue(relic, mods);

                var eff = StatMath.BuildEffective(relic, config);

                // Эталон собирается ТЕМ ЖЕ каскадом, что и бой: базовые слои первыми, персона поверх.
                // Ступень дальности — базовый слой, а не модификатор, и в стат-блоке её быть не может.
                var expected = new Stats(config);
                RangeBaseline.Apply(expected, relic, config);
                expected.AddModifiersFrom(relic, mods);

                foreach (StatType st in Enum.GetValues(typeof(StatType)))
                    Assert.AreEqual(expected.Get(st), eff.Get(st), 1e-4f, $"stat={st}");

                // спот-чек формулы: 40 flat, +25% add → 50
                Assert.AreEqual(50f, eff.Get(StatType.AutoAttackDamage), 1e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(relic);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void BuildEffective_NoModifiers_EqualsConfigDefaults()
        {
            var config = ScriptableObject.CreateInstance<StatsConfig>();
            var relic = ScriptableObject.CreateInstance<RelicData>();
            try
            {
                var eff = StatMath.BuildEffective(relic, config);
                foreach (StatType st in Enum.GetValues(typeof(StatType)))
                {
                    // Дальность в дефолтах конфига не живёт намеренно: её владелец — ступень юнита
                    // (RangeBaseline), а «пусто» означало бы бойца, который не достаёт ни до кого.
                    if (st == StatType.AttackRange) continue;
                    Assert.AreEqual(config.GetDefault(st), eff.Get(st), 1e-4f, $"stat={st}");
                }

                Assert.AreEqual(config.RangeOf(relic.RangeBand) * (1f + relic.RangeAdjustPct),
                    eff.Get(StatType.AttackRange), 1e-4f, "дальность приходит ступенью, а не дефолтом конфига");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(relic);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
