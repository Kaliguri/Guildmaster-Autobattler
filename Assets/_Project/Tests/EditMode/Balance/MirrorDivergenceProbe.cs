using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Диагностический зонд: тикает зеркальный бой полной длины и печатает ПЕРВЫЙ тик, на котором
    /// отражённые друг друга бойцы разошлись, вместе со слепком обеих сторон и ударами того тика.
    /// Не сторож качества — инструмент для поиска причины, оставлен намеренно: если перекос стороны
    /// вернётся, этот тест покажет не «плохо», а где именно.
    /// </summary>
    /// <remarks>
    /// Судит по той же линейке, что и сторож (<see cref="MirrorFixture.FirstDifference"/>). Разница
    /// только в назначении: сторож гоняет серию составов и отвечает «да/нет», зонд берёт один
    /// показательный отряд на полный бой и отвечает «вот здесь и вот чем».
    /// </remarks>
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

            int tick = MirrorFixture.FirstDivergingTick(
                squad, Lineups.Squad, MirrorFixture.FullBattleTicks, out string report);

            if (tick >= 0) Assert.Fail(report);
            Assert.Pass("Зеркало не разошлось за весь бой");
        }
    }
}
