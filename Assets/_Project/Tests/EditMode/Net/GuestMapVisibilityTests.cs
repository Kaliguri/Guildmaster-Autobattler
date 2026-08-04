using System;
using System.Collections.Generic;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using Guildmaster.Presentation.Map;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Просьба показать карту переживает то, что показывать пока нечего.
    /// </summary>
    /// <remarks>
    /// У гостя забег и объявление «карта открыта» едут РАЗНЫМИ каналами, и порядок между каналами не
    /// гарантирован ничем — просьба вполне обгоняет данные. Пока показ хранил один флаг, такой обгон
    /// гасил намерение навсегда: карта не появлялась уже никогда, а выглядело это как «гостя не кинуло
    /// на карту» (наход. Макса 03.08.2026).
    /// <para>Инвариант живёт между сетевым слоем и показом мира, поэтому держится тестом: обе стороны
    /// правятся порознь, и вторая о первой не узнает.</para>
    /// </remarks>
    public sealed class GuestMapVisibilityTests
    {
        [Test]
        public void ShowBeforeData_DrawsWhenDataArrives()
        {
            var view = new FakeView();
            var runs = new FakeRuns();                       // забега ещё нет — снимок не доехал
            var map  = new WorldMapController(view, runs, null, null, null);

            map.SetVisible(true);
            Assert.IsFalse(map.IsShown, "рисовать нечего — карта не показана, и это правильно");

            runs.Current = RunWithMap();
            map.Refresh();

            Assert.IsTrue(map.IsShown, "снимок доехал — карта обязана появиться, просьбу никто не отменял");
            Assert.AreEqual(1, view.ShowCalls);
        }

        [Test]
        public void RefreshWithoutRequest_DoesNothing()
        {
            var view = new FakeView();
            var runs = new FakeRuns { Current = RunWithMap() };
            var map  = new WorldMapController(view, runs, null, null, null);

            map.Refresh();

            Assert.IsFalse(map.IsShown, "карту не просили показывать — снимок сам её открывать не должен");
            Assert.AreEqual(0, view.ShowCalls);
        }

        [Test]
        public void Hide_AfterUndrawnRequest_LeavesViewAlone()
        {
            var view = new FakeView();
            var runs = new FakeRuns();
            var map  = new WorldMapController(view, runs, null, null, null);

            map.SetVisible(true);   // нечего рисовать
            map.SetVisible(false);

            Assert.AreEqual(0, view.HideCalls, "скрывать нечего: карта так и не была показана");
        }

        private static RunState RunWithMap() => new RunState
        {
            Map = new MapState
            {
                CurrentNodeId = "n1",
                Nodes = new[]
                {
                    new MapNode { Id = "n1", Floor = 0, Row = 0, Edges = new[] { "n2" } },
                    new MapNode { Id = "n2", Floor = 1, Row = 0, Edges = new[] { "n1" } },
                },
            },
        };

        private sealed class FakeRuns : IRunStateView
        {
            public RunState Current { get; set; }
        }

        private sealed class FakeView : IWorldMapView
        {
            public int ShowCalls { get; private set; }
            public int HideCalls { get; private set; }

            public event Action<string> NodeClicked { add { } remove { } }

            public Guildmaster.Core.Arena.Rect2D Bounds => default;

            public IReadOnlyList<string> NodeIds => Array.Empty<string>();

            public void Show(IReadOnlyList<MapNodeVisual> nodes,
                             IReadOnlyList<(string From, string To)> edges, long seed) => ShowCalls++;

            public void Hide() => HideCalls++;

            public void PreviewTravel(string nodeId) { }
            public void ResetPawn() { }
        }
    }
}
