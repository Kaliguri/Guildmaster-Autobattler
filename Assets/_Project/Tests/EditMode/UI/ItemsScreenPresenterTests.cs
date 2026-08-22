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
    /// Проводка страницы «Предметы»: что попадает в строки и чем закрытый слот отличается от пустого.
    /// <para>Разница между ними живёт в тесте, потому что в записи строки она выражена значением —
    /// пустая строка против <c>null</c>, — и перепутать их можно молча.</para>
    /// </summary>
    public sealed class ItemsScreenPresenterTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;
        private ItemsScreenPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _presenter = new ItemsScreenPresenter(new RunStateViewStub(_runStates), commands: null, loc: null, config: _config);
        }

        [Test]
        public void Rows_SkipPlacesWithNobodyToDressUp()
        {
            RunState run = _runStates.NewDefaultRun(1L); // людей нет, киты базовые

            Assert.IsEmpty(_presenter.BuildRows(), "Вешать вещь не на кого — строки нет вовсе.");

            run.Guild[1].VesselId = "vessel.kai";
            IReadOnlyList<ItemsRowView> rows = _presenter.BuildRows();

            Assert.AreEqual(1, rows.Count, "Строка появляется у занятого места.");
            Assert.AreEqual(1, rows[0].SlotIndex, "Место в отряде едет во вью: по нему уходит команда.");
        }

        [Test]
        public void Rows_TellLockedSlotFromEmptyOne()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.irma";

            ItemsRowView row = _presenter.BuildRows()[0];

            Assert.AreEqual(_config.VesselItemSlotsMax, row.Items.Count, "Слотов столько, сколько их бывает всего.");
            for (int i = 0; i < run.OpenItemSlots; i++)
                Assert.AreEqual(string.Empty, row.Items[i], "Открытый и свободный слот — пустая строка: он ждёт вещь.");
            for (int i = run.OpenItemSlots; i < row.Items.Count; i++)
                Assert.IsNull(row.Items[i], "Закрытый слот — null: он ждёт награду забега, а не вещь.");
        }

        [Test]
        public void Rows_ShowWornItems()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId      = "vessel.irma";
            run.Guild[0].VesselItemIds = new[] { "item.boots", string.Empty, string.Empty };

            ItemsRowView row = _presenter.BuildRows()[0];

            Assert.AreEqual("item.boots", row.Items[0]);
            Assert.AreEqual(string.Empty, row.Items[1]);
        }

        [Test]
        public void Stash_ShowsWhatTheRunKeeps()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.ItemInventory = new[] { "item.rune_frost", "item.cloak" };

            Assert.AreEqual(2, _presenter.BuildStash().Count);
        }

        private sealed class RunStateViewStub : IRunStateView
        {
            private readonly RunStateService _service;
            public RunStateViewStub(RunStateService service) => _service = service;
            public RunState Current => _service.Current;
        }
    }
}
