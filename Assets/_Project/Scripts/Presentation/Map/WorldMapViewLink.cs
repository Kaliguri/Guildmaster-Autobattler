using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Линк к world-слою карты для потребителей, которые живут ВЫШЕ мирового DI-скоупа.
    /// <para>Зачем: петля забега (<c>ActRunner</c> и её <c>IMapNodeChooser</c>) зарегистрирована в корневом
    /// скоупе, а сам слой карты — компонент persist-мира, и <c>RegisterComponentInHierarchy</c> ищет
    /// объекты ТОЛЬКО в сцене своего скоупа. Родитель не видит регистраций ребёнка — поэтому корень держит
    /// этот линк, а мировой слой при старте привязывает себя к нему (дочерний скоуп предка видит).</para>
    /// <para>Это не сервис-локатор: линк — обычная DI-регистрация с явной привязкой, без глобального
    /// доступа и статики. Пока привязки нет (headless, тесты, сцена без карты) — методы безопасно пусты.</para>
    /// </summary>
    public sealed class WorldMapViewLink : IWorldMapView
    {
        private IWorldMapView _target;

        /// <inheritdoc/>
        public event Action<string> NodeClicked;

        /// <inheritdoc/>
        public Rect2D Bounds => _target?.Bounds ?? new Rect2D(default, default);

        /// <summary>Привязать живой слой карты (зовёт сам слой при старте). Повторная привязка перецепляет событие.</summary>
        public void Bind(IWorldMapView target)
        {
            if (ReferenceEquals(_target, target)) return;
            if (_target != null) _target.NodeClicked -= Relay;
            _target = target;
            if (_target != null) _target.NodeClicked += Relay;
        }

        /// <summary>Отвязать слой (уничтожение объекта мира), чтобы не держать мёртвую ссылку.</summary>
        public void Unbind(IWorldMapView target)
        {
            if (!ReferenceEquals(_target, target)) return;
            _target.NodeClicked -= Relay;
            _target = null;
        }

        /// <inheritdoc/>
        public void Show(IReadOnlyList<MapNodeVisual> nodes, IReadOnlyList<(string From, string To)> edges)
            => _target?.Show(nodes, edges);

        /// <inheritdoc/>
        public void Hide() => _target?.Hide();

        private void Relay(string id) => NodeClicked?.Invoke(id);
    }
}
