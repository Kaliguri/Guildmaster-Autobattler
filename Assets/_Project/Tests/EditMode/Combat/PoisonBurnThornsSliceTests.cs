using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Срез трёх новых китов (E2): Друид (яд + «Взрыв спор»), Огненный мечник («Пылающие клинки»),
    /// Древень («Шипастое древо»). Проверяем механику, а не числа баланса.
    /// </summary>
    public sealed class PoisonBurnThornsSliceTests
    {
        // --- Древень: ответка по площади от БРОНИ, только на авто-атаку ---

        [Test]
        public void ArmorThorns_RetaliatesAllEnemiesAround_ForArmorValue()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });

            var attacker = TestUnit.Make(team: 1);
            var bystander = TestUnit.Make(team: 1);
            bystander.Position = new Vector2(2f, 0f); // в радиусе шипов
            ctx.UnitsInWorld.Add(attacker);
            ctx.UnitsInWorld.Add(bystander);

            var comp = new ArmorThornsComponent().With("_armorRatio", 1f).With("_radius", 3f);
            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: comp), treant, ctx);

            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f, EffectTag.None, sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(treant, in hit, ctx);

            Assert.AreEqual(2, ctx.DamageCalls.Count, "Шипы бьют ВСЕХ врагов вокруг, не только атакующего");
            Assert.AreEqual(20f, ctx.DamageCalls[0].RawDamage, 1e-4f, "Урон шипов = 100% брони носителя");
        }

        [Test]
        public void ArmorThorns_RetaliatesOnAbilityHit_ButNotOnDotOrReactive()
        {
            // Решение Макса: крону будит любой ПРЯМОЙ удар — и авто-атака, и атакующее заклинание.
            // А тики DoT и чужие ответки — нет: иначе яд превращает Древня в вечный дамаг-пульс,
            // а два Древня зацикливают друг друга.
            Assert.AreEqual(1, ThornsVolleysFor(DamageSourceKind.Ability),  "атакующая способность будит шипы");
            Assert.AreEqual(0, ThornsVolleysFor(DamageSourceKind.Periodic), "тик DoT шипы не будит");
            Assert.AreEqual(0, ThornsVolleysFor(DamageSourceKind.Reactive), "чужая ответка шипы не будит (нет пинг-понга)");
        }

        [Test]
        public void ArmorThorns_MicroCooldown_ThrottlesVolleys()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });
            var attacker = TestUnit.Make(team: 1);
            ctx.UnitsInWorld.Add(attacker);

            var comp = new ArmorThornsComponent().With("_armorRatio", 1f).With("_radius", 3f).With("_cooldownSeconds", 0.5f);
            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: comp), treant, ctx);

            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f, EffectTag.None,
                                          sourceKind: DamageSourceKind.AutoAttack);

            // Три удара подряд в один тик (толпа быстрых мили) — залп должен быть ОДИН.
            sys.Dispatch(treant, in hit, ctx);
            sys.Dispatch(treant, in hit, ctx);
            sys.Dispatch(treant, in hit, ctx);

            Assert.AreEqual(1, ctx.DamageCalls.Count,
                "микро-КД: ритм ответки задаёт Древень, а не скорость атаки окружившей его толпы");
        }

        [Test]
        public void ArmorThorns_GrowthStacks_WidenTheRadius()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });

            var attacker = TestUnit.Make(team: 1);
            var faraway  = TestUnit.Make(team: 1);
            faraway.Position = new Vector2(3.5f, 0f); // за базовым радиусом 3, но внутри раздутого (3 × 1.4)
            ctx.UnitsInWorld.Add(attacker);
            ctx.UnitsInWorld.Add(faraway);

            // «Разрастание»: 2 стака → радиус шипов +40% (карточка ГДД: активка раздувает и крону тоже).
            EffectData growth = TestEffect.Make(baseDuration: -1f, stacking: StackRule.Stack, maxStacks: 5);
            var comp = new ArmorThornsComponent()
                .With("_armorRatio", 1f)
                .With("_radius", 3f)
                .With("_growthEffect", growth)
                .With("_radiusPerGrowthStack", 0.2f)
                .With("_cooldownSeconds", 0f); // КД тут не мешаем — проверяем ровно радиус

            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: comp), treant, ctx);

            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f, EffectTag.None,
                                          sourceKind: DamageSourceKind.AutoAttack);

            sys.Dispatch(treant, in hit, ctx);
            Assert.AreEqual(1, ctx.DamageCalls.Count, "без «Разрастания» дальний враг вне радиуса шипов");

            ctx.DamageCalls.Clear();
            sys.Apply(treant, growth, treant, ctx);
            sys.Apply(treant, growth, treant, ctx); // 2 стака

            sys.Dispatch(treant, in hit, ctx);
            Assert.AreEqual(2, ctx.DamageCalls.Count, "крона разрослась — дальний враг теперь достаётся шипами");
        }

        /// <summary>Сколько раз шипы ответили на один входящий удар данного вида.</summary>
        private static int ThornsVolleysFor(DamageSourceKind kind)
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });
            var attacker = TestUnit.Make(team: 1);
            ctx.UnitsInWorld.Add(attacker);

            var comp = new ArmorThornsComponent().With("_armorRatio", 1f).With("_radius", 3f);
            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: comp), treant, ctx);

            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f, EffectTag.None, sourceKind: kind);
            sys.Dispatch(treant, in hit, ctx);

            return ctx.DamageCalls.Count;
        }

        // --- Огненный мечник: разгон скорости атаки + само-урон за удар ---

        [Test]
        public void BlazingBlades_RampsAttackSpeed_AndCostsOwnHp()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var pyre = TestUnit.Make(team: 0);
            pyre.CurrentHP = 1000f;
            pyre.Stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AttackSpeed, ModifierOp.Flat, 1f) });
            var victim = TestUnit.Make(team: 1);

            EffectData ramp = TestEffect.Make(
                baseDuration: -1f,
                polarity: EffectPolarity.Buff,
                tags: EffectTag.Buff,
                stacking: StackRule.Stack,
                maxStacks: 20,
                components: new StatModifierComponent().With("_modifiers",
                    new[] { new StatModifier(StatType.AttackSpeed, ModifierOp.PercentAdd, 0.05f) }));

            var comp = new BlazingBladesComponent()
                .With("_selfDamagePctCurrentHp", 0.01f)
                .With("_rampEffect", ramp);
            sys.Apply(pyre, TestEffect.Make(baseDuration: -1f, components: comp), pyre, ctx);

            float baseAttackSpeed = pyre.Stats.Get(StatType.AttackSpeed);

            var hit = new CombatEventData(CombatEvent.DamageDealt, pyre, victim, 100f, EffectTag.None, sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(pyre, in hit, ctx);
            EffectSystem.CommitPending(pyre);   // разгон — стат-мод, он проявляется в конце тика

            Assert.Greater(pyre.Stats.Get(StatType.AttackSpeed), baseAttackSpeed, "Удар клинком разгоняет скорость атаки");

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Каждый удар стоит носителю части своего HP");
            Assert.AreSame(pyre, ctx.DamageCalls[0].Target, "Само-урон бьёт по себе");
            Assert.AreEqual(10f, ctx.DamageCalls[0].RawDamage, 1e-4f, "1% от текущего HP (1000)");
            Assert.AreEqual(DamageSchool.True, ctx.DamageCalls[0].School, "Само-урон идёт мимо брони");
        }

        [Test]
        public void BlazingBlades_Ignores_NonAutoAttackDamage()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);
            var pyre = TestUnit.Make(team: 0);
            var victim = TestUnit.Make(team: 1);

            sys.Apply(pyre, TestEffect.Make(baseDuration: -1f, components: new BlazingBladesComponent()), pyre, ctx);

            var abilityHit = new CombatEventData(CombatEvent.DamageDealt, pyre, victim, 100f);
            sys.Dispatch(pyre, in abilityHit, ctx);

            Assert.AreEqual(0, ctx.DamageCalls.Count, "Само-урон не капает с урона способностей (иначе рекурсия)");
        }

        // --- Друид: детонация всех отравленных («Взрыв спор») ---

        [Test]
        public void SporeBurst_DamagesEveryPoisonedEnemy_WithPoisonAffinity_AndConsumesTag()
        {
            var effects = new EffectSystem();

            var druid = TestUnit.Make(team: 0);
            druid.Stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f) });

            var poisoned1 = TestUnit.Make(team: 1);
            var poisoned2 = TestUnit.Make(team: 1);
            var clean = TestUnit.Make(team: 1);

            EffectData spores = TestEffect.Make(
                baseDuration: 4f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff | EffectTag.DoT | EffectTag.Poison,
                stacking: StackRule.Refresh, maxStacks: 1);

            var units = new List<RuntimeUnit> { druid, poisoned1, poisoned2, clean };
            var ctx = new MockCombatContext(effects: effects);
            effects.Apply(poisoned1, spores, druid, ctx);
            effects.Apply(poisoned2, spores, druid, ctx);
            // Яд лёг тиком раньше взрыва: условие каста читает маску тегов на начало тика.
            foreach (RuntimeUnit u in units) EffectSystem.CommitPending(u);

            AbilityData burst = TestAbility.Make(
                mode: AbilityTargetMode.AllEnemiesWithTag,
                damageMultiplier: 2.5f,
                castCondition: CastCondition.EnemiesWithTagCount,
                castConditionCount: 2,
                triggerTag: EffectTag.Poison,
                consumesTriggerTag: true,
                schoolOverride: DamageSchoolOverride.True,
                affinityOverride: DamageAffinityOverride.Poison);

            druid.Abilities.Add(new AbilityRuntime(burst));

            var abilities = new AbilitySystem();
            Assert.IsTrue(abilities.TryCast(druid, 0, units, ctx), "Двое отравленных — условие каста выполнено");
            // Расход тега — снятие эффекта, а оно проявляется на коммите, как и наложение.
            foreach (RuntimeUnit u in units) EffectSystem.CommitPending(u);

            Assert.AreEqual(2, ctx.DamageCalls.Count, "Детонируют только отравленные");
            Assert.AreEqual(250f, ctx.DamageCalls[0].RawDamage, 1e-4f, "2.5 × AutoAttackDamage");
            Assert.AreEqual(DamageAffinity.Poison, ctx.DamageCalls[0].Affinity);
            Assert.AreEqual(DamageSchool.True, ctx.DamageCalls[0].School);

            Assert.AreEqual(EffectTag.None, poisoned1.EffectTagMask & EffectTag.Poison, "Тег «Яд» израсходован взрывом");
        }

        [Test]
        public void SporeBurst_HealsAllies_PerUniquePoison_NotPerStack()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);

            var druid = TestUnit.Make(team: 0);
            druid.Position = new Vector2(50f, 0f);            // далеко от эпицентра — сам не лечится, чистый assert
            druid.Stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f) });

            var ally    = TestUnit.Make(team: 0);
            ally.Position = new Vector2(1f, 0f);              // рядом с врагом → в облаке спор
            var enemy   = TestUnit.Make(team: 1);
            enemy.Position = Vector2.zero;

            // ДВА РАЗНЫХ яда (разные Def) + один из них наложен дважды (стак). Уников должно быть 2, не 3.
            EffectData poisonA = TestEffect.Make(baseDuration: 4f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff | EffectTag.Poison, stacking: StackRule.Stack, maxStacks: 5);
            EffectData poisonB = TestEffect.Make(baseDuration: 4f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff | EffectTag.Poison, stacking: StackRule.Stack, maxStacks: 5);

            var units = new List<RuntimeUnit> { druid, ally, enemy };
            ctx.UnitsInWorld.AddRange(units);
            effects.Apply(enemy, poisonA, druid, ctx);
            effects.Apply(enemy, poisonA, druid, ctx); // второй стак того же яда — уник не добавляет
            effects.Apply(enemy, poisonB, druid, ctx);
            EffectSystem.CommitPending(enemy);         // яды легли тиком раньше взрыва (закон видимости)

            AbilityData burst = TestAbility.Make(
                mode: AbilityTargetMode.AllEnemiesWithTag,
                damageMultiplier: 2.5f,
                areaRadius: 3.5f,
                healFlat: 80f,                    // лечение союзнику за ОДИН уникальный яд
                triggerTag: EffectTag.Poison,
                consumesTriggerTag: true,
                schoolOverride: DamageSchoolOverride.True,
                affinityOverride: DamageAffinityOverride.Poison);
            druid.Abilities.Add(new AbilityRuntime(burst));

            Assert.IsTrue(new AbilitySystem().TryCast(druid, 0, units, ctx));

            // 2 уникальных яда × 80 = 160 хила союзнику. Стак того же яда третьим уником НЕ считается.
            Assert.AreEqual(160f, ctx.TotalHealed, 1e-3f, "Хил = за каждый уникальный яд, стаки не в счёт");
            Assert.AreEqual(1, ctx.DamageCalls.Count, "Взрыв всё ещё наносит урон отравленному врагу");
        }

        // --- Огненный мечник: «Воспламенение» сжигает накопленные «Угли» (модель 2026-07-26/4) ---

        [Test]
        public void Ignition_BurnsEmberStacks_AndConsumesThem()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var pyre   = TestUnit.Make(team: 0);
            var victim = TestUnit.Make(team: 1);

            // Три уголька на цели: каждый стак стоит 15 урона взрыва.
            EffectData ember = EmberEffect();
            sys.Apply(victim, ember, pyre, ctx);
            sys.Apply(victim, ember, pyre, ctx);
            sys.Apply(victim, ember, pyre, ctx);
            // Топливо детонация считает по снимку начала тика, а снятие судит по тику появления, — угли
            // должны успеть «повисеть». Сдвигаем границу тика, как это делает бой.
            ctx.AdvanceTick(victim);

            EffectData ignition = TestEffect.Make(baseDuration: 0f, components:
                new IgnitionComponent()
                    .With("_detonateTag", EffectTag.Ember)
                    .With("_damagePerStack", 15f)
                    .With("_school", DamageSchool.Magical)
                    .With("_magicElement", MagicElement.Fire));
            sys.Apply(victim, ignition, pyre, ctx);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Детонация наносит один удар — сумму по стакам");
            Assert.AreEqual(45f, ctx.DamageCalls[0].RawDamage, 1e-3f, "15 за стак × 3 стака");
            Assert.AreEqual(DamageSchool.Magical, ctx.DamageCalls[0].School);
            Assert.AreEqual(MagicElement.Fire, ctx.DamageCalls[0].Element, "Взрыв — огонь: его же усиливают «Угли»");
            // Маска тегов тоже отложена законом видимости: снятие проявляется в конце тика, а не в
            // момент взрыва. Проводим границу второй раз — и только теперь спрашиваем про маску.
            ctx.AdvanceTick(victim);
            Assert.AreEqual(EffectTag.None, victim.EffectTagMask & EffectTag.Ember, "«Угли» израсходованы взрывом");
        }

        [Test]
        public void Ignition_NoEmbers_DoesNothing()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var pyre   = TestUnit.Make(team: 0);
            var victim = TestUnit.Make(team: 1);

            EffectData ignition = TestEffect.Make(baseDuration: 0f, components:
                new IgnitionComponent().With("_detonateTag", EffectTag.Ember).With("_damagePerStack", 15f));
            sys.Apply(victim, ignition, pyre, ctx);

            Assert.AreEqual(0, ctx.DamageCalls.Count, "Нечего сжигать — урона нет");
        }

        // --- «Угли»: усиливают огонь по цели и осыпаются без подпитки ---

        [Test]
        public void Embers_AmplifyIncomingFire_ByOnePercentPerStack()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);
            var victim = TestUnit.Make(team: 1);

            EffectData ember = EmberEffect();
            for (int i = 0; i < 10; i++) sys.Apply(victim, ember, null, ctx);
            // Стаки под законом видимости: набранное за тик влияет на исход со следующего. В бою эту
            // границу проводит CommitTickChanges, здесь — сдвиг тика у мока.
            ctx.AdvanceTick(victim);

            var fire = new DamageRequest(null, victim, 100f, DamageSchool.Magical, 100f,
                sourceKind: DamageSourceKind.Periodic, element: MagicElement.Fire);
            sys.RunPreDamage(victim, in fire, ctx);
            Assert.AreEqual(1.10f, sys.PreDamageMultiplier, 1e-4f, "10 стаков = +10% урона огнём");

            var slash = new DamageRequest(null, victim, 100f, DamageSchool.Physical, 100f,
                sourceKind: DamageSourceKind.AutoAttack);
            sys.RunPreDamage(victim, in slash, ctx);
            Assert.AreEqual(1f, sys.PreDamageMultiplier, 1e-4f, "Не-огонь «Угли» не усиливают");
        }

        private static EffectData EmberEffect() => TestEffect.Make(
            baseDuration: -1f, polarity: EffectPolarity.Debuff,
            tags: EffectTag.Debuff | EffectTag.Ember,
            stacking: StackRule.Stack, maxStacks: 999,
            components: new EmberComponent().With("_fireDamagePerStack", 0.01f));
    }
}
