using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.UI;
using NUnit.Framework;
using System.Collections.Generic;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Расширенная карточка «Сосуда»: кого показывает и что делает, когда данных ещё нет.
    /// <para>Карточка — единственный дом травм, поэтому её пустые состояния важны не меньше полных:
    /// выдуманная биография или ступень Закалки читались бы как правда.</para>
    /// </summary>
    public sealed class VesselCardPresenterTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;
        private StubRoster      _guild;
        private VesselCardPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _guild     = new StubRoster();
            _presenter = new VesselCardPresenter(new RunStateViewStub(_runStates), _guild, content: null, loc: null);
        }

        [Test]
        public void Subject_IsEmpty_ForFreePlace()
        {
            _runStates.NewDefaultRun(1L);
            Assert.IsTrue(_presenter.BuildSubject(0).IsEmpty, "Свободное место показывать нечем.");
        }

        [Test]
        public void Subject_TakesNameAndTraitsFromTheHouse()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai";
            _guild.Add(new VesselState
            {
                Id = "vessel.kai",
                Name = "Кай",
                PositiveTraitId = "trait.steady",
                NegativeTraitId = "trait.slow",
                CompletedRuns = 3,
            });

            VesselCardSubject subject = _presenter.BuildSubject(0);

            Assert.AreEqual("Кай", subject.Name, "Имя берётся у человека дома, а не у слота забега.");
            Assert.AreEqual(2, subject.Traits.Count, "Перков ровно два: плюс и минус.");
            Assert.IsTrue(subject.Traits[0].Positive);
            Assert.IsFalse(subject.Traits[1].Positive);
        }

        [Test]
        public void Subject_ShowsNoMettleAndNoLore_WhileNobodyWritesThem()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai";
            _guild.Add(new VesselState { Id = "vessel.kai", Name = "Кай" });

            VesselCardSubject subject = _presenter.BuildSubject(0);

            Assert.IsNull(subject.Mettle, "Закалку никто не выдаёт — пусто честнее выдуманной ступени.");
            Assert.IsEmpty(subject.Lore, "Досье ещё не генерируется — биографию не выдумываем.");
        }

        [Test]
        public void Subject_KnowsWhereThePersonStands()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai";
            _guild.Add(new VesselState { Id = "vessel.kai", Name = "Кай" });

            StringAssert.Contains("в бою", _presenter.BuildSubject(0).Subtitle);

            run.Guild[0].InBattle = false;
            StringAssert.Contains("в запасе", _presenter.BuildSubject(0).Subtitle);
        }

        private sealed class StubRoster : IGuildRosterView
        {
            private readonly List<VesselState> _people = new();
            public void Add(VesselState v) => _people.Add(v);
            public IReadOnlyList<VesselState> Roster => _people;
        }

        private sealed class RunStateViewStub : IRunStateView
        {
            private readonly RunStateService _service;
            public RunStateViewStub(RunStateService service) => _service = service;
            public RunState Current => _service.Current;
        }
    }
}
