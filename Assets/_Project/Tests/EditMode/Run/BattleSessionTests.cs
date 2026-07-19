using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Часы/фаза/старт боевого моста для верхней панели забега (план 12 Фаза 2): фаза дефолтит в None,
    /// привязанные часы отдают время, отвязка сбрасывает фазу, кнопка «Начать» дёргает привязанный делегат.
    /// </summary>
    public sealed class BattleSessionTests
    {
        [Test]
        public void Phase_DefaultsToNone()
        {
            var session = new BattleSession();
            Assert.AreEqual(BattlePhase.None, session.Phase);
            Assert.AreEqual(0f, session.ElapsedSeconds);
        }

        [Test]
        public void SetPhase_UpdatesPhase()
        {
            var session = new BattleSession();
            session.SetPhase(BattlePhase.Deployment);
            Assert.AreEqual(BattlePhase.Deployment, session.Phase);
            session.SetPhase(BattlePhase.Fighting);
            Assert.AreEqual(BattlePhase.Fighting, session.Phase);
        }

        [Test]
        public void SetPhase_RaisesPhaseChanged_OnlyOnRealChange()
        {
            var session = new BattleSession();
            int calls = 0;
            session.PhaseChanged += () => calls++;

            session.SetPhase(BattlePhase.Deployment);
            Assert.AreEqual(1, calls, "None→Deployment = одно событие");

            session.SetPhase(BattlePhase.Deployment);
            Assert.AreEqual(1, calls, "тот же phase = без события (навигатор не дёргается зря)");

            session.SetPhase(BattlePhase.Fighting);
            Assert.AreEqual(2, calls, "Deployment→Fighting = ещё одно");
        }

        [Test]
        public void UnbindClock_RaisesPhaseChanged_WhenPhaseWasNotNone()
        {
            var session = new BattleSession();
            session.SetPhase(BattlePhase.Fighting);
            int calls = 0;
            session.PhaseChanged += () => calls++;

            session.UnbindClock(); // сброс Fighting→None → событие (навигатор погасит контекст в None)

            Assert.AreEqual(1, calls);
            Assert.AreEqual(BattlePhase.None, session.Phase);
        }

        [Test]
        public void BindClock_ElapsedReadsDelegate()
        {
            var session = new BattleSession();
            float t = 0f;
            session.BindClock(() => t);
            Assert.AreEqual(0f, session.ElapsedSeconds);
            t = 12.5f;
            Assert.AreEqual(12.5f, session.ElapsedSeconds);
        }

        [Test]
        public void UnbindClock_ResetsClockAndPhase()
        {
            var session = new BattleSession();
            session.BindClock(() => 42f);
            session.SetPhase(BattlePhase.Fighting);

            session.UnbindClock();

            Assert.AreEqual(0f, session.ElapsedSeconds, "часы отвязаны → 0");
            Assert.AreEqual(BattlePhase.None, session.Phase, "выгрузка боя → панель скрыта");
        }

        [Test]
        public void RequestStart_InvokesBoundStart()
        {
            var session = new BattleSession();
            int calls = 0;
            session.BindStart(() => calls++);

            session.RequestStart();
            session.RequestStart();

            Assert.AreEqual(2, calls);
        }

        [Test]
        public void RequestStart_AfterUnbind_IsNoOp()
        {
            var session = new BattleSession();
            int calls = 0;
            session.BindStart(() => calls++);
            session.UnbindStart();

            session.RequestStart();

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void RequestStart_WithNoBinding_DoesNotThrow()
        {
            var session = new BattleSession();
            Assert.DoesNotThrow(() => session.RequestStart());
        }
    }
}
