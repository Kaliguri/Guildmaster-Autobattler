using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Инварианты учёта ПРИЗЫВОВ на стенде — три взгляда, которые не должны слипаться: кит сам, одно
    /// тело, кит вместе с армией. Живут тестом, а не комментарием: правило «тело не боец отряда»
    /// нарушается в чужом файле (любой бенч, считающий павших и остаток HP по <c>report.Units</c>), и
    /// без падающего теста нарушитель об этом не узнает.
    /// </summary>
    public sealed class SummonMetricsTests
    {
        private const int TenSeconds = 300; // 30 Гц

        /// <summary>Хозяин: бессмертный, чтобы бой не кончился раньше замера, и безоружный.</summary>
        private static RuntimeUnit Idle(int team, Vector2 pos)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("test", new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.Flat, 1e9f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 0f),
                new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 0f),
            });
            var unit = new RuntimeUnit { Team = team, Stats = stats, Position = pos, PreviousPosition = pos };
            unit.CurrentHP = 1e9f;
            return unit;
        }

        /// <summary>Тело, привязанное к хозяину: бьёт само, метрика обязана уехать хозяину.</summary>
        private static RuntimeUnit Body(RuntimeUnit owner, Vector2 pos, float dps, int id)
        {
            RuntimeUnit body = SyntheticUnits.ImmortalAttacker(owner.Team, pos, dps);
            body.Id = id;
            body.Summoner = owner;
            return body;
        }

        /// <summary>
        /// Бой, где тело появляется в первом же тике: расставленных призывов не бывает, поэтому и в
        /// тесте оно приходит через тот же шов, что в бою — <c>EnqueueUnitSpawn</c>.
        /// </summary>
        private static BattleReport RunWithBody(out int ownerId, out int bodyId)
        {
            var env = new SimEnvironment(1UL, null);
            RuntimeUnit owner = Idle(0, new Vector2(-1f, 0f));
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(owner, "owner", "owner"),
                new TrackedUnit(SyntheticUnits.ImmortalDummy(1, new Vector2(1f, 0f)), "dummy", "dummy"),
            };

            ownerId = 0;
            bodyId = tracked.Count;   // после переразметки Id бойцов — как это делает фабрика в бою
            RuntimeUnit body = Body(owner, new Vector2(0.5f, 0f), dps: 50f, id: bodyId);

            // Тело кладётся в очередь спавна до прогона: коллектор внутри Drive подписывается раньше
            // FlushSpawns, поэтому увидит рождение тем же событием, что и в бою.
            env.Sim.EnqueueUnitSpawn(body);
            return SimBench.Drive(env, tracked, RunMode.FixedDuration, TenSeconds);
        }

        [Test]
        public void SummonWork_GoesToOwnerRollup_NotToHisOwnDamage()
        {
            BattleReport report = RunWithBody(out int ownerId, out _);

            UnitMetric owner = report.Find(ownerId);
            SummonRollup army = report.Summons(ownerId);

            Assert.AreEqual(0.0, owner.DamageDealt, 1e-6,
                "Безоружный хозяин не наносит урон сам — урон тела не должен подмешиваться в его строку");
            Assert.Greater(army.DamageDealt, 0.0, "Урон тела обязан попасть в свёртку армии хозяина");
            Assert.AreEqual(1, army.Spawned, "Ровно одно тело за бой");
        }

        [Test]
        public void SummonKeepsOwnRow_AndPointsAtOwner()
        {
            BattleReport report = RunWithBody(out int ownerId, out int bodyId);

            UnitMetric body = report.Find(bodyId);
            Assert.IsNotNull(body, "У тела должна быть СВОЯ строка: «сколько бьёт один призыв» — свой вопрос");
            Assert.IsTrue(body.IsSummon, "Строка тела обязана быть помечена призывом");
            Assert.AreEqual(ownerId, body.OwnerId, "Тело должно указывать на своего хозяина");
            Assert.Greater(body.DamageDealt, 0.0, "Собственная строка тела считает его урон");
        }

        [Test]
        public void SummonIsNotCountedAsSquadMember()
        {
            BattleReport report = RunWithBody(out _, out _);

            // Ровно это правило нарушается снаружи: бенч, считающий отряд по report.Units, обязан
            // отбросить тела — иначе кит с армией «дешевле» проходит бой, просто разбавив отряд.
            int fighters = 0;
            for (int i = 0; i < report.Units.Count; i++)
                if (report.Units[i].Team == 0 && !report.Units[i].IsSummon) fighters++;

            Assert.AreEqual(1, fighters, "В команде 0 один боец — тело в состав отряда не входит");
        }

        [Test]
        public void ArmyUptime_CountsLivedTime_NotSummonCount()
        {
            BattleReport report = RunWithBody(out int ownerId, out _);
            SummonRollup army = report.Summons(ownerId);

            // Тело живёт весь бой → среднее число живых тел равно единице. Считать надо прожитое время,
            // а не число вызовов: восемь тел, поднятых к концу боя, и три, стоявшие всю дорогу, дают
            // одинаковый «Spawned», но разную силу кита.
            Assert.AreEqual(1.0, army.AvgAlive(report.DurationTicks), 0.02,
                "Тело прожило весь бой — аптайм армии равен одному телу");
            Assert.AreEqual(0.0, army.FirstSpawnSeconds, 1e-6, "Тело поднято на старте боя");
        }
    }
}
