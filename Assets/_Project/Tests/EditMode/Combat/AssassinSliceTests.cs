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
    /// Вертикальный срез «Скрытный убийца» (вики «13» §10.5): «Скрытность» (§9.6 — усиление первой
    /// авто-атаки, рестелс после своего убийства) и «Изворотливость» (§9.3/§9.4 — 2 заряда негейта
    /// входящего удара с независимой перезарядкой).
    /// </summary>
    public sealed class AssassinSliceTests
    {

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
            EffectSystem.CommitPending(assassin);                // фабрика тем же и заканчивает — иначе пассивка не видна

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

            sim.DealDamage(new DamageRequest(assassin, victim, 999f, DamageType.Pure, sim.ArmorK)); // смертельный удар
            sim.Tick(SimConstants.TickDelta); // дренаж UnitKilled → рестелс: подкрепление бафа
            // Юниты этого стенда не зарегистрированы в симуляции, поэтому закон видимости
            // (CommitTickChanges по списку боя) до них не доходит — проявляем отложенное вручную.
            EffectSystem.CommitPending(assassin);

            Assert.AreEqual(2f, assassin.EmpowerDamageMult, 1e-4f, "После своего убийства «Скрытность» перевзводит усиление");
            Assert.AreNotEqual(EffectTag.None, assassin.EffectTagMask & EffectTag.Stealth,
                "Усиление приходит ВМЕСТЕ с бафом скрытности, а не отдельно от него");
        }

        [Test]
        public void Stealth_Dispelled_TakesEmpowerWithIt()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, relic: AssassinRelic(PassiveTrigger.AnyHit));

            sim.ApplyEffect(assassin, StealthBuff(), assassin);
            sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(2f, assassin.EmpowerDamageMult, 1e-4f, "Баф скрытности взвёл усиление");
            Assert.AreEqual(20f, assassin.EmpowerFlatPen, 1e-4f, "И пробивание брони на один удар");

            sim.Dispel(new DispelRequest(assassin, DispelTargetPolarity.Any, EffectTag.Stealth, int.MaxValue, 0));
            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(0f, assassin.EmpowerDamageMult, 1e-4f,
                "Развеяли скрытность — заряженный удар уходит вместе с ней, а не остаётся наградой за сработавшую контру");
            Assert.AreEqual(0f, assassin.EmpowerFlatPen, 1e-4f, "Пробивание тоже снято");
        }

        // ===================== «Уйти в тень» (активка, мана 75) =====================

        [Test]
        public void ShadowStep_CloaksWithoutKill_AndSpendsResource()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, relic: AssassinRelic(PassiveTrigger.AnyHit));
            assassin.Stats.AddModifiersFrom("resource", new[]
            {
                new StatModifier(StatType.MaxResource, ModifierOp.Flat, 75f),
            });
            assassin.CurrentResource = 75f;

            var units = new List<RuntimeUnit> { assassin };
            assassin.Abilities.Add(new AbilityRuntime(ShadowStep()));

            Assert.IsTrue(new AbilitySystem().TryCast(assassin, 0, units, ctx), "Хватает маны — активка кастуется");
            EffectSystem.CommitPending(assassin);

            Assert.AreEqual(0f, assassin.CurrentResource, 1e-4f, "Запас равен стоимости: каст обнуляет шкалу");
            Assert.AreNotEqual(EffectTag.None, assassin.EffectTagMask & EffectTag.Stealth,
                "Убийца уходит в тень САМ, без убийства");
            Assert.AreEqual(2f, assassin.EmpowerDamageMult, 1e-4f, "Тот же баф — то же усиление, что от пассивки");
            Assert.AreEqual(0f, assassin.Abilities[0].CooldownRemaining, 1e-4f,
                "Гейт один — ресурсный: кулдауна у активки нет");
        }

        [Test]
        public void ShadowStep_WithoutResource_DoesNotCast()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, relic: AssassinRelic(PassiveTrigger.AnyHit));
            assassin.CurrentResource = 74f;

            var units = new List<RuntimeUnit> { assassin };
            assassin.Abilities.Add(new AbilityRuntime(ShadowStep()));

            Assert.IsFalse(new AbilitySystem().TryCast(assassin, 0, units, ctx), "Одной единицы не хватило — каста нет");
            Assert.AreEqual(74f, assassin.CurrentResource, 1e-4f, "Ресурс не списан");
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
            var hit = new DamageRequest(null, assassin, 30f, DamageType.Pure, CombatTestValues.ArmorK, sourceKind: DamageSourceKind.AutoAttack);

            ctx.Tick = 0;
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx),  "1-й удар негейтнут (заряд 1)");
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx),  "2-й удар негейтнут (заряд 2)");
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx), "3-й удар проходит — зарядов нет");

            int recharge = Mathf.RoundToInt(8f * SimConstants.TickRate);
            ctx.Tick = recharge; // прошёл кулдаун одного заряда
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx), "Заряд восстановился — снова негейт");
        }

        [Test]
        public void Dodge_DoesNotWorkWhileIncapacitated()
        {
            // Решение Макса 29.07: «Изворотливость» — кувырок с уходом с места, то есть ДЕЙСТВИЕ.
            // Оглушённый ассасин уклоняться не может, и удар по нему проходит.
            var es  = new EffectSystem();
            var ctx = new TickContext(es);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));

            ctx.ApplyEffect(assassin, DodgePassive(maxCharges: 2, rechargeSeconds: 8f), assassin);
            var hit = new DamageRequest(null, assassin, 30f, DamageType.Pure, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack);

            ctx.Tick = 0;
            assassin.CanAct = assassin.CanActAtTickStart = false;
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx), "Оглушённый не уклоняется — удар проходит");

            // И заряд при этом не потрачен: контроль отнимает возможность, а не запас.
            assassin.CanAct = assassin.CanActAtTickStart = true;
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx), "Заряды целы: под контролем они не тратятся");
            Assert.IsTrue(es.RunPreDamage(assassin, in hit, ctx), "Оба заряда на месте");
            Assert.IsFalse(es.RunPreDamage(assassin, in hit, ctx), "Третий удар проходит — вот теперь зарядов нет");
        }

        [Test]
        public void Dodge_NegatedHit_DealsNoDamage_ViaSimulation()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(assassin, DodgePassive(maxCharges: 1, rechargeSeconds: 8f), assassin);

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.Pure, sim.ArmorK, sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);   // удары применяются реестром в конце тика
            Assert.AreEqual(200f, assassin.CurrentHP, 1e-4f, "Первый удар негейтнут — HP не тронуто");

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.Pure, sim.ArmorK, sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);
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

            var hit = new DamageRequest(null, assassin, 30f, DamageType.Pure, CombatTestValues.ArmorK, sourceKind: DamageSourceKind.AutoAttack);
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

            var ability = new DamageRequest(null, assassin, 30f, DamageType.Pure, CombatTestValues.ArmorK); // isAutoAttack=false
            Assert.IsFalse(es.RunPreDamage(assassin, in ability, ctx), "Урон способности не уклоняется");

            var auto = new DamageRequest(null, assassin, 30f, DamageType.Pure, CombatTestValues.ArmorK, sourceKind: DamageSourceKind.AutoAttack);
            Assert.IsTrue(es.RunPreDamage(assassin, in auto, ctx), "Заряд был цел — автоатака уклоняется");
        }

        // ===================== Кувырок уклонения (решение 2026-07-26) =====================

        // Двигался — катится ПО ходу своего движения: уклонение не сбивает план, а ускоряет его.
        [Test]
        public void DodgeRoll_WhileMoving_GoesAlongOwnIntent_AndHastes()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            assassin.PreviousPosition = new Vector2(-1f, 0f); // шёл вперёд по +X
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f)); // стоит как раз впереди

            sim.ApplyEffect(assassin, DodgePassive(maxCharges: 1, rechargeSeconds: 8f), assassin);
            float baseSpeed = assassin.Stats.Get(StatType.MoveSpeed);

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.Pure, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta); // перекат: 2 ед. на 12 ед/сек

            Assert.AreEqual(200f, assassin.CurrentHP, 1e-4f, "Предусловие: удар негейтнут");
            Assert.AreEqual(2f, assassin.Position.x, 0.05f, "Кувырок унёс на дистанцию переката по ходу движения");
            Assert.AreEqual(0f, assassin.Position.y, 1e-3f, "Вбок кувырок не уводит");

            EffectSystem.CommitPending(assassin); // юниты стенда вне списка боя — проявляем вручную
            Assert.Greater(assassin.Stats.Get(StatType.MoveSpeed), baseSpeed,
                "После переката висит ускорение — кувырок нужен, чтобы занять позицию");
        }

        // Стоял вплотную и бил — катится ОТ атакующего: разрыв дистанции происходит сам.
        [Test]
        public void DodgeRoll_StandingStill_GoesAwayFromAttacker()
        {
            var sim = BuildSim(1UL);
            var assassin = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 200f, hp: 200f,
                relic: AssassinRelic(PassiveTrigger.AnyHit));
            assassin.PreviousPosition = Vector2.zero; // стоит на месте
            var attacker = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(assassin, DodgePassive(maxCharges: 1, rechargeSeconds: 8f), assassin);

            sim.DealDamage(new DamageRequest(attacker, assassin, 50f, DamageType.Pure, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(-2f, assassin.Position.x, 0.05f, "Кувырок ушёл от атакующего, а не сквозь него");
        }

        // ===================== Фабрики / хелперы =====================

        private static EffectData StealthBuff()
        {
            var mod = new StatModifierComponent().With("_modifiers", new[]
            {
                new StatModifier(StatType.DamageTakenEff, ModifierOp.PercentMult, -0.4f),
                new StatModifier(StatType.MoveSpeed,      ModifierOp.PercentMult,  0.3f),
            });
            // Усиление живёт на бафе, а не на том, кто его выдал — так активка «Уйти в тень»
            // и пассивка «Скрытность» дают одно и то же, не дублируя чисел.
            var empower = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_flatPen", 20f)
                .With("_blinkBehind", true);
            // stacking как в ассете StealthBuff (Refresh): повторный уход в тень поверх висящего бафа
            // подкрепляет его и заново взводит одноразовое усиление (IRearmOnRefreshComponent).
            return TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Stealth | EffectTag.Buff, stacking: StackRule.Refresh,
                components: new IEffectComponent[] { mod, empower });
        }

        private static EffectData StealthPassive()
        {
            var stealth = new StealthComponent()
                .With("_stealthBuff", StealthBuff());
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: stealth);
        }

        /// <summary>«Уйти в тень»: активка на себя за 75 маны, без кулдауна — накладывает тот же баф скрытности.</summary>
        private static AbilityData ShadowStep() =>
            TestAbility.Make(effects: new[] { StealthBuff() }, cooldown: 0f, cost: 75f,
                mode: AbilityTargetMode.Self);

        /// <summary>Числа как в ассете Dodge: перекат 2 ед. на 12 ед/сек + баф ускорения на 1 с.</summary>
        private static EffectData DodgePassive(int maxCharges, float rechargeSeconds)
        {
            var dodge = new DodgeComponent()
                .With("_maxCharges", maxCharges)
                .With("_rechargeSeconds", rechargeSeconds)
                .With("_rollDistance", 2f)
                .With("_rollSpeedPerSecond", 12f)
                .With("_hasteBuff", DodgeHaste());
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: dodge);
        }

        /// <summary>Ускорение после переката: +100% скорости передвижения на 1 с (ассет DodgeHaste).</summary>
        private static EffectData DodgeHaste()
        {
            var mod = new StatModifierComponent().With("_modifiers", new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, 1f),
            });
            return TestEffect.Make(
                baseDuration: 1f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff,
                stacking: StackRule.Refresh, components: mod);
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
            public int QueryUnitsInRadius(Vector2 c, float r, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
            public int QueryUnitsInLine(Vector2 o, Vector2 d, float len, float w, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
        }
    }
}
