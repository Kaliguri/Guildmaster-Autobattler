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
    /// Пул перезапусков боя на акт (реш. №65, план act-map-run-loop §3.4, C1): сброс в начале акта, трата с
    /// клампом в ноль, отсутствие сброса при повторном BeginAct того же акта (перезаход из сейва).
    /// </summary>
    public sealed class RunStateRestartTests
    {
        private RunStateService _runStates;
        private GameConfig      _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<GameConfig>(); // RestartsPerAct код-дефолт = 2
            _runStates = new RunStateService(new InMemorySaveService(), _config);
            _runStates.NewRun(1L, Array.Empty<RosterSlot>());
        }

        [Test]
        public void BeginAct_SetsRestartPool()
        {
            _runStates.BeginAct();
            Assert.AreEqual(_config.RestartsPerAct, _runStates.RestartsRemaining);
            Assert.AreEqual(2, _runStates.RestartsRemaining);
        }

        [Test]
        public void TrySpendRestart_Decrements_ThenFloorsAtZero()
        {
            _runStates.BeginAct(); // 2
            Assert.IsTrue(_runStates.TrySpendRestart());  // 1
            Assert.IsTrue(_runStates.TrySpendRestart());  // 0
            Assert.IsFalse(_runStates.TrySpendRestart(), "Пул пуст — перезапуск недоступен.");
            Assert.AreEqual(0, _runStates.RestartsRemaining);
        }

        [Test]
        public void BeginAct_DoesNotReset_WhenActAlreadyStarted()
        {
            _runStates.BeginAct();          // пул 2, карта сгенерирована
            _runStates.TrySpendRestart();   // 1
            _runStates.BeginAct();          // карта уже есть → НЕ сбрасывает пул
            Assert.AreEqual(1, _runStates.RestartsRemaining);
        }
    }
}
