using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
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
    /// Вертикальный срез «Надёжный защитник» (вики «13» §10.3): pre-damage «Оплот» (§9.3) —
    /// щит `20 + 15% недостающего HP` до вычета HP, поглощает триггер-удар, внутренний КД;
    /// активка «Решительный удар» (стан + −30% урона цели). Плюс влитый спайк S5: детерминизм
    /// pre-damage прохода (two-run checksum).
    /// </summary>
    public sealed class DefenderSliceTests
    {
        private const float ArmorK   = 100f;
        private const float CellSize = 3f;

        // ===================== §9.3 «Оплот» (pre-damage щит) =====================

        [Test]
        public void Bulwark_RaisesShield_BeforeHit_AbsorbsTriggeringHit()
        {
            var sim = BuildSim(1UL);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(defender, BulwarkPassive(), defender);

            // True-урон 30: брони нет, эффективности 1.0. «Оплот» до Execute поднимает щит 20 + 15%×100 = 35.
            sim.DealDamage(new DamageRequest(attacker, defender, 30f, DamageType.True, sim.ArmorK));

            Assert.AreEqual(100f, defender.CurrentHP, 1e-4f, "Триггер-удар поглощён щитом — HP не просело");
            Assert.AreEqual(5f, defender.CurrentShield, 1e-4f, "Остаток щита = 35 − 30");
        }

        [Test]
        public void Bulwark_ShieldScalesWithMissingHp()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            EffectData shield = BulwarkShield();

            var full = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f); // недостаёт 0
            var hurt = MakeUnit(1, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f); // недостаёт 100

            es.Apply(full, shield, full, ctx);
            es.Apply(hurt, shield, hurt, ctx);

            Assert.AreEqual(20f, full.CurrentShield, 1e-4f, "Целый: только плоские 20");
            Assert.AreEqual(35f, hurt.CurrentShield, 1e-4f, "Раненый: 20 + 15%×100 = 35");
        }

        [Test]
        public void Bulwark_RespectsInternalCooldown()
        {
            // Внутренний КД проверяем напрямую по ReactiveReadyTick: срабатывание перевзводит его,
            // блокировка — нет. (Не по щиту: в headless-контексте 2-сек щит-эффект не истекает без
            // EffectSystem.Tick, и повторный Apply ушёл бы в Refresh — это артефакт теста, не бага.)
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            RuntimeEffect bulwark = defender.ActiveEffects[0];
            var incoming = new DamageRequest(null, defender, 10f, DamageType.True, ArmorK);

            int cd = Mathf.RoundToInt(7f * SimConstants.TickRate);

            ctx.Tick = 0;
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd, bulwark.ReactiveReadyTick, "Первое срабатывание взвело внутренний КД");

            ctx.Tick = 1; // в пределах КД
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd, bulwark.ReactiveReadyTick, "В КД повторного срабатывания нет — КД не перевзведён");

            ctx.Tick = cd; // КД истёк
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd + cd, bulwark.ReactiveReadyTick, "После КД сработало снова — КД перевзведён");
        }

        [Test]
        public void Bulwark_None_DoesNotTrigger()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.None));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            es.RunPreDamage(defender, new DamageRequest(null, defender, 50f, DamageType.True, ArmorK), ctx);

            Assert.AreEqual(0f, defender.CurrentShield, 1e-4f, "Триггер None — щит не поднимается");
        }

        [Test]
        public void Bulwark_AboveThreshold_TriggersOnBigHit_NotSmall()
        {
            EffectData passive = BulwarkPassive();

            // Порог 20% от 200 = 40. Мелкий удар 30 < 40 — не срабатывает; крупный 50 > 40 — срабатывает.
            {
                var es = new EffectSystem(); var ctx = new TickContext(es);
                var d = MakeUnit(0, 0, Vector2.zero, maxHp: 200f, hp: 200f,
                    relic: DefenderRelic(PassiveTrigger.OnHitAbovePctMaxHp, thresholdPct: 0.2f));
                ctx.ApplyEffect(d, passive, d);
                es.RunPreDamage(d, new DamageRequest(null, d, 30f, DamageType.True, ArmorK), ctx);
                Assert.AreEqual(0f, d.CurrentShield, 1e-4f, "Удар ниже порога — нет щита");
            }
            {
                var es = new EffectSystem(); var ctx = new TickContext(es);
                var d = MakeUnit(0, 0, Vector2.zero, maxHp: 200f, hp: 200f,
                    relic: DefenderRelic(PassiveTrigger.OnHitAbovePctMaxHp, thresholdPct: 0.2f));
                ctx.ApplyEffect(d, passive, d);
                es.RunPreDamage(d, new DamageRequest(null, d, 50f, DamageType.True, ArmorK), ctx);
                Assert.AreEqual(20f, d.CurrentShield, 1e-4f, "Удар выше порога — щит поднят (целый → плоские 20)");
            }
        }

        // ===================== «Решительный удар» (актив) =====================

        [Test]
        public void ResoluteStrike_StunsAndWeakens_CurrentTargetOnly()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var caster    = MakeUnit(0, team: 0, pos: Vector2.zero);
            var mainThreat = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var bystander  = MakeUnit(2, team: 1, pos: new Vector2(3f, 0f));
            caster.CurrentTarget = mainThreat; // мозг (блок C/A) выбрал главную угрозу

            caster.Abilities.Add(new AbilityRuntime(ResoluteStrike()));
            var units = new List<RuntimeUnit> { caster, mainThreat, bystander };

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast);
            Assert.AreNotEqual(EffectTag.None, mainThreat.EffectTagMask & EffectTag.Control, "Цель оглушена");
            Assert.AreEqual(0.7f, mainThreat.Stats.Get(StatType.DamageDealtEff), 1e-4f, "Урон цели снижен на 30%");
            Assert.AreEqual(EffectTag.None, bystander.EffectTagMask & EffectTag.Control, "Соседа ульта не задевает");
        }

        // ===================== S5 (влитый): детерминизм pre-damage =====================

        [Test]
        public void PreDamage_TwoRunsSameChecksum_Deterministic()
        {
            ulong a = RunBulwarkBattle();
            ulong b = RunBulwarkBattle();
            Assert.AreEqual(a, b, "Pre-damage проход внёс рассинхрон между идентичными прогонами");
        }

        private static ulong RunBulwarkBattle()
        {
            var sim = BuildSim(7UL);
            var defender = MakeUnit(0, team: 0, pos: new Vector2(-2f, 0f), maxHp: 200f, hp: 200f,
                relic: DefenderRelic(PassiveTrigger.AnyHit), aad: 8f, range: 1.5f);
            var a1 = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), aad: 20f, range: 1.5f);
            var a2 = MakeUnit(2, team: 1, pos: new Vector2(2f, 1f), aad: 20f, range: 1.5f);

            sim.EnqueueUnitSpawn(defender);
            sim.EnqueueUnitSpawn(a1);
            sim.EnqueueUnitSpawn(a2);
            sim.Tick(SimConstants.TickDelta); // флаш спавнов
            sim.ApplyEffect(defender, BulwarkPassive(), defender);

            for (int t = 0; t < 150; t++) sim.Tick(SimConstants.TickDelta);
            return sim.ComputeChecksum();
        }

        // ===================== Фабрики контента среза =====================

        private static EffectData BulwarkShield()
        {
            var shield = new MissingHpShieldComponent().With("_flat", 20f).With("_pctMissingHp", 0.15f);
            return TestEffect.Make(
                baseDuration: 2f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Shield | EffectTag.Buff, stacking: StackRule.Refresh,
                components: shield);
        }

        private static EffectData BulwarkPassive()
        {
            var bulwark = new BulwarkComponent()
                .With("_internalCooldownSeconds", 7f)
                .With("_shieldEffect", BulwarkShield());
            return TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Neutral, components: bulwark);
        }

        private static AbilityData ResoluteStrike()
        {
            var stun = new ControlComponent().With("_preventAct", true);
            EffectData stunEffect = TestEffect.Make(
                baseDuration: 0.5f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Control | EffectTag.Debuff, components: stun);

            var weaken = new StatModifierComponent().With("_modifiers",
                new[] { new StatModifier(StatType.DamageDealtEff, ModifierOp.PercentMult, -0.3f) });
            EffectData weakenEffect = TestEffect.Make(
                baseDuration: 3f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff, components: weaken);

            return TestAbility.Make(
                effects: new[] { stunEffect, weakenEffect },
                cost: 0f, mode: AbilityTargetMode.NearestEnemy, castCondition: CastCondition.Immediately);
        }

        private static RelicData DefenderRelic(PassiveTrigger trigger, float thresholdPct = 0.2f)
        {
            var ai = new AIProfile(
                autoAttackTargeting: TargetingMode.HighestThreat,
                passiveTrigger: trigger,
                passiveThresholdPct: thresholdPct);
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
                Relic            = relic,
            };
        }

        /// <summary>Контекст с управляемым CurrentTick для теста внутреннего КД pre-damage; наложение — реальный EffectSystem.</summary>
        private sealed class TickContext : ICombatContext
        {
            private readonly EffectSystem _effects;
            public int Tick;

            public TickContext(EffectSystem effects) => _effects = effects;

            public int CurrentTick => Tick;
            public float ArmorK => 100f;
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
