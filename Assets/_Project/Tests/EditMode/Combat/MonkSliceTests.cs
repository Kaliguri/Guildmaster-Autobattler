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
    /// Вертикальный срез «Монах вихря» (вики «13» §10.6): «Шквальный толчок» (§9.9 — отбрасывание
    /// на фикс. дистанцию, оглушение полёта, урон-«ядро» по врагам источника на линии) и «Вихревой
    /// заход» (§9.6 — телепорт к цели + усиление в конце полёта). Спайк детерминизма перемещений (S5)
    /// влит two-run проверкой.
    /// </summary>
    public sealed class MonkSliceTests
    {

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
                    distance: 4f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 1f));
                // Длительность полёта считается из дистанции: 4 ед. при 10 ед/с = 12 тиков (30 Гц).
                for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta);
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
            // 6 ед. при 10 ед/с = 18 тиков полёта.
            sim.Displace(new DisplaceRequest(target, MakeUnit(0, 0, Vector2.zero), new Vector2(1f, 0f),
                distance: 6f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 1f));

            for (int t = 0; t < 9; t++) sim.Tick(SimConstants.TickDelta);
            Assert.Greater(target.DisplacedTicksRemaining, 0, "В полёте цель оглушена (жёсткое состояние)");

            for (int t = 0; t < 9; t++) sim.Tick(SimConstants.TickDelta);
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
                distance: 10f, cannonball: true, damage: 50f, damageType: DamageType.Slash, width: 2f));
            for (int t = 0; t < 31; t++) sim.Tick(SimConstants.TickDelta); // 10 ед. = 30 тиков полёта

            Assert.Less(enemyB.CurrentHP, 200f, "«Ядро» бьёт врага источника на линии");
            Assert.AreEqual(200f, allyC.CurrentHP, 1e-4f, "Союзника источника «ядро» не задевает");
            // Отброшенный получает урон толчка ОДИН раз, при старте полёта (решение 2026-07-28): раньше
            // заданный урон уходил только задетым на линии, то есть толчок бил мимо того, кого толкнули.
            // Через себя «ядром» он при этом не проходит — иначе урон удваивался бы на каждом тике.
            float flyingLost = flying.Stats.Get(StatType.MaxHP) - flying.CurrentHP;
            Assert.AreEqual(50f, flyingLost, 1e-4f, "Летящий получил урон толчка ровно один раз");
        }

        [Test]
        public void FlightDuration_ScalesWithDistance()
        {
            // Скорость полёта фиксирована, поэтому длительность — производная дистанции (решение
            // 2026-07-28): вдвое дальше = вдвое дольше в контроль-иммунном оглушении.
            int TicksToLand(float distance)
            {
                var sim = BuildSim(7UL);
                var target = MakeUnit(1, team: 1, pos: Vector2.zero, moveSpeed: 0f);
                sim.Displace(new DisplaceRequest(target, MakeUnit(0, 0, Vector2.zero), new Vector2(1f, 0f),
                    distance: distance, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 0f));

                int ticks = 0;
                while (target.DisplacedTicksRemaining > 0 && ticks < 300)
                {
                    sim.Tick(SimConstants.TickDelta);
                    ticks++;
                }
                return ticks;
            }

            int near = TicksToLand(3f);
            int far  = TicksToLand(6f);

            Assert.Greater(near, 0, "Полёт длится хотя бы тик — иначе не поднимется событие конца смещения");
            Assert.AreEqual(near * 2, far, "Двойная дистанция — двойная длительность полёта");
        }

        [Test]
        public void IntoWall_DealsImpactDamage_OncePerFlight()
        {
            // Арена узкая по X: цель у самого края, толчок вжимает её в стену на первом же тике.
            var sim = new CombatSimulation(
                new XorShiftRng(5UL), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem(),
                arena: new ArenaBounds(Vector2.zero, new Vector2(4f, 20f)));

            var monk   = MakeUnit(0, team: 0, pos: new Vector2(-2f, 0f), aad: 0f, moveSpeed: 0f);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1.9f, 0f), maxHp: 500f, aad: 0f, moveSpeed: 0f);
            sim.EnqueueUnitSpawn(monk);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            float hpBefore = victim.CurrentHP;
            sim.Displace(new DisplaceRequest(victim, monk, new Vector2(1f, 0f),
                distance: 6f, cannonball: true, damage: 40f, damageType: DamageType.Slash, width: 1f));

            for (int t = 0; t < 5; t++) sim.Tick(SimConstants.TickDelta);

            // 40 за сам толчок (на старте полёта) + 40 за удар о край арены = ровно 80.
            float lost = hpBefore - victim.CurrentHP;
            Assert.AreEqual(80f, lost, 1e-3f, "Толчок + удар о стену: по одному разу каждый, а не урон на каждый прижатый тик");

            // Полёт СТОИТ у стены, но цель продолжает лежать оглушённой ~1 секунду (30 тиков).
            Vector2 restingPos = victim.Position;
            for (int t = 0; t < 15; t++) sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(restingPos, victim.Position, "У стены полёт остановлен — цель не скользит вдоль границы");
            Assert.Greater(victim.DisplacedTicksRemaining, 0, "Через полсекунды после удара цель ещё лежит");

            for (int t = 0; t < 20; t++) sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(0, victim.DisplacedTicksRemaining, "Лежание кончилось — только теперь поднимается конец смещения (телепорт Монаха)");
            Assert.AreEqual(80f, hpBefore - victim.CurrentHP, 1e-3f, "Лежание урона не добавляет");
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
                distance: 4f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 1f));

            for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta); // конец полёта (4 ед. = 12 тиков) → сигнал → телепорт+усиление тем же тиком

            Assert.AreEqual(2f, monk.EmpowerDamageMult, 1e-4f, "В конце полёта «Вихревой заход» взвёл усиление ×2");
            Assert.AreSame(victim, monk.CurrentTarget, "Монах перенацелился на смещённую цель");
            float distToVictim = (monk.Position - victim.Position).magnitude;
            Assert.LessOrEqual(distToVictim, monk.Stats.Get(StatType.AttackRange) + 1e-3f, "Телепорт поставил монаха в пределах досягаемости");
        }

        [Test]
        public void VortexEntry_LoopsControl_StunsBeforeTheBackstab_AndCutsTheQueue()
        {
            // M11 (решение Макса 2026-07-28): удар в спину выходит ВНЕ ОЧЕРЕДИ атак, ускоренным замахом,
            // а цель к моменту удара уже зафиксирована микро-станом. Порядок и есть механика: стан
            // ложится ДО удара, иначе монах бьёт в убегающую спину и комбо не замыкается.
            var sim = BuildSim(1UL);
            var monk   = MakeUnit(0, team: 0, pos: new Vector2(-3f, 0f), range: 2f);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));

            // Монах «в хвосте» после предыдущего удара и с полным кулдауном — комбо обязано это перебить.
            monk.AttackCooldownTicks = 25;
            monk.Phase = AttackPhase.Recovery;
            monk.RecoveryRemaining = 10;

            sim.ApplyEffect(monk, VortexPassive(2f, MicroStun(0.25f), windupMult: 0.5f), monk);
            sim.Displace(new DisplaceRequest(victim, monk, new Vector2(1f, 0f),
                distance: 4f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 1f));

            for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta);   // конец полёта → заход сработал

            // Ещё пара тиков: заход накладывает стан в фазе доставки событий, которая идёт ПОСЛЕ тика
            // эффектов, а флаги контроля пересчитываются на своём проходе. Именно этот зазор и делает
            // стан «ложащимся ДО удара»: укороченный замах дозревает позже.
            sim.Tick(SimConstants.TickDelta);
            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(0, monk.AttackCooldownTicks, "Удар вне очереди: таймер атаки обнулён");
            Assert.AreEqual(AttackPhase.Idle, monk.Phase, "Хвост предыдущего удара перебит");
            Assert.AreEqual(0.5f, monk.NextWindupMult, 1e-4f, "Замах удара в спину ускорен вдвое");
            // Проверяем ФАКТ наложения, а не производный флаг: контракт этого компонента — «повесить
            // микро-стан на цель», а превращение эффекта в CanAct принадлежит EffectSystem, и у неё
            // свои тесты. Иначе тест захода начнёт падать от чужих правок порядка в тике.
            bool stunned = false;
            for (int i = 0; i < victim.ActiveEffects.Count; i++)
                if ((victim.ActiveEffects[i].Def.Tags & EffectTag.Control) != 0) stunned = true;

            Assert.IsTrue(stunned, "Микро-стан наложен на цель до того, как дозреет удар в спину");
        }

        [Test]
        public void VortexEntry_WithoutStunAsset_StillWorks_ButDoesNotFixTheTarget()
        {
            // Честная деградация: микро-стан — поле ассета, и без него комбо остаётся ударом вне очереди,
            // а не ломается. Так видно, что фиксация цели — решение дизайна, а не побочный эффект кода.
            var sim = BuildSim(1UL);
            var monk   = MakeUnit(0, team: 0, pos: new Vector2(-3f, 0f), range: 2f);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));

            sim.ApplyEffect(monk, VortexPassive(2f), monk);
            sim.Displace(new DisplaceRequest(victim, monk, new Vector2(1f, 0f),
                distance: 4f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 1f));

            for (int t = 0; t < 12; t++) sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(2f, monk.EmpowerDamageMult, 1e-4f, "Усиление взведено как раньше");
            bool anyControl = false;
            for (int i = 0; i < victim.ActiveEffects.Count; i++)
                if ((victim.ActiveEffects[i].Def.Tags & EffectTag.Control) != 0) anyControl = true;

            Assert.IsFalse(anyControl, "Без ассета стана цель не фиксируется");
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

            sim.ApplyEffect(monk, DashLandingPassive(distance: 4f, dmgMult: 1.5f), monk);
            sim.ApplyEffect(monk, VortexPassive(2f), monk);

            float enemyStartX     = enemy.Position.x;
            float bystanderStartX = bystander.Position.x;

            // Рывок монаха к цели (self-displacement) — как это делает активка «Шквальный толчок».
            sim.Displace(new DisplaceRequest(monk, monk, new Vector2(1f, 0f),
                distance: 1f, cannonball: false, damage: 0f, damageType: DamageType.Slash, width: 0f));

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

        private static EffectData VortexPassive(float mult, EffectData microStun = null, float windupMult = 0f)
        {
            var vortex = new VortexEntryComponent()
                .With("_empowerMult", mult)
                .With("_microStun", microStun)
                .With("_windupMult", windupMult);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: vortex);
        }

        /// <summary>Микро-стан захода: полный вывод из строя на заданные секунды (как VortexMicroStun в ассетах).</summary>
        private static EffectData MicroStun(float seconds) =>
            EffectData.CreateRuntime(
                "test.vortex_micro_stun", EffectPolarity.Debuff, EffectTag.Debuff | EffectTag.Control,
                seconds, unremovable: false,
                new ControlComponent(preventAct: true, preventMove: true, preventCast: true));

        private static EffectData DashLandingPassive(float distance, float dmgMult)
        {
            var landing = new WhirlDashLandingComponent()
                .With("_displaceDistance", distance)
                .With("_displaceDamageMult", dmgMult)
                .With("_displaceWidth", 1.5f);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: landing);
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
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
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }
    }
}
