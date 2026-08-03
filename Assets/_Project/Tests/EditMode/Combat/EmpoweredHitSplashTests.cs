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
    /// Площадь ЗАРЯЖЕННОГО удара (<c>EmpowerNextAttackComponent._splashRadius</c>): взмах, который задевает
    /// соседей цели. Носители — огненная сфера гоблина-мага и размашистый удар земляного голема; форма
    /// принадлежит заряду, а не киту, поэтому одиночная атака и площадной взмах живут у одного юнита.
    /// </summary>
    /// <remarks>
    /// Инвариант между тремя файлами: заряд кладёт радиус (<c>EmpowerNextAttackComponent</c>), юнит его
    /// держит до удара (<c>RuntimeUnit.EmpowerSplashRadius</c>), система тратит вместе с цифрами удара
    /// (<c>AutoAttackSystem</c>). Нарушить можно из любого звена, и два других не узнают — поэтому тест.
    /// </remarks>
    public sealed class EmpoweredHitSplashTests
    {
        [Test]
        public void EmpoweredHit_DamagesNeighboursOfTheTarget()
        {
            Dealt hits = OneEmpoweredHit(splashRadius: 2f);

            Assert.AreEqual(200f, hits.Victim,    1e-3f, "Цель: базовые 100 × заряд 2");
            Assert.AreEqual(200f, hits.Neighbour, 1e-3f, "Сосед цели получил ту же цифру");
            Assert.AreEqual(0f,   hits.Ally,      1e-3f, "Свой в радиусе взмаха не задет");
        }

        [Test]
        public void EmpoweredHit_WithoutSplash_LeavesNeighboursAlone()
        {
            Dealt hits = OneEmpoweredHit(splashRadius: 0f);

            Assert.AreEqual(200f, hits.Victim,    1e-3f, "Предусловие: заряженный удар состоялся");
            Assert.AreEqual(0f,   hits.Neighbour, 1e-3f, "Без радиуса взмах остаётся одиночным");
        }

        [Test]
        public void EmpoweredHit_DoesNotHitTheTargetTwice()
        {
            // Цель и сосед стоят на равном удалении и с равными статами: задевай площадь саму цель
            // повторно — она получила бы вдвое больше соседа, а не столько же.
            Dealt hits = OneEmpoweredHit(splashRadius: 2f);

            Assert.AreEqual(hits.Neighbour, hits.Victim, 1e-3f,
                "Цель получила ровно один удар, а не свой плюс площадной");
        }

        // ===================== Обвязка =====================

        /// <summary>Сколько урона пришло каждому за прогон — по событию, а не по HP на выходе.</summary>
        private struct Dealt
        {
            public float Victim;
            public float Neighbour;
            public float Ally;
        }

        /// <summary>
        /// Один заряженный удар носителя по цели, у которой есть сосед-враг и сосед-союзник в радиусе.
        /// </summary>
        /// <remarks>
        /// Урон считается по <c>OnDamageDealt</c> и только ОТ НОСИТЕЛЯ: цель и её сосед — живые враги и
        /// бьют в ответ, поэтому итоговое HP отвечает на другой вопрос («сколько всего досталось»), чем
        /// нужен тесту («что сделал этот удар»). Прогон обрывается на первом ударе носителя, иначе в
        /// счёт попадёт и его следующая, уже НЕ заряженная атака.
        /// </remarks>
        private static Dealt OneEmpoweredHit(float splashRadius)
        {
            var sim = BuildSim();
            var attacker  = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim    = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 1000f);
            var neighbour = MakeUnit(2, team: 1, pos: new Vector2(1.6f, 0f), maxHp: 1000f);
            var ally      = MakeUnit(3, team: 0, pos: new Vector2(1.3f, 0.4f), maxHp: 1000f);
            foreach (var u in new[] { attacker, victim, neighbour, ally }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta);        // завести в списки и пространственный хеш
            attacker.CurrentTarget = victim;

            var dealt = new Dealt();
            bool struck = false;
            sim.OnDamageDealt += (src, tgt, res) =>
            {
                if (src != attacker) return;
                if (tgt == victim)    { dealt.Victim    += res.HpDamage; struck = true; }
                if (tgt == neighbour) { dealt.Neighbour += res.HpDamage; }
                if (tgt == ally)      { dealt.Ally      += res.HpDamage; }
            };

            sim.ApplyEffect(attacker, Charge(splashRadius), attacker);
            EffectSystem.CommitPending(attacker);

            for (int t = 0; t < 90 && !struck; t++) sim.Tick(SimConstants.TickDelta);
            Assert.IsTrue(struck, "Предусловие: носитель успел ударить за прогон");
            return dealt;
        }

        /// <summary>Заряд усиления с заданным радиусом взмаха. Тег траты — свой, чтобы не срывать чужой стелс.</summary>
        private static EffectData Charge(float splashRadius)
        {
            var comp = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_splashRadius", splashRadius)
                .With("_consumeTag", EffectTag.None);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(5UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f),
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
