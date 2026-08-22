using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using Guildmaster.UI;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Проводка страницы «Отряд»: что экран видит в состоянии забега и что он с ним делает.
    /// <para>Инвариант живёт в тесте, потому что стороны шва не знают друг о друге: вью получает
    /// плоские записи и не умеет читать <c>RunState</c>, а состояние не знает про экран. Разъедутся —
    /// компилятор промолчит.</para>
    /// </summary>
    public sealed class PartyScreenPresenterTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;
        private RunCommandBus   _bus;
        private PartyScreenPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _bus       = new RunCommandBus(new RunCommandApplier(_runStates), new RunCommandLog());
            _presenter = new PartyScreenPresenter(new RunStateViewStub(_runStates), _bus, content: null, loc: null, config: _config);
        }

        [Test]
        public void Slots_ShowEveryPlace_OpenAndClosed()
        {
            RunState run = _runStates.NewDefaultRun(1L);

            IReadOnlyList<PartySlotView> slots = _presenter.BuildSlots();

            Assert.AreEqual(run.Guild.Length, slots.Count, "Экран показывает ВСЕ места, включая закрытые.");

            int open = 0, inBattle = 0;
            foreach (PartySlotView s in slots)
            {
                if (s.Open) open++;
                if (s.InBattle) inBattle++;
            }
            Assert.AreEqual(run.OpenSlots, open, "Открытых столько, сколько открыл забег.");
            Assert.AreEqual(_config.BattleSlots, inBattle, "В бою помечены ровно те, кто выходит на арену.");

            for (int i = 0; i < slots.Count; i++)
                Assert.AreEqual(i, slots[i].Index, "Индекс места едет во вью: по нему уходит команда.");
        }

        [Test]
        public void Slots_WithoutVesselsAndKits_ReadAsEmpty()
        {
            // Так выглядит забег сейчас: людей нет вовсе, у всех базовый кит. Экран обязан показать
            // это как пустые места, а не выдумать имена.
            _runStates.NewDefaultRun(1L);

            foreach (PartySlotView slot in _presenter.BuildSlots())
                Assert.IsTrue(slot.IsEmpty, "Место без человека и без кита читается как свободное.");
        }

        [Test]
        public void Slots_ShowKitName_WhenNobodyCarriesIt()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].RelicId = "relic.druid"; // кит есть, человека нет

            IReadOnlyList<PartySlotView> slots = _presenter.BuildSlots();

            Assert.IsFalse(slots[0].IsEmpty, "Место с китом занято: именно оно выходит в бой.");
            Assert.AreEqual("relic.druid", slots[0].Relic);
        }

        /// <summary>Читающая половина забега для тех, кто живёт дольше сессии. В тесте — прямой доступ.</summary>
        private sealed class RunStateViewStub : IRunStateView
        {
            private readonly RunStateService _service;
            public RunStateViewStub(RunStateService service) => _service = service;
            public RunState Current => _service.Current;
        }
    }
}
