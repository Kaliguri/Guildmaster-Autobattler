using System.Collections.Generic;
using System.Text;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Общий стенд зеркального боя: разворачивает отряд против его собственного отражения, тикает вручную
    /// и на каждом тике сверяет отражённые пары целиком. Единственное место, где записано, что значит
    /// «стороны сошлись» — сторож (<see cref="MirrorMatchTests"/>) и диагностический зонд
    /// (<see cref="MirrorDivergenceProbe"/>) обязаны судить по одной линейке, иначе сторож зеленеет там,
    /// где зонд показывает расхождение.
    /// </summary>
    /// <remarks>
    /// Зеркало = отражение по оси X: у отражённой пары X противоположен, Y совпадает, всё остальное
    /// (HP, щит, ресурс, кулдауны, фаза свинга, эффекты со стаками) обязано быть равным ТИК В ТИК.
    /// Сравнивать один исход боя мало: расхождение рождается задолго до того, как доедет до счёта, и
    /// к моменту, когда его видно по HP, причина уже далеко позади.
    /// </remarks>
    internal static class MirrorFixture
    {
        /// <summary>Потолок зеркального боя по умолчанию, тиков (240 с при 30 Гц) — как у бенчей.</summary>
        public const int FullBattleTicks = 240 * 30;

        /// <summary>
        /// Прогнать зеркальный бой и вернуть номер первого тика, на котором стороны разошлись,
        /// либо -1, если отражение выстояло весь бой.
        /// </summary>
        /// <param name="squad">Киты испытуемой команды; вторая команда — её точное отражение.</param>
        /// <param name="lineup">Строй (свободные слоты закрывают эталонные манекены).</param>
        /// <param name="capTicks">Потолок боя в тиках.</param>
        /// <param name="report">Разбор расхождения: что именно разошлось, слепок обеих сторон и удары тика.</param>
        public static int FirstDivergingTick(
            IReadOnlyList<RelicData> squad, Slot[] lineup, int capTicks, out string report)
        {
            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, lineup);

            for (int i = 0; i < tracked.Count; i++) tracked[i].Unit.Id = i;
            for (int i = 0; i < tracked.Count; i++) env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            int half = tracked.Count / 2;

            // Журнал попаданий и лечения за текущий тик: когда весь слепок совпадает, а HP уже врозь,
            // ответ даёт не состояние, а список того, что по этим HP ударило — и в каком порядке.
            var ledger = new List<string>();
            env.Sim.OnDamageDealt += (src, dst, res) => ledger.Add(
                $"    урон {res.TotalDamage,8:0.0} ({res.HpDamage:0.0} по HP) " +
                $"{Name(tracked, src)} → {Name(tracked, dst)}");
            env.Sim.OnHealed += (src, dst, amount) => ledger.Add(
                $"    хил  {amount,8:0.0}            {Name(tracked, src)} → {Name(tracked, dst)}");

            for (int tick = 0; tick < capTicks; tick++)
            {
                ledger.Clear();
                env.Sim.Tick(SimConstants.TickDelta);

                for (int i = 0; i < half; i++)
                {
                    string apart = FirstDifference(tracked[i].Unit, tracked[i + half].Unit);
                    if (apart == null) continue;

                    report = Describe(tracked, half, tick, i, apart, ledger);
                    return tick;
                }
            }

            report = null;
            return -1;
        }

        /// <summary>Разбор расхождения: заголовок, слепок обеих сторон построчно и удары этого тика.</summary>
        private static string Describe(List<TrackedUnit> tracked, int half, int tick, int pair,
                                       string apart, List<string> ledger)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Тик {tick} ({tick / 30f:0.00} с) — разошлась пара «{tracked[pair].Label}»: {apart}");

            for (int k = 0; k < half; k++)
            {
                sb.AppendLine($"  {tracked[k].Label,-16} L {State(tracked, tracked[k].Unit)}");
                sb.AppendLine($"  {"",-16} R {State(tracked, tracked[k + half].Unit)}");
            }

            sb.AppendLine($"  Что прилетело на этом тике ({ledger.Count} событий):");
            for (int e = 0; e < ledger.Count; e++) sb.AppendLine(ledger[e]);
            return sb.ToString();
        }

        /// <summary>
        /// Первое расхождение отражённой пары словами, либо null — пара зеркальна.
        /// </summary>
        /// <remarks>
        /// Сравнивать одни HP и позиции мало: расхождение рождается раньше, чем доходит до них. Криомант,
        /// успевший наложить стан на тик раньше зеркала, ломает бой сразу — но HP разъедутся только когда
        /// застаненная сторона недоберёт удары, а это сотни тиков спустя.
        /// </remarks>
        public static string FirstDifference(RuntimeUnit l, RuntimeUnit r)
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

        /// <summary>Полное состояние бойца одной строкой: HP, щит, ресурс, позиция, цель, свинг, кулдауны, эффекты.</summary>
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
