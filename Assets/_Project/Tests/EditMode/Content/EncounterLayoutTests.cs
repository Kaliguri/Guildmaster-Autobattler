using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Расстановка копий врага внутри пакета энкаунтера (<see cref="EncounterUnit.PositionOf"/>). Правило
    /// живёт в самой структуре и читается двумя потребителями сразу — боевым <c>EncounterLoader</c> и
    /// балансным <c>EncounterBench</c>, — поэтому у него есть сторож: разъехавшись, стенд начал бы мерить
    /// бой, которого в игре нет.
    /// </summary>
    /// <remarks>
    /// Пакеты собираются через <see cref="JsonUtility"/>: у структуры приватные <c>SerializeField</c> и нет
    /// публичного конструктора (она авторится в инспекторе), а тест обязан оставаться герметичным и не
    /// зависеть от того, какие энкаунтеры сейчас лежат в ассетах.
    /// </remarks>
    public sealed class EncounterLayoutTests
    {
        private const float Eps = 1e-4f;

        private static EncounterUnit Pack(string count, string spacing, string x = "5", string y = "0")
            => JsonUtility.FromJson<EncounterUnit>(
                "{\"_enemyId\":\"enemy.test\"," +
                "\"_position\":{\"x\":" + x + ",\"y\":" + y + "}," +
                "\"_count\":" + count + ",\"_spacing\":" + spacing + "}");

        [Test]
        public void SingleCopy_SitsExactlyOnAnchor()
        {
            EncounterUnit pack = Pack(count: "1", spacing: "1.5", x: "6", y: "3");
            Assert.AreEqual(new Vector2(6f, 3f), pack.PositionOf(0),
                "Одиночный враг стоит ровно там, где автор поставил якорь");
        }

        [Test]
        public void OddCluster_IsCenteredOnAnchor()
        {
            EncounterUnit pack = Pack(count: "3", spacing: "1", y: "2");

            Assert.AreEqual(1f, pack.PositionOf(0).y, Eps);
            Assert.AreEqual(2f, pack.PositionOf(1).y, Eps, "Средняя копия занимает сам якорь");
            Assert.AreEqual(3f, pack.PositionOf(2).y, Eps);

            for (int c = 0; c < pack.Count; c++)
                Assert.AreEqual(5f, pack.PositionOf(c).x, Eps, "Кластер вертикальный: X не разъезжается");
        }

        [Test]
        public void EvenCluster_StraddlesAnchor()
        {
            EncounterUnit pack = Pack(count: "2", spacing: "1");

            Assert.AreEqual(-0.5f, pack.PositionOf(0).y, Eps);
            Assert.AreEqual(0.5f, pack.PositionOf(1).y, Eps);
            Assert.AreEqual(0f, pack.PositionOf(0).y + pack.PositionOf(1).y, Eps,
                "Якорь остаётся центром кластера при чётном числе копий");
        }

        [Test]
        public void NonPositiveSpacing_UsesTheSameDefaultAsTheGetter()
        {
            EncounterUnit pack = Pack(count: "2", spacing: "0");

            Assert.AreEqual(0.8f, pack.Spacing, Eps, "Шаг <=0 подменяется дефолтом");
            Assert.AreEqual(pack.Spacing, pack.PositionOf(1).y - pack.PositionOf(0).y, Eps,
                "Расстановка обязана считать тем же шагом, который отдаёт геттер");
        }

        [Test]
        public void ClusterStep_MatchesSpacing()
        {
            EncounterUnit pack = Pack(count: "4", spacing: "1.4");

            for (int c = 1; c < pack.Count; c++)
                Assert.AreEqual(1.4f, pack.PositionOf(c).y - pack.PositionOf(c - 1).y, Eps,
                    "Шаг между соседними копиями равен Spacing, без накопления ошибки");
        }
    }
}
