using System.Collections.Generic;
using System.Text;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
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

            // Журнал попаданий и лечения за текущий тик: когда всё состояние совпадает, а HP уже врозь,
            // ответ даёт не слепок, а список того, что по этим HP ударило — и в каком порядке.
            var ledger = new List<string>();
            env.Sim.OnDamageDealt += (src, dst, res) => ledger.Add(
                $"    урон {res.TotalDamage,8:0.0} ({res.HpDamage:0.0} по HP) " +
                $"{Name(tracked, src)} → {Name(tracked, dst)}");
            env.Sim.OnHealed += (src, dst, amount) => ledger.Add(
                $"    хил  {amount,8:0.0}            {Name(tracked, src)} → {Name(tracked, dst)}");

            for (int tick = 0; tick < 240 * 30; tick++)
            {
                ledger.Clear();
                env.Sim.Tick(SimConstants.TickDelta);

                for (int i = 0; i < half; i++)
                {
                    RuntimeUnit left = tracked[i].Unit;
                    RuntimeUnit right = tracked[i + half].Unit;

                    string apart = FirstDifference(left, right);
                    if (apart == null) continue;

                    sb.AppendLine($"Тик {tick} ({tick / 30f:0.00} с) — разошлась пара «{tracked[i].Label}»: {apart}");
                    for (int k = 0; k < half; k++)
                    {
                        RuntimeUnit l = tracked[k].Unit;
                        RuntimeUnit r = tracked[k + half].Unit;
                        sb.AppendLine($"  {tracked[k].Label,-16} L {State(tracked, l)}");
                        sb.AppendLine($"  {"",-16} R {State(tracked, r)}");
                    }

                    sb.AppendLine($"  Что прилетело на этом тике ({ledger.Count} событий):");
                    for (int e = 0; e < ledger.Count; e++) sb.AppendLine(ledger[e]);

                    Assert.Fail(sb.ToString());
                }
            }

            Assert.Pass("Зеркало не разошлось за весь бой");
        }

        /// <summary>
        /// Первое расхождение отражённой пары словами, либо null — пара зеркальна.
        /// </summary>
        /// <remarks>
        /// Сравнивать одни HP и позиции мало: расхождение рождается раньше, чем доходит до них. Крио,
        /// успевший наложить стан на тик раньше зеркала, ломает бой сразу — но HP разъедутся только когда
        /// застаненная сторона недоберёт удары, а это сотни тиков спустя, и след причины уже простыл.
        /// Поэтому сверяется весь наблюдаемый слепок: ресурс, щит, кулдауны, фаза свинга и полный набор
        /// эффектов со стаками и остатком.
        /// </remarks>
        private static string FirstDifference(RuntimeUnit l, RuntimeUnit r)
        {
            if (!Mathf.Approximately(l.CurrentHP, r.CurrentHP))             return "HP";
            if (!Mathf.Approximately(l.Position.x, -r.Position.x))          return "позиция X (не отражена)";
            if (!Mathf.Approximately(l.Position.y, r.Position.y))           return "позиция Y";
            if (!Mathf.Approximately(l.CurrentShield, r.CurrentShield))     return "щит";
            if (!Mathf.Approximately(l.CurrentResource, r.CurrentResource)) return "ресурс";
            if (l.AttackCooldownTicks != r.AttackCooldownTicks)             return "кулдаун атаки";
            if (l.WindupRemaining != r.WindupRemaining)                     return "остаток замаха";
            if (l.Phase != r.Phase)                                         return $"фаза свинга ({l.Phase} против {r.Phase})";

            for (int a = 0; a < l.Abilities.Count && a < r.Abilities.Count; a++)
                if (!Mathf.Approximately(l.Abilities[a].CooldownRemaining, r.Abilities[a].CooldownRemaining))
                    return $"кулдаун способности {a}";

            if (l.ActiveEffects.Count != r.ActiveEffects.Count) return "число эффектов";
            for (int e = 0; e < l.ActiveEffects.Count; e++)
            {
                RuntimeEffect le = l.ActiveEffects[e], re = r.ActiveEffects[e];
                string lid = le.Def != null ? le.Def.Id : "?", rid = re.Def != null ? re.Def.Id : "?";
                if (lid != rid)                             return $"эффект {e}: «{lid}» против «{rid}»";
                if (le.RemainingTicks != re.RemainingTicks) return $"остаток эффекта «{lid}»";
                if (le.Stacks != re.Stacks)                 return $"стаки эффекта «{lid}»";
            }

            return null;
        }

        /// <summary>
        /// Полное состояние бойца одной строкой. Позиции зеркало держит с первой правки, поэтому расходится
        /// теперь то, что позади них: HP, щит, ресурс, фаза свинга, кулдауны способностей и эффекты со стаками.
        /// </summary>
        private static string State(List<TrackedUnit> tracked, RuntimeUnit u)
        {
            var sb = new StringBuilder();
            sb.Append($"hp={u.CurrentHP,8:0.0} sh={u.CurrentShield,6:0.0} res={u.CurrentResource,5:0.0} ");
            sb.Append($"pos=({u.Position.x,6:0.00};{u.Position.y,5:0.00}) tgt={Name(tracked, u.CurrentTarget),-18} ");
            sb.Append($"phase={u.Phase,-9} cd={u.AttackCooldownTicks,3} wind={u.WindupRemaining,3} ");

            for (int a = 0; a < u.Abilities.Count; a++)
                sb.Append($"[ab{a} cd={u.Abilities[a].CooldownRemaining:0.00}] ");

            for (int e = 0; e < u.ActiveEffects.Count; e++)
            {
                RuntimeEffect eff = u.ActiveEffects[e];
                sb.Append($"[{(eff.Def != null ? eff.Def.Id : "?")} t={eff.RemainingTicks} x{eff.Stacks} " +
                          $"src={(eff.Source != null ? Name(tracked, eff.Source) : "—")}] ");
            }

            return sb.ToString();
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
