using System.Collections.Generic;
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
    /// Боевые стойки по дистанции (<see cref="AttackStanceComponent"/>): форма авто-атаки переписывается
    /// целиком, переключение идёт с гистерезисом и только в покое между ударами. Носитель — Десятина.
    /// <para><b>Инварианты, которые нельзя выразить комментарием:</b> форма меняет ВСЁ (тип урона,
    /// доставку, on-hit, канал, статы) — форма, задающая часть профиля, оставила бы остальное от кита, и
    /// кит стал бы третьей невидимой стойкой; и форма не меняется посреди удара — иначе замах начинается
    /// одной формой, а прилетает другой.</para>
    /// </summary>
    public sealed class AttackStanceTests
    {
        [Test]
        public void FarByDefault_StanceOverwritesWholeProfile()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 9f);

            Assert.AreEqual(0, unit.AttackStance, "Далеко от цели — дальняя форма");
            Assert.AreEqual(DamageType.Bleed, unit.AutoAttackDamageType, "Дальняя форма режет кровью");
            Assert.IsTrue(unit.AttackChannel.Exists, "Дальняя форма — поток");
            Assert.AreEqual(AttackType.Ranged, unit.AttackType);

            ctx.AdvanceTick(unit);
            Assert.AreEqual(8f, unit.Stats.Get(StatType.AttackRange), 0.01f, "Дальность формы применена");
        }

        [Test]
        public void EnemyStepsIn_SwitchesToCloseForm()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 9f);

            enemy.Position = new Vector2(2f, 0f);   // вошёл в ближнюю зону (порог 3)
            TickEffects(effects, unit, ctx, seconds: 0.2f);

            Assert.AreEqual(1, unit.AttackStance, "В упор — ближняя форма");
            Assert.AreEqual(DamageType.Pierce, unit.AutoAttackDamageType, "Ближняя форма колет");
            Assert.IsFalse(unit.AttackChannel.Exists, "Ближняя форма бьёт обычными ударами, без потока");
            Assert.AreEqual(AttackType.Melee, unit.AttackType);
            Assert.AreEqual(1.5f, unit.Stats.Get(StatType.AttackRange), 0.01f, "Дальность стала ближней");
            Assert.AreEqual(1.5f, unit.Stats.Get(StatType.AttackSpeed), 0.01f, "И темп — ближней формы");
        }

        [Test]
        public void Hysteresis_HoldsCloseFormInsideTheGap()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 2f);
            Assert.AreEqual(1, unit.AttackStance, "Предусловие: начали в упор");

            // Между входным порогом (3) и выходным (4.5): форма НЕ должна дёргаться.
            enemy.Position = new Vector2(3.8f, 0f);
            TickEffects(effects, unit, ctx, seconds: 0.5f);
            Assert.AreEqual(1, unit.AttackStance, "В зазоре гистерезиса форма держится");

            // За выходным порогом — возврат в дальнюю.
            enemy.Position = new Vector2(6f, 0f);
            TickEffects(effects, unit, ctx, seconds: 0.2f);
            Assert.AreEqual(0, unit.AttackStance, "За выходным порогом — снова поток");
        }

        [Test]
        public void FormDoesNotChangeMidSwing()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 9f);

            unit.Phase = AttackPhase.Windup;        // удар уже начат дальней формой
            enemy.Position = new Vector2(1f, 0f);   // и цель прыгнула в упор
            TickEffects(effects, unit, ctx, seconds: 0.5f);

            Assert.AreEqual(0, unit.AttackStance,
                "Начатый удар доигрывает прежней формой — иначе замах и попадание разошлись бы по форме");

            unit.Phase = AttackPhase.Idle;          // удар доигран
            TickEffects(effects, unit, ctx, seconds: 0.2f);
            Assert.AreEqual(1, unit.AttackStance, "В первом же окне покоя форма меняется");
        }

        [Test]
        public void LosingTargetKeepsCurrentForm()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 2f);
            Assert.AreEqual(1, unit.AttackStance, "Предусловие: ближняя форма");

            unit.CurrentTarget = null;
            TickEffects(effects, unit, ctx, seconds: 0.5f);

            Assert.AreEqual(1, unit.AttackStance,
                "Без цели форма не сбрасывается: следующий враг уже может стоять в лицо");
        }

        /// <summary>
        /// Фокус — часть формы, и мозг обязан читать его у формы, а не у кита: профиль он берёт один раз
        /// при сборке, поэтому «в упор бью самого бронированного» через профиль невыразимо в принципе.
        /// </summary>
        [Test]
        public void FocusFollowsTheForm_AndBeatsTheKitProfile()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 9f);
            Assert.AreEqual(TargetingMode.HighestHp, unit.StanceTargeting,
                "Вдали кит ищет самого живучего — тот дольше кровоточит");

            enemy.Position = new Vector2(2f, 0f);
            TickEffects(effects, unit, ctx, seconds: 0.2f);
            Assert.AreEqual(TargetingMode.HighestArmor, unit.StanceTargeting,
                "В упор — самого бронированного, там окупается шред");

            // Профиль кита просит «ближайшего», но форма сильнее.
            var brain = new ProfileBrain(new AIProfile());
            RuntimeUnit near = MakeUnit(2, team: 1, pos: new Vector2(1f, 0f));
            RuntimeUnit armored = MakeUnit(3, team: 1, pos: new Vector2(7f, 0f));
            armored.Stats.AddModifiersFrom("armor", new[]
            {
                new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 80f),
            });

            brain.Decide(unit, new FakeBattleView(unit, near, armored));
            Assert.AreSame(armored, unit.CurrentTarget,
                "Форма переписала фокус: выбран бронированный, а не ближний");
        }

        [Test]
        public void FormGone_FocusReturnsToTheKitProfile()
        {
            var (unit, enemy, effects, ctx) = Scene(enemyDist: 9f);
            Assert.IsNotNull(unit.StanceTargeting, "Предусловие: стойка стоит");

            effects.Remove(unit, unit.ActiveEffects[0], ctx);
            EffectSystem.CommitPending(unit);

            Assert.IsNull(unit.StanceTargeting,
                "Стойки нет — нет и её фокуса, иначе боец выбирал бы цель по правилу снятой формы");
        }

        /// <summary>Прогнать систему эффектов на заданное число секунд боя.</summary>
        private static void TickEffects(EffectSystem effects, RuntimeUnit unit, MockCombatContext ctx, float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * Guildmaster.Core.Simulation.SimConstants.TickRate);
            var units = new List<RuntimeUnit> { unit };
            for (int i = 0; i < ticks; i++)
            {
                effects.Tick(units, ctx, Guildmaster.Core.Simulation.SimConstants.TickDelta);
                ctx.AdvanceTick(unit);
            }
        }

        private static (RuntimeUnit unit, RuntimeUnit enemy, EffectSystem effects, MockCombatContext ctx)
            Scene(float enemyDist)
        {
            var stance = new AttackStanceComponent()
                .With("_enterCloseRange", 3f)
                .With("_exitCloseRange", 4.5f)
                .With("_checkInterval", 0.1f)
                .With("_farStance", FarStance())
                .With("_closeStance", CloseStance());

            EffectData def = TestEffect.Make(baseDuration: -1f, components: stance);

            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);

            RuntimeUnit unit  = MakeUnit(0, team: 0, pos: Vector2.zero);
            RuntimeUnit enemy = MakeUnit(1, team: 1, pos: new Vector2(enemyDist, 0f));
            unit.CurrentTarget = enemy;

            effects.Apply(unit, def, unit, ctx);
            EffectSystem.CommitPending(unit);

            return (unit, enemy, effects, ctx);
        }

        // Дальняя форма: кровавый поток снарядом-каналом, дальность 8, темп 0.75.
        private static AttackStanceComponent.AttackStance FarStance() => new AttackStanceComponent.AttackStance
        {
            Delivery   = AttackType.Ranged,
            DamageType = DamageType.Bleed,
            Targeting  = TargetingMode.HighestHp,
            Channel    = new AttackChannel { DurationSeconds = 3f, WindupSeconds = 1f },
            Stats = new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Override, 8f),
                new StatModifier(StatType.AttackSpeed, ModifierOp.Override, 0.75f),
            },
        };

        // Ближняя форма: колющие выпады, дальность 1.5, темп выше.
        private static AttackStanceComponent.AttackStance CloseStance() => new AttackStanceComponent.AttackStance
        {
            Delivery   = AttackType.Melee,
            DamageType = DamageType.Pierce,
            Targeting  = TargetingMode.HighestArmor,
            Channel    = AttackChannel.None,
            Stats = new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Override, 1.5f),
                new StatModifier(StatType.AttackSpeed, ModifierOp.Override, 1.5f),
            },
        };

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 1000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
            });

            return new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats,
                CurrentHP = 1000f, Position = pos, PreviousPosition = pos,
                AutoAttackDamageType = DamageType.Blunt,
            };
        }
    }
}
