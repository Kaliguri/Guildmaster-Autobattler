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

        // ===================== §9.3 «Оплот» (pre-damage щит) =====================

        [Test]
        public void Bulwark_RaisesShield_BeforeHit_AbsorbsTriggeringHit()
        {
            var sim = BuildSim(1UL);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(defender, BulwarkPassive(), defender);

            // True-урон 30: брони нет, эффективности 1.0. «Оплот» СИНХРОННО (pre-damage — исключение из
            // закона видимости) поднимает щит 20 + 15%×100 = 35, и он успевает к тому самому удару.
            sim.DealDamage(new DamageRequest(attacker, defender, 30f, DamageType.Pure, sim.ArmorK));
            sim.Tick(SimConstants.TickDelta);   // сам удар применяется реестром в конце тика

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
        public void Bulwark_SpendsTwoChargesThenWaitsForRecharge()
        {
            // Заряды проверяем напрямую по ChargeReadyTick: срабатывание тратит ОДИН заряд, каждый
            // перезаряжается независимо. (Не по щиту: в headless-контексте щит-эффект не истекает без
            // EffectSystem.Tick, и повторный Apply ушёл бы в Refresh — это артефакт теста, не бага.)
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            RuntimeEffect bulwark = defender.ActiveEffects[0];
            var incoming = new DamageRequest(null, defender, 10f, DamageType.Pure, CombatTestValues.ArmorK);

            int cd = Mathf.RoundToInt(7f * SimConstants.TickRate);

            Assert.AreEqual(2, bulwark.ChargeCount, "Два заряда взведены при наложении пассивки");

            ctx.Tick = 0;
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd, bulwark.ChargeReadyTick(0), "Первый удар потратил первый заряд");
            Assert.AreEqual(0,  bulwark.ChargeReadyTick(1), "Второй заряд ещё цел");

            ctx.Tick = 1; // сразу следом — второй удар гасится вторым зарядом
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(1 + cd, bulwark.ChargeReadyTick(1), "Второй удар подряд потратил второй заряд");

            ctx.Tick = 2; // зарядов не осталось — удар проходит, таймеры не сдвигаются
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd,     bulwark.ChargeReadyTick(0), "Без готовых зарядов первый таймер не перевзведён");
            Assert.AreEqual(1 + cd, bulwark.ChargeReadyTick(1), "Без готовых зарядов второй таймер не перевзведён");

            ctx.Tick = cd; // первый заряд восстановился
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(cd + cd, bulwark.ChargeReadyTick(0), "Восстановившийся заряд снова потрачен");
        }

        [Test]
        public void Bulwark_IgnoresDotTicks_AndThornsBackfire()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            RuntimeEffect bulwark = defender.ActiveEffects[0];

            es.RunPreDamage(defender, new DamageRequest(null, defender, 10f, DamageType.Pure, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.Periodic), ctx);
            es.RunPreDamage(defender, new DamageRequest(null, defender, 10f, DamageType.Pure, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.Reactive), ctx);

            Assert.AreEqual(0, bulwark.ChargeReadyTick(0), "Тик DoT заряд не тратит");
            Assert.AreEqual(0, bulwark.ChargeReadyTick(1), "Ответка шипов заряд не тратит");

            // Способность — прямой удар: щит встаёт.
            es.RunPreDamage(defender, new DamageRequest(null, defender, 10f, DamageType.Pure, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.Ability), ctx);
            Assert.AreNotEqual(0, bulwark.ChargeReadyTick(0), "Урон способности поднимает щит");
        }

        [Test]
        public void Bulwark_DoesNotRaiseTheShieldWhileIncapacitated()
        {
            // Решение Макса 29.07: пассивка, требующая ДЕЙСТВИЯ, не работает у выведенного контролем.
            // Щит носитель поднимает сам — оглушённый или спящий этого не может, и телеграф не имеет права
            // показывать поднимающийся щит у юнита, который собой не владеет.
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            RuntimeEffect bulwark = defender.ActiveEffects[0];
            var incoming = new DamageRequest(null, defender, 30f, DamageType.Pure, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack);

            defender.CanAct = defender.CanActAtTickStart = false;
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreEqual(0, bulwark.ChargeReadyTick(0), "Оглушённый щит не поднимает — заряд цел");
            Assert.AreEqual(0, defender.CurrentShield, 1e-4f, "И щита на нём не появилось");

            // Контроль кончился — та же пассивка работает как обычно (проверка, что гейт именно по CanAct,
            // а не «сломали Оплот»).
            defender.CanAct = defender.CanActAtTickStart = true;
            es.RunPreDamage(defender, in incoming, ctx);
            Assert.AreNotEqual(0, bulwark.ChargeReadyTick(0), "Дееспособный поднимает щит");
        }

        [Test]
        public void Thorns_StillBiteWhileIncapacitated()
        {
            // Обратная сторона того же правила: шипы колют БРОНЁЙ, а не действием. Оглушённый носитель
            // обязан продолжать колоть — иначе «дееспособность» превратилась бы в «под контролем не
            // работает ничего», а это уже другой дизайн.
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f,
                relic: DefenderRelic(PassiveTrigger.AnyHit));
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 200f, hp: 200f);

            var thorns = new ThornsComponent().With("_reflectFraction", 0.5f).With("_damageType", DamageType.Pure);
            ctx.ApplyEffect(defender, TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Neutral, components: thorns), defender);

            defender.CanAct = defender.CanActAtTickStart = false;
            es.Dispatch(defender, new CombatEventData(
                CombatEvent.DamageTaken, attacker, defender, 30f, EffectTag.None,
                sourceKind: DamageSourceKind.AutoAttack), ctx);

            Assert.AreEqual(1, ctx.Dealt.Count, "Шипы работают и у оглушённого: это свойство брони, не действие");
            Assert.AreEqual(attacker, ctx.Dealt[0].Target, "И колют они атакующего");
        }

        [Test]
        public void Bulwark_None_DoesNotTrigger()
        {
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var defender = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 100f,
                relic: DefenderRelic(PassiveTrigger.None));

            ctx.ApplyEffect(defender, BulwarkPassive(), defender);
            es.RunPreDamage(defender, new DamageRequest(null, defender, 50f, DamageType.Pure, CombatTestValues.ArmorK), ctx);

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
                es.RunPreDamage(d, new DamageRequest(null, d, 30f, DamageType.Pure, CombatTestValues.ArmorK), ctx);
                Assert.AreEqual(0f, d.CurrentShield, 1e-4f, "Удар ниже порога — нет щита");
            }
            {
                var es = new EffectSystem(); var ctx = new TickContext(es);
                var d = MakeUnit(0, 0, Vector2.zero, maxHp: 200f, hp: 200f,
                    relic: DefenderRelic(PassiveTrigger.OnHitAbovePctMaxHp, thresholdPct: 0.2f));
                ctx.ApplyEffect(d, passive, d);
                es.RunPreDamage(d, new DamageRequest(null, d, 50f, DamageType.Pure, CombatTestValues.ArmorK), ctx);
                Assert.AreEqual(20f, d.CurrentShield, 1e-4f, "Удар выше порога — щит поднят (целый → плоские 20)");
            }
        }

        // ===================== «Решительный удар» (актив) =====================

        [Test]
        public void ResoluteStrike_CastItself_AppliesNothingToTheTarget()
        {
            // Активка — заявка на удар, а не удар. На касте цель не получает ничего: ни урона, ни стана,
            // ни ослабления. Всё это придёт вместе с попаданием — или не придёт вовсе.
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var caster     = MakeUnit(0, team: 0, pos: Vector2.zero);
            var mainThreat = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            caster.CurrentTarget = mainThreat;

            caster.Abilities.Add(new AbilityRuntime(ResoluteStrike()));
            var units = new List<RuntimeUnit> { caster, mainThreat };

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);
            foreach (RuntimeUnit u in units) EffectSystem.CommitPending(u);

            Assert.IsTrue(cast, "Активка скастована");
            Assert.AreEqual(EffectTag.None, mainThreat.EffectTagMask & EffectTag.Control,
                "На касте цель НЕ оглушена — стан ложится при попадании");
            Assert.AreEqual(1f, mainThreat.Stats.Get(StatType.DamageDealtEff), 1e-4f,
                "На касте ослабления нет");
            Assert.AreEqual(2f, caster.EmpowerDamageMult, 1e-4f, "Зато взведён уникальный удар ×2");
            Assert.AreEqual(0, caster.AttackCooldownTicks, "И он выходит вне очереди: ожидание интервала снято");
        }

        [Test]
        public void ResoluteStrike_StunAndWeaken_LandOnHit()
        {
            var sim = BuildSim(11UL);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero, relic: DefenderRelic(PassiveTrigger.None),
                aad: 10f, range: 3f);
            var target = MakeUnit(1, team: 1, pos: new Vector2(1.5f, 0f), maxHp: 10000f, hp: 10000f);
            caster.CurrentTarget = target;
            caster.AutoAttackTarget = target;

            sim.EnqueueUnitSpawn(caster);
            sim.EnqueueUnitSpawn(target);
            sim.Tick(SimConstants.TickDelta);

            caster.Abilities.Add(new AbilityRuntime(ResoluteStrike()));
            new AbilitySystem().TryCast(caster, 0, new List<RuntimeUnit> { caster, target }, sim);

            // Ждём, пока взведённый удар дозреет и прилетит.
            for (int i = 0; i < 60 && (target.EffectTagMask & EffectTag.Control) == 0; i++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreNotEqual(EffectTag.None, target.EffectTagMask & EffectTag.Control,
                "Стан пришёл вместе с ударом");
            Assert.AreEqual(0.7f, target.Stats.Get(StatType.DamageDealtEff), 1e-4f,
                "Ослабление пришло тем же ударом");
            Assert.Less(target.CurrentHP, 10000f, "И урон нанесён самим ударом, а не активкой");
        }

        [Test]
        public void ResoluteStrike_SwingInterrupted_AppliesNothing()
        {
            // Главное следствие модели: уникальный удар можно СБИТЬ. Контроль в замахе — и заявка ушла
            // в никуда, ни стана, ни ослабления, ни урона.
            var sim = BuildSim(12UL);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero, relic: DefenderRelic(PassiveTrigger.None),
                aad: 10f, range: 3f);
            var target = MakeUnit(1, team: 1, pos: new Vector2(1.5f, 0f), maxHp: 10000f, hp: 10000f);
            caster.CurrentTarget = target;
            caster.AutoAttackTarget = target;

            sim.EnqueueUnitSpawn(caster);
            sim.EnqueueUnitSpawn(target);
            sim.Tick(SimConstants.TickDelta);

            caster.Abilities.Add(new AbilityRuntime(ResoluteStrike()));
            new AbilitySystem().TryCast(caster, 0, new List<RuntimeUnit> { caster, target }, sim);

            sim.Tick(SimConstants.TickDelta);          // заряженный замах пошёл

            // Срываем НАСТОЯЩИМ станом, а не флагом: CanAct пересчитывается системой эффектов каждый
            // тик, и присвоенное руками значение она затирает на следующем же проходе.
            var stun = new ControlComponent().With("_preventAct", true);
            sim.ApplyEffect(caster, TestEffect.Make(
                baseDuration: 1f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Control | EffectTag.Debuff, components: stun), caster);

            for (int i = 0; i < 10; i++) sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(EffectTag.None, target.EffectTagMask & EffectTag.Control,
                "Сорванный удар не оглушает");
            Assert.AreEqual(1f, target.Stats.Get(StatType.DamageDealtEff), 1e-4f,
                "И не ослабляет");
            Assert.AreEqual(10000f, target.CurrentHP, 1e-4f, "И не наносит урона");
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
            var bulwark = new BlockComponent()
                .With("_maxCharges", 2)
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

            // Модель Макса 2026-07-31: активка НЕ бьёт сама. Она взводит уникальный удар — ×2, вне
            // очереди, — а стан и ослабление ложатся ПРИ ПОПАДАНИИ. Поэтому удар можно сбить контролем,
            // от него можно уклониться, и он может уйти в промах, ничего не наложив.
            var empower = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_recastImmediately", true)
                .With("_bonusOnHitEffects", new[] { stunEffect, weakenEffect })
                .With("_bonusOnHitCount", 1)
                .With("_consumeTag", EffectTag.Empowered);

            EffectData charge = TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Neutral, tags: EffectTag.Empowered,
                components: empower);

            return TestAbility.Make(
                selfEffects: new[] { charge },
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
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
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
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
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
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
            public IRngService Rng => null;

            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) => _effects.Apply(target, def, source, this);
            // Срок, посчитанный по ходу боя, заглушке безразличен — она мерит факт наложения.
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds)
                => ApplyEffect(target, def, source);

            // Наложение с величиной (порции кровотечения): заглушке величина безразлична —
            // она мерит факт наложения.
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds,
                float potency)
                => ApplyEffect(target, def, source);
            public void Dispel(in DispelRequest req) => _effects.Dispel(in req, this);
            // Слепота стабу не нужна: промах проверяют свои тесты, здесь удар всегда доходит.
            public bool ResolveAttackMiss(RuntimeUnit attacker) => false;
            public void ReportAttackMissed(RuntimeUnit attacker, RuntimeUnit target) { }
            // Каст никто не слушает: реакцию на чужое заклинание проверяют бои, а не заглушка.
            public void ReportAbilityCast(RuntimeUnit caster) { }
            public void Displace(in DisplaceRequest req) { }

            // Призывов в этом срезе нет: стаб честно отвечает «призывать нечем».
            public RuntimeUnit Summon(UnitData data, int team, Vector2 position, RuntimeUnit summoner) => null;

            // Заглушке нечего откладывать: раундов тут нет, поэтому переход отыгрывается сразу.
            public void TeleportBehind(RuntimeUnit unit, RuntimeUnit target)
                => CombatPositioning.TeleportBehind(unit, target);

            /// <summary>Что через контекст просили нанести. Нужно реактивам: их работа — это ВЫЗОВ урона,
            /// и без записи «сработали ли шипы» проверялось бы по HP, которого пустышка не двигает.</summary>
            public readonly List<DamageRequest> Dealt = new List<DamageRequest>();

            public void DealDamage(in DamageRequest req) => Dealt.Add(req);
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
