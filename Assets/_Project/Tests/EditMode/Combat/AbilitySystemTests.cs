using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Способности: кулдаун (× CooldownEff), трата ресурса, наложение эффектов на цель,
    /// плейсхолдер-автокаст (вики «12» §6, §8).
    /// </summary>
    public sealed class AbilitySystemTests
    {
        private static RuntimeUnit WithAbility(RuntimeUnit u, AbilityData data)
        {
            u.Abilities.Add(new AbilityRuntime(data));
            return u;
        }

        // Урон способности считается от AutoAttackDamage кастующего: без него множитель ×2 даёт ноль,
        // и «нагрузка пришла» проверить нечем.
        private static RuntimeUnit WithAttackDamage(RuntimeUnit u, float damage = 100f)
        {
            u.Stats.AddModifiersFrom("attack", new[]
            {
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
            });
            return u;
        }

        [Test]
        public void Cast_ConsumesResource_AndSetsCooldown()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 30f, mode: AbilityTargetMode.Self));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.IsTrue(cast);
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f);
            Assert.AreEqual(4f, caster.Abilities[0].CooldownRemaining, 1e-4f);
        }

        [Test]
        public void Cast_FailsWhenInsufficientResource()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 20f;
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 30f, mode: AbilityTargetMode.Self));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.IsFalse(cast);
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f);
            Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "Кулдаун не ставится при провале");
        }

        [Test]
        public void Cooldown_ScaledBy_CooldownEff()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.Stats.AddModifiersFrom("cdr", new[] { new StatModifier(StatType.CooldownEff, ModifierOp.Flat, -0.5f) }); // 1.0 → 0.5
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 0f, mode: AbilityTargetMode.Self));

            sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.AreEqual(2f, caster.Abilities[0].CooldownRemaining, 1e-4f, "4с × 0.5 CooldownEff = 2с");
        }

        [Test]
        public void Cast_AppliesEffectsToTarget()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects); // ApplyEffect делегирует в EffectSystem
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, mode: AbilityTargetMode.Self));

            sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);
            EffectSystem.CommitPending(caster);   // закон видимости: стат проявляется в конце тика

            Assert.AreEqual(5f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f, "Эффект способности наложен на цель");
        }

        [Test]
        public void AutoCast_FiresReadyAbility_OnTick()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, cooldown: 5f, mode: AbilityTargetMode.Self));

            sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);
            EffectSystem.CommitPending(caster);   // закон видимости: стат проявляется в конце тика

            Assert.AreEqual(5f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f);
            Assert.Greater(caster.Abilities[0].CooldownRemaining, 0f, "После автокаста способность на кулдауне");
        }

        [Test]
        public void AutoCast_Blocked_WhenSilenced()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            caster.CanCast = false; // немота
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, mode: AbilityTargetMode.Self));

            sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(0f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f, "Под немотой не кастуем");
        }

        [Test]
        public void Cooldown_TicksDown_OverTime()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 0f;
            // Стоимость выше ресурса → автокаст не сработает, изолируем убывание кулдауна.
            WithAbility(caster, TestAbility.Make(cost: 100f, mode: AbilityTargetMode.Self));
            caster.Abilities[0].CooldownRemaining = 1f;

            for (int i = 0; i < SimConstants.TickRate; i++)
                sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.LessOrEqual(caster.Abilities[0].CooldownRemaining, 0f);
        }

        // ===================== Каст-тайм и каналы (M3) =====================

        [Test]
        public void CastTime_PaysUpFront_AndDelaysThePayload()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget  = enemy;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cooldown: 5f, cost: 30f, mode: AbilityTargetMode.NearestEnemy,
                damageMultiplier: 2f, castSeconds: 0.5f));

            var units = new List<RuntimeUnit> { caster, enemy };
            sys.Tick(units, ctx, SimConstants.TickDelta);

            // Цена уплачена на СТАРТЕ (решение Макса): прерывание контролем жжёт каст, а не откладывает.
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f, "Ресурс списан в начале подготовки");
            Assert.AreEqual(5f, caster.Abilities[0].CooldownRemaining, 1e-4f, "И кулдаун тоже");
            Assert.IsTrue(caster.IsCasting, "Идёт подготовка");
            Assert.AreEqual(0, ctx.DamageCalls.Count, "Но урона ещё нет");

            // 0.5 с = 15 тиков; на старте один тик уже прошёл.
            for (int i = 0; i < 15; i++) sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Нагрузка пришла ровно по окончании подготовки");
            Assert.IsFalse(caster.IsCastBusy, "И каст закончился");
        }

        [Test]
        public void CastTime_BrokenByHardControl_LosesTheCast()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget   = enemy;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 2f, castSeconds: 1f));

            var units = new List<RuntimeUnit> { caster, enemy };
            sys.Tick(units, ctx, SimConstants.TickDelta);
            Assert.IsTrue(caster.IsCasting);

            // Оглушение = полный вывод из строя (Q10): именно оно и рвёт каст.
            caster.CanAct = false;
            sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.IsFalse(caster.IsCastBusy, "Каст оборван");
            Assert.AreEqual(0, ctx.DamageCalls.Count, "Нагрузка не пришла");
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f, "Ресурс НЕ возвращается — каст сгорел");
        }

        [Test]
        public void CastTime_NotBrokenBySoftControlOrDamage()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget   = enemy;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 2f, castSeconds: 0.5f));

            var units = new List<RuntimeUnit> { caster, enemy };
            sys.Tick(units, ctx, SimConstants.TickDelta);

            // Корень (обездвиживание без запрета действий) и урон по кастующему каст НЕ рвут — Q10.
            caster.CanMove   = false;
            caster.CurrentHP -= 50f;

            for (int i = 0; i < 15; i++) sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Каст доиграл под корнем и уроном");
        }

        [Test]
        public void CastTime_TargetDied_RetargetsInsteadOfHittingTheCorpse()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var first  = TestUnit.Make(team: 1);
            var second = TestUnit.Make(team: 1);
            caster.CurrentTarget   = first;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 2f, castSeconds: 0.5f));

            var units = new List<RuntimeUnit> { caster, first, second };
            sys.Tick(units, ctx, SimConstants.TickDelta);

            // Цель добили за время подготовки — мозг уже перевёл фокус на второго.
            first.IsDead = true;
            caster.CurrentTarget = second;

            for (int i = 0; i < 15; i++) sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Каст не ушёл в пустоту");
            Assert.AreSame(second, ctx.DamageCalls[0].Target, "Нагрузка перевелась на живую цель");
        }

        [Test]
        public void Channel_FiresOnStartAndByPeriod_NotOnceMore()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget   = enemy;
            caster.CurrentResource = 50f;
            // Канал 3 с с периодом 1 с: первое срабатывание на старте, дальше по периоду — ТРИ всего.
            // Четвёртое (на тике, где канал кончается) было бы бесплатной прибавкой к силе кита.
            WithAbility(caster, TestAbility.Make(
                cooldown: 30f, cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 1f,
                channelSeconds: 3f, channelTickSeconds: 1f));

            var units = new List<RuntimeUnit> { caster, enemy };
            for (int i = 0; i < 4 * SimConstants.TickRate; i++)
                sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(3, ctx.DamageCalls.Count, "Три срабатывания за три секунды канала");
            Assert.IsFalse(caster.IsCastBusy, "Канал закончился сам");
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f, "Канал платится один раз, на старте");
        }

        [Test]
        public void Channel_BrokenByHardControl_KeepsWhatItAlreadyGave()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget   = enemy;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cooldown: 30f, cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 1f,
                channelSeconds: 3f, channelTickSeconds: 1f));

            var units = new List<RuntimeUnit> { caster, enemy };
            for (int i = 0; i < SimConstants.TickRate + 2; i++)
                sys.Tick(units, ctx, SimConstants.TickDelta);

            int beforeStun = ctx.DamageCalls.Count;
            Assert.AreEqual(2, beforeStun, "Старт плюс первый период");

            caster.CanAct = false;
            for (int i = 0; i < 2 * SimConstants.TickRate; i++)
                sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.IsFalse(caster.IsCastBusy, "Канал оборван контролем");
            Assert.AreEqual(beforeStun, ctx.DamageCalls.Count, "Отданное остаётся, дальнейшее не приходит");
        }

        // ===================== Рекаст авто-атаки (M18) =====================

        [Test]
        public void Recast_WaitsOutTheWindup_ButCutsTheRecovery()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget   = enemy;
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(
                cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 2f, castSeconds: 0.2f));

            var units = new List<RuntimeUnit> { caster, enemy };

            // Занесённый замах доигрывается: удар без замаха читался бы как пропущенный кадр (Q8).
            caster.Phase = AttackPhase.Windup;
            caster.WindupRemaining = 3;
            sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.IsFalse(caster.IsCastBusy, "В замахе умение не начинается");
            Assert.AreEqual(50f, caster.CurrentResource, 1e-4f, "И цена не списана");

            // Хвост после удара умение перебивает — в этом весь выигрыш рекаста.
            caster.Phase = AttackPhase.Recovery;
            caster.RecoveryRemaining = 10;
            sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.IsTrue(caster.IsCasting, "Хвост не держит умение");
            Assert.AreEqual(AttackPhase.Idle, caster.Phase, "Хвост снят");
            Assert.AreEqual(0, caster.RecoveryRemaining);
        }

        [Test]
        public void Recast_ResetsTheAutoAttackTimer_AfterTheAbilityHits()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = WithAttackDamage(TestUnit.Make());
            var enemy  = TestUnit.Make(team: 1);
            caster.CurrentTarget       = enemy;
            caster.CurrentResource     = 50f;
            caster.AttackCooldownTicks = 25;
            WithAbility(caster, TestAbility.Make(
                cost: 30f, mode: AbilityTargetMode.NearestEnemy, damageMultiplier: 2f, castSeconds: 0.2f));

            var units = new List<RuntimeUnit> { caster, enemy };
            sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(25, caster.AttackCooldownTicks, "Пока умение готовится — таймер не трогаем (Q8: после удара)");

            for (int i = 0; i < 6; i++) sys.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreEqual(1, ctx.DamageCalls.Count);
            Assert.AreEqual(0, caster.AttackCooldownTicks, "Удар умением обнулил таймер — авто-атака выходит сразу");
        }

        [Test]
        public void Recast_DoesNotApplyToNonDamagingAbilities()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource     = 50f;
            caster.AttackCooldownTicks = 25;
            // Чистый баф на себя: ритм авто-атак он не ломает, значит и таймер не сбрасывает.
            WithAbility(caster, TestAbility.Make(cost: 30f, mode: AbilityTargetMode.Self));

            sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(25, caster.AttackCooldownTicks, "Не-ударное умение таймер атаки не трогает");
        }
    }
}
