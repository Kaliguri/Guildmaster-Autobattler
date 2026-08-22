using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.UI;
using Guildmaster.UI.Components;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Панель осмотра: кого она показывает и когда молчит. Панель одна на всю игру, поэтому её
    /// сборка проверяется отдельно от экранов, которые её приютили.
    /// </summary>
    public sealed class InspectPanelPresenterTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;
        private InspectPanelPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _presenter = new InspectPanelPresenter(new RunStateViewStub(_runStates), content: null, loc: null);
        }

        [Test]
        public void Subject_IsEmpty_WhenNobodySelected()
        {
            _runStates.NewDefaultRun(1L);
            Assert.IsTrue(_presenter.BuildSubject(-1).IsEmpty, "Никто не выбран — показывать некого.");
        }

        [Test]
        public void Subject_IsEmpty_ForFreePlace()
        {
            _runStates.NewDefaultRun(1L); // людей нет, киты базовые
            Assert.IsTrue(_presenter.BuildSubject(0).IsEmpty, "Свободное место осматривать нечего.");
        }

        [Test]
        public void Subject_TellsWhoAndWhereHeStands()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai";
            run.Guild[0].RelicId  = "relic.bulwark";

            InspectSubject subject = _presenter.BuildSubject(0);

            Assert.IsFalse(subject.IsEmpty);
            Assert.AreEqual("vessel.kai", subject.Name, "Без локализации имя показывается своим id, а не заглушкой.");
            StringAssert.Contains("в бою", subject.Subtitle, "Строка под именем говорит, выходит ли он на арену.");
            Assert.AreEqual("relic.bulwark", subject.RelicId, "Есть Реликвия — кнопка «о Реликвии» горит.");
        }

        [Test]
        public void Subject_WithoutRelic_LeavesTheRelicButtonDark()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai"; // кит базовый, то есть Реликвии нет

            InspectSubject subject = _presenter.BuildSubject(0);

            Assert.IsFalse(subject.IsEmpty, "Человек есть, осматривать его можно.");
            Assert.IsNull(subject.RelicId, "Реликвии нет — кнопка гаснет, но панель работает.");
        }

        [Test]
        public void Subject_WithoutStatsService_ShowsNoNumbersInsteadOfZeroes()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.kai";
            run.Guild[0].RelicId  = "relic.bulwark";

            // Ноль в панели читался бы как «боец слабый», хотя это «мы не посчитали».
            Assert.IsEmpty(_presenter.BuildSubject(0).Stats);
        }

        private sealed class RunStateViewStub : IRunStateView
        {
            private readonly RunStateService _service;
            public RunStateViewStub(RunStateService service) => _service = service;
            public RunState Current => _service.Current;
        }
    }
}
