using System.Collections.Generic;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Браузерная история Back/Forward: линейный стек с курсором; переход после Back затирает «вперёд».
    /// Чистая структура (тестируемая), окно хранит здесь адреса (страница + guid).
    /// </summary>
    public sealed class NavHistory<T>
    {
        private readonly List<T> _items = new List<T>();
        private int _index = -1;

        public bool CanBack => _index > 0;
        public bool CanForward => _index >= 0 && _index < _items.Count - 1;
        public bool HasCurrent => _index >= 0;
        public T Current => _index >= 0 ? _items[_index] : default;
        public int Count => _items.Count;

        /// <summary>Добавить адрес как новый текущий; всё «вперёд» от курсора отбрасывается.</summary>
        public void Push(T item)
        {
            if (_index < _items.Count - 1)
                _items.RemoveRange(_index + 1, _items.Count - _index - 1);
            _items.Add(item);
            _index = _items.Count - 1;
        }

        public T Back()
        {
            if (CanBack) _index--;
            return Current;
        }

        public T Forward()
        {
            if (CanForward) _index++;
            return Current;
        }

        public void Clear()
        {
            _items.Clear();
            _index = -1;
        }
    }
}
