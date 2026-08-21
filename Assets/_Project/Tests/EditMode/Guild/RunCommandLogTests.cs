using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.Guild.Commands;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Шина команд забега (ТЗ кооп-вертикали §4.1): <see cref="RunState"/> меняется только обработчиком
    /// команды, а поток команд — append-only лог.
    /// <para>Главный тест здесь — «один лог → один <see cref="RunState"/>»: он же и есть причина, по
    /// которой лог существует. Из него следуют реплей забега, аудит «кто передвинул юнита» и реконнект
    /// как «снимок плюс хвост лога»; сломается он — сломаются все трое, поэтому проверяется именно
    /// воспроизведение на ЧИСТОМ состоянии, а не совпадение с самим собой.</para>
    /// </summary>
    public sealed class RunCommandLogTests
    {
        private GameConfig      _config;
        private RunStateService _state;
        private RunCommandLog   _log;
        private RunCommandBus   _bus;

        [SetUp]
        public void SetUp()
        {
            _config = GameConfig.CreateDefault();
            _state  = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            _state.NewDefaultRun(1L);   // 4 сосуда с relic.base

            _log = new RunCommandLog();
            _bus = new RunCommandBus(new RunCommandApplier(_state), _log);
        }

        // ═══ Главное обещание лога ═══

        [Test]
        public void Log_ReplayedOnAFreshRun_GivesTheSameState()
        {
            _state.TryAddRelic("relic.druid");          // транзакция: в лог не идёт, см. отдельный тест ниже
            _bus.AddGold(120);
            _bus.SetSlotPosition(1, new Vector2(-3.5f, 2f));
            _bus.SetSlotRelic(2, "relic.knight");
            _bus.RemoveRelic("relic.druid");
            _bus.AddGold(-40);

            int    goldAfterPlay = _state.Current.Gold;
            var    posAfterPlay  = _state.Current.Guild[1].SavedPosition;
            string relicAfterPlay = _state.Current.Guild[2].RelicId;
            int    stashAfterPlay = _state.Current.RelicInventory.Length;

            // Чистый забег с тем же сидом и тот же лог, прогнанный по порядку.
            var replayState = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService(), content: null);
            replayState.NewDefaultRun(1L);
            replayState.TryAddRelic("relic.druid");     // транзакции восстанавливаются не логом — см. ниже
            var replayApplier = new RunCommandApplier(replayState);

            for (int i = 0; i < _log.Count; i++)
                replayApplier.Apply(_log[i]);

            Assert.AreEqual(goldAfterPlay, replayState.Current.Gold, "Золото воспроизвелось из лога");
            Assert.AreEqual(posAfterPlay, replayState.Current.Guild[1].SavedPosition,
                "Позиция сосуда воспроизвелась из лога");
            Assert.AreEqual(relicAfterPlay, replayState.Current.Guild[2].RelicId,
                "Кит слота воспроизвёлся из лога");
            Assert.AreEqual(stashAfterPlay, replayState.Current.RelicInventory.Length,
                "Запас реликвий сошёлся");
        }

        // ═══ Идемпотентность: лекарство от дублей на стыке реконнекта ═══

        [Test]
        public void SameCommandTwice_IsAppliedOnce()
        {
            var gold = new RunCommand(RunCommandKind.AddGold, playerId: 0, sequence: 7,
                clientTimeMs: 1000, amount: 50);

            int before = _state.Current.Gold;

            Assert.IsTrue(_bus.Submit(gold), "Первая отправка применяется");
            Assert.IsFalse(_bus.Submit(gold),
                "Повтор с той же парой «игрок, номер» — дубль, а не второе начисление");

            Assert.AreEqual(before + 50, _state.Current.Gold, "Золото начислено ровно один раз");
            Assert.AreEqual(1, _log.Count, "И в логе он один");
        }

        [Test]
        public void SameSequence_FromAnotherPlayer_IsNotADuplicate()
        {
            var mine   = new RunCommand(RunCommandKind.AddGold, playerId: 0, sequence: 1,
                clientTimeMs: 1000, amount: 10);
            var theirs = new RunCommand(RunCommandKind.AddGold, playerId: 1, sequence: 1,
                clientTimeMs: 1001, amount: 10);

            Assert.IsTrue(_bus.Submit(mine));
            Assert.IsTrue(_bus.Submit(theirs),
                "Номера у игроков свои: одинаковый номер от другого игрока — другая команда");
            Assert.AreEqual(2, _log.Count);
        }

        // ═══ Журнал хранит то, что случилось ═══

        [Test]
        public void CommandThatChangedNothing_IsNotLogged()
        {
            Assert.IsFalse(_bus.SetSlotPosition(99, Vector2.zero),
                "Слота 99 в ростере нет — применять нечего");
            Assert.AreEqual(0, _log.Count,
                "В лог попадает только случившееся: иначе реплей повторил бы отказы как события");
        }

        [Test]
        public void Log_RemembersWhoAskedAndInWhatOrder()
        {
            _bus.AddGold(5);
            _bus.SetSlotPosition(0, new Vector2(1f, 1f));

            Assert.AreEqual(RunCommandKind.AddGold, _log[0].Kind, "Порядок в логе — порядок применения");
            Assert.AreEqual(RunCommandKind.SetSlotPosition, _log[1].Kind);
            Assert.AreEqual(RunCommand.LocalPlayerId, _log[1].PlayerId, "Аудит знает, кто передвинул");
            Assert.AreEqual(0, _log[0].Sequence, "Свой счётчик игрока начинается с нуля");
            Assert.AreEqual(1, _log[1].Sequence, "И растёт на каждую принятую команду");
        }

        // ═══ Ключи живут ровно столько, сколько забег ═══

        [Test]
        public void NewRun_StartsNumberingOver_WithoutTakingItForDuplicates()
        {
            _bus.AddGold(5);                       // номер 0 израсходован
            Assert.AreEqual(1, _log.Count);

            _bus.ResetForNewRun();
            _state.NewDefaultRun(2L);

            _bus.AddGold(5);                       // снова номер 0 — и это НЕ дубль
            Assert.AreEqual(1, _log.Count, "У нового забега свой лог");
            Assert.AreEqual(5, _state.Current.Gold - _config.StartGold + 0,
                "Команда нового забега применилась, а не отброшена памятью о старых номерах");
        }

        // ═══ Честная граница шва ═══

        // Транзакции (спрашивают «вышло ли» синхронно) через шину ПОКА не идут — это отложенный шаг ТЗ.
        // Тест фиксирует границу, чтобы «в логе нет покупок» было заявленным состоянием, а не находкой.
        [Test]
        public void Transactions_DoNotGoThroughTheBusYet()
        {
            _state.TryAddRelic("relic.x");
            _state.TrySpendGold(10);
            _state.IncreaseCapacity();

            Assert.AreEqual(0, _log.Count,
                "TryAddRelic / TrySpendGold / IncreaseCapacity меняют забег мимо лога — граница шва " +
                "фазы А, закрывается вместе с транзакциями над экранами подготовки");
        }
    }
}
