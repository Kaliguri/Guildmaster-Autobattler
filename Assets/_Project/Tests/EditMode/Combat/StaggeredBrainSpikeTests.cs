using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Спайк S1 (вики «13» §5, §8 шаг 1) — <b>гейт всей Фазы 3</b>.
    /// Проверяет, что staggered 10-Гц мозг (переоценка раз в 3 тика, фаза по <c>Id</c>)
    /// поверх 30-Гц исполнения интента остаётся побитово детерминированным.
    /// <para>
    /// Весь код ниже — throwaway: прото-шов <see cref="ISpikeBrain"/> + каденс-система
    /// крутятся в обход <c>CombatSimulation</c> (мозг — единственный, кто ставит цель,
    /// иначе продовый <c>TargetingSystem</c> дозаполнял бы цели и стаггер стал бы ненаблюдаем).
    /// Настоящие <c>IUnitBrain</c>/<c>BrainSystem</c> внутри <c>Tick</c> — шаг 3 после зелёного гейта.
    /// </para>
    /// </summary>
    public sealed class StaggeredBrainSpikeTests
    {
        private const int Ticks = 120;

        // === Гейт: checksum ×2 ===

        [Test]
        public void StaggeredBrain_SameChecksum_AcrossTwoRuns()
        {
            ulong a = RunStaggered();
            ulong b = RunStaggered();

            Assert.AreEqual(a, b,
                "Staggered 10-Гц мозг внёс рассинхрон между двумя идентичными прогонами");
        }

        // === Стаггер реален (не no-op): юнит думает раз в 3 тика, на своей фазе ===

        [Test]
        public void StaggeredBrain_UnitDecidesOnItsPhaseOnly_Every3rdTick()
        {
            var units = MakeTwoTeams();
            var brain = new SpikeBrainSystem(new NearestEnemySpikeBrain());
            var move  = new MovementSystem();

            for (int tick = 0; tick < 9; tick++)
            {
                brain.Tick(units, tick);
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            }

            // Id=1 → фаза 1 → решает на тиках 1,4,7 (никто не умирает → dirty не срабатывает).
            var ticksForId1 = DecisionTicksFor(brain, unitId: 1);
            CollectionAssert.AreEqual(new[] { 1, 4, 7 }, ticksForId1,
                "Юнит должен думать строго на своей фазе раз в 3 тика, не каждый тик");
        }

        // === Интент залипает между AI-тиками: движение исполняет прошлое решение ===

        [Test]
        public void IntentPersistsBetweenAiTicks_UnitKeepsMoving()
        {
            var units = MakeTwoTeams();
            var brain = new SpikeBrainSystem(new NearestEnemySpikeBrain());
            var move  = new MovementSystem();

            // Тик 0: решает только фаза 0. Берём юнит Id=1 (фаза 1) — на тике 1 он решит,
            // а тики 2 и 3 для него вне фазы → должен продолжать двигаться по старому интенту.
            RuntimeUnit u = units.Find(x => x.Id == 1);

            for (int tick = 0; tick <= 1; tick++)   // прогоняем до тика 1 включительно (юнит получил цель)
            {
                brain.Tick(units, tick);
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            }
            Assert.IsNotNull(u.CurrentTarget, "После своей фазы у юнита должна быть цель");

            // Тик 2 — вне фазы для Id=1: Decide НЕ зовётся, но движение обязано продвинуть.
            brain.Tick(units, 2);
            Assert.IsFalse(DecisionTicksFor(brain, 1).Contains(2), "Тик 2 — вне фазы Id=1, решения быть не должно");

            Vector2 before = u.Position;
            move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);
            Assert.AreNotEqual(before, u.Position, "Интент должен залипать: юнит двигается между AI-тиками");
        }

        // === Прерывание: цель умерла → переоценка на следующем тике вне фазы ===

        [Test]
        public void TargetDeath_SetsBrainDirty_ReevaluatesNextTick()
        {
            // Атакующий Id=3 (фаза 0) против двух врагов. На тике 0 (его фаза) берёт ближнего.
            var attacker = MakeUnit(id: 3, team: 0, x: 0f);
            var near     = MakeUnit(id: 1, team: 1, x: 2f);   // ближе
            var far      = MakeUnit(id: 2, team: 1, x: 5f);
            var units    = new List<RuntimeUnit> { attacker, near, far };

            var brain = new SpikeBrainSystem(new NearestEnemySpikeBrain());

            brain.Tick(units, 0);                              // фаза 0 → attacker выбирает near
            Assert.AreSame(near, attacker.CurrentTarget);

            near.IsDead = true;                                // цель погибла

            // Тик 1 — вне фазы attacker (3%3=0 ≠ 1). Без dirty решения бы не было,
            // но мёртвая цель ставит dirty → переоценка немедленно.
            brain.Tick(units, 1);

            Assert.IsTrue(DecisionTicksFor(brain, 3).Contains(1),
                "Смерть цели должна вызвать переоценку на следующем тике вне фазы (BrainDirty)");
            Assert.AreSame(far, attacker.CurrentTarget, "После прерывания должен перецелиться на живого врага");
        }

        // === Прогон гейта ===

        private static ulong RunStaggered()
        {
            var units = MakeTwoTeams();
            var brain = new SpikeBrainSystem(new NearestEnemySpikeBrain());
            var move  = new MovementSystem();

            int tick = 0;
            for (; tick < Ticks; tick++)
            {
                brain.Tick(units, tick);                       // 10 Гц staggered → пишет CurrentTarget
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, SimTuning.Default);      // 30 Гц → исполняет интент
            }
            return Checksum(units, tick);
        }

        // === Хелперы сетапа ===

        private static List<RuntimeUnit> MakeTwoTeams()
        {
            // Id 0..5 → фазы 0,1,2,0,1,2. Команды сближаются (движение эволюционирует позиции).
            return new List<RuntimeUnit>
            {
                MakeUnit(0, team: 0, x: -5f),
                MakeUnit(1, team: 0, x: -4f),
                MakeUnit(2, team: 0, x: -3f),
                MakeUnit(3, team: 1, x:  5f),
                MakeUnit(4, team: 1, x:  4f),
                MakeUnit(5, team: 1, x:  3f),
            };
        }

        private static RuntimeUnit MakeUnit(int id, int team, float x)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 500f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = 500f,
                Position         = new Vector2(x, 0f),
                PreviousPosition = new Vector2(x, 0f),
            };
        }

        // === Checksum (зеркалит CombatSimulation.ComputeChecksum, минус RNG — спайк его не трогает) ===

        private static ulong Checksum(IReadOnlyList<RuntimeUnit> units, int tick)
        {
            ulong hash = (ulong)tick * 2654435761UL;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                hash ^= (ulong)(u.Id * 1000003);
                hash ^= (ulong)(long)(u.Position.x * 1000f) * 2246822519UL;
                hash ^= (ulong)(long)(u.Position.y * 1000f) * 3266489917UL;
                hash ^= (ulong)(long)(u.CurrentHP  * 100f)  * 668265263UL;
                hash  = (hash << 13) | (hash >> 51);
            }
            return hash;
        }

        private static List<int> DecisionTicksFor(SpikeBrainSystem brain, int unitId)
        {
            var result = new List<int>();
            foreach (int packed in brain.DecisionLog)
                if ((packed & 0xFFFF) == unitId) result.Add(packed >> 16);
            return result;
        }
    }

    // ===================== throwaway-код спайка =====================

    /// <summary>Прото-шов <c>IUnitBrain</c> (§3.1): пишет ТОЛЬКО интент, не мутирует мир.</summary>
    internal interface ISpikeBrain
    {
        void Decide(RuntimeUnit self, IReadOnlyList<RuntimeUnit> all);
    }

    /// <summary>v0-мозг спайка: ближайший живой враг, тай-брейк по <c>Id</c> (детерминизм, §4.2).</summary>
    internal sealed class NearestEnemySpikeBrain : ISpikeBrain
    {
        public void Decide(RuntimeUnit self, IReadOnlyList<RuntimeUnit> all)
        {
            RuntimeUnit best = null;
            float       bestSq = float.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                RuntimeUnit o = all[i];
                if (o.IsDead || o.Team == self.Team) continue;

                float dSq = (o.Position - self.Position).sqrMagnitude;
                if (dSq < bestSq || (dSq == bestSq && (best == null || o.Id < best.Id)))
                {
                    bestSq = dSq;
                    best   = o;
                }
            }

            self.CurrentTarget = best;
        }
    }

    /// <summary>
    /// Каденс-система спайка: stagger по фазе <c>Id % AiTickInterval</c> + прерывание «цель мертва».
    /// Обход строго по индексу списка (НЕ по HashSet) — иначе порядок решений плавает → рассинхрон.
    /// </summary>
    internal sealed class SpikeBrainSystem
    {
        private readonly ISpikeBrain _brain;

        /// <summary>Инструментовка стаггера: упаковка <c>(tick &lt;&lt; 16) | id</c>.</summary>
        public readonly List<int> DecisionLog = new List<int>();

        public SpikeBrainSystem(ISpikeBrain brain) => _brain = brain;

        public void Tick(IReadOnlyList<RuntimeUnit> units, int currentTick)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead) continue;

                bool onPhase = currentTick % SimConstants.AiTickInterval == u.Id % SimConstants.AiTickInterval;
                bool dirty   = u.CurrentTarget != null && u.CurrentTarget.IsDead;   // прерывание
                if (!onPhase && !dirty) continue;

                _brain.Decide(u, units);
                DecisionLog.Add((currentTick << 16) | u.Id);
            }
        }
    }
}
