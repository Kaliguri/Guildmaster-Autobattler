using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Интеграция фабрики (шаг 9): пассивки реликвии накладываются при сборке (постоянная длительность,
    /// до инициализации CurrentHP), активки оборачиваются в <see cref="Abilities.AbilityRuntime"/>,
    /// ресурс стартует с <see cref="StatType.StartResource"/> (вики «12» §2.4, §6).
    /// </summary>
    public sealed class RuntimeUnitFactoryTests
    {
        private static RuntimeUnitFactory MakeFactory(out EffectSystem effects)
        {
            effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            return new RuntimeUnitFactory(null, effects, ctx);
        }

        [Test]
        public void Resource_InitializedFromStartResource()
        {
            var factory = MakeFactory(out _);
            RelicData relic = TestRelic.Make(stats: new[]
            {
                new StatModifier(StatType.MaxResource,   ModifierOp.Flat, 100f),
                new StatModifier(StatType.StartResource, ModifierOp.Flat,  40f),
            });

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero);

            Assert.AreEqual(40f, unit.CurrentResource, 1e-4f, "CurrentResource стартует со StartResource");
        }

        [Test]
        public void Abilities_BuiltFromRelic()
        {
            var factory = MakeFactory(out _);
            RelicData relic = TestRelic.Make(abilities: new[]
            {
                TestAbility.Make(cooldown: 5f),
                TestAbility.Make(cooldown: 8f),
            });

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero);

            Assert.AreEqual(2, unit.Abilities.Count, "Обе активки реликвии обёрнуты в AbilityRuntime");
            Assert.AreEqual(5f, unit.Abilities[0].Data.BaseCooldown, 1e-4f);
        }

        [Test]
        public void Passive_GrantedEffect_AppliedAtBuildWithInfiniteDuration()
        {
            var factory = MakeFactory(out _);
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 7f) });
            EffectData passive = TestEffect.Make(
                baseDuration: -1f, tags: EffectTag.Buff, components: statMod);
            RelicData relic = TestRelic.Make(grantedEffects: new[] { passive });

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero);

            Assert.AreEqual(1, unit.ActiveEffects.Count, "Пассивка в активных эффектах");
            Assert.IsTrue(unit.ActiveEffects[0].IsPermanent, "Длительность постоянная (−1)");
            Assert.AreEqual(7f, unit.Stats.Get(StatType.MoveSpeed), 1e-4f, "Стат-мод пассивки применён");
            Assert.AreNotEqual(EffectTag.None, unit.EffectTagMask & EffectTag.Buff, "Маска тегов обновлена");
        }

        [Test]
        public void Passive_MaxHpBuff_RaisesStartingHp()
        {
            var factory = MakeFactory(out _);
            var hpBuff = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 200f) });
            EffectData passive = TestEffect.Make(baseDuration: -1f, components: hpBuff);
            RelicData relic = TestRelic.Make(
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 300f) },
                grantedEffects: new[] { passive });

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero);

            // 300 (база реликвии) + 200 (пассив) = 500; CurrentHP стартует с полного, т.к. пассивки
            // накладываются ДО инициализации CurrentHP.
            Assert.AreEqual(500f, unit.Stats.Get(StatType.MaxHP), 1e-4f);
            Assert.AreEqual(500f, unit.CurrentHP, 1e-4f, "Юнит стартует с полным HP, включая бонус пассивки");
        }

        // ── D1: предметы/баннеры (план 11 §5.5) ──────────────────────────────

        [Test]
        public void Item_StatMods_Applied_AndRaiseStartingHp()
        {
            var factory = MakeFactory(out _);
            RelicData relic = TestRelic.Make(
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 300f) });
            ItemData item = MakeItem(new StatModifier(StatType.MaxHP, ModifierOp.Flat, 150f));

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero, new[] { item });

            Assert.AreEqual(450f, unit.Stats.Get(StatType.MaxHP), 1e-4f, "мод предмета сложился с базой кита");
            Assert.AreEqual(450f, unit.CurrentHP, 1e-4f, "предмет с +MaxHP поднимает стартовый HP (моды до HP-init)");
        }

        [Test]
        public void MultipleItems_AndBanners_AllApplied()
        {
            var factory = MakeFactory(out _);
            RelicData relic = TestRelic.Make(
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 100f) });
            ItemData vesselItem = MakeItem(new StatModifier(StatType.MaxHP, ModifierOp.Flat, 50f));
            ItemData banner     = MakeItem(new StatModifier(StatType.MaxHP, ModifierOp.Flat, 25f));

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero, new[] { vesselItem, banner });

            Assert.AreEqual(175f, unit.Stats.Get(StatType.MaxHP), 1e-4f, "моды всех предметов сложились (100+50+25)");
        }

        [Test]
        public void NullItems_NoChange()
        {
            var factory = MakeFactory(out _);
            RelicData relic = TestRelic.Make(
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 200f) });

            RuntimeUnit unit = factory.Create(relic, null, team: 0, Vector2.zero, items: null);

            Assert.AreEqual(200f, unit.Stats.Get(StatType.MaxHP), 1e-4f);
        }

        private static ItemData MakeItem(params StatModifier[] mods)
        {
            var item = ScriptableObject.CreateInstance<ItemData>();
            typeof(ItemData).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Instance)
                            .SetValue(item, mods);
            return item;
        }
    }
}
