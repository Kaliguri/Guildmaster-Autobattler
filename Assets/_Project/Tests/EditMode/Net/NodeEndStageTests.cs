using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Game.Session.Net;
using Guildmaster.Guild;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Конец узла: кадр-прощание и кнопки «дальше» — один шаг, а не два.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между файлами: ключи прощания кладёт тот, кто вёл узел (<c>ChestFlow</c> и
    /// соседи), а кнопки объявляет петля через <see cref="RunBeatStage"/>. Комментария тут мало —
    /// петля не видит, что узел уже проводил себя сам, и стёрла бы кадр голыми кнопками молча.
    /// </remarks>
    public sealed class NodeEndStageTests
    {
        /// <summary>Узел проводил себя сам — петля его кадр не трогает.</summary>
        [Test]
        public void FarewellOfTheNode_SurvivesTheRestBeat()
        {
            var net   = new LoopbackNetwork();
            var stage = new HostSessionStage(net.CreateNode());
            var beat  = new RunBeatStage(new SilentSession(), stage);

            stage.Announce(SessionStageState.Idle.EndingNode("ui.node.chest.title", "ui.node.chest.farewell"));
            beat.EnterRestBeat(CancellationToken.None);

            Assert.IsTrue(stage.Current.Rest.Ended);
            Assert.AreEqual("ui.node.chest.title", stage.Current.Rest.TitleKey,
                "петля объявила бы кнопки без кадра — и прощание сундука исчезло бы у обоих игроков");
        }

        /// <summary>После боя провожать нечего: петля объявляет конец узла сама, без ключей.</summary>
        [Test]
        public void BattleEnds_WithButtonsAndNoFarewell()
        {
            var net   = new LoopbackNetwork();
            var stage = new HostSessionStage(net.CreateNode());
            var beat  = new RunBeatStage(new SilentSession(), stage);

            beat.EnterRestBeat(CancellationToken.None);

            Assert.IsTrue(stage.Current.Rest.Ended);
            Assert.IsFalse(stage.Current.Rest.HasFarewell);
        }

        /// <summary>Вход в следующий узел снимает конец предыдущего — у всех сразу.</summary>
        [Test]
        public void EnteringTheNextNode_ClearsTheStage()
        {
            var net   = new LoopbackNetwork();
            var stage = new HostSessionStage(net.CreateNode());
            var beat  = new RunBeatStage(new SilentSession(), stage);

            stage.Announce(SessionStageState.Idle.EndingNode("ui.node.camp.title", "ui.node.camp.farewell"));
            beat.EnterNode();

            Assert.AreEqual(SessionStageKind.None, stage.Current.Kind);
            Assert.IsFalse(stage.Current.Rest.Ended, "кнопки прошлого узла исчезают вместе с ним");
        }

        /// <summary>
        /// Сессия боя, от которой биту нужны только две команды: вернуть мир и сменить фазу.
        /// Остальное реализовано пусто — здесь проверяется шаг узла, а не бой.
        /// </summary>
        private sealed class SilentSession : IBattleSession
        {
            public BattlePhase Phase { get; private set; } = BattlePhase.None;
            public void SetPhase(BattlePhase phase) => Phase = phase;
            public bool RequestReset() => true;

            public void BindLaunch(Action<BattlePresetData> launch) { }
            public void UnbindLaunch() { }
            public bool RequestLaunch(BattlePresetData preset) => true;
            public void BindReset(Action reset) { }
            public void UnbindReset() { }

            public UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct) =>
                UniTask.FromResult(default(BattleOutcome));

            public void ReportOutcome(BattleOutcome outcome,
                                      System.Collections.Generic.IReadOnlyList<int> fallenGuildIndices = null) { }
            public System.Collections.Generic.IReadOnlyList<int> LastFallen =>
                System.Array.Empty<int>();
            public void BindRestart(Action restart) { }
            public void UnbindRestart() { }
            public bool CanRestart => false;
            public bool RequestRestart() => false;
            public bool RestartInPlace() => false;
            public event Action ReplayRequested { add { } remove { } }

            public event Action PhaseChanged { add { } remove { } }
            public float ElapsedSeconds => 0f;
            public void BindClock(Func<float> elapsedSeconds) { }
            public void UnbindClock() { }
            public void BindStart(Action start) { }
            public void UnbindStart() { }
            public void RequestStart() { }
        }
    }
}
