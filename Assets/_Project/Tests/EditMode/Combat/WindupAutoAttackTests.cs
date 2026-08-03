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

        [Test]
        public void Stun_AfterHit_DoesNotRushNextAttack()
        {
            // Guard под будущие серии ударов (техдолг §3.9). Сегодня инвариант держится сам собой:
            // стан после удара приходит в фазу Recovery, где кулдаун НЕ рефандится, а лишь замирает.
            // Когда свинг научится нескольким контактам, прерывание после первого из них пойдёт через
            // Interrupt — а он обнуляет кулдаун. Наивная реализация тем самым позволит спамом микростанов
            // УСКОРЯТЬ жертву: каждый новый свинг начинается заново с первого контакта. Правило, которое
            // этот тест охраняет: рефанд положен только свингу, не нанёсшему НИ ОДНОГО контакта.
            var (attacker, enemy, units, ctx) = Scene();
            var s = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);

            int firstHitTick = TickUntilNextDamage(s, units, ctx, 0);

            // Микростан сразу после удара — 8 тиков ≈ 0.25 с (длительность из контроль-лупа Монаха).
            const int StunTicks = 8;
            attacker.CanAct = false;
            for (int i = 0; i < StunTicks; i++) s.Tick(units, ctx, 0f);
            attacker.CanAct = true;

            int secondHitTick = TickUntilNextDamage(s, units, ctx, firstHitTick + StunTicks);

            Assert.GreaterOrEqual(secondHitTick - firstHitTick, interval,
                "Стан после удара не может приблизить следующий удар: период damage→damage не меньше интервала");
            Assert.AreEqual(interval + StunTicks, secondHitTick - firstHitTick,
                "Стан замораживает кулдаун ровно на свою длительность — не ускоряет и не крадёт лишнего");
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

        // --- Рекаст: атака вне очереди (модель Макса 2026-07-31) ---

        [Test]
        public void Recast_DuringWindup_DoesNotShortenTheSwingAlreadyRaised()
        {
            // Занесённый удар доигрывает ЦЕЛИКОМ — это условие честного телеграфа: иначе парирование и
            // уклонение теряют окно, на которое рассчитывали.
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);
            int windupLeft = attacker.WindupRemaining;

            attacker.RecastAttack(SimTuning.Default);

            Assert.AreEqual(windupLeft, attacker.WindupRemaining, "Замах текущей атаки рекаст не трогает");
            Assert.AreEqual(21, attacker.WindupTicks, "Полная длина замаха тоже не меняется");
        }

        [Test]
        public void Recast_SpeedsUpRecoveryOfCurrentAttack_ButNeverRemovesIt()
        {
            // Главное отличие от «обрезает» (поправка Макса 2026-07-31): хвост остаётся, просто идёт вдвое
            // быстрее. Снятая фаза убрала бы окно чужого ответа целиком — ускоренная только сокращает его.
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase, "Предусловие: после удара идёт хвост");
            int fullTail = attacker.RecoveryRemaining;
            Assert.Greater(fullTail, 1, "Предусловие: хвост длиннее тика, иначе ускорять нечего");

            attacker.RecastAttack(SimTuning.Default);

            int expected = (int)System.Math.Round(fullTail / SimTuning.Default.RecastRecoverySpeed,
                System.MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, attacker.RecoveryRemaining, "Хвост ускорен ровно на общую константу");
            Assert.Greater(attacker.RecoveryRemaining, 0, "Но не снят: окно ответа сокращено, а не убрано");
        }

        [Test]
        public void Recast_DuringWindup_SpeedsUpTheRecoveryThatFollows()
        {
            // Второй путь ускорения: рекаст взведён, когда хвоста ещё нет. Скорость обязана дожить до входа
            // в него — иначе рекаст в замахе (а это его обычный случай) не ускорял бы ничего.
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Windup, attacker.Phase, "Предусловие: идёт замах");

            attacker.RecastAttack(SimTuning.Default);
            Assert.AreEqual(SimTuning.Default.RecastRecoverySpeed, attacker.SwingRecoverySpeed, 0.001f,
                "Скорость запомнена до входа в хвост");

            var (plain, _, plainUnits, plainCtx) = SlowScene();
            var plainSys = new AutoAttackSystem();
            TickUntilNextDamage(plainSys, plainUnits, plainCtx, 0);
            TickUntilNextDamage(sys, units, ctx, 0);

            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase, "После удара идёт хвост, а не пустота");
            int expected = (int)System.Math.Round(plain.RecoveryRemaining / SimTuning.Default.RecastRecoverySpeed,
                System.MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, attacker.RecoveryRemaining, "Хвост вышел вдвое короче обычного");
        }

        [Test]
        public void Recast_FasterWindup_ShortensTheSwing_DoesNotLengthenTheFollowThrough()
        {
            // Хвост меряется от контакта, поэтому наивная формула вернула бы весь выигрыш ускоренного
            // замаха обратно — доигрышем той же длины. Ускорение обязано сокращать свинг целиком.
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            attacker.RecastAttack(SimTuning.Default);
            while (attacker.Phase == AttackPhase.Recovery) sys.Tick(units, ctx, 0f);

            int recastWindup = attacker.WindupTicks;
            int recastTail   = attacker.RecoveryTicks;

            var (plain, _, plainUnits, plainCtx) = SlowScene();
            var plainSys = new AutoAttackSystem();
            plainSys.Tick(plainUnits, plainCtx, 0f);

            Assert.Less(recastWindup, plain.WindupTicks, "Замах короче — удар выходит раньше");
            Assert.AreEqual(plain.RecoveryTicks, recastTail, "А доигрыш ровно такой же, как у обычной атаки");
        }

        [Test]
        public void Recast_KeepsAtLeastOneTickOfRecovery()
        {
            // Край: короткий хвост при ускорении не должен схлопываться в ноль — фаза, у которой доигрыш
            // есть, обязана прожить хотя бы тик, иначе «ускорение» на быстром ките молча станет снятием.
            var (attacker, _, _, _) = SlowScene();

            attacker.Phase = AttackPhase.Recovery;
            attacker.RecoveryRemaining = 1;

            attacker.RecastAttack(SimTuning.Default);

            Assert.AreEqual(1, attacker.RecoveryRemaining, "Хвост в один тик ускорять некуда — он остаётся");
        }

        [Test]
        public void Recast_SkipsTheIntervalQueue()
        {
            // Без снятия очереди обрезанный хвост лишь удлиняет боевое ожидание — рекаст не даёт ничего.
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            Assert.Greater(attacker.AttackCooldownTicks, 0, "Предусловие: интервал ещё не вышел");

            attacker.RecastAttack(SimTuning.Default);

            Assert.AreEqual(0, attacker.AttackCooldownTicks, "Ожидание интервала снято");
        }

        [Test]
        public void Recast_ShortensWindupOfTheNextAttackOnly()
        {
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            attacker.RecastAttack(SimTuning.Default);

            // Хвост ускорен и очередь пропущена → дотикав его, юнит сразу входит в укороченный замах.
            while (attacker.Phase == AttackPhase.Recovery) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Windup, attacker.Phase, "Новая атака вышла вне очереди");

            var (plain, _, plainUnits, plainCtx) = SlowScene();
            var plainSys = new AutoAttackSystem();
            plainSys.Tick(plainUnits, plainCtx, 0f);

            Assert.Less(attacker.WindupTicks, plain.WindupTicks,
                "Замах атаки, вышедшей по рекасту, короче обычного");
        }

        [Test]
        public void RecoveryCut_IsSpentByOneSwing()
        {
            // Скорость хвоста принадлежит одному свингу: следующая атака доигрывает в обычном темпе,
            // иначе рекаст незаметно стал бы постоянным режимом кита.
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            int fullTail = attacker.RecoveryRemaining;
            attacker.RecastAttack(SimTuning.Default);
            Assert.AreEqual(1f, attacker.SwingRecoverySpeed, 0.001f,
                "Скорость уже потрачена: хвост этого свинга ускорен на месте");

            TickUntilNextDamage(sys, units, ctx, 0);   // следующая атака
            Assert.AreEqual(fullTail, attacker.RecoveryRemaining, "У новой атаки хвост обычный");
        }

        // --- Атака из нескольких Ударов (2026-07-30/6) ---

        [Test]
        public void TwoMarkers_MakeTwoHits_InOneAttack()
        {
            var (attacker, enemy, units, ctx) = DoubleHitScene();
            var sys = new AutoAttackSystem();

            // Один свинг целиком: до конца хвоста.
            for (int i = 0; i < 40 && attacker.Phase != AttackPhase.Recovery; i++) sys.Tick(units, ctx, 0f);
            sys.Tick(units, ctx, 0f);

            Assert.AreEqual(2, ctx.Damage.Count, "Два маркера в клипе — два Удара в одной Атаке");
            Assert.AreEqual(2, attacker.SwingHitIndex, "Оба контакта свинга разрешены");
        }

        [Test]
        public void HitShares_ScaleEachHitSeparately()
        {
            // Монах: два Удара по половине силы. Доля задаётся КАЖДОМУ лично (вердикт Макса 2026-07-31),
            // поэтому суммарный урон Атаки равен обычному, а не удвоенному.
            var (attacker, enemy, units, ctx) = DoubleHitScene(new[] { 0.5f, 0.5f });
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 40 && ctx.Damage.Count < 2; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(2, ctx.Damage.Count);
            Assert.AreEqual(5f, ctx.Damage[0].RawDamage, 0.001f, "Первый Удар — половина силы");
            Assert.AreEqual(5f, ctx.Damage[1].RawDamage, 0.001f, "Второй Удар — половина силы");
        }

        [Test]
        public void SeriesWithoutShares_HitsAtFullStrengthTwice()
        {
            // Дефолт — полная сила КАЖДОМУ Удару, а не «поровну»: движок не делит урон сам, силу серии
            // объявляет автор кита.
            var (attacker, enemy, units, ctx) = DoubleHitScene();
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 40 && ctx.Damage.Count < 2; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(10f, ctx.Damage[0].RawDamage, 0.001f);
            Assert.AreEqual(10f, ctx.Damage[1].RawDamage, 0.001f);
        }

        [Test]
        public void SeriesContacts_AreAtLeastOneTickApart()
        {
            var (attacker, enemy, units, ctx) = DoubleHitScene();
            var sys = new AutoAttackSystem();
            sys.Tick(units, ctx, 0f);   // вход в замах считает контакты

            Assert.AreEqual(2, attacker.SwingContacts.Count);
            Assert.Greater(attacker.SwingContacts[1], attacker.SwingContacts[0],
                "Удары не сливаются в один тик — иначе второго не видно ни показу, ни игроку");
        }

        [Test]
        public void Series_FirstContactPeriod_EqualsInterval()
        {
            // Якорь интервала — ПЕРВЫЙ контакт свинга: сколько бы Ударов ни было внутри, темп Атак не
            // меняется.
            var (attacker, enemy, units, ctx) = DoubleHitScene();
            var sys = new AutoAttackSystem();
            int interval = AttackTiming.IntervalTicks(1f);

            int firstOfFirstAttack = TickUntilNextDamage(sys, units, ctx, 0);
            int secondHit = TickUntilNextDamage(sys, units, ctx, firstOfFirstAttack);      // второй Удар той же Атаки
            int firstOfSecondAttack = TickUntilNextDamage(sys, units, ctx, secondHit);     // первый Удар следующей

            Assert.AreEqual(interval, firstOfSecondAttack - firstOfFirstAttack,
                "Период «первый контакт → первый контакт следующей Атаки» равен интервалу");
        }

        [Test]
        public void StunAfterFirstHitOfSeries_DoesNotRefundCooldown()
        {
            // Инвариант техдолга §3.9: рефанд положен только пустому свингу. Иначе спам микростанов
            // ускорял бы жертву — каждый новый свинг начинался бы заново с первого контакта.
            var (attacker, enemy, units, ctx) = DoubleHitScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);   // первый Удар серии прошёл
            Assert.AreEqual(1, attacker.SwingHitIndex);

            int cooldownBefore = attacker.AttackCooldownTicks;
            attacker.CanAct = false;
            sys.Tick(units, ctx, 0f);                  // контроль рвёт остаток серии

            Assert.Greater(attacker.AttackCooldownTicks, 0,
                "Свинг, нанёсший контакт, рефанда не получает");
            Assert.AreEqual(cooldownBefore, attacker.AttackCooldownTicks,
                "Кулдаун замирает на время стана, а не обнуляется");
        }

        // --- Четвёртое состояние цикла: боевое ожидание (решение Макса 2026-07-30/10) ---

        [Test]
        public void BetweenSwings_WithTargetInReach_IsCombatIdle()
        {
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            TickUntilRest(sys, units, ctx, attacker);

            Assert.AreEqual(AttackPhase.CombatIdle, attacker.Phase,
                "Удар отработан, цель под рукой — юнит ЖДЁТ своего окна, а не бездельничает вне боя");
            Assert.IsFalse(attacker.IsSwinging, "Боевое ожидание — это не идущий удар");
        }

        [Test]
        public void FastKit_HasNoWaitingWindow_AtAll()
        {
            // Обратная сторона того же правила (два режима темпа, 2026-07-30/15): у кита, чья анимация
            // занимает ВЕСЬ интервал, ждать нечего — хвост кончается ровно тогда, когда обнуляется
            // кулдаун, и следующий замах начинается тем же тиком. Боевого ожидания у него не существует,
            // и это не дефект, а «непрекращающийся град».
            var (attacker, enemy, units, ctx) = Scene();   // windup 21 + хвост 9 = интервал 30
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            TickUntilRest(sys, units, ctx, attacker);

            Assert.AreEqual(AttackPhase.Windup, attacker.Phase,
                "Свинг занимает весь интервал — юнит уходит из хвоста прямо в новый замах");
        }

        [Test]
        public void BetweenSwings_WithoutTarget_IsIdle()
        {
            var (attacker, enemy, units, ctx) = SlowScene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            attacker.CurrentTarget = null;
            TickUntilRest(sys, units, ctx, attacker);

            Assert.AreEqual(AttackPhase.Idle, attacker.Phase, "Без цели юнит вне боя");
        }

        [Test]
        public void TargetOutOfReach_IsIdle_NotCombatIdle()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            // Цель выбрана, но до неё бежать: в цикл атаки юнит ещё не вошёл (уточнение Макса — граница
            // проходит по досягаемости, а не по наличию цели).
            enemy.Position = enemy.PreviousPosition = new Vector2(40f, 0f);
            sys.Tick(units, ctx, 0f);

            Assert.AreEqual(AttackPhase.Idle, attacker.Phase, "Бегущий к цели — вне боя");
        }

        [Test]
        public void Stunned_WithTargetInReach_IsIdle()
        {
            var (attacker, enemy, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            TickUntilNextDamage(sys, units, ctx, 0);
            attacker.CanAct = false;
            sys.Tick(units, ctx, 0f);

            Assert.AreEqual(AttackPhase.Idle, attacker.Phase,
                "Оглушённый выпал из цикла атаки — боевого ожидания без дееспособности не бывает");
        }

        /// <summary>
        /// Сцена с ДВУХУДАРНЫМ бойцом: в клипе два маркера (кадры 3 и 5 из 7), то есть его Атака состоит
        /// из двух Ударов. Доли урона задаются отдельно — пусто означает полную силу каждому.
        /// </summary>
        private static (RuntimeUnit attacker, RuntimeUnit enemy, List<RuntimeUnit> units, StubContext ctx)
            DoubleHitScene(float[] hitShares = null)
        {
            UnitVisual visual = TestVisual.Make(FrameCount, 3, HitFrame);
            RelicData relic = TestRelic.Make(visual: visual, hitDamageShares: hitShares);

            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic, range: 5f, aad: 10f, atkSpeed: 1f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), maxHp: 10000f);
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            return (attacker, enemy, units, new StubContext());
        }

        /// <summary>
        /// Сцена с МЕДЛЕННЫМ бойцом: интервал 60 тиков при свинге в 30, то есть между ударами остаётся
        /// настоящее окно ожидания. На быстром ките (<see cref="Scene"/>) его нет по построению —
        /// анимация занимает весь интервал.
        /// </summary>
        private static (RuntimeUnit attacker, RuntimeUnit enemy, List<RuntimeUnit> units, StubContext ctx) SlowScene()
        {
            UnitVisual visual = TestVisual.Make(FrameCount, HitFrame);
            RelicData relic = TestRelic.Make(visual: visual);

            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic, range: 5f, aad: 10f, atkSpeed: 0.5f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            return (attacker, enemy, units, new StubContext());
        }

        /// <summary>Тикает, пока юнит не выйдет из удара (замах/канал/хвост) в фазу покоя.</summary>
        private static void TickUntilRest(AutoAttackSystem sys, List<RuntimeUnit> units, StubContext ctx,
            RuntimeUnit unit)
        {
            for (int guard = 0; guard < 200 && unit.IsSwinging; guard++) sys.Tick(units, ctx, 0f);
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
            var u = new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats,
                CurrentHP = maxHp, Position = pos, PreviousPosition = pos,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
            u.AdoptKit(relic);   // доставка и on-hit — из снимка кита, как в фабрике
            return u;
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
            public void Displace(in DisplaceRequest req) { }

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

            public int QueryUnitsInRadius(Vector2 c, float r, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }
            public int QueryUnitsInLine(Vector2 o, Vector2 d, float l, float w, List<RuntimeUnit> res, TargetFilter f, int team) { res.Clear(); return 0; }

            public IRngService Rng => null;
            public int CurrentTick => 0;
            public float ArmorK => 100f;
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
        }
    }
}
