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
    /// Вертикальный срез «Железный копейщик» (вики «13» шаг 4): линейная авто-атака
    /// «Размашистый выпад», круговой AOE-актив «Стальной вихрь» (×3 от AutoAttackDamage),
    /// cast-условие (блоки D/E) и прирост маны за удар.
    /// </summary>
    public sealed class SpearmanSliceTests
    {
        // ===================== Линейная авто-атака («Размашистый выпад») =====================

        [Test]
        public void LineAutoAttack_HitsAllEnemiesInBand_MissesOutsideAndAllies()
        {
            RelicData spear = TestRelic.Make(autoAttackShape: AreaShape.Line, autoAttackWidth: 2f); // полуширина 1
            var attacker = MakeUnit(0, team: 0, pos: new Vector2(0f, 0f), relic: spear, range: 5f, aad: 10f);

            var target = MakeUnit(1, team: 1, pos: new Vector2(5f, 0f));   // прямо по курсу (== радиус) → в полосе
            var sideIn = MakeUnit(2, team: 1, pos: new Vector2(3f, 0.5f)); // perp 0.5 ≤ 1 → в полосе
            var sideOut = MakeUnit(3, team: 1, pos: new Vector2(3f, 3f));  // perp 3 > 1 → мимо
            var ally    = MakeUnit(4, team: 0, pos: new Vector2(2f, 0f));  // в полосе, но союзник → исключён
            attacker.CurrentTarget = target;

            var units = new List<RuntimeUnit> { attacker, target, sideIn, sideOut, ally };
            var ctx   = new SpatialStubContext(units);

            TickUntilDamage(units, ctx);

            CollectionAssert.AreEquivalent(
                new[] { target, sideIn },
                ctx.Damage.ConvertAll(d => d.Target),
                "Линейная АА должна задеть всех врагов в полосе, кроме боковых и союзников");
            Assert.That(ctx.Damage.TrueForAll(d => Mathf.Approximately(d.RawDamage, 10f)), "Урон каждой цели = AutoAttackDamage");
        }

        [Test]
        public void LineAutoAttack_ReachesUpToAttackRange_NotBeyond()
        {
            RelicData spear = TestRelic.Make(autoAttackShape: AreaShape.Line, autoAttackWidth: 2f);
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: spear, range: 5f);

            // Длина линии = досягаемость с учётом тел (AttackRange + радиусы обоих тел ≈ 5 + 0.575·2 ≈ 6.15).
            var near = MakeUnit(1, team: 1, pos: new Vector2(4.9f, 0f)); // в пределах reach → бьём
            var far  = MakeUnit(2, team: 1, pos: new Vector2(7f,   0f)); // дальше reach линии → мимо
            attacker.CurrentTarget = near;

            var units = new List<RuntimeUnit> { attacker, near, far };
            var ctx   = new SpatialStubContext(units);

            TickUntilDamage(units, ctx);

            CollectionAssert.AreEquivalent(new[] { near }, ctx.Damage.ConvertAll(d => d.Target),
                "Длина линии = body-aware reach: цель за пределом досягаемости не задевается");
        }

        // ===================== Круговой AOE-актив («Стальной вихрь») =====================

        [Test]
        public void SteelWhirl_DealsTripleAutoAttackDamage_ToEnemiesInRadius_NotAllies()
        {
            AbilityData whirl = TestAbility.Make(
                cost: 0f, damageMultiplier: 3f, areaShape: AreaShape.Circle, areaRadius: 3f);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero, aad: 10f);
            caster.Abilities.Add(new AbilityRuntime(whirl));

            var inA  = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));   // в радиусе
            var inB  = MakeUnit(2, team: 1, pos: new Vector2(2f, 1f));   // в радиусе
            var ally = MakeUnit(3, team: 0, pos: new Vector2(1f, 1f));   // в радиусе, но союзник
            var outE = MakeUnit(4, team: 1, pos: new Vector2(5f, 0f));   // вне радиуса

            var units = new List<RuntimeUnit> { caster, inA, inB, ally, outE };
            var ctx   = new SpatialStubContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast);
            CollectionAssert.AreEquivalent(new[] { inA, inB }, ctx.Damage.ConvertAll(d => d.Target),
                "Вихрь бьёт только врагов в радиусе");
            Assert.That(ctx.Damage.TrueForAll(d => Mathf.Approximately(d.RawDamage, 30f)),
                "Урон = 3 × AutoAttackDamage (3 × 10)");
        }

        // ===================== Cast-условие (блоки D/E) =====================

        [Test]
        public void CastCondition_EnemiesInRadius_BelowThreshold_DoesNotCast()
        {
            AbilityData ab = TestAbility.Make(
                cost: 0f, cooldown: 5f, damageMultiplier: 3f, areaShape: AreaShape.Circle, areaRadius: 3f,
                castCondition: CastCondition.EnemiesInRadius, castConditionCount: 2);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            caster.Abilities.Add(new AbilityRuntime(ab));

            var lone = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f)); // лишь 1 враг < 2
            var units = new List<RuntimeUnit> { caster, lone };
            var ctx   = new SpatialStubContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsFalse(cast, "Меньше X врагов в радиусе — каста нет");
            Assert.AreEqual(0, ctx.Damage.Count);
            Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "Кулдаун не ставится при невыполненном условии");
        }

        [Test]
        public void CastCondition_EnemiesInRadius_MetThreshold_Casts()
        {
            AbilityData ab = TestAbility.Make(
                cost: 0f, cooldown: 5f, damageMultiplier: 3f, areaShape: AreaShape.Circle, areaRadius: 3f,
                castCondition: CastCondition.EnemiesInRadius, castConditionCount: 2);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            caster.Abilities.Add(new AbilityRuntime(ab));

            var e1 = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            var e2 = MakeUnit(2, team: 1, pos: new Vector2(2f, 0f)); // 2 врага ≥ 2 → каст
            var units = new List<RuntimeUnit> { caster, e1, e2 };
            var ctx   = new SpatialStubContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast, "X врагов в радиусе достигнуто — кастуем");
            Assert.Greater(caster.Abilities[0].CooldownRemaining, 0f);
        }

        [Test]
        public void CastOverride_SelfLowHp_CastsRegardlessOfCondition()
        {
            // Условие требует 5 врагов (не будет выполнено), но отмена по HP ≤ 30%.
            AbilityData ab = TestAbility.Make(
                cost: 0f, cooldown: 5f, areaShape: AreaShape.Circle, areaRadius: 3f,
                castCondition: CastCondition.EnemiesInRadius, castConditionCount: 5,
                castOverrideSelfHpPct: 0.3f);
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 20f); // 20% ≤ 30%
            caster.Abilities.Add(new AbilityRuntime(ab));

            var units = new List<RuntimeUnit> { caster };
            var ctx   = new SpatialStubContext(units);

            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast, "Низкое HP кастующего отменяет условие — каст идёт независимо от числа врагов");
        }

        // ===================== Мана за удар =====================

        [Test]
        public void AutoAttack_GrantsResourceOnHit()
        {
            RelicData spear = TestRelic.Make(resourceOnHit: 5f);
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: spear, range: 5f, maxResource: 30f);
            attacker.CurrentResource = 0f;
            var enemy = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            var ctx   = new SpatialStubContext(units);

            TickUntilDamage(units, ctx);

            Assert.AreEqual(5f, attacker.CurrentResource, 1e-4f, "Авто-атака даёт +5 маны");
        }

        [Test]
        public void AutoAttack_ResourceClampedToMax()
        {
            RelicData spear = TestRelic.Make(resourceOnHit: 5f);
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: spear, range: 5f, maxResource: 30f);
            attacker.CurrentResource = 28f;
            var enemy = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            var ctx   = new SpatialStubContext(units);

            TickUntilDamage(units, ctx);

            Assert.AreEqual(30f, attacker.CurrentResource, 1e-4f, "Мана клампится к MaxResource (28 + 5 → 30)");
        }

        // ===================== Хелперы =====================

        /// <summary>
        /// Тикает авто-атаку до первого нанесённого урона (сквозь двухфазный замах, вики «14»).
        /// Без визуала у мементо windup = MinWindupTicks; удар наступает через несколько тиков.
        /// </summary>
        private static void TickUntilDamage(List<RuntimeUnit> units, SpatialStubContext ctx, int maxTicks = 64)
        {
            var system = new AutoAttackSystem();
            for (int t = 0; t < maxTicks && ctx.Damage.Count == 0; t++)
                system.Tick(units, ctx, SimConstants.TickDelta);
        }

        // ===================== «Стальной вихрь» целиком (M2) =====================

        [Test]
        public void SteelWhirl_PushesEveryoneOutwardFromTheCentre()
        {
            RelicData spear = TestRelic.Make();
            var spearman = MakeUnit(0, team: 0, pos: Vector2.zero, relic: spear, aad: 10f, maxResource: 45f);
            spearman.CurrentResource = 45f;

            var east = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var west = MakeUnit(2, team: 1, pos: new Vector2(-1.5f, 0f));
            spearman.CurrentTarget = east;

            var whirl = TestAbility.Make(
                cost: 45f, mode: AbilityTargetMode.Self, damageMultiplier: 3f,
                areaShape: AreaShape.Circle, areaRadius: 2.5f,
                displaces: true, displaceDistance: 4f);
            spearman.Abilities.Add(new AbilityRuntime(whirl));

            var units = new List<RuntimeUnit> { spearman, east, west };
            var ctx   = new SpatialStubContext(units);

            new AbilitySystem().TryCast(spearman, 0, units, ctx);

            Assert.AreEqual(2, ctx.Displaces.Count, "Толчок достаётся каждому задетому, а не одной цели");

            // Направление — строго от центра вихря, у каждого своё.
            DisplaceRequest toEast = ctx.Displaces.Find(d => ReferenceEquals(d.Target, east));
            DisplaceRequest toWest = ctx.Displaces.Find(d => ReferenceEquals(d.Target, west));
            Assert.Greater(toEast.Direction.x, 0.9f, "Стоящего справа несёт вправо");
            Assert.Less(toWest.Direction.x, -0.9f, "Стоящего слева — влево");
            Assert.AreEqual(4f, toEast.Distance, 1e-4f);

            // Урон вихрь нанёс сам: «ядро» на линии полёта было бы вторым ударом той же способности.
            Assert.AreEqual(0f, toEast.Damage, 1e-4f, "Толчок вихря — без урона-ядра");
        }

        [Test]
        public void SteelWhirl_DoesNotDashTheCasterLikeTheMonk()
        {
            // Один и тот же флаг `_displaces` означает РАЗНОЕ у круга и у одиночной цели. Если круг уйдёт
            // в монашью ветку, копейщик будет рывком прыгать к врагу вместо удара вокруг себя.
            RelicData spear = TestRelic.Make();
            var spearman = MakeUnit(0, team: 0, pos: Vector2.zero, relic: spear, aad: 10f, maxResource: 45f);
            spearman.CurrentResource = 45f;
            var enemy = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            spearman.CurrentTarget = enemy;

            var whirl = TestAbility.Make(
                cost: 45f, mode: AbilityTargetMode.Self, damageMultiplier: 3f,
                areaShape: AreaShape.Circle, areaRadius: 2.5f, displaces: true);
            spearman.Abilities.Add(new AbilityRuntime(whirl));

            var units = new List<RuntimeUnit> { spearman, enemy };
            var ctx   = new SpatialStubContext(units);

            new AbilitySystem().TryCast(spearman, 0, units, ctx);

            Assert.AreEqual(1, ctx.Damage.Count, "Круг ударил врага");
            for (int i = 0; i < ctx.Displaces.Count; i++)
                Assert.AreNotSame(spearman, ctx.Displaces[i].Target, "Кастующего вихрь не двигает");
        }

        [Test]
        public void SteelWhirl_ShieldGrowsFromDamageActuallyDealt()
        {
            // Щит вихря — 20% от НАНЕСЁННОГО: считать долю от заявленного урона значило бы платить
            // щитом за урон, который срезала броня или который ушёл в уже мёртвую цель.
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);

            var spearman = TestUnit.Make();
            EffectData whirlShield = TestEffect.Make(
                baseDuration: 3f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Shield, stacking: StackRule.Refresh,
                components: new DamageToShieldComponent().With("_fraction", 0.2f));

            effects.Apply(spearman, whirlShield, spearman, ctx);
            EffectSystem.CommitPending(spearman);

            // Вихрь нанёс 300 урона суммарно (три цели по 100) — щит обязан вырасти на 60.
            for (int i = 0; i < 3; i++)
            {
                var hit = new CombatEventData(
                    CombatEvent.DamageDealt, spearman, spearman, 100f, EffectTag.None,
                    DamageSourceKind.Ability);
                effects.Dispatch(spearman, in hit, ctx);
            }

            Assert.AreEqual(60f, spearman.CurrentShield, 1e-3f, "20% от 300 нанесённого урона");
        }

        [Test]
        public void SteelWhirl_SlowFadesToZeroInsteadOfSnapping()
        {
            // Замедление 80%, «медленно проходящее за секунду до нуля» — иначе цель отпускает щелчком,
            // и рывок из вихря не читается.
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            var victim = MakeUnit(1, team: 1, pos: Vector2.zero);
            victim.Stats.AddModifiersFrom("move", new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 10f),
            });

            EffectData slow = TestEffect.Make(
                baseDuration: 1f, polarity: EffectPolarity.Debuff, 
                stacking: StackRule.Refresh,
                components: new DecayingStatModifierComponent().With("_modifiers", new[]
                {
                    new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -0.8f),
                }));

            effects.Apply(victim, slow, victim, ctx);
            EffectSystem.CommitPending(victim);

            float atStart = victim.Stats.Get(StatType.MoveSpeed);
            Assert.AreEqual(2f, atStart, 0.2f, "В первый тик замедление в полную силу: 10 → ~2");

            // Полсекунды спустя должно отпустить примерно наполовину.
            for (int i = 0; i < SimConstants.TickRate / 2; i++)
            {
                effects.Tick(new List<RuntimeUnit> { victim }, ctx, SimConstants.TickDelta);
                EffectSystem.CommitPending(victim);
            }

            float atHalf = victim.Stats.Get(StatType.MoveSpeed);
            Assert.Greater(atHalf, atStart, "Замедление отпускает постепенно, а не держится ровным");
            Assert.Less(atHalf, 10f, "Но ещё не отпустило совсем");
        }

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float aad = 10f, float range = 5f, float atkSpeed = 1f,
            float maxHp = 100f, float hp = -1f, float maxResource = 0f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, atkSpeed),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MaxResource,      ModifierOp.Flat, maxResource),
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

        /// <summary>Контекст с брутфорс-запросами по списку юнитов — для теста форм удара без spatial hash.</summary>
        private sealed class SpatialStubContext : ICombatContext
        {
            private readonly List<RuntimeUnit> _all;
            public readonly List<DamageRequest> Damage = new List<DamageRequest>();

            public SpatialStubContext(List<RuntimeUnit> all) => _all = all;

            public int AttackStarted;
            public int AttackInterrupted;

            public void DealDamage(in DamageRequest req) => Damage.Add(req);
            public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) { }
            public void SpawnProjectile(in ProjectileSpawn spawn) { }
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) { }
            // Срок, посчитанный по ходу боя, заглушке безразличен — она мерит факт наложения.
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds)
                => ApplyEffect(target, def, source);

            // Наложение с величиной (порции кровотечения): заглушке величина безразлична —
            // она мерит факт наложения.
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds,
                float potency)
                => ApplyEffect(target, def, source);
            public void ReportAreaHit(in AreaHit hit) { }
            public void Dispel(in DispelRequest req) { }
            // Слепота стабу не нужна: промах проверяют свои тесты, здесь удар всегда доходит.
            public bool ResolveAttackMiss(RuntimeUnit attacker) => false;
            public void ReportAttackMissed(RuntimeUnit attacker, RuntimeUnit target) { }
            // Каст никто не слушает: реакцию на чужое заклинание проверяют бои, а не заглушка.
            public void ReportAbilityCast(RuntimeUnit caster) { }
            public readonly List<DisplaceRequest> Displaces = new List<DisplaceRequest>();
            public void Displace(in DisplaceRequest req) => Displaces.Add(req);

            // Призывов в этом срезе нет: стаб честно отвечает «призывать нечем».
            public RuntimeUnit Summon(UnitData data, int team, Vector2 position, RuntimeUnit summoner) => null;

            // Заглушке нечего откладывать: раундов тут нет, поэтому переход отыгрывается сразу.
            public void TeleportBehind(RuntimeUnit unit, RuntimeUnit target)
                => CombatPositioning.TeleportBehind(unit, target);
            public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) => AttackStarted++;
            public void NotifyAttackInterrupted(RuntimeUnit unit) => AttackInterrupted++;
            public void NotifyAttackCompleted(RuntimeUnit unit) { }
            public void NotifyComboBroken(RuntimeUnit unit) { }
            public void RemoveEffect(RuntimeUnit unit, EffectData def) { }

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
                    if (TeamOk(filter, u.Team == requestingTeam)) results.Add(u);
                }
                return results.Count;
            }

            public int QueryUnitsInLine(Vector2 origin, Vector2 direction, float length, float width, List<RuntimeUnit> results, TargetFilter filter, int requestingTeam)
            {
                results.Clear();
                Vector2 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
                float halfW = width * 0.5f;
                for (int i = 0; i < _all.Count; i++)
                {
                    RuntimeUnit u = _all[i];
                    if (u.IsDead) continue;
                    Vector2 v = u.Position - origin;
                    float along = Vector2.Dot(v, d);
                    if (along < 0f || along > length) continue;
                    if ((v - along * d).magnitude > halfW) continue;
                    if (TeamOk(filter, u.Team == requestingTeam)) results.Add(u);
                }
                return results.Count;
            }

            private static bool TeamOk(TargetFilter f, bool isAlly) =>
                f == TargetFilter.All
                || (f == TargetFilter.Enemies && !isAlly)
                || (f == TargetFilter.Allies && isAlly);
        }
    }
}
