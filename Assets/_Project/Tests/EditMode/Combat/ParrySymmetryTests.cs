using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Парирование не должно зависеть от места бойца в обходе списка. Ответка парирования дёргает
    /// <see cref="RuntimeUnit.RecastAttack"/> у самого парирующего — то есть правит ЕГО цикл атаки из
    /// обработки ЧУЖОГО удара. Если бы это случалось в момент заявки удара, боец, стоящий в списке позже,
    /// получал бы ускоренный хвост до своего хода, а стоящий раньше — после.
    /// </summary>
    /// <remarks>
    /// Сторож класса, а не срез кита: носителя у парирования в контенте пока нет (журнал ГД,
    /// 2026-07-31/42 — «носителя у парирования нет, пока не выдан»), а кит-дуэлянт делается параллельно.
    /// Тест ставит парирование руками, чтобы механика была закрыта ДО того, как её кому-то выдадут.
    /// <para>
    /// Проверка — симметрия: два одинаковых бойца лицом к лицу обязаны идти тик в тик. Любое расхождение
    /// здесь означает, что порядок обхода снова стал частью исхода.
    /// </para>
    /// </remarks>
    public sealed class ParrySymmetryTests
    {
        private const int Ticks = 90;

        [Test]
        public void TwoParryingDuelists_StayInLockstep()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(1UL, effects);

            RuntimeUnit left  = MakeUnit(0, team: 0, pos: new Vector2(-1f, 0f));
            RuntimeUnit right = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f));
            foreach (RuntimeUnit u in new[] { left, right }) sim.EnqueueUnitSpawn(u);
            sim.FlushSpawns();

            EffectData parry = Parry();
            effects.Apply(left,  parry, left,  sim);
            effects.Apply(right, parry, right, sim);

            for (int tick = 0; tick < Ticks; tick++)
            {
                sim.Tick(SimConstants.TickDelta);

                string apart = FirstDifference(left, right);
                Assert.That(apart, Is.Null,
                    $"Парирующие разошлись на тике {tick}: {apart}. Ответка парирования правит цикл атаки " +
                    "носителя из обработки чужого удара — значит она снова случается посреди фазы решений.");
            }
        }

        /// <summary>Что должно совпадать у зеркальной пары. Позиция сверяется отражением по X.</summary>
        private static string FirstDifference(RuntimeUnit l, RuntimeUnit r)
        {
            if (!Mathf.Approximately(l.CurrentHP, r.CurrentHP))            return $"HP ({l.CurrentHP} против {r.CurrentHP})";
            if (l.Phase != r.Phase)                                        return $"фаза свинга ({l.Phase} против {r.Phase})";
            if (l.WindupRemaining != r.WindupRemaining)                    return "остаток замаха";
            if (l.RecoveryRemaining != r.RecoveryRemaining)                return "остаток доигрыша";
            if (l.AttackCooldownTicks != r.AttackCooldownTicks)            return "кулдаун атаки";
            if (!Mathf.Approximately(l.SwingRecoverySpeed, r.SwingRecoverySpeed)) return "скорость доигрыша (рекаст)";
            if (l.ActiveEffects.Count != r.ActiveEffects.Count)            return "число эффектов";
            if (!Mathf.Approximately(l.Position.x, -r.Position.x))         return "позиция X (не отражена)";

            return null;
        }

        // --- Стенд ---

        /// <summary>
        /// Парирование без стана и без заряда ответки: пустой <c>_riposteCharge</c> — это и есть путь
        /// «ответка через рекаст», ровно тот, что правит цикл атаки носителя.
        /// </summary>
        private static EffectData Parry()
        {
            EffectData window = TestEffect.Make(
                baseDuration: 0.3f, polarity: EffectPolarity.Buff,
                components: new ParryWindowComponent());

            return TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Buff, unremovable: true,
                components: new ParryComponent()
                    .With("_parryWindow", window)
                    .With("_cooldownSeconds", 1f)
                    .With("_maxCharges", 1));
        }

        private static CombatSimulation BuildSim(ulong seed, EffectSystem effects) =>
            new CombatSimulation(
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                effects, new RegenSystem());

        /// <summary>Мили-боец, стоящий на месте: дистанция уже боевая, движение не нужно.</summary>
        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 1000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 40f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 3f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = 1000f,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
