using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Зеркало на НАКОПИТЕЛЯХ: сторож класса дефектов «состояние копится при чужом ударе, а обналичивается
    /// решением носителя». Гоняет дуэль и отряд-дубль Антимагов с ЗАРАНЕЕ НАЛИТЫМ
    /// <see cref="RuntimeUnit.AbsorbedByWard"/> — потому что именно налитый накопитель включает условие,
    /// при котором обмен «Перегрузками» в один тик разводил стороны.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Почему отдельно от <see cref="MirrorMatchTests"/>: там условие складывалось СЛУЧАЙНО. Антимаг стоит
    /// в ростере первым, поэтому в скользящее окно четвёрок попадает ровно один раз, а его собственная
    /// дуэль зелёная лишь потому, что без чужой магии накопитель не наполняется. Дефект 2026-07-31 сторож
    /// поймал по удаче состава, и повторяться на удачу нельзя: здесь условие задаётся руками.
    /// </para>
    /// <para>
    /// <b>Дубль реликвии — часть проверки</b> (поручение Макса 2026-07-31): киты могут повторяться и в одной
    /// команде, и в обеих. Сам дубль зеркало не ломает — ломал взаимный обмен, — но проверять это должен
    /// тест, а не память.
    /// </para>
    /// </remarks>
    public sealed class MirrorAccumulatorTests
    {
        /// <summary>Тиков на прогон: обмен случается в первые же тики, длинная дистанция здесь ничего не добавляет.</summary>
        private const int Ticks = 60;

        /// <summary>Налив накопителя, единиц поглощённого. 120 = два полных варда Антимага.</summary>
        private const float FilledAbsorb = 120f;

        [Test]
        public void AntimageDuel_WithFilledAccumulator_NeverDiverges()
        {
            AssertHolds(Squad("Antimage"), Lineups.Solo, "дуэль Антимагов с налитым накопителем");
        }

        [Test]
        public void AntimageDuplicates_WithFilledAccumulator_NeverDiverge()
        {
            AssertHolds(Squad("Antimage", "Antimage", "Antimage", "Antimage"), Lineups.Squad,
                        "отряд из четырёх Антимагов с налитым накопителем");
        }

        /// <summary>
        /// Дубль вперемешку с китами, наполняющими накопитель чужой магией: именно этот состав вскрыл
        /// дефект, только на 543-м тике и без гарантии повторяемости.
        /// </summary>
        [Test]
        public void AntimagePairWithCasters_WithFilledAccumulator_NeverDiverges()
        {
            AssertHolds(Squad("Antimage", "Antimage", "Arcanist", "Assassin"), Lineups.Squad,
                        "два Антимага с Арканистом и Убийцей");
        }

        // --- Общий сторож ---

        private static void AssertHolds(IReadOnlyList<RelicData> squad, Slot[] lineup, string what)
        {
            var env     = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, lineup);

            for (int i = 0; i < tracked.Count; i++) tracked[i].Unit.Id = i;
            for (int i = 0; i < tracked.Count; i++) env.Sim.EnqueueUnitSpawn(tracked[i].Unit);
            env.Sim.FlushSpawns();

            // Налив ОБЕИМ сторонам одинаково: зеркало обязано остаться зеркалом. Мана до полной — иначе
            // «Перегрузка» ждала бы регена и обмен уехал бы за горизонт прогона.
            for (int i = 0; i < tracked.Count; i++)
            {
                RuntimeUnit u = tracked[i].Unit;
                u.AbsorbedByWard  = FilledAbsorb;
                u.CurrentResource = u.Stats.Get(StatType.MaxResource);
            }

            int half = tracked.Count / 2;

            for (int tick = 0; tick < Ticks; tick++)
            {
                env.Sim.Tick(SimConstants.TickDelta);

                for (int i = 0; i < half; i++)
                {
                    string apart = MirrorFixture.FirstDifference(tracked[i].Unit, tracked[i + half].Unit);
                    if (apart == null) continue;

                    Assert.Fail(
                        $"Зеркало разошлось на тике {tick} ({what}), пара «{tracked[i].Label}»: {apart}.\n" +
                        "Накопитель обналичивается решением носителя, а копится от чужого удара — значит " +
                        "где-то удар снова меняет мир посреди фазы решений (см. TickLedger.ResolveIncoming).");
                }
            }
        }

        private static List<RelicData> Squad(params string[] names)
        {
            List<RelicData> all = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>(names.Length);

            foreach (string name in names)
            {
                RelicData relic = all.Find(r => r.name == name);
                Assert.That(relic, Is.Not.Null, $"Реликвия {name} не найдена");
                squad.Add(relic);
            }

            return squad;
        }
    }
}
