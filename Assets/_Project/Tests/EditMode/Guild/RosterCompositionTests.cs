using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Состав отряда и путь вещи: кто выходит в бой, что происходит со старым сейвом и как предмет
    /// переезжает между складом и «Сосудом».
    /// <para>Эти инварианты живут в тесте, а не в комментарии, потому что нарушить их можно ИЗВНЕ —
    /// командой из UI или из сети, — и обе стороны шва про чужую половину не знают.</para>
    /// </summary>
    public sealed class RosterCompositionTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;
        private RunCommandBus   _bus;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _bus       = new RunCommandBus(new RunCommandApplier(_runStates), new RunCommandLog());
        }

        // ── Кто выходит в бой ────────────────────────────────────────────────

        [Test]
        public void SetInBattle_RefusesFifthFighter()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            for (int i = 0; i < run.Guild.Length; i++) run.Guild[i].VesselId = "vessel.x";

            // Четверо уже на арене (NewDefaultRun), пятого не пускаем: мест столько, сколько BattleSlots.
            Assert.IsFalse(_bus.SetSlotInBattle(_config.BattleSlots, true),
                           "Пятый боец в четырёхместный бой попасть не должен.");

            Assert.IsTrue(_bus.SetSlotInBattle(0, false), "Увести в запас можно всегда.");
            Assert.IsTrue(_bus.SetSlotInBattle(_config.BattleSlots, true),
                          "Место освободилось — теперь пятый заходит.");
        }

        [Test]
        public void SetInBattle_RefusesEmptyPlace()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            _bus.SetSlotInBattle(0, false);
            run.Guild[0].VesselId = string.Empty;

            Assert.IsFalse(_bus.SetSlotInBattle(0, true), "Пустое место выводить в бой некого.");
        }

        [Test]
        public void SetInBattle_RefusesClosedPlace()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            int closed = run.OpenSlots; // первое место за пределами открытых
            if (closed >= run.Guild.Length) Assert.Ignore("Все места открыты — проверять нечего.");

            run.Guild[closed].VesselId = "vessel.x";
            _bus.SetSlotInBattle(0, false);

            Assert.IsFalse(_bus.SetSlotInBattle(closed, true),
                           "Место, которое ещё не открыто наградой забега, в бой не выводит.");
        }

        [Test]
        public void SwapSlots_CarriesBattleFlagWithThePerson()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].VesselId = "vessel.a";
            int spare = _config.BattleSlots; // первый запасной
            run.Guild[spare].VesselId = "vessel.b";

            Assert.IsTrue(_bus.SwapSlots(0, spare));

            Assert.AreEqual("vessel.b", run.Guild[0].VesselId);
            Assert.AreEqual("vessel.a", run.Guild[spare].VesselId);

            // «В бою» — свойство ЧЕЛОВЕКА, а не места в ленте: боец, которого переставили, остаётся
            // на арене. Иначе перетаскивание значило бы две разные вещи разом — и порядок, и состав,
            // ровно то, из-за чего признак сделали полем, а не позицией.
            Assert.IsFalse(run.Guild[0].InBattle,  "Запасной, переехавший на первое место, в бой не попадает.");
            Assert.IsTrue(run.Guild[spare].InBattle, "Боец остаётся в бою, куда бы его ни переставили в ленте.");

            int inBattle = 0;
            for (int i = 0; i < run.Guild.Length; i++) if (run.Guild[i].InBattle) inBattle++;
            Assert.AreEqual(_config.BattleSlots, inBattle, "Состав арены перестановкой не меняется.");
        }

        // ── Старый сейв ──────────────────────────────────────────────────────

        [Test]
        public void Normalize_GrowsOldFourSlotRun_AndPicksBattleFour()
        {
            // Так выглядел забег до того, как отряд вырос: четыре места, поля InBattle нет вовсе.
            var old = new RunState
            {
                Guild = new[]
                {
                    new RosterSlot { VesselId = "vessel.a" },
                    new RosterSlot { VesselId = "vessel.b" },
                    new RosterSlot { VesselId = "vessel.c" },
                    new RosterSlot { VesselId = "vessel.d" },
                }
            };

            RunStateService.Normalize(old, _config);

            Assert.AreEqual(_config.GuildSize, old.Guild.Length, "Массив дорос до потолка мест.");
            Assert.AreEqual(_config.GuildSlotsOpenAtStart, old.OpenSlots, "Открытых мест — база из конфига.");

            int inBattle = 0;
            for (int i = 0; i < old.Guild.Length; i++) if (old.Guild[i].InBattle) inBattle++;
            Assert.AreEqual(_config.BattleSlots, inBattle, "Боевыми стали первые занятые места, а не все.");
            Assert.IsTrue(old.Guild[0].InBattle && old.Guild[1].InBattle);

            for (int i = 4; i < old.Guild.Length; i++)
                Assert.AreEqual(ContentIds.BaseRelic, old.Guild[i].RelicId, "Дописанные места несут базовый кит.");
        }

        [Test]
        public void Normalize_IsIdempotent()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            int before = 0;
            for (int i = 0; i < run.Guild.Length; i++) if (run.Guild[i].InBattle) before++;

            RunStateService.Normalize(run, _config);
            RunStateService.Normalize(run, _config);

            int after = 0;
            for (int i = 0; i < run.Guild.Length; i++) if (run.Guild[i].InBattle) after++;
            Assert.AreEqual(before, after, "Повторная нормализация свежий забег не трогает.");
            Assert.AreEqual(_config.GuildSize, run.Guild.Length);
        }

        // ── Люди дома садятся на боевые места ────────────────────────────────

        [Test]
        public void NewRun_SeatsGuildPeople_OnBattlePlacesOnly()
        {
            var roster = new StubRoster("vessel.a", "vessel.b", "vessel.c", "vessel.d", "vessel.e");
            var service = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(),
                                              content: null, audio: null, roster: roster);

            RunState run = service.NewDefaultRun(1L);

            int seated = 0;
            for (int i = 0; i < run.Guild.Length; i++)
                if (!string.IsNullOrEmpty(run.Guild[i].VesselId)) seated++;

            Assert.AreEqual(_config.BattleSlots, seated, "Садятся ровно те, кто выходит в бой, — не весь дом.");
            Assert.AreEqual("vessel.a", run.Guild[0].VesselId);
            Assert.IsTrue(run.Guild[0].InBattle, "Занятые места — боевые.");
        }

        [Test]
        public void NewRun_WithoutGuild_LeavesPlacesEmpty()
        {
            RunState run = _runStates.NewDefaultRun(1L); // сервис поднят без дома

            for (int i = 0; i < run.Guild.Length; i++)
                Assert.IsEmpty(run.Guild[i].VesselId,
                               "Без дома забег стартует с пустыми местами: выдумывать бойцов нельзя.");
        }

        /// <summary>Дом с людьми — ровно столько контракта, сколько нужно забегу.</summary>
        private sealed class StubRoster : IGuildRosterView
        {
            private readonly List<VesselState> _people = new();
            public StubRoster(params string[] ids)
            {
                foreach (string id in ids) _people.Add(new VesselState { Id = id, Name = id });
            }
            public IReadOnlyList<VesselState> Roster => _people;
        }

        // ── Путь вещи ────────────────────────────────────────────────────────

        [Test]
        public void Item_TravelsThroughStash_AndNeverDuplicates()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.ItemInventory = new[] { "item.boots" };

            Assert.IsTrue(_bus.SetSlotItem(0, 0, "item.boots"), "Вещь из склада надевается.");
            Assert.AreEqual("item.boots", run.Guild[0].VesselItemIds[0]);
            Assert.AreEqual(0, run.ItemInventory.Length, "Надетая вещь ушла со склада — копий не осталось.");

            Assert.IsFalse(_bus.SetSlotItem(1, 0, "item.boots"),
                           "Второй раз надеть ту же вещь нельзя: в складе её больше нет.");

            Assert.IsTrue(_bus.SetSlotItem(0, 0, string.Empty), "Снятие возвращает вещь в склад.");
            Assert.AreEqual(1, run.ItemInventory.Length);
            Assert.AreEqual("item.boots", run.ItemInventory[0]);
            Assert.IsTrue(string.IsNullOrEmpty(run.Guild[0].VesselItemIds[0]));
        }

        [Test]
        public void Item_ReplacingWorn_ReturnsPreviousToStash()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.ItemInventory = new[] { "item.boots", "item.amulet" };

            _bus.SetSlotItem(0, 0, "item.boots");
            Assert.IsTrue(_bus.SetSlotItem(0, 0, "item.amulet"), "Замена в занятом слоте разрешена.");

            Assert.AreEqual("item.amulet", run.Guild[0].VesselItemIds[0]);
            Assert.Contains("item.boots", run.ItemInventory, "Прежняя вещь вернулась в склад, а не исчезла.");
        }

        [Test]
        public void Item_RefusesSlotBeyondConfiguredCount()
        {
            RunState run = _runStates.NewDefaultRun(1L);
            run.ItemInventory = new[] { "item.boots" };

            Assert.AreEqual(_config.VesselItemSlots, run.OpenItemSlots, "На старте открыта база слотов.");
            Assert.IsFalse(_bus.SetSlotItem(0, run.OpenItemSlots, "item.boots"),
                           "Четвёртый слот закрыт, пока его не открыла награда забега.");
            Assert.AreEqual(1, run.ItemInventory.Length, "Отказ не съедает вещь со склада.");

            run.OpenItemSlots = _config.VesselItemSlotsMax; // награда забега открыла четвёртый
            Assert.IsTrue(_bus.SetSlotItem(0, _config.VesselItemSlotsMax - 1, "item.boots"),
                          "Открытый наградой слот принимает вещь.");
            Assert.AreEqual(0, run.ItemInventory.Length, "Надетое ушло со склада.");
        }
    }
}
