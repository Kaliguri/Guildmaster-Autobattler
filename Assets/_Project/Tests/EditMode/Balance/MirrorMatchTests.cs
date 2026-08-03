using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Зеркальные бои: две ОДИНАКОВЫЕ команды, отражённые по оси. Ни один такой бой не должен разойтись —
    /// иначе у симуляции есть встроенное преимущество стороны, и тогда врут все замеры стенда, а в игре
    /// одна из команд системно сильнее другой.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сторож заведён после того, как замена в отряде показала зеркальный бой со счётом 59.7% против нуля.
    /// Причин оказалось три, и все — один дефект: система читала мир и писала в него внутри одного обхода,
    /// поэтому место юнита в списке решало исход, а у отражённых сторон этот порядок обратный.
    /// </para>
    /// <para>
    /// <b>Критерий строгий и это принципиально: сверяется каждый тик, а не итог боя.</b> Прежняя редакция
    /// сравнивала остатки HP в конце с допуском в 10 процентных пунктов — так сторож молчал, пока
    /// расхождение не разрасталось до разгрома, а причина к тому времени лежала за сотни тиков позади.
    /// Зеркало обязано давать ровный ноль на КАЖДОМ тике КАЖДОГО боя; усреднять здесь нечего — усреднение
    /// маскирует перекос вместо того, чтобы его показать.
    /// </para>
    /// <para>
    /// Четвёртая причина (BAL-014) оказалась другого рода: не чтение-и-запись в одном обходе, а сам
    /// ПОРЯДОК СЛОЖЕНИЯ float. Расталкивание копило вклады в порядке соседей из пространственного хэша и
    /// добирало часть слагаемых из чужих итераций внешнего цикла, поэтому у отражённых сторон суммы
    /// расходились в последнем бите. Лечится каноническим порядком обхода — см. <c>SeparationSystem</c>.
    /// </para>
    /// <para>
    /// <b>Серия, а не один бой:</b> третья причина вылезала только на четвёрке из РАЗНЫХ китов и только на
    /// десятой секунде — один показательный бой её не ловил. Поэтому гоняется скользящее окно по всему
    /// ростеру и несколько строёв: разные составы включают разные способности, а разные строи — разные
    /// дистанции, кайтинг и расталкивание.
    /// </para>
    /// </remarks>
    public sealed class MirrorMatchTests
    {
        /// <summary>Потолок боя в серии, тиков (120 с при 30 Гц).</summary>
        /// <remarks>
        /// Короче полного боя намеренно: расхождение — дефект детерминизма, оно проявляется рано (все три
        /// пойманные причины били на тиках 1, 240 и 300), а серия должна оставаться дешёвой, чтобы её
        /// гонял каждый прогон. Полную дистанцию держит зонд <see cref="MirrorDivergenceProbe"/>.
        /// </remarks>
        private const int SeriesTicks = 120 * 30;

        // --- Синтетики: чистое ядро без китов ---

        [Test]
        public void Mirror_DummiesOneOnOne_NeverDiverges()
        {
            AssertMirrorHolds(new RelicData[0], new[] { new Slot(UnitClass.Bruiser, 1f, 0f) }, "дуэль манекенов");
        }

        [Test]
        public void Mirror_DummiesSquad_NeverDiverges()
        {
            AssertMirrorHolds(new RelicData[0], Lineups.Squad, "отряд манекенов");
        }

        /// <summary>
        /// Два тела в ОДНОЙ точке: вырожденная ветка расталкивания, где направление берётся не из геометрии
        /// (её нет), а из стороны команды и Id. Ветка была не зеркальной — обе отражённые пары разъезжались
        /// в одну сторону вместо противоположных, — и ни один другой тест её не достаёт: штатные строи
        /// никогда не ставят двоих точь-в-точь.
        /// </summary>
        [Test]
        public void Mirror_CoincidentBodies_NeverDiverges()
        {
            // Класс ОБЯЗАН быть один и тот же: у разных классов разная скорость, движение растащило бы
            // тела ещё до расталкивания, и вырожденная ветка не отработала бы вовсе.
            AssertMirrorHolds(new RelicData[0], new[]
            {
                new Slot(UnitClass.Bruiser, Lineups.FrontX, 0f),
                new Slot(UnitClass.Bruiser, Lineups.FrontX, 0f),   // ровно там же — тела слиплись
            }, "слипшиеся тела");
        }

        // --- Один кит против самого себя ---

        /// <summary>
        /// Зеркало КАЖДОГО реального кита против самого себя. Манекены симметричны по построению, а кит
        /// несёт способности, ресурс и мозги — если сторона решает исход, ломается именно здесь.
        /// </summary>
        [Test]
        public void Mirror_EachRelicAgainstItself_NeverDiverges([ValueSource(nameof(RelicNames))] string relicName)
        {
            RelicData relic = Relic(relicName);
            AssertMirrorHolds(new[] { relic },
                new[] { new Slot(relic.CombatClass, 2.2f, 0f) }, $"дуэль «{relicName}»");
        }

        // --- Серия отрядов: скользящее окно по всему ростеру ---

        /// <summary>
        /// Четвёрки из РАЗНЫХ китов, окно за окном по всему ростеру. Именно такой бой вскрыл третью причину
        /// перекоса: два готовых в один тик криоманта, и каст доставался тому, кто заспавнен раньше.
        /// </summary>
        [Test]
        public void Mirror_SquadSeries_NeverDiverges([ValueSource(nameof(SquadWindows))] int start)
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            for (int i = 0; i < 4 && start + i < relics.Count; i++) squad.Add(relics[start + i]);

            var names = new List<string>();
            foreach (RelicData r in squad) names.Add(r.name);

            AssertMirrorHolds(squad, Lineups.Squad, $"отряд [{string.Join(", ", names)}]");
        }

        // --- Строи: те же киты в разной геометрии ---

        /// <summary>
        /// Один и тот же состав в разных строях. Строй меняет дистанции, а с ними — кайтинг, расталкивание
        /// и то, кто до кого дотягивается: расхождение, невидимое в плотной четвёрке, вылезает в растянутой
        /// шестёрке (и наоборот).
        /// </summary>
        [Test]
        public void Mirror_Lineups_NeverDiverge([Values("Trio", "Squad", "Large")] string lineupName)
        {
            Slot[] lineup = lineupName switch
            {
                "Trio"  => Lineups.Trio,
                "Large" => Lineups.Large,
                _       => Lineups.Squad,
            };

            // Состав режем по числу слотов: лишний герой слота не получает и на арену не выходит вовсе
            // (SpawnTeam об этом кричит). Зеркальность от этого не страдает — стороны одинаковы, — но
            // тест проверял бы тройку, называя её четвёркой.
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            foreach (string name in new[] { "Defender", "FlameSwordsman", "Cryomancer", "LightShepherd" })
            {
                if (squad.Count >= lineup.Length) break;
                RelicData relic = relics.Find(r => r.name == name);
                if (relic != null) squad.Add(relic);
            }

            AssertMirrorHolds(squad, lineup, $"строй «{lineupName}»");
        }

        // --- Общий сторож ---

        private static void AssertMirrorHolds(IReadOnlyList<RelicData> squad, Slot[] lineup, string what)
        {
            int tick = MirrorFixture.FirstDivergingTick(squad, lineup, SeriesTicks, out string report);

            if (tick >= 0)
                Assert.Fail($"Зеркало разошлось ({what}). Стороны обязаны идти тик в тик: расхождение " +
                            $"означает, что исход решает порядок обработки, а не бойцы.\n{report}");
        }

        private static RelicData Relic(string name)
        {
            RelicData relic = BalanceAssets.LoadRelics().Find(r => r.name == name);
            Assert.IsNotNull(relic, $"Реликвия {name} не найдена");
            return relic;
        }

        private static IEnumerable<string> RelicNames()
        {
            var names = new List<string>();
            foreach (RelicData r in BalanceAssets.LoadRelics()) names.Add(r.name);
            return names;
        }

        /// <summary>Стартовые индексы скользящего окна по ростеру: каждый кит попадает в четвёрку не раз.</summary>
        private static IEnumerable<int> SquadWindows()
        {
            int count = BalanceAssets.LoadRelics().Count;
            var starts = new List<int>();
            for (int i = 0; i + 4 <= count; i++) starts.Add(i);
            if (starts.Count == 0) starts.Add(0);   // ростер короче четвёрки — гоняем что есть
            return starts;
        }
    }
}
