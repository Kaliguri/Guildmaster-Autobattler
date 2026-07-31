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
    /// Модель огня The Pyre (принята 2026-07-26/4): расщепление удара по горящей цели, накопление
    /// «Углей» от любого огня и их сход по ускоряющейся кривой. Числа — карточка [[the-pyre]].
    /// </summary>
    public sealed class PyreFireModelTests
    {
        private const float ArmorK = 100f;

        // ===================== Расщепление удара (половина клинком, половина огнём) =====================

        [Test]
        public void AutoAttack_OnBurningTarget_SplitsHalfIntoFire()
        {
            var sim = BuildSim();
            var pyre   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            sim.EnqueueUnitSpawn(pyre);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(pyre, SplitPassive(), pyre);
            sim.ApplyEffect(victim, BurningMarker(), pyre); // цель уже горит
            sim.Tick(SimConstants.TickDelta);               // «уже горит» = с прошлого тика (закон видимости)

            var hits = new System.Collections.Generic.List<(DamageType type, float dmg)>();
            sim.OnDamageDealt += (src, tgt, res) => hits.Add((res.Type, res.TotalDamage));

            sim.DealDamage(new DamageRequest(pyre, victim, 100f, DamageType.Slash, ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);   // удар применяется реестром в конце тика

            Assert.AreEqual(2, hits.Count, "Удар по горящей цели приходит двумя половинами");
            Assert.AreEqual(DamageType.Slash, hits[0].type, "Первая половина — клинок");
            Assert.AreEqual(DamageType.Fire, hits[1].type, "Вторая половина — огонь");
            Assert.AreEqual(100f, hits[0].dmg + hits[1].dmg, 1e-3f, "Суммарный урон удара не изменился");
        }

        [Test]
        public void AutoAttack_OnCleanTarget_StaysWhole()
        {
            var sim = BuildSim();
            var pyre   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            sim.EnqueueUnitSpawn(pyre);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(pyre, SplitPassive(), pyre); // цель НЕ горит

            int hits = 0;
            sim.OnDamageDealt += (src, tgt, res) => hits++;

            sim.DealDamage(new DamageRequest(pyre, victim, 100f, DamageType.Slash, ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);   // удар применяется реестром в конце тика

            Assert.AreEqual(1, hits, "Первый удар по негорящей цели идёт целиком клинком");
        }

        // ===================== «Угли»: накопление и сход =====================

        [Test]
        public void FireDamage_LaysAnEmber_OnTheTarget()
        {
            var sim = BuildSim();
            var pyre   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            sim.EnqueueUnitSpawn(pyre);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(pyre, IgniterPassive(), pyre);

            sim.DealDamage(new DamageRequest(pyre, victim, 10f, DamageType.Fire, ArmorK,
                sourceKind: DamageSourceKind.Periodic));
            sim.Tick(SimConstants.TickDelta); // событие доставляется через очередь

            Assert.AreNotEqual(EffectTag.None, victim.EffectTagMask & EffectTag.Ember, "Огонь оставил уголёк");

            // Не-огонь уголька не кладёт.
            var clean = MakeUnit(2, team: 1, pos: new Vector2(2f, 0f));
            sim.EnqueueUnitSpawn(clean);
            sim.Tick(SimConstants.TickDelta);
            sim.DealDamage(new DamageRequest(pyre, clean, 10f, DamageType.Slash, ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(EffectTag.None, clean.EffectTagMask & EffectTag.Ember, "Сталь углей не оставляет");
        }

        [Test]
        public void Embers_HoldThroughGrace_ThenFallOffFaster()
        {
            var sys = new EffectSystem();
            var ctx = new TickingContext(sys);
            var victim = MakeUnit(1, team: 1, pos: Vector2.zero);

            EffectData ember = EmberEffect();
            for (int i = 0; i < 5; i++) sys.Apply(victim, ember, null, ctx);
            RuntimeEffect eff = victim.ActiveEffects[0];
            Assert.AreEqual(5, eff.Stacks, "Пять угольков");

            var units = new System.Collections.Generic.List<RuntimeUnit> { victim };

            // Льготные 5 секунд: стаки держатся.
            AdvanceSeconds(sys, units, ctx, 4.5f);
            Assert.AreEqual(5, eff.Stacks, "В льготном окне ничего не осыпается");

            // Дальше — сход, и каждый следующий быстрее предыдущего.
            AdvanceSeconds(sys, units, ctx, 1.2f);
            Assert.AreEqual(4, eff.Stacks, "Первый стак ушёл через секунду после окна");

            AdvanceSeconds(sys, units, ctx, 0.8f);
            Assert.AreEqual(3, eff.Stacks, "Второй ушёл быстрее первого (интервал ×0.75)");
        }

        [Test]
        public void FireDamage_OnEmberedTarget_ReportsHowMuchEmbersAdded()
        {
            var sim = BuildSim();
            var pyre   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            sim.EnqueueUnitSpawn(pyre);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            EffectData ember = EmberEffect();
            for (int i = 0; i < 10; i++) sim.ApplyEffect(victim, ember, pyre);
            // Уязвимость считается по стакам НАЧАЛА тика (закон видимости). Стаки набраны между тиками,
            // поэтому границу тика надо провести явно — иначе удар увидит один уголёк из десяти.
            EffectSystem.CommitPending(victim);

            DamageResult captured = default;
            sim.OnDamageDealt += (src, tgt, res) => captured = res;

            sim.DealDamage(new DamageRequest(pyre, victim, 100f, DamageType.Fire, ArmorK));
            sim.Tick(SimConstants.TickDelta);   // удар применяется реестром в конце тика

            // Десять угольков по 1% → удар сильнее на 10%, и результат обязан уметь это назвать:
            // без разбивки стенд не отличит «кит разогнался» от «кит и так столько бьёт».
            Assert.AreEqual(1.1f, captured.Vulnerability, 1e-3f, "Множитель уязвимости — 10 угольков по 1%");
            Assert.AreEqual(110f, captured.TotalDamage, 1e-2f, "Брони нет: 100 сырого × 1.1");
            Assert.AreEqual(10f, captured.VulnerabilityBonus, 1e-2f, "Из 110 ровно 10 добавили угольки");
        }

        // ===================== Фабрики =====================

        private static void AdvanceSeconds(EffectSystem sys, System.Collections.Generic.List<RuntimeUnit> units,
                                           TickingContext ctx, float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * SimConstants.TickRate);
            for (int i = 0; i < ticks; i++)
            {
                ctx.Tick++;
                sys.Tick(units, ctx, SimConstants.TickDelta);
            }
        }

        private static EffectData EmberEffect() => TestEffect.Make(
            baseDuration: -1f, polarity: EffectPolarity.Debuff,
            tags: EffectTag.Debuff | EffectTag.Ember,
            stacking: StackRule.Stack, maxStacks: 999,
            components: new EmberComponent()
                .With("_fireDamagePerStack", 0.01f)
                .With("_graceSeconds", 5f)
                .With("_firstDecaySeconds", 1f)
                .With("_decayFalloff", 0.75f)
                .With("_minDecaySeconds", 0.25f));

        private static EffectData SplitPassive() => TestEffect.Make(
            baseDuration: -1f, polarity: EffectPolarity.Neutral,
            components: new SplitAttackOnTagComponent()
                .With("_requiredTargetTag", EffectTag.Burn)
                .With("_share", 0.5f)
                .With("_damageType", DamageType.Fire));

        private static EffectData IgniterPassive() => TestEffect.Make(
            baseDuration: -1f, polarity: EffectPolarity.Neutral,
            components: new EmberIgniterComponent().With("_emberEffect", EmberEffect()));

        /// <summary>Пустой эффект с тегом «Поджог» — маркер «цель горит» для теста расщепления.</summary>
        private static EffectData BurningMarker() => TestEffect.Make(
            baseDuration: -1f, polarity: EffectPolarity.Debuff,
            tags: EffectTag.Debuff | EffectTag.DoT | EffectTag.Burn);

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new XorShiftRng(1UL), ArmorK, new SpatialHash(3f),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 10000f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 2f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });
            return new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats, CurrentHP = maxHp,
                Position = pos, PreviousPosition = pos,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }

        /// <summary>Контекст с управляемым тиком: «Угли» ведут сход по абсолютным тикам, а не по dt.</summary>
        private sealed class TickingContext : ICombatContext
        {
            private readonly EffectSystem _effects;
            public int Tick;

            public TickingContext(EffectSystem effects) => _effects = effects;

            public int CurrentTick => Tick;
            public float ArmorK => 100f;
            public SimTuning Tuning => SimTuning.Default;
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
            public void DealDamage(in DamageRequest req) { }
            public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) { }
            public void SpawnProjectile(in ProjectileSpawn spawn) { }
            public void ReportAreaHit(in AreaHit hit) { }
            public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) { }
            public void NotifyAttackInterrupted(RuntimeUnit unit) { }
            public int QueryUnitsInRadius(Vector2 c, float r, System.Collections.Generic.List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
            public int QueryUnitsInLine(Vector2 o, Vector2 d, float len, float w, System.Collections.Generic.List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
        }
    }
}
