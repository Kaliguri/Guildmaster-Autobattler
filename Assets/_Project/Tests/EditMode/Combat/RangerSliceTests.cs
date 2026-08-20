using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Вертикальный срез «Лесной следопыт» (вики «13» §10.4): «Метка охотника» (+25% получаемого
    /// урона, перенос на ближайшего врага при смерти — §9.5, §9.10), кайт (§9.7) и стрельба
    /// на ходу со штрафом скорости (§9.8).
    /// </summary>
    public sealed class RangerSliceTests
    {

        // ===================== «Метка охотника» =====================

        [Test]
        public void HuntersMark_Adds25PctDamageTaken()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var enemy = MakeUnit(1, team: 1, pos: Vector2.zero);

            es.Apply(enemy, HuntersMark(), MakeUnit(0, team: 0, pos: Vector2.zero), ctx);
            EffectSystem.CommitPending(enemy);   // закон видимости: метка видна с конца тика

            Assert.AreNotEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Marked, "Метка ставит тег Marked");
            Assert.AreEqual(1.25f, enemy.Stats.Get(StatType.DamageTakenEff), 1e-4f, "Метка = +25% получаемого урона");
        }

        [Test]
        public void HuntersMark_TransfersToNearestEnemy_OnCarrierDeath()
        {
            var sim = BuildSim(1UL);
            var ranger = MakeUnit(0, team: 0, pos: new Vector2(-5f, 0f), moveSpeed: 0f, aad: 0f);
            var enemyA = MakeUnit(1, team: 1, pos: new Vector2(0f, 0f), moveSpeed: 0f, aad: 0f);
            var enemyB = MakeUnit(2, team: 1, pos: new Vector2(1f, 0f), moveSpeed: 0f, aad: 0f); // ближе к A
            var enemyC = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), moveSpeed: 0f, aad: 0f); // дальше

            foreach (var u in new[] { ranger, enemyA, enemyB, enemyC }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta); // флаш спавнов + регистрация в spatial hash

            sim.ApplyEffect(enemyA, HuntersMark(), ranger);
            sim.Tick(SimConstants.TickDelta);   // метка проявляется на коммите в конце тика
            Assert.AreNotEqual(EffectTag.None, enemyA.EffectTagMask & EffectTag.Marked, "Предусловие: A помечен");

            enemyA.CurrentHP = 0f;                              // убиваем носителя
            for (int t = 0; t < 4; t++) sim.Tick(SimConstants.TickDelta); // смерть (enqueue) → дренаж → перенос

            Assert.AreNotEqual(EffectTag.None, enemyB.EffectTagMask & EffectTag.Marked, "Метка перешла на ближайшего (B)");
            Assert.AreEqual(1.25f, enemyB.Stats.Get(StatType.DamageTakenEff), 1e-4f, "На новой цели метка работает");
            Assert.AreEqual(EffectTag.None, enemyC.EffectTagMask & EffectTag.Marked, "На дальнего (C) метка не ушла");
        }

        [Test]
        public void HuntersMark_NoTransfer_WhenNoEnemiesLeft()
        {
            var sim = BuildSim(1UL);
            var ranger = MakeUnit(0, team: 0, pos: new Vector2(-5f, 0f), moveSpeed: 0f, aad: 0f);
            var lone   = MakeUnit(1, team: 1, pos: Vector2.zero, moveSpeed: 0f, aad: 0f);

            sim.EnqueueUnitSpawn(ranger);
            sim.EnqueueUnitSpawn(lone);
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(lone, HuntersMark(), ranger);
            lone.CurrentHP = 0f;
            for (int t = 0; t < 4; t++) sim.Tick(SimConstants.TickDelta); // не должно бросить исключений

            Assert.AreEqual(EffectTag.None, ranger.EffectTagMask & EffectTag.Marked, "Метку некуда переносить — гаснет с трупом");
        }

        // ===================== Кайт (§9.7) =====================

        [Test]
        public void Kite_KeepsDistanceBand()
        {
            // Полоса кайта = [AttackRange×0.6, AttackRange] = [6, 10]. maxMove за тик мал → проверяем направление.
            // Слишком близко (dist 3 < 6) → отходит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(3f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.Less(u.Position.x, 0f, "Ближе нижней границы полосы — отходит от цели");
            }
            // В полосе (dist 8) → стоит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(8f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.AreEqual(0f, u.Position.x, 1e-4f, "В полосе дистанции — держит позицию");
            }
            // Дальше радиуса (dist 15 > 10) → подходит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(15f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.Greater(u.Position.x, 0f, "Вне радиуса — подходит к цели");
            }
        }

        // Регресс 07 §3.8 B4: полоса кайта берётся из Kite.FleeDist/FallbackDist профиля, а не из
        // магического AttackRange×0.6. Радиус атаки 10 (старая полоса дала бы [6,10]), но профиль
        // задаёт [2,4] — поведение должно следовать профилю.
        [Test]
        public void Kite_UsesProfileFleeAndFallbackDist_NotAttackRange()
        {
            var kiteAi = new AIProfile(
                autoAttackTargeting: TargetingMode.Nearest,
                kite: new Kite { Enabled = true, FleeDist = 2f, FallbackDist = 4f });
            RelicData kiteRelic = TestRelic.Make(ai: kiteAi);

            // dist 8: старая полоса [6,10] → стой; профильная [2,4] → дальше FallbackDist → подходит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, relic: kiteRelic,
                    positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(8f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.Greater(u.Position.x, 0f, "dist>FallbackDist(4) — подходит (профиль, не AttackRange)");
            }
            // dist 1 < FleeDist(2) → отходит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, relic: kiteRelic,
                    positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.Less(u.Position.x, 0f, "dist<FleeDist(2) — отходит");
            }
            // dist 3 в полосе [2,4] → стоит.
            {
                var u = MakeUnit(0, team: 0, pos: Vector2.zero, range: 10f, relic: kiteRelic,
                    positioning: PositioningIntent.Kite);
                var target = MakeUnit(1, team: 1, pos: new Vector2(3f, 0f));
                u.CurrentTarget = target;
                new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
                Assert.AreEqual(0f, u.Position.x, 1e-4f, "В полосе [FleeDist, FallbackDist] — держит позицию");
            }
        }

        // ===================== Репозиция у стены (вики «15» §7: зажат у стены) =====================

        // Отход прочь строго от врага упирается в стену — юнит не должен стоять/продавливаться наружу,
        // а репозиционируется вдоль стены (скольжение к центру арены = перебежка мимо/сквозь скопление).
        [Test]
        public void Retreat_PinnedAtWall_SlidesAlongWall_NotStuck()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(10f, 10f)); // x,y ∈ [-5, 5]
            var u     = MakeUnit(0, team: 0, pos: new Vector2(-5f, 0f), positioning: PositioningIntent.Retreat);
            var enemy = MakeUnit(1, team: 1, pos: new Vector2(-3f, 0f)); // справа → отход в -x упирается в левую стену
            u.CurrentTarget = enemy; // MoveRetreat ищет ближайшего сам, но Tick требует ненулевую цель

            new MovementSystem().Tick(new List<RuntimeUnit> { u, enemy }, SimConstants.TickDelta, bounds, SimTuning.Default);

            Assert.AreEqual(-5f, u.Position.x, 1e-3f, "За стену по X не продавливается");
            Assert.Greater(Mathf.Abs(u.Position.y), 1e-4f, "Зажатый у стены идёт ВДОЛЬ неё, а не утыкается на месте");
        }

        [Test]
        public void Kite_PinnedAtWall_RepositionsAlongWall()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(10f, 10f)); // x,y ∈ [-5, 5]
            var u      = MakeUnit(0, team: 0, pos: new Vector2(-5f, 0f), range: 10f, positioning: PositioningIntent.Kite);
            var target = MakeUnit(1, team: 1, pos: new Vector2(-4.5f, 0f)); // впритык → flee-ветка отхода в стену
            u.CurrentTarget = target;

            new MovementSystem().Tick(new List<RuntimeUnit> { u, target }, SimConstants.TickDelta, bounds, SimTuning.Default);

            Assert.AreEqual(-5f, u.Position.x, 1e-3f, "Кайт за стену по X не продавливается");
            Assert.Greater(Mathf.Abs(u.Position.y), 1e-4f, "Кайт, зажатый у стены, перебегает вдоль неё, а не утыкается");
        }

        // ===================== Стрельба на ходу (§9.8) =====================

        [Test]
        public void AttackWhileMoving_KeepsMoving_WithHalfSpeedPenalty()
        {
            var target = MakeUnit(9, team: 1, pos: new Vector2(10f, 0f));

            // Реликвия со стрельбой на ходу, штраф 50%. Юнит в замахе — обычно рут, но флаг снимает рут.
            RelicData movingRelic = TestRelic.Make(canAttackWhileMoving: true, movingAttackSpeedPenaltyPct: 0.5f);

            var moving = MakeUnit(0, team: 0, pos: Vector2.zero, range: 1f, relic: movingRelic);
            moving.CurrentTarget = target; moving.Phase = AttackPhase.Windup;

            var rooted = MakeUnit(1, team: 0, pos: Vector2.zero, range: 1f); // без флага
            rooted.CurrentTarget = target; rooted.Phase = AttackPhase.Windup;

            var baseline = MakeUnit(2, team: 0, pos: Vector2.zero, range: 1f, relic: movingRelic);
            baseline.CurrentTarget = target; // НЕ в замахе → полная скорость

            var sys = new MovementSystem();
            sys.Tick(new List<RuntimeUnit> { moving, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            sys.Tick(new List<RuntimeUnit> { rooted, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            sys.Tick(new List<RuntimeUnit> { baseline, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);

            Assert.Greater(moving.Position.x, 0f, "Со стрельбой на ходу юнит движется в замахе");
            Assert.AreEqual(0f, rooted.Position.x, 1e-4f, "Без флага замах рутит (поведение Ф1)");

            // Штраф меряем от СОБСТВЕННОЙ скорости юнита, а не от свободного соседа: тот на дистанции 10
            // при радиусе 1 идёт в разбеге (Ф5) и полной скоростью уже не движется. Эталоном он быть
            // перестал, но остался проверкой, что занятый свингом медленнее свободного.
            float fullStep = moving.Stats.Get(StatType.MoveSpeed) * SimConstants.TickDelta;
            Assert.AreEqual(fullStep * 0.5f, moving.Position.x, 1e-4f, "В замахе скорость вдвое ниже (штраф 50%)");
            Assert.Greater(baseline.Position.x, moving.Position.x, "Свободный юнит движется быстрее занятого свингом");
        }

        // Regress: хвост-восстановление — тоже «занятость». Замедление держится ВЕСЬ доигрыш свинга,
        // а не только замах — иначе стрелок спринтит на полной скорости в паузе между выстрелами
        // («скользкий кайт»). Непрерывно атакующий = непрерывно замедлен.
        [Test]
        public void AttackWhileMoving_StaysSlowed_DuringRecovery()
        {
            var target = MakeUnit(9, team: 1, pos: new Vector2(10f, 0f));
            RelicData movingRelic = TestRelic.Make(canAttackWhileMoving: true, movingAttackSpeedPenaltyPct: 0.5f);

            // Юнит в хвосте-восстановлении (урон уже нанесён, но свинг доигрывается).
            var recovering = MakeUnit(0, team: 0, pos: Vector2.zero, range: 1f, relic: movingRelic);
            recovering.CurrentTarget = target;
            recovering.Phase = AttackPhase.Recovery; recovering.RecoveryRemaining = 5;

            // Тот же юнит в Idle (между циклами) — эталон полной скорости.
            var baseline = MakeUnit(2, team: 0, pos: Vector2.zero, range: 1f, relic: movingRelic);
            baseline.CurrentTarget = target;

            var sys = new MovementSystem();
            sys.Tick(new List<RuntimeUnit> { recovering, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            sys.Tick(new List<RuntimeUnit> { baseline, target }, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);

            Assert.Greater(recovering.Position.x, 0f, "В хвосте-восстановлении стрелок продолжает движение");

            // Как и в замахе: эталон — своя скорость, а не свободный сосед (он на этой дистанции разбегается).
            float fullStep = recovering.Stats.Get(StatType.MoveSpeed) * SimConstants.TickDelta;
            Assert.AreEqual(fullStep * 0.5f, recovering.Position.x, 1e-4f,
                "В хвосте скорость вдвое ниже — штраф держится, а не только в замахе");
            Assert.Greater(baseline.Position.x, recovering.Position.x, "Свободный юнит движется быстрее доигрывающего свинг");
        }

        // ===================== Фабрики / хелперы =====================

        private static EffectData HuntersMark()
        {
            var dmgUp = new StatModifierComponent().With("_modifiers",
                new[] { new StatModifier(StatType.DamageTakenEff, ModifierOp.PercentMult, 0.25f) });
            var transfer = new MarkTransferComponent();
            return TestEffect.Make(
                baseDuration: 5f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Marked, stacking: StackRule.Refresh,
                components: new IEffectComponent[] { dmgUp, transfer });
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float maxHp = 200f, float aad = 10f, float range = 5f, float moveSpeed = 3f,
            PositioningIntent positioning = PositioningIntent.Approach)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, moveSpeed),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = maxHp,
                Position         = pos,
                PreviousPosition = pos,
                Unit             = relic,
                Positioning      = positioning,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }
    }
}
