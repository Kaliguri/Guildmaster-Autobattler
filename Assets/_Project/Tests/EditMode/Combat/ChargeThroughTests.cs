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
    /// «Волчий разгон» наездника: на старте боя носитель проходит СКВОЗЬ строй, снося всех на линии, урон
    /// растёт со скоростью передвижения, броня считается вдвое меньшей. Плюс сам примитив процентного
    /// пробивания (<c>DamageRequest.BonusPctPen</c>), заведённый ради него.
    /// </summary>
    /// <remarks>
    /// Инвариант между файлами: компонент считает урон и заказывает полёт, <c>DisplaceRequest</c> несёт
    /// пробивание, <c>DisplacementSystem</c> бьёт им «ядром», <c>DamagePipeline</c> его применяет. Порвать
    /// можно в любом звене, и соседние не заметят.
    /// </remarks>
    public sealed class ChargeThroughTests
    {
        // ===================== Процентное пробивание =====================

        [Test]
        public void PctPen_HalvesArmorForThisHitOnly()
        {
            // ArmorK 100, броня 100: без пробивания проходит половина урона, с 50% — две трети.
            Assert.AreEqual(50f, DamageThroughArmor(armor: 100f, pctPen: 0f), 1e-3f,
                "Броня 100 при ArmorK 100 режет удар вдвое");
            Assert.AreEqual(66.667f, DamageThroughArmor(armor: 100f, pctPen: 0.5f), 1e-2f,
                "Пробивание половины брони: 100 / (100 + 50)");
            Assert.AreEqual(100f, DamageThroughArmor(armor: 100f, pctPen: 1f), 1e-3f,
                "Полное пробивание — броня не мешает вовсе");
        }

        private static float DamageThroughArmor(float armor, float pctPen)
        {
            var sim = BuildSim();
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim   = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 10_000f, armor: armor);
            sim.EnqueueUnitSpawn(attacker);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);

            float before = victim.CurrentHP;
            sim.DealDamage(new DamageRequest(attacker, victim, 100f, DamageType.Slash, sim.ArmorK,
                sourceKind: DamageSourceKind.Ability, bonusPctPen: pctPen));
            sim.Tick(SimConstants.TickDelta);
            return before - victim.CurrentHP;
        }

        // ===================== Разгон =====================

        [Test]
        public void Charge_HitsEveryoneOnTheLine_NotJustTheFirst()
        {
            var (sim, rider, front, behind, flank) = Field();
            sim.ApplyEffect(rider, Charge(), rider);
            EffectSystem.CommitPending(rider);

            for (int t = 0; t < 60; t++) sim.Tick(SimConstants.TickDelta);

            Assert.Less(front.CurrentHP, 10_000f, "Первый на пути получил разгон");
            Assert.Less(behind.CurrentHP, 10_000f, "Стоящий за ним — тоже: разгон идёт насквозь");
            Assert.AreEqual(10_000f, flank.CurrentHP, 1e-3f, "Тот, кто в стороне от коридора, не задет");
        }

        [Test]
        public void Charge_ScalesWithMoveSpeed()
        {
            float fast = ChargeDamage(moveSpeed: 3.63f);   // штатная скорость наездника
            float slow = ChargeDamage(moveSpeed: 1.81f);   // замедлен вдвое

            Assert.Greater(fast, slow, "Замедление режет урон разгона");
            // Множитель: 1 + 1.5 × (скорость − 1). При 3.63 → ×4.945, при 1.81 → ×2.215.
            Assert.AreEqual(4.945f / 2.215f, fast / slow, 1e-2f,
                "Урон падает ровно пропорционально множителю, а не «как-нибудь»");
        }

        [Test]
        public void Charge_HappensOnlyOncePerBattle()
        {
            var (sim, rider, front, _, _) = Field();
            sim.ApplyEffect(rider, Charge(), rider);
            EffectSystem.CommitPending(rider);

            for (int t = 0; t < 30; t++) sim.Tick(SimConstants.TickDelta);
            float afterFirst = front.CurrentHP;
            Assert.Less(afterFirst, 10_000f, "Предусловие: разгон состоялся");

            // Дальше носитель живёт обычной жизнью: второй раз он не разгоняется. Урон от авто-атак
            // исключён — у наездника в этом стенде их нет (дальность 0 не даёт ударить).
            for (int t = 0; t < 120; t++) sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(afterFirst, front.CurrentHP, 1e-3f, "Второго разгона не было");
        }

        /// <summary>
        /// Урон ПЕРВОГО удара носителя по первому на пути — это и есть разгон.
        /// </summary>
        /// <remarks>
        /// Считается по событию, а не разницей HP: разогнавшись, наездник живёт дальше и на быстрой
        /// скорости успевает догнать ту же цель и добавить обычную авто-атаку — в итоговом HP она
        /// неотличима от разгона и ломала пропорцию.
        /// </remarks>
        private static float ChargeDamage(float moveSpeed)
        {
            var sim = BuildSim();
            var rider = MakeUnit(0, team: 0, pos: Vector2.zero, moveSpeed: moveSpeed);
            var front = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), maxHp: 100_000f);
            sim.EnqueueUnitSpawn(rider);
            sim.EnqueueUnitSpawn(front);
            sim.Tick(SimConstants.TickDelta);

            float first = 0f;
            sim.OnDamageDealt += (src, tgt, res) =>
            {
                if (first == 0f && src == rider && tgt == front) first = res.HpDamage;
            };

            sim.ApplyEffect(rider, Charge(), rider);
            EffectSystem.CommitPending(rider);
            for (int t = 0; t < 60 && first == 0f; t++) sim.Tick(SimConstants.TickDelta);

            Assert.Greater(first, 0f, "Предусловие: разгон попал в цель");
            return first;
        }

        // ===================== Обвязка =====================

        /// <summary>Наездник, двое на линии разгона и один в стороне. Все толстые — тест меряет попадания.</summary>
        private static (CombatSimulation, RuntimeUnit, RuntimeUnit, RuntimeUnit, RuntimeUnit) Field()
        {
            var sim = BuildSim();
            var rider  = MakeUnit(0, team: 0, pos: Vector2.zero, moveSpeed: 3.63f);
            var front  = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f),   maxHp: 10_000f);
            var behind = MakeUnit(2, team: 1, pos: new Vector2(4f, 0f),   maxHp: 10_000f);
            var flank  = MakeUnit(3, team: 1, pos: new Vector2(2f, 5f),   maxHp: 10_000f);
            foreach (var u in new[] { rider, front, behind, flank }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta);
            return (sim, rider, front, behind, flank);
        }

        /// <summary>Разгон с числами наездника (ассет GoblinCharge).</summary>
        private static EffectData Charge()
        {
            var comp = new ChargeThroughOnBattleStartComponent()
                .With("_distance", 6f)
                .With("_width", 1.2f)
                .With("_damageMultiplier", 1f)
                .With("_speedBaseline", 1f)
                .With("_multPerSpeed", 1.5f)
                .With("_pctArmorPen", 0.5f)
                .With("_damageType", DamageType.Pierce);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(13UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f,
                                            float moveSpeed = 0f, float armor = 0f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                // Дальность 0: в этом стенде никто не должен бить авто-атаками — считаем только разгон.
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 0f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, moveSpeed),
                new StatModifier(StatType.PhysArmor,        ModifierOp.Flat, armor),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
