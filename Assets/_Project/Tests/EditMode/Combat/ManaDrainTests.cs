using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
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
    /// Мана-дрейн гоблина-проказника: плоское замедление набора ресурса
    /// (<see cref="StatType.ResourceRegenFlat"/>) и выбор цели «ближайший ИЗ тех, у кого есть мана»
    /// (<see cref="AbilityTargetMode.NearestEnemyWithResource"/>).
    /// </summary>
    /// <remarks>
    /// Оба инварианта живут между файлами, поэтому комментарием их не удержать. Первый — что дельта
    /// складывается с базой <b>до</b> множителя и не переворачивает набор в утечку: «медленнее
    /// восстанавливается» и «сосёт ману» — разные обещания, и второе мы не давали. Второй — что каст
    /// не уходит в безресурсного бойца: там дебафф ресурса стоит ровно ноль, и мимо жертвы с маной он
    /// читался бы как «проказник ничего не делает».
    /// </remarks>
    public sealed class ManaDrainTests
    {
        private const float BaseRegen = 5f;

        [Test]
        public void FlatDebuff_SlowsRefill_ProportionallyToTheDelta()
        {
            RuntimeUnit clean   = WithResource(TestUnit.Make(), pool: 100f);
            RuntimeUnit drained = WithResource(TestUnit.Make(), pool: 100f, regenFlat: -4f);

            RegenFor(1f, clean, drained);

            Assert.AreEqual(BaseRegen, clean.CurrentResource, 1e-3f, "Без дебаффа — базовые 5 в секунду");
            Assert.AreEqual(1f, drained.CurrentResource, 1e-3f, "5 - 4 = 1 в секунду: время до каста впятеро");
        }

        [Test]
        public void FlatDebuff_StopsRefill_ButNeverDrainsTheStored()
        {
            RuntimeUnit unit = WithResource(TestUnit.Make(), pool: 100f, regenFlat: -20f);
            unit.CurrentResource = 40f;

            RegenFor(2f, unit);

            Assert.AreEqual(40f, unit.CurrentResource, 1e-3f,
                "Дельта сильнее базы останавливает набор, а не разворачивает его в утечку");
        }

        [Test]
        public void FlatDebuff_AppliesBeforeTheMultiplier()
        {
            // (5 - 3) × 0.5 = 1 в секунду. При обратном порядке (5 × 0.5 - 3) вышло бы -0.5, то есть
            // остановка — числа выбраны так, чтобы порядок был виден по результату.
            RuntimeUnit unit = WithResource(TestUnit.Make(), pool: 100f, regenFlat: -3f, regenEffDelta: -0.5f);

            RegenFor(1f, unit);

            Assert.AreEqual(1f, unit.CurrentResource, 1e-3f, "Плоская дельта складывается с базой ДО множителя");
        }

        [Test]
        public void Target_SkipsTheNearestEnemy_WhenHeHasNoResource()
        {
            var effects = new EffectSystem();
            var ctx     = new MockCombatContext(effects: effects);
            var sys     = new AbilitySystem();

            RuntimeUnit caster    = Reaching(At(TestUnit.Make(team: 0), 0f));
            RuntimeUnit warrior   = At(TestUnit.Make(team: 1), 1f);                          // рядом, но без маны
            RuntimeUnit arcanist  = WithResource(At(TestUnit.Make(team: 1), 6f), pool: 15f);  // дальше, зато с маной

            WithAbility(caster, TestAbility.Make(
                effects: new[] { Drain(-4f) }, mode: AbilityTargetMode.NearestEnemyWithResource));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster, warrior, arcanist }, ctx);
            EffectSystem.CommitPending(warrior);
            EffectSystem.CommitPending(arcanist);

            Assert.IsTrue(cast, "Жертва с маной есть — каст состоялся");
            Assert.AreEqual(-4f, arcanist.Stats.Get(StatType.ResourceRegenFlat), 1e-4f, "Дебафф уехал носителю маны");
            Assert.AreEqual(0f, warrior.Stats.Get(StatType.ResourceRegenFlat), 1e-4f,
                "Ближайший, но безресурсный боец не тронут");
        }

        [Test]
        public void Target_NoCast_WhenNobodyHasResource()
        {
            var effects = new EffectSystem();
            var ctx     = new MockCombatContext(effects: effects);
            var sys     = new AbilitySystem();

            RuntimeUnit caster = At(TestUnit.Make(team: 0), 0f);
            RuntimeUnit foe    = At(TestUnit.Make(team: 1), 1f);

            WithAbility(caster, TestAbility.Make(
                effects: new[] { Drain(-4f) }, cooldown: 4f, mode: AbilityTargetMode.NearestEnemyWithResource));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster, foe }, ctx);

            Assert.IsFalse(cast, "Дебаффить некого — каст не состоялся");
            Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "И кулдаун впустую не сгорел");
        }

        // ===================== Обвязка =====================

        /// <summary>Прогнать регенерацию заданное время тиками сима.</summary>
        private static void RegenFor(float seconds, params RuntimeUnit[] units)
        {
            var regen = new RegenSystem { ResourcePerSecond = BaseRegen };
            var list  = new List<RuntimeUnit>(units);
            int ticks = Mathf.RoundToInt(seconds * SimConstants.TickRate);

            for (int t = 0; t < ticks; t++)
                regen.Tick(list, SimConstants.TickDelta);
        }

        /// <param name="regenEffDelta">
        /// Дельта к множителю <see cref="StatType.ResourceGainEff"/>, чей натуральный старт — <c>1.0</c>
        /// (<c>StatsConfig.NaturalDefault</c>). Поэтому -0.5 означает множитель ×0.5, а не ×-0.5.
        /// </param>
        private static RuntimeUnit WithResource(RuntimeUnit u, float pool, float regenFlat = 0f,
                                                float regenEffDelta = 0f)
        {
            u.Stats.AddModifiersFrom("resource", new[]
            {
                new StatModifier(StatType.MaxResource,       ModifierOp.Flat, pool),
                new StatModifier(StatType.ResourceRegenFlat, ModifierOp.Flat, regenFlat),
                new StatModifier(StatType.ResourceGainEff,   ModifierOp.Flat, regenEffDelta),
            });
            return u;
        }

        private static RuntimeUnit At(RuntimeUnit u, float x)
        {
            u.Position         = new Vector2(x, 0f);
            u.PreviousPosition = u.Position;
            return u;
        }

        /// <summary>
        /// Дальность каста, покрывающая всю расстановку теста. Проверяем ВЫБОР жертвы, а не дистанцию:
        /// с появлением дальности каста (2026-08-04) умение обязано доставать до цели, и без этой строки
        /// тест падал бы на гейте дистанции, ничего не сообщив про выбор.
        /// </summary>
        private static RuntimeUnit Reaching(RuntimeUnit u, float range = 12f)
        {
            u.Stats.AddModifiersFrom("reach", new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Flat, range),
            });
            return u;
        }

        private static RuntimeUnit WithAbility(RuntimeUnit u, AbilityData data)
        {
            u.Abilities.Add(new AbilityRuntime(data));
            return u;
        }

        /// <summary>Дебафф «мана-дрейн»: плоская дельта к скорости набора, без длительности.</summary>
        private static EffectData Drain(float perSecond)
        {
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[]
                {
                    new StatModifier(StatType.ResourceRegenFlat, ModifierOp.Flat, perSecond),
                });
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Debuff, components: statMod);
        }
    }
}
