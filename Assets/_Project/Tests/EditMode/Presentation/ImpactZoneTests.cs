using Guildmaster.Data.Definitions;
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

        /// <summary>Резкость по умолчанию — квадрат (решение Макса 06.08.2026).</summary>
        private const float Sharpness = 2f;

        /// <summary>
        /// Зоны обычного бойца ростом 1.7, стоящего в <paramref name="x"/> с ногами на нуле. Расчётный и
        /// живой центры здесь совпадают: расхождение между ними — предмет отдельного теста, а не фон.
        /// </summary>
        private static ImpactZoneSample[] HumanZones(float x = 0f, float scale = 1f)
        {
            float h = Height * scale;
            return new[]
            {
                Zone(new Vector2(x, 0.71f * h), 0.09f * h, HeadWeight),
                Zone(new Vector2(x, 0.56f * h), 0.19f * h, BodyWeight),
                Zone(new Vector2(x, 0.32f * h), 0.11f * h, LegsWeight),
            };
        }

        private static ImpactZoneSample Zone(Vector2 centre, float radius, float weight)
            => new ImpactZoneSample(centre, centre, radius, weight);

        /// <summary>Откуда бьёт боец ростом 1.7, стоящий в <paramref name="x"/>: с высоты корпуса.</summary>
        private static Vector2 AttackerAt(float x) => new Vector2(x, 0.56f * Height);

        /// <summary>Сколько раз за <paramref name="rolls"/> сидов выпала каждая зона.</summary>
        private static int[] ZoneHistogram(
            Vector2 attacker, Vector2 targetCentre, float reach, ImpactZoneSample[] zones, int rolls = 4000)
        {
            var counts = new int[zones.Length];
            for (uint s = 1; s <= rolls; s++)
            {
                var r = ImpactZoneSolver.Solve(
                    attacker, attacker, targetCentre, reach, zones, Sharpness, 0.6f, s * 2654435761u);
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
            // Великан втрое выше: корпус на 2.86, ноги на 1.63. Мечник бьёт из своего корпуса (0.95).
            var giant = HumanZones(x: 2.2f, scale: 3f);

            // Круга хватает только до ног: до корпуса далеко даже с учётом радиуса его зоны.
            var counts = ZoneHistogram(AttackerAt(0f), new Vector2(2.2f, 0f), reach: 1.9f, zones: giant);

            Assert.That(counts[(int)ImpactZoneKind.Head], Is.Zero,
                "до головы великана мечник не дотягивается — она обязана выпасть из розыгрыша полностью");
            Assert.That(counts[(int)ImpactZoneKind.Body], Is.Zero,
                "до корпуса великана мечник тоже не дотягивается");
            Assert.That(counts[(int)ImpactZoneKind.Legs], Is.GreaterThan(0),
                "остались одни ноги — по ним и бьём");
        }

        /// <summary>
        /// Резкость работает МОНОТОННО: чем она выше, тем больше ударов уходит в хорошо накрытые ноги и
        /// меньше — в еле накрытый корпус, хотя заявленный вес корпуса больше в шестнадцать раз.
        /// <para>
        /// Тест намеренно проверяет ручку, а не одну точку. Абсолютная точка перелома зависит от
        /// геометрии: в этой конфигурации квадрат уравнивает зоны примерно при 33% накрытия корпуса, и
        /// привязка теста к «ноги победили» сделала бы его хрупким к сдвигу радиуса на сантиметр. Ломается
        /// же тут другое — если кто-то вернёт линейность или перепутает знак степени, монотонность
        /// исчезнет, и тест это поймает.
        /// </para>
        /// </summary>
        [Test]
        public void HigherSharpness_ShiftsHitsFromBarelyReachedBodyToLegs()
        {
            var giant = HumanZones(x: 2.2f, scale: 3f);
            var attacker = AttackerAt(0f);
            var target = new Vector2(2.2f, 0f);

            System.Func<float, float> legsShare = sharpness =>
            {
                int body = 0, legs = 0;
                for (uint s = 1; s <= 4000; s++)
                {
                    var r = ImpactZoneSolver.Solve(
                        attacker, attacker, target, 2.6f, giant, sharpness, 0.6f, s * 2654435761u);
                    if (r.ZoneIndex == (int)ImpactZoneKind.Body) body++;
                    if (r.ZoneIndex == (int)ImpactZoneKind.Legs) legs++;
                }
                return legs / (float)(body + legs);
            };

            float linear = legsShare(1f);
            float squared = legsShare(2f);
            float cubed = legsShare(3f);

            Assert.That(squared, Is.GreaterThan(linear + 0.1f),
                "квадрат обязан заметно сдвинуть удары в достижимую зону по сравнению с линейной резкостью");
            Assert.That(cubed, Is.GreaterThan(squared),
                "куб обязан сдвинуть их ещё дальше — иначе ручка не монотонна");
            Assert.That(cubed, Is.GreaterThan(0.5f),
                "при кубе плохо накрытый корпус обязан ПРОИГРАТЬ ногам");
        }

        /// <summary>
        /// Выбор зоны считается ТОЛЬКО по расчётным центрам: сдвиг живого якоря (то есть поза, кадр
        /// анимации, разный FPS) не имеет права перебросить удар в другую часть тела.
        /// <para>
        /// Это условие того, что в кооперативе двое видят удар в одну зону. Свяжет кто-нибудь вес с
        /// живым якорем — тест упадёт здесь, а не всплывёт жалобой «у нас по-разному бьёт».
        /// </para>
        /// </summary>
        [Test]
        public void ZoneChoice_IgnoresLiveAnchorDrift()
        {
            float h = Height;
            var steady = HumanZones(x: 1.4f);
            // Тот же боец, но кости уехали: голова свесилась вниз, корпус подался вперёд, ноги согнулись.
            var drifted = new[]
            {
                new ImpactZoneSample(new Vector2(1.4f, 0.71f * h), new Vector2(1.25f, 0.60f * h), 0.09f * h, HeadWeight),
                new ImpactZoneSample(new Vector2(1.4f, 0.56f * h), new Vector2(1.30f, 0.52f * h), 0.19f * h, BodyWeight),
                new ImpactZoneSample(new Vector2(1.4f, 0.32f * h), new Vector2(1.48f, 0.26f * h), 0.11f * h, LegsWeight),
            };

            var attacker = AttackerAt(0f);
            for (uint s = 1; s <= 1500; s++)
            {
                uint seed = s * 2654435761u;
                var a = ImpactZoneSolver.Solve(attacker, attacker, new Vector2(1.4f, 0f), 2.4f, steady,  Sharpness, 0.6f, seed);
                var b = ImpactZoneSolver.Solve(attacker, attacker, new Vector2(1.4f, 0f), 2.4f, drifted, Sharpness, 0.6f, seed);
                Assert.That(b.ZoneIndex, Is.EqualTo(a.ZoneIndex),
                    $"сид {s}: поза сдвинула ВЫБОР зоны — в кооперативе это разные части тела у двух игроков");
            }
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
            var attacker = AttackerAt(0f);

            var result = ImpactZoneSolver.Solve(
                attacker, attacker, new Vector2(1.9f, 0f), attackRange + rSelf + rTarget, zones,
                Sharpness, 0.6f, 12345u);

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
            var attacker = AttackerAt(0f);   // слева от цели

            for (uint s = 1; s <= 3000; s++)
            {
                var r = ImpactZoneSolver.Solve(
                    attacker, attacker, target, 50f, zones, Sharpness, 0.6f, s * 2654435761u);
                Assert.That(r.Point.x, Is.LessThanOrEqualTo(target.x + 1e-3f),
                    $"сид {s}: удар слева пришёл за дальний край силуэта (x={r.Point.x:F3} > {target.x:F3})");
            }
        }

        /// <summary>Точка удара не выходит за круг атаки: достижимость — закон, а не только вес.</summary>
        [Test]
        public void Point_NeverLeavesAttackCircle()
        {
            var zones = HumanZones(x: 1.6f);
            var attacker = AttackerAt(0f);
            const float reach = 1.8f;

            for (uint s = 1; s <= 3000; s++)
            {
                var r = ImpactZoneSolver.Solve(
                    attacker, attacker, new Vector2(1.6f, 0f), reach, zones, Sharpness, 0.6f, s * 40503u);
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
            var at = AttackerAt(0f);
            var a = ImpactZoneSolver.Solve(at, at, new Vector2(1.2f, 0f), 3f, zones, Sharpness, 0.6f, 987654321u);
            var b = ImpactZoneSolver.Solve(at, at, new Vector2(1.2f, 0f), 3f, zones, Sharpness, 0.6f, 987654321u);

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
            var at = AttackerAt(0f);
            var result = ImpactZoneSolver.Solve(
                at, at, new Vector2(40f, 0f), 1.5f, zones, Sharpness, 0.6f, 7u);

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
                var at = AttackerAt(0f);
                var r = ImpactZoneSolver.Solve(
                    at, at, new Vector2(1f, 0f), 50f, zones, Sharpness, 0.6f, s * 7919u);
                seen.Add((Mathf.RoundToInt(r.Point.x * 50f), Mathf.RoundToInt(r.Point.y * 50f)));
            }

            Assert.That(seen.Count, Is.GreaterThan(100),
                "точки удара сбились в кучу — разброс внутри зоны потерялся");
        }

        // --- Круг показа равен кругу боя ------------------------------------------------------------

        /// <summary>
        /// Одиночная авто-атака достаёт ровно на свой круг: <see cref="ImpactReach"/> обязан вернуть его
        /// нетронутым, иначе показ начнёт спорить с боем на самом частом ударе в игре.
        /// </summary>
        [Test]
        public void SingleAutoAttack_KeepsSimulationReach()
        {
            Assert.That(ImpactReach.ForAutoAttack(2.6f, AreaShape.None, lengthMult: 2f, width: 2.25f),
                Is.EqualTo(2.6f).Within(1e-4f),
                "форма None — обычный удар: множитель длины к нему не относится вовсе");
        }

        /// <summary>
        /// Копейщик: бой бьёт полосой <c>Reach * AutoAttackLengthMult</c> и задевает в ней всех, поэтому
        /// круг показа обязан дотянуться до дальнего УГЛА полосы — до цели, стоящей у её края.
        /// </summary>
        /// <remarks>
        /// Инвариант шва: до этого теста показ считал круг только по <c>AttackRange</c> и объявлял
        /// второго задетого недосягаемым — <c>VisualDefects</c> орал на штатном ударе (2026-08-20).
        /// Числа взяты с живого кита: круг 2.6, длина ×2, ширина 2.25.
        /// </remarks>
        [Test]
        public void LineAutoAttack_ReachesFarCornerOfTheStrip()
        {
            float reach = ImpactReach.ForAutoAttack(2.6f, AreaShape.Line, lengthMult: 2f, width: 2.25f);

            float length = 2.6f * 2f, halfWidth = 2.25f * 0.5f;
            Assert.That(reach, Is.EqualTo(Mathf.Sqrt(length * length + halfWidth * halfWidth)).Within(1e-4f),
                "круг обязан накрывать угол полосы: вбок от оси бой пускает цель на полуширину");
            Assert.That(reach, Is.GreaterThan(length),
                "цель у края полосы дальше её конца — круг ровно в length оставил бы её недосягаемой");
        }

        /// <summary>
        /// И главное, ради чего всё: цель, задетая СЕРЕДИНОЙ полосы, для показа достижима — вырожденного
        /// случая на ней нет, а значит нет и ложного дефекта.
        /// </summary>
        [Test]
        public void TargetInsideStrip_IsNotDegenerate()
        {
            // Второй враг в полосе стоит в 3.6 — дальше круга выбора цели (2.6), но внутри полосы (5.2).
            var zones = HumanZones(x: 3.6f);
            var at = AttackerAt(0f);
            float reach = ImpactReach.ForAutoAttack(2.6f, AreaShape.Line, lengthMult: 2f, width: 2.25f);

            var solved = ImpactZoneSolver.Solve(at, at, new Vector2(3.6f, 0f), reach, zones, Sharpness, 0.6f, 11u);

            Assert.That(solved.Degenerate, Is.False,
                "бой засчитал удар полосой — показ обязан найти зону, а не выкручиваться заплаткой");
        }
    }
}
