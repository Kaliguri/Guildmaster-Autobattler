using Guildmaster.Presentation.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Зона удара: досягаемость владеет распределением (ГД-журнал <c>2026-08-06/7</c>).
    /// Тесты держат то, что комментарием не удержишь, — поведение на границах и статистику выбора.
    /// </summary>
    /// <remarks>
    /// Чистая математика без сцены: солвер не трогает движок, поэтому проверки идут быстрым прогоном.
    /// Фигура во всех тестах вертикальная, ноги в нуле: голова 1.55, корпус 1.05, ноги 0.48 — это доли
    /// 0.90 / 0.62 / 0.28 от роста 1.7, те же, что стоят фолбэком в <c>UnitView</c>.
    /// </remarks>
    [TestFixture]
    public sealed class ImpactZoneTests
    {
        private const float Height = 1.7f;
        private const float HeadWeight = 0.05f;
        private const float BodyWeight = 0.80f;
        private const float LegsWeight = 0.15f;

        /// <summary>Зоны обычного бойца ростом 1.7, стоящего в <paramref name="x"/> с ногами на нуле.</summary>
        private static ImpactZoneSample[] HumanZones(float x = 0f, float scale = 1f)
        {
            float h = Height * scale;
            return new[]
            {
                new ImpactZoneSample(new Vector2(x, 0.90f * h), 0.09f * h, HeadWeight),
                new ImpactZoneSample(new Vector2(x, 0.62f * h), 0.20f * h, BodyWeight),
                new ImpactZoneSample(new Vector2(x, 0.28f * h), 0.14f * h, LegsWeight),
            };
        }

        /// <summary>Сколько раз за <paramref name="rolls"/> сидов выпала каждая зона.</summary>
        private static int[] ZoneHistogram(
            Vector2 attacker, Vector2 targetCentre, float reach, ImpactZoneSample[] zones, int rolls = 4000)
        {
            var counts = new int[zones.Length];
            for (uint s = 1; s <= rolls; s++)
            {
                var r = ImpactZoneSolver.Solve(attacker, targetCentre, reach, zones, 0.6f, s * 2654435761u);
                counts[r.ZoneIndex]++;
            }
            return counts;
        }

        // --- Досягаемость решает, а не украшает ------------------------------------------------------

        /// <summary>
        /// Мечник, достающий великану только до ног, бьёт по ногам. Ради этого случая модель и переписана:
        /// раньше точка бралась разбросом вокруг груди и в такой ситуации висела бы в воздухе у пояса.
        /// </summary>
        [Test]
        public void Swordsman_ReachingOnlyGiantsLegs_HitsLegs()
        {
            // Великан втрое выше: корпус на 3.16, ноги на 1.43. Мечник бьёт из своего корпуса (1.05).
            var giant = HumanZones(x: 2.2f, scale: 3f);
            var swordsman = new Vector2(0f, 0.62f * Height);

            // Круга хватает только до ног: до корпуса 3.05 при радиусе зоны 1.02 — не дотянуться вовсе.
            var counts = ZoneHistogram(swordsman, new Vector2(2.2f, 0f), reach: 1.9f, zones: giant);

            Assert.That(counts[(int)ImpactZoneKind.Head], Is.Zero,
                "до головы великана мечник не дотягивается — она обязана выпасть из розыгрыша полностью");
            Assert.That(counts[(int)ImpactZoneKind.Body], Is.Zero,
                "до корпуса великана мечник тоже не дотягивается");
            Assert.That(counts[(int)ImpactZoneKind.Legs], Is.GreaterThan(0),
                "остались одни ноги — по ним и бьём");
        }

        /// <summary>
        /// ЧАСТИЧНО накрытая зона не проигрывает автоматически: вес умножается на накрытие линейно,
        /// поэтому корпус, доступный на четверть, всё ещё бьётся чаще ног, доступных на три четверти —
        /// шестнадцатикратная разница заявленных весов это перевешивает.
        /// <para>
        /// Тест фиксирует ИМЕННО ЭТО поведение, потому что оно неочевидно и легко «чинится» кем-то, кто
        /// ждал обратного. Если решим, что достижимость должна решать резче, меняется формула в
        /// <see cref="ImpactZoneSolver"/> — и этот тест обязан упасть, а не промолчать.
        /// </para>
        /// </summary>
        [Test]
        public void PartiallyReachableBody_StillOutweighsWellReachableLegs()
        {
            var giant = HumanZones(x: 2.2f, scale: 3f);
            var counts = ZoneHistogram(new Vector2(0f, 0.62f * Height), new Vector2(2.2f, 0f), 2.6f, giant);

            Assert.That(counts[(int)ImpactZoneKind.Body], Is.GreaterThan(counts[(int)ImpactZoneKind.Legs]),
                "линейный множитель сохраняет перевес базового веса — это решение, а не случайность");
        }

        /// <summary>
        /// При полном накрытии распределение сходится к ЗАЯВЛЕННЫМ весам. Иначе «80/15/5» в конфиге —
        /// просто три числа, которые ни на что не отвечают.
        /// </summary>
        [Test]
        public void FullyReachableTarget_FollowsDeclaredWeights()
        {
            var zones = HumanZones(x: 0.8f);
            var counts = ZoneHistogram(new Vector2(0f, 1.05f), new Vector2(0.8f, 0f), reach: 50f, zones);

            int total = counts[0] + counts[1] + counts[2];
            Assert.That(counts[(int)ImpactZoneKind.Head] / (float)total, Is.EqualTo(HeadWeight).Within(0.02f));
            Assert.That(counts[(int)ImpactZoneKind.Body] / (float)total, Is.EqualTo(BodyWeight).Within(0.03f));
            Assert.That(counts[(int)ImpactZoneKind.Legs] / (float)total, Is.EqualTo(LegsWeight).Within(0.03f));
        }

        /// <summary>
        /// Удар, засчитанный боем, находит зону: круг показа строится той же формулой, что боевая
        /// досягаемость, поэтому вырожденный ответ означает поломку, а не тесную расстановку.
        /// </summary>
        [Test]
        public void HitAcceptedByCombat_IsNeverDegenerate()
        {
            var zones = HumanZones(x: 1.9f);
            // Бой засчитывает удар на дистанции центров ≤ AttackRange + r_self + r_target.
            const float attackRange = 1.6f, rSelf = 0.3f, rTarget = 0.3f;
            var attacker = new Vector2(0f, 0.62f * Height);

            var result = ImpactZoneSolver.Solve(
                attacker, new Vector2(1.9f, 0f), attackRange + rSelf + rTarget, zones, 0.6f, 12345u);

            Assert.That(result.Degenerate, Is.False,
                "показ обязан найти зону там, где бой засчитал удар — иначе формулы разъехались");
        }

        // --- Законы, а не вероятности ----------------------------------------------------------------

        /// <summary>
        /// Дальний край силуэта не получает ударов НИКОГДА — ни от мечника, ни от стрелка. Стрела,
        /// вошедшая в спину при выстреле в лицо, читается как баг, а не как разнообразие.
        /// </summary>
        [Test]
        public void FarSideOfSilhouette_IsNeverHit()
        {
            var zones = HumanZones(x: 3f);
            var target = new Vector2(3f, 0f);
            var attacker = new Vector2(0f, 1.05f);   // слева от цели

            for (uint s = 1; s <= 3000; s++)
            {
                var r = ImpactZoneSolver.Solve(attacker, target, 50f, zones, 0.6f, s * 2654435761u);
                Assert.That(r.Point.x, Is.LessThanOrEqualTo(target.x + 1e-3f),
                    $"сид {s}: удар слева пришёл за дальний край силуэта (x={r.Point.x:F3} > {target.x:F3})");
            }
        }

        /// <summary>Точка удара не выходит за круг атаки: достижимость — закон, а не только вес.</summary>
        [Test]
        public void Point_NeverLeavesAttackCircle()
        {
            var zones = HumanZones(x: 1.6f);
            var attacker = new Vector2(0f, 1.05f);
            const float reach = 1.8f;

            for (uint s = 1; s <= 3000; s++)
            {
                var r = ImpactZoneSolver.Solve(attacker, new Vector2(1.6f, 0f), reach, zones, 0.6f, s * 40503u);
                Assert.That(Vector2.Distance(attacker, r.Point), Is.LessThanOrEqualTo(reach + 1e-3f),
                    $"сид {s}: точка удара оказалась дальше круга атаки");
            }
        }

        /// <summary>
        /// Один и тот же удар решается одинаково. В кооперативе сид общий, поэтому это и есть условие
        /// того, что оба игрока увидят вспышку в одном месте.
        /// </summary>
        [Test]
        public void SameSeed_GivesSameResult()
        {
            var zones = HumanZones(x: 1.2f);
            var a = ImpactZoneSolver.Solve(Vector2.zero, new Vector2(1.2f, 0f), 3f, zones, 0.6f, 987654321u);
            var b = ImpactZoneSolver.Solve(Vector2.zero, new Vector2(1.2f, 0f), 3f, zones, 0.6f, 987654321u);

            Assert.That(b.ZoneIndex, Is.EqualTo(a.ZoneIndex));
            Assert.That(b.Point, Is.EqualTo(a.Point));
        }

        /// <summary>
        /// Совсем недостижимая цель помечается вырожденной, а не выбирается молча. Именно на этот флаг
        /// презентер ГРОМКО ругается: тихо выбранная «наименее плохая» зона скрыла бы разъехавшиеся якоря.
        /// </summary>
        [Test]
        public void UnreachableTarget_IsFlaggedDegenerate()
        {
            var zones = HumanZones(x: 40f);
            var result = ImpactZoneSolver.Solve(Vector2.zero, new Vector2(40f, 0f), 1.5f, zones, 0.6f, 7u);

            Assert.That(result.Degenerate, Is.True);
            Assert.That(result.ZoneIndex, Is.InRange(0, zones.Length - 1),
                "даже в вырожденном случае точка обязана быть — показ не имеет права остаться без вспышки");
        }

        /// <summary>
        /// Зона — это площадь, а не точка: восемь бойцов вокруг цели не должны давать восемь одинаковых
        /// вспышек в одном пикселе. Ради этого модель и завелась.
        /// </summary>
        [Test]
        public void PointsWithinZone_AreSpread()
        {
            var zones = HumanZones(x: 1f);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (uint s = 1; s <= 200; s++)
            {
                var r = ImpactZoneSolver.Solve(new Vector2(0f, 1.05f), new Vector2(1f, 0f), 50f, zones, 0.6f, s * 7919u);
                seen.Add((Mathf.RoundToInt(r.Point.x * 50f), Mathf.RoundToInt(r.Point.y * 50f)));
            }

            Assert.That(seen.Count, Is.GreaterThan(100),
                "точки удара сбились в кучу — разброс внутри зоны потерялся");
        }
    }
}
