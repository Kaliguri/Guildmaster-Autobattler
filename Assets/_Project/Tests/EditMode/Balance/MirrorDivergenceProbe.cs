using System.Collections.Generic;
using System.Text;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Диагностический зонд: тикает зеркальный бой вручную и ловит ПЕРВЫЙ тик, на котором отражённые друг
    /// друга бойцы разошлись. Не сторож качества — инструмент для поиска причины, оставлен намеренно:
    /// если перекос стороны вернётся, этот тест покажет не «плохо», а где именно.
    /// </summary>
    [Explicit("Диагностика: запускать руками, когда MirrorMatchTests краснеет")]
    public sealed class MirrorDivergenceProbe
    {
        [Test]
        public void FindFirstDivergingTick()
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            foreach (string name in new[] { "Defender", "FlameSwordsman", "Cryomancer", "LightShepherd" })
                squad.Add(relics.Find(r => r.name == name));

            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, Lineups.Squad);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, Lineups.Squad);

            for (int i = 0; i < tracked.Count; i++) tracked[i].Unit.Id = i;
            for (int i = 0; i < tracked.Count; i++) env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            int half = tracked.Count / 2;
            var sb = new StringBuilder();

            for (int tick = 0; tick < 240 * 30; tick++)
            {
                env.Sim.Tick(SimConstants.TickDelta);

                for (int i = 0; i < half; i++)
                {
                    RuntimeUnit left = tracked[i].Unit;
                    RuntimeUnit right = tracked[i + half].Unit;

                    bool hpApart = !Mathf.Approximately(left.CurrentHP, right.CurrentHP);
                    bool posApart = !Mathf.Approximately(left.Position.x, -right.Position.x)
                                    || !Mathf.Approximately(left.Position.y, right.Position.y);
                    if (!hpApart && !posApart) continue;

                    sb.AppendLine($"Тик {tick} ({tick / 30f:0.00} с) — разошлась пара «{tracked[i].Label}»:");
                    for (int k = 0; k < half; k++)
                    {
                        RuntimeUnit l = tracked[k].Unit;
                        RuntimeUnit r = tracked[k + half].Unit;
                        sb.AppendLine($"  {tracked[k].Label,-16} " +
                                      $"L hp={l.CurrentHP,8:0.0} pos=({l.Position.x,6:0.00};{l.Position.y,5:0.00}) tgt={Name(tracked, l.CurrentTarget)}  |  " +
                                      $"R hp={r.CurrentHP,8:0.0} pos=({r.Position.x,6:0.00};{r.Position.y,5:0.00}) tgt={Name(tracked, r.CurrentTarget)}");
                    }

                    Assert.Fail(sb.ToString());
                }
            }

            Assert.Pass("Зеркало не разошлось за весь бой");
        }

        private static string Name(List<TrackedUnit> tracked, RuntimeUnit unit)
        {
            if (unit == null) return "—";
            for (int i = 0; i < tracked.Count; i++)
                if (ReferenceEquals(tracked[i].Unit, unit))
                    return (unit.Team == 0 ? "L:" : "R:") + tracked[i].Label;
            return "?";
        }
    }
}
