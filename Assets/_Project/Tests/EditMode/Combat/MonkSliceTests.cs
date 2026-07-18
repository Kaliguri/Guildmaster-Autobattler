using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Вертикальный срез «Монах вихря» (вики «13» §10.6): «Шквальный толчок» (§9.9 — отбрасывание
    /// на фикс. дистанцию, оглушение полёта, урон-«ядро» по врагам источника на линии) и «Вихревой
    /// заход» (§9.6 — телепорт к цели + усиление в конце полёта). Спайк детерминизма перемещений (S5)
    /// влит two-run проверкой.
    /// </summary>
    public sealed class MonkSliceTests
    {
        private const float ArmorK   = 100f;
        private const float CellSize = 3f;

        // ===================== §9.9 отбрасывание =====================

        [Test]
        public void Knockback_MovesTargetFixedDistance_Deterministically()
        {
            Vector2 Run()
            {
                var sim = BuildSim(3UL);
                var monk   = MakeUnit(0, team: 0, pos: new Vector2(-1f, 0f));
                var target = MakeUnit(1, team: 1, pos: Vector2.zero);
                sim.Displace(new DisplaceRequest(target, monk, new Vector2(1f, 0f),
                    distance: 4f, ticks: 8, cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 1f));
                for (int t = 0; t < 8; t++) sim.Tick(SimConstants.TickDelta);
                return target.Position;
            }

            Vector2 a = Run();
            Vector2 b = Run();

            Assert.AreEqual(4f, a.x, 1e-4f, "Цель отброшена ровно на фикс. дистанцию (0 + 4)");
            Assert.AreEqual(0f, a.y, 1e-4f, "По направлению толчка (без бокового сноса)");
            Assert.AreEqual(a, b, "Два идентичных прогона совпали — перемещение детерминировано (S5)");
        }

        [Test]
        public void Displace_TargetStunnedDuringFlight_FreedAfter()
        {
            var sim = BuildSim(1UL);
            var target = MakeUnit(1, team: 1, pos: Vector2.zero);
            sim.Displace(new DisplaceRequest(target, MakeUnit(0, 0, Vector2.zero), new Vector2(1f, 0f),
                distance: 6f, ticks: 6, cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 1f));

            for (int t = 0; t < 3; t++) sim.Tick(SimConstants.TickDelta);
            Assert.Greater(target.DisplacedTicksRemaining, 0, "В полёте цель оглушена (жёсткое состояние)");

            for (int t = 0; t < 3; t++) sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(0, target.DisplacedTicksRemaining, "После приземления оглушение снято");
        }

        [Test]
        public void Cannonball_HitsCasterEnemiesOnLine_NotAllies()
        {
            var sim = BuildSim(1UL);
            // aad/moveSpeed = 0: глушим обычный бой и дрейф — урон только от «ядра», позиции стабильны.
            var monk    = MakeUnit(0, team: 0, pos: new Vector2(-2f, 0f), aad: 0f, moveSpeed: 0f);
            var flying  = MakeUnit(1, team: 1, pos: new Vector2(0f, 0f), aad: 0f, moveSpeed: 0f);   // «снаряд» — враг монаха
            var enemyB  = MakeUnit(2, team: 1, pos: new Vector2(5f, 0f), maxHp: 200f, aad: 0f, moveSpeed: 0f); // враг на линии → бьём
            var allyC   = MakeUnit(3, team: 0, pos: new Vector2(3f, 0f), maxHp: 200f, aad: 0f, moveSpeed: 0f); // союзник на линии → НЕ бьём

            foreach (var u in new[] { monk, flying, enemyB, allyC }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta); // флаш + регистрация в spatial hash

            sim.Displace(new DisplaceRequest(flying, monk, new Vector2(1f, 0f),
                distance: 10f, ticks: 10, cannonball: true, damage: 50f, school: DamageSchool.Physical, width: 2f));
            for (int t = 0; t < 11; t++) sim.Tick(SimConstants.TickDelta);

            Assert.Less(enemyB.CurrentHP, 200f, "«Ядро» бьёт врага источника на линии");
            Assert.AreEqual(200f, allyC.CurrentHP, 1e-4f, "Союзника источника «ядро» не задевает");
            Assert.AreEqual(flying.Stats.Get(StatType.MaxHP), flying.CurrentHP, 1e-4f, "Сам летящий не бьёт себя");
        }

        // ===================== §10.6 «Вихревой заход» =====================

        [Test]
        public void VortexEntry_TeleportsAndEmpowers_AtFlightEnd()
        {
            var sim = BuildSim(1UL);
            var monk   = MakeUnit(0, team: 0, pos: new Vector2(-3f, 0f), range: 2f);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));

            sim.ApplyEffect(monk, VortexPassive(2f), monk);
            sim.Displace(new DisplaceRequest(victim, monk, new Vector2(1f, 0f),
                distance: 4f, ticks: 6, cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 1f));

            for (int t = 0; t < 6; t++) sim.Tick(SimConstants.TickDelta); // конец полёта → сигнал → телепорт+усиление тем же тиком

            Assert.AreEqual(2f, monk.EmpowerDamageMult, 1e-4f, "В конце полёта «Вихревой заход» взвёл усиление ×2");
            Assert.AreSame(victim, monk.CurrentTarget, "Монах перенацелился на смещённую цель");
            float distToVictim = (monk.Position - victim.Position).magnitude;
            Assert.LessOrEqual(distToVictim, monk.Stats.Get(StatType.AttackRange) + 1e-3f, "Телепорт поставил монаха в пределах досягаемости");
        }

        // ===================== §10.6 полная цепочка: рывок → отбрасывание → телепорт =====================

        [Test]
        public void DashLanding_KnocksBackEnemy_ThenVortexTeleports_SelfDashNoSelfTeleport()
        {
            var sim = BuildSim(1UL);
            // Монах с двумя пассивами: приземление рывка (→ отбрасывание) и «Вихревой заход» (→ телепорт).
            var monk     = MakeUnit(0, team: 0, pos: new Vector2(0f, 0f), range: 2f, aad: 10f, moveSpeed: 0f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), maxHp: 500f, aad: 0f, moveSpeed: 0f);
            var bystander = MakeUnit(2, team: 1, pos: new Vector2(4f, 0f), maxHp: 500f, aad: 0f, moveSpeed: 0f); // на линии полёта «ядра»
            sim.EnqueueUnitSpawn(monk);
            sim.EnqueueUnitSpawn(enemy);
            sim.EnqueueUnitSpawn(bystander);
            sim.Tick(SimConstants.TickDelta); // флаш + регистрация в spatial hash

            sim.ApplyEffect(monk, DashLandingPassive(distance: 4f, ticks: 8, dmgMult: 1.5f), monk);
            sim.ApplyEffect(monk, VortexPassive(2f), monk);

            float enemyStartX     = enemy.Position.x;
            float bystanderStartX = bystander.Position.x;

            // Рывок монаха к цели (self-displacement) — как это делает активка «Шквальный толчок».
            sim.Displace(new DisplaceRequest(monk, monk, new Vector2(1f, 0f),
                distance: 1f, ticks: 6, cannonball: false, damage: 0f, school: DamageSchool.Physical, width: 0f));

            // Конец рывка → приземление → отбрасывание врага → конец отбрасывания → телепорт монаха.
            // Усиление ×2 могло быть израсходовано авто-атакой к концу прогона — трекаем максимум за прогон.
            float maxEmpower = 0f;
            for (int t = 0; t < 30; t++)
            {
                sim.Tick(SimConstants.TickDelta);
                maxEmpower = Mathf.Max(maxEmpower, monk.EmpowerDamageMult);
            }

            Assert.Greater(enemy.Position.x, enemyStartX + 1f, "Приземление рывка оттолкнуло врага (фаза «отбрасывание»)");
            Assert.AreEqual(2f, maxEmpower, 1e-4f, "Конец отбрасывания врага взвёл «Вихревой заход» (×2)");
            // Финальный телепорт садит монаха вплотную к отброшенной цели (какой именно — детерминировано,
            // но зависит от того, чей полёт кончился последним; жёстко «исходную» не зашиваем — это интент, не инвариант).
            float distMain  = (monk.Position - enemy.Position).magnitude;
            float distChain = (monk.Position - bystander.Position).magnitude;
            float range = monk.Stats.Get(StatType.AttackRange);
            Assert.LessOrEqual(Mathf.Min(distMain, distChain), range + 1e-3f, "Монах телепортнулся вплотную к отброшенной цели");

            // «Ядро» задело прохожего на линии: и урон, и слабое цепное отбрасывание (§10.6).
            Assert.Less(bystander.CurrentHP, 500f, "«Ядро» нанесло урон задетому на линии");
            Assert.Greater(bystander.Position.x, bystanderStartX + 0.1f, "Задетый «ядром» слабо отброшен (цепь)");
        }

        // ===================== Фабрики / хелперы =====================

        private static EffectData VortexPassive(float mult)
        {
            var vortex = new VortexEntryComponent().With("_empowerMult", mult);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: vortex);
        }

        private static EffectData DashLandingPassive(float distance, int ticks, float dmgMult)
        {
            var landing = new WhirlDashLandingComponent()
                .With("_displaceDistance", distance)
                .With("_displaceTicks", ticks)
                .With("_displaceDamageMult", dmgMult)
                .With("_displaceWidth", 1.5f);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: landing);
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new XorShiftRng(seed), ArmorK, new SpatialHash(CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, float maxHp = 100f, float range = 5f,
            float aad = 10f, float moveSpeed = 3f)
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
            };
        }
    }
}
