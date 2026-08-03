using System.Collections.Generic;
using System.Linq;
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
    /// Линия Кровотечения (<see cref="StackRule.Portions"/>, решения Макса 2026-07-30): каждое ранение —
    /// своя порция со своей силой и своим сроком, порции складываются, потолка нет, каждая сходит отдельно.
    /// <para><b>Инварианты, которые нельзя выразить комментарием</b> — оба ловятся только на двух
    /// источниках или на двух порциях сразу: сила порции принадлежит ТОМУ, кто её наложил (в модели со
    /// стаками её задавал первый ранивший, и второй кровоточащий кит молча раздавал бы чужую силу), и
    /// вклад порций не умножается на их число (у порционного эффекта он уже суммирован, поэтому наивное
    /// «rate × стаки» дало бы квадратичный урон).</para>
    /// </summary>
    public sealed class BleedLineTests
    {
        private const int TickRate = SimConstants.TickRate;

        [Test]
        public void SinglePortion_DealsItsWholeDamageOverItsDuration()
        {
            var (victim, effects, ctx) = Scene();
            RuntimeUnit source = Attacker(attack: 90f);

            // Порция 90 урона на 3 секунды.
            effects.Apply(victim, BleedDef(), source, ctx, durationSecondsOverride: 0f, potencyOverride: 90f);
            RunSeconds(effects, victim, ctx, 3f);

            Assert.AreEqual(90f, ctx.TotalRawDamage, 0.01f,
                "Порция отдаёт РОВНО свою величину за свой срок — иначе длительность линии стала бы ручкой силы");
            Assert.AreEqual(3, ctx.DamageCalls.Count, "Три тика по секунде");
        }

        [Test]
        public void PortionsFromDifferentSources_KeepTheirOwnStrength()
        {
            var (victim, effects, ctx) = Scene();
            EffectData bleed = BleedDef();

            // Слабый ранит первым, сильный — вторым. В модели со стаками сила осталась бы от первого.
            effects.Apply(victim, bleed, Attacker(30f), ctx, 0f, potencyOverride: 30f);
            effects.Apply(victim, bleed, Attacker(150f), ctx, 0f, potencyOverride: 150f);

            RunSeconds(effects, victim, ctx, 3f);

            Assert.AreEqual(180f, ctx.TotalRawDamage, 0.01f,
                "Каждая порция несёт СВОЮ силу: 30 + 150, а не удвоенную силу первого ранившего");
        }

        [Test]
        public void PortionRate_IsNotMultipliedByPortionCount()
        {
            var (victim, effects, ctx) = Scene();
            EffectData bleed = BleedDef();

            // Три одинаковые порции по 30: ждём 90 всего, а не 270.
            for (int i = 0; i < 3; i++) effects.Apply(victim, bleed, Attacker(30f), ctx, 0f, potencyOverride: 30f);

            RunSeconds(effects, victim, ctx, 3f);

            Assert.AreEqual(90f, ctx.TotalRawDamage, 0.01f,
                "Вклад порций уже суммирован в потенции — множить его ещё и числом порций значит считать их дважды");
        }

        [Test]
        public void EachPortionExpiresOnItsOwnSchedule()
        {
            var (victim, effects, ctx) = Scene();
            EffectData bleed = BleedDef();
            RuntimeEffect effect;

            effects.Apply(victim, bleed, Attacker(60f), ctx, 0f, potencyOverride: 60f);
            RunSeconds(effects, victim, ctx, 2f);          // первой порции жить ещё секунду

            effects.Apply(victim, bleed, Attacker(60f), ctx, 0f, potencyOverride: 60f);
            effect = victim.ActiveEffects.Single();
            Assert.AreEqual(2, effect.PortionCount, "Обе порции живы");

            RunSeconds(effects, victim, ctx, 1f);          // первая иссякла, вторая нет
            Assert.AreEqual(1, effect.PortionCount,
                "Порции сходят каждая по своему сроку — новая кровь не продлевает старую");

            RunSeconds(effects, victim, ctx, 2f);          // и вторая иссякла
            Assert.IsEmpty(victim.ActiveEffects, "Эффект снят, когда иссякла последняя порция");
        }

        [Test]
        public void NoStackCeiling()
        {
            var (victim, effects, ctx) = Scene();
            EffectData bleed = BleedDef();

            // Двадцать ранений — вдвое больше прежнего потолка в 10 стаков.
            for (int i = 0; i < 20; i++) effects.Apply(victim, bleed, Attacker(30f), ctx, 0f, potencyOverride: 30f);

            RuntimeEffect effect = victim.ActiveEffects.Single();
            Assert.AreEqual(20, effect.PortionCount, "Потолка у линии нет: ограничителем служит короткий срок порции");

            RunSeconds(effects, victim, ctx, 3f);
            Assert.AreEqual(600f, ctx.TotalRawDamage, 0.5f, "Двадцать порций по 30 = 600 урона");
        }

        [Test]
        public void BleedOnHit_TakesShareOfAttackBeforeArmor()
        {
            var (victim, effects, ctx) = Scene();
            EffectData bleed = BleedDef();

            var carrier = Attacker(attack: 90f);
            var onHit = new BleedOnHitComponent().With("_bleed", bleed).With("_shareOfAttack", 0.3f);
            EffectData passive = TestEffect.Make(baseDuration: -1f, components: onHit);
            effects.Apply(carrier, passive, carrier, ctx);
            EffectSystem.CommitPending(carrier);

            // Носитель ударил цель. Величина порции считается от СТАТА, а не от прошедшего брони урона:
            // e.Amount здесь заведомо другой (37), и если бы кровь считалась от него, вышло бы 11.1.
            effects.Dispatch(carrier, new CombatEventData(
                CombatEvent.DamageDealt, source: carrier, target: victim, amount: 37f,
                tags: EffectTag.None, sourceKind: DamageSourceKind.AutoAttack, damageType: DamageType.Pierce), ctx);

            RuntimeEffect effect = victim.ActiveEffects.Single();
            Assert.AreEqual(1, effect.PortionCount, "Удар пустил кровь");

            RunSeconds(effects, victim, ctx, 3f);
            Assert.AreEqual(27f, ctx.TotalRawDamage, 0.01f,
                "30% от урона удара ПО СТАТАМ (90 × 0.3), а не от прошедшего сквозь броню");
        }

        [Test]
        public void EveryDamagingDotInTheProjectTicksOncePerSecond()
        {
            // Правило Макса 2026-07-30: единый шаг DoT и HoT — одна секунда. Сторож здесь, а не в
            // комментарии: шаг живёт в каждом ассете отдельно, и новый DoT с интервалом 0.5 не поссорился
            // бы ни с чем, кроме читаемости боя — то есть прошёл бы молча.
            //
            // Правило про УРОН И ЛЕЧЕНИЕ во времени, а не про всякую периодику: сход стаков «Углей»,
            // опрос холодной линии, пересмотр боевой стойки и разовый призыв тоже периодичны, но игрок их
            // не считает тиками — им единый шаг не нужен и был бы вреден.
            var offenders = new List<string>();

            foreach (EffectData def in AllEffectAssets())
            {
                if (def.Components == null) continue;
                foreach (IEffectComponent c in def.Components)
                {
                    if (c is not PeriodicDamageComponent and not PeriodicHealComponent) continue;
                    if (c is not IPeriodicComponent periodic) continue;
                    if (Mathf.Approximately(periodic.Interval, 1f)) continue;
                    offenders.Add($"{def.Id} ({c.GetType().Name}): interval={periodic.Interval}");
                }
            }

            Assert.IsEmpty(offenders,
                "Все периодические эффекты обязаны тикать раз в секунду: " + string.Join("; ", offenders));
        }

        private static IEnumerable<EffectData> AllEffectAssets()
        {
            return UnityEditor.AssetDatabase.FindAssets("t:EffectData")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<EffectData>)
                .Where(d => d != null);
        }

        private static EffectData BleedDef() => TestEffect.Make(
            baseDuration: 3f,
            polarity: EffectPolarity.Debuff,
            tags: EffectTag.DoT,
            stacking: StackRule.Portions,
            maxStacks: 0,
            components: new PeriodicDamageComponent().With("_interval", 1f).With("_damageType", DamageType.Bleed));

        private static void RunSeconds(EffectSystem effects, RuntimeUnit unit, MockCombatContext ctx, float seconds)
        {
            var units = new List<RuntimeUnit> { unit };
            int ticks = Mathf.RoundToInt(seconds * TickRate);
            for (int i = 0; i < ticks; i++)
            {
                effects.Tick(units, ctx, SimConstants.TickDelta);
                ctx.AdvanceTick(unit);
            }
        }

        private static (RuntimeUnit victim, EffectSystem effects, MockCombatContext ctx) Scene()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            return (Attacker(0f), effects, ctx);
        }

        private static RuntimeUnit Attacker(float attack)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 5000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, attack),
            });

            return new RuntimeUnit
            {
                Stats = stats, CurrentHP = 5000f, AutoAttackDamageType = DamageType.Bleed,
            };
        }
    }
}
