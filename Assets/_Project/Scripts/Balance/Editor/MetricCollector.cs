using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;

namespace Guildmaster.Balance.Editor
{
    /// <summary>Метрики одного юнита за бой (агрегат по outward-событиям сима).</summary>
    internal sealed class UnitMetric
    {
        public int Id;
        public string Label;
        public string Archetype;
        public int Team;

        public double DamageDealt;
        public double DamageTaken;
        public double ShieldAbsorbed;
        public double Overkill;
        public double HealingDone;

        public bool Died;
        public int DeathTick = -1;
    }

    /// <summary>Итог одного боя: исход, длительность, timeout и метрики отслеживаемых юнитов.</summary>
    internal sealed class BattleReport
    {
        public BattleOutcome Outcome;
        public int DurationTicks;
        public bool TimedOut;
        public readonly List<UnitMetric> Units = new List<UnitMetric>();

        public double Seconds => DurationTicks / (double)SimConstants.TickRate;

        public UnitMetric Find(int id)
        {
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].Id == id) return Units[i];
            return null;
        }
    }

    /// <summary>
    /// Собирает боевые метрики, подписавшись на outward C#-события сима (<see cref="CombatSimulation.OnDamageDealt"/>
    /// и др.) — тот самый развязанный от презентации шов. НЕ пересчитывает формулы урона (значения берём
    /// из фактических <see cref="DamageResult"/>), чтобы «таблица не врала». Живёт ровно на время боя вместе с симом.
    /// </summary>
    internal sealed class MetricCollector
    {
        private readonly CombatSimulation _sim;
        private readonly Dictionary<int, UnitMetric> _byId = new Dictionary<int, UnitMetric>();

        public MetricCollector(CombatSimulation sim, IReadOnlyList<TrackedUnit> tracked)
        {
            _sim = sim;
            for (int i = 0; i < tracked.Count; i++)
            {
                RuntimeUnit u = tracked[i].Unit;
                _byId[u.Id] = new UnitMetric
                {
                    Id = u.Id,
                    Label = tracked[i].Label,
                    Archetype = tracked[i].Archetype,
                    Team = u.Team,
                };
            }

            sim.OnDamageDealt += HandleDamage;
            sim.OnHealed += HandleHeal;
            sim.OnUnitDied += HandleDeath;
        }

        private void HandleDamage(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            if (source != null && _byId.TryGetValue(source.Id, out UnitMetric sm))
                sm.DamageDealt += result.TotalDamage;

            if (target != null && _byId.TryGetValue(target.Id, out UnitMetric tm))
            {
                tm.DamageTaken += result.TotalDamage;
                tm.ShieldAbsorbed += result.ShieldDamage;
                // Оверкилл — урон сверх остатка HP: после смертельного удара CurrentHP уходит в минус.
                if (result.KilledTarget && target.CurrentHP < 0f)
                    tm.Overkill += -target.CurrentHP;
            }
        }

        private void HandleHeal(RuntimeUnit source, RuntimeUnit target, float amount)
        {
            if (source != null && _byId.TryGetValue(source.Id, out UnitMetric sm))
                sm.HealingDone += amount;
        }

        private void HandleDeath(RuntimeUnit unit)
        {
            if (unit != null && _byId.TryGetValue(unit.Id, out UnitMetric m) && !m.Died)
            {
                m.Died = true;
                m.DeathTick = _sim.CurrentTick;
            }
        }

        public BattleReport Build(BattleOutcome outcome, int durationTicks, bool timedOut)
        {
            var report = new BattleReport
            {
                Outcome = outcome,
                DurationTicks = durationTicks,
                TimedOut = timedOut,
            };
            foreach (UnitMetric m in _byId.Values) report.Units.Add(m);
            report.Units.Sort((a, b) => a.Id.CompareTo(b.Id));
            return report;
        }
    }
}
