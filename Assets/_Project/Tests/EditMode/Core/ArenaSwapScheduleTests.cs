using Guildmaster.Core.Arena;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Расписание цифрового перехода арены (журнал `docs/arena-swap-progress.md`, Ф1): три акта,
    /// разнобой клеток, ускорение хвоста. Чистый headless — ни сцены, ни тайлмап.
    /// </summary>
    public sealed class ArenaSwapScheduleTests
    {
        private const int Cols = 20;
        private const int Rows = 11;

        private static ArenaSwapSchedule Default() => new ArenaSwapSchedule(ArenaSwapShape.Default);

        [Test]
        public void BeforeStart_NothingMoved_AfterEnd_EverythingDone()
        {
            var s = Default();

            ArenaCellPhase at0 = s.Sample(0f, 3, 4);
            Assert.AreEqual(0f, at0.Digitize, "в нуле клетка ещё реальна");
            Assert.AreEqual(0f, at0.Load,     "в нуле текстура ещё старая");

            ArenaCellPhase at1 = s.Sample(1f, 3, 4);
            Assert.AreEqual(1f, at1.Digitize, "в конце акт 1 доигран");
            Assert.AreEqual(1f, at1.Load,     "в конце текстура новая");
            Assert.AreEqual(1f, at1.Restore,  "в конце цифра снята");
            Assert.AreEqual(0f, at1.Digital,  "в конце клетка снова реальна");
        }

        [Test]
        public void EveryActFinishesForEveryCell_NoStragglers()
        {
            var s = Default();
            ArenaSwapShape shape = s.Shape;

            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                Assert.AreEqual(1f, s.Sample(shape.DigitizeEnd,  x, y).Digitize, 1e-5f,
                                $"клетка ({x},{y}) не успела уйти в цифру к концу акта 1");
                Assert.AreEqual(1f, s.Sample(shape.RestoreStart, x, y).Load, 1e-5f,
                                $"клетка ({x},{y}) не догрузила текстуру к концу акта 2");
                Assert.AreEqual(1f, s.Sample(1f, x, y).Restore, 1e-5f,
                                $"клетка ({x},{y}) осталась в цифре после акта 3");
            }
        }

        [Test]
        public void MidLoad_CellsAreOutOfStep_SomeDoneSomeNot()
        {
            var s = Default();
            float mid = (s.Shape.DigitizeEnd + s.Shape.RestoreStart) * 0.5f;

            int done = 0, waiting = 0, running = 0;
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                float p = s.Sample(mid, x, y).Load;
                if (p <= 0f) waiting++;
                else if (p >= 1f) done++;
                else running++;
            }

            // Именно это Макс и просил: не волна и не общий фейд, а «везде сразу, но вразнобой».
            Assert.Greater(done,    0, "к середине акта часть клеток обязана быть готова");
            Assert.Greater(waiting, 0, "и часть — ещё не начата");
            Assert.Greater(running, 0, "и часть — в процессе");
        }

        [Test]
        public void ZeroSpread_MakesEveryCellMoveTogether()
        {
            var shape = new ArenaSwapShape(4.5f, 0.12f, 0.12f,
                                           cellSpread: 0f, cellDurationMin: 0.3f, cellDurationMax: 0.3f,
                                           tailAcceleration: 0f);
            var s = new ArenaSwapSchedule(shape);
            float mid = (shape.DigitizeEnd + shape.RestoreStart) * 0.5f;

            float first = s.Sample(mid, 0, 0).Load;
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                Assert.AreEqual(first, s.Sample(mid, x, y).Load, 1e-5f,
                                "без разброса клетки обязаны идти строем — это и есть вырожденный случай");
        }

        [Test]
        public void TailAcceleration_CrowdsTheFinishIntoTheLastThird()
        {
            var even = new ArenaSwapSchedule(new ArenaSwapShape(4.5f, 0.12f, 0.12f, 0.62f, 0.10f, 0.34f, 0f));
            var fast = new ArenaSwapSchedule(new ArenaSwapShape(4.5f, 0.12f, 0.12f, 0.62f, 0.10f, 0.34f, 1f));

            // Смысл ускорения — не «раньше готово», а нарастающий темп: чем ближе финал, тем гуще
            // догружаются клетки. Значит мерить надо ПРИРОСТ за последнюю треть акта, а не срез в точке.
            Assert.Greater(DoneInLastThird(fast), DoneInLastThird(even),
                           "с нарастающим темпом в последнюю треть акта должно попадать больше догрузок");
        }

        private static int DoneInLastThird(ArenaSwapSchedule s)
        {
            float lo = s.Shape.DigitizeEnd;
            float hi = s.Shape.RestoreStart;
            float atTwoThirds = lo + (hi - lo) * (2f / 3f);

            int doneBefore = 0, doneTotal = 0;
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (s.Sample(atTwoThirds, x, y).Load >= 1f) doneBefore++;
                if (s.Sample(hi, x, y).Load >= 1f) doneTotal++;
            }
            return doneTotal - doneBefore;
        }

        [Test]
        public void Digital_IsHighInTheMiddle_AndGoneAtTheEnd()
        {
            var s = Default();
            float mid = (s.Shape.DigitizeEnd + s.Shape.RestoreStart) * 0.5f;

            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                Assert.AreEqual(1f, s.Sample(mid, x, y).Digital, 1e-5f,
                                $"в середине перехода клетка ({x},{y}) обязана быть в цифре");

            Assert.AreEqual(0f, s.Sample(1f, 5, 5).Digital, "после акта 3 цифры не остаётся");
        }

        [Test]
        public void MipSteps_ClimbFromFlatPatchToFullResolution()
        {
            var s = Default();
            var seen = new System.Collections.Generic.List<int>();

            // Ведём одну клетку через весь акт 2 и смотрим, что ступени только растут: 1 → 2 → 4.
            int prev = 0;
            for (float t = s.Shape.DigitizeEnd; t <= s.Shape.RestoreStart; t += 0.002f)
            {
                ArenaCellPhase p = s.Sample(t, 7, 3);
                if (p.Load <= 0f || p.Load >= 1f) continue;
                int steps = p.MipSteps;
                Assert.GreaterOrEqual(steps, prev, "ступень разрешения не должна падать назад");
                if (steps != prev) seen.Add(steps);
                prev = steps;
            }

            CollectionAssert.AreEqual(new[] { 1, 2, 4 }, seen, "текстура обязана подгружаться ступенями 1-2-4");
        }

        [Test]
        public void CrossTime_MatchesTheMomentThePhaseActuallyTurns()
        {
            var s = Default();

            for (int y = 0; y < Rows; y += 3)
            for (int x = 0; x < Cols; x += 3)
            {
                float t = s.CrossTime(ArenaSwapAct.Load, x, y);

                Assert.IsFalse(s.Sample(t - 0.01f, x, y).ShowsTarget,
                               $"клетка ({x},{y}) не должна показывать новый тайл до своего момента");
                Assert.IsTrue(s.Sample(t + 0.01f, x, y).ShowsTarget,
                              $"клетка ({x},{y}) обязана перевернуться сразу после своего момента");
            }
        }

        [Test]
        public void CrossTime_StaysInsideItsOwnAct()
        {
            var s = Default();
            ArenaSwapShape shape = s.Shape;

            for (int y = 0; y < Rows; y += 4)
            for (int x = 0; x < Cols; x += 4)
            {
                Assert.That(s.CrossTime(ArenaSwapAct.Digitize, x, y),
                            Is.InRange(0f, shape.DigitizeEnd), "уход в каркас — только в первом акте");
                Assert.That(s.CrossTime(ArenaSwapAct.Load, x, y),
                            Is.InRange(shape.DigitizeEnd, shape.RestoreStart), "подмена тайла — только во втором");
                Assert.That(s.CrossTime(ArenaSwapAct.Restore, x, y),
                            Is.InRange(shape.RestoreStart, 1f), "возврат в реальность — только в третьем");
            }
        }

        [Test]
        public void Hash_IsStableAndSpreadAcrossCells()
        {
            Assert.AreEqual(ArenaSwapSchedule.Hash01(4, 9, 2), ArenaSwapSchedule.Hash01(4, 9, 2),
                            "хеш обязан быть детерминированным — на нём держится совпадение тайлов и каркаса");
            Assert.AreNotEqual(ArenaSwapSchedule.Hash01(4, 9, 2), ArenaSwapSchedule.Hash01(5, 9, 2),
                               "соседние клетки не должны совпадать по фазе");

            float sum = 0f;
            int n = 0;
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++) { sum += ArenaSwapSchedule.Hash01(x, y, 1); n++; }

            Assert.That(sum / n, Is.EqualTo(0.5f).Within(0.08f), "хеш должен ложиться ровно, без перекоса поля");
        }

        [Test]
        public void Shape_KeepsMiddleActDominant_EvenWhenSidesAreGreedy()
        {
            var shape = new ArenaSwapShape(4.5f, digitizeShare: 0.45f, restoreShare: 0.45f,
                                           cellSpread: 0.6f, cellDurationMin: 0.1f, cellDurationMax: 0.3f,
                                           tailAcceleration: 0.5f);

            float middle = shape.RestoreStart - shape.DigitizeEnd;
            Assert.Greater(middle, 0.39f, "акт подгрузки обязан оставаться самым длинным даже при жадных краях");
        }
    }
}
