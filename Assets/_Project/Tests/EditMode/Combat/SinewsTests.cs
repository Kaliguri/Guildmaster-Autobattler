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
    /// «Жилы» Десятины: в ближней форме и пока запас выше порога кит бьёт быстрее, платит за каждый удар
    /// долей максимума и пускает кровь сверху удара.
    /// </summary>
    /// <remarks>
    /// <b>Инварианты, которые видно только на связке компонента с формой:</b> навык гейтится СТОЙКОЙ, а
    /// стойку ставит другой компонент, поэтому «работает в упор» нельзя проверить внутри одного файла; и
    /// порог HP гейтит бонус ВМЕСТЕ с платой — разведи их, и кит добивал бы себя ударами по пустому
    /// запасу, чего карточка не просит.
    /// </remarks>
    public sealed class SinewsTests
    {
        private const int TickRate = SimConstants.TickRate;
        private const float MaxHp = 1000f;

        [Test]
        public void AttackSpeedBonus_AppliesOnlyInCloseStance()
        {
            var (unit, effects, ctx) = Scene(stance: AttackStanceComponent.FarStanceIndex);
            effects.Apply(unit, SinewsDef(), unit, ctx);
            EffectSystem.CommitPending(unit);

            RunSeconds(effects, unit, ctx, 0.5f);
            Assert.AreEqual(1f, unit.Stats.Get(StatType.AttackSpeed), 0.001f,
                "В дальней форме «Жилы» молчат: поток — не выпады");

            unit.AttackStance = AttackStanceComponent.CloseStanceIndex;
            RunSeconds(effects, unit, ctx, 0.5f);
            Assert.AreEqual(1.5f, unit.Stats.Get(StatType.AttackSpeed), 0.001f,
                "В упор навык поднимает темп на половину");
        }

        [Test]
        public void AttackSpeedBonus_FallsOffBelowHpThreshold()
        {
            var (unit, effects, ctx) = Scene(stance: AttackStanceComponent.CloseStanceIndex);
            effects.Apply(unit, SinewsDef(), unit, ctx);
            EffectSystem.CommitPending(unit);

            RunSeconds(effects, unit, ctx, 0.5f);
            Assert.AreEqual(1.5f, unit.Stats.Get(StatType.AttackSpeed), 0.001f, "Здоров — бонус висит");

            unit.CurrentHP = MaxHp * 0.25f;                 // просел ниже 30%
            RunSeconds(effects, unit, ctx, 0.5f);
            Assert.AreEqual(1f, unit.Stats.Get(StatType.AttackSpeed), 0.001f,
                "Ниже порога навык выключается целиком, а не только в части платы");
        }

        [Test]
        public void EachAutoAttack_CostsShareOfMaxHp()
        {
            var (unit, effects, ctx) = Scene(stance: AttackStanceComponent.CloseStanceIndex);
            effects.Apply(unit, SinewsDef(), unit, ctx);
            EffectSystem.CommitPending(unit);

            Strike(effects, unit, ctx);
            Assert.AreEqual(MaxHp - 40f, unit.CurrentHP, 0.01f, "Удар стоит 4% максимума");

            Strike(effects, unit, ctx);
            Assert.AreEqual(MaxHp - 80f, unit.CurrentHP, 0.01f, "Плата берётся за КАЖДЫЙ удар");
        }

        /// <summary>
        /// Плата не может добить: порог, гейтящий её, заведомо выше нуля — поэтому отдельного клампа
        /// «не ниже 1 HP» навыку не нужно, и его отсутствие здесь не недосмотр.
        /// </summary>
        [Test]
        public void Cost_StopsAtThreshold_SoItCannotKill()
        {
            var (unit, effects, ctx) = Scene(stance: AttackStanceComponent.CloseStanceIndex);
            effects.Apply(unit, SinewsDef(), unit, ctx);
            EffectSystem.CommitPending(unit);

            for (int i = 0; i < 100; i++) Strike(effects, unit, ctx);

            Assert.Greater(unit.CurrentHP, 0f, "Своей платой кит себя не убивает");
            Assert.LessOrEqual(unit.CurrentHP, MaxHp * 0.3f, "И платит ровно до порога");
        }

        [Test]
        public void Cost_IsNotTakenInFarStance()
        {
            var (unit, effects, ctx) = Scene(stance: AttackStanceComponent.FarStanceIndex);
            effects.Apply(unit, SinewsDef(), unit, ctx);
            EffectSystem.CommitPending(unit);

            Strike(effects, unit, ctx);
            Assert.AreEqual(MaxHp, unit.CurrentHP, 0.01f, "Поток себя не рвёт — «Жилы» это форма в упор");
        }

        [Test]
        public void Bleed_IsOnlyDrawnInCloseStance()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            RuntimeUnit carrier = Unit(AttackStanceComponent.FarStanceIndex);
            RuntimeUnit victim = Unit(AttackStanceComponent.FarStanceIndex);

            effects.Apply(carrier, SinewsDef(), carrier, ctx);
            EffectSystem.CommitPending(carrier);

            Hit(effects, carrier, victim, ctx);
            Assert.IsEmpty(victim.ActiveEffects,
                "Поток бьёт типом «Кровотечение» напрямую — DoT он не вешает, иначе урон формы удвоится");

            carrier.AttackStance = AttackStanceComponent.CloseStanceIndex;
            Hit(effects, carrier, victim, ctx);
            EffectSystem.CommitPending(victim);
            Assert.AreEqual(1, victim.ActiveEffects.Count, "Выпад в упор добавляет кровь сверху");
        }

        // --- сцена ---

        private static (RuntimeUnit unit, EffectSystem effects, MockCombatContext ctx) Scene(int stance)
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            return (Unit(stance), effects, ctx);
        }

        private static RuntimeUnit Unit(int stance)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, MaxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Override, 1f),
            });

            return new RuntimeUnit
            {
                Stats = stats, CurrentHP = MaxHp, AttackStance = stance,
                AutoAttackDamageType = DamageType.Pierce,
            };
        }

        /// <summary>Носитель бьёт кого-то рукой: событие идёт ему, как в бою.</summary>
        private static void Strike(EffectSystem effects, RuntimeUnit self, MockCombatContext ctx)
            => Hit(effects, self, Unit(AttackStanceComponent.FarStanceIndex), ctx);

        private static void Hit(EffectSystem effects, RuntimeUnit self, RuntimeUnit victim, MockCombatContext ctx)
            => effects.Dispatch(self, new CombatEventData(
                CombatEvent.DamageDealt, source: self, target: victim, amount: 100f,
                tags: EffectTag.None, sourceKind: DamageSourceKind.AutoAttack,
                damageType: DamageType.Pierce), ctx);

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

        private static EffectData SinewsDef() => TestEffect.Make(
            baseDuration: -1f,
            components: new IEffectComponent[]
            {
                new SinewsComponent()
                    .With("_requiredStance", AttackStanceComponent.CloseStanceIndex)
                    .With("_hpThresholdPct", 0.3f)
                    .With("_attackSpeedBonusPct", 0.5f)
                    .With("_costPctMaxHp", 0.04f)
                    .With("_checkInterval", 0.1f),
                new BleedOnHitComponent()
                    .With("_bleed", BleedDef())
                    .With("_shareOfAttack", 0.3f)
                    .With("_requiredStance", AttackStanceComponent.CloseStanceIndex),
            });

        private static EffectData BleedDef() => TestEffect.Make(
            baseDuration: 3f,
            polarity: EffectPolarity.Debuff,
            tags: EffectTag.DoT,
            stacking: StackRule.Portions,
            maxStacks: 0,
            components: new PeriodicDamageComponent().With("_interval", 1f).With("_damageType", DamageType.Bleed));
    }
}
