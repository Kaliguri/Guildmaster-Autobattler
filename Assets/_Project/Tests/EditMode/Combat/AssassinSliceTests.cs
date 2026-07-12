using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
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
    /// Вертикальный срез «Скрытный убийца» (вики «13» §10.5): «Скрытность» (§9.6 — усиление первой
    /// авто-атаки, рестелс после своего убийства) и «Изворотливость» (§9.3/§9.4 — 2 заряда негейта
    /// входящего удара с независимой перезарядкой).
    /// </summary>
    public sealed class AssassinSliceTests
    {
        private const float ArmorK   = 100f;
        private const float CellSize = 3f;

        // ===================== «Скрытность» (§9.6) =====================

        [Test]
        public void Stealth_EmpowersFirstAutoAttack_ThenConsumed()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, aad: 10f, range: 5f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            var enemy = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), maxHp: 100000f);
            assassin.CurrentTarget = enemy;
            assassin.EmpowerDamageMult = 2f;

            var sys   = new AutoAttackSystem();
            var units = new List<RuntimeUnit> { assassin, enemy };

            for (int t = 0; t < 64 && ctx.DamageCalls.Count == 0; t++) sys.Tick(units, ctx, SimConstants.TickDelta);
            Assert.AreEqual(20f, ctx.DamageCalls[0].RawDamage, 1e-4f, "Первая атака усилена ×2 (10 → 20)");
            Assert.AreEqual(0f, assassin.EmpowerDamageMult, 1e-4f, "Усиление потрачено (однострел)");

            for (int t = 0; t < 128 && ctx.DamageCalls.Count < 2; t++) sys.Tick(units, ctx, SimConstants.TickDelta);
            Assert.AreEqual(10f, ctx.DamageCalls[1].RawDamage, 1e-4f, "Вторая атака — обычная");
        }

        [Test]
        public void Stealth_AppliedAtCombatStart_ViaPassiveOnApply()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, relic: AssassinRelic(PassiveTrigger.AnyHit));

            es.Apply(assassin, StealthPassive(), assassin, ctx); // как выдаёт фабрика при спавне

            Assert.AreEqual(2f, assassin.EmpowerDamageMult, 1e-4f, "Стелс в начале боя взвёл усиление");
            Assert.AreNotEqual(EffectTag.None, assassin.EffectTagMask & EffectTag.Stealth, "Наложен баф Stealth");
            Assert.Less(assassin.Stats.Get(StatType.DamageTakenEff), 1f, "Стелс снижает получаемый урон");
        }

        [Test]
        public void Stealth_RetriggersOnOwnKill()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, range: 0.5f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            var victim = MakeUnit(1, team: 1, pos: new Vector2(5f, 0f), maxHp: 10f, hp: 10f); // вне досягаемости АА

            sim.ApplyEffect(assassin, StealthPassive(), assassin);
            assassin.EmpowerDamageMult = 0f; // симулируем, что усиление уже израсходовано

            sim.DealDamage(new DamageRequest(assassin, victim, 999f, DamageType.True, sim.ArmorK)); // смертельный удар
            sim.Tick(SimConstants.TickDelta); // дренаж UnitKilled → рестелс

            Assert.AreEqual(2f, assassin.EmpowerDamageMult, 1e-4f, "После своего убийства «Скрытность» перевзводит усиление");
        }

        // ===================== «Изворотливость» (§9.3/§9.4) =====================

        [Test]
        public void Dodge_NegatesUpToChargeCount_ThenRecharges()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(assassin, DodgePassive(maxCharges: 2, rechargeSeconds: 8f), assassin);
            var hit = new DamageRequest(null, assassin, 30f, DamageType.True, ArmorK, isAutoAttack: true);

            ctx.Tick = 0;
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx),  "1-й удар негейтнут (заряд 1)");
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx),  "2-й удар негейтнут (заряд 2)");
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx), "3-й удар проходит — зарядов нет");

            int recharge = Mathf.RoundToInt(8f * SimConstants.TickRate);
            ctx.Tick = recharge; // прошёл кулдаун одного заряда
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx), "Заряд восстановился — снова негейт");
        }

        [Test]
        public void Dodge_NegatedHit_DealsNoDamage_ViaSimulation()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(assassin, DodgePassive(maxCharges: 1, rechargeSeconds: 8f), assassin);

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.True, sim.ArmorK, isAutoAttack: true));
            Assert.AreEqual(200f, assassin.CurrentHP, 1e-4f, "Первый удар негейтнут — HP не тронуто");

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.True, sim.ArmorK, isAutoAttack: true));
            Assert.AreEqual(150f, assassin.CurrentHP, 1e-4f, "Заряд израсходован — второй удар проходит");
        }

        // Регресс 07 §3.8 B2: рестак стакающегося дожа НЕ перезаряжает уже израсходованные заряды.
        [Test]
        public void Dodge_Restack_DoesNotRefillSpentCharges()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));

            EffectData dodge = DodgePassiveStacking(maxCharges: 1, rechargeSeconds: 8f);
            ctx.Tick = 0;
            es.Apply(assassin, dodge, assassin, ctx);

            var hit = new DamageRequest(null, assassin, 30f, DamageType.True, ArmorK, isAutoAttack: true);
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx),  "Заряд израсходован на 1-м ударе");
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx), "Зарядов больше нет");

            es.Apply(assassin, dodge, assassin, ctx); // рестак того же эффекта → стак растёт
            Assert.AreEqual(2, assassin.ActiveEffects[0].Stacks, "Предусловие: стак вырос");
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx),
                "Рестак НЕ перезарядил израсходованный заряд (07 §3.8 B2)");
        }

        // Изворотливость гасит ТОЛЬКО автоатаки: урон способности (IsAutoAttack=false) проходит и не тратит заряд.
        [Test]
        public void Dodge_IgnoresNonAutoAttackDamage()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(assassin, DodgePassive(maxCharges: 1, rechargeSeconds: 5f), assassin);

            var ability = new DamageRequest(null, assassin, 30f, DamageType.True, ArmorK); // isAutoAttack=false
            Assert.IsFalse(es.RunPreDamage(assassin, in ability, ctx), "Урон способности не уклоняется");

            var auto = new DamageRequest(null, assassin, 30f, DamageType.True, ArmorK, isAutoAttack: true);
            Assert.IsTrue(es.RunPreDamage(assassin, in auto, ctx), "Заряд был цел — автоатака уклоняется");
        }

        // ===================== Фабрики / хелперы =====================

        private static EffectData StealthBuff()
        {
            var mod = new StatModifierComponent().With("_modifiers", new[]
            {
                new StatModifier(StatType.DamageTakenEff, ModifierOp.PercentMult, -0.4f),
                new StatModifier(StatType.MoveSpeed,      ModifierOp.PercentMult,  0.3f),
            });
            return TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Stealth | EffectTag.Buff, components: mod);
        }

        private static EffectData StealthPassive()
        {
            var stealth = new StealthComponent()
                .With("_stealthBuff", StealthBuff())
                .With("_empowerMult", 2f);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: stealth);
        }

        private static EffectData DodgePassive(int maxCharges, float rechargeSeconds)
        {
            var dodge = new DodgeComponent()
                .With("_maxCharges", maxCharges)
                .With("_rechargeSeconds", rechargeSeconds);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: dodge);
        }

        private static EffectData DodgePassiveStacking(int maxCharges, float rechargeSeconds)
        {
            var dodge = new DodgeComponent()
                .With("_maxCharges", maxCharges)
                .With("_rechargeSeconds", rechargeSeconds);
            return TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Neutral,
                stacking: StackRule.Stack, maxStacks: 2, components: dodge);
        }

        private static RelicData AssassinRelic(PassiveTrigger trigger)
        {
            var ai = new AIProfile(
                autoAttackTargeting: TargetingMode.Nearest,
                passiveTrigger: trigger);
            return TestRelic.Make(attackType: AttackType.Melee, ai: ai);
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new XorShiftRng(seed), ArmorK, new SpatialHash(CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float maxHp = 100f, float hp = -1f, float aad = 10f, float range = 5f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp < 0f ? maxHp : hp,
                Position         = pos,
                PreviousPosition = pos,
                Unit             = relic,
            };
        }

        /// <summary>Контекст с управляемым CurrentTick для теста перезарядки зарядов; наложение/диспел — реальный EffectSystem.</summary>
        private sealed class TickContext : ICombatContext
        {
            private readonly EffectSystem _effects;
            public int Tick;

            public TickContext(EffectSystem effects) => _effects = effects;

            public int CurrentTick => Tick;
            public float ArmorK => 100f;
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
            public IRngService Rng => null;

            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) => _effects.Apply(target, def, source, this);
            public void Dispel(in DispelRequest req) => _effects.Dispel(in req, this);
            public void Displace(in DisplaceRequest req) { }

            public void DealDamage(in DamageRequest req) { }
            public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) { }
            public void SpawnProjectile(in ProjectileSpawn spawn) { }
            public void ReportAreaHit(in AreaHit hit) { }
            public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) { }
            public void NotifyAttackInterrupted(RuntimeUnit unit) { }
            public int QueryUnitsInRadius(Vector2 c, float r, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
            public int QueryUnitsInLine(Vector2 o, Vector2 d, float len, float w, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
        }
    }
}
