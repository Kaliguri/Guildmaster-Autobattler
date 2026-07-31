using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Слепота (ГДД, каталог эффектов §Детерминизм): промах КАЖДОЙ X-й атаки, без шанса. Глубина — стаками:
    /// один стак промахивает одну атаку из четырёх, четыре стака — все.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между тремя файлами: счётчик атак на юните (<c>RuntimeUnit.AttacksMade</c>), опрос
    /// на снятии цифр (<c>AutoAttackSystem</c>) и сама лестница (<c>BlindComponent</c>). Проверяется здесь
    /// именно лестница и то, что промах не сдвигает собственный период — иначе первый же промах отодвигал
    /// бы следующий на четыре УДАЧНЫХ удара, и «каждая четвёртая» превращалась бы в «реже, чем обещано».
    /// </remarks>
    public sealed class BlindTests
    {
        [Test]
        public void Blind_OneStack_MissesOneAttackOutOfFour()
        {
            Assert.AreEqual(2, MissesOver(attacks: 8, stacks: 1),
                "Один стак: мимо четвёртая и восьмая");
        }

        [Test]
        public void Blind_StacksShortenThePeriod()
        {
            // Лестница из карточки: 4 / 3 / 2 / 1 атаки на один промах.
            Assert.AreEqual(3, MissesOver(attacks: 12, stacks: 1), "1 стак: 12 / 4");
            Assert.AreEqual(4, MissesOver(attacks: 12, stacks: 2), "2 стака: 12 / 3");
            Assert.AreEqual(6, MissesOver(attacks: 12, stacks: 3), "3 стака: 12 / 2");
        }

        [Test]
        public void Blind_FourStacks_MissesEverything()
        {
            Assert.AreEqual(8, MissesOver(attacks: 8, stacks: 4),
                "Четыре стака: слепой не попадает вовсе");
        }

        [Test]
        public void Blind_MissDoesNotPushItsOwnPeriod()
        {
            // Период отмеряет ВЗМАХИ, а не попадания. Считай его от попаданий — за 12 атак промахов было
            // бы два (4-я, потом 9-я), потому что промах не двигал бы счёт.
            Assert.AreEqual(3, MissesOver(attacks: 12, stacks: 1));
        }

        // ===================== Обвязка =====================

        /// <summary>
        /// Сколько из первых <paramref name="attacks"/> взмахов носителя не нанесли урона.
        /// </summary>
        /// <remarks>
        /// Считается разницей «взмахи минус попадания» в КОНЦЕ прогона, а не по каждому тику: цифры удара
        /// снимаются на замахе, а прилетает он позже, поэтому в момент инкремента счётчика попадание ещё в
        /// пути — и потиковая проверка объявляла промахом каждый второй нормальный удар. После нужных
        /// взмахов даём хвост тиков, чтобы последний удар успел дойти.
        /// </remarks>
        private static int MissesOver(int attacks, int stacks)
        {
            var sim = BuildSim();
            // Слепой должен дожить до конца прогона, а манекен — не мешать счёту: у безоружной цели нет
            // сдачи. Первая версия теста этого не учла, и слепой погибал на пятом обмене.
            var blinded = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 1_000_000f);
            var dummy   = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 1_000_000f, damage: 0f);
            sim.EnqueueUnitSpawn(blinded);
            sim.EnqueueUnitSpawn(dummy);
            sim.Tick(SimConstants.TickDelta);
            blinded.CurrentTarget = dummy;

            EffectData blind = Blind();
            for (int i = 0; i < stacks; i++) sim.ApplyEffect(blinded, blind, blinded);
            EffectSystem.CommitPending(blinded);

            // Промах подаётся тем же сигналом, что уклонение (у безоружного манекена своих уклонений нет),
            // поэтому счёт точен и не зависит от того, долетел ли удар: считаем только промахи первых
            // attacks взмахов, дальнейшие в счёт не идут.
            int missesSeen = 0;
            sim.OnAttackEvaded += _ => { if (blinded.AttacksMade <= attacks) missesSeen++; };

            for (int t = 0; t < attacks * 90 && blinded.AttacksMade < attacks; t++)
                sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(attacks, blinded.AttacksMade, "Предусловие: носитель сделал ровно столько взмахов");

            // Хвост на разрешение последнего взмаха: цифры снимаются на замахе, а исход приходит позже.
            for (int t = 0; t < 20; t++) sim.Tick(SimConstants.TickDelta);

            return missesSeen;
        }

        /// <summary>
        /// Слепота: порции, до четырёх. Срок заведомо длиннее прогона — тест меряет лестницу, а не то, как
        /// она сходит. Бессрочной её сделать нельзя: у порции есть срок по определению, и при
        /// <c>baseDuration = -1</c> порция не создаётся вовсе (порционный эффект без порций = без стаков).
        /// </summary>
        private static EffectData Blind()
        {
            var comp = new BlindComponent().With("_periodAtOneStack", 4);
            return TestEffect.Make(baseDuration: 60f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff, stacking: StackRule.Portions, maxStacks: 4, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(9UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f, float damage = 100f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 3f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Pure,
            };
        }
    }
}
