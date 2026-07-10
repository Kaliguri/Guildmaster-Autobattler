using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
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
    /// Вертикальный срез «Светлый пастырь» (вики «13» §10.1): хил-автоатака снарядом (§9.2)
    /// и глобальная лечащая активка «Длань жизни» (§9.10). Проверяет: мозг/система целят раненого
    /// союзника (не врага), снаряд лечит при попадании, величины скейлятся, нет оверхила,
    /// «Длань» = X + недостающее HP, блоки D (порог союзника) и E (самолечение), и что хилер
    /// не наносит урон.
    /// <para>
    /// Разделение ответственности: маршрутизацию систем (кого целим, Heal-вместо-DealDamage)
    /// проверяем через <see cref="RecordingContext"/>; корректность самого <c>Heal</c>
    /// (эффективности + кламп к MaxHP) — через реальный <see cref="CombatSimulation"/>.
    /// </para>
    /// </summary>
    public sealed class ShepherdSliceTests
    {
        private const float ArmorK   = 100f;
        private const float CellSize = 3f;

        // ===================== §9.2 Хил-автоатака =====================

        [Test]
        public void HealAutoAttack_SpawnsHealProjectile_AtWoundedAlly()
        {
            var shepherd = MakeShepherd(0, team: 0, pos: Vector2.zero, aad: 20f, range: 10f);
            var wounded  = MakeUnit(1, team: 0, pos: new Vector2(2f, 0f), maxHp: 100f, hp: 40f);
            var enemy    = MakeUnit(2, team: 1, pos: new Vector2(3f, 0f));

            // Мозг (симулируем его вывод): лечим раненого союзника, а «текущая цель» — враг для позиционирования.
            shepherd.AutoAttackTarget = wounded;
            shepherd.CurrentTarget    = enemy;

            var units = new List<RuntimeUnit> { shepherd, wounded, enemy };
            var ctx   = new RecordingContext(units);

            TickUntilSpawn(units, ctx);

            Assert.AreEqual(1, ctx.Spawns.Count, "Хил-АА должна заспавнить ровно один снаряд");
            ProjectileSpawn p = ctx.Spawns[0];
            Assert.IsTrue(p.IsHeal, "Снаряд должен быть хил-снарядом");
            Assert.AreSame(wounded, p.TargetUnit, "Хил летит в раненого союзника (AutoAttackTarget), не во врага");
            Assert.AreEqual(0, ctx.Damage.Count, "Хил-АА не наносит урон");
        }

        [Test]
        public void HealProjectile_HealsAllyOnImpact_NotDamage()
        {
            var source = MakeUnit(0, team: 0, pos: Vector2.zero);
            var ally   = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 50f);

            var units = new List<RuntimeUnit> { source, ally };
            var ctx   = new RecordingContext(units);

            var proj = new Projectile
            {
                Source          = source,
                Position        = source.Position,
                PreviousPosition = source.Position,
                Velocity        = new Vector2(20f, 0f),
                CollisionRadius = 0.25f,
                TargetUnit      = ally,
                RawDamage       = 30f,
                IsHeal          = true,
                IsAlive         = true,
            };

            var projectiles = new List<Projectile> { proj };
            var system = new ProjectileSystem();
            for (int t = 0; t < 32 && ctx.Heals.Count == 0; t++)
                system.Tick(projectiles, units, ctx, SimConstants.TickDelta, ArenaBounds.Unbounded);

            Assert.AreEqual(1, ctx.Heals.Count, "Снаряд при попадании лечит цель");
            Assert.AreSame(ally, ctx.Heals[0].Target);
            Assert.AreEqual(0, ctx.Damage.Count, "Хил-снаряд не наносит урон");
            Assert.AreEqual(80f, ally.CurrentHP, 1e-4f, "HP цели выросло (50 + 30)");
        }

        [Test]
        public void HealAmount_ScalesWith_AutoAttackDamage_And_DealtEff()
        {
            // Реальный Heal: величина = amount (сырое = AutoAttackDamage) × HealShieldDealtEff кастующего.
            var sim = BuildSim(1UL);

            var source = MakeUnit(0, team: 0, pos: Vector2.zero, healDealtEff: 1.5f);
            var target = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 1000f, hp: 100f);

            sim.Heal(target, amount: 20f, source: source); // 20 (AutoAttackDamage) × 1.5 dealtEff × 1.0 takenEff

            Assert.AreEqual(130f, target.CurrentHP, 1e-4f, "Вылечено 20 × 1.5 = 30 → 100 + 30");
        }

        [Test]
        public void Heal_NoOverheal_ClampedToMaxHp()
        {
            var sim = BuildSim(1UL);

            var source = MakeUnit(0, team: 0, pos: Vector2.zero);
            var target = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 90f);

            sim.Heal(target, amount: 999f, source: source);

            Assert.AreEqual(100f, target.CurrentHP, 1e-4f, "Лечение не превышает MaxHP");
        }

        // ===================== §9.10 «Длань жизни» (активка) =====================

        [Test]
        public void HandOfLife_HealsForFlatPlusMissingHp()
        {
            var caster  = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 100f);
            var wounded = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 30f); // недостаёт 70

            AbilityData hand = TestAbility.Make(
                cost: 0f, mode: AbilityTargetMode.LowestHpAlly,
                healFlat: 30f, healPctTargetMissingHp: 1f,
                castCondition: CastCondition.Immediately);
            caster.Abilities.Add(new AbilityRuntime(hand));

            var units = new List<RuntimeUnit> { caster, wounded };
            var ctx   = new RecordingContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast);
            Assert.AreEqual(1, ctx.Heals.Count);
            Assert.AreSame(wounded, ctx.Heals[0].Target, "Целит самого раненого союзника");
            Assert.AreEqual(100f, ctx.Heals[0].Amount, 1e-4f, "X (30) + недостающее HP (70) = 100 (сырое, до эффективностей)");
        }

        [Test]
        public void HandOfLife_CastsWhenAllyBelowThreshold_NotWhenHealthy()
        {
            AbilityData hand = TestAbility.Make(
                cost: 0f, mode: AbilityTargetMode.LowestHpAlly,
                healFlat: 30f, healPctTargetMissingHp: 1f,
                castCondition: CastCondition.AllyTargetHpBelowPct, castConditionHpPct: 0.5f);

            // Раненый союзник (40% ≤ 50%) → каст.
            {
                var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
                var ally   = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 40f);
                caster.Abilities.Add(new AbilityRuntime(hand));
                var units = new List<RuntimeUnit> { caster, ally };
                var ctx   = new RecordingContext(units);

                Assert.IsTrue(new AbilitySystem().TryCast(caster, 0, units, ctx),
                    "Союзник ниже порога — кастуем");
            }

            // Здоровый союзник (90% > 50%) → каста нет.
            {
                var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
                var ally   = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 90f);
                caster.Abilities.Add(new AbilityRuntime(hand));
                var units = new List<RuntimeUnit> { caster, ally };
                var ctx   = new RecordingContext(units);

                Assert.IsFalse(new AbilitySystem().TryCast(caster, 0, units, ctx),
                    "Союзник здоров — не кастуем");
                Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "Кулдаун не ставится без каста");
            }
        }

        [Test]
        public void HandOfLife_SelfHeal_WhenOwnHpLow()
        {
            // Блок E: при своём низком HP цель разворачивается на самого кастующего, даже если есть раненый союзник.
            var caster  = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 20f);       // 20% ≤ 30%
            var wounded = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 45f); // раненее по %, но не сам

            AbilityData hand = TestAbility.Make(
                cost: 0f, mode: AbilityTargetMode.LowestHpAlly,
                healFlat: 30f, healPctTargetMissingHp: 1f,
                castCondition: CastCondition.AllyTargetHpBelowPct, castConditionHpPct: 0.5f,
                castOverrideSelfHpPct: 0.3f);
            caster.Abilities.Add(new AbilityRuntime(hand));

            var units = new List<RuntimeUnit> { caster, wounded };
            var ctx   = new RecordingContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast);
            Assert.AreEqual(1, ctx.Heals.Count);
            Assert.AreSame(caster, ctx.Heals[0].Target, "При своём низком HP лечим себя (блок E)");
        }

        // ===================== Пастырь не наносит урон =====================

        [Test]
        public void Shepherd_NeverDamagesEnemies()
        {
            var shepherd = MakeShepherd(0, team: 0, pos: Vector2.zero, aad: 20f, range: 10f);
            var wounded  = MakeUnit(1, team: 0, pos: new Vector2(2f, 0f), maxHp: 100f, hp: 40f);
            var enemy    = MakeUnit(2, team: 1, pos: new Vector2(3f, 0f));
            shepherd.AutoAttackTarget = wounded;
            shepherd.CurrentTarget    = enemy;

            AbilityData hand = TestAbility.Make(
                cost: 0f, mode: AbilityTargetMode.LowestHpAlly,
                healFlat: 30f, healPctTargetMissingHp: 1f,
                castCondition: CastCondition.Immediately);
            shepherd.Abilities.Add(new AbilityRuntime(hand));

            var units = new List<RuntimeUnit> { shepherd, wounded, enemy };
            var ctx   = new RecordingContext(units);

            TickUntilSpawn(units, ctx);            // хил-АА
            new AbilitySystem().TryCast(shepherd, 0, units, ctx); // «Длань жизни»

            Assert.AreEqual(0, ctx.Damage.Count, "Ни авто-атака, ни активка Пастыря не наносят урон");
            Assert.Greater(ctx.Spawns.Count + ctx.Heals.Count, 0, "Но лечение он выдаёт");
        }

        // ===================== Хелперы =====================

        private static void TickUntilSpawn(List<RuntimeUnit> units, RecordingContext ctx, int maxTicks = 64)
        {
            var system = new AutoAttackSystem();
            for (int t = 0; t < maxTicks && ctx.Spawns.Count == 0; t++)
                system.Tick(units, ctx, SimConstants.TickDelta);
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new XorShiftRng(seed), ArmorK, new SpatialHash(CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        /// <summary>Хилер: relic с AutoAttackMode.Heal + Ranged, статы силы лечения/дальности/снаряда.</summary>
        private static RuntimeUnit MakeShepherd(int id, int team, Vector2 pos, float aad, float range)
        {
            var ai = new AIProfile(
                autoAttackTargeting: TargetingMode.AllyLowestHpPercent,
                autoAttackMode: AutoAttackMode.Heal);
            RelicData relic = TestRelic.Make(attackType: AttackType.Ranged, ai: ai);
            return MakeUnit(id, team, pos, relic: relic, aad: aad, range: range, projectileSpeed: 20f);
        }

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float aad = 10f, float range = 5f, float atkSpeed = 1f,
            float maxHp = 100f, float hp = -1f,
            float projectileSpeed = 0f, float healDealtEff = 1f, float healTakenEff = 1f)
        {
            var stats = new Stats(null);
            var mods = new List<StatModifier>
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, atkSpeed),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.ProjectileSpeed,  ModifierOp.Flat, projectileSpeed),
            };
            // Эффективности стартуют с 1.0 (NaturalDefault); мод PercentMult сдвигает на (eff - 1).
            if (!Mathf.Approximately(healDealtEff, 1f))
                mods.Add(new StatModifier(StatType.HealShieldDealtEff, ModifierOp.PercentMult, healDealtEff - 1f));
            if (!Mathf.Approximately(healTakenEff, 1f))
                mods.Add(new StatModifier(StatType.HealShieldTakenEff, ModifierOp.PercentMult, healTakenEff - 1f));

            stats.AddModifiersFrom("base", mods.ToArray());
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp < 0f ? maxHp : hp,
                Position         = pos,
                PreviousPosition = pos,
                Relic            = relic,
            };
        }

        /// <summary>Контекст-регистратор: копит спавны/урон/хилы; Heal применяет кламп к MaxHP (без эффективностей — их проверяет реальный Heal).</summary>
        private sealed class RecordingContext : ICombatContext
        {
            private readonly List<RuntimeUnit> _all;

            public readonly List<ProjectileSpawn> Spawns = new List<ProjectileSpawn>();
            public readonly List<DamageRequest> Damage = new List<DamageRequest>();
            public readonly List<(RuntimeUnit Target, float Amount, RuntimeUnit Source)> Heals =
                new List<(RuntimeUnit, float, RuntimeUnit)>();

            public RecordingContext(List<RuntimeUnit> all) => _all = all;

            public void DealDamage(in DamageRequest req) => Damage.Add(req);

            public void Heal(RuntimeUnit target, float amount, RuntimeUnit source)
            {
                Heals.Add((target, amount, source));
                float maxHp = target.Stats.Get(StatType.MaxHP);
                target.CurrentHP = Mathf.Min(target.CurrentHP + amount, maxHp);
            }

            public void SpawnProjectile(in ProjectileSpawn spawn) => Spawns.Add(spawn);

            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) { }
            public void ReportAreaHit(in AreaHit hit) { }
            public void Dispel(in DispelRequest req) { }
            public void Displace(in DisplaceRequest req) { }
            public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) { }
            public void NotifyAttackInterrupted(RuntimeUnit unit) { }

            public IRngService Rng => null;
            public int CurrentTick => 0;
            public float ArmorK => 100f;
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;

            public int QueryUnitsInRadius(Vector2 center, float radius, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam)
            {
                results.Clear();
                float r2 = radius * radius;
                for (int i = 0; i < _all.Count; i++)
                {
                    RuntimeUnit u = _all[i];
                    if (u.IsDead || (u.Position - center).sqrMagnitude > r2) continue;
                    bool isAlly = u.Team == requestingTeam;
                    if (filter == TargetFilter.Enemies && isAlly) continue;
                    if (filter == TargetFilter.Allies && !isAlly) continue;
                    results.Add(u);
                }
                return results.Count;
            }

            public int QueryUnitsInLine(Vector2 origin, Vector2 direction, float length, float width, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam)
            {
                results.Clear();
                return 0;
            }
        }
    }
}
