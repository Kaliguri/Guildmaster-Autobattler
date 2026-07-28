using System.Collections.Generic;
using System.Text;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// ДИАГНОСТИКА BAL-014: ищет тик, на котором зеркало разошлось В МЛАДШИХ БИТАХ, и печатает СРАЗУ ВСЕ
    /// разошедшиеся поля — чтобы отделить причину от следствия.
    /// </summary>
    /// <remarks>
    /// Отличие от <see cref="MirrorFixture"/>: тот судит через <c>Mathf.Approximately</c> и потому видит
    /// расхождение уже подросшим до эпсилона (тик 116), а родиться оно могло сотнями тиков раньше. Здесь
    /// сравнение ТОЧНОЕ (<c>==</c>), поэтому виден тик рождения. И печатается не первое поле по списку, а
    /// весь набор: первое по списку — не обязательно первопричина.
    /// </remarks>
    [Explicit("Диагностика BAL-014: запускать руками")]
    public sealed class MirrorBitProbe
    {
        /// <summary>
        /// <paramref name="separation"/> = false выключает расталкивание: если при этом точное расхождение
        /// исчезает, источник именно в нём, и это уже не гипотеза, а замер.
        /// </summary>
        [Test]
        public void FindBirthTickOfDivergence([Values(true, false)] bool separation)
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            for (int i = 0; i < 4 && i < relics.Count; i++) squad.Add(relics[i]);

            var names = new List<string>();
            foreach (RelicData r in squad) names.Add(r.name);

            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, Lineups.Squad);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, Lineups.Squad);

            for (int i = 0; i < tracked.Count; i++) tracked[i].Unit.Id = i;
            for (int i = 0; i < tracked.Count; i++) env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            if (!separation) env.Sim.Separation.Iterations = 0;

            int half = tracked.Count / 2;
            var sb = new StringBuilder();
            sb.AppendLine($"Отряд [{string.Join(", ", names)}], строй Squad, пар: {half}, " +
                          $"расталкивание: {(separation ? "включено" : "ВЫКЛЮЧЕНО")}");

            for (int tick = 0; tick < 400; tick++)
            {
                env.Sim.Tick(SimConstants.TickDelta);

                var diffs = new List<string>();
                for (int i = 0; i < half; i++)
                    Collect(tracked, i, half, diffs);

                if (diffs.Count == 0) continue;

                sb.AppendLine($"ПЕРВОЕ ТОЧНОЕ РАСХОЖДЕНИЕ на тике {tick} ({tick / 30f:0.000} с):");
                foreach (string d in diffs) sb.AppendLine("  " + d);
                Assert.Fail(sb.ToString());
            }

            sb.AppendLine("За 400 тиков точного расхождения нет.");
            Assert.Pass(sb.ToString());
        }

        /// <summary>
        /// Держится ли зеркало ТОЧНО весь бой и на всём ростере, а не только в окне диагностики.
        /// Отвечает на вопрос «починено или отодвинуто»: сторож судит с допуском и потому молчит про
        /// дрейф в младших битах, а он и есть зерно расхождения. Ползёт по тем же окнам, что
        /// <c>MirrorMatchTests.Mirror_SquadSeries_NeverDiverges</c>, но на полную дистанцию боя.
        /// </summary>
        [Test]
        public void HoldsExactlyForFullBattle([ValueSource(nameof(SquadWindows))] int start)
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            for (int i = 0; i < 4 && start + i < relics.Count; i++) squad.Add(relics[start + i]);

            var names = new List<string>();
            foreach (RelicData r in squad) names.Add(r.name);

            int tick = FirstExactDivergence(squad, MirrorFixture.FullBattleTicks, out string what);

            if (tick >= 0)
                Assert.Fail($"Отряд [{string.Join(", ", names)}] разошёлся ТОЧНО на тике {tick} " +
                            $"({tick / 30f:0.00} с):\n{what}");

            Assert.Pass($"Отряд [{string.Join(", ", names)}]: зеркало точно весь бой " +
                        $"({MirrorFixture.FullBattleTicks} тиков).");
        }

        private static IEnumerable<int> SquadWindows()
        {
            int count = BalanceAssets.LoadRelics().Count;
            var starts = new List<int>();
            for (int i = 0; i + 4 <= count; i++) starts.Add(i);
            if (starts.Count == 0) starts.Add(0);
            return starts;
        }

        /// <summary>Прогнать зеркальный бой и вернуть первый тик ТОЧНОГО расхождения, либо -1.</summary>
        private static int FirstExactDivergence(IReadOnlyList<RelicData> squad, int capTicks, out string what)
        {
            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, Lineups.Squad);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, Lineups.Squad);
            for (int i = 0; i < tracked.Count; i++) tracked[i].Unit.Id = i;
            for (int i = 0; i < tracked.Count; i++) env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            int half = tracked.Count / 2;
            for (int tick = 0; tick < capTicks; tick++)
            {
                env.Sim.Tick(SimConstants.TickDelta);

                var diffs = new List<string>();
                for (int i = 0; i < half; i++) Collect(tracked, i, half, diffs);
                if (diffs.Count == 0) continue;

                what = string.Join("\n", diffs);
                return tick;
            }

            what = null;
            return -1;
        }

        /// <summary>Собрать ВСЕ точные отличия отражённой пары, а не первое по списку.</summary>
        private static void Collect(List<TrackedUnit> tracked, int i, int half, List<string> diffs)
        {
            RuntimeUnit l = tracked[i].Unit, r = tracked[i + half].Unit;
            string who = tracked[i].Label;

            // Позиция: X обязан быть отражён, Y — совпасть. Точное сравнение значений, не битов:
            // -0.0 и +0.0 равны как числа, но различаются битами, и это не расхождение.
            if (l.Position.x != -r.Position.x)
                diffs.Add($"{who}: X {Hex(l.Position.x)} против отражённого {Hex(-r.Position.x)} " +
                          $"(дельта {l.Position.x + r.Position.x:E3})");
            if (l.Position.y != r.Position.y)
                diffs.Add($"{who}: Y {Hex(l.Position.y)} против {Hex(r.Position.y)} " +
                          $"(дельта {l.Position.y - r.Position.y:E3})");

            if (l.CurrentHP != r.CurrentHP)
                diffs.Add($"{who}: HP {Hex(l.CurrentHP)} против {Hex(r.CurrentHP)}");
            if (l.CurrentShield != r.CurrentShield)
                diffs.Add($"{who}: щит {Hex(l.CurrentShield)} против {Hex(r.CurrentShield)}");
            if (l.CurrentResource != r.CurrentResource)
                diffs.Add($"{who}: ресурс {Hex(l.CurrentResource)} против {Hex(r.CurrentResource)}");

            if (l.AttackCooldownTicks != r.AttackCooldownTicks)
                diffs.Add($"{who}: кулдаун атаки {l.AttackCooldownTicks} против {r.AttackCooldownTicks}");
            if (l.WindupRemaining != r.WindupRemaining)
                diffs.Add($"{who}: замах {l.WindupRemaining} против {r.WindupRemaining}");
            if (l.Phase != r.Phase)
                diffs.Add($"{who}: фаза {l.Phase} против {r.Phase}");
            if (l.Positioning != r.Positioning)
                diffs.Add($"{who}: интент {l.Positioning} против {r.Positioning}");

            // Цель: зеркальна, если это один и тот же НОМЕР В КОМАНДЕ. Глобальные Id у сторон разные,
            // поэтому сравниваем приведённый к команде индекс, а не Id.
            int lt = TeamIndex(tracked, l.CurrentTarget, half), rt = TeamIndex(tracked, r.CurrentTarget, half);
            if (lt != rt)
                diffs.Add($"{who}: ЦЕЛЬ не зеркальна — {Name(tracked, l.CurrentTarget)} против {Name(tracked, r.CurrentTarget)}");

            if (l.ActiveEffects.Count != r.ActiveEffects.Count)
                diffs.Add($"{who}: число эффектов {l.ActiveEffects.Count} против {r.ActiveEffects.Count}");
        }

        /// <summary>Номер юнита внутри своей команды — единственная зеркально-инвариантная его примета.</summary>
        private static int TeamIndex(List<TrackedUnit> tracked, RuntimeUnit u, int half)
        {
            if (u == null) return -1;
            for (int i = 0; i < tracked.Count; i++)
                if (ReferenceEquals(tracked[i].Unit, u))
                    return i % half;
            return -2;
        }

        private static string Name(List<TrackedUnit> tracked, RuntimeUnit u)
        {
            if (u == null) return "—";
            for (int i = 0; i < tracked.Count; i++)
                if (ReferenceEquals(tracked[i].Unit, u))
                    return (u.Team == 0 ? "L:" : "R:") + tracked[i].Label;
            return "?";
        }

        /// <summary>Значение и его биты: расхождение в младших битах по десятичной записи не видно.</summary>
        private static string Hex(float v) => $"{v:0.000000} (0x{System.BitConverter.SingleToInt32Bits(v):x8})";
    }
}
