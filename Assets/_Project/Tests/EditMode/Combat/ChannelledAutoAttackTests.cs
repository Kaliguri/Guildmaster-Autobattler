using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Канальная авто-атака (<see cref="AttackPhase.Channel"/>, решение Макса 2026-07-30): удар растянут в
    /// поток тиков между длинным замахом и длинным хвостом. Носитель — Десятина (кровавый поток).
    /// <para><b>Главный инвариант здесь — тот, который нельзя выразить комментарием:</b> канал не является
    /// способом обойти классовый коридор DPS. Тик канала — обычный удар (<c>AutoAttackDamage</c> с
    /// интервалом <c>1/AttackSpeed</c>), поэтому за время потока кит выдаёт ровно свою норму, а замах и
    /// хвост делают его СРЕДНИЙ DPS ниже нормы. Разъедься это — и канальный кит станет тихо сильнее всех
    /// остальных при тех же статах, чего ни один тест по отдельному киту не заметит.</para>
    /// </summary>
    public sealed class ChannelledAutoAttackTests
    {
        private const float Aad      = 12f;  // урон одного тика = урон обычного удара
        private const float AtkSpeed = 2f;   // интервал 15 тиков (0.5 сек) = расстояние между тиками
        private const float Duration = 2f;   // 60 тиков потока
        private const float Windup   = 1f;   // 30 тиков заноса — длиннее интервала атаки, и это законно

        [Test]
        public void ChannelWindup_IsNotClampedByAttackInterval()
        {
            var (attacker, _, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            sys.Tick(units, ctx, 0f);

            int interval = AttackTiming.IntervalTicks(AtkSpeed);
            Assert.AreEqual(AttackPhase.Windup, attacker.Phase, "Канал начинается с обычного замаха");
            Assert.AreEqual(30, attacker.WindupTicks, "Занос задан секундами канала: 1 сек = 30 тиков");
            Assert.Greater(attacker.WindupTicks, interval,
                "Занос канала ДОЛЖЕН быть длиннее интервала атаки — обычный кламп 'интервал − 1' его бы обрезал");
        }

        [Test]
        public void Rhythm_WindupThenChannelThenRecovery()
        {
            var (attacker, _, units, ctx) = Scene(recoverySeconds: 1f);
            var sys = new AutoAttackSystem();

            // Замах: ни одного тика урона.
            for (int i = 0; i < 30; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(0, ctx.DamageCalls.Count, "За весь занос канал не бьёт ни разу");

            // Первый тик урона совпадает с открытием потока.
            sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Channel, attacker.Phase, "Замах кончился — поток пошёл");
            Assert.AreEqual(1, ctx.DamageCalls.Count, "Первый тик урона приходит вместе с открытием канала");

            // Поток идёт свои 2 сек, потом хвост — и только за ним новый занос.
            for (int i = 0; i < 60; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase, "Время канала вышло — сворачивание потока");

            int damageAfterChannel = ctx.DamageCalls.Count;
            for (int i = 0; i < 29; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(damageAfterChannel, ctx.DamageCalls.Count, "В хвосте канал не бьёт");
        }

        [Test]
        public void ChannelDps_EqualsPlainAttackDps()
        {
            var (attacker, _, units, ctx) = Scene();
            var sys = new AutoAttackSystem();

            // Весь занос + весь поток.
            for (int i = 0; i < 30 + 60; i++) sys.Tick(units, ctx, 0f);

            // За Duration секунд при частоте AtkSpeed тиков/сек ожидаем Duration × AtkSpeed попаданий:
            // ровно столько ударов сделал бы за это же время обычный боец с теми же статами.
            int expectedTicks = (int)(Duration * AtkSpeed);
            Assert.AreEqual(expectedTicks, ctx.DamageCalls.Count,
                "Тиков урона за канал = длительность × скорость атаки, то есть DPS канала равен норме кита");
            Assert.AreEqual(Aad * expectedTicks, ctx.TotalRawDamage, 0.01f,
                "Урон тика = урон обычного удара, без скрытого множителя за канальность");
        }

        [Test]
        public void ChannelTick_HitsInstantly_EvenForRangedKit()
        {
            // Дальнобойный кит: обычная его атака ушла бы снарядом со временем полёта.
            var (attacker, _, units, ctx) = Scene(attackType: AttackType.Ranged);
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 31; i++) sys.Tick(units, ctx, 0f);

            Assert.AreEqual(AttackPhase.Channel, attacker.Phase);
            Assert.AreEqual(0, ctx.Projectiles.Count, "Поток не стреляет снарядами — урон мгновенный");
            Assert.AreEqual(1, ctx.DamageCalls.Count, "Урон нанесён прямо в тике канала");
        }

        [Test]
        public void ControlBreaksChannel_IntoRecovery_WithoutRefund()
        {
            var (attacker, _, units, ctx) = Scene(recoverySeconds: 1f);
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 31; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Channel, attacker.Phase, "Предусловие: поток идёт");
            int damageBeforeStun = ctx.DamageCalls.Count;

            attacker.CanAct = false; // оглушение посреди потока (то, что кладёт контроль)
            sys.Tick(units, ctx, 0f);

            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase,
                "Сорванный канал доигрывает хвост: сворачивание потока отрабатывается всегда");
            Assert.AreEqual(0, ctx.AttackInterrupted,
                "Это не прерванный замах — часть урона уже нанесена, событие срыва не поднимается");
            Assert.AreEqual(damageBeforeStun, ctx.DamageCalls.Count, "Оглушённый не бьёт");
            Assert.IsNull(attacker.AttackChannelTarget, "Цель потока отпущена");
            Assert.AreEqual(0, attacker.AttackChannelRemaining);
        }

        [Test]
        public void TargetLeavingReach_BreaksChannel()
        {
            // С заданным хвостом: он и показывает, что поток именно СВЁРНУТ, а не просто выключен.
            var (attacker, enemy, units, ctx) = Scene(recoverySeconds: 1f);
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 31; i++) sys.Tick(units, ctx, 0f);
            Assert.AreEqual(AttackPhase.Channel, attacker.Phase, "Предусловие: поток идёт");
            int damageBeforeEscape = ctx.DamageCalls.Count;

            enemy.Position = new Vector2(50f, 0f); // вышел из радиуса
            sys.Tick(units, ctx, 0f);

            Assert.AreEqual(AttackPhase.Recovery, attacker.Phase, "Поток без цели гаснет и доигрывает хвост");
            Assert.AreEqual(damageBeforeEscape, ctx.DamageCalls.Count, "Убежавшего поток больше не задевает");
            Assert.IsNull(attacker.AttackChannelTarget);
        }

        [Test]
        public void PlainKit_HasNoChannelPhase()
        {
            // Регресс: кит без канала ведёт себя ровно как раньше — фазы Channel в его ритме нет вовсе.
            var (attacker, _, units, ctx) = Scene(channelled: false);
            var sys = new AutoAttackSystem();

            for (int i = 0; i < 120; i++)
            {
                sys.Tick(units, ctx, 0f);
                Assert.AreNotEqual(AttackPhase.Channel, attacker.Phase,
                    "Обычная атака в канал не превращается");
            }

            Assert.Greater(ctx.DamageCalls.Count, 0, "Предусловие: обычный кит всё же бил");
        }

        private static (RuntimeUnit attacker, RuntimeUnit enemy, List<RuntimeUnit> units, MockCombatContext ctx)
            Scene(bool channelled = true, AttackType attackType = AttackType.Melee, float recoverySeconds = 0f)
        {
            // Хвост канала живёт в самом профиле канала, а не в общем AttackRecoverySeconds кита.
            AttackChannel channel = channelled
                ? new AttackChannel
                {
                    DurationSeconds = Duration, WindupSeconds = Windup, RecoverySeconds = recoverySeconds,
                }
                : AttackChannel.None;

            RelicData relic = TestRelic.Make(
                attackType: attackType, channel: channel, attackRecoverySeconds: recoverySeconds);

            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), relic: null);
            attacker.CurrentTarget = enemy;

            var units = new List<RuntimeUnit> { attacker, enemy };
            return (attacker, enemy, units, new MockCombatContext());
        }

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, RelicData relic)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 1000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, Aad),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, AtkSpeed),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 8f),
            });

            var u = new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats,
                CurrentHP = 1000f, Position = pos, PreviousPosition = pos,
                AutoAttackDamageType = DamageType.Bleed,
            };
            // Форму (доставку, канал, on-hit) снимаем с кита тем же вызовом, что фабрика.
            u.AdoptKit(relic);
            if (relic == null) u.AutoAttackDamageType = DamageType.Bleed;
            return u;
        }
    }
}
