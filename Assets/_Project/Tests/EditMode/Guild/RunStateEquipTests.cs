using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Лоадаут-операции <see cref="RunStateService"/> (кольцо реликвий, Фаза 2): надеть релик из запаса на сосуд
    /// (свап прежнего кита обратно в запас), снять релик обратно в запас (слот → базовый кит). Проверяем, что
    /// это durable-правки <see cref="RunState.Guild"/> + <see cref="RunState.RelicInventory"/>, без потери реликов.
    /// </summary>
    public sealed class RunStateEquipTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;

        [SetUp]
        public void SetUp()
        {
            _config    = GameConfig.CreateDefault();
            _runStates = new RunStateService(new InMemorySaveService(), _config, new FixedProfileService());
            _runStates.NewDefaultRun(1L); // 4 сосуда с relic.base
        }

        [Test]
        public void Equip_MovesRelicFromStashToSlot()
        {
            _runStates.TryAddRelic("relic.druid");

            Assert.IsTrue(_runStates.EquipRelic(0, "relic.druid"));
            Assert.AreEqual("relic.druid", _runStates.Current.Guild[0].RelicId, "Слот несёт надетый релик.");
            Assert.AreEqual(-1, Array.IndexOf(_runStates.Current.RelicInventory, "relic.druid"),
                "Надетый релик ушёл из запаса.");
        }

        [Test]
        public void Equip_SwapsPreviousBackToStash()
        {
            _runStates.TryAddRelic("relic.a");
            _runStates.TryAddRelic("relic.b");
            _runStates.EquipRelic(0, "relic.a");            // слот 0 = relic.a
            Assert.IsTrue(_runStates.EquipRelic(0, "relic.b")); // свап на relic.b

            Assert.AreEqual("relic.b", _runStates.Current.Guild[0].RelicId);
            Assert.Contains("relic.a", _runStates.Current.RelicInventory, "Прежний кит вернулся в запас.");
        }

        [Test]
        public void Equip_BaseKit_NotReturnedToStash_OnSwap()
        {
            _runStates.TryAddRelic("relic.a");
            Assert.IsTrue(_runStates.EquipRelic(0, "relic.a")); // прежний = relic.base → не возвращаем
            Assert.AreEqual(-1, Array.IndexOf(_runStates.Current.RelicInventory, "relic.base"),
                "Базовый кит не засоряет запас при свапе.");
        }

        [Test]
        public void Equip_Fails_WhenRelicNotInStash()
        {
            Assert.IsFalse(_runStates.EquipRelic(0, "relic.ghost"), "Нельзя надеть то, чего нет в запасе.");
            Assert.AreEqual("relic.base", _runStates.Current.Guild[0].RelicId, "Слот не тронут.");
        }

        [Test]
        public void Equip_Fails_WhenSlotOutOfRange()
        {
            _runStates.TryAddRelic("relic.a");
            Assert.IsFalse(_runStates.EquipRelic(99, "relic.a"));
            Assert.Contains("relic.a", _runStates.Current.RelicInventory, "Релик остался в запасе.");
        }

        [Test]
        public void Unequip_ReturnsRelicToStash_SetsBase()
        {
            _runStates.TryAddRelic("relic.a");
            _runStates.EquipRelic(0, "relic.a");

            Assert.IsTrue(_runStates.UnequipRelic(0));
            Assert.AreEqual("relic.base", _runStates.Current.Guild[0].RelicId, "Слот вернулся к базовому киту.");
            Assert.Contains("relic.a", _runStates.Current.RelicInventory, "Снятый релик вернулся в запас.");
        }

        [Test]
        public void Unequip_NoOp_WhenBaseKit()
        {
            Assert.IsFalse(_runStates.UnequipRelic(0), "На слоте базовый кит — снимать нечего.");
        }

        // ── Правки прямо с арены (фаза расстановки): позиция и кит сосуда ──

        [Test]
        public void SetSlotPosition_PersistsIntoGuild()
        {
            var pos = new Vector2(-3.5f, 1.25f);
            Assert.IsTrue(_runStates.SetSlotPosition(1, pos));
            Assert.AreEqual(pos, _runStates.Current.Guild[1].SavedPosition,
                "Переставленный на поле сосуд обязан помнить своё место между боями.");
        }

        [Test]
        public void SetSlotRelic_PutsKitOnSlot_WithoutTouchingStash()
        {
            _runStates.TryAddRelic("relic.a");

            Assert.IsTrue(_runStates.SetSlotRelic(0, "relic.b")); // притащили из грида, минуя запас
            Assert.AreEqual("relic.b", _runStates.Current.Guild[0].RelicId);
            Assert.Contains("relic.a", _runStates.Current.RelicInventory, "Запас не трогаем — это другой путь.");
        }

        [Test]
        public void SetSlot_Fails_WhenSlotOutOfRange()
        {
            Assert.IsFalse(_runStates.SetSlotRelic(99, "relic.a"));
            Assert.IsFalse(_runStates.SetSlotPosition(-1, Vector2.zero));
        }
    }
}
