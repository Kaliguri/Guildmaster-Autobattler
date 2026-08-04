using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Контракт мозга Фазы 3 (вики «13» §7): ProfileBrain (Filter→Score→Override) и BrainSystem
    /// (staggered 10-Гц каденс + dirty-прерывание). Зеркалит S1-спайк, но на ПРОДОВЫХ типах
    /// (<see cref="ProfileBrain"/>/<see cref="BrainSystem"/>), не на throwaway-прото.
    /// </summary>
    public sealed class BrainTests
    {
        // ===================== Мозг / таргетинг =====================

        [Test]
        public void Score_LowestHpPercent_PicksWoundedOverNearer()
        {
            var self    = MakeUnit(0, team: 0, x: 0f);
            var nearFull = MakeUnit(1, team: 1, x: 2f, hp: 500f, maxHp: 500f); // ближе, целый
            var farLow   = MakeUnit(2, team: 1, x: 6f, hp:  50f, maxHp: 500f); // дальше, раненый

            var view = new FakeBattleView(self, nearFull, farLow);
            var brain = new ProfileBrain(new AIProfile(TargetingMode.LowestHpPercent));

            brain.Decide(self, view);

            Assert.AreSame(farLow, self.CurrentTarget, "LowestHpPercent должен предпочесть раненого, а не ближнего");
        }

        [Test]
        public void Score_TiesBrokenById_Deterministic()
        {
            // Два врага на равной дистанции и с равным HP — Nearest. Тай-брейк по Id → меньший Id.
            var self = MakeUnit(0, team: 0, x: 0f);
            var a    = MakeUnit(5, team: 1, x: 3f);
            var b    = MakeUnit(2, team: 1, x: -3f); // та же дистанция, Id меньше

            var view  = new FakeBattleView(self, a, b);
            var brain = new ProfileBrain(new AIProfile(TargetingMode.Nearest));

            brain.Decide(self, view);
            RuntimeUnit first = self.CurrentTarget;
            brain.Decide(self, view);

            Assert.AreSame(b, first, "При равенстве должен побеждать меньший Id");
            Assert.AreSame(first, self.CurrentTarget, "Тай-брейк детерминирован между прогонами");
        }

        [Test]
        public void PreferTagged_PicksMarkedEnemy_WhenPresent()
        {
            var self      = MakeUnit(0, team: 0, x: 0f);
            var nearPlain = MakeUnit(1, team: 1, x: 2f);                 // ближе, без тега
            var farMarked = MakeUnit(2, team: 1, x: 7f);                 // дальше, помечен
            farMarked.EffectTagMask = EffectTag.Marked;

            var view  = new FakeBattleView(self, nearPlain, farMarked);
            var brain = new ProfileBrain(new AIProfile(TargetingMode.PreferTagged, targetTag: EffectTag.Marked));

            brain.Decide(self, view);

            Assert.AreSame(farMarked, self.CurrentTarget, "PreferTagged(Marked) должен выбрать помеченного даже дальше");
        }

        [Test]
        public void Brain_DoesNotMutateSim_OnlyWritesIntent()
        {
            var self  = MakeUnit(0, team: 0, x: 0f);
            var enemy = MakeUnit(1, team: 1, x: 4f, hp: 300f, maxHp: 500f);

            var view  = new FakeBattleView(self, enemy);
            var brain = new ProfileBrain(new AIProfile(TargetingMode.Nearest));

            float   enemyHpBefore  = enemy.CurrentHP;
            Vector2 enemyPosBefore = enemy.Position;
            Vector2 selfPosBefore  = self.Position;

            brain.Decide(self, view);

            // Мозг пишет ТОЛЬКО интент — мир не тронут.
            Assert.AreEqual(enemyHpBefore,  enemy.CurrentHP, "Мозг не должен менять HP");
            Assert.AreEqual(enemyPosBefore, enemy.Position,  "Мозг не должен двигать юнитов");
            Assert.AreEqual(selfPosBefore,  self.Position,   "Мозг не должен двигать себя");
            Assert.AreSame(enemy, self.CurrentTarget,        "Интент (цель) должен быть записан");
        }

        // ===================== Каденс / детерминизм (S1, продовый BrainSystem) =====================

        [Test]
        public void StaggeredBrain_SameChecksum_AcrossTwoRuns()
        {
            ulong a = RunStaggered();
            ulong b = RunStaggered();
            Assert.AreEqual(a, b, "Продовый staggered BrainSystem внёс рассинхрон между идентичными прогонами");
        }

        [Test]
        public void TargetDeath_SetsBrainDirty_ReevaluatesNextTick()
        {
            // Атакующий Id=3 (фаза 0). На тике 0 берёт ближнего; ближний гибнет; тик 1 (вне фазы) —
            // dirty по мёртвой цели → переоценка немедленно на живого.
            var attacker = MakeUnit(3, team: 0, x: 0f);
            var near     = MakeUnit(1, team: 1, x: 2f);
            var far      = MakeUnit(2, team: 1, x: 5f);
            var units    = new List<RuntimeUnit> { attacker, near, far };
            SetBrains(units, TargetingMode.Nearest);

            var brain = new BrainSystem();
            var view  = new FakeBattleView(units);

            view.CurrentTick = 0; brain.Tick(units, view);
            Assert.AreSame(near, attacker.CurrentTarget);

            near.IsDead = true;

            view.CurrentTick = 1; brain.Tick(units, view); // 3%3=0 ≠ 1 → решает только из-за dirty
            Assert.AreSame(far, attacker.CurrentTarget, "Смерть цели должна вызвать немедленную переоценку (BrainDirty)");
        }

        [Test]
        public void IntentPersistsBetweenAiTicks_UnitKeepsMoving()
        {
            var units = MakeTwoTeams();
            SetBrains(units, TargetingMode.Nearest);
            RuntimeUnit u = units.Find(x => x.Id == 1); // фаза 1 → думает на тиках 1,4,7…

            var brain = new BrainSystem();
            var move  = new MovementSystem();
            var view  = new FakeBattleView(units);

            for (int tick = 0; tick <= 1; tick++)   // до тика 1: юнит получил цель
            {
                view.CurrentTick = tick;
                brain.Tick(units, view);
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            }
            Assert.IsNotNull(u.CurrentTarget, "После своей фазы у юнита должна быть цель");

            // Тик 2 — вне фазы Id=1: Decide не зовётся, но движение исполняет залипший интент.
            view.CurrentTick = 2;
            brain.Tick(units, view);
            Vector2 before = u.Position;
            move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);

            Assert.AreNotEqual(before, u.Position, "Интент должен залипать: юнит двигается между AI-тиками");
        }

        // ===================== Хелперы =====================

        private static ulong RunStaggered()
        {
            var units = MakeTwoTeams();
            SetBrains(units, TargetingMode.Nearest);
            var brain = new BrainSystem();
            var move  = new MovementSystem();
            var view  = new FakeBattleView(units);

            int tick = 0;
            for (; tick < 120; tick++)
            {
                view.CurrentTick = tick;
                brain.Tick(units, view);
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            }
            return Checksum(units, tick);
        }

        private static void SetBrains(List<RuntimeUnit> units, TargetingMode mode)
        {
            for (int i = 0; i < units.Count; i++)
            {
                units[i].Brain      = new ProfileBrain(new AIProfile(mode));
                units[i].BrainPhase = units[i].Id % SimConstants.AiTickInterval;
            }
        }

        private static List<RuntimeUnit> MakeTwoTeams() => new List<RuntimeUnit>
        {
            MakeUnit(0, team: 0, x: -5f),
            MakeUnit(1, team: 0, x: -4f),
            MakeUnit(2, team: 0, x: -3f),
            MakeUnit(3, team: 1, x:  5f),
            MakeUnit(4, team: 1, x:  4f),
            MakeUnit(5, team: 1, x:  3f),
        };

        private static RuntimeUnit MakeUnit(int id, int team, float x, float hp = 500f, float maxHp = 500f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp,
                Position         = new Vector2(x, 0f),
                PreviousPosition = new Vector2(x, 0f),
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }

        private static ulong Checksum(IReadOnlyList<RuntimeUnit> units, int tick)
        {
            ulong hash = (ulong)tick * 2654435761UL;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                hash ^= (ulong)(u.Id * 1000003);
                hash ^= (ulong)(long)(u.Position.x * 1000f) * 2246822519UL;
                hash ^= (ulong)(long)(u.Position.y * 1000f) * 3266489917UL;
                hash ^= (ulong)(long)(u.CurrentHP  * 100f)  * 668265263UL;
                hash  = (hash << 13) | (hash >> 51);
            }
            return hash;
        }
    }

    /// <summary>Мок read-only среза боя для изолированных тестов мозга (граница §3.1).</summary>
    internal sealed class FakeBattleView : IBattleView
    {
        private readonly List<RuntimeUnit> _units;
        public int CurrentTick;

        public FakeBattleView(List<RuntimeUnit> units) => _units = units;
        public FakeBattleView(params RuntimeUnit[] units) => _units = new List<RuntimeUnit>(units);

        public IReadOnlyList<RuntimeUnit> Units => _units;
        int IBattleView.CurrentTick => CurrentTick;
        public Guildmaster.Core.Simulation.SimTuning Tuning { get; set; } = Guildmaster.Core.Simulation.SimTuning.Default;
    }
}
