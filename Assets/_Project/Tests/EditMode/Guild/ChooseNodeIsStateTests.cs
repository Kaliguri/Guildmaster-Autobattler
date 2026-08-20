using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Выбор узла — durable-состояние, а не событие в стеке одной машины.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт МЕЖДУ файлами и потому проверяется тестом, а не комментарием: записывает выбор
    /// применитель команды, читает его петля акта (<c>WorldMapNodeChooser</c>), гасит —
    /// <see cref="MapTraversal.Advance"/>, а подсветку у обоих игроков выводит из него же гостевая
    /// половина. Нарушь любой из четырёх — остальные не узнают.
    /// <para>Ради этого всё и делалось (решение Макса 04.08.2026): клик хозяина и клик напарника
    /// приходят одной дорогой, а «куда мы идём» переживает реконнект. До этого факт жил в
    /// <c>UniTaskCompletionSource</c> внутри петли, которой у гостя нет.</para>
    /// </remarks>
    public sealed class ChooseNodeIsStateTests
    {
        // Развилка: стоим на старте, из него ведут два узла, третий недостижим.
        private static MapState Fork() => new MapState
        {
            CurrentNodeId = "start",
            Nodes = new[]
            {
                new MapNode { Id = "start", Edges = new[] { "left", "right" }, Cleared = true },
                new MapNode { Id = "left",  Edges = new[] { "start" } },
                new MapNode { Id = "right", Edges = new[] { "start" } },
                new MapNode { Id = "far",   Edges = new System.String[0] },
            },
        };

        [Test]
        public void FreshFork_WaitsForChoice()
        {
            MapState map = Fork();

            Assert.IsTrue(string.IsNullOrEmpty(map.EnteringNodeId),
                "на развилке поле входа пусто — по нему обе стороны и понимают, что выбор ждут");
            Assert.AreEqual(2, MapTraversal.AvailableNext(map).Count,
                "гореть должны оба соседа: список достижимого считается из карты, а не присылается");
        }

        [Test]
        public void Advance_ClearsTheEnteredNode()
        {
            MapState map = Fork();
            map.EnteringNodeId = "left";

            Assert.IsTrue(MapTraversal.Advance(map, "left"));
            Assert.AreEqual("left", map.CurrentNodeId);
            Assert.IsTrue(string.IsNullOrEmpty(map.EnteringNodeId),
                "узел пройден — вход обязан погаснуть, иначе следующая развилка встретит игрока " +
                "уже «выбранным» узлом и петля пройдёт её не спрашивая");
        }

        [Test]
        public void UnreachableNode_IsNotEnterable()
        {
            MapState map = Fork();

            Assert.IsFalse(MapTraversal.CanEnter(map, "far"),
                "недостижимый узел отвергается: у напарника карта могла отстать на снимок, и решать " +
                "по ЕГО копии нельзя");
            Assert.IsFalse(MapTraversal.Advance(map, "far"));
        }
    }
}
