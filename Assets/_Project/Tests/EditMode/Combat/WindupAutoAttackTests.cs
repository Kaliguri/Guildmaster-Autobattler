using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Двухфазная авто-атака с windup (вики «14»): урон на кадре контакта, период = интервал,
    /// рефанд/потеря кулдауна, прерывание стаганом, неизменность WindupTicks при смене скорости.
    /// </summary>
    public sealed class WindupAutoAttackTests
    {
        // frameCount 7, hitFrame 5, atkSpeed 1 → interval 30, windup (5*30)/7 = 21.
        private const int FrameCount = 7;
        private const int HitFrame   = 5;

        [Test]
        public void EnterWindup_FirstTick_NoDamage_FiresAttackStarted()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);

            Assert.IsTrue(attacker.IsWindingUp, "После первого тика юнит в замахе");
            Assert.AreEqual(0, ctx.Damage.Count, "Урона на старте замаха нет");
            Assert.AreEqual(1, ctx.AttackStarted, "Событие старта замаха сработало");
            Assert.AreEqual(21, attacker.WindupTicks, "windup = (5*30)/7 = 21");
            Assert.AreEqual(21, attacker.WindupRemaining);
        }

        [Test]
        public void Damage_LandsOnWindupTicksPlusOne()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();
            int windup = AttackTiming.WindupTicks(HitFrame, FrameCount, AttackTiming.IntervalTicks(1f));

            // Ровно windup тиков — урона ещё нет.
            for (int i = 0; i < windup; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(0, ctx.Damage.Count, $"После {windup} тиков урона ещё нет");

            // Следующий тик — кадр контакта.
            sys.Tick(units, ctx, 0f);
            Assert.AreEqual(1, ctx.Damage.Count, "Урон ровно на windup+1-м тике");
            Assert.AreSame(enemy, ctx.Damage[0].Target);
            Assert.IsFalse(attacker.IsWindingUp);
        }

        [Test]
        public void CooldownPeriod_DamageToDamage_EqualsInterval()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);

            int firstHitTick = TickUntilNextDamage(sys, units, ctx, 0);
            int secondHitTick = TickUntilNextDamage(sys, units, ctx, firstHitTick);

            Assert.AreEqual(interval, secondHitTick - firstHitTick,
                "Период damage→damage = интервал (windup не добавляется)");
        }

        [Test]
        public void TargetDies_DuringWindup_NoDamage_CooldownStaysSpent()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);
            int windup   = AttackTiming.WindupTicks(HitFrame, FrameCount, interval);

            sys.Tick(units, ctx, 0f);                       // вход в замах (tick1)
            enemy.IsDead = true;                            // цель умерла в замахе
            for (int i = 0; i < windup; i++) sys.Tick(units, ctx, 0f); // досчитываем до резолва (whiff)

            Assert.AreEqual(0, ctx.Damage.Count, "Мёртвая цель к удару → вхолостую");
            Assert.AreEqual(0, ctx.AttackInterrupted, "Смерть ЦЕЛИ — не прерывание (это whiff)");
            // Кулдаун НЕ рефандится (в отличие от прерывания стаганом): тикает естественно = interval − windup.
            Assert.AreEqual(interval - windup, attacker.AttackCooldownTicks,
                "Кулдаун потрачен и тикает естественно, без мгновенного рефанда");
        }

        [Test]
        public void TargetLeavesRange_DuringWindup_NoDamage()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);                 // вход в замах (цель в радиусе)
            enemy.Position = new Vector2(999f, 0f);   // цель ушла из радиуса

            for (int i = 0; i < 40; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(0, ctx.Damage.Count, "Цель вне радиуса к удару → вхолостую");
        }

        [Test]
        public void Stun_DuringWindup_Interrupts_NoDamage_RefundsCooldown()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);     // вход в замах
            Assert.IsTrue(attacker.IsWindingUp);

            attacker.CanAct = false;      // стан в замахе
            sys.Tick(units, ctx, 0f);     // → прерывание

            Assert.IsFalse(attacker.IsWindingUp, "Замах сброшен");
            Assert.AreEqual(0, ctx.Damage.Count, "Урона нет");
            Assert.AreEqual(1, ctx.AttackInterrupted, "Событие прерывания сработало");
            Assert.AreEqual(0, attacker.AttackCooldownTicks, "Кулдаун рефандится");

            // Снят стан → бьёт снова немедленно (новый замах).
            attacker.CanAct = true;
            sys.Tick(units, ctx, 0f);
            Assert.IsTrue(attacker.IsWindingUp, "После снятия стана сразу новый замах");
            Assert.AreEqual(2, ctx.AttackStarted);
        }

        [Test]
        public void Recovery_AfterHit_LastsClipFollowThrough_ThenLeavesRecovery()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);
            int windup   = AttackTiming.WindupTicks(HitFrame, FrameCount, interval);
            int tail     = AttackTiming.FollowThroughTicks(HitFrame, FrameCount, interval, windup);
            Assert.Greater(tail, 0, "Предусловие: у юнита с клипом есть доигрыш-хвост");

            // Тикаем до кадра контакта включительно (вход в замах + windup тиков до удара).
            for (int i = 0; i <= windup; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(1, ctx.Damage.Count, "Удар нанесён");
            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase, "Сразу после удара — фаза восстановления");
            Assert.AreEqual(tail, attacker.RecoveryRemaining, "Длина хвоста = доигрыш клипа (interval − windup)");

            // Досчитываем хвост: на последнем тике Recovery истекает и фаза покидает Recovery
            // (у стрелка кулдаун обнуляется тогда же → сразу новый замах, поэтому НЕ обязательно Idle).
            for (int i = 0; i < tail; i++) sys.Tick(units, ctx, 0f);
            Assert.AreNotEqual(AttackPhase.Recovery, attacker.Phase, "Хвост истёк — юнит больше не в Recovery");
        }

        [Test]
        public void AttackSpeedChange_DuringWindup_DoesNotChangeCurrentWindupTicks()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);
            int locked = attacker.WindupTicks;

            // Резко ускоряем атаку в полёте — текущий замах не должен пересчитаться.
            attacker.Stats.AddModifiersFrom("haste", new[]
            {
                new StatModifier(StatType.AttackSpeed, ModifierOp.Flat, 10f),
            });

            for (int i = 0; i < locked; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(locked, attacker.WindupTicks, "WindupTicks зафиксирован на старте замаха");
            Assert.AreEqual(1, ctx.Damage.Count, "Удар наступил по исходному таймингу замаха");
        }

        // ===================== Погоня / кайт: гейт старта + прощающий буфер (вики «14») =====================

        [Test]
        public void FleeingTarget_AtReachEdge_DoesNotStartWindup()
        {
            // Цель у края досягаемости и убегает: рутовый замах отсюда не докрутит (уйдёт за reach+tol) →
            // юнит НЕ начинает свинг (слой 2), чтобы не бить вхолостую и не тормозить погоню занятостью.
            var (attacker, enemy, units, ctx) = FleeScene(enemyDist: null, recedePerTick: 0.15f);

            sys().Tick(units, ctx, 0f);

            Assert.IsFalse(attacker.IsWindingUp, "Убегающую цель у края reach не бьём — сначала догоняем");
            Assert.AreEqual(0, ctx.AttackStarted, "Замах не стартовал");
        }

        [Test]
        public void StationaryTarget_AtReachEdge_StartsWindup()
        {
            // Тот же край reach, но цель СТОИТ (recede = 0) → замах докрутит → стартуем как обычно.
            // Контроль-регрессия: предсказательный гейт не ломает базовый случай «цель в радиусе».
            var (attacker, enemy, units, ctx) = FleeScene(enemyDist: null, recedePerTick: 0f);

            sys().Tick(units, ctx, 0f);

            Assert.IsTrue(attacker.IsWindingUp, "Стоящую цель в радиусе бьём без задержки");
            Assert.AreEqual(1, ctx.AttackStarted);
        }

        [Test]
        public void SmallDrift_WithinTolerance_DuringWindup_StillLands()
        {
            // Слой 1: цель за замах сместилась чуть за базовый reach, но в пределах tolerance → удар засчитан.
            var (attacker, enemy, units, ctx) = Scene();
            var s = new AutoAttackSystem();
            int windup = AttackTiming.WindupTicks(HitFrame, FrameCount, AttackTiming.IntervalTicks(1f));
            float reach = CombatPositioning.AttackReachCenter(attacker, enemy, SimTuning.Default);

            s.Tick(units, ctx, 0f);                                  // вход в замах (цель в радиусе)
            enemy.Position = new Vector2(reach + 0.2f, 0f);          // сдвиг < tolerance (0.35) за край reach
            for (int i = 0; i < windup; i++) s.Tick(units, ctx, 0f); // досчитываем до кадра контакта

            Assert.AreEqual(1, ctx.Damage.Count, "Сдвиг в пределах буфера — удар проходит");
        }

        [Test]
        public void Dodge_BeyondTolerance_DuringWindup_Whiffs_AndSpendsCooldown()
        {
            // «Воу, уклонился»: цель ушла за reach+tolerance (блинк/рывок) во время замаха → свинг доиграл
            // ВПУСТУЮ (не прерван), кулдаун потрачен на старте, юнит занят весь цикл. Так и задумано.
            var (attacker, enemy, units, ctx) = Scene();
            var s = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);
            int windup   = AttackTiming.WindupTicks(HitFrame, FrameCount, interval);
            float reach  = CombatPositioning.AttackReachCenter(attacker, enemy, SimTuning.Default);

            s.Tick(units, ctx, 0f);                                  // вход в замах
            enemy.Position = new Vector2(reach + 1f, 0f);            // блинк далеко за буфер
            for (int i = 0; i < windup; i++) s.Tick(units, ctx, 0f); // до резолва

            Assert.AreEqual(0, ctx.Damage.Count, "Ушедшую за буфер цель удар не достаёт");
            Assert.AreEqual(0, ctx.AttackInterrupted, "Это whiff, а не прерывание (замах доиграл)");
            Assert.AreNotEqual(AttackPhase.Windup, attacker.Phase, "Свинг доигран (не завис в замахе)");
            Assert.AreEqual(interval - windup, attacker.AttackCooldownTicks,
                "Кулдаун потрачен на старте и тикал — уклонение стоит мили полного цикла");
        }

        [Test]
        public void CanLandWindup_TrueForStationary_FalseForFastFlee()
        {
            // Чистый предикат слоя 2 без симуляции.
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, range: 2f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f)); // в пределах reach
            int windup   = 21;

            enemy.PreviousPosition = enemy.Position; // стоит
            Assert.IsTrue(CombatPositioning.CanLandWindup(attacker, enemy, windup, SimTuning.Default),
                "Стоящую цель в радиусе замах достаёт");

            enemy.PreviousPosition = new Vector2(1.7f, 0f); // ушла на +0.3/тик = быстрый побег
            Assert.IsFalse(CombatPositioning.CanLandWindup(attacker, enemy, windup, SimTuning.Default),
                "Быстрый побег: за замах уйдёт за reach+tolerance — замах не докрутит");
        }

        // ===================== Хелперы =====================

        private static AutoAttackSystem sys() => new AutoAttackSystem();

        /// <summary>Сцена «цель у края reach, задан её уход»: attacker с клипом, enemy на дистанции ≈reach·0.98,
        /// PreviousPosition сдвинут так, что цель уходит на <paramref name="recedePerTick"/> ед./тик по оси.</summary>
        private static (RuntimeUnit attacker, RuntimeUnit enemy, List<RuntimeUnit> units, StubContext ctx)
            FleeScene(float? enemyDist, float recedePerTick)
        {
            UnitVisual visual = TestVisual.Make(FrameCount, HitFrame);
            RelicData relic = TestRelic.Make(visual: visual);

            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic, range: 2f, aad: 10f, atkSpeed: 1f);
            var enemyTmp = MakeUnit(1, team: 1, pos: Vector2.zero);
            float reach  = CombatPositioning.AttackReachCenter(attacker, enemyTmp, SimTuning.Default);
            float dist   = enemyDist ?? reach * 0.98f; // надёжно в пределах reach

            var enemy = MakeUnit(1, team: 1, pos: new Vector2(dist, 0f));
            enemy.PreviousPosition = new Vector2(dist - recedePerTick, 0f); // шаг +recede по +x = уход от attacker
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            return (attacker, enemy, units, new StubContext());
        }

        private static (RuntimeUnit attacker, RuntimeUnit enemy, List<RuntimeUnit> units, StubContext ctx) Scene()
        {
            UnitVisual visual = TestVisual.Make(FrameCount, HitFrame);
            RelicData relic = TestRelic.Make(visual: visual);

            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic, range: 5f, aad: 10f, atkSpeed: 1f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            return (attacker, enemy, units, new StubContext());
        }

        /// <summary>Тикает, пока счётчик урона не вырастет; возвращает абсолютный номер тика (от старта).</summary>
        private static int TickUntilNextDamage(AutoAttackSystem sys, List<RuntimeUnit> units, StubContext ctx, int fromTick)
        {
            int baseline = ctx.Damage.Count;
            int tick = fromTick;
            for (int guard = 0; guard < 200 && ctx.Damage.Count == baseline; guard++)
            {
                sys.Tick(units, ctx, 0f);
                tick++;
            }
            return tick;
        }

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float aad = 10f, float range = 5f, float atkSpeed = 1f, float maxHp = 100f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, atkSpeed),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
            });
            return new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats,
                CurrentHP = maxHp, Position = pos, PreviousPosition = pos, Unit = relic,
            };
        }

        /// <summary>Минимальный ICombatContext: копит урон + считает события замаха.</summary>
        private sealed class StubContext : ICombatContext
        {
            public readonly List<DamageRequest> Damage = new List<DamageRequest>();
            public int AttackStarted;
            public int AttackInterrupted;

            public void DealDamage(in DamageRequest req) => Damage.Add(req);
            public void Heal(RuntimeUnit target, float amount, RuntimeUnit source) { }
            public void SpawnProjectile(in ProjectileSpawn spawn) { }
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source) { }
            // Срок, посчитанный по ходу боя, заглушке безразличен — она мерит факт наложения.
            public void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds)
                => ApplyEffect(target, def, source);
            public void ReportAreaHit(in AreaHit hit) { }
            public void Dispel(in DispelRequest req) { }
            // Каст никто не слушает: реакцию на чужое заклинание проверяют бои, а не заглушка.
            public void ReportAbilityCast(RuntimeUnit caster) { }
            public void Displace(in DisplaceRequest req) { }

            // Призывов в этом срезе нет: стаб честно отвечает «призывать нечем».
            public RuntimeUnit Summon(UnitData data, int team, Vector2 position, RuntimeUnit summoner) => null;

            // Заглушке нечего откладывать: раундов тут нет, поэтому переход отыгрывается сразу.
            public void TeleportBehind(RuntimeUnit unit, RuntimeUnit target)
                => CombatPositioning.TeleportBehind(unit, target);
            public void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target) => AttackStarted++;
            public void NotifyAttackInterrupted(RuntimeUnit unit) => AttackInterrupted++;

            public int QueryUnitsInRadius(Vector2 c, float r, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
            public int QueryUnitsInLine(Vector2 o, Vector2 d, float l, float w, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }

            public IRngService Rng => null;
            public int CurrentTick => 0;
            public float ArmorK => 100f;
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
        }
    }
}
