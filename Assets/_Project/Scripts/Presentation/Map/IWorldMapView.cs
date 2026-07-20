using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// World-слой карты акта: рисует узлы и рёбра в мире и отдаёт наружу клик по доступному узлу.
    /// Шов между петлёй забега (<c>WorldMapNodeChooser</c>, слой Game) и презентацией — чтобы петля не
    /// зависела от MonoBehaviour, а карта оставалась тестируемой подменой.
    /// </summary>
    public interface IWorldMapView
    {
        /// <summary>Клик по узлу в состоянии <see cref="MapNodeVisualState.Available"/>: отдаёт его id.</summary>
        event Action<string> NodeClicked;

        /// <summary>Границы разложенной карты в мире — по ним камера карты выставляет кламп и стартовый кадр.</summary>
        Rect2D Bounds { get; }

        /// <summary>Показать карту: разложить узлы и рёбра, включить слой.</summary>
        /// <param name="nodes">Узлы: топология (этаж/ряд) и состояние. Раскладку в координаты считает слой сам.</param>
        /// <param name="edges">Рёбра как пары id (from, to) — рисуются линией между узлами.</param>
        /// <param name="seed">Сид забега: из него выводится стабильный разброс узлов (в данных он не хранится).</param>
        void Show(IReadOnlyList<MapNodeVisual> nodes, IReadOnlyList<(string From, string To)> edges, long seed);

        /// <summary>Скрыть слой карты. Обязателен при отмене забега (QA #37), иначе карта останется висеть в мире.</summary>
        void Hide();
    }
}
