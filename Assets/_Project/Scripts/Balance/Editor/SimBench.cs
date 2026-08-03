using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;

namespace Guildmaster.Balance.Editor
{
    /// <summary>Юнит под наблюдением бенча: рантайм-объект + читаемая метка + архетип (для группировки в отчёте).</summary>
    internal readonly struct TrackedUnit
    {
        public readonly RuntimeUnit Unit;
        public readonly string Label;
        public readonly string Archetype;

        public TrackedUnit(RuntimeUnit unit, string label, string archetype)
        {
            Unit = unit;
            Label = label;
            Archetype = archetype;
        }
    }

    /// <summary>Режим остановки прогона.</summary>
    internal enum RunMode
    {
        /// <summary>Тикать ровно <c>maxTicks</c> (для DPS/AoE — цель бессмертна, исход не наступает).</summary>
        FixedDuration,

        /// <summary>Тикать до исхода боя либо до <c>maxTicks</c> (тогда timeout).</summary>
        UntilOutcome,
    }

    /// <summary>
    /// Примитив прогона одного боя: раздать Id, подписать сборщик, влить юнитов, оттикать, вернуть отчёт.
    /// На нём стоят все бенчи. Id раздаются последовательно ЗДЕСЬ (а не фабрикой) — гарантия уникальности
    /// ключей метрик между реальными и синтетическими юнитами.
    /// </summary>
    internal static class SimBench
    {
        public static BattleReport Drive(SimEnvironment env, List<TrackedUnit> tracked, RunMode mode, int maxTicks)
        {
            for (int i = 0; i < tracked.Count; i++)
                tracked[i].Unit.Id = i;

            // Тела рождаются в бою ТОЙ ЖЕ фабрикой, а её счётчик про эту переразметку не знает: часть
            // бойцов — синтетика, созданная мимо фабрики, поэтому её счётчик отстаёт от длины списка.
            // Без сдвига первый же призыв получил бы Id живого бойца и весь его урон лёг бы в чужую строку.
            env.Factory.AdvanceIdsPast(tracked.Count - 1);

            // EffectSystem передаём: контроль, проклятия и выданные бафы видны только на шве наложения.
            var collector = new MetricCollector(env.Sim, tracked, env.Effects);

            for (int i = 0; i < tracked.Count; i++)
                env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            for (int t = 0; t < maxTicks; t++)
            {
                if (mode == RunMode.UntilOutcome && env.Sim.Outcome.IsFinished) break;
                env.Sim.Tick(SimConstants.TickDelta);
            }

            bool timedOut = mode == RunMode.UntilOutcome && !env.Sim.Outcome.IsFinished;
            return collector.Build(env.Sim.Outcome, env.Sim.CurrentTick, timedOut);
        }

        /// <summary>Тиков из секунд по частоте сима.</summary>
        public static int TicksFromSeconds(float seconds) => (int)(seconds * SimConstants.TickRate);
    }
}
